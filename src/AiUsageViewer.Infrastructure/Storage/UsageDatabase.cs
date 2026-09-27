using System.Text.Json;
using AiUsageViewer.Core;
using AiUsageViewer.Infrastructure.Logs;
using Microsoft.Data.Sqlite;

namespace AiUsageViewer.Infrastructure.Storage;

public sealed record FileCursor(string Path, long Offset, long Length, long ModifiedTicks,
    int PrefixLength, string PrefixHash, string TailHash, ParserState State, int ParserVersion = UsageLogParser.Version, bool ScanComplete = false, string? FileIdentity = null);

public sealed class UsageDatabase : IUsageStore
{
    private readonly string connectionString;
    public UsageDatabase(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, DefaultTimeout = 15 }.ToString();
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        using var command = connection.CreateCommand();
        command.CommandText="PRAGMA user_version";
        var version=Convert.ToInt64(await command.ExecuteScalarAsync(ct));
        if(version>2) throw new InvalidDataException("Database was created by a newer application version.");
        command.CommandText="PRAGMA journal_mode=WAL";await command.ExecuteScalarAsync(ct);
        using var transaction=connection.BeginTransaction();command.Transaction=transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS usage_events (
                id TEXT PRIMARY KEY, provider INTEGER NOT NULL, session TEXT NOT NULL,
                project TEXT NOT NULL, model TEXT NOT NULL, timestamp INTEGER NOT NULL,
                input INTEGER NOT NULL, output INTEGER NOT NULL, cache_read INTEGER NOT NULL,
                cache_write5 INTEGER NOT NULL, cache_write1 INTEGER NOT NULL, reasoning INTEGER NOT NULL,
                identity_quality INTEGER NOT NULL, account_id TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_usage_time ON usage_events(timestamp);
            CREATE INDEX IF NOT EXISTS ix_usage_session ON usage_events(session);
            CREATE TABLE IF NOT EXISTS file_cursors (path TEXT PRIMARY KEY, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS account_status (id TEXT PRIMARY KEY, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS quota_observations(account TEXT NOT NULL,observed INTEGER NOT NULL,data TEXT NOT NULL,PRIMARY KEY(account,observed));
            PRAGMA user_version=2;
            """;
        await command.ExecuteNonQueryAsync(ct);
        transaction.Commit();
    }

    public async Task<FileCursor?> ReadCursorAsync(string path, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM file_cursors WHERE path=$path";
        command.Parameters.AddWithValue("$path", path);
        return await command.ExecuteScalarAsync(ct) is string json ? JsonSerializer.Deserialize<FileCursor>(json) : null;
    }

    public async Task<IReadOnlyDictionary<string,FileCursor>> ReadCursorsAsync(CancellationToken ct)
    {
        await using var connection=await OpenAsync(ct);
        using var command=connection.CreateCommand();command.CommandText="SELECT data FROM file_cursors";
        var cursors=new Dictionary<string,FileCursor>(StringComparer.OrdinalIgnoreCase);
        await using var reader=await command.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            if(JsonSerializer.Deserialize<FileCursor>(reader.GetString(0)) is { } cursor) cursors[cursor.Path]=cursor;
        return cursors;
    }

    public async Task CommitBatchAsync(IReadOnlyList<UsageEvent> events, FileCursor cursor, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO usage_events VALUES ($id,$provider,$session,$project,$model,$timestamp,
                $input,$output,$read,$write5,$write1,$reasoning,$quality,$account)
            ON CONFLICT(id) DO UPDATE SET
                input=excluded.input, output=excluded.output, cache_read=excluded.cache_read,
                cache_write5=excluded.cache_write5, cache_write1=excluded.cache_write1,
                reasoning=excluded.reasoning, identity_quality=min(identity_quality,excluded.identity_quality)
            WHERE excluded.input+excluded.output+excluded.cache_read+excluded.cache_write5+excluded.cache_write1
                    > input+output+cache_read+cache_write5+cache_write1
                OR (excluded.input+excluded.output+excluded.cache_read+excluded.cache_write5+excluded.cache_write1
                    = input+output+cache_read+cache_write5+cache_write1 AND excluded.cache_write1>cache_write1);
            UPDATE usage_events SET identity_quality=min(identity_quality,$quality) WHERE id=$id;
            """;
        foreach (var e in events)
        {
            command.Parameters.Clear();
            foreach (var (key, value) in new (string, object)[] {
                ("id",e.Id),("provider",(int)e.Provider),("session",e.SessionId),("project",e.Project),("model",e.Model),
                ("timestamp",e.Timestamp.ToUnixTimeMilliseconds()),("input",e.Tokens.Input),("output",e.Tokens.Output),
                ("read",e.Tokens.CacheRead),("write5",e.Tokens.CacheWrite5m),("write1",e.Tokens.CacheWrite1h),
                ("reasoning",e.Tokens.Reasoning),("quality",(int)e.Identity),("account",(object?)e.AccountId ?? DBNull.Value) })
                command.Parameters.AddWithValue("$" + key, value);
            await command.ExecuteNonQueryAsync(ct);
        }
        command.Parameters.Clear();
        command.CommandText = "INSERT INTO file_cursors VALUES ($path,$data) ON CONFLICT(path) DO UPDATE SET data=excluded.data";
        command.Parameters.AddWithValue("$path", cursor.Path);
        command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(cursor));
        await command.ExecuteNonQueryAsync(ct);
        transaction.Commit();
    }

    private static string Where(SqliteCommand command, UsageFilter filter, bool verified = true)
    {
        var clauses = new List<string> { verified ? "identity_quality<>2" : "1=1" };
        foreach (var (column, value) in new (string, object?)[] { ("provider", filter.Provider is { } p ? (int)p : null),
                     ("model",filter.Model),("project",filter.Project),("session",filter.SessionId) })
            if (value is not null) { clauses.Add(column + "=$" + column); command.Parameters.AddWithValue("$" + column, value); }
        if (filter.From is { } from) { clauses.Add("timestamp >= $from"); command.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds()); }
        if (filter.Until is { } until) { clauses.Add("timestamp < $until"); command.Parameters.AddWithValue("$until", until.ToUnixTimeMilliseconds()); }
        return " WHERE " + string.Join(" AND ", clauses);
    }

