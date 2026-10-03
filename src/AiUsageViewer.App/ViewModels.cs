using System.Collections.ObjectModel;
using AiUsageViewer.Core;
using AiUsageViewer.Application;
using AiUsageViewer.Application.WindowsWidgets;
using AiUsageViewer.Infrastructure.Storage;
using AiUsageViewer.Infrastructure.Analytics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUsageViewer.App;

public sealed record Choice(string Key,string Label) { public override string ToString()=>Label; }

// One row of the Models / Projects / Sessions table.
public sealed record GroupRow(string Name,string Total,string Input,string Output,string Cache,string Requests,string Cost)
{
    public string Tool { get; init; }="";
    public string DotKey { get; init; }="";
    public bool ShowDot=>DotKey.Length>0;
    public bool IsModel { get; init; }
    public double Share { get; init; }
    public string ShareText { get; init; }="";
}
public sealed record ModelShare(string Model,string Tool,string DotKey,string Tokens,double Share,string ShareText);
public sealed record Crumb(string Label,string Target,bool IsCurrent);
public sealed record FilterChip(string Key,string Value,string Kind,string RemoveName);
public sealed record SetupStep(int Number,string Title,string Note,string Status,string? Path,string Action,string ActionTarget,bool Done,bool Warning,bool Primary,bool Optional)
{
    public bool HasPath=>!string.IsNullOrEmpty(Path);
    public bool HasAction=>Action.Length>0&&!Done;
    public bool ShowDoneNote=>Done&&Action.Length>0;
}
public sealed record OpenRouterCard(string Name,string Spend,string Detail,string AccountId);

