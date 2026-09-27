using System.Text.Json;
using AiUsageViewer.Core;
using AiUsageViewer.Infrastructure.Logs;
using AiUsageViewer.Infrastructure.Providers;
using AiUsageViewer.Infrastructure.Storage;

if(!args.Contains("--scan") && !args.Contains("--quota"))
{
    Console.WriteLine("AI Usage Viewer diagnostics: --scan and/or --quota [--data-dir PATH]. Outputs counts and quota windows only.");
    return;
}
var directoryIndex=Array.IndexOf(args,"--data-dir");
var dataDirectory=directoryIndex>=0 && directoryIndex+1<args.Length?Path.GetFullPath(args[directoryIndex+1]):
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AiUsageViewer");
var settings=new SettingsStore(dataDirectory).Load();
using var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(10));
var db=new UsageDatabase(Path.Combine(dataDirectory,"usage.db"));
await db.InitializeAsync(cancellation.Token);
if(args.Contains("--scan"))
{
    using var collector=new JsonlCollector(db);
    var progress=await collector.CollectAsync(settings.Sources,cancellation.Token);
    var summary=await db.SummarizeAsync(new(),cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(new { collection=progress,summary }));
    if(args.Contains("--repeat"))
    {
        await Task.Delay(3000,cancellation.Token);
        Console.WriteLine(JsonSerializer.Serialize(new { repeatedCollection=await collector.CollectAsync(settings.Sources,cancellation.Token) }));
    }
}
if(args.Contains("--quota"))
{
    using var http=new ProviderHttp();
    IQuotaProvider[] providers=[new ClaudeProvider(http),new CodexProvider(),new OpenRouterProvider(http,new WindowsSecretStore(Path.Combine(dataDirectory,"secrets")))];
    foreach(var account in settings.Accounts.Where(a=>a.Enabled))
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var result=await providers.Single(p=>p.Kind==account.Provider).FetchAsync(account,deadline.Token);
        Console.WriteLine(JsonSerializer.Serialize(new { provider=account.Provider.ToString(),state=result.State.ToString(),
            result.MessageCode,windows=result.Snapshot?.Windows,observedAt=result.Snapshot?.ObservedAt }));
    }
}