    private const string Sums = "coalesce(sum(input),0),coalesce(sum(output),0),coalesce(sum(cache_read),0),coalesce(sum(cache_write5),0),coalesce(sum(cache_write1),0),coalesce(sum(reasoning),0)";
    private static TokenUsage Tokens(SqliteDataReader reader, int start) => new(reader.GetInt64(start),reader.GetInt64(start+1),
        reader.GetInt64(start+2),reader.GetInt64(start+3),reader.GetInt64(start+4),reader.GetInt64(start+5));

    public async Task<UsageSummary> SummarizeAsync(UsageFilter filter, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT " + Sums + ",count(*),min(timestamp),max(timestamp) FROM usage_events" + Where(command,filter);
        TokenUsage tokens;
        long count;
        DateTimeOffset? first, last;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            tokens = Tokens(reader,0); count=reader.GetInt64(6);
            first=reader.IsDBNull(7)?null:DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7));
            last=reader.IsDBNull(8)?null:DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(8));
        }
        command.Parameters.Clear();
        command.CommandText = "SELECT count(*) FROM usage_events" + Where(command,filter,false) + " AND identity_quality=2";
        var uncertain = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        return new(tokens,count,uncertain,first,last);
    }

    public async Task<IReadOnlyList<UsageGroup>> GroupAsync(UsageFilter filter, string dimension, CancellationToken cancellationToken = default)
    {
        if (dimension is not ("provider" or "model" or "project" or "session")) throw new ArgumentException("Unsupported grouping.",nameof(dimension));
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {dimension},{Sums},count(*) FROM usage_events" + Where(command,filter) + $" GROUP BY {dimension} ORDER BY sum(input+output+cache_read+cache_write5+cache_write1) DESC";
        var result = new List<UsageGroup>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(dimension=="provider"?((ProviderKind)reader.GetInt32(0)).ToString():reader.GetString(0),Tokens(reader,1),reader.GetInt64(7)));
        return result;
    }

    public async Task<IReadOnlyList<DailyUsage>> DailyAsync(UsageFilter filter, TimeZoneInfo zone, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT timestamp,input,output,cache_read,cache_write5,cache_write1,reasoning FROM usage_events"+Where(command,filter);
        var days = new Dictionary<DateOnly,DailyUsage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),zone).DateTime);
            var previous=days.GetValueOrDefault(day,new(day,new(),0));
            days[day]=previous with { Tokens=previous.Tokens+Tokens(reader,1), Requests=previous.Requests+1 };
        }
        return days.Values.OrderBy(x=>x.Day).ToList();
    }

    public async Task<IReadOnlyList<UsageEvent>> ReadEventsAsync(UsageFilter filter, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,provider,session,project,model,timestamp,input,output,cache_read,cache_write5,cache_write1,reasoning,identity_quality,account_id FROM usage_events"+Where(command,filter,false)+" ORDER BY timestamp";
        var events = new List<UsageEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            events.Add(new(reader.GetString(0),(ProviderKind)reader.GetInt32(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5)),Tokens(reader,6),(IdentityQuality)reader.GetInt32(12),reader.IsDBNull(13)?null:reader.GetString(13)));
        return events;
    }

    public async Task SaveStatusAsync(AccountStatus status, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction=connection.BeginTransaction();using var command = connection.CreateCommand();command.Transaction=transaction;
        command.CommandText="INSERT INTO account_status VALUES ($id,$data) ON CONFLICT(id) DO UPDATE SET data=excluded.data";
        command.Parameters.AddWithValue("$id",status.Account.Id);
        command.Parameters.AddWithValue("$data",JsonSerializer.Serialize(status));
        await command.ExecuteNonQueryAsync(cancellationToken);
        if(status.State is ConnectionState.Ready or ConnectionState.Partial&&status.LastGood is { } snapshot)
        {
            command.CommandText="INSERT OR IGNORE INTO quota_observations VALUES($id,$observed,$data)";
            command.Parameters["$data"].Value=JsonSerializer.Serialize(snapshot);command.Parameters.AddWithValue("$observed",snapshot.ObservedAt.ToUnixTimeMilliseconds());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        transaction.Commit();
    }

    public async Task<AccountStatus?> LoadStatusAsync(AccountProfile account, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText="SELECT data FROM account_status WHERE id=$id";
        command.Parameters.AddWithValue("$id",account.Id);
        var saved=await command.ExecuteScalarAsync(cancellationToken) is string json?JsonSerializer.Deserialize<AccountStatus>(json):null;
        return saved is null||!saved.Account.SameConnection(account)?null:saved with { Account=account };
    }
}
