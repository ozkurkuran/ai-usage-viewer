using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using AiUsageViewer.Core;
using AiUsageViewer.Infrastructure.Analytics;
using System.Globalization;

namespace AiUsageViewer.App;

public sealed class SettingsWindow : Window
{
    private readonly AppSettings original;
    private readonly ISecretStore secrets;
    private readonly Localization l;
    private readonly ObservableCollection<AccountProfile> accounts;
    private readonly ObservableCollection<SourceLocation> sources;
    private readonly Dictionary<string,string> pendingSecrets=[];
    private readonly ObservableCollection<ModelPrice> priceOverrides;
    private readonly ComboBox language=new(),theme=new();
    private readonly CheckBox topmost=new(),locked=new(),remaining=new(),compact=new(),cost=new(),startup=new(),showWidget=new(),notifications=new(),notifyResets=new();
    private readonly TextBox quotaThreshold=new(),balanceThreshold=new();
    private readonly HashSet<string> hidden;
    private readonly Func<AccountProfile,string?,CancellationToken,Task<string>>? testConnection;
    private readonly CancellationTokenSource lifetime=new();
    private TabControl tabs=null!;
    private Func<bool>? applyAccountDraft,applyPriceDraft;
    internal int PageCount=>tabs.Items.Count;
    internal void SelectPage(int index)=>tabs.SelectedIndex=index;
    private readonly Slider opacity=new() { Minimum=0.35,Maximum=1,TickFrequency=0.05,IsSnapToTickEnabled=true };
    public AppSettings? Result { get; private set; }

