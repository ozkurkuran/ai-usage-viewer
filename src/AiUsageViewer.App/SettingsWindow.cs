using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AiUsageViewer.Core;
using AiUsageViewer.Application;
using AiUsageViewer.Infrastructure.Analytics;
using AiUsageViewer.Infrastructure.Storage;

namespace AiUsageViewer.App;

// Live data the dialog shows but does not own: account freshness, store counters, the section to open.
public sealed record SettingsContext(QuotaCoordinator Quotas,UsageDatabase Database,string? Section,Func<Task>? Rescan=null,Func<DateTimeOffset?>? LastScan=null);

// One Save for all sections; edits apply to a working copy and the footer lists the sections that differ.
public sealed class SettingsWindow : Window
{
    private static readonly string[] Sections=["appearance","accounts","sources","prices","notifications"];
    private static readonly string[] SectionIcons=["Icon.Nav.Appearance","Icon.Nav.Accounts","Icon.Nav.DataSources","Icon.Nav.Prices","Icon.Nav.Notifications"];
    private readonly AppSettings original;
    private readonly ISecretStore secrets;
    private readonly Localization l;
    private readonly UiFormat format;
    private readonly SettingsContext? context;
    private readonly Func<AccountProfile,string?,CancellationToken,Task<string>>? testConnection;
    private readonly CancellationTokenSource lifetime=new();
    private readonly ObservableCollection<AccountProfile> accounts;
    private readonly ObservableCollection<SourceLocation> sources;
    private readonly List<ModelPrice> overrides;
    private readonly IReadOnlyList<ModelPrice> catalog=BundledPrices.Load().Entries;
    private readonly HashSet<string> hidden;
    private readonly List<string> originalOrder;
    private readonly Dictionary<string,string> pendingSecrets=[];
    private readonly Dictionary<string,string> pendingReference=[];
    // Working copy of scalar settings.
    private string language,theme;
    private bool startup,showWidget,topmost,locked,compact,remaining,cost,notifications,notifyResets,notifyPace;
    private double opacity;
    private string threshold,lowBalance;
    private readonly HashSet<string> invalid=[];
    private readonly Button[] nav=new Button[5];
    private readonly UIElement?[] pages=new UIElement?[5];
    private readonly ContentControl body=new() { Focusable=false };
    private readonly TextBlock dirtyText=new() { VerticalAlignment=VerticalAlignment.Center,FontSize=13 };
    private readonly Ellipse dirtyDot=new() { Width=8,Height=8,Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center };
    private int current=-1;
    private bool discard;
    private string? selectedAccount,selectedPrice;
    private Action? refreshAccounts,refreshPrices,refreshPreview,refreshSources;
    public AppSettings? Result { get; private set; }
    internal int PageCount=>Sections.Length;
    internal void SelectPage(int index)=>ShowPage(index);
    internal IReadOnlyList<string> DirtySections=>Dirty();

