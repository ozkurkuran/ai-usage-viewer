namespace AiUsageViewer.Core;

public sealed record ActivityRow(string AccountId,DateOnly Day,string Model,string Endpoint,long Input,long Output,
    long Reasoning,long Requests,decimal Spend,decimal ByokSpend,string Currency="USD");
public sealed record ActivityResult(ConnectionState State,IReadOnlyList<ActivityRow>? Rows=null,
    string? MessageCode=null,TimeSpan? RetryAfter=null);
public sealed record ActivityStatus(string AccountId,string ConnectionKey,ConnectionState State,DateTimeOffset LastAttempt,
    DateTimeOffset NextAttempt,DateTimeOffset? LastGood,DateOnly? From,DateOnly? Through,string? MessageCode);
public interface IActivityProvider
{
    Task<ActivityResult> FetchActivityAsync(AccountProfile account,CancellationToken ct);
}
