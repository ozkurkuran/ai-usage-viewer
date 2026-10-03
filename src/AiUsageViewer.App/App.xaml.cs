using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AiUsageViewer.Core;
using AiUsageViewer.Application;
using AiUsageViewer.Application.WindowsWidgets;
using AiUsageViewer.Infrastructure.Logs;
using AiUsageViewer.Infrastructure.Providers;
using AiUsageViewer.Infrastructure.Storage;
using AiUsageViewer.Infrastructure.Analytics;
using AiUsageViewer.Infrastructure;
using Forms=System.Windows.Forms;

namespace AiUsageViewer.App;

public partial class App : System.Windows.Application
{
    private readonly CancellationTokenSource lifetime=new();
    private SettingsStore settingsStore=null!;
    private AppSettings settings=null!;
    private UsageDatabase database=null!;
    private JsonlCollector collector=null!;
    private QuotaCoordinator quotas=null!;
    private ActivityStore activityStore=null!;
    private ActivitySynchronizer activitySync=null!;
    private NotificationStore notificationStore=null!;
    private NotificationEvaluator notifications=null!;
    private DashboardViewModel viewModel=null!;
    private DashboardWindow dashboard=null!;
    private WidgetWindow widget=null!;
    private TrayPanel panel=null!;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private ProviderHttp? http;
    private Mutex? instance;
    private bool ownsInstance;
    private bool demo;
    private double captureScale=1;
    private bool validationRun;
    private readonly List<FileSystemWatcher> watchers=[];
    private DispatcherTimer? reconcileTimer;
    private DispatcherTimer? quotaTimer;
    private DispatcherTimer? displayTimer;
    private DispatcherTimer? debounce;
    private bool scanning;
    private DateTimeOffset? lastScan;
    private WindowsWidgetBridge? windowsWidgetBridge;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeManager.Register();
        demo=e.Args.Contains("--demo");
        var widgetAction=Argument(e.Args,"--windows-widget-action");
        if(e.Args.Contains("--windows-widget-action")&&(widgetAction is not ("background" or "open" or "refresh")||!PackagedApp.IsPackaged))
        { Shutdown(2);return; }
        var observation=Argument(e.Args,"--observe-seconds");
        var validatePackage=e.Args.Contains("--validate-package");
        validationRun=e.Args.Contains("--validate-live")||observation is not null||validatePackage;
        var dataDirectory=Argument(e.Args,"--data-dir")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),demo?"AiUsageViewer.Demo":"AiUsageViewer");
        Directory.CreateDirectory(dataDirectory);
        if(demo||validationRun) DispatcherUnhandledException+=(_,failure)=> {
            File.WriteAllText(Path.Combine(dataDirectory,"startup-error.txt"),demo?failure.Exception.ToString():failure.Exception.GetType().FullName);
            failure.Handled=true;Quit(3);
        };
        instance=new Mutex(false,"Local\\AiUsageViewer-"+UsageLogParser.Hash(Path.GetFullPath(dataDirectory).ToLowerInvariant())[..24]);
        try { ownsInstance=instance.WaitOne(0); } catch(AbandonedMutexException) { ownsInstance=true; }
        if(!ownsInstance) {
            if(widgetAction is not null)
                for(var retry=0;retry<30&&!WidgetAppSignals.Send(dataDirectory,widgetAction);retry++) await Task.Delay(100);
            Shutdown();return;
        }
        try
        {
            var language=Argument(e.Args,"--language");
            if(e.Args.Contains("--language")&&(!demo||!AppLanguages.IsSupported(language)))
                throw new ArgumentException("--language requires --demo and a supported language code.");
            if(demo&&language is not null)
            {
                AppLanguages.ApplyCulture(language);
            }
            if(e.Args.Contains("--capture-scale")&&(Argument(e.Args,"--screenshot") is null||!demo||
                !double.TryParse(Argument(e.Args,"--capture-scale"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out captureScale)||
                !double.IsFinite(captureScale)||captureScale is <1 or >3))
                throw new ArgumentException("--capture-scale requires demo screenshots and a value from 1 to 3.");
            var observationSeconds=0;
            if(observation is not null&&(!int.TryParse(observation,out observationSeconds)||observationSeconds is <60 or >43200||demo||
                Argument(e.Args,"--data-dir") is null||!e.Args.Contains("--background")||e.Args.Contains("--validate-live")||e.Args.Contains("--screenshot")))
                throw new ArgumentException("Observation requires --background, an explicit --data-dir and 60–43200 seconds; it cannot use demo or screenshot validation.");
            DataDirectoryMode.Ensure(dataDirectory,demo);
            settingsStore=new(dataDirectory);settings=demo?DemoSettings():settingsStore.Load();
            if(demo&&language is not null) settings=settings with { Language=AppLanguages.Resolve(language) };
            AppLanguages.ApplyCulture(settings.Language);
            notificationStore=new(dataDirectory);notifications=notificationStore.Load();
            ApplyTheme();
            database=new(Path.Combine(dataDirectory,"usage.db"));await database.InitializeAsync(lifetime.Token);
            if(validatePackage)
            {
                // Disposable Sandbox check: enables the packaged StartupTask and reports the resulting state.
                if(!PackagedApp.IsPackaged||demo) throw new InvalidOperationException("Package validation requires the installed MSIX package.");
                var startupEnabled=await PackagedApp.SetStartupEnabledAsync(true);
                var widgetRuntime=false;var widgetProvider=false;
                var widgetExecutable=Path.Combine(AppContext.BaseDirectory,"Widgets","AIUsageViewer.Widgets.exe");
                if(File.Exists(widgetExecutable)) {
                    foreach(var argument in new[]{"--validate-runtime","--validate-provider"}) {
                        using var probe=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(widgetExecutable,argument) { UseShellExecute=false,CreateNoWindow=true });
                        if(probe is null) continue;
                        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
                        try { await probe.WaitForExitAsync(timeout.Token); }
                        catch(OperationCanceledException) { probe.Kill();continue; }
                        if(argument=="--validate-runtime") widgetRuntime=probe.ExitCode==0;else widgetProvider=probe.ExitCode==0;
                    }
                }
                await File.WriteAllTextAsync(Path.Combine(dataDirectory,"package-validation.json"),JsonSerializer.Serialize(new {
                    packaged=true,familyName=PackagedApp.FamilyName,startupEnabled,widgetRuntime,widgetProvider,observedAt=DateTimeOffset.UtcNow },new JsonSerializerOptions { WriteIndented=true }));
                Quit(startupEnabled&&widgetRuntime&&widgetProvider?0:2);return;
            }
            collector=new(database);http=new();
            activityStore=new(Path.Combine(dataDirectory,"usage.db"));await activityStore.InitializeAsync(lifetime.Token);
            activitySync=new(new OpenRouterActivity(http,new WindowsSecretStore(Path.Combine(dataDirectory,"secrets"))),activityStore);
            var providers=demo?new IQuotaProvider[] { new DemoProvider(ProviderKind.Claude),new DemoProvider(ProviderKind.Codex),new DemoProvider(ProviderKind.OpenRouter) }:
                [new ClaudeProvider(http),new CodexProvider(),new OpenRouterProvider(http,new WindowsSecretStore(Path.Combine(dataDirectory,"secrets")))];
            quotas=new(providers,database);await quotas.LoadAsync(settings.Accounts,lifetime.Token);
            if(demo) await SeedDemoAsync();
            viewModel=new(database,quotas,settings,RefreshAllAsync,demo,activityStore);
            dashboard=new(viewModel);widget=new(viewModel);panel=new(viewModel);MainWindow=dashboard;
            viewModel.SettingsRequested+=OpenSettings;viewModel.WidgetRequested+=()=> { widget.Show();widget.EnsureVisible(); };
            panel.QuitRequested+=()=>Quit();
            viewModel.ExportRequested+=Export;
            viewModel.SampleRequested+=OpenSampleData;
            widget.DetailsRequested+=ShowDashboard;panel.DetailsRequested+=ShowDashboard;
            widget.ApplySettings(settings,true);
            if(PackagedApp.IsPackaged&&!demo&&!validationRun) {
                windowsWidgetBridge=new(dataDirectory,Dispatcher,ShowDashboard,RefreshAllAsync);
                viewModel.WidgetDataChanged+=PublishWindowsWidget;
            }
            quotas.Changed+=status=>Dispatcher.BeginInvoke(()=> { viewModel.UpdateAccounts();Notify(status); });
            await viewModel.QueryAsync();viewModel.UpdateAccounts();
            var screenshot=Argument(e.Args,"--screenshot");
            var validateLive=e.Args.Contains("--validate-live");
            if(screenshot is not null || validateLive)
            {
                if(screenshot is not null&&!demo) throw new InvalidOperationException("Screenshots require --demo to prevent exposing account information.");
                if(validateLive&&demo) throw new InvalidOperationException("Live validation cannot use demo fixtures.");
                var validationStarted=DateTimeOffset.UtcNow;
                if(validateLive) await RefreshAllAsync();
                else await quotas.RefreshAsync(settings.Accounts,ct:lifetime.Token);
                viewModel.UpdateAccounts();
                dashboard.Left=-10000;widget.Left=-10000;panel.Left=-10000;
                dashboard.Show();widget.Show();panel.Show();
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                if(screenshot is not null)
                {
                    Directory.CreateDirectory(screenshot);
                    Capture(dashboard,Path.Combine(screenshot,"dashboard.png"),captureScale);Capture(widget,Path.Combine(screenshot,"widget.png"),captureScale);
                    Capture(panel,Path.Combine(screenshot,"tray.png"),captureScale);
                    if(e.Args.Contains("--smoke-suite")) await RunSmokeSuiteAsync(screenshot,e.Args.Contains("--views"));
                }
                if(validateLive)
                {
                    var summary=await database.SummarizeAsync(new());
                    var enabled=settings.Accounts.Where(a=>a.Enabled).ToList();
                    var dataAndRenderingPassed=summary.Requests>0&&dashboard.IsLoaded&&widget.IsLoaded&&panel.IsLoaded&&enabled.Count>0&&
                        enabled.All(a=>quotas.Statuses.Any(s=>s.Account.SameConnection(a)&&s.LastGood is not null));
                    var freshQuotaPassed=enabled.Count>0&&enabled.All(a=>quotas.Statuses.Any(s=>s.Account.SameConnection(a)&&
                        s.State is ConnectionState.Ready or ConnectionState.Partial&&s.LastGood?.ObservedAt>=validationStarted));
                    var passed=dataAndRenderingPassed&&freshQuotaPassed;
                    await File.WriteAllTextAsync(Path.Combine(dataDirectory,"ui-validation.json"),JsonSerializer.Serialize(new {
                        passed,dataAndRenderingPassed,freshQuotaPassed,observedAt=DateTimeOffset.UtcNow,storedRecords=summary.Requests,uiAccountCards=viewModel.Accounts.Count,
                        accounts=quotas.Statuses.Select(s=>new { provider=s.Account.Provider.ToString(),state=s.State.ToString(),windows=s.LastGood?.Windows.Count??0 }),
                        windowsRendered=new[]{dashboard.IsLoaded,widget.IsLoaded,panel.IsLoaded}
                    },new JsonSerializerOptions { WriteIndented=true }));
                    if(!passed) { Quit(2);return; }
                }
                File.Delete(Path.Combine(dataDirectory,"startup-error.txt"));
                Quit();return;
            }
            // An interactive sample-data instance ends with its window so it never lingers in the notification area.
            if(demo) { dashboard.AllowClose=true;dashboard.Closed+=(_,_)=>Quit(); }
            CreateTray();if(widgetAction=="open"||(widgetAction is null&&!e.Args.Contains("--background")&&!PackagedApp.LaunchedByStartupTask())) ShowDashboard();
            if(settings.ShowWidgetOnLaunch&&observation is null&&widgetAction is null) widget.Show();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplayChanged;
            Microsoft.Win32.SystemEvents.PowerModeChanged+=PowerChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged+=ThemePreferenceChanged;
            SetUpWatchers();
            reconcileTimer=new(TimeSpan.FromSeconds(30),DispatcherPriority.Background,async(_,_)=>await CollectSafelyAsync(),Dispatcher);
            quotaTimer=new(TimeSpan.FromSeconds(30),DispatcherPriority.Background,async(_,_)=>await Task.WhenAll(RefreshQuotasSafelyAsync(false),RefreshActivitySafelyAsync(false)),Dispatcher);
            displayTimer=new(TimeSpan.FromSeconds(30),DispatcherPriority.Background,(_,_)=> { if(windowsWidgetBridge is not null||widget.IsVisible||dashboard.IsVisible||panel.IsVisible) viewModel.UpdateAccounts(); },Dispatcher);
            await RefreshAllAsync();
            if(observation is not null) { await ObserveRuntimeAsync(observationSeconds);Quit(); }
        }
        catch(Exception ex)
        {
            // Exception text can contain local paths or provider payloads; record only its type.
            File.WriteAllText(Path.Combine(dataDirectory,"startup-error.txt"),demo?ex.ToString():ex.GetType().FullName);
            if(Argument(e.Args,"--screenshot") is null&&!validationRun) System.Windows.MessageBox.Show(new Localization(settings?.Language??new AppSettings().Language)["startFailed"]+ex.GetType().Name,Localization.AppName,MessageBoxButton.OK,MessageBoxImage.Error);
            Quit(1);
        }
    }

    private static string? Argument(string[] args,string key) { var index=Array.IndexOf(args,key);return index>=0&&index+1<args.Length?args[index+1]:null; }
    private void PublishWindowsWidget()
    {
        try { WidgetSnapshotFile.Write(settingsStore.DirectoryPath,viewModel.CreateWindowsWidgetSnapshot()); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
    }
    // Separate process with its own data folder, so sample records never mix with real usage.
    private void OpenSampleData()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!,["--demo","--language",AppLanguages.Resolve(settings.Language)]) { UseShellExecute=false })?.Dispose(); }
        catch(Exception ex) when(ex is Win32Exception or InvalidOperationException) { MessageBox.Show(dashboard,viewModel.L["startFailed"]+ex.GetType().Name,Localization.AppName); }
    }
    private void ShowDashboard() { dashboard.Show();dashboard.WindowState=WindowState.Normal;dashboard.Activate(); }
    private async Task RefreshAllAsync()=>await Task.WhenAll(CollectSafelyAsync(),RefreshQuotasSafelyAsync(true),RefreshActivitySafelyAsync(true));
    private async Task RefreshActivitySafelyAsync(bool manual)
    {
        if(demo) return;
        try { await activitySync.RefreshAsync(settings.Accounts,manual,lifetime.Token); }
        catch(OperationCanceledException) {} catch(Exception ex) when(ex is IOException or Microsoft.Data.Sqlite.SqliteException) { viewModel.Status=viewModel.L["unavailable"]; }
    }
    private async Task CollectSafelyAsync()
    {
        if(scanning||demo) return;scanning=true;
        try
        {
            var progress=await collector.CollectAsync(settings.Sources,lifetime.Token);lastScan=DateTimeOffset.Now;
            observedScans++;observedChangedFiles+=progress.FilesChanged;observedSourceWarnings=progress.Warnings.Count;
            await viewModel.QueryAsync();
            if(progress.Warnings.Count>0) viewModel.Status=viewModel.L["sourceErrors"]+" · "+progress.Warnings.Count;
        }
        catch(OperationCanceledException) {} catch(Exception ex) when(ex is IOException or Microsoft.Data.Sqlite.SqliteException)
        { observedCollectionFailures++;viewModel.Status=viewModel.L["sourceErrors"]; }
        finally { scanning=false; }
    }
    private async Task RefreshQuotasSafelyAsync(bool manual)
    {
        try { await quotas.RefreshAsync(settings.Accounts,manual,lifetime.Token);viewModel.UpdateAccounts(); }
        catch(OperationCanceledException) {} catch(Exception ex) when(ex is IOException or Microsoft.Data.Sqlite.SqliteException)
        { viewModel.Status=viewModel.L["unavailable"]; }
    }
    private void SetUpWatchers()
    {
        foreach(var watcher in watchers) watcher.Dispose();watchers.Clear();
        debounce??=new DispatcherTimer { Interval=TimeSpan.FromSeconds(2) };
        debounce.Tick-=DebouncedScan;debounce.Tick+=DebouncedScan;
        foreach(var source in settings.Sources.Where(s=>s.Enabled&&Directory.Exists(s.Directory)))
        {
            try
            {
                var watcher=new FileSystemWatcher(source.Directory,"*.jsonl") { IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size };
                watcher.Changed+=ScheduleScan;watcher.Created+=ScheduleScan;watcher.Renamed+=ScheduleScan;
                watcher.EnableRaisingEvents=true;watchers.Add(watcher);
            }
            catch(IOException) {} catch(UnauthorizedAccessException) {}
        }
    }
    private void ScheduleScan(object sender,FileSystemEventArgs e)=>Dispatcher.BeginInvoke(()=> { debounce!.Stop();debounce.Start(); });
    private async void DebouncedScan(object? sender,EventArgs e) { debounce!.Stop();await CollectSafelyAsync(); }
    private void DisplayChanged(object? sender,EventArgs e)=>Dispatcher.BeginInvoke(widget.EnsureVisible);
    private void PowerChanged(object sender,Microsoft.Win32.PowerModeChangedEventArgs e)
    { if(e.Mode==Microsoft.Win32.PowerModes.Resume) Dispatcher.BeginInvoke(async()=>await RefreshAllAsync()); }
    private void CreateTray()
    {
        trayIcon??=LoadTrayIcon();
        tray=new Forms.NotifyIcon { Text=Localization.AppName,Icon=trayIcon,Visible=true };
        tray.MouseClick+=(_,e)=> { if(e.Button==Forms.MouseButtons.Left) panel.ShowAtCursor(); };
        tray.DoubleClick+=(_,_)=>ShowDashboard();
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add(viewModel.L["details"],null,(_,_)=>ShowDashboard());
        menu.Items.Add(viewModel.L["widget"],null,(_,_)=> { widget.Show();widget.EnsureVisible(); });
        menu.Items.Add(viewModel.L["settings"],null,(_,_)=>OpenSettings(null));
        menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add(viewModel.L["exit"],null,(_,_)=>Quit());
        tray.ContextMenuStrip=menu;
    }
    private static System.Drawing.Icon LoadTrayIcon()
    {
        using var stream=GetResourceStream(new Uri("pack://application:,,,/Assets/AppIcon.ico"))?.Stream;
        return stream is null?System.Drawing.SystemIcons.Application:new System.Drawing.Icon(stream,Forms.SystemInformation.SmallIconSize);
    }
    // Returns the startup state actually in effect.
    private async Task<bool> ApplyStartupAsync(bool enabled)
    {
        if(!PackagedApp.IsPackaged) { WindowsStartup.SetEnabled(enabled,Environment.ProcessPath!,Path.GetFullPath(settingsStore.DirectoryPath));return enabled; }
        var applied=await PackagedApp.SetStartupEnabledAsync(enabled);
        if(applied!=enabled) MessageBox.Show(viewModel.L["startupManagedByWindows"],Localization.AppName);
        return applied;
    }
    private async void OpenSettings(string? section)
    {
        var secrets=new WindowsSecretStore(Path.Combine(settingsStore.DirectoryPath,"secrets"));
        // Users can change a packaged startup task in Windows Settings; show its actual state.
        if(!demo&&PackagedApp.IsPackaged)
            try { settings=settings with { StartWithWindows=await PackagedApp.IsStartupEnabledAsync() }; } catch(COMException) {}
        var before=settings with { WidgetPlacement=NativePlacement.Capture(widget) };
        var window=new SettingsWindow(before,secrets,TestConnectionAsync,new(quotas,database,section,CollectSafelyAsync,()=>lastScan)) { Owner=dashboard.IsVisible?dashboard:null };
        if(window.ShowDialog()==true && window.Result is { } saved)
        {
            try
            {
                if(!demo&&saved.StartWithWindows!=settings.StartWithWindows) saved=saved with { StartWithWindows=await ApplyStartupAsync(saved.StartWithWindows) };
                settingsStore.Save(saved);
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or COMException)
            {
                try { if(!demo&&saved.StartWithWindows!=settings.StartWithWindows) await ApplyStartupAsync(settings.StartWithWindows); } catch(Exception rollback) when(rollback is IOException or UnauthorizedAccessException or System.Security.SecurityException or COMException) {}
                MessageBox.Show(viewModel.L["saveFailed"],Localization.AppName);return;
            }
            settings=saved;AppLanguages.ApplyCulture(settings.Language);ApplyTheme();widget.ApplySettings(settings);viewModel.ApplySettings(settings);
            foreach(var reference in before.Accounts.Select(a=>a.SecretReference).OfType<string>().Where(r=>!settings.Accounts.Any(a=>a.SecretReference==r)))
                try { secrets.Delete(reference); } catch(IOException) {} catch(UnauthorizedAccessException) {}
            SetUpWatchers();tray?.Dispose();CreateTray();await quotas.LoadAsync(settings.Accounts,lifetime.Token);await RefreshAllAsync();
        }
    }
    private async Task<string> TestConnectionAsync(AccountProfile account,string? key,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(40));
        IQuotaProvider provider=demo?new DemoProvider(account.Provider):account.Provider switch {
            ProviderKind.Claude=>new ClaudeProvider(http!),ProviderKind.Codex=>new CodexProvider(),
            _=>new OpenRouterProvider(http!,new TestSecrets(new WindowsSecretStore(Path.Combine(settingsStore.DirectoryPath,"secrets")),key)) };
        if(key is not null) account=account with { SecretReference="connection-test" };
        var result=await provider.FetchAsync(account,deadline.Token);
        var state=result.State switch { ConnectionState.Ready=>"ready",ConnectionState.Partial=>"partial",ConnectionState.SignInRequired=>"signIn",ConnectionState.RateLimited=>"rateLimited",ConnectionState.Unsupported=>"unsupported",_=>"unavailable" };
        return viewModel.L[state]+(result.Snapshot is { } snapshot?$" · {viewModel.L["quota"]}: {snapshot.Windows.Count} · {viewModel.L["reportedSpend"]}/{viewModel.L["balance"]}: {snapshot.Money.Count}":"");
    }
    private sealed class TestSecrets(ISecretStore saved,string? key):ISecretStore
    { public string? Read(string reference)=>key??saved.Read(reference);public void Write(string reference,string value)=>throw new NotSupportedException();public void Delete(string reference)=>throw new NotSupportedException(); }
    private void Notify(AccountStatus status)
    {
        if(demo||validationRun||tray is null||!settings.Accounts.Any(a=>a.Enabled&&a.SameConnection(status.Account))) return;
        var alerts=notifications.Observe(status,settings);
        if(alerts.Count>0)
        {
            // Same templates as the Settings preview; extra alerts are listed by title.
            var texts=alerts.Take(4).Select(a=>NotificationText.Build(viewModel.L,viewModel.Format,a,DateTimeOffset.Now)).ToList();
            var body=texts.Count==1?texts[0].Body:string.Join(Environment.NewLine,texts.Select(t=>t.Title));
            tray.ShowBalloonTip(6000,texts.Count==1?texts[0].Title:Localization.AppName,body.Length>0?body:texts[0].Title,Forms.ToolTipIcon.Info);
        }
        try { notificationStore.Save(notifications); } catch(IOException) {} catch(UnauthorizedAccessException) {}
    }
    private async void Export()
    {
        if(viewModel.IsOpenRouter) { await ExportActivityAsync();return; }
        var dialog=new Microsoft.Win32.SaveFileDialog { Filter="CSV (*.csv)|*.csv|JSON (*.json)|*.json",FileName="ai-usage-summary.csv" };
        if(dialog.ShowDialog(dashboard)!=true) return;
        try { var records=await database.ReadEventsAsync(viewModel.CurrentFilter());await UsageExport.WriteAsync(dialog.FileName,records); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        { MessageBox.Show(dashboard,viewModel.L["exportFailed"],Localization.AppName); }
    }
    // OpenRouter page: the stored activity rows of the selected account, invariant-culture CSV.
    private async Task ExportActivityAsync()
    {
        if(viewModel.OpenRouter.SelectedAccount is not { } account) return;
        var dialog=new Microsoft.Win32.SaveFileDialog { Filter="CSV (*.csv)|*.csv",FileName="openrouter-activity.csv" };
        if(dialog.ShowDialog(dashboard)!=true) return;
        try
        {
            static string Field(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
            var rows=await activityStore.ReadAsync(account.Id);var culture=System.Globalization.CultureInfo.InvariantCulture;
            var lines=new List<string> { "date_utc,model,input_tokens,output_tokens,requests,spend,byok_spend,currency" };
            lines.AddRange(rows.OrderByDescending(r=>r.Day).Select(r=>string.Join(",",r.Day.ToString("yyyy-MM-dd",culture),Field(r.Model),r.Input.ToString(culture),r.Output.ToString(culture),
                r.Requests.ToString(culture),r.Spend.ToString(culture),r.ByokSpend.ToString(culture),r.Currency)));
            await File.WriteAllLinesAsync(dialog.FileName,lines);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        { MessageBox.Show(dashboard,viewModel.L["exportFailed"],Localization.AppName); }
    }
    private void ApplyTheme()=>ThemeManager.Apply(settings.Theme);
    private void ThemePreferenceChanged(object sender,Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if(e.Category==Microsoft.Win32.UserPreferenceCategory.General&&settings?.Theme=="system")
            Dispatcher.BeginInvoke(()=> { var wasDark=ThemeManager.IsDark;ApplyTheme();if(wasDark!=ThemeManager.IsDark) viewModel?.ApplySettings(settings); });
    }
    private static void Capture(Window window,string path,double scale=1)
    {
        window.UpdateLayout();var content=(FrameworkElement)window.Content;
        var bitmap=new RenderTargetBitmap((int)(content.ActualWidth*scale),(int)(content.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()) {
            var bounds=new Rect(0,0,content.ActualWidth,content.ActualHeight);
            drawing.DrawRectangle(window.Background,null,bounds);
            // Map exactly the laid-out area; content bounds can be stale right after a layout switch.
            drawing.DrawRectangle(new VisualBrush(content) { Stretch=Stretch.Fill,ViewboxUnits=BrushMappingMode.Absolute,Viewbox=bounds },null,bounds);
        }
        bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(path);encoder.Save(stream);
    }
    private async Task SeedDemoAsync()
    {
        if((await database.SummarizeAsync(new())).Requests>0) return;
        var random=new Random(41);var events=new List<UsageEvent>();
        for(var day=0;day<91;day++)
        {
            if(day>0&&day%7==0) continue;
            for(var i=0;i<8;i++)
            {
                var time=DateTimeOffset.Now.Date.AddDays(-day).AddHours(8+i);
                events.Add(new($"demo-{day}-{i}",i%2==0?ProviderKind.Claude:ProviderKind.Codex,$"session-{day}-{i/2}",i%3==0?"ai-usage-viewer":"sample-project",
                    i%2==0?"claude-sonnet-4-5":"gpt-5.3-codex",time,new(random.Next(500,14000),random.Next(100,2500),random.Next(0,14000))));
            }
        }
        await database.CommitBatchAsync(events,new("demo",0,0,0,0,"","",new()),default);
        var now=DateTimeOffset.UtcNow;var yesterday=DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1);
        await activityStore.SaveAsync(new("demo-openrouter","demo-openrouter-key",ConnectionState.Ready,now,now.AddMinutes(15),now,yesterday.AddDays(-29),yesterday,null),
            [new("demo-openrouter",yesterday,"openai/gpt-4.1","demo-endpoint",25000,15000,0,8,.75m,.12m)],false);
    }
    private static AppSettings DemoSettings()=>new() { Accounts=[new() { Id="demo-claude",Provider=ProviderKind.Claude,Label="Claude Pro" },new() { Id="demo-codex",Provider=ProviderKind.Codex,Label="ChatGPT Plus" },
        new() { Id="demo-openrouter",Provider=ProviderKind.OpenRouter,Label="OpenRouter demo",SecretReference="demo-openrouter-key" }] };
    private sealed class DemoProvider(ProviderKind kind):IQuotaProvider
    {
        public ProviderKind Kind=>kind;
        public ProviderCapabilities Capabilities=>new(true,true,false,false,false);
        public Task<ProviderResult> FetchAsync(AccountProfile account,CancellationToken cancellationToken)
        {
            var now=DateTimeOffset.UtcNow;
            if(kind==ProviderKind.OpenRouter)
                return Task.FromResult(new ProviderResult(ConnectionState.Ready,new(account.Id,kind,now,[],[new("balance","balance",9.25m),new("usage_monthly","usage_monthly",0.75m)])));
            return Task.FromResult(new ProviderResult(ConnectionState.Ready,
                new(account.Id,kind,now,[new("session","five_hour",kind==ProviderKind.Claude?38:21,now.AddHours(1).AddMinutes(59),WindowMinutes:300),
                    new("week","seven_day",kind==ProviderKind.Claude?54:67,kind==ProviderKind.Claude?now.AddDays(2).AddHours(23):now.AddDays(3).AddHours(21),WindowMinutes:10080)],[])));
        }
    }
    private void Quit(int code=0)
    {
        if(!demo && !validationRun && settings is not null && widget is not null)
        {
            settings=settings with { WidgetPlacement=NativePlacement.Capture(widget) };
            try { settingsStore.Save(settings); } catch(IOException) {} catch(UnauthorizedAccessException) {}
        }
        windowsWidgetBridge?.Dispose();
        if(viewModel is not null) viewModel.WidgetDataChanged-=PublishWindowsWidget;
        lifetime.Cancel();reconcileTimer?.Stop();quotaTimer?.Stop();displayTimer?.Stop();debounce?.Stop();
        foreach(var watcher in watchers) watcher.Dispose();tray?.Dispose();http?.Dispose();
        collector?.Dispose();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplayChanged;Microsoft.Win32.SystemEvents.PowerModeChanged-=PowerChanged;Microsoft.Win32.SystemEvents.UserPreferenceChanged-=ThemePreferenceChanged;
        if(dashboard is not null) dashboard.AllowClose=true;if(widget is not null) widget.AllowClose=true;if(panel is not null) panel.AllowClose=true;
        Shutdown(code);
    }
    protected override void OnExit(ExitEventArgs e) { if(ownsInstance) instance?.ReleaseMutex();instance?.Dispose();base.OnExit(e); }
}
