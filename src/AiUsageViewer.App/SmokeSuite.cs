using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AiUsageViewer.Core;
using AiUsageViewer.Infrastructure.Storage;

namespace AiUsageViewer.App;
public partial class App
{
    private async Task RunSmokeSuiteAsync(string directory)
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
        emptyWindow.Show();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture(emptyWindow,Path.Combine(directory,"empty-data.png"),captureScale);emptyWindow.Close();
        Check(viewModel.Accounts.Count==2&&viewModel.WidgetAccounts.Count==2,"default account surfaces");
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
            if(i==3) Find<ComboBox>(preferences,"PriceCatalog").SelectedIndex=0;
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Capture(preferences,Path.Combine(directory,$"settings-{i}.png"),captureScale);
        }
        preferences.Close();checks.Add("all settings pages render");
        var edit=new SettingsWindow(settings,new WindowsSecretStore(Path.Combine(settingsStore.DirectoryPath,"secrets"))) { Left=-10000,Top=-10000,WindowStartupLocation=WindowStartupLocation.Manual };
        _=Dispatcher.BeginInvoke(()=> {
            edit.SelectPage(1);edit.UpdateLayout();Find<ListBox>(edit,"AccountList").SelectedIndex=0;
            Find<TextBox>(edit,"AccountName").Text="Renamed synthetic account";Find<CheckBox>(edit,"AccountWidgetToggle").IsChecked=false;
            edit.SelectPage(3);edit.UpdateLayout();Find<ComboBox>(edit,"PriceCatalog").SelectedIndex=0;Find<TextBox>(edit,"Rate0").Text="7,5";
            Check(Find<TextBlock>(edit,"PriceSource").Text.Contains("https://"),"price source visible");
            Find<Button>(edit,"SaveSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Check(edit.ShowDialog()==true&&edit.Result?.Accounts[0].Label=="Renamed synthetic account"&&edit.Result.HiddenAccounts.Contains(settings.Accounts[0].Id)&&edit.Result.PriceOverrides.Single().InputPerMillion==7.5m,
            "main Save commits account, visibility and pending price edits");
        var account=new AccountProfile { Id="synthetic-openrouter",Label="OpenRouter demo",Provider=ProviderKind.OpenRouter,SecretReference="synthetic-reference" };
        var now=DateTimeOffset.UtcNow;var day=DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1);
        await activityStore.SaveAsync(new(account.Id,account.SecretReference!,ConnectionState.Ready,now,now.AddMinutes(15),now,day.AddDays(-29),day,null),
            [new(account.Id,day,"openai/gpt-4.1","demo-endpoint",25000,15000,3000,8,.75m,.12m)],false);
        var activity=new ActivityWindow(settings with { Accounts=[account] },activityStore,()=>Task.CompletedTask) { Left=-10000,Top=-10000,WindowStartupLocation=WindowStartupLocation.Manual };
        activity.Show();await activity.LoadAsync();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        Progress("history-view");
        Capture(activity,Path.Combine(directory,"openrouter-history.png"),captureScale);activity.Close();checks.Add("synthetic activity view");
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
        CreateTray();dashboard.Close();widget.Close();panel.Close();
        Check(!dashboard.IsVisible&&!widget.IsVisible&&!panel.IsVisible&&tray?.Visible==true,"closing windows keeps tray alive");
        Progress("complete");
        var stored=await database.SummarizeAsync(new());
        await File.WriteAllTextAsync(Path.Combine(directory,"smoke-report.json"),JsonSerializer.Serialize(new { passed=true,observedAt=now,checks,monitors,actualDpi=VisualTreeHelper.GetDpi(widget).PixelsPerInchX,
            runtimeDirectory=System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),storedRecords=stored.Requests,storedTokens=stored.Tokens.Total },new JsonSerializerOptions { WriteIndented=true }));
    }
    private static T Find<T>(DependencyObject root,string name) where T:FrameworkElement=>Descendants(root).OfType<T>().First(e=>e.Name==name);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
}
