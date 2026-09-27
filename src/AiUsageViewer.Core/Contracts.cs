namespace AiUsageViewer.Core;

public interface IQuotaProvider
{
    ProviderKind Kind { get; }
    ProviderCapabilities Capabilities { get; }
    Task<ProviderResult> FetchAsync(AccountProfile account, CancellationToken cancellationToken);
}

public interface ISecretStore
{
    string? Read(string reference);
    void Write(string reference, string value);
    void Delete(string reference);
}

public interface IUsageStore
{
    Task<UsageSummary> SummarizeAsync(UsageFilter filter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UsageGroup>> GroupAsync(UsageFilter filter, string dimension, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailyUsage>> DailyAsync(UsageFilter filter, TimeZoneInfo zone, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UsageEvent>> ReadEventsAsync(UsageFilter filter, CancellationToken cancellationToken = default);
    Task SaveStatusAsync(AccountStatus status, CancellationToken cancellationToken = default);
    Task<AccountStatus?> LoadStatusAsync(AccountProfile account, CancellationToken cancellationToken = default);
}

public sealed record CollectionProgress(int FilesVisited, int FilesChanged, int EventsRead, int InvalidLines,
    int UncertainEvents, IReadOnlyList<string> Warnings, long BytesRead=0,
    IReadOnlyDictionary<string,double>? Timings=null);
public interface IUsageCollector
{
    Task<CollectionProgress> CollectAsync(IReadOnlyList<SourceLocation> sources, CancellationToken cancellationToken);
}