    public SettingsWindow(AppSettings settings,ISecretStore secrets,Func<AccountProfile,string?,CancellationToken,Task<string>>? testConnection=null,SettingsContext? context=null)
    {
        original=settings;this.secrets=secrets;this.testConnection=testConnection;this.context=context;
        l=new(settings.Language);format=UiFormat.For(settings.Language);hidden=new(settings.HiddenAccounts);
        accounts=new(settings.Accounts.OrderBy(a=>settings.AccountOrder.IndexOf(a.Id) is var n&&n>=0?n:int.MaxValue));
        originalOrder=accounts.Select(a=>a.Id).ToList();
        sources=new(settings.Sources);overrides=[..settings.PriceOverrides];
        language=AppLanguages.NormalizeSetting(settings.Language);theme=settings.Theme;startup=settings.StartWithWindows;showWidget=settings.ShowWidgetOnLaunch;
        topmost=settings.AlwaysOnTop;locked=settings.LockPosition;compact=settings.Compact;remaining=settings.ShowRemaining;cost=settings.ShowEstimatedCost;
        opacity=Math.Clamp(settings.Opacity,0.4,1);notifications=settings.NotificationsEnabled;notifyResets=settings.NotifyResets;notifyPace=settings.NotifyPace;
        threshold=settings.NotifyUsagePercent.ToString(CultureInfo.InvariantCulture);lowBalance=settings.NotifyLowBalance.ToString("0.00",CultureInfo.InvariantCulture);
        Title=l["settings"]+" · "+Localization.AppName;
        Style=(Style)FindResource(typeof(Window));WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;
        Width=960;Height=Math.Min(760,SystemParameters.WorkArea.Height-24);WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"Bg.Surface");SetResourceReference(BorderBrushProperty,"Border.Window");BorderThickness=new Thickness(1);
        Content=BuildShell();
        var section=context?.Section??"";var index=Array.IndexOf(Sections,section.Split(':')[0]);
        ShowPage(index<0?0:index);
        if(section is "accounts:new" or "accounts:new-openrouter") AddAccount(section=="accounts:new-openrouter"?ProviderKind.OpenRouter:ProviderKind.Claude);
        PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape) { e.Handled=true;Close(); } };
        Closed+=(_,_)=> { lifetime.Cancel();lifetime.Dispose();pendingSecrets.Clear(); };
        UpdateDirty();
    }

    // ── Shell ─────────────────────────────────────────────────────────────
    private UIElement BuildShell()
    {
        var root=new Grid();root.RowDefinitions.Add(new() { Height=new GridLength(60) });root.RowDefinitions.Add(new());root.RowDefinitions.Add(new() { Height=new GridLength(64) });
        var header=new DockPanel { Background=Brushes.Transparent };
        header.MouseLeftButtonDown+=(_,e)=> { if(e.ButtonState==MouseButtonState.Pressed&&e.OriginalSource is not Button) DragMove(); };
        var close=IconButton("Icon.Close",l["closeSettings"],()=>Close(),"IconButton.Widget");close.Margin=new Thickness(0,0,14,0);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        header.Children.Add(new TextBlock { Text=l["settings"],Style=Res<Style>("Text.DialogTitle"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(24,0,0,0) });
        root.Children.Add(Bordered(header,new Thickness(0,0,0,1)));
        var middle=new Grid();Grid.SetRow(middle,1);root.Children.Add(middle);
        middle.ColumnDefinitions.Add(new() { Width=new GridLength(212) });middle.ColumnDefinitions.Add(new());
        var navPanel=new StackPanel { Margin=new Thickness(12,16,12,16) };
        for(var i=0;i<Sections.Length;i++)
        {
            var index=i;var item=new Button { Content=l[Sections[i]],Style=Res<Style>("NavItem") };
            Ui.SetIcon(item,Res<Geometry>(SectionIcons[i]));item.Click+=(_,_)=>ShowPage(index);nav[i]=item;navPanel.Children.Add(item);
        }
        var navBorder=Bordered(navPanel,new Thickness(0,0,1,0));navBorder.SetResourceReference(Border.BackgroundProperty,"Bg.Panel");middle.Children.Add(navBorder);
        var scroll=new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(28,24,28,24),Focusable=false };
        Grid.SetColumn(scroll,1);middle.Children.Add(scroll);
        var footer=new DockPanel { Margin=new Thickness(24,0,24,0) };
        var save=new Button { Content=l["save"],Name="SaveSettings",Style=Res<Style>("Button.Primary"),Height=38,MinWidth=96,Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Center };
        save.Click+=(_,_)=>Save();
        var cancel=new Button { Content=l["cancel"],Height=38,MinWidth=96,VerticalAlignment=VerticalAlignment.Center };cancel.Click+=(_,_)=> { discard=true;Close(); };
        DockPanel.SetDock(save,Dock.Right);DockPanel.SetDock(cancel,Dock.Right);footer.Children.Add(save);footer.Children.Add(cancel);
        var state=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };state.Children.Add(dirtyDot);state.Children.Add(dirtyText);footer.Children.Add(state);
        dirtyDot.SetResourceReference(Shape.FillProperty,"Warn.Bar");
        var footerBorder=Bordered(footer,new Thickness(0,1,0,0));footerBorder.SetResourceReference(Border.BackgroundProperty,"Bg.Panel");Grid.SetRow(footerBorder,2);root.Children.Add(footerBorder);
        return root;
    }
    private void ShowPage(int index)
    {
        if(index==current) return;current=index;
        pages[index]??=index switch { 0=>Appearance(),1=>Accounts(),2=>Sources(),3=>Prices(),_=>Notifications() };
        body.Content=pages[index];
        for(var i=0;i<nav.Length;i++) Ui.SetIsActive(nav[i],i==index);
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if(DialogResult!=true&&!discard&&Dirty().Count>0&&
            MessageBox.Show(this,l["discardChangesQuestion"],Localization.AppName,MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)
            e.Cancel=true;
        base.OnClosing(e);
    }

    // ── Dirty state ───────────────────────────────────────────────────────
    private static string Json(object value)=>JsonSerializer.Serialize(value);
    private List<string> Dirty()
    {
        var dirty=new List<string>();
        if(language!=AppLanguages.NormalizeSetting(original.Language)||theme!=original.Theme||startup!=original.StartWithWindows||showWidget!=original.ShowWidgetOnLaunch||
            topmost!=original.AlwaysOnTop||locked!=original.LockPosition||compact!=original.Compact||remaining!=original.ShowRemaining||cost!=original.ShowEstimatedCost||
            Math.Abs(opacity-Math.Clamp(original.Opacity,0.4,1))>0.004) dirty.Add("appearance");
        if(Json(accounts)!=Json(original.Accounts.OrderBy(a=>originalOrder.IndexOf(a.Id)))||!hidden.SetEquals(original.HiddenAccounts.Where(id=>original.Accounts.Any(a=>a.Id==id)))||
            !accounts.Select(a=>a.Id).SequenceEqual(originalOrder)||pendingSecrets.Count>0) dirty.Add("accounts");
        if(Json(sources)!=Json(original.Sources)) dirty.Add("sources");
        if(Json(overrides)!=Json(original.PriceOverrides)||invalid.Any(k=>k.StartsWith("price"))) dirty.Add("prices");
        if(notifications!=original.NotificationsEnabled||notifyResets!=original.NotifyResets||notifyPace!=original.NotifyPace||
            Decimal(threshold)!=original.NotifyUsagePercent||Decimal(lowBalance)!=original.NotifyLowBalance) dirty.Add("notifications");
        return dirty;
    }
    private void UpdateDirty()
    {
        var dirty=Dirty();
        dirtyDot.Visibility=dirty.Count>0?Visibility.Visible:Visibility.Collapsed;
        dirtyText.Text=dirty.Count==0?l["noUnsavedChanges"]:string.Format(format.Culture,l["unsavedChangesIn"],string.Join(", ",dirty.Select(s=>l[s])));
        dirtyText.SetResourceReference(TextBlock.ForegroundProperty,dirty.Count==0?"Text.Muted":"Warn.Text");
    }
    private void Changed() { UpdateDirty();refreshPreview?.Invoke(); }
    private static decimal? Decimal(string text)=>decimal.TryParse(text.Trim().Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value)?value:null;

    // ── Building blocks ───────────────────────────────────────────────────
    private T Res<T>(string key)=>(T)FindResource(key);
    private static Border Bordered(UIElement child,Thickness thickness)
    { var border=new Border { Child=child,BorderThickness=thickness };border.SetResourceReference(Border.BorderBrushProperty,"Border.Card");return border; }
    private Button IconButton(string icon,string name,Action action,string style="IconButton")
    {
        var button=new Button { Style=Res<Style>(style),ToolTip=name };Ui.SetIcon(button,Res<Geometry>(icon));AutomationProperties.SetName(button,name);
        button.Click+=(_,_)=>action();return button;
    }
    private Button TextButton(string text,Action action,string? style=null,string? icon=null)
    {
        var button=new Button { Content=text };if(style is not null) button.Style=Res<Style>(style);if(icon is not null) Ui.SetIcon(button,Res<Geometry>(icon));
        button.Click+=(_,_)=>action();return button;
    }
    private TextBlock Text(string text,string style="Text.Body",double? size=null,string? brush=null)
    {
        var block=new TextBlock { Text=text,Style=Res<Style>(style),TextWrapping=TextWrapping.Wrap };
        if(size is { } s) block.FontSize=s;if(brush is not null) block.SetResourceReference(TextBlock.ForegroundProperty,brush);return block;
    }
    private TextBlock Label(string text) { var label=Text(text,"Text.Body",14);label.FontWeight=FontWeights.Medium;label.Margin=new Thickness(0,0,0,8);return label; }
    private FrameworkElement Header(string title,string description,UIElement? action=null)
    {
        var panel=new DockPanel { Margin=new Thickness(0,0,0,22) };
        if(action is not null) { DockPanel.SetDock(action,Dock.Right);((FrameworkElement)action).VerticalAlignment=VerticalAlignment.Top;panel.Children.Add(action); }
        var text=new StackPanel();text.Children.Add(Text(title,"Text.SectionTitle"));
        var help=Text(description,"Text.Body",13,"Text.Muted");help.Margin=new Thickness(0,6,24,0);text.Children.Add(help);panel.Children.Add(text);
        return panel;
    }
    private TextBlock Overline(string text)=>new() { Text=text,Style=Res<Style>("Text.Overline") };
    private Border Card(params FrameworkElement[] rows)
    {
        var stack=new StackPanel();
        for(var i=0;i<rows.Length;i++) { if(i==0&&rows[i] is Border first) first.BorderThickness=new Thickness(0);stack.Children.Add(rows[i]); }
        return new Border { Style=Res<Style>("SettingsCard"),Child=stack };
    }
    // Label (+ optional muted description) on the left, control on the right.
    private Border Row(string label,string? description,FrameworkElement control)
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        var text=new StackPanel { VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,16,0) };
        text.Children.Add(Text(label,"Text.Body"));
        if(description is not null) { var help=Text(description,"Text.Caption",13);help.Margin=new Thickness(0,3,0,0);text.Children.Add(help); }
        grid.Children.Add(text);Grid.SetColumn(control,1);control.VerticalAlignment=VerticalAlignment.Center;grid.Children.Add(control);
        if(control is ToggleSwitch toggle) AutomationProperties.SetName(toggle,label);
        return new Border { Style=Res<Style>("SettingsRow"),Child=grid };
    }
    private ToggleSwitch Toggle(bool value,Action<bool> changed,string? name=null)
    {
        var toggle=new ToggleSwitch { IsChecked=value };if(name is not null) toggle.Name=name;
        toggle.Checked+=(_,_)=> { changed(true);Changed(); };toggle.Unchecked+=(_,_)=> { changed(false);Changed(); };return toggle;
    }
    private ListBox Segments(List<Choice> choices,string selected,Action<string> changed,string name)
    {
        var list=new ListBox { Style=Res<Style>("Segmented"),ItemsSource=choices,HorizontalAlignment=HorizontalAlignment.Right,SelectedItem=choices.FirstOrDefault(c=>c.Key==selected) };
        AutomationProperties.SetName(list,name);
        list.SelectionChanged+=(_,_)=> { if(list.SelectedItem is Choice choice) { changed(choice.Key);Changed(); } };return list;
    }
    private FrameworkElement Dashed(string text,Action action,string icon="Icon.Plus")
    {
        var grid=new Grid { Height=44,Margin=new Thickness(0,6,0,0) };
        var outline=new Rectangle { RadiusX=10,RadiusY=10,StrokeDashArray=[4,3],StrokeThickness=1 };outline.SetResourceReference(Shape.StrokeProperty,"Border.Control");grid.Children.Add(outline);
        var button=TextButton(text,action,"Button.Ghost",icon);button.Height=42;button.Margin=new Thickness(1);button.SetResourceReference(Control.ForegroundProperty,"Accent.Text");grid.Children.Add(button);
        return grid;
    }
    private TextBox Field(string text,string? name=null,string? placeholder=null,bool mono=false)
    {
        var box=new TextBox { Text=text };if(name is not null) box.Name=name;if(placeholder is not null) Ui.SetPlaceholder(box,placeholder);
        if(mono) box.FontFamily=Res<FontFamily>("Font.Mono");return box;
    }
    private static Ellipse Dot(string token,double size=8) { var dot=new Ellipse { Width=size,Height=size,VerticalAlignment=VerticalAlignment.Center };dot.SetResourceReference(Shape.FillProperty,token);return dot; }
    private Border Badge(string text,string background,string foreground)
    {
        var label=new TextBlock { Text=text,FontWeight=FontWeights.Medium };label.SetResourceReference(TextBlock.ForegroundProperty,foreground);
        var badge=new Border { Style=Res<Style>("Badge"),Margin=new Thickness(0,0,8,0),Child=label };badge.SetResourceReference(Border.BackgroundProperty,background);return badge;
    }
    private static Grid Columns(FrameworkElement left,FrameworkElement right,double leftWidth=-1,double rightWidth=-1)
    {
        var grid=new Grid();
        grid.ColumnDefinitions.Add(leftWidth<0?new ColumnDefinition():new ColumnDefinition { Width=new GridLength(leftWidth) });
        grid.ColumnDefinitions.Add(new() { Width=new GridLength(20) });
        grid.ColumnDefinitions.Add(rightWidth<0?new ColumnDefinition():new ColumnDefinition { Width=new GridLength(rightWidth) });
        grid.Children.Add(left);Grid.SetColumn(right,2);grid.Children.Add(right);return grid;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach(var item in Descendants(child)) yield return item;
    }

    // ── Appearance ────────────────────────────────────────────────────────
    private UIElement Appearance()
    {
        var page=new StackPanel();page.Children.Add(Header(l["appearance"],l["appearanceDescription"]));
        var languageChoice=new ComboBox { Name="LanguageChoice",Style=Res<Style>("Select.Input"),MinWidth=220,SelectedValuePath=nameof(Choice.Key) };
        languageChoice.ItemsSource=new[]{new Choice(AppLanguages.System,l["systemLanguage"])}.Concat(AppLanguages.Supported.Select(x=>new Choice(x.Code,x.NativeName))).ToArray();
        languageChoice.SelectedValue=language;languageChoice.SelectionChanged+=(_,_)=> { if(languageChoice.SelectedValue is string value) { language=value;Changed(); } };
        AutomationProperties.SetName(languageChoice,l["language"]);
        var left=new StackPanel();
        left.Children.Add(Overline(l["general"]));
        left.Children.Add(Card(Row(l["language"],null,languageChoice),
            Row(l["theme"],null,Segments([new("dark",l["dark"]),new("light",l["light"]),new("system",l["systemTheme"])],theme,v=>theme=v,l["theme"])),
            Row(l["startup"],null,Toggle(startup,v=>startup=v))));
        var previewTitle=Overline(l["widgetPreview"]);previewTitle.Margin=new Thickness(0,26,0,10);left.Children.Add(previewTitle);
        var preview=new Border { CornerRadius=new CornerRadius(14),Height=230,Padding=new Thickness(20),ClipToBounds=true };preview.SetResourceReference(Border.BackgroundProperty,"Bg.Backdrop");
        var caption=Text("","Text.Caption",13);caption.Margin=new Thickness(0,10,0,0);
        left.Children.Add(preview);left.Children.Add(caption);
        refreshPreview=()=> { preview.Child=WidgetPreview();caption.Text=string.Format(format.Culture,l["previewCaption"],l[compact?"compactLayoutName":"standardLayoutName"],l[remaining?"leftLower":"used"],format.Percent(opacity*100)); };
        refreshPreview();
        var slider=new Slider { Minimum=0.4,Maximum=1,Value=opacity,Width=170,SmallChange=0.01,LargeChange=0.05,IsSnapToTickEnabled=true,TickFrequency=0.01 };
        AutomationProperties.SetName(slider,l["opacity"]);
        var sliderValue=Text(format.Percent(opacity*100),"Text.Body");sliderValue.Width=52;sliderValue.TextAlignment=TextAlignment.Right;sliderValue.VerticalAlignment=VerticalAlignment.Center;
        slider.ValueChanged+=(_,_)=> { opacity=Math.Round(slider.Value,2);sliderValue.Text=format.Percent(opacity*100);Changed(); };
        var opacityControl=new StackPanel { Orientation=Orientation.Horizontal };opacityControl.Children.Add(slider);opacityControl.Children.Add(sliderValue);
        var right=new StackPanel();right.Children.Add(Overline(l["widget"]));
        right.Children.Add(Card(Row(l["showOnLaunch"],null,Toggle(showWidget,v=>showWidget=v)),Row(l["keepOnTop"],null,Toggle(topmost,v=>topmost=v)),
            Row(l["lockPositionShort"],null,Toggle(locked,v=>locked=v)),
            Row(l["layout"],null,Segments([new("standard",l["standardLayout"]),new("compact",l["compactLayout"])],compact?"compact":"standard",v=>compact=v=="compact",l["layout"])),
            Row(l["showQuotaAs"],null,Segments([new("used",l["usedOption"]),new("left",l["leftOption"])],remaining?"left":"used",v=>remaining=v=="left",l["showQuotaAs"])),
            Row(l["estimatedCost"],l["estimatedCostHelp"],Toggle(cost,v=>cost=v)),
            Row(l["opacity"],null,opacityControl)));
        page.Children.Add(Columns(left,right));return page;
    }
    // A miniature widget on a neutral desktop backdrop: layout, quota mode and opacity.
    private UIElement WidgetPreview()
    {
        var canvas=new Grid();
        var desk=new Border { CornerRadius=new CornerRadius(10),Width=220,Height=140,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Opacity=0.6 };
        desk.SetResourceReference(Border.BackgroundProperty,"Bg.Track");canvas.Children.Add(desk);
        var widget=new Border { CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),Padding=new Thickness(14,12,14,10),Width=compact?220:240,
            HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Opacity=opacity };
        widget.SetResourceReference(Border.BackgroundProperty,"Bg.Surface");widget.SetResourceReference(Border.BorderBrushProperty,"Border.Window");
        var stack=new StackPanel();widget.Child=stack;
        var head=new DockPanel { Margin=new Thickness(0,0,0,6) };var time=Text(DateTime.Now.ToString("HH:mm",format.Culture),"Text.Caption");DockPanel.SetDock(time,Dock.Right);head.Children.Add(time);
        head.Children.Add(new TextBlock { Text=Localization.AppName,FontSize=12,FontWeight=FontWeights.SemiBold });stack.Children.Add(head);
        var names=accounts.Where(a=>a.Provider!=ProviderKind.OpenRouter).Take(2).ToList();
        var samples=new[]{(name:names.ElementAtOrDefault(0)?.Label??"Claude Pro",key:"Claude",rows:new[]{(38d,60d),(54d,58d)}),
            (name:names.ElementAtOrDefault(1)?.Label??"ChatGPT Plus",key:"Codex",rows:new[]{(21d,60d),(67d,45d)})};
        foreach(var (name,key,rows) in samples)
        {
            var title=new TextBlock { Text=name,FontSize=11.5,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,4,0,4),TextTrimming=TextTrimming.CharacterEllipsis };
            title.SetResourceReference(TextBlock.ForegroundProperty,key+".Text");stack.Children.Add(title);
            var line=new Grid();if(compact) { line.ColumnDefinitions.Add(new());line.ColumnDefinitions.Add(new() { Width=new GridLength(10) });line.ColumnDefinitions.Add(new()); }
            for(var i=0;i<rows.Length;i++)
            {
                var (used,elapsed)=rows[i];var ahead=QuotaPace.Severity(used,elapsed)==QuotaSeverity.Ahead;
                var bar=new QuotaBar { Value=QuotaPace.DisplayFill(used,remaining),Marker=QuotaPace.DisplayMarker(elapsed,remaining)??double.NaN,BarHeight=4,MarkerHeight=8 };
                bar.SetResourceReference(QuotaBar.FillProperty,ahead?"Warn.Bar":key+".Bar");
                var value=Text(format.Percent(remaining?100-used:used),"Text.Caption",11,ahead?"Warn.Text":"Text.Primary");value.Width=36;value.TextAlignment=TextAlignment.Right;
                var cell=new DockPanel { Margin=new Thickness(0,0,0,5) };DockPanel.SetDock(value,Dock.Right);cell.Children.Add(value);cell.Children.Add(bar);
                if(compact) { Grid.SetColumn(cell,i*2);line.Children.Add(cell); } else stack.Children.Add(cell);
            }
            if(compact) stack.Children.Add(line);
        }
        canvas.Children.Add(widget);return canvas;
    }

    // ── Accounts ──────────────────────────────────────────────────────────
    private static string ProviderName(ProviderKind provider)=>provider switch { ProviderKind.Claude=>"Claude",ProviderKind.Codex=>"ChatGPT",_=>"OpenRouter" };
    private AccountStatus? StatusOf(AccountProfile account)=>context?.Quotas.Statuses.FirstOrDefault(s=>s.Account.SameConnection(account));
    private (string Dot,string Text) Health(AccountProfile account)
    {
        var status=StatusOf(account);
        if(status?.LastGood is null) return ("Text.Disabled",l[status?.State==ConnectionState.SignInRequired?"signIn":"notChecked"]);
        var time=status.LastGood.ObservedAt.ToLocalTime().ToString("HH:mm",format.Culture);
        return QuotaPace.IsStale(status,DateTimeOffset.UtcNow)?("Warn.Bar",string.Format(format.Culture,l["refreshFailed"],time)):("Ok.Dot",string.Format(format.Culture,l["connectedUpdated"],time));
    }
    private int IndexOf(string id)=>accounts.ToList().FindIndex(a=>a.Id==id);
    private void Update(string id,Func<AccountProfile,AccountProfile> change) { var index=IndexOf(id);if(index>=0) accounts[index]=change(accounts[index]); }
    private void AddAccount(ProviderKind provider)
    {
        var account=new AccountProfile { Provider=provider,Label=ProviderName(provider) };
        accounts.Add(account);selectedAccount=account.Id;ShowPage(1);refreshAccounts?.Invoke();Changed();
    }
    private UIElement Accounts()
    {
        var page=new StackPanel();page.Children.Add(Header(l["accounts"],l["accountsDescription"]));
        var list=new ListBox { Name="AccountList",Style=Res<Style>("MasterList") };AutomationProperties.SetName(list,l["accounts"]);
        var detail=new Border { Style=Res<Style>("Card"),Padding=new Thickness(24) };
        var left=new StackPanel();left.Children.Add(list);left.Children.Add(Dashed(l["addAccount"],()=>AddAccount(ProviderKind.Claude)));
        var updating=false;
        refreshAccounts=()=>
        {
            updating=true;var keep=selectedAccount;list.Items.Clear();
            for(var i=0;i<accounts.Count;i++) list.Items.Add(new ListBoxItem { Content=AccountItem(accounts[i],i),Tag=accounts[i].Id });
            list.SelectedItem=list.Items.Cast<ListBoxItem>().FirstOrDefault(i=>(string)i.Tag==keep)??list.Items.Cast<ListBoxItem>().FirstOrDefault();
            updating=false;
            var id=(list.SelectedItem as ListBoxItem)?.Tag as string;
            if(id!=(detail.Tag as string)) { selectedAccount=id;detail.Tag=id;detail.Child=AccountDetail(id,detail); }
        };
        list.SelectionChanged+=(_,_)=> { if(updating) return;selectedAccount=(list.SelectedItem as ListBoxItem)?.Tag as string;detail.Tag=selectedAccount;detail.Child=AccountDetail(selectedAccount,detail); };
        refreshAccounts();
        page.Children.Add(Columns(left,detail,224));return page;
        // Rows refresh in place so typing in the detail keeps focus.
    }
    private FrameworkElement AccountItem(AccountProfile account,int index)
    {
        var key=DashboardViewModel.ProviderKey(account.Provider);
        var grid=new Grid();grid.ColumnDefinitions.Add(new() { Width=GridLength.Auto });grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        var letter=new TextBlock { Text=ProviderName(account.Provider)[..1],FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
        letter.SetResourceReference(TextBlock.ForegroundProperty,key+".TintText");
        var tile=new Border { Width=36,Height=36,CornerRadius=new CornerRadius(8),Margin=new Thickness(0,0,12,0),Child=letter };tile.SetResourceReference(Border.BackgroundProperty,key+".TintBg");grid.Children.Add(tile);
        var text=new StackPanel { VerticalAlignment=VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text=account.Label,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis });
        var role=account.Provider==ProviderKind.OpenRouter?l["apiKey"]:index==0?l["defaultRole"]:DashboardViewModel.ToolName(account.Provider);
        var sub=Text(ProviderName(account.Provider)+" · "+role,"Text.Caption");sub.TextWrapping=TextWrapping.NoWrap;sub.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(sub);
        Grid.SetColumn(text,1);grid.Children.Add(text);
        var dot=Dot(account.Enabled?Health(account).Dot:"Text.Disabled");dot.Margin=new Thickness(10,0,0,0);Grid.SetColumn(dot,2);grid.Children.Add(dot);
        return grid;
    }
    private void RefreshItem(string id)
    {
        if(pages[1] is not FrameworkElement page) return;
        var list=Descendants(page).OfType<ListBox>().FirstOrDefault(b=>b.Name=="AccountList");
        var item=list?.Items.Cast<ListBoxItem>().FirstOrDefault(i=>(string)i.Tag==id);var index=IndexOf(id);
        if(item is not null&&index>=0) item.Content=AccountItem(accounts[index],index);
    }
    private UIElement AccountDetail(string? id,Border host)
    {
        var account=accounts.FirstOrDefault(a=>a.Id==id);var stack=new StackPanel();
        if(account is null) { stack.Children.Add(Text(l["noAccountsTitle"],"Text.Body",14,"Text.Muted"));return stack; }
        var key=DashboardViewModel.ProviderKey(account.Provider);var isNew=!original.Accounts.Any(a=>a.Id==account.Id);
        var head=new DockPanel { Margin=new Thickness(0,0,0,18) };
        var badge=Badge(ProviderName(account.Provider),key+".TintBg",key+".TintText");badge.Margin=new Thickness(12,0,0,0);badge.VerticalAlignment=VerticalAlignment.Top;DockPanel.SetDock(badge,Dock.Right);head.Children.Add(badge);
        var titles=new StackPanel();var title=Text(account.Label,"Text.SectionTitle");titles.Children.Add(title);
        var (dotKey,health)=Health(account);var statusLine=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,6,0,0) };
        var dot=Dot(dotKey);dot.Margin=new Thickness(0,0,8,0);statusLine.Children.Add(dot);
        statusLine.Children.Add(Text(health,"Text.Body",13,dotKey=="Ok.Dot"?"Ok.Text":dotKey=="Warn.Bar"?"Warn.Text":"Text.Muted"));titles.Children.Add(statusLine);head.Children.Add(titles);
        stack.Children.Add(head);
        if(isNew)
        {
            stack.Children.Add(Label(l["provider"]));
            var providers=Segments([new("Claude","Claude"),new("Codex","ChatGPT"),new("OpenRouter","OpenRouter")],account.Provider.ToString(),value=> {
                var provider=Enum.Parse<ProviderKind>(value);
                Update(account.Id,a=> { ClearPendingKey(a);return a with { Provider=provider,Label=a.Label==ProviderName(a.Provider)?ProviderName(provider):a.Label,ProfileDirectory=null,SecretReference=null }; });
                refreshAccounts?.Invoke();host.Child=AccountDetail(account.Id,host);
            },l["provider"]);
            providers.HorizontalAlignment=HorizontalAlignment.Left;providers.Margin=new Thickness(0,0,0,18);stack.Children.Add(providers);
        }
        stack.Children.Add(Label(l["displayName"]));
        var name=Field(account.Label,"AccountName");AutomationProperties.SetName(name,l["displayName"]);
        var nameError=Text(l["accountNameRequired"],"Text.Caption",12,"Danger.Text");nameError.Visibility=Visibility.Collapsed;nameError.Margin=new Thickness(0,6,0,0);
        name.TextChanged+=(_,_)=> {
            var blank=string.IsNullOrWhiteSpace(name.Text);
            nameError.Visibility=blank?Visibility.Visible:Visibility.Collapsed;name.Tag=blank?"invalid":null;
            if(blank) invalid.Add("account:"+account.Id);else invalid.Remove("account:"+account.Id);
            Update(account.Id,a=>a with { Label=name.Text.Trim() });title.Text=name.Text.Trim();RefreshItem(account.Id);Changed();
        };
        stack.Children.Add(name);stack.Children.Add(nameError);
        if(account.Provider==ProviderKind.OpenRouter) stack.Children.Add(ApiKeySection(account));
        else stack.Children.Add(ProfileSection(account));
        var inWidget=Overline(l["inTheWidget"]);inWidget.Margin=new Thickness(0,22,0,10);stack.Children.Add(inWidget);
        var index=IndexOf(account.Id);
        var position=Text(string.Format(format.Culture,l["positionOf"],index+1,accounts.Count),"Text.Caption",13);
        var up=IconButton("Icon.ArrowUp",l["moveUp"],()=>MoveTo(account.Id,IndexOf(account.Id)-1,host));up.IsEnabled=index>0;up.Margin=new Thickness(0,0,8,0);
        var down=IconButton("Icon.ArrowDown",l["moveDown"],()=>MoveTo(account.Id,IndexOf(account.Id)+1,host));down.IsEnabled=index<accounts.Count-1;
        var arrows=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };arrows.Children.Add(up);arrows.Children.Add(down);
        var orderLabel=new StackPanel { VerticalAlignment=VerticalAlignment.Center };orderLabel.Children.Add(Text(l["order"],"Text.Body"));orderLabel.Children.Add(position);
        var orderRow=new DockPanel();DockPanel.SetDock(arrows,Dock.Right);orderRow.Children.Add(arrows);orderRow.Children.Add(orderLabel);
        stack.Children.Add(Card(
            Row(l["enabled"],null,Toggle(account.Enabled,v=> { Update(account.Id,a=>a with { Enabled=v });RefreshItem(account.Id); })),
            Row(l["showInWidget"],null,Toggle(!hidden.Contains(account.Id),v=> { if(v) hidden.Remove(account.Id);else hidden.Add(account.Id); },"AccountWidgetToggle")),
            Row(l["defaultAccount"],l["defaultAccountHelp"],Toggle(index==0,v=> { if(v) MoveTo(account.Id,0,host);else if(IndexOf(account.Id)==0&&accounts.Count>1) MoveTo(account.Id,1,host); })),
            new Border { Style=Res<Style>("SettingsRow"),Child=orderRow }));
        var remove=TextButton(l["removeAccount"],()=> {
            if(MessageBox.Show(this,string.Format(format.Culture,l["removeAccountQuestion"],accounts[IndexOf(account.Id)].Label),Localization.AppName,MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes) return;
            ClearPendingKey(account);accounts.RemoveAt(IndexOf(account.Id));hidden.Remove(account.Id);invalid.Remove("account:"+account.Id);
            selectedAccount=null;host.Tag=null;refreshAccounts?.Invoke();Changed();
        },"Button.DangerGhost","Icon.Trash");
        remove.HorizontalAlignment=HorizontalAlignment.Left;remove.Margin=new Thickness(-8,18,0,0);stack.Children.Add(remove);
        return stack;
    }
    private void MoveTo(string id,int target,Border host)
    {
        var index=IndexOf(id);target=Math.Clamp(target,0,accounts.Count-1);if(index<0||index==target) return;
        accounts.Move(index,target);selectedAccount=id;host.Tag=null;refreshAccounts?.Invoke();Changed();
    }
    private UIElement ProfileSection(AccountProfile account)
    {
        var panel=new StackPanel { Margin=new Thickness(0,18,0,0) };
        var codex=account.Provider==ProviderKind.Codex;var label=l[codex?"profileFolderCodex":"profileFolderClaude"];
        panel.Children.Add(Label(label));
        var folder=Field(account.ProfileDirectory??"","ProfileFolder",string.Format(format.Culture,l["profileDefault"],codex?@"%USERPROFILE%\.codex":@"%USERPROFILE%\.claude"),true);
        AutomationProperties.SetName(folder,label);
        folder.TextChanged+=(_,_)=> { Update(account.Id,a=>a with { ProfileDirectory=string.IsNullOrWhiteSpace(folder.Text)?null:folder.Text.Trim() });Changed(); };
        var browse=TextButton(l["browse"],()=> { var dialog=new Microsoft.Win32.OpenFolderDialog();if(dialog.ShowDialog(this)==true) folder.Text=dialog.FolderName; });
        browse.Height=38;browse.Margin=new Thickness(10,0,0,0);
        var row=new DockPanel();DockPanel.SetDock(browse,Dock.Right);row.Children.Add(browse);row.Children.Add(folder);panel.Children.Add(row);
        var help=Text(l[codex?"profileHelpCodex":"profileHelpClaude"],"Text.Caption",13);help.Margin=new Thickness(0,8,0,0);panel.Children.Add(help);
        return panel;
    }
    private void ClearPendingKey(AccountProfile account)
    {
        if(pendingReference.Remove(account.Id,out var reference)) pendingSecrets.Remove(reference);
    }
    // OpenRouter only: masked key with Show and Test. An empty field keeps the saved key.
    private UIElement ApiKeySection(AccountProfile account)
    {
        var panel=new StackPanel { Margin=new Thickness(0,18,0,0) };panel.Children.Add(Label(l["apiKey"]));
        var masked=new PasswordBox { Name="ApiKey" };AutomationProperties.SetName(masked,l["apiKey"]);
        var plain=Field("",null,null,true);plain.Visibility=Visibility.Collapsed;AutomationProperties.SetName(plain,l["apiKey"]);
        if(pendingReference.TryGetValue(account.Id,out var existing)&&pendingSecrets.TryGetValue(existing,out var typed)) { masked.Password=typed;plain.Text=typed; }
        void Apply(string key)
        {
            if(key.Length==0) { Update(account.Id,a=> { ClearPendingKey(a);return a with { SecretReference=original.Accounts.FirstOrDefault(o=>o.Id==a.Id)?.SecretReference }; });Changed();return; }
            if(!pendingReference.TryGetValue(account.Id,out var pending)) pendingReference[account.Id]=pending="account-"+account.Id+"-"+Guid.NewGuid().ToString("N");
            pendingSecrets[pending]=key;Update(account.Id,a=>a with { SecretReference=pending });Changed();
        }
        masked.PasswordChanged+=(_,_)=> { if(masked.Visibility==Visibility.Visible) { plain.Text=masked.Password;Apply(masked.Password); } };
        plain.TextChanged+=(_,_)=> { if(plain.Visibility==Visibility.Visible) { masked.Password=plain.Text;Apply(plain.Text); } };
        var result=Text(account.SecretReference is not null&&!pendingReference.ContainsKey(account.Id)?l["apiKeyHelp"]:"","Text.Caption",13);result.Margin=new Thickness(0,8,0,0);
        var show=TextButton(l["show"],()=>{});show.Height=38;show.Margin=new Thickness(10,0,0,0);
        show.Click+=(_,_)=> { var reveal=masked.Visibility==Visibility.Visible;masked.Visibility=reveal?Visibility.Collapsed:Visibility.Visible;plain.Visibility=reveal?Visibility.Visible:Visibility.Collapsed;show.Content=l[reveal?"hideKey":"show"]; };
        var test=TextButton(l["test"],()=>{});test.Height=38;test.Margin=new Thickness(10,0,0,0);test.IsEnabled=testConnection is not null;
        test.Click+=async(_,_)=> {
            test.IsEnabled=false;result.Text=l["loading"];
            try { var key=masked.Password.Length>0?masked.Password:null;result.Text=await testConnection!(accounts[IndexOf(account.Id)],key,lifetime.Token); }
            catch(OperationCanceledException) { if(IsLoaded) result.Text=l["unavailable"]; }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException) { result.Text=l["unavailable"]; }
            finally { test.IsEnabled=true; }
        };
        var row=new DockPanel();DockPanel.SetDock(test,Dock.Right);DockPanel.SetDock(show,Dock.Right);row.Children.Add(test);row.Children.Add(show);
        var fields=new Grid();fields.Children.Add(masked);fields.Children.Add(plain);row.Children.Add(fields);
        panel.Children.Add(row);panel.Children.Add(result);return panel;
    }

    // ── Data sources ──────────────────────────────────────────────────────
    private static string Display(string path)
    {
        var home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length>0&&path.StartsWith(home,StringComparison.OrdinalIgnoreCase)?"%USERPROFILE%"+path[home.Length..]:path;
    }
    private UIElement Sources()
    {
        var page=new StackPanel();
        var rescan=TextButton(l["rescanNow"],async()=> { if(context?.Rescan is { } scan) { await scan();refreshSources?.Invoke(); } },null,"Icon.Refresh");rescan.IsEnabled=context?.Rescan is not null;
        page.Children.Add(Header(l["sources"],l["sourcesDescription"],rescan));
        var listCard=new Border { Style=Res<Style>("SettingsCard") };var rows=new StackPanel();listCard.Child=rows;page.Children.Add(listCard);
        var provider=new ComboBox { Style=Res<Style>("Select.Input"),ItemsSource=new[]{new Choice("Claude","Claude Code"),new Choice("Codex","Codex")},SelectedIndex=0,MinWidth=180,Margin=new Thickness(12,0,10,0) };
        AutomationProperties.SetName(provider,l["addFolderFor"]);
        var add=TextButton(l["chooseFolderEllipsis"],()=> {
            var dialog=new Microsoft.Win32.OpenFolderDialog();if(dialog.ShowDialog(this)!=true) return;
            if(!sources.Any(s=>string.Equals(s.Directory,dialog.FolderName,StringComparison.OrdinalIgnoreCase)))
                sources.Add(new(Guid.NewGuid().ToString("N"),Enum.Parse<ProviderKind>(((Choice)provider.SelectedItem).Key),dialog.FolderName));
            refreshSources?.Invoke();Changed();
        },null,"Icon.Plus");add.Height=38;
        var addRow=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,18,0,0) };
        var addLabel=Text(l["addFolderFor"],"Text.Body",14,"Text.Muted");addLabel.VerticalAlignment=VerticalAlignment.Center;addRow.Children.Add(addLabel);addRow.Children.Add(provider);addRow.Children.Add(add);
        page.Children.Add(addRow);
        var stats=new Grid { Margin=new Thickness(0,24,0,0) };stats.ColumnDefinitions.Add(new());stats.ColumnDefinitions.Add(new() { Width=new GridLength(20) });stats.ColumnDefinitions.Add(new());
        page.Children.Add(stats);
        IReadOnlyDictionary<string,FileCursor> cursors=new Dictionary<string,FileCursor>();
        refreshSources=()=>
        {
            rows.Children.Clear();var scanned=context?.LastScan?.Invoke();
            if(sources.Count==0) rows.Children.Add(new Border { Padding=new Thickness(20,18,20,18),Child=Text(l["noFolders"],"Text.Body",14,"Text.Muted") });
            for(var i=0;i<sources.Count;i++)
            {
                var source=sources[i];var exists=Directory.Exists(source.Directory);var key=DashboardViewModel.ProviderKey(source.Provider);
                var prefix=source.Directory.TrimEnd('\\')+"\\";var files=cursors.Keys.Count(p=>p.StartsWith(prefix,StringComparison.OrdinalIgnoreCase));
                var info=new StackPanel();
                var badges=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,8) };
                badges.Children.Add(Badge(DashboardViewModel.ToolName(source.Provider),key+".TintBg",key+".TintText"));
                badges.Children.Add(Badge(source.Optional?l["defaultBadge"]:l["addedByYou"],"Bg.Control","Text.Secondary"));info.Children.Add(badges);
                info.Children.Add(new TextBlock { Text=Display(source.Directory),FontFamily=Res<FontFamily>("Font.Mono"),FontSize=14,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=source.Directory });
                var status=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0) };var dot=Dot(exists?"Ok.Dot":"Warn.Bar",7);dot.Margin=new Thickness(0,0,8,0);status.Children.Add(dot);
                var line=exists?scanned is { } at?string.Format(format.Culture,l["logFilesScanned"],format.Number(files),at.ToLocalTime().ToString("HH:mm",format.Culture)):
                    string.Format(format.Culture,l["logFiles"],format.Number(files)):l["folderNotFoundRecords"];
                status.Children.Add(Text(line,"Text.Body",13,exists?"Text.Secondary":"Warn.Text"));info.Children.Add(status);
                var actions=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };
                if(!source.Optional)
                {
                    var index=i;
                    var change=TextButton(l["change"],()=> { var dialog=new Microsoft.Win32.OpenFolderDialog();if(dialog.ShowDialog(this)!=true) return;sources[index]=sources[index] with { Directory=dialog.FolderName };refreshSources?.Invoke();Changed(); });
                    change.Margin=new Thickness(0,0,8,0);actions.Children.Add(change);
                    var remove=IconButton("Icon.Trash",l["removeFolder"],()=> { sources.Remove(source);refreshSources?.Invoke();Changed(); });remove.SetResourceReference(Control.ForegroundProperty,"Danger.Text");remove.Margin=new Thickness(0,0,8,0);actions.Children.Add(remove);
                }
                var open=IconButton("Icon.ExternalLink",l["openFolder"],()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",'"'+source.Directory+'"') { UseShellExecute=true })?.Dispose());
                open.IsEnabled=exists;actions.Children.Add(open);
                var content=new DockPanel();DockPanel.SetDock(actions,Dock.Right);content.Children.Add(actions);content.Children.Add(info);
                rows.Children.Add(new Border { Style=Res<Style>("SettingsRow"),Padding=new Thickness(20,16,16,16),BorderThickness=new Thickness(0,i==0?0:1,0,0),Child=content });
            }
        };
        refreshSources();
        page.Loaded+=async(_,_)=> {
            if(context is null) return;
            try
            {
                cursors=await context.Database.ReadCursorsAsync(lifetime.Token);var summary=await context.Database.SummarizeAsync(new(),lifetime.Token);
                stats.Children.Clear();
                var records=StatCard(l["storedOnDevice"],string.Format(format.Culture,l["recordsCount"],format.Number(summary.Requests)));
                var tokens=StatCard(l["tokensInRecords"],format.Number(summary.Tokens.Total));Grid.SetColumn(tokens,2);
                stats.Children.Add(records);stats.Children.Add(tokens);refreshSources();
            }
            catch(Exception ex) when(ex is IOException or OperationCanceledException or ObjectDisposedException or Microsoft.Data.Sqlite.SqliteException) { }
        };
        return page;
    }
    private Border StatCard(string label,string value)
    {
        var stack=new StackPanel();stack.Children.Add(Text(label,"Text.Body",13,"Text.Muted"));var number=Text(value,"Text.Kpi");number.Margin=new Thickness(0,8,0,0);stack.Children.Add(number);
        return new Border { Style=Res<Style>("SettingsCard"),Padding=new Thickness(22,18,22,18),Child=stack };
    }

    // ── Model prices ──────────────────────────────────────────────────────
    private ModelPrice? Bundled(string model)=>catalog.Where(p=>p.Model==model).OrderByDescending(p=>p.EffectiveFrom).FirstOrDefault();
    private ModelPrice? Effective(string model)=>overrides.Where(p=>p.Model==model).OrderByDescending(p=>p.EffectiveFrom).FirstOrDefault()??Bundled(model);
    private static string DotFor(string model)=>model.StartsWith("claude",StringComparison.OrdinalIgnoreCase)?"Claude.Bar":
        model.StartsWith("gpt",StringComparison.OrdinalIgnoreCase)||model.StartsWith("codex",StringComparison.OrdinalIgnoreCase)?"Codex.Bar":"OpenRouter.Bar";
    private UIElement Prices()
    {
        var page=new StackPanel();page.Children.Add(Header(l["prices"],l["pricesDescription"]));
        var search=Field("",null,l["searchModels"]);Ui.SetIcon(search,Res<Geometry>("Icon.Search"));AutomationProperties.SetName(search,l["searchModels"]);
        var list=new ListBox { Name="PriceList",Style=Res<Style>("MasterList"),Margin=new Thickness(0,10,0,0) };AutomationProperties.SetName(list,l["prices"]);
        var detail=new Border { Style=Res<Style>("Card"),Padding=new Thickness(24) };
        var left=new StackPanel();left.Children.Add(search);left.Children.Add(list);
        left.Children.Add(Dashed(l["addModel"],()=> { selectedPrice="";list.SelectedItem=null;detail.Child=PriceDetail("",detail); }));
        var updating=false;
        refreshPrices=()=>
        {
            updating=true;var keep=selectedPrice;list.Items.Clear();
            var models=catalog.Select(p=>p.Model).Concat(overrides.Select(p=>p.Model)).Distinct().Where(m=>m.Contains(search.Text.Trim(),StringComparison.OrdinalIgnoreCase)).OrderBy(m=>m).ToList();
            foreach(var model in models)
            {
                var row=new DockPanel();var dot=Dot(DotFor(model),7);DockPanel.SetDock(dot,Dock.Right);row.Children.Add(dot);
                row.Children.Add(new TextBlock { Text=model,FontFamily=Res<FontFamily>("Font.Mono"),FontSize=13.5,TextTrimming=TextTrimming.CharacterEllipsis,FontWeight=overrides.Any(o=>o.Model==model)?FontWeights.SemiBold:FontWeights.Normal });
                list.Items.Add(new ListBoxItem { Content=row,Tag=model,ToolTip=model });
            }
            list.SelectedItem=list.Items.Cast<ListBoxItem>().FirstOrDefault(i=>(string)i.Tag==keep);updating=false;
        };
        search.TextChanged+=(_,_)=>refreshPrices();
        list.SelectionChanged+=(_,_)=> { if(updating||list.SelectedItem is not ListBoxItem item) return;selectedPrice=(string)item.Tag;detail.Child=PriceDetail(selectedPrice,detail); };
        refreshPrices();detail.Child=Text(l["selectModel"],"Text.Body",14,"Text.Muted");
        page.Children.Add(Columns(left,detail,224));return page;
    }
    private UIElement PriceDetail(string model,Border host)
    {
        var stack=new StackPanel();var price=model.Length>0?Effective(model):null;var bundled=model.Length>0?Bundled(model):null;var userPrice=overrides.Any(p=>p.Model==model);
        var head=new DockPanel { Margin=new Thickness(0,0,0,16) };
        var reset=TextButton(l["resetToSource"],()=> { overrides.RemoveAll(p=>p.Model==model);refreshPrices?.Invoke();host.Child=PriceDetail(model,host);Changed(); });
        reset.IsEnabled=bundled is not null&&userPrice;reset.VerticalAlignment=VerticalAlignment.Top;DockPanel.SetDock(reset,Dock.Right);head.Children.Add(reset);
        var titles=new StackPanel();TextBox? idBox=null;
        var idError=Text(l["modelIdRequired"],"Text.Caption",12,"Danger.Text");idError.Visibility=Visibility.Collapsed;idError.Margin=new Thickness(0,6,0,0);
        if(model.Length>0) titles.Children.Add(new TextBlock { Text=model,FontFamily=Res<FontFamily>("Font.Mono"),FontSize=19,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis });
        else { idBox=Field("","ModelId",l["modelIdPlaceholder"],true);AutomationProperties.SetName(idBox,l["modelColumn"]);titles.Children.Add(idBox);titles.Children.Add(idError); }
        var exact=Text(l["exactMatch"],"Text.Caption",13);exact.Margin=new Thickness(0,6,0,0);titles.Children.Add(exact);head.Children.Add(titles);stack.Children.Add(head);
        var sourceUrl=bundled?.Source is { } url&&url.StartsWith("https://",StringComparison.OrdinalIgnoreCase)?url:null;
        var sourceBox=new Border { CornerRadius=new CornerRadius(10),Padding=new Thickness(14,8,14,8),Margin=new Thickness(0,0,0,18) };sourceBox.SetResourceReference(Border.BackgroundProperty,"Bg.CardInset");
        var source=new WrapPanel();
        var linkIcon=new Icon { Data=Res<Geometry>("Icon.ExternalLink"),Size=16,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center };linkIcon.SetResourceReference(Control.ForegroundProperty,"Text.Muted");source.Children.Add(linkIcon);
        var sourceLabel=Text(l["sourceLabel"],"Text.Body",13,"Text.Muted");sourceLabel.Margin=new Thickness(0,0,8,0);sourceLabel.VerticalAlignment=VerticalAlignment.Center;source.Children.Add(sourceLabel);
        var link=new Button { Name="PriceSource",Style=Res<Style>("Button.Link"),Height=28,Padding=new Thickness(2,0,2,0),Tag=sourceUrl,ToolTip=sourceUrl,
            Content=userPrice||sourceUrl is null?l["userOverrideSource"]:string.Format(format.Culture,l["pricingLink"],new Uri(sourceUrl).Host) };
        link.IsEnabled=sourceUrl is not null;link.Click+=(_,_)=> { if(sourceUrl is not null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sourceUrl) { UseShellExecute=true })?.Dispose(); };
        AutomationProperties.SetName(link,sourceUrl??l["userOverrideSource"]);source.Children.Add(link);
        if(price is not null) { var date=Text(string.Format(format.Culture,l["checkedOn"],price.EffectiveFrom.ToString("MMM d, yyyy",format.Culture)),"Text.Body",13,"Text.Muted");date.Margin=new Thickness(6,0,0,0);date.VerticalAlignment=VerticalAlignment.Center;source.Children.Add(date); }
        sourceBox.Child=source;stack.Children.Add(sourceBox);
        string[] labels=[l["input"],l["output"],l["cacheRead"],l["cacheWrite5"],l["cacheWrite1h"]];
        decimal?[] values=price is null?new decimal?[5]:[price.InputPerMillion,price.OutputPerMillion,price.CacheReadPerMillion,price.CacheWrite5mPerMillion,price.CacheWrite1hPerMillion];
        var currency=new ComboBox { Style=Res<Style>("Select.Input"),ItemsSource=new[]{"USD","EUR","GBP"},SelectedItem=price?.Currency??"USD" };AutomationProperties.SetName(currency,l["currency"]);
        static string Symbol(string code)=>code switch { "USD"=>"$","EUR"=>"€","GBP"=>"£",_=>code };
        var boxes=new TextBox[5];var errors=new TextBlock[5];
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new() { Width=new GridLength(20) });grid.ColumnDefinitions.Add(new());
        for(var r=0;r<3;r++) grid.RowDefinitions.Add(new() { Height=GridLength.Auto });
        for(var i=0;i<6;i++)
        {
            var cell=new StackPanel { Margin=new Thickness(0,0,0,16) };cell.Children.Add(Label(i<5?labels[i]:l["currency"]));
            if(i<5)
            {
                var box=Field(values[i]?.ToString("0.00##",format.Culture)??"","Rate"+i);Ui.SetPrefix(box,Symbol((string)currency.SelectedItem));Ui.SetSuffix(box,l["perMillion"]);
                AutomationProperties.SetName(box,labels[i]);boxes[i]=box;cell.Children.Add(box);
                var error=Text(l["invalidNumber"],"Text.Caption",12,"Danger.Text");error.Visibility=Visibility.Collapsed;error.Margin=new Thickness(0,6,0,0);errors[i]=error;cell.Children.Add(error);
            }
            else cell.Children.Add(currency);
            Grid.SetRow(cell,i/2);Grid.SetColumn(cell,i%2*2);grid.Children.Add(cell);
        }
        stack.Children.Add(grid);stack.Children.Add(Text(l["cacheHelp"],"Text.Caption",13));
        var id=model;
        void Commit()
        {
            var parsed=new decimal?[5];var ok=true;
            for(var i=0;i<5;i++)
            {
                var text=boxes[i].Text.Trim();var valid=i>=2&&text.Length==0||Decimal(text) is >= 0;
                if(valid&&text.Length>0) parsed[i]=Decimal(text);
                errors[i].Visibility=valid?Visibility.Collapsed:Visibility.Visible;boxes[i].Tag=valid?null:"invalid";ok&=valid;
            }
            var name=idBox?.Text.Trim()??id;var named=name.Length>0;idError.Visibility=named||idBox is null?Visibility.Collapsed:Visibility.Visible;
            invalid.Remove("price:"+id);
            if(!ok||!named) { invalid.Add("price:"+id);Changed();return; }
            var updated=new ModelPrice(name,DateTimeOffset.UtcNow,parsed[0]!.Value,parsed[1]!.Value,parsed[2],parsed[3],parsed[4],"user",(string)currency.SelectedItem,"user_override");
            if(!updated.IsValid) { invalid.Add("price:"+id);Changed();return; }
            if(id.Length>0&&id!=name) overrides.RemoveAll(p=>p.Model==id);
            overrides.RemoveAll(p=>p.Model==name);overrides.Add(updated);id=name;selectedPrice=name;
            reset.IsEnabled=Bundled(name) is not null;link.Content=l["userOverrideSource"];
            refreshPrices?.Invoke();Changed();
        }
        foreach(var box in boxes) box.TextChanged+=(_,_)=>Commit();
        if(idBox is not null) idBox.TextChanged+=(_,_)=>Commit();
        currency.SelectionChanged+=(_,_)=> { foreach(var box in boxes) Ui.SetPrefix(box,Symbol((string)currency.SelectedItem));Commit(); };
        return stack;
    }

    // ── Notifications ─────────────────────────────────────────────────────
    private UIElement Notifications()
    {
        var page=new StackPanel();page.Children.Add(Header(l["notifications"],l["notificationsDescription"]));
        var dependent=new StackPanel();
        void Dim() { dependent.IsEnabled=notifications;dependent.Opacity=notifications?1:0.45; }
        var left=new StackPanel();
        left.Children.Add(Card(Row(l["windowsNotifications"],l["focusStillApplies"],Toggle(notifications,v=> { notifications=v;Dim(); },"NotificationsToggle"))));
        var quotas=Overline(l["quotasSection"]);quotas.Margin=new Thickness(0,24,0,10);dependent.Children.Add(quotas);
        var usage=Field(threshold,"UsageThreshold");usage.Width=150;usage.TextAlignment=TextAlignment.Right;Ui.SetSuffix(usage,l["percentUsedSuffix"]);AutomationProperties.SetName(usage,l["usageAlert"]);
        var usageError=Text(l["usageAlertRange"],"Text.Caption",12,"Danger.Text");usageError.Visibility=Visibility.Collapsed;usageError.Margin=new Thickness(0,4,0,0);
        usage.TextChanged+=(_,_)=> { threshold=usage.Text;var ok=Decimal(threshold) is >= 50 and <= 100;usage.Tag=ok?null:"invalid";usageError.Visibility=ok?Visibility.Collapsed:Visibility.Visible;
            if(ok) invalid.Remove("threshold");else invalid.Add("threshold");Changed(); };
        var usageRow=Row(l["usageAlert"],l["usageAlertHelp"],usage);((StackPanel)((Grid)usageRow.Child).Children[0]).Children.Add(usageError);
        dependent.Children.Add(Card(usageRow,Row(l["paceAlert"],l["paceAlertHelp"],Toggle(notifyPace,v=>notifyPace=v,"PaceToggle")),
            Row(l["resetNotice"],l["resetNoticeHelp"],Toggle(notifyResets,v=>notifyResets=v))));
        var openRouter=Overline("OpenRouter");openRouter.Margin=new Thickness(0,24,0,10);dependent.Children.Add(openRouter);
        var balance=Field(lowBalance,"LowBalance");balance.Width=150;balance.TextAlignment=TextAlignment.Right;Ui.SetPrefix(balance,"$");AutomationProperties.SetName(balance,l["lowBalance"]);
        var balanceError=Text(l["invalidNumber"],"Text.Caption",12,"Danger.Text");balanceError.Visibility=Visibility.Collapsed;balanceError.Margin=new Thickness(0,4,0,0);
        balance.TextChanged+=(_,_)=> { lowBalance=balance.Text;var ok=Decimal(lowBalance) is >= 0;balance.Tag=ok?null:"invalid";balanceError.Visibility=ok?Visibility.Collapsed:Visibility.Visible;
            if(ok) invalid.Remove("balance");else invalid.Add("balance");Changed(); };
        var balanceRow=Row(l["lowBalance"],l["lowBalanceHelp"],balance);((StackPanel)((Grid)balanceRow.Child).Children[0]).Children.Add(balanceError);
        dependent.Children.Add(Card(balanceRow));
        var note=Text(l["oncePerPeriod"],"Text.Body",13,"Text.Muted");note.Margin=new Thickness(0,18,0,0);dependent.Children.Add(note);
        left.Children.Add(dependent);Dim();
        var right=new StackPanel();right.Children.Add(Overline(l["preview"]));
        foreach(var (title,text) in PreviewToasts()) right.Children.Add(Toast(title,text));
        page.Children.Add(Columns(left,right,-1,236));return page;
    }
    // Sample toasts rendered from the same templates the app uses for real alerts.
    private IEnumerable<(string Title,string Body)> PreviewToasts()
    {
        var now=DateTimeOffset.Now;var reset=now.AddDays(3).AddHours(21);
        var codex=accounts.FirstOrDefault(a=>a.Provider==ProviderKind.Codex)?.Label??"ChatGPT Plus";var claude=accounts.FirstOrDefault(a=>a.Provider==ProviderKind.Claude)?.Label??"Claude Pro";
        var elapsed=QuotaPace.ElapsedPercent(reset,QuotaPace.WeeklyWindow,now);var runsOut=QuotaPace.RunsOutAt(67,elapsed,reset,QuotaPace.WeeklyWindow,now)??now.AddDays(2);
        yield return NotificationText.Build(l,format,new UsageAlert(UsageAlertKind.Pace,codex,"seven_day",67,ResetsAt:reset,RunsOutAt:runsOut),now);
        yield return NotificationText.Build(l,format,new UsageAlert(UsageAlertKind.Reset,claude,"five_hour",0),now);
    }
    private Border Toast(string title,string text)
    {
        var stack=new StackPanel();
        var head=new DockPanel { Margin=new Thickness(0,0,0,10) };var when=Text(l["nowShort"],"Text.Caption");DockPanel.SetDock(when,Dock.Right);head.Children.Add(when);
        var brand=new StackPanel { Orientation=Orientation.Horizontal };brand.Children.Add(new LogoMark { Size=16 });var app=Text(Localization.AppName,"Text.Caption",13);app.Margin=new Thickness(8,0,0,0);brand.Children.Add(app);head.Children.Add(brand);
        stack.Children.Add(head);var bold=Text(title,"Text.Body",14);bold.FontWeight=FontWeights.SemiBold;stack.Children.Add(bold);
        var body=Text(text,"Text.Body",13,"Text.Secondary");body.Margin=new Thickness(0,6,0,0);stack.Children.Add(body);
        return new Border { Style=Res<Style>("SettingsCard"),Padding=new Thickness(16,14,16,14),Margin=new Thickness(0,0,0,14),Child=stack };
    }

    // ── Save ──────────────────────────────────────────────────────────────
    private void Save()
    {
        var blank=accounts.FirstOrDefault(a=>string.IsNullOrWhiteSpace(a.Label));
        if(blank is not null) { selectedAccount=blank.Id;ShowPage(1);refreshAccounts?.Invoke();return; }
        if(invalid.Any(k=>k.StartsWith("price"))) { ShowPage(3);return; }
        if(invalid.Contains("threshold")||invalid.Contains("balance")||Decimal(threshold) is not { } usagePercent||Decimal(lowBalance) is not { } balance) { ShowPage(4);return; }
        try { foreach(var pair in pendingSecrets.Where(p=>accounts.Any(a=>a.SecretReference==p.Key))) secrets.Write(pair.Key,pair.Value); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException) { MessageBox.Show(this,l["saveFailed"],Title);return; }
        Result=original with { Language=language,Theme=theme,AlwaysOnTop=topmost,LockPosition=locked,ShowRemaining=remaining,Opacity=opacity,
            Compact=compact,ShowEstimatedCost=cost,StartWithWindows=startup,ShowWidgetOnLaunch=showWidget,
            NotificationsEnabled=notifications,NotifyResets=notifyResets,NotifyPace=notifyPace,NotifyUsagePercent=usagePercent,NotifyLowBalance=balance,
            HiddenAccounts=hidden.Where(id=>accounts.Any(a=>a.Id==id)).ToList(),AccountOrder=accounts.Select(a=>a.Id).ToList(),
            Accounts=accounts.ToList(),Sources=sources.ToList(),PriceOverrides=overrides.ToList() };
        DialogResult=true;
    }
}