    public SettingsWindow(AppSettings settings,ISecretStore secrets,Func<AccountProfile,string?,CancellationToken,Task<string>>? testConnection=null)
    {
        original=settings;this.secrets=secrets;l=new(settings.Language);
        this.testConnection=testConnection;hidden=new(settings.HiddenAccounts);
        accounts=new(settings.Accounts.OrderBy(a=>settings.AccountOrder.IndexOf(a.Id) is var n&&n>=0?n:int.MaxValue));sources=new(settings.Sources);
        priceOverrides=new(settings.PriceOverrides);
        Title=l["settings"]+" · "+Localization.AppName;Width=800;Height=690;MinWidth=650;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Style=(Style)FindResource(typeof(Window));
        var root=new DockPanel { Margin=new Thickness(24) };Content=root;
        var footer=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0) };
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var cancel=Button(l["cancel"],()=>DialogResult=false);cancel.Margin=new Thickness(0,0,10,0);footer.Children.Add(cancel);
        var save=Button(l["save"],Save);save.Name="SaveSettings";footer.Children.Add(save);
        tabs=new TabControl { Background=(System.Windows.Media.Brush)FindResource("BackgroundBrush"),BorderThickness=new Thickness(0) };root.Children.Add(tabs);
        tabs.Items.Add(Tab(l["appearance"],Appearance()));tabs.Items.Add(Tab(l["accounts"],Accounts()));tabs.Items.Add(Tab(l["sources"],Sources()));
        tabs.Items.Add(Tab(l["prices"],Prices()));
        tabs.Items.Add(Tab(l["notifications"],Notifications()));
        Closed+=(_,_)=> { lifetime.Cancel();lifetime.Dispose();pendingSecrets.Clear(); };
    }
    private TabItem Tab(string title,UIElement body)=>new() { Header=title,Content=new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(10,20,10,0) } };
    private static Button Button(string label,Action action) { var button=new Button { Content=label };button.Click+=(_,_)=>action();return button; }
    private static void Label(Panel panel,string text) => panel.Children.Add(new TextBlock { Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,6) });
    private UIElement Appearance()
    {
        var stack=new StackPanel();
        Label(stack,l["language"]);
        language.Name="LanguageChoice";language.ItemsSource=new[]{new Choice(AppLanguages.System,l["systemLanguage"])}
            .Concat(AppLanguages.Supported.Select(x=>new Choice(x.Code,x.NativeName))).ToArray();
        language.SelectedValuePath=nameof(Choice.Key);language.SelectedValue=AppLanguages.NormalizeSetting(original.Language);
        stack.Children.Add(language);
        Label(stack,l["theme"]);theme.ItemsSource=new[]{new Choice("dark",l["dark"]),new Choice("light",l["light"])};theme.SelectedIndex=original.Theme=="light"?1:0;stack.Children.Add(theme);
        topmost.Content=l["alwaysOnTop"];topmost.IsChecked=original.AlwaysOnTop;stack.Children.Add(topmost);
        locked.Content=l["lockPosition"];locked.IsChecked=original.LockPosition;stack.Children.Add(locked);
        remaining.Content=l["remainingMode"];remaining.IsChecked=original.ShowRemaining;stack.Children.Add(remaining);
        compact.Content=l["compact"];compact.IsChecked=original.Compact;stack.Children.Add(compact);
        cost.Content=l["showCost"];cost.IsChecked=original.ShowEstimatedCost;stack.Children.Add(cost);
        startup.Content=l["startup"];startup.IsChecked=original.StartWithWindows;stack.Children.Add(startup);
        showWidget.Content=l["showWidgetOnLaunch"];showWidget.IsChecked=original.ShowWidgetOnLaunch;stack.Children.Add(showWidget);
        Label(stack,l["opacity"]);opacity.Value=original.Opacity;stack.Children.Add(opacity);
        return stack;
    }
    private UIElement Accounts()
    {
        var stack=new StackPanel();
        var list=new ListBox { Name="AccountList",ItemsSource=accounts,DisplayMemberPath=nameof(AccountProfile.Label),Height=145 };stack.Children.Add(list);
        var provider=new ComboBox { ItemsSource=Enum.GetValues<ProviderKind>(),SelectedIndex=0 };
        var name=new TextBox { Name="AccountName" };var profile=new TextBox();var key=new PasswordBox();
        var enabled=new CheckBox { Content=l["enabled"],IsChecked=true };var inWidget=new CheckBox { Name="AccountWidgetToggle",Content=l["showInWidget"],IsChecked=true };
        Label(stack,"Provider");stack.Children.Add(provider);Label(stack,l["name"]);stack.Children.Add(name);
        Label(stack,l["profile"]+" (Claude / Codex)");stack.Children.Add(profile);
        var browse=Button("…",()=> { var dialog=new Microsoft.Win32.OpenFolderDialog();if(dialog.ShowDialog(this)==true) profile.Text=dialog.FolderName; });
        browse.HorizontalAlignment=HorizontalAlignment.Right;browse.Margin=new Thickness(0,5,0,0);stack.Children.Add(browse);
        Label(stack,l["apiKey"]+" (OpenRouter)");stack.Children.Add(key);
        stack.Children.Add(enabled);stack.Children.Add(inWidget);
        var hint=new TextBlock { Text=l["connectionHelp"],TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0) };stack.Children.Add(hint);
        list.SelectionChanged+=(_,_)=> { if(list.SelectedItem is AccountProfile selected) { provider.SelectedItem=selected.Provider;name.Text=selected.Label;profile.Text=selected.ProfileDirectory??"";key.Clear();enabled.IsChecked=selected.Enabled;inWidget.IsChecked=!hidden.Contains(selected.Id); } };
        AccountProfile Draft()=>((list.SelectedItem as AccountProfile)??new AccountProfile()) with { Provider=(ProviderKind)provider.SelectedItem,Label=name.Text.Trim(),ProfileDirectory=string.IsNullOrWhiteSpace(profile.Text)?null:profile.Text.Trim(),Enabled=enabled.IsChecked==true };
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,15,0,0) };stack.Children.Add(buttons);
        void ClearForm() { list.SelectedItem=null;name.Clear();profile.Clear();key.Clear();enabled.IsChecked=true;inWidget.IsChecked=true; }
        bool CommitDraft()
        {
            if(string.IsNullOrWhiteSpace(name.Text)) { MessageBox.Show(this,l["accountNameRequired"],Title);return false; }
            var selected=list.SelectedItem as AccountProfile;
            var record=Draft();
            if(record.Provider!=ProviderKind.OpenRouter) record=record with { SecretReference=null };
            if(record.Provider==ProviderKind.OpenRouter&&key.Password.Length>0) { record=record with { SecretReference="account-"+record.Id+"-"+Guid.NewGuid().ToString("N") };pendingSecrets[record.SecretReference]=key.Password;key.Clear(); }
            if(inWidget.IsChecked==true) hidden.Remove(record.Id);else hidden.Add(record.Id);
            if(selected is not null) accounts[accounts.IndexOf(selected)]=record;else accounts.Add(record);
            ClearForm();return true;
        }
        applyAccountDraft=()=>list.SelectedItem is null&&string.IsNullOrWhiteSpace(name.Text)&&string.IsNullOrWhiteSpace(profile.Text)&&key.Password.Length==0||CommitDraft();
        buttons.Children.Add(Button(l["add"]+" / "+l["save"],()=>CommitDraft()));
        var remove=Button(l["remove"],()=> { if(list.SelectedItem is AccountProfile selected) { accounts.Remove(selected);hidden.Remove(selected.Id);ClearForm(); } });remove.Margin=new Thickness(10,0,0,0);buttons.Children.Add(remove);
        void Move(int delta) { if(list.SelectedItem is AccountProfile selected&&CommitDraft()) { var record=accounts.Single(a=>a.Id==selected.Id);var index=accounts.IndexOf(record);var next=index+delta;if(next>=0&&next<accounts.Count) accounts.Move(index,next);list.SelectedItem=record; } }
        var up=Button("↑",()=>Move(-1));up.ToolTip=l["moveUp"];buttons.Children.Add(up);
        var down=Button("↓",()=>Move(1));down.ToolTip=l["moveDown"];buttons.Children.Add(down);
        var test=new Button { Content=l["test"],Margin=new Thickness(0,12,0,0),IsEnabled=testConnection is not null };stack.Children.Add(test);
        test.Click+=async(_,_)=> {
            test.IsEnabled=false;hint.Text=l["loading"];
            try { var draft=Draft();var token=key.Password.Length>0?key.Password:draft.SecretReference is { } reference?pendingSecrets.GetValueOrDefault(reference):null;
                hint.Text=await testConnection!(draft,token,lifetime.Token); }
            catch(OperationCanceledException) { if(IsLoaded) hint.Text=l["unavailable"]; }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException) { hint.Text=l["unavailable"]; }
            finally { test.IsEnabled=true; }
        };
        return stack;
    }
    private UIElement Sources()
    {
        var stack=new StackPanel();var list=new ListBox { ItemsSource=sources,DisplayMemberPath=nameof(SourceLocation.Directory),Height=250 };stack.Children.Add(list);
        var provider=new ComboBox { ItemsSource=new[]{ProviderKind.Claude,ProviderKind.Codex},SelectedIndex=0,Margin=new Thickness(0,15,0,15) };stack.Children.Add(provider);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal };stack.Children.Add(buttons);
        buttons.Children.Add(Button(l["add"],()=> {
            var dialog=new Microsoft.Win32.OpenFolderDialog();if(dialog.ShowDialog(this)!=true) return;
            if(!sources.Any(s=>string.Equals(s.Directory,dialog.FolderName,StringComparison.OrdinalIgnoreCase))) sources.Add(new(Guid.NewGuid().ToString("N"),(ProviderKind)provider.SelectedItem,dialog.FolderName));
        }));
        var remove=Button(l["remove"],()=> { if(list.SelectedItem is SourceLocation selected) sources.Remove(selected); });remove.Margin=new Thickness(10,0,0,0);buttons.Children.Add(remove);
        return stack;
    }
    private void Save()
    {
        if(applyAccountDraft?.Invoke()==false) { tabs.SelectedIndex=1;return; }
        if(applyPriceDraft?.Invoke()==false) { tabs.SelectedIndex=3;return; }
        if(!decimal.TryParse(quotaThreshold.Text.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var threshold)||threshold is <1 or >100||
            !decimal.TryParse(balanceThreshold.Text.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var lowBalance)||lowBalance<0)
        { MessageBox.Show(this,l["invalidThreshold"],Title);return; }
        try { foreach(var pair in pendingSecrets.Where(p=>accounts.Any(a=>a.SecretReference==p.Key))) secrets.Write(pair.Key,pair.Value); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException) { MessageBox.Show(this,l["saveFailed"],Title);return; }
        Result=original with { Language=((Choice)language.SelectedItem).Key,Theme=((Choice)theme.SelectedItem).Key,
            AlwaysOnTop=topmost.IsChecked==true,LockPosition=locked.IsChecked==true,ShowRemaining=remaining.IsChecked==true,Opacity=opacity.Value,
            Compact=compact.IsChecked==true,ShowEstimatedCost=cost.IsChecked==true,StartWithWindows=startup.IsChecked==true,ShowWidgetOnLaunch=showWidget.IsChecked==true,
            NotificationsEnabled=notifications.IsChecked==true,NotifyResets=notifyResets.IsChecked==true,NotifyUsagePercent=threshold,NotifyLowBalance=lowBalance,
            HiddenAccounts=hidden.Where(id=>accounts.Any(a=>a.Id==id)).ToList(),AccountOrder=accounts.Select(a=>a.Id).ToList(),
            Accounts=accounts.ToList(),Sources=sources.ToList(),PriceOverrides=priceOverrides.ToList() };
        DialogResult=true;
    }
    private UIElement Notifications()
    {
        var stack=new StackPanel();notifications.Content=l["enableNotifications"];notifications.IsChecked=original.NotificationsEnabled;stack.Children.Add(notifications);
        notifyResets.Content=l["notifyResets"];notifyResets.IsChecked=original.NotifyResets;stack.Children.Add(notifyResets);
        Label(stack,l["quotaThreshold"]);quotaThreshold.Text=original.NotifyUsagePercent.ToString(CultureInfo.InvariantCulture);stack.Children.Add(quotaThreshold);
        Label(stack,l["balanceThreshold"]);balanceThreshold.Text=original.NotifyLowBalance.ToString(CultureInfo.InvariantCulture);stack.Children.Add(balanceThreshold);
        Label(stack,l["notificationHelp"]);return stack;
    }
    private UIElement Prices()
    {
        var stack=new StackPanel();stack.Children.Add(new TextBlock { Text=l["priceHelp"],TextWrapping=TextWrapping.Wrap });
        var catalog=new ComboBox { Name="PriceCatalog",ItemsSource=BundledPrices.Load().Entries,DisplayMemberPath="Model",Margin=new Thickness(0,12,0,0) };stack.Children.Add(catalog);
        var provenance=new TextBlock { Name="PriceSource",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0) };stack.Children.Add(provenance);
        var name=new TextBox();Label(stack,"Model ID");stack.Children.Add(name);
        var rates=new List<TextBox>();var names=new[]{"Input / 1M","Output / 1M","Cache read / 1M","Cache write 5m / 1M","Cache write 1h / 1M"};
        var grid=new System.Windows.Controls.Primitives.UniformGrid { Columns=2 };
        foreach(var label in names) { var panel=new StackPanel { Margin=new Thickness(0,0,10,0) };Label(panel,label);var box=new TextBox { Name="Rate"+rates.Count };System.Windows.Automation.AutomationProperties.SetName(box,label);rates.Add(box);panel.Children.Add(box);grid.Children.Add(panel); }
        stack.Children.Add(grid);var currency=new TextBox { Text="USD" };Label(stack,"Currency");stack.Children.Add(currency);
        var list=new ListBox { ItemsSource=priceOverrides,DisplayMemberPath="Model",Height=110,Margin=new Thickness(0,14,0,0) };stack.Children.Add(list);
        var dirty=false;name.TextChanged+=(_,_)=>dirty=true;currency.TextChanged+=(_,_)=>dirty=true;
        foreach(var rate in rates) rate.TextChanged+=(_,_)=>dirty=true;
        void Fill(ModelPrice p)
        {
            name.Text=p.Model;currency.Text=p.Currency;
            provenance.Text=$"{l["priceSource"]}: {(p.Source=="user"?l["userPrice"]:p.Source)} · {p.EffectiveFrom:yyyy-MM-dd}";
            decimal?[] values=[p.InputPerMillion,p.OutputPerMillion,p.CacheReadPerMillion,p.CacheWrite5mPerMillion,p.CacheWrite1hPerMillion];
            for(var i=0;i<values.Length;i++) rates[i].Text=values[i]?.ToString(CultureInfo.InvariantCulture)??"";
            dirty=false;
        }
        catalog.SelectionChanged+=(_,_)=> { if(catalog.SelectedItem is ModelPrice p) Fill(p); };
        list.SelectionChanged+=(_,_)=> { if(list.SelectedItem is ModelPrice p) Fill(p); };
        var feedback=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0) };stack.Children.Add(feedback);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,12,0,0) };stack.Children.Add(buttons);
        bool CommitPrice()
        {
            var values=new decimal?[5];
            for(var i=0;i<5;i++)
            {
                if(i>=2&&string.IsNullOrWhiteSpace(rates[i].Text)) continue;
                if(!decimal.TryParse(rates[i].Text.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var amount)||amount<0) { feedback.Text=l["invalidPrice"];return false; }
                values[i]=amount;
            }
            var price=new ModelPrice(name.Text.Trim(),DateTimeOffset.UtcNow,values[0]!.Value,values[1]!.Value,values[2],values[3],values[4],"user",currency.Text.Trim().ToUpperInvariant(),"user_override");
            if(!price.IsValid) { feedback.Text=l["invalidPrice"];return false; }
            foreach(var old in priceOverrides.Where(p=>p.Model==price.Model).ToList()) priceOverrides.Remove(old);
            priceOverrides.Add(price);provenance.Text=$"{l["priceSource"]}: {l["userPrice"]} · {price.EffectiveFrom:yyyy-MM-dd}";feedback.Text=l["save"]+" ✓";dirty=false;return true;
        }
        applyPriceDraft=()=>!dirty||CommitPrice();
        buttons.Children.Add(Button(l["add"]+" / "+l["save"],()=>CommitPrice()));
        var remove=Button(l["remove"],()=> { if(list.SelectedItem is ModelPrice p) { priceOverrides.Remove(p);catalog.SelectedItem=null;name.Clear();provenance.Text="";foreach(var rate in rates) rate.Clear();dirty=false; } });remove.Margin=new Thickness(10,0,0,0);buttons.Children.Add(remove);
        Label(stack,l["tariffBasis"]+" · 2026-09-27");return stack;
    }
}
