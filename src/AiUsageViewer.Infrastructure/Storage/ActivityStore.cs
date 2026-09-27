using System.Text.Json;
using AiUsageViewer.Core;
using Microsoft.Data.Sqlite;

namespace AiUsageViewer.Infrastructure.Storage;
public sealed class ActivityStore(string path)
{
    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path,DefaultTimeout=15 }.ToString());
        await connection.OpenAsync(ct);return connection;
    }
    public async Task InitializeAsync(CancellationToken ct=default)
    {
        await using var connection=await OpenAsync(ct);using var command=connection.CreateCommand();
        command.CommandText="""
            CREATE TABLE IF NOT EXISTS activity_status(account TEXT PRIMARY KEY,data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS activity_rows(account TEXT NOT NULL,day TEXT NOT NULL,model TEXT NOT NULL,endpoint TEXT NOT NULL,data TEXT NOT NULL,PRIMARY KEY(account,day,model,endpoint));
            """;await command.ExecuteNonQueryAsync(ct);
    }
    public async Task<ActivityStatus?> StatusAsync(string account,CancellationToken ct=default)
    {
        await using var connection=await OpenAsync(ct);using var command=connection.CreateCommand();
        command.CommandText="SELECT data FROM activity_status WHERE account=$account";command.Parameters.AddWithValue("$account",account);
        return await command.ExecuteScalarAsync(ct) is string json?JsonSerializer.Deserialize<ActivityStatus>(json):null;
    }
    public async Task<IReadOnlyList<ActivityRow>> ReadAsync(string account,CancellationToken ct=default)
    {
        await using var connection=await OpenAsync(ct);using var command=connection.CreateCommand();
        command.CommandText="SELECT data FROM activity_rows WHERE account=$account ORDER BY day DESC,model";command.Parameters.AddWithValue("$account",account);
        await using var reader=await command.ExecuteReaderAsync(ct);var rows=new List<ActivityRow>();
        while(await reader.ReadAsync(ct)) if(JsonSerializer.Deserialize<ActivityRow>(reader.GetString(0)) is { } row) rows.Add(row);
        return rows;
    }
    public async Task SaveAsync(ActivityStatus status,IReadOnlyList<ActivityRow>? rows,bool changedConnection,CancellationToken ct=default)
    {
        await using var connection=await OpenAsync(ct);using var transaction=connection.BeginTransaction();using var command=connection.CreateCommand();command.Transaction=transaction;
        if(changedConnection||rows is not null)
        {
            command.CommandText="DELETE FROM activity_rows WHERE account=$account"+(changedConnection?"":" AND day >= $from AND day <= $through");
            command.Parameters.AddWithValue("$account",status.AccountId);
            if(!changedConnection) { command.Parameters.AddWithValue("$from",status.From!.Value.ToString("yyyy-MM-dd"));command.Parameters.AddWithValue("$through",status.Through!.Value.ToString("yyyy-MM-dd")); }
            await command.ExecuteNonQueryAsync(ct);
        }
        if(rows is not null) foreach(var row in rows)
        {
            command.Parameters.Clear();command.CommandText="INSERT INTO activity_rows VALUES($a,$d,$m,$e,$v) ON CONFLICT(account,day,model,endpoint) DO UPDATE SET data=excluded.data";
            command.Parameters.AddWithValue("$a",status.AccountId);command.Parameters.AddWithValue("$d",row.Day.ToString("yyyy-MM-dd"));command.Parameters.AddWithValue("$m",row.Model);command.Parameters.AddWithValue("$e",row.Endpoint);command.Parameters.AddWithValue("$v",JsonSerializer.Serialize(row));
            await command.ExecuteNonQueryAsync(ct);
        }
        command.Parameters.Clear();command.CommandText="INSERT INTO activity_status VALUES($a,$v) ON CONFLICT(account) DO UPDATE SET data=excluded.data";
        command.Parameters.AddWithValue("$a",status.AccountId);command.Parameters.AddWithValue("$v",JsonSerializer.Serialize(status));await command.ExecuteNonQueryAsync(ct);transaction.Commit();
    }
}
