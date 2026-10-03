using AiUsageViewer.Core;
using AiUsageViewer.Application;
using AiUsageViewer.Infrastructure;
using AiUsageViewer.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace AiUsageViewer.Tests;
public sealed class WindowsBehaviorTests : IDisposable
{
    private readonly string directory=Path.Combine(Path.GetTempPath(),"AiUsageViewer-tests",Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-09-27T12:00:00Z");
    private static readonly AccountProfile Account=new() { Id="a",Label="Test",Provider=ProviderKind.Claude };
    private static AppSettings Settings=>new() { NotificationsEnabled=true,NotifyResets=true };
    private static AccountStatus Status(decimal? used,int minute=0,DateTimeOffset? reset=null,decimal? balance=null,ConnectionState state=ConnectionState.Ready)=>
        new(Account,new(Account.Id,Account.Provider,Now.AddMinutes(minute),[new("session","session",used,reset??Now.AddHours(1))],balance is { } money?[new("balance","balance",money)]:[]),
            Now.AddMinutes(minute),null,state,null);
    public WindowsBehaviorTests()=>Directory.CreateDirectory(directory);
    public void Dispose() { SqliteConnection.ClearAllPools();Directory.Delete(directory,true); }
    [Fact] public void QuotaNotificationIsPersistedAndDoesNotRepeatAfterRestart()
    {
        var evaluator=new NotificationEvaluator();var store=new NotificationStore(directory);
        Assert.Empty(evaluator.Observe(Status(80),Settings));
        Assert.Equal(UsageAlertKind.Usage,Assert.Single(evaluator.Observe(Status(92,1),Settings)).Kind);
        store.Save(evaluator);var restarted=store.Load();
        Assert.Empty(restarted.Observe(Status(96,5),Settings));
        Assert.Empty(restarted.Observe(Status(96,5),Settings));
        Assert.Single(restarted.Observe(Status(97,6),Settings with { NotifyUsagePercent=95 }));
    }
    [Fact] public void ResetRequiresFreshProviderEvidenceNotAnExpiredTimer()
    {
        var evaluator=new NotificationEvaluator();evaluator.Observe(Status(95),Settings);
        Assert.Empty(evaluator.Observe(Status(null,65,state:ConnectionState.Unavailable),Settings));
        var alerts=evaluator.Observe(Status(2,70,Now.AddHours(6)),Settings);
        Assert.Equal(UsageAlertKind.Reset,Assert.Single(alerts).Kind);
        Assert.Empty(evaluator.Observe(Status(2,71,Now.AddHours(6)),Settings));
    }
    [Fact] public void PaceAlertFiresOncePerWindowBeforeTheUsageThreshold()
    {
        // 5 h session, 1 h left (80 % elapsed): 70 % used is under pace, nothing to say.
        var evaluator=new NotificationEvaluator();Assert.Empty(evaluator.Observe(Status(70),Settings));
        // Same window, 3 h left (40 % elapsed): 60 % used runs out ~1 h before the reset.
        var pace=Assert.Single(evaluator.Observe(Status(60,1,Now.AddHours(3)),Settings));
        Assert.Equal(UsageAlertKind.Pace,pace.Kind);Assert.NotNull(pace.RunsOutAt);Assert.True(pace.RunsOutAt<pace.ResetsAt);
        Assert.Empty(evaluator.Observe(Status(65,2,Now.AddHours(3)),Settings));
        // A new window may warn again; turning the option off silences it.
        Assert.Single(evaluator.Observe(Status(60,400,Now.AddHours(11)),Settings),a=>a.Kind==UsageAlertKind.Pace);
        Assert.Empty(new NotificationEvaluator().Observe(Status(60,1,Now.AddHours(3)),Settings with { NotifyPace=false }));
    }
    [Fact] public void LowBalanceTriggersOnCrossingAndIgnoresMissingPartialData()
    {
        var evaluator=new NotificationEvaluator();Assert.Empty(evaluator.Observe(Status(10,balance:10),Settings));
        Assert.Equal(UsageAlertKind.LowBalance,Assert.Single(evaluator.Observe(Status(10,1,balance:4),Settings)).Kind);
        Assert.Empty(evaluator.Observe(Status(10,2,state:ConnectionState.Partial),Settings));
        Assert.Empty(evaluator.Observe(Status(10,3,balance:3),Settings));
        Assert.Empty(evaluator.Observe(Status(10,4,balance:20),Settings));
        Assert.Single(evaluator.Observe(Status(10,5,balance:1),Settings));
    }
    [Fact] public void DisabledNotificationsAndChangedConnectionsAreIndependent()
    {
        var evaluator=new NotificationEvaluator();Assert.Empty(evaluator.Observe(Status(99,balance:1),Settings with { NotificationsEnabled=false }));
        var changed=Status(99,1) with { Account=Account with { ProfileDirectory="different" } };
        Assert.Single(evaluator.Observe(changed,Settings));
    }
    [Fact] public void SettingsNormalizeBoundsAndKeepFutureVersionsUntouched()
    {
        var settings=SettingsStore.Validate(new() { Opacity=double.NaN,WidgetPlacement=new(Width:double.PositiveInfinity,Height:0),
            Accounts=[Account,Account],HiddenAccounts=["a","missing"],NotifyUsagePercent=500,NotifyLowBalance=-5,
            PriceOverrides=[new("m",Now,1,2,null,null,null,"test",null!)] });
        Assert.Equal(.97,settings.Opacity);Assert.Equal(370,settings.WidgetPlacement.Width);Assert.Equal(310,settings.WidgetPlacement.Height);
        Assert.Single(settings.Accounts);Assert.Single(settings.HiddenAccounts);Assert.Empty(settings.PriceOverrides);
        Assert.Equal(100,settings.NotifyUsagePercent);Assert.Equal(0,settings.NotifyLowBalance);
        Assert.Throws<InvalidDataException>(()=>SettingsStore.Validate(new() { Version=999 }));
    }
    [Fact] public void StartupCommandQuotesPathsAndDoesNotUseAShell()
    {
        Assert.Equal("\"C:\\Apps\\AI Usage\\AIUsageViewer.exe\" --background --data-dir \"D:\\Usage Data\"",
            WindowsStartup.BuildCommand(@"C:\Apps\AI Usage\AIUsageViewer.exe",@"D:\Usage Data\"));
        Assert.Throws<ArgumentException>(()=>WindowsStartup.BuildCommand("relative.exe",directory));
        Assert.Throws<ArgumentException>(()=>WindowsStartup.BuildCommand("C:\\invalid\".exe",directory));
    }
    [Fact] public void DpapiSecretDoesNotAppearInStoredSettingsOrCiphertext()
    {
        var secret="synthetic-api-key-test-value";var vault=new WindowsSecretStore(directory);vault.Write("reference",secret);
        Assert.Equal(secret,vault.Read("reference"));
        Assert.DoesNotContain(secret,System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Assert.Single(Directory.GetFiles(directory,"*.secrets")))));
        vault.Delete("reference");Assert.Null(vault.Read("reference"));
    }
    [Fact] public async Task MigrationPreservesUsageAndRefusesFutureSchemas()
    {
        var path=Path.Combine(directory,"usage.db");var database=new UsageDatabase(path);await database.InitializeAsync();
        await database.CommitBatchAsync([new("e",ProviderKind.Claude,"s","p","m",Now,new(10,20))],new("f",0,0,0,0,"","",new()),default);
        await using(var connection=new SqliteConnection("Data Source="+path))
        { await connection.OpenAsync();using var command=connection.CreateCommand();command.CommandText="DROP TABLE quota_observations; PRAGMA user_version=1";await command.ExecuteNonQueryAsync(); }
        await new UsageDatabase(path).InitializeAsync();Assert.Equal(30,(await database.SummarizeAsync(new())).Tokens.Total);
        await database.SaveStatusAsync(Status(42));await database.SaveStatusAsync(Status(42));
        await using(var connection=new SqliteConnection("Data Source="+path))
        {
            await connection.OpenAsync();using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM quota_observations";
            Assert.Equal(1L,await command.ExecuteScalarAsync());command.CommandText="PRAGMA user_version=999";await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InvalidDataException>(()=>database.InitializeAsync());
        Assert.Equal(30,(await database.SummarizeAsync(new())).Tokens.Total);
    }
    [Fact] public void ScreenshotsCannotReuseAnUnmarkedExistingDatabase()
    {
        File.WriteAllText(Path.Combine(directory,"usage.db"),"private-placeholder");
        Assert.Throws<InvalidDataException>(()=>DataDirectoryMode.Ensure(directory,true));
        Assert.Equal("private-placeholder",File.ReadAllText(Path.Combine(directory,"usage.db")));
        Assert.False(File.Exists(Path.Combine(directory,"synthetic-data.marker")));
    }
    [Fact] public void DemoRestartIsAllowedButLiveImportIntoDemoIsRejected()
    {
        DataDirectoryMode.Ensure(directory,true);File.WriteAllText(Path.Combine(directory,"usage.db"),"synthetic-placeholder");
        DataDirectoryMode.Ensure(directory,true);
        Assert.Throws<InvalidDataException>(()=>DataDirectoryMode.Ensure(directory,false));
    }
}
