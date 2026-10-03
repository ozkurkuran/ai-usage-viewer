using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AiUsageViewer.Core;
using AiUsageViewer.Application;
using AiUsageViewer.Infrastructure.Storage;

namespace AiUsageViewer.App;
public partial class App
{
    private async Task RunSmokeSuiteAsync(string directory,bool views=false)
    {
        // Called only from synthetic screenshot mode; does not touch startup, user profiles or the network.
        var checks=new List<string>();
        void Progress(string stage)=>File.WriteAllText(Path.Combine(directory,"smoke-stage.txt"),stage);
        Progress("begin");
        void Check(bool condition,string name) { if(!condition) throw new InvalidOperationException("Smoke check failed: "+name);checks.Add(name); }
        var emptyDatabase=new UsageDatabase(Path.Combine(settingsStore.DirectoryPath,"empty-smoke.db"));await emptyDatabase.InitializeAsync();
        var emptyModel=new DashboardViewModel(emptyDatabase,quotas,new AppSettings { Language=settings.Language },()=>Task.CompletedTask,true);await emptyModel.QueryAsync();emptyModel.UpdateAccounts();
        Check(emptyModel.Total=="0"&&emptyModel.WidgetTotal=="0"&&emptyModel.EstimatedCost=="—"&&emptyModel.WidgetStatus==emptyModel.L["noData"]&&emptyModel.Accounts.Count==0&&emptyModel.ShowGettingStarted,"empty data is explained without invented cost or accounts");
        Check(!viewModel.ShowGettingStarted,"getting-started guidance hidden once usage records exist");
        var emptyWindow=new DashboardWindow(emptyModel) { Left=-10000,Top=-10000,WindowStartupLocation=WindowStartupLocation.Manual,AllowClose=true };
        emptyWindow.Show();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture(emptyWindow,Path.Combine(directory,"empty-data.png"),captureScale);
        var emptySetup=Find<Border>(emptyWindow,"SetupCard");emptyWindow.Close();
        Check(emptySetup.Visibility==Visibility.Visible&&emptyModel.SetupSteps.Count==4&&Find<Border>(dashboard,"SetupCard").Visibility==Visibility.Collapsed&&emptyModel.HeroValue=="—"&&emptyModel.CostValue=="—",
            "first-run checklist only with zero records");
        Check(viewModel.Accounts.Count==2&&viewModel.WidgetAccounts.Count==2,"default account surfaces");
        var claudeCard=viewModel.Accounts.Single(a=>a.Provider=="Claude");var codexCard=viewModel.Accounts.Single(a=>a.Provider=="Codex");
        Check(Math.Abs(claudeCard.Windows.Single(w=>w.Id=="session").Marker-60.3)<1&&Math.Abs(claudeCard.Windows.Single(w=>w.Id=="week").Marker-57.7)<1&&
            claudeCard.Windows.All(w=>w.MarkerTip==viewModel.L["markerUsedTip"]),"pace marker position");
        var codexWeek=codexCard.Windows.Single(w=>w.Id=="week");
        Check(codexWeek.IsAhead&&codexWeek.HasForecast&&codexWeek.FillKey=="Warn.Bar"&&claudeCard.Windows.All(w=>!w.HasForecast&&!w.IsAhead)&&
            codexCard.Windows.Single(w=>w.Id=="session").FillKey=="Codex.Bar","forecast only when ahead of pace");
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        Check(!widget.AccountsScroll&&widget.ActualHeight<=SystemParameters.WorkArea.Height,"widget fits two accounts without a scrollbar");
        await CheckStaleAsync(directory,Check);
        var todayTotal=viewModel.WidgetTotal;var todayCost=viewModel.WidgetCost;
        viewModel.NavigateCommand.Execute("projects");await viewModel.QueryAsync();
        var project=viewModel.Groups.First();viewModel.OpenGroupCommand.Execute(project);await viewModel.QueryAsync();
        Check(viewModel.Page=="sessions"&&viewModel.CurrentFilter().Project==project.Name,"project to sessions");
        viewModel.OpenGroupCommand.Execute(viewModel.Groups.First());await viewModel.QueryAsync();
        Check(viewModel.Page=="models"&&viewModel.CurrentFilter().SessionId is not null,"session to models");
        Check(viewModel.WidgetTotal==todayTotal&&viewModel.WidgetCost==todayCost,"widget today totals independent of project/session filter");
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        Capture(dashboard,Path.Combine(directory,"session-detail.png"),captureScale);
        viewModel.NavigateCommand.Execute("overview");await viewModel.QueryAsync();
        viewModel.Range=viewModel.Ranges.Single(r=>r.Key=="all");viewModel.Tool=viewModel.Tools.Single(t=>t.Key=="Codex");
        viewModel.Model=viewModel.Models.First(m=>m.Key.Length>0);await viewModel.QueryAsync();
        Check(viewModel.WidgetTotal==todayTotal&&viewModel.WidgetCost==todayCost&&viewModel.TodayLabel==viewModel.L["today"]&&viewModel.WidgetStatus!=viewModel.L["noData"],"widget today totals independent of date/tool/model filter");
        viewModel.Range=viewModel.Ranges[0];viewModel.Tool=viewModel.Tools[0];viewModel.Model=viewModel.Models[0];await viewModel.QueryAsync();
        var original=settings;
        Progress("change-language-theme");
        settings=original with { Language="en",Theme="light",Compact=true,ShowRemaining=true,HiddenAccounts=["demo-claude"],AccountOrder=["demo-codex","demo-claude"] };
        ApplyTheme();widget.ApplySettings(settings);viewModel.ApplySettings(settings);await viewModel.QueryAsync();
        Progress("verify-language-theme");
        Check(viewModel.Accounts.Count==2&&viewModel.WidgetAccounts.Count==1&&viewModel.Accounts[0].Provider=="Codex","widget-only visibility and ordering");
        Check(viewModel.WidgetAccounts[0].Windows[0].Percent.StartsWith("79%"),"remaining mode");
        var left=viewModel.WidgetAccounts[0].Windows.Single(w=>w.Id=="session");
        Check(Math.Abs(left.Value-79)<0.01&&Math.Abs(left.Marker-(100-60.3))<1&&left.MarkerTip==viewModel.L["markerLeftTip"]&&viewModel.PaceLegend==viewModel.L["paceLegendLeft"],
            "remaining-mode inversion of fill and marker");
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        Capture(widget,Path.Combine(directory,"widget-light-compact-en.png"),captureScale);
        settings=original with { Theme="light",Compact=true,ShowRemaining=true,HiddenAccounts=["demo-claude"],AccountOrder=["demo-codex","demo-claude"] };
        ApplyTheme();widget.ApplySettings(settings);viewModel.ApplySettings(settings);await viewModel.QueryAsync();
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        Capture(widget,Path.Combine(directory,"widget-light-compact.png"),captureScale);
        settings=original;ApplyTheme();widget.ApplySettings(settings);viewModel.ApplySettings(settings);await viewModel.QueryAsync();
        var preferences=new SettingsWindow(settings,new WindowsSecretStore(Path.Combine(settingsStore.DirectoryPath,"secrets"))) { Left=-10000,Top=-10000,WindowStartupLocation=WindowStartupLocation.Manual };
        preferences.Show();
        var languageChoices=Find<ComboBox>(preferences,"LanguageChoice");
        Check(languageChoices.Items.Count==AppLanguages.Supported.Count+1&&
            (string)languageChoices.SelectedValue==settings.Language,"all languages selectable and saved choice selected");
        Check(viewModel.L.Language==AppLanguages.Resolve(settings.Language)&&
            System.Globalization.CultureInfo.CurrentCulture.Name==AppLanguages.CultureFor(settings.Language).Name,"UI language and formatting culture agree");
        Progress("settings-pages");
        for(var i=0;i<preferences.PageCount;i++)
        {
            preferences.SelectPage(i);await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            if(i==1) Find<ListBox>(preferences,"AccountList").SelectedIndex=0;
            if(i==3) Find<ListBox>(preferences,"PriceList").SelectedIndex=0;
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Capture(preferences,Path.Combine(directory,$"settings-{i}.png"),captureScale);
        }
        preferences.Close();checks.Add("all settings pages render");
        var edit=new SettingsWindow(settings,new WindowsSecretStore(Path.Combine(settingsStore.DirectoryPath,"secrets"))) { Left=-10000,Top=-10000,WindowStartupLocation=WindowStartupLocation.Manual };
        _=Dispatcher.BeginInvoke(()=> {
            edit.SelectPage(1);edit.UpdateLayout();Find<ListBox>(edit,"AccountList").SelectedIndex=0;edit.UpdateLayout();
            Check(!Descendants(edit).OfType<PasswordBox>().Any(p=>p.Name=="ApiKey")&&Descendants(edit).OfType<TextBox>().Any(t=>t.Name=="ProfileFolder"),"no API key field for Claude or ChatGPT accounts");
            var keyAccount=Find<ListBox>(edit,"AccountList").Items.Cast<ListBoxItem>().ToList().FindIndex(i=>(string)i.Tag=="demo-openrouter");
            if(keyAccount>=0) { Find<ListBox>(edit,"AccountList").SelectedIndex=keyAccount;edit.UpdateLayout();Check(Descendants(edit).OfType<PasswordBox>().Any(p=>p.Name=="ApiKey"),"API key field for OpenRouter accounts"); }
            Find<ListBox>(edit,"AccountList").SelectedIndex=0;edit.UpdateLayout();
            Find<TextBox>(edit,"AccountName").Text="Renamed synthetic account";Find<CheckBox>(edit,"AccountWidgetToggle").IsChecked=false;
            edit.SelectPage(3);edit.UpdateLayout();Find<ListBox>(edit,"PriceList").SelectedIndex=0;edit.UpdateLayout();Find<TextBox>(edit,"Rate0").Text="7,5";
            Check(((string?)Find<Button>(edit,"PriceSource").Tag)?.StartsWith("https://")==true,"price source visible");
            edit.SelectPage(4);edit.UpdateLayout();Find<CheckBox>(edit,"PaceToggle").IsChecked=!settings.NotifyPace;
            edit.SelectPage(0);edit.UpdateLayout();
            Check(edit.DirtySections.SequenceEqual(["accounts","prices","notifications"]),"dirty sections listed before Save");
            Find<Button>(edit,"SaveSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Check(edit.ShowDialog()==true&&edit.Result?.Accounts[0].Label=="Renamed synthetic account"&&edit.Result.HiddenAccounts.Contains(settings.Accounts[0].Id)&&edit.Result.PriceOverrides.Single().InputPerMillion==7.5m,
            "main Save commits account, visibility and pending price edits");
        Check(edit.Result!.NotifyPace!=settings.NotifyPace,"Settings Save commits all dirty sections");
        var account=new AccountProfile { Id="synthetic-openrouter",Label="OpenRouter demo",Provider=ProviderKind.OpenRouter,SecretReference="synthetic-reference" };
        var now=DateTimeOffset.UtcNow;var day=DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1);
        await activityStore.SaveAsync(new(account.Id,account.SecretReference!,ConnectionState.Ready,now,now.AddMinutes(15),now,day.AddDays(-29),day,null),
            [new(account.Id,day,"openai/gpt-4.1","demo-endpoint",25000,15000,3000,8,.75m,.12m)],false);
        viewModel.NavigateCommand.Execute("openrouter");viewModel.OpenRouter.UseAccounts([account]);await viewModel.OpenRouter.LoadAsync();
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        Progress("history-view");
        Check(viewModel.OpenRouter.Rows.Count==1&&viewModel.OpenRouter.Bars.Count==30&&viewModel.OpenRouter.Requests=="8","synthetic activity view");
        Capture(dashboard,Path.Combine(directory,"openrouter-history.png"),captureScale);
        viewModel.OpenRouter.ApplySettings(settings);viewModel.NavigateCommand.Execute("overview");await viewModel.QueryAsync();
        foreach(var scale in new[]{1d,1.5d,2d}) Capture(widget,Path.Combine(directory,$"widget-render-{scale*100:0}.png"),scale);
        checks.Add("100/150/200 percent render targets (not physical DPI switching)");
        var monitors=System.Windows.Forms.Screen.AllScreens.Select(s=>new { s.DeviceName,s.Primary,s.WorkingArea.Width,s.WorkingArea.Height }).ToList();
        var saved=NativePlacement.Capture(widget);
        foreach(var monitor in System.Windows.Forms.Screen.AllScreens)
        {
            NativePlacement.Restore(widget,new(Width:widget.Width,Height:widget.Height,MonitorDevice:monitor.DeviceName,OffsetX:1000000,OffsetY:-500));
            widget.EnsureVisible();var positioned=NativePlacement.Capture(widget);
            Check(positioned.OffsetX>=0&&positioned.OffsetY>=0,"monitor placement "+monitor.DeviceName);
        }
        NativePlacement.Restore(widget,saved);widget.Left=-10000;
        if(views) { Progress("views");await CaptureViewsAsync(directory);checks.Add("every view captured in dark and light at 100/150/200 percent"); }
        CreateTray();dashboard.Close();widget.Close();panel.Close();
        Check(!dashboard.IsVisible&&!widget.IsVisible&&!panel.IsVisible&&tray?.Visible==true,"closing windows keeps tray alive");
        Progress("complete");
        var stored=await database.SummarizeAsync(new());
        await File.WriteAllTextAsync(Path.Combine(directory,"smoke-report.json"),JsonSerializer.Serialize(new { passed=true,observedAt=now,checks,monitors,actualDpi=VisualTreeHelper.GetDpi(widget).PixelsPerInchX,
            runtimeDirectory=System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),storedRecords=stored.Requests,storedTokens=stored.Tokens.Total },new JsonSerializerOptions { WriteIndented=true }));
    }
    // A refresh that fails after a good one leaves the account stale: amber pill, callout, desaturated bars, no marker or forecast.
    private DashboardViewModel? staleModel;
    private async Task CheckStaleAsync(string directory,Action<bool,string> check)
    {
        var clock=new ManualClock(DateTimeOffset.UtcNow);
        var account=new AccountProfile { Id="synthetic-stale",Provider=ProviderKind.Codex,Label="ChatGPT Plus" };
        var store=new UsageDatabase(Path.Combine(settingsStore.DirectoryPath,"stale-smoke.db"));await store.InitializeAsync();
        var staleQuotas=new QuotaCoordinator([new FailingAfterFirst()],store,clock);
        await staleQuotas.RefreshAsync([account]);clock.Now=clock.Now.AddMinutes(6);await staleQuotas.RefreshAsync([account]);
        var model=new DashboardViewModel(store,staleQuotas,new AppSettings { Language=settings.Language,Accounts=[account] },()=>Task.CompletedTask,true);
        await model.QueryAsync();model.UpdateAccounts();
        var card=model.Accounts.Single();staleModel=model;
        check(card.IsStale&&model.PillKind=="stale"&&card.Windows.All(w=>double.IsNaN(w.Marker)&&!w.HasForecast&&w.FillKey.EndsWith(".StaleBar")&&w.ValueKey=="Text.Secondary"),
            "stale account rendering");
        var tray=new TrayPanel(model) { Left=-10000,Top=-10000,AllowClose=true };tray.Show();
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture(tray,Path.Combine(directory,"tray-stale.png"),captureScale);tray.Close();
    }
    // Screenshot matrix: each view in dark and light at 1×, 1.5× and 2× render scale, in the run's language.
    private async Task CaptureViewsAsync(string directory)
    {
        var original=settings;double[] scales=[1,1.5,2];
        async Task Idle()=>await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        async Task Apply(AppSettings value) { settings=value;ApplyTheme();widget.ApplySettings(settings);viewModel.ApplySettings(settings);await viewModel.QueryAsync();await Idle(); }
        void Shot(Window window,string folder,string name) { window.UpdateLayout();foreach(var scale in scales) Capture(window,Path.Combine(folder,$"{name}@{scale.ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)}x.png"),scale); }
        foreach(var theme in new[]{"dark","light"})
        {
            var folder=Path.Combine(directory,"views",theme);Directory.CreateDirectory(folder);
            await Apply(original with { Theme=theme,Compact=false,ShowRemaining=false,HiddenAccounts=[] });
            viewModel.NavigateCommand.Execute("overview");await viewModel.QueryAsync();await Idle();Shot(dashboard,folder,"overview");
            viewModel.NavigateCommand.Execute("subscriptions");await Idle();Shot(dashboard,folder,"subscriptions");
            viewModel.NavigateCommand.Execute("projects");await viewModel.QueryAsync();await Idle();Shot(dashboard,folder,"projects");
            viewModel.OpenGroupCommand.Execute(viewModel.Groups.First());await viewModel.QueryAsync();
            viewModel.OpenGroupCommand.Execute(viewModel.Groups.First());await viewModel.QueryAsync();await Idle();Shot(dashboard,folder,"models-session");
            viewModel.NavigateCommand.Execute("openrouter");await viewModel.OpenRouter.LoadAsync();await Idle();Shot(dashboard,folder,"openrouter");
            viewModel.NavigateCommand.Execute("overview");await viewModel.QueryAsync();await Idle();
            Shot(widget,folder,"widget");Shot(panel,folder,"tray");
            await Apply(settings with { Compact=true });Shot(widget,folder,"widget-compact");
            await Apply(settings with { Compact=false,ShowRemaining=true,HiddenAccounts=["demo-claude"] });Shot(widget,folder,"widget-remaining");
            await Apply(settings with { ShowRemaining=false,HiddenAccounts=[] });
            if(staleModel is not null) { staleModel.ApplySettings(staleModel.Settings with { Theme=theme });var stale=new TrayPanel(staleModel) { Left=-10000,Top=-10000,AllowClose=true };stale.Show();await Idle();Shot(stale,folder,"tray-stale");stale.Close(); }
            var emptyDatabase=new UsageDatabase(Path.Combine(settingsStore.DirectoryPath,"empty-smoke.db"));await emptyDatabase.InitializeAsync();
            var emptyModel=new DashboardViewModel(emptyDatabase,quotas,new AppSettings { Language=settings.Language,Theme=theme },()=>Task.CompletedTask,true);await emptyModel.QueryAsync();emptyModel.UpdateAccounts();
            var empty=new DashboardWindow(emptyModel) { Left=-10000,Top=-10000,WindowStartupLocation=WindowStartupLocation.Manual,AllowClose=true };empty.Show();await Idle();Shot(empty,folder,"first-run");empty.Close();
            var preferences=new SettingsWindow(settings,new WindowsSecretStore(Path.Combine(settingsStore.DirectoryPath,"secrets"))) { Left=-10000,Top=-10000,WindowStartupLocation=WindowStartupLocation.Manual };
            preferences.Show();
            string[] pages=["appearance","accounts","sources","prices","notifications"];
            for(var i=0;i<pages.Length;i++)
            {
                preferences.SelectPage(i);await Idle();
                if(i==3) { Find<ListBox>(preferences,"PriceList").SelectedIndex=0;await Idle(); }
                Shot(preferences,folder,"settings-"+pages[i]);
            }
            preferences.Close();
        }
        await Apply(original);
    }
    private sealed class ManualClock(DateTimeOffset now):TimeProvider { public DateTimeOffset Now { get; set; }=now;public override DateTimeOffset GetUtcNow()=>Now; }
    private sealed class FailingAfterFirst:IQuotaProvider
    {
        private int calls;
        public ProviderKind Kind=>ProviderKind.Codex;
        public ProviderCapabilities Capabilities=>new(true,true,false,false,false);
        public Task<ProviderResult> FetchAsync(AccountProfile account,CancellationToken cancellationToken)
        {
            if(calls++>0) return Task.FromResult(new ProviderResult(ConnectionState.Unavailable,MessageCode:"timeout"));
            var now=DateTimeOffset.UtcNow;
            return Task.FromResult(new ProviderResult(ConnectionState.Ready,new(account.Id,ProviderKind.Codex,now.AddMinutes(-80),
                [new("session","five_hour",21,now.AddHours(1).AddMinutes(59),WindowMinutes:300),new("week","seven_day",67,now.AddDays(3).AddHours(21),WindowMinutes:10080)],[])));
        }
    }
    private static T Find<T>(DependencyObject root,string name) where T:FrameworkElement=>Descendants(root).OfType<T>().First(e=>e.Name==name);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
}