// Notification title and body, shared by real alerts and the Settings preview.
public static class NotificationText
{
    public static (string Title,string Body) Build(Localization l,UiFormat format,UsageAlert alert,DateTimeOffset now)
    {
        var window=alert.Metric is "seven_day" or "weekly"?l["weeklyWindow"]:alert.Metric is "five_hour" or "session"?l["sessionWindow"]:l.Has(alert.Metric)?l[alert.Metric]:alert.Metric;
        var percent=alert.Value is { } value?format.PercentUsed((double)value):"";
        var reset=alert.ResetsAt is { } at?format.Time(at,now):"";
        return alert.Kind switch
        {
            UsageAlertKind.Pace=>(string.Format(format.Culture,l["toastPaceTitle"],alert.AccountLabel),
                string.Format(format.Culture,l["toastPaceBody"],window,percent,alert.RunsOutAt is { } runs?"~"+format.Time(runs,now):"",reset)),
            UsageAlertKind.Usage=>(string.Format(format.Culture,l["toastUsageTitle"],alert.AccountLabel,window,percent),
                reset.Length>0?string.Format(format.Culture,l["toastUsageBody"],reset):""),
            UsageAlertKind.Reset=>(string.Format(format.Culture,l["toastResetTitle"],alert.AccountLabel,window),
                string.Format(format.Culture,l["toastResetBody"],window,percent.Length>0?percent:format.PercentUsed(0))),
            _=>(string.Format(format.Culture,l["toastBalanceTitle"],alert.AccountLabel),
                string.Format(format.Culture,l["toastBalanceBody"],alert.Value is { } balance?format.Money(balance,alert.Currency??"USD"):""))
        };
    }
}
