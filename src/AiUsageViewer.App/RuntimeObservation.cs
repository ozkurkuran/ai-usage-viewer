using System.Diagnostics;
using System.Text.Json;

namespace AiUsageViewer.App;

public partial class App
{
    private long observedScans,observedChangedFiles,observedCollectionFailures;
    private int observedSourceWarnings;
    private sealed record RuntimeSample(double Seconds,long PrivateBytes,long WorkingSetBytes,int Handles,int Threads,double CpuSeconds,
        long CompletedScans,long ChangedFiles,long CollectionFailures,int SourceWarnings,DateTimeOffset? LatestQuotaAttempt);

    // Exercises the normal background timers/watchers, without screenshots, preferences,
    // notifications, simulated events, forced collection or additional quota requests.
    private async Task ObserveRuntimeAsync(int seconds)
    {
        var started=DateTimeOffset.UtcNow;var timer=Stopwatch.StartNew();
        using var process=Process.GetCurrentProcess();
        var samples=new List<RuntimeSample>();var nextSample=0d;var lastPulse=0d;var maximumPulseDelay=0d;
        var reportPath=Path.Combine(settingsStore.DirectoryPath,"runtime-observation.json");
        while(!lifetime.IsCancellationRequested)
        {
            var elapsed=timer.Elapsed.TotalSeconds;
            if(lastPulse>0) maximumPulseDelay=Math.Max(maximumPulseDelay,Math.Max(0,elapsed-lastPulse-1));
            lastPulse=elapsed;
            if(elapsed>=nextSample||elapsed>=seconds)
            {
                process.Refresh();
                samples.Add(new(elapsed,process.PrivateMemorySize64,process.WorkingSet64,process.HandleCount,process.Threads.Count,process.TotalProcessorTime.TotalSeconds,
                    observedScans,observedChangedFiles,observedCollectionFailures,observedSourceWarnings,quotas.Statuses.Select(s=>(DateTimeOffset?)s.LastAttempt).Max()));
                var complete=elapsed>=seconds;
                var report=new { complete,startedAt=started,observedAt=DateTimeOffset.UtcNow,requestedSeconds=seconds,
                    elapsedSeconds=elapsed,mode="normal-background",maximumDispatcherDelaySeconds=maximumPulseDelay,
                    providerStates=quotas.Statuses.Select(s=>new { provider=s.Account.Provider.ToString(),state=s.State.ToString(),lastAttempt=s.LastAttempt }),samples };
                var temporary=reportPath+".tmp";
                await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented=true }),lifetime.Token);
                File.Move(temporary,reportPath,true);
                if(complete) break;
                nextSample=elapsed+30;
            }
            await Task.Delay(TimeSpan.FromSeconds(1),lifetime.Token);
        }
    }
}