// Presentation of one quota window (section 3 of the redesign spec).
public sealed record QuotaRow(string Label,string Percent,double Value,string Color,string Reset,string Scope,bool Primary)
{
    public string Id { get; init; }="";
    public double Marker { get; init; }=double.NaN;
    public string FillKey { get; init; }="Accent.Fill";
    public string ValueKey { get; init; }="Text.Primary";
    public string ShortValue { get; init; }="";
    public string MarkerTip { get; init; }="";
    public string PaceNote { get; init; }="";
    public string PaceNoteKey { get; init; }="Text.Muted";
    public string? Forecast { get; init; }
    public bool HasForecast=>Forecast is not null;
    public string WidgetMeta { get; init; }="";
    public string WidgetMetaKey { get; init; }="Text.Muted";
    public QuotaSeverity Severity { get; init; }
    public bool IsAhead=>Severity==QuotaSeverity.Ahead;
    public int Order { get; init; }
    public string AccessibleName=>$"{Label}: {Percent}"+(PaceNote.Length>0?" · "+PaceNote:"");
}
public sealed record MoneyRow(string Id,string Label,string Value);
public sealed record AccountCard(string Name,string Provider,string Color,string Status,string Updated,
    IReadOnlyList<QuotaRow> Windows,IReadOnlyList<MoneyRow> Money,string NextReset,bool Compact,string Detail)
{
    public string AccountId { get; init; }="";
    public string Tool { get; init; }="";
    public string TintBgKey { get; init; }="";
    public string TintTextKey { get; init; }="";
    public bool IsStale { get; init; }
    public string StaleNote { get; init; }="";
    public string UpdatedTime { get; init; }="";
    public string CompactFact { get; init; }="";
    public string CompactFactKey { get; init; }="Text.Muted";
    public bool HasMoney=>Money.Count>0;
    public IReadOnlyList<QuotaRow> WidgetWindows=>Windows.Any(w=>w.Primary)?Windows.Where(w=>w.Primary).OrderBy(w=>w.Order).Take(2).ToList():Windows.Take(1).ToList();
    public IReadOnlyList<MoneyRow> WidgetMoney=>Money.Where(m=>m.Id is "balance" or "usage_daily").ToList();
    public QuotaRow? First=>WidgetWindows.ElementAtOrDefault(0);
    public QuotaRow? Second=>WidgetWindows.ElementAtOrDefault(1);
}

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly UsageDatabase database;
    private readonly QuotaCoordinator quotas;
    private readonly ActivityStore? activity;
    private readonly Func<Task> refresh;
    private AppSettings settings;
    private int queryGeneration;
    private readonly PriceCatalog prices=BundledPrices.Load();
    private string? focusedProject,focusedSession;
    private DateTimeOffset lastQuery=DateTimeOffset.Now;
    public Localization L { get; private set; }
    public UiFormat Format { get; private set; }
    public AppSettings Settings=>settings;
    public ObservableCollection<AccountCard> Accounts { get; }=[];
    public ObservableCollection<AccountCard> KeyAccounts { get; }=[];
    public ObservableCollection<AccountCard> WidgetAccounts { get; }=[];
    public ObservableCollection<OpenRouterCard> OpenRouterCards { get; }=[];
    public bool ShowCost=>settings.ShowEstimatedCost;
    public bool Compact=>settings.Compact;
    public bool ShowRemaining=>settings.ShowRemaining;
    public ObservableCollection<GroupRow> Groups { get; }=[];
    public ObservableCollection<Choice> Models { get; }=[];
    public IReadOnlyList<Choice> Ranges { get; private set; }
    public IReadOnlyList<Choice> Tools { get; private set; }
    private Choice range;
    private Choice tool;
    private Choice model;
    // WPF can transiently clear SelectedItem while localized option lists are replaced.
    public Choice Range { get=>range;set { if(value is not null&&SetProperty(ref range,value)) QueueQuery(); } }
    public Choice Tool { get=>tool;set { if(value is not null&&SetProperty(ref tool,value)) QueueQuery(); } }
    public Choice Model { get=>model;set { if(value is not null&&SetProperty(ref model,value)) QueueQuery(); } }
    [ObservableProperty] private string page="overview";
    [ObservableProperty] private string total="0";
    [ObservableProperty] private string input="0";
    [ObservableProperty] private string output="0";
    [ObservableProperty] private string cache="0";
    [ObservableProperty] private string requests="0";
    [ObservableProperty] private string status="";
    [ObservableProperty] private string coverage="";
    [ObservableProperty] private string estimatedCost="—";
    [ObservableProperty] private string priceCoverage="";
    [ObservableProperty] private string widgetTotal="0";
    [ObservableProperty] private string widgetCost="—";
    [ObservableProperty] private string widgetPriceCoverage="";
    [ObservableProperty] private string widgetStatus="";
    [ObservableProperty] private string focusLabel="";
    public bool HasFocus=>!string.IsNullOrEmpty(FocusLabel);
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool showGettingStarted;
    [ObservableProperty] private IReadOnlyList<DailyUsage> daily=[];
    // Overview
    [ObservableProperty] private double mixInput;
    [ObservableProperty] private double mixCache;
    [ObservableProperty] private double mixOutput;
    [ObservableProperty] private string inputShare="";
    [ObservableProperty] private string cacheShare="";
    [ObservableProperty] private string outputShare="";
    [ObservableProperty] private bool hasPeriodUsage;
    [ObservableProperty] private IReadOnlyList<ChartBar> historyBars=[];
    [ObservableProperty] private double historyNow=double.NaN;
    [ObservableProperty] private string historyCaption="";
    [ObservableProperty] private IReadOnlyList<HeatDay> heatDays=[];
    [ObservableProperty] private IReadOnlyList<ModelShare> topModels=[];
    [ObservableProperty] private IReadOnlyList<SetupStep> setupSteps=[];
    [ObservableProperty] private string setupProgress="";
    // Table pages
    [ObservableProperty] private IReadOnlyList<Crumb> breadcrumbs=[];
    [ObservableProperty] private IReadOnlyList<FilterChip> chips=[];
    [ObservableProperty] private string groupCount="0";
    [ObservableProperty] private string groupCountText="";
    [ObservableProperty] private string tableTotalsLabel="";
    // Status
    [ObservableProperty] private string pillKind="empty";
    [ObservableProperty] private string pillText="";
    [ObservableProperty] private string widgetTime="";
    [ObservableProperty] private string widgetDotKey="Text.Disabled";
    [ObservableProperty] private string hiddenAccountsText="";
    public bool Demo { get; }
    public bool CanTrySample=>!Demo;
    public bool IsOverview=>Page=="overview";
    public bool IsSubscriptions=>Page=="subscriptions";
    public bool IsOpenRouter=>Page=="openrouter";
    public bool IsTable=>Page is "models" or "projects" or "sessions";
    public bool HasSubscriptions=>Accounts.Count>0;
    public bool HasHiddenAccounts=>HiddenAccountsText.Length>0;
    public bool HasChips=>Chips.Count>0;
    public bool HasBreadcrumbs=>Breadcrumbs.Count>0;
    public bool ShowDemoPill=>Demo;
    public string PageTitle=>Page=="openrouter"?"OpenRouter":L[Page];
    public string PageSubtitle=>L[Page switch { "overview"=>"overviewSubtitle","subscriptions"=>"subscriptionsSubtitle","models"=>"modelsSubtitle",
        "projects"=>"projectsSubtitle","sessions"=>"sessionsSubtitle",_=>"openRouterSubtitle" }];
    public string TableNameHeader=>L[Page switch { "projects"=>"projectColumn","sessions"=>"sessionColumn",_=>"modelColumn" }];
    public string CountLabel=>L[Page switch { "projects"=>"projects","sessions"=>"sessions",_=>"models" }];
    public string TodayLabel=>L["today"];
    public string PeriodLabel=>Range.Label;
    public string PaceLegend=>L[settings.ShowRemaining?"paceLegendLeft":"paceLegendUsed"];
    // First run shows placeholders instead of zero totals or an invented cost.
    public string HeroValue=>ShowGettingStarted?"—":Total;
    public string CostValue=>ShowGettingStarted?"—":EstimatedCost;
    public string CostNote=>L[ShowGettingStarted?"estimatedOnceUsage":"standardTariffNote"];
    public OpenRouterViewModel OpenRouter { get; }
    public event Action<string?>? SettingsRequested;
    public event Action? WidgetRequested;
    public event Action? ExportRequested;
    public event Action? SampleRequested;
    public event Action? WidgetDataChanged;
    private DateOnly widgetDay;
    private DateTimeOffset widgetUpdatedAt;
    public WidgetSnapshot CreateWindowsWidgetSnapshot()=>new(WidgetSnapshot.CurrentVersion,L.Language,
        widgetDay,widgetUpdatedAt,DateTimeOffset.UtcNow,WidgetTotal,ShowCost?WidgetCost:null,
        WidgetAccounts.Select(a=>new WidgetAccount(a.Name,a.Status,
            a.WidgetWindows.Select(w=>w.Label+": "+w.Percent).Concat(a.WidgetMoney.Select(m=>m.Label+": "+m.Value)).ToArray())).ToArray(),Demo);

    public DashboardViewModel(UsageDatabase database,QuotaCoordinator quotas,AppSettings settings,Func<Task> refresh,bool demo=false,ActivityStore? activity=null)
    {
        this.database=database;this.quotas=quotas;this.settings=settings;this.refresh=refresh;this.activity=activity;Demo=demo;
        L=new(settings.Language);Format=UiFormat.For(settings.Language);
        Ranges=CreateRanges();Tools=CreateTools();range=Ranges[0];tool=Tools[0];
        model=new("",L["allShort"]);Models.Add(model);
        OpenRouter=new(activity,settings,refresh,demo);
    }
    private List<Choice> CreateRanges()=>new[]{"today","week","month","all"}.Select(k=>new Choice(k,L[k])).ToList();
    private List<Choice> CreateTools()=>[new("",L["allShort"]),new("Claude","Claude Code"),new("Codex","Codex")];
    public void ApplySettings(AppSettings value)
    {
        var rangeKey=Range.Key;var toolKey=Tool.Key;var modelKey=Model.Key;
        settings=value;AppLanguages.ApplyCulture(value.Language);L=new(value.Language);Format=UiFormat.For(value.Language);OnPropertyChanged(nameof(L));
        OnPropertyChanged(nameof(ShowCost));OnPropertyChanged(nameof(Compact));OnPropertyChanged(nameof(ShowRemaining));OnPropertyChanged(nameof(PaceLegend));
        Ranges=CreateRanges();Tools=CreateTools();OnPropertyChanged(nameof(Ranges));OnPropertyChanged(nameof(Tools));
        Range=Ranges.FirstOrDefault(x=>x.Key==rangeKey)??Ranges[0];Tool=Tools.FirstOrDefault(x=>x.Key==toolKey)??Tools[0];
        // Replacing the selected "All" item clears the selector after this call returns, so only swap it when its
        // label changes and re-announce the selections once the controls have processed the new lists.
        if(Models[0].Label!=L["allShort"]) Models[0]=new("",L["allShort"]);
        model=Models.FirstOrDefault(m=>m.Key==modelKey)??Models[0];
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(()=> { OnPropertyChanged(nameof(Range));OnPropertyChanged(nameof(Tool));OnPropertyChanged(nameof(Model)); },
            System.Windows.Threading.DispatcherPriority.Background);
        OnPropertyChanged(nameof(PageTitle));OnPropertyChanged(nameof(PageSubtitle));OpenRouter.ApplySettings(value);UpdateAccounts();QueueQuery();
    }
    partial void OnPageChanged(string value)
    {
        foreach(var name in new[]{nameof(IsOverview),nameof(IsSubscriptions),nameof(IsTable),nameof(IsOpenRouter),nameof(PageTitle),nameof(PageSubtitle),nameof(TableNameHeader),nameof(CountLabel)})
            OnPropertyChanged(name);
        if(value=="openrouter") _=OpenRouter.LoadAsync();
        QueueQuery();
    }
    partial void OnStatusChanged(string value)=>WidgetStatus=value;
    partial void OnTotalChanged(string value)=>OnPropertyChanged(nameof(HeroValue));
    partial void OnEstimatedCostChanged(string value)=>OnPropertyChanged(nameof(CostValue));
    partial void OnShowGettingStartedChanged(bool value) { OnPropertyChanged(nameof(HeroValue));OnPropertyChanged(nameof(CostValue));OnPropertyChanged(nameof(CostNote)); }
    partial void OnIsBusyChanged(bool value)=>UpdatePill();
    partial void OnChipsChanged(IReadOnlyList<FilterChip> value)=>OnPropertyChanged(nameof(HasChips));
    partial void OnBreadcrumbsChanged(IReadOnlyList<Crumb> value)=>OnPropertyChanged(nameof(HasBreadcrumbs));
    partial void OnHiddenAccountsTextChanged(string value)=>OnPropertyChanged(nameof(HasHiddenAccounts));
    private async void QueueQuery() { try { await QueryAsync(); } catch(Exception e) when(e is IOException or Microsoft.Data.Sqlite.SqliteException) { Status=L["sourceErrors"]; } }
    [RelayCommand] private void Navigate(string page) { ClearFocus();Page=page; }
    [RelayCommand] private void ClearFocus() { focusedProject=null;focusedSession=null;FocusLabel="";OnPropertyChanged(nameof(HasFocus));QueueQuery(); }
    [RelayCommand] private void RemoveChip(FilterChip? chip)
    {
        if(chip?.Kind=="project") { focusedProject=null; }
        else if(chip?.Kind=="session") { focusedSession=null; }
        FocusLabel=string.Join(" / ",new[]{focusedProject,focusedSession}.OfType<string>());OnPropertyChanged(nameof(HasFocus));QueueQuery();
    }
    [RelayCommand] private void OpenCrumb(Crumb? crumb)
    {
        if(crumb is null||crumb.IsCurrent) return;
        if(crumb.Target=="projects") { Navigate("projects");return; }
        if(crumb.Target=="project") { focusedSession=null;FocusLabel=focusedProject??"";Page="sessions";OnPropertyChanged(nameof(HasFocus)); }
        if(crumb.Target=="session") { Page="models"; }
    }
    [RelayCommand] private void OpenGroup(GroupRow? row)
    {
        if(row is null) return;
        if(Page=="projects") { focusedProject=row.Name;focusedSession=null;FocusLabel=row.Name;Page="sessions"; }
        else if(Page=="sessions") { focusedSession=row.Name;FocusLabel=(focusedProject is null?"":focusedProject+" / ")+row.Name;Page="models"; }
        else { Model=Models.FirstOrDefault(m=>m.Key==row.Name)??Models[0];Page="overview"; }
        OnPropertyChanged(nameof(HasFocus));
    }
    [RelayCommand] private void OpenSettings(string? section)=>SettingsRequested?.Invoke(section);
    [RelayCommand] private void ShowWidget()=>WidgetRequested?.Invoke();
    [RelayCommand] private void Export()=>ExportRequested?.Invoke();
    [RelayCommand] private void TrySample()=>SampleRequested?.Invoke();
    [RelayCommand] private void OpenActivity()=>Navigate("openrouter");
    [RelayCommand] private static void OpenPrivacy()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Localization.PrivacyUrl) { UseShellExecute=true })?.Dispose();
    [RelayCommand] private async Task RefreshAsync()
    {
        if(IsBusy) return;IsBusy=true;Status=L["refreshing"];
        try { await refresh();await QueryAsync();UpdateAccounts();if(IsOpenRouter) await OpenRouter.LoadAsync(); }
        finally { IsBusy=false; }
    }
    public UsageFilter CurrentFilter()
    {
        var today=DateOnly.FromDateTime(DateTime.Now);
        var start=Range.Key switch { "today"=>today,"week"=>today.AddDays(-6),"month"=>today.AddDays(-29),_=>default };
        return new(start==default?null:UsageDates.StartOfDay(start,TimeZoneInfo.Local),null,
            Enum.TryParse<ProviderKind>(Tool.Key,out var provider)?provider:null,string.IsNullOrEmpty(Model.Key)?null:Model.Key,focusedProject,focusedSession);
    }
    public async Task QueryAsync()
    {
        var generation=Interlocked.Increment(ref queryGeneration);
        var filter=CurrentFilter();var dimension=Page switch { "projects"=>"project","sessions"=>"session",_=>"model" };
        var today=DateOnly.FromDateTime(DateTime.Now);
        var todayFilter=new UsageFilter(UsageDates.StartOfDay(today,TimeZoneInfo.Local),UsageDates.StartOfDay(today.AddDays(1),TimeZoneInfo.Local));
        var heatStart=HeatmapStart(today);
        var result=await Task.Run(async()=>new {
            Summary=await database.SummarizeAsync(filter),Days=await database.DailyAsync(filter,TimeZoneInfo.Local),
            Groups=await database.GroupAsync(filter,dimension),Models=await database.GroupAsync(new(),"model"),
            Heat=await database.DailyAsync(filter with { From=UsageDates.StartOfDay(heatStart,TimeZoneInfo.Local),Until=null },TimeZoneInfo.Local),
            Events=await database.ReadEventsAsync(filter),TodayEvents=await database.ReadEventsAsync(todayFilter)
        });
        if(generation!=queryGeneration) return;
        lastQuery=DateTimeOffset.Now;
        var summary=result.Summary;var tokens=summary.Tokens;
        Total=Number(tokens.Total);Input=Number(tokens.Input);Output=Number(tokens.Output);
        var cacheTokens=tokens.CacheRead+tokens.CacheWrite5m+tokens.CacheWrite1h;
        Cache=Number(cacheTokens);Requests=Number(summary.Requests);
        HasPeriodUsage=tokens.Total>0;
        MixInput=tokens.Input;MixCache=cacheTokens;MixOutput=tokens.Output;
        InputShare=Share(tokens.Input,tokens.Total);CacheShare=Share(cacheTokens,tokens.Total);OutputShare=Share(tokens.Output,tokens.Total);
        Coverage=summary.UncertainRequests>0?$"{summary.UncertainRequests} {L["verified"]}":L["scope"];
        Status=summary.Requests==0?L["noData"]:L["local"]+" · "+DateTime.Now.ToString("HH:mm");
        // First run: no stored usage records at all, in any range.
        ShowGettingStarted=result.Models.Count==0;
        Daily=result.Days;Groups.Clear();
        var tariff=DateTimeOffset.UtcNow;
        var estimate=prices.Estimate(result.Events,settings.PriceOverrides,tariff);EstimatedCost=CostText(estimate);
        PriceCoverage=DescribePrices(estimate,result.Events,tariff);
        WidgetTotal=Number(result.TodayEvents.Where(e=>e.Identity!=IdentityQuality.UncertainFork).Sum(e=>e.Tokens.Total));
        WidgetStatus=result.TodayEvents.Count==0?L["noData"]:L["local"]+" · "+DateTime.Now.ToString("HH:mm");
        var todayEstimate=prices.Estimate(result.TodayEvents,settings.PriceOverrides,tariff);
        WidgetCost=CostText(todayEstimate);WidgetPriceCoverage=DescribePrices(todayEstimate,result.TodayEvents,tariff);
        var providerByModel=result.Events.GroupBy(e=>e.Model).ToDictionary(g=>g.Key,g=>g.First().Provider);
        foreach(var entry in result.Models) if(!Models.Any(m=>m.Key==entry.Key)) Models.Add(new(entry.Key,entry.Key));
        var groupedEvents=result.Events.GroupBy(e=>dimension switch { "project"=>e.Project,"session"=>e.SessionId,_=>e.Model }).ToDictionary(g=>g.Key,g=>g.AsEnumerable());
        var groupTotal=Math.Max(1,result.Groups.Sum(g=>g.Tokens.Total));
        foreach(var group in result.Groups)
        {
            var provider=dimension=="model"&&providerByModel.TryGetValue(group.Key,out var kind)?kind:(ProviderKind?)null;
            Groups.Add(new(group.Key,Number(group.Tokens.Total),Number(group.Tokens.Input),Number(group.Tokens.Output),
                Number(group.Tokens.CacheRead+group.Tokens.CacheWrite5m+group.Tokens.CacheWrite1h),Number(group.Requests),
                CostText(prices.Estimate(groupedEvents.GetValueOrDefault(group.Key,[]),settings.PriceOverrides,tariff))) {
                    IsModel=dimension=="model",Tool=provider is { } p?ToolName(p):"",DotKey=provider is { } q?ProviderKey(q)+".Bar":"",
                    Share=group.Tokens.Total*100d/groupTotal,ShareText=Format.Percent(group.Tokens.Total*100d/groupTotal) });
        }
        GroupCount=Number(result.Groups.Count);
        var unit=dimension switch { "project"=>"projectCount","session"=>"sessionCount",_=>"modelCount" }+(result.Groups.Count==1?"One":"Many");
        GroupCountText=string.Format(Format.Culture,L[unit],GroupCount);
        TopModels=dimension=="model"?Groups.Take(5).Select(g=>new ModelShare(g.Name,g.Tool,g.DotKey,g.Total,g.Share,g.ShareText)).ToList():TopModels;
        HistoryBars=BuildHistory(result.Events,result.Days,today,out var now);HistoryNow=now;
        HistoryCaption=Range.Label+" · "+L[Range.Key switch { "today"=>"tokensPerHour","all"=>"tokensPerWeek",_=>"tokensPerDay" }];
        HeatDays=BuildHeat(result.Heat,heatStart,today);
        Breadcrumbs=BuildCrumbs();Chips=BuildChips();
        SetupSteps=BuildSetup();SetupProgress=string.Format(Format.Culture,L["setupProgress"],SetupSteps.Count(s=>s.Done),SetupSteps.Count);
        await LoadOpenRouterCardsAsync();
        OnPropertyChanged(nameof(TodayLabel));OnPropertyChanged(nameof(PeriodLabel));
        UpdatePill();
        widgetDay=today;widgetUpdatedAt=DateTimeOffset.UtcNow;WidgetDataChanged?.Invoke();
    }
    private string Number(long value)=>Format.Number(value);
    private string Share(long part,long whole)=>whole<=0?"":Format.Percent(part*100d/whole);
    public static string ProviderKey(ProviderKind provider)=>provider switch { ProviderKind.Claude=>"Claude",ProviderKind.Codex=>"Codex",_=>"OpenRouter" };
    public static string ToolName(ProviderKind provider)=>provider switch { ProviderKind.Claude=>"Claude Code",ProviderKind.Codex=>"Codex",_=>"OpenRouter" };
    private string DescribePrices(CostSummary estimate,IEnumerable<UsageEvent> events,DateTimeOffset tariff)
    {
        var models=events.Where(e=>e.Identity!=IdentityQuality.UncertainFork).Select(e=>e.Model).ToHashSet();
        var activeOverrides=settings.PriceOverrides.Where(p=>p.IsValid&&p.EffectiveFrom<=tariff&&models.Contains(p.Model)).Select(p=>p.Model).Distinct().Count();
        return $"{L["tariffBasis"]} · {prices.Entries.Max(p=>p.EffectiveFrom):yyyy-MM-dd}"+
            (activeOverrides>0?$" · {L["activeOverrides"]}: {activeOverrides}":"")+
            (estimate.UnknownRecords>0?$" · {L["priceUnknown"]}: {string.Join(", ",estimate.UnknownModels)}":"");
    }
    private string CostText(CostSummary cost)=>cost.PricedRecords==0?"—":
        string.Join(" + ",cost.Amounts.Select(p=>Format.Estimate(p.Value,p.Key)))+(cost.UnknownRecords>0?" + ?":"");

    // Usage history: 24 hourly bars today, daily bars for 7 / 30 days, weekly bars for all time.
    private IReadOnlyList<ChartBar> BuildHistory(IReadOnlyList<UsageEvent> events,IReadOnlyList<DailyUsage> days,DateOnly today,out double nowIndex)
    {
        nowIndex=double.NaN;var bars=new List<ChartBar>();var culture=Format.Culture;
        static (long,long,long) Split(TokenUsage t)=>(t.Input,t.CacheRead+t.CacheWrite5m+t.CacheWrite1h,t.Output);
        if(Range.Key=="today")
        {
            var now=DateTime.Now;nowIndex=now.Hour+now.Minute/60d;
            var byHour=events.Where(e=>e.Identity!=IdentityQuality.UncertainFork).GroupBy(e=>e.Timestamp.ToLocalTime().Hour)
                .ToDictionary(g=>g.Key,g=>g.Aggregate(new TokenUsage(),(sum,e)=>sum+e.Tokens));
            for(var hour=0;hour<24;hour++)
            {
                var (i,c,o)=Split(byHour.GetValueOrDefault(hour)??new());
                bars.Add(new(hour%3==0?hour.ToString("00",culture):"",$"{hour:00}:00 – {(hour+1)%24:00}:00",i,c,o,hour>now.Hour));
            }
            return bars;
        }
        var first=Range.Key=="week"?today.AddDays(-6):Range.Key=="month"?today.AddDays(-29):days.Count>0?days.Min(d=>d.Day):today;
        var byDay=days.ToDictionary(d=>d.Day,d=>d.Tokens);
        if(Range.Key=="all")
        {
            var start=first.AddDays(-(((int)first.DayOfWeek+6)%7));var weeks=0;
            for(var week=start;week<=today;week=week.AddDays(7),weeks++)
            {
                var sum=Enumerable.Range(0,7).Select(week.AddDays).Aggregate(new TokenUsage(),(s,d)=>s+(byDay.GetValueOrDefault(d)??new()));
                var (i,c,o)=Split(sum);
                bars.Add(new("",string.Format(culture,L["weekOf"],week.ToString("MMM d",culture)),i,c,o,false) { Day=week });
            }
            var step=Math.Max(1,(int)Math.Ceiling(bars.Count/6d));
            return bars.Select((b,index)=>index%step==0?b with { Label=b.Day!.Value.ToString("MMM d",culture) }:b).ToList();
        }
        var count=today.DayNumber-first.DayNumber+1;var labelStep=count<=7?1:7;
        for(var n=0;n<count;n++)
        {
            var day=first.AddDays(n);var (i,c,o)=Split(byDay.GetValueOrDefault(day)??new());
            bars.Add(new(n%labelStep==0||count<=7?day.ToString(count<=7?"ddd":"MMM d",culture):"",day.ToString("ddd, MMM d",culture),i,c,o,false) { Day=day });
        }
        return bars;
    }
    private static DateOnly HeatmapStart(DateOnly today)=>today.AddDays(-(((int)today.DayOfWeek+6)%7)).AddDays(-7*12);
    // 13 Monday-first weeks; levels 1–4 split the non-zero days at their quartiles.
    private IReadOnlyList<HeatDay> BuildHeat(IReadOnlyList<DailyUsage> heat,DateOnly start,DateOnly today)
    {
        var byDay=heat.ToDictionary(d=>d.Day,d=>d.Tokens.Total);
        var values=byDay.Where(p=>p.Value>0&&p.Key>=start&&p.Key<=today).Select(p=>p.Value).OrderBy(v=>v).ToList();
        long Quantile(double q)=>values.Count==0?0:values[Math.Clamp((int)Math.Ceiling(q*values.Count)-1,0,values.Count-1)];
        var thresholds=new[]{Quantile(0.25),Quantile(0.5),Quantile(0.75)};
        var cells=new List<HeatDay>();
        for(var i=0;i<91;i++)
        {
            var day=start.AddDays(i);var value=byDay.GetValueOrDefault(day);
            var level=value<=0?0:value<=thresholds[0]?1:value<=thresholds[1]?2:value<=thresholds[2]?3:4;
            cells.Add(new(day,value,level,day==today,day>today,
                day.ToString("ddd, MMM d, yyyy",Format.Culture)+" · "+string.Format(Format.Culture,L["tokensCount"],Number(value))));
        }
        return cells;
    }
    private IReadOnlyList<Crumb> BuildCrumbs()
    {
        if(!IsTable||focusedProject is null&&focusedSession is null) return [];
        var crumbs=new List<Crumb> { new(L["projects"],"projects",false) };
        if(focusedProject is not null) crumbs.Add(new(focusedProject,"project",Page=="sessions"));
        if(focusedSession is not null) crumbs.Add(new(focusedSession,"session",false));
        if(Page=="models") crumbs.Add(new(L["models"],"models",true));
        return crumbs;
    }
    private IReadOnlyList<FilterChip> BuildChips()
    {
        var chips=new List<FilterChip>();
        if(focusedProject is not null) chips.Add(new(L["projectColumn"],focusedProject,"project",string.Format(L["removeFilter"],L["projectColumn"])));
        if(focusedSession is not null) chips.Add(new(L["sessionColumn"],focusedSession,"session",string.Format(L["removeFilter"],L["sessionColumn"])));
        return chips;
    }
    private IReadOnlyList<SetupStep> BuildSetup()
    {
        string Display(string path)
        {
            var home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return path.StartsWith(home,StringComparison.OrdinalIgnoreCase)?"%USERPROFILE%"+path[home.Length..]:path;
        }
        SetupStep Logs(int number,ProviderKind provider,string title,string runAction)
        {
            var folders=settings.Sources.Where(s=>s.Provider==provider&&s.Enabled).ToList();
            var found=folders.FirstOrDefault(s=>Directory.Exists(s.Directory));
            var shown=found??folders.FirstOrDefault();
            return found is not null
                ?new(number,title,"",L["folderFoundWaiting"],shown is null?null:Display(shown.Directory),runAction,"",true,false,false,false)
                :new(number,title,"",L["folderNotFound"],shown is null?null:Display(shown.Directory),L["chooseFolder"],"sources",false,true,false,false);
        }
        var hasSubscription=settings.Accounts.Any(a=>a.Enabled&&a.Provider!=ProviderKind.OpenRouter);
        var hasKey=settings.Accounts.Any(a=>a.Enabled&&a.Provider==ProviderKind.OpenRouter);
        return [Logs(1,ProviderKind.Claude,L["setupClaudeLogs"],L["runClaudeOnce"]),Logs(2,ProviderKind.Codex,L["setupCodexLogs"],L["runCodexOnce"]),
            new(3,L["setupQuotas"],"",hasSubscription?L["setupQuotasDone"]:L["setupQuotasNote"],null,hasSubscription?"":L["addAccount"],"accounts:new",hasSubscription,false,true,false),
            new(4,"OpenRouter",L["optional"],hasKey?L["setupOpenRouterDone"]:L["setupOpenRouterNote"],null,hasKey?"":L["addApiKey"],"accounts:new-openrouter",hasKey,false,false,true)];
    }
    private async Task LoadOpenRouterCardsAsync()
    {
        var cards=new List<OpenRouterCard>();
        foreach(var account in settings.Accounts.Where(a=>a.Enabled&&a.Provider==ProviderKind.OpenRouter))
        {
            var rows=activity is null?[]:await activity.ReadAsync(account.Id);
            var since=DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30);
            var recent=rows.Where(r=>r.Day>=since).ToList();
            var spend=recent.Sum(r=>r.Spend);var byok=recent.Sum(r=>r.ByokSpend);var count=recent.Sum(r=>r.Requests);
            cards.Add(new(account.Label,Format.Money(spend),string.Format(Format.Culture,L["requestsByok"],Number(count),Format.Money(byok)),account.Id));
        }
        OpenRouterCards.Clear();foreach(var card in cards) OpenRouterCards.Add(card);
    }

    public void UpdateAccounts()
    {
        Accounts.Clear();KeyAccounts.Clear();WidgetAccounts.Clear();var now=DateTimeOffset.UtcNow;
        var hidden=0;
        foreach(var account in settings.Accounts.Where(a=>a.Enabled)
                    .OrderBy(a=>settings.AccountOrder.IndexOf(a.Id) is var index && index>=0?index:int.MaxValue))
        {
            var card=BuildCard(account,now);
            if(account.Provider==ProviderKind.OpenRouter) KeyAccounts.Add(card);else Accounts.Add(card);
            if(settings.HiddenAccounts.Contains(account.Id)) hidden++;
            else if(account.Provider!=ProviderKind.OpenRouter||card.Windows.Count>0) WidgetAccounts.Add(card);
        }
        HiddenAccountsText=hidden==0?"":string.Format(Format.Culture,L[hidden==1?"hiddenAccountsOne":"hiddenAccountsMany"],hidden);
        OnPropertyChanged(nameof(HasSubscriptions));OnPropertyChanged(nameof(PaceLegend));
        UpdatePill();
        WidgetDataChanged?.Invoke();
    }
    private AccountCard BuildCard(AccountProfile account,DateTimeOffset now)
    {
        var state=quotas.Statuses.FirstOrDefault(s=>s.Account.SameConnection(account));
        var statusKey=state?.State switch { ConnectionState.Ready=>"ready",ConnectionState.Partial=>"partial",ConnectionState.SignInRequired=>"signIn",
            ConnectionState.RateLimited=>"rateLimited",ConnectionState.Unsupported=>"unsupported",ConnectionState.Unavailable=>"unavailable",_=>"notChecked" };
        var stale=state is not null&&QuotaPace.IsStale(state,now);
        var providerKey=ProviderKey(account.Provider);
        var observed=state?.LastGood?.ObservedAt.ToLocalTime().ToString("HH:mm",Format.Culture)??"";
        var statusText=L[statusKey]+(stale?" · "+L["stale"]:"");
        var rows=(state?.LastGood?.Windows??[]).Select(w=>BuildRow(w,providerKey,stale,now)).ToList();
        var money=(state?.LastGood?.Money??[]).Select(m=>new MoneyRow(m.Id,L[m.Label],Format.Money(m.Value,m.Currency)+
            (state?.LastGood?.Sources?.Any(s=>s.Id==(m.Scope=="key"?"key":"credits")&&s.State!=ConnectionState.Ready&&s.LastSuccess is not null)==true?" · "+L["stale"]:""))).ToList();
        var updated=state?.LastGood is not null?L["updated"]+" "+observed:L["notChecked"];
        var resetWindow=rows.Where(r=>r.Primary).OrderBy(r=>r.Order).FirstOrDefault();
        var detail=string.Join("\n",(state?.LastGood?.Sources??[]).Select(s=>L[s.Id=="key"?"apiKey":"balance"]+": "+
            L[s.State==ConnectionState.Ready?"ready":s.State==ConnectionState.SignInRequired?"permissionRequired":s.State==ConnectionState.RateLimited?"rateLimited":"unavailable"]+
            (s.LastSuccess is { } success?" · "+L["updated"]+" "+success.ToLocalTime().ToString("g",Format.Culture):"")));
        var ahead=rows.Where(r=>r.Primary&&r.HasForecast).OrderBy(r=>r.Order).FirstOrDefault();
        var window=state?.LastGood?.Windows.FirstOrDefault(w=>w.Id==resetWindow?.Id);
        var compactFact=ahead is not null?string.Format(Format.Culture,L["runsOutShort"],ahead.Label,ahead.Forecast is null?"":ForecastDay(state!,ahead.Id,now)):
            window?.ResetsAt is { } reset?string.Format(Format.Culture,L["resetsShort"],resetWindow!.Label,Format.Time(reset,now)):"";
        return new AccountCard(account.Label,account.Provider.ToString(),providerKey+".Text",statusText,updated,rows,money,
            resetWindow?.Reset??"",settings.Compact,detail) {
            AccountId=account.Id,Tool=ToolName(account.Provider),TintBgKey=providerKey+".TintBg",TintTextKey=providerKey+".TintText",
            IsStale=stale,StaleNote=stale?string.Format(Format.Culture,L["dataFrom"],observed):"",UpdatedTime=observed,
            CompactFact=compactFact,CompactFactKey=ahead is not null&&!stale?"Warn.Text":"Text.Muted" };
    }
    private string ForecastDay(AccountStatus state,string windowId,DateTimeOffset now)
    {
        var window=state.LastGood?.Windows.FirstOrDefault(w=>w.Id==windowId);
        return window is not null&&QuotaPace.Read(window,now)?.RunsOutAt is { } at?Format.Day(at):"";
    }
    private QuotaRow BuildRow(QuotaWindow w,string providerKey,bool stale,DateTimeOffset now)
    {
        var label=w.WindowMinutes is >= 10080 || w.Label=="seven_day"?L["weeklyWindow"]:
            w.WindowMinutes is >0 and <=1440 || w.Label is "five_hour" or "session"?L["sessionWindow"]:
            w.Label is "daily" or "weekly" or "monthly" or "lifetime"?L[w.Label]:L["additionalWindow"]+" · "+w.Label;
        if(w.Scope is not ("account" or "key" or "codex")) label+=" · "+w.Scope;
        var order=w.Label is "five_hour" or "session"||w.WindowMinutes is >0 and <=1440?0:w.Label=="seven_day"||w.WindowMinutes>=10080?1:2;
        var primary=w.Label is "five_hour" or "seven_day" or "session" || w.WindowMinutes>0 || w.Id=="key_limit";
        var remaining=settings.ShowRemaining;
        var reading=QuotaPace.Read(w,now);
        var resetText=w.ResetsAt is { } reset?reset>now?string.Format(Format.Culture,L["resetsIn"],Format.Time(reset,now),Format.Duration(reset-now)):L["awaitingReset"]:L["unknown"];
        if(reading is null)
            return new QuotaRow(label,"—",0,"Text.Muted",resetText,w.Scope,primary) { Id=w.Id,Order=order,ShortValue="—",FillKey=providerKey+".Bar",WidgetMeta=resetText };
        var severity=reading.Severity;
        var fillKey=severity switch { QuotaSeverity.Critical=>"Danger",QuotaSeverity.Ahead=>"Warn",_=>providerKey };
        fillKey+=stale?".StaleBar":".Bar";
        var valueKey=stale?"Text.Secondary":severity switch { QuotaSeverity.Critical=>"Danger.Text",QuotaSeverity.Ahead=>"Warn.Text",_=>"Text.Primary" };
        var paceNote=stale?"":reading.Pace switch { PaceState.Ahead=>L["aheadOfPace"],PaceState.On=>L["onPace"],PaceState.Under=>L["underPace"],_=>"" };
        var runsOut=stale?null:reading.RunsOutAt;
        string? forecast=null;var widgetMeta=resetText;var metaKey="Text.Muted";
        if(runsOut is { } at&&w.ResetsAt is { } resetAt)
        {
            var approx="~"+Format.Time(at,now);
            forecast=string.Format(Format.Culture,L["forecastCallout"],approx,Format.Duration(resetAt-at));
            widgetMeta=string.Format(Format.Culture,L["runsOutMeta"],approx,Format.Time(resetAt,now));metaKey="Warn.Text";
        }
        var value=Format.QuotaValue(reading.Used,remaining);
        return new QuotaRow(label,value,QuotaPace.DisplayFill(reading.Used,remaining),valueKey,resetText,w.Scope,primary) {
            Id=w.Id,Order=order,Marker=stale?double.NaN:QuotaPace.DisplayMarker(reading.Elapsed,remaining)??double.NaN,FillKey=fillKey,ValueKey=valueKey,
            ShortValue=Format.Percent(remaining?100-reading.Used:reading.Used),MarkerTip=L[remaining?"markerLeftTip":"markerUsedTip"],
            PaceNote=paceNote,PaceNoteKey=reading.Pace==PaceState.Ahead?"Warn.Text":"Text.Muted",Forecast=forecast,
            WidgetMeta=widgetMeta,WidgetMetaKey=metaKey,Severity=stale?QuotaSeverity.Normal:severity };
    }
    private void UpdatePill()
    {
        var cards=Accounts.Concat(KeyAccounts).ToList();var staleCount=cards.Count(c=>c.IsStale);
        var latest=quotas.Statuses.Where(s=>s.LastGood is not null&&settings.Accounts.Any(a=>a.Enabled&&a.SameConnection(s.Account)))
            .Select(s=>s.LastGood!.ObservedAt).DefaultIfEmpty(lastQuery).Max().ToLocalTime();
        var time=latest.ToString("HH:mm",Format.Culture);
        WidgetTime=time;
        if(IsBusy) { PillKind="refreshing";PillText=L["refreshingShort"];WidgetDotKey="Text.Muted";return; }
        if(staleCount>0) { PillKind="stale";PillText=string.Format(Format.Culture,L[staleCount==1?"staleOne":"staleMany"],staleCount);WidgetDotKey="Warn.Bar";return; }
        if(ShowGettingStarted) { PillKind="empty";PillText=L["noUsageYet"];WidgetDotKey="Text.Disabled";return; }
        PillKind="ok";PillText=string.Format(Format.Culture,L["upToDateAt"],time);WidgetDotKey="Ok.Dot";
    }
}

public sealed record ChartBar(string Label,string Range,long Input,long Cache,long Output,bool IsFuture)
{
    public long Total=>Input+Cache+Output;
    public DateOnly? Day { get; init; }
}
public sealed record HeatDay(DateOnly Day,long Tokens,int Level,bool IsToday,bool IsFuture,string Tooltip);
