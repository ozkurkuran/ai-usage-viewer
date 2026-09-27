using AiUsageViewer.Core;
using AiUsageViewer.Infrastructure.Logs;
using AiUsageViewer.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace AiUsageViewer.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"AiUsageViewer-tests",Guid.NewGuid().ToString("N"));
    public StorageTests()=>Directory.CreateDirectory(root);
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(root,true); }
    private async Task<UsageDatabase> CreateAsync()
    {
        var db=new UsageDatabase(Path.Combine(root,"usage.db")); await db.InitializeAsync(); return db;
    }
    [Fact] public async Task IncrementalIngestionRetriesPartialLinesAndSurvivesRestart()
    {
        var db=await CreateAsync();
        var log=Path.Combine(root,"session.jsonl");
        var second=ParserTests.Claude("msg-2");
        await File.WriteAllTextAsync(log,ParserTests.Claude()+"\n"+second[..40]);
        SourceLocation[] sources=[new("test",ProviderKind.Claude,root)];
        var scanner=new JsonlCollector(db);
        await scanner.CollectAsync(sources,default);
        Assert.Equal(200,(await db.SummarizeAsync(new())).Tokens.Total);
        await File.AppendAllTextAsync(log,second[40..]+"\n");
        await new JsonlCollector(await CreateAsync()).CollectAsync(sources,default);
        Assert.Equal(400,(await db.SummarizeAsync(new())).Tokens.Total);
        var unchanged=await scanner.CollectAsync(sources,default);
        Assert.Equal(0,unchanged.FilesChanged);
        Assert.Equal(400,(await db.SummarizeAsync(new())).Tokens.Total);
    }
    [Fact] public async Task CopiesAndStreamUpdatesDoNotDoubleCount()
    {
        var db=await CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(root,"a.jsonl"),ParserTests.Claude()+"\n"+ParserTests.Claude(output:80)+"\n");
        await File.WriteAllTextAsync(Path.Combine(root,"b.jsonl"),ParserTests.Claude()+"\n");
        await new JsonlCollector(db).CollectAsync([new("test",ProviderKind.Claude,root)],default);
        var summary=await db.SummarizeAsync(new());
        Assert.Equal(1,summary.Requests);
        Assert.Equal(250,summary.Tokens.Total);
    }
    [Fact] public async Task RotationPreservesHistoryAndReadsNewRecords()
    {
        var db=await CreateAsync(); var log=Path.Combine(root,"a.jsonl");
        var scanner=new JsonlCollector(db); SourceLocation[] sources=[new("test",ProviderKind.Claude,root)];
        await File.WriteAllTextAsync(log,ParserTests.Claude()+"\n"); await scanner.CollectAsync(sources,default);
        await File.WriteAllTextAsync(log,ParserTests.Claude("new-message")+"\n"); await scanner.CollectAsync(sources,default);
        Assert.Equal(400,(await db.SummarizeAsync(new())).Tokens.Total);
    }
    [Fact] public async Task UnknownForksAreReportedOutsideVerifiedTotals()
    {
        var db=await CreateAsync();
        var entry=new UsageEvent("id",ProviderKind.Codex,"fork","project","model",DateTimeOffset.UtcNow,new(10,20),IdentityQuality.UncertainFork);
        await db.CommitBatchAsync([entry],new("file",0,0,0,0,"","",new()),default);
        var summary=await db.SummarizeAsync(new());
        Assert.Equal(0,summary.Tokens.Total); Assert.Equal(1,summary.UncertainRequests);
    }
    [Fact] public async Task DayGroupingUsesRequestedTimeZone()
    {
        var db=await CreateAsync();
        var entry=new UsageEvent("id",ProviderKind.Claude,"s","p","m",DateTimeOffset.Parse("2026-09-26T22:30:00Z"),new(10,20));
        await db.CommitBatchAsync([entry],new("f",0,0,0,0,"","",new()),default);
        var zone=TimeZoneInfo.CreateCustomTimeZone("UTC+3",TimeSpan.FromHours(3),"UTC+3","UTC+3");
        var days=await db.DailyAsync(new(),zone);
        Assert.Equal(new DateOnly(2026,9,27),Assert.Single(days).Day);
        Assert.Equal(30,Assert.Single(await db.GroupAsync(new(),"model")).Tokens.Total);
    }
    [Fact] public async Task InterruptedCheckpointDoesNotSkipUnreadFileRemainder()
    {
        var db=await CreateAsync(); var path=Path.Combine(root,"session.jsonl");
        await File.WriteAllTextAsync(path,ParserTests.Claude()+"\n");
        var file=new FileInfo(path);
        await db.CommitBatchAsync([],new(path,0,file.Length,file.LastWriteTimeUtc.Ticks,0,"","",new(),ScanComplete:false),default);
        await new JsonlCollector(db).CollectAsync([new("test",ProviderKind.Claude,root)],default);
        Assert.Equal(200,(await db.SummarizeAsync(new())).Tokens.Total);
    }
    [Fact] public async Task CacheTtlReclassificationDoesNotIncreaseTotal()
    {
        var db=await CreateAsync(); var time=DateTimeOffset.UtcNow;
        var initial=new UsageEvent("id",ProviderKind.Claude,"s","p","m",time,new(100,20,0,20));
        var detailed=initial with { Tokens=new(100,20,0,10,10) };
        await db.CommitBatchAsync([initial,detailed],new("f",0,0,0,0,"","",new()),default);
        var tokens=(await db.SummarizeAsync(new())).Tokens;
        Assert.Equal(140,tokens.Total); Assert.Equal(10,tokens.CacheWrite1h);
    }
    [Fact] public async Task RetainedHandleDetectsReplacementEvenWhenLengthAndModifiedTimeMatch()
    {
        var db=await CreateAsync();var path=Path.Combine(root,"a.jsonl");
        using var scanner=new JsonlCollector(db);SourceLocation[] sources=[new("test",ProviderKind.Claude,root)];
        await File.WriteAllTextAsync(path,ParserTests.Claude("msg-1")+"\n");await scanner.CollectAsync(sources,default);
        var stamp=File.GetLastWriteTimeUtc(path);File.Move(path,Path.Combine(root,"old.log"));
        await File.WriteAllTextAsync(path,ParserTests.Claude("msg-2")+"\n");File.SetLastWriteTimeUtc(path,stamp);
        await scanner.CollectAsync(sources,default);Assert.Equal(400,(await db.SummarizeAsync(new())).Tokens.Total);
    }
}
