using System.Collections.ObjectModel;
using System.Globalization;
using AiUsageViewer.Core;
using AiUsageViewer.Application;
using AiUsageViewer.Application.WindowsWidgets;
using AiUsageViewer.Infrastructure.Storage;
using AiUsageViewer.Infrastructure.Analytics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUsageViewer.App;

public sealed record Choice(string Key,string Label) { public override string ToString()=>Label; }
public sealed record GroupRow(string Name,string Total,string Input,string Output,string Cache,string Requests,string Cost);
public sealed record HeatCell(string Color,string Tooltip);
public sealed record QuotaRow(string Label,string Percent,double Value,string Color,string Reset,string Scope,bool Primary);
public sealed record MoneyRow(string Id,string Label,string Value);
public sealed record AccountCard(string Name,string Provider,string Color,string Status,string Updated,
    IReadOnlyList<QuotaRow> Windows,IReadOnlyList<MoneyRow> Money,string NextReset,bool Compact,string Detail)
{
    public IReadOnlyList<QuotaRow> WidgetWindows=>Windows.Any(w=>w.Primary)?Windows.Where(w=>w.Primary).Take(3).ToList():Windows.Take(1).ToList();
    public IReadOnlyList<MoneyRow> WidgetMoney=>Money.Where(m=>m.Id is "balance" or "usage_daily").ToList();
}

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly UsageDatabase database;
    private readonly QuotaCoordinator quotas;
    private readonly Func<Task> refresh;
    private AppSettings settings;
    private int queryGeneration;
    private readonly PriceCatalog prices=BundledPrices.Load();
    private string? focusedProject,focusedSession;
    public Localization L { get; private set; }
    public ObservableCollection<AccountCard> Accounts { get; }=[];
    public ObservableCollection<AccountCard> WidgetAccounts { get; }=[];
    public bool ShowCost=>settings.ShowEstimatedCost;
    public bool Compact=>settings.Compact;
    public ObservableCollection<GroupRow> Groups { get; }=[];
    public ObservableCollection<HeatCell> Heatmap { get; }=[];
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
    public bool Demo { get; }
    public bool CanTrySample=>!Demo;
    public bool IsOverview=>Page=="overview";
    public bool IsSubscriptions=>Page=="subscriptions";
    public bool IsTable=>!IsOverview&&!IsSubscriptions;
    public string PageTitle=>L[Page];
    public string TodayLabel=>L["today"];
    public event Action? SettingsRequested;
    public event Action? WidgetRequested;
    public event Action? ExportRequested;
    public event Action? ActivityRequested;
    public event Action? SampleRequested;
    public event Action? WidgetDataChanged;
    private DateOnly widgetDay;
    private DateTimeOffset widgetUpdatedAt;
    public WidgetSnapshot CreateWindowsWidgetSnapshot()=>new(WidgetSnapshot.CurrentVersion,L.Language,
        widgetDay,widgetUpdatedAt,DateTimeOffset.UtcNow,WidgetTotal,ShowCost?WidgetCost:null,
        WidgetAccounts.Select(a=>new WidgetAccount(a.Name,a.Status,
            a.WidgetWindows.Select(w=>w.Label+": "+w.Percent).Concat(a.WidgetMoney.Select(m=>m.Label+": "+m.Value)).ToArray())).ToArray(),Demo);

    public DashboardViewModel(UsageDatabase database,QuotaCoordinator quotas,AppSettings settings,Func<Task> refresh,bool demo=false)
    {
        this.database=database;this.quotas=quotas;this.settings=settings;this.refresh=refresh;Demo=demo;
        L=new(settings.Language);
        Ranges=CreateRanges();Tools=CreateTools();range=Ranges[0];tool=Tools[0];
        model=new("",L["allModels"]);Models.Add(model);
    }
    private List<Choice> CreateRanges()=>new[]{"today","week","month","all"}.Select(k=>new Choice(k,L[k])).ToList();
    private List<Choice> CreateTools()=>[new("",L["allTools"]),new("Claude","Claude Code"),new("Codex","Codex")];
    public void ApplySettings(AppSettings value)
    {
        var rangeKey=Range.Key;var toolKey=Tool.Key;var modelKey=Model.Key;
        settings=value;AppLanguages.ApplyCulture(value.Language);L=new(value.Language);OnPropertyChanged(nameof(L));
        OnPropertyChanged(nameof(ShowCost));OnPropertyChanged(nameof(Compact));
        Ranges=CreateRanges();Tools=CreateTools();OnPropertyChanged(nameof(Ranges));OnPropertyChanged(nameof(Tools));
        Range=Ranges.FirstOrDefault(x=>x.Key==rangeKey)??Ranges[0];Tool=Tools.FirstOrDefault(x=>x.Key==toolKey)??Tools[0];
        Models[0]=new("",L["allModels"]);Model=Models.FirstOrDefault(m=>m.Key==modelKey)??Models[0];
        OnPropertyChanged(nameof(PageTitle));UpdateAccounts();
    }
    partial void OnPageChanged(string value)
    {
        OnPropertyChanged(nameof(IsOverview));OnPropertyChanged(nameof(IsSubscriptions));OnPropertyChanged(nameof(IsTable));OnPropertyChanged(nameof(PageTitle));QueueQuery();
    }
    partial void OnStatusChanged(string value)=>WidgetStatus=value;
    private async void QueueQuery() { try { await QueryAsync(); } catch(Exception e) when(e is IOException or Microsoft.Data.Sqlite.SqliteException) { Status=L["sourceErrors"]; } }
    [RelayCommand] private void Navigate(string page) { ClearFocus();Page=page; }
    [RelayCommand] private void ClearFocus() { focusedProject=null;focusedSession=null;FocusLabel="";OnPropertyChanged(nameof(HasFocus));QueueQuery(); }
    [RelayCommand] private void OpenGroup(GroupRow? row)
    {
        if(row is null) return;
        if(Page=="projects") { focusedProject=row.Name;focusedSession=null;FocusLabel=row.Name;Page="sessions"; }
        else if(Page=="sessions") { focusedSession=row.Name;FocusLabel=(focusedProject is null?"":focusedProject+" / ")+row.Name;Page="models"; }
        else { Model=Models.FirstOrDefault(m=>m.Key==row.Name)??Models[0];Page="overview"; }
        OnPropertyChanged(nameof(HasFocus));
    }
    [RelayCommand] private void OpenSettings()=>SettingsRequested?.Invoke();
    [RelayCommand] private void ShowWidget()=>WidgetRequested?.Invoke();
    [RelayCommand] private void Export()=>ExportRequested?.Invoke();
    [RelayCommand] private void OpenActivity()=>ActivityRequested?.Invoke();
    [RelayCommand] private void TrySample()=>SampleRequested?.Invoke();
    [RelayCommand] private static void OpenPrivacy()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Localization.PrivacyUrl) { UseShellExecute=true })?.Dispose();
    [RelayCommand] private async Task RefreshAsync()
    {
        if(IsBusy) return;IsBusy=true;Status=L["refreshing"];
        try { await refresh();await QueryAsync();UpdateAccounts(); }
        finally { IsBusy=false; }
    }
    public UsageFilter CurrentFilter()
    {
        var today=DateOnly.FromDateTime(DateTime.Now);
        var start=Range.Key switch { "today"=>today,"week"=>today.AddDays(-6),"month"=>new(today.Year,today.Month,1),_=>default };
        return new(start==default?null:UsageDates.StartOfDay(start,TimeZoneInfo.Local),null,
            Enum.TryParse<ProviderKind>(Tool.Key,out var provider)?provider:null,string.IsNullOrEmpty(Model.Key)?null:Model.Key,focusedProject,focusedSession);
    }
    public async Task QueryAsync()
    {
        var generation=Interlocked.Increment(ref queryGeneration);
        var filter=CurrentFilter();var dimension=Page switch { "projects"=>"project","sessions"=>"session",_=>"model" };
        var today=DateOnly.FromDateTime(DateTime.Now);
        var todayFilter=new UsageFilter(UsageDates.StartOfDay(today,TimeZoneInfo.Local),UsageDates.StartOfDay(today.AddDays(1),TimeZoneInfo.Local));
        var heatStart=DateOnly.FromDateTime(DateTime.Now).AddDays(-90);
        var result=await Task.Run(async()=>new {
            Summary=await database.SummarizeAsync(filter),Days=await database.DailyAsync(filter,TimeZoneInfo.Local),
            Groups=await database.GroupAsync(filter,dimension),Models=await database.GroupAsync(new(),"model"),
            Heat=await database.DailyAsync(filter with { From=UsageDates.StartOfDay(heatStart,TimeZoneInfo.Local) },TimeZoneInfo.Local),
            Events=await database.ReadEventsAsync(filter),TodayEvents=await database.ReadEventsAsync(todayFilter)
        });
        if(generation!=queryGeneration) return;
        var summary=result.Summary;
        Total=Number(summary.Tokens.Total);Input=Number(summary.Tokens.Input);Output=Number(summary.Tokens.Output);
        Cache=Number(summary.Tokens.CacheRead+summary.Tokens.CacheWrite5m+summary.Tokens.CacheWrite1h);Requests=Number(summary.Requests);
        Coverage=summary.UncertainRequests>0?$"{summary.UncertainRequests} {L["verified"]}":L["scope"];
        Status=summary.Requests==0?L["noData"]:L["local"]+" · "+DateTime.Now.ToString("HH:mm");
        // No stored records at all (any range) and nothing else to show: explain what the app needs.
        ShowGettingStarted=result.Models.Count==0&&!settings.Accounts.Any(a=>a.Provider==ProviderKind.OpenRouter);
        Daily=result.Days;Groups.Clear();
        var tariff=DateTimeOffset.UtcNow;
        var estimate=prices.Estimate(result.Events,settings.PriceOverrides,tariff);EstimatedCost=CostText(estimate);
        PriceCoverage=DescribePrices(estimate,result.Events,tariff);
        WidgetTotal=Number(result.TodayEvents.Where(e=>e.Identity!=IdentityQuality.UncertainFork).Sum(e=>e.Tokens.Total));
        WidgetStatus=result.TodayEvents.Count==0?L["noData"]:L["local"]+" · "+DateTime.Now.ToString("HH:mm");
        var todayEstimate=prices.Estimate(result.TodayEvents,settings.PriceOverrides,tariff);
        WidgetCost=CostText(todayEstimate);WidgetPriceCoverage=DescribePrices(todayEstimate,result.TodayEvents,tariff);
        var groupedEvents=result.Events.GroupBy(e=>dimension switch { "project"=>e.Project,"session"=>e.SessionId,_=>e.Model }).ToDictionary(g=>g.Key,g=>g.AsEnumerable());
        foreach(var group in result.Groups) Groups.Add(new(group.Key,Number(group.Tokens.Total),Number(group.Tokens.Input),Number(group.Tokens.Output),
            Number(group.Tokens.CacheRead+group.Tokens.CacheWrite5m+group.Tokens.CacheWrite1h),Number(group.Requests),
            CostText(prices.Estimate(groupedEvents.GetValueOrDefault(group.Key,[]),settings.PriceOverrides,tariff))));
        foreach(var entry in result.Models) if(!Models.Any(m=>m.Key==entry.Key)) Models.Add(new(entry.Key,entry.Key));
        Heatmap.Clear();var byDay=result.Heat.ToDictionary(d=>d.Day);var maximum=Math.Max(1,result.Heat.Select(d=>d.Tokens.Total).DefaultIfEmpty().Max());
        for(var i=0;i<91;i++)
        {
            var day=heatStart.AddDays((i%13)*7+i/13);var value=byDay.GetValueOrDefault(day)?.Tokens.Total??0;
            var color=value==0?"#293243":(value/(double)maximum) switch { >0.75=>"#B4A8FF",>0.4=>"#8B7ADB",>0.15=>"#6558A0",_=>"#453F67" };
            Heatmap.Add(new(color,day.ToString("dd MMM yyyy")+" · "+Number(value)+" token"));
        }
        OnPropertyChanged(nameof(TodayLabel));
        widgetDay=today;widgetUpdatedAt=DateTimeOffset.UtcNow;WidgetDataChanged?.Invoke();
    }
    private string Number(long value)=>value.ToString("N0",AppLanguages.CultureFor(settings.Language));
    private string DescribePrices(CostSummary estimate,IEnumerable<UsageEvent> events,DateTimeOffset tariff)
    {
        var models=events.Where(e=>e.Identity!=IdentityQuality.UncertainFork).Select(e=>e.Model).ToHashSet();
        var activeOverrides=settings.PriceOverrides.Where(p=>p.IsValid&&p.EffectiveFrom<=tariff&&models.Contains(p.Model)).Select(p=>p.Model).Distinct().Count();
        return $"{L["tariffBasis"]} · {prices.Entries.Max(p=>p.EffectiveFrom):yyyy-MM-dd}"+
            (activeOverrides>0?$" · {L["activeOverrides"]}: {activeOverrides}":"")+
            (estimate.UnknownRecords>0?$" · {L["priceUnknown"]}: {string.Join(", ",estimate.UnknownModels)}":"");
    }
    private static string CostText(CostSummary cost)=>cost.PricedRecords==0?"—":
        "≈ "+string.Join(" + ",cost.Amounts.Select(p=>$"{p.Value:N2} {p.Key}"))+(cost.UnknownRecords>0?" + ?":"");
    public void UpdateAccounts()
    {
        Accounts.Clear();WidgetAccounts.Clear();var now=DateTimeOffset.UtcNow;
        foreach(var account in settings.Accounts.Where(a=>a.Enabled)
                    .OrderBy(a=>settings.AccountOrder.IndexOf(a.Id) is var index && index>=0?index:int.MaxValue))
        {
            var state=quotas.Statuses.FirstOrDefault(s=>s.Account.SameConnection(account));
            var statusKey=state?.State switch { ConnectionState.Ready=>"ready",ConnectionState.Partial=>"partial",ConnectionState.SignInRequired=>"signIn",
                ConnectionState.RateLimited=>"rateLimited",ConnectionState.Unsupported=>"unsupported",ConnectionState.Unavailable=>"unavailable",_=>"notChecked" };
            var statusText=L[statusKey]+(state?.IsStale(now)==true?" · "+L["stale"]:"");
            var rows=(state?.LastGood?.Windows??[]).Select(w=> {
                var percent=settings.ShowRemaining?w.RemainingPercent:w.UsedPercent;
                var oldKey=state?.LastGood?.Sources?.Any(s=>s.Id=="key"&&s.State!=ConnectionState.Ready&&s.LastSuccess is not null)==true;
                var label=w.WindowMinutes is >= 10080 || w.Label=="seven_day"?L["weeklyWindow"]:
                    w.WindowMinutes is >0 and <=1440 || w.Label is "five_hour" or "session"?L["sessionWindow"]:
                    w.Label is "daily" or "weekly" or "monthly" or "lifetime"?L[w.Label]:L["additionalWindow"]+" · "+w.Label;
                if(w.Scope is not ("account" or "key" or "codex")) label+=" · "+w.Scope;
                var countdown=w.ResetsAt is { } reset?reset>now?FormatDuration(reset-now):L["awaitingReset"]:L["unknown"];
                var bandColor=settings.Theme=="light"?w.Band switch { "critical"=>"#BA233E","high"=>"#995016","medium"=>"#886511","unknown"=>"#566780",_=>"#087F6E" }:
                    w.Band switch { "critical"=>"#FF7A90","high"=>"#F4A261","medium"=>"#E6C66C","unknown"=>"#7F8B9F",_=>"#54D6B2" };
                return new QuotaRow(label,percent is { } p?$"{p:0.#}% {L[settings.ShowRemaining?"remaining":"used"]}"+(oldKey?" · "+L["stale"]:""):"—",(double)(percent??0),
                    bandColor,
                    L["reset"]+" · "+countdown,w.Scope,w.Label is "five_hour" or "seven_day" or "session" || w.WindowMinutes>0 || w.Id=="key_limit");
            }).ToList();
            var money=(state?.LastGood?.Money??[]).Select(m=>new MoneyRow(m.Id,L[m.Label],$"{m.Value:N2} {m.Currency}"+
                (state?.LastGood?.Sources?.Any(s=>s.Id==(m.Scope=="key"?"key":"credits")&&s.State!=ConnectionState.Ready&&s.LastSuccess is not null)==true?" · "+L["stale"]:""))).ToList();
            var updated=state?.LastGood is { } snapshot?L["updated"]+" "+snapshot.ObservedAt.ToLocalTime().ToString("HH:mm"):L["notChecked"];
            var resetWindow=state?.LastGood?.Windows.Where(w=>w.ResetsAt is not null).OrderBy(w=>w.ResetsAt).FirstOrDefault();
            var resetText=resetWindow?.ResetsAt is { } next?L["reset"]+" · "+(next>now?FormatDuration(next-now):L["awaitingReset"]):"";
            var detail=string.Join("\n",(state?.LastGood?.Sources??[]).Select(s=>L[s.Id=="key"?"apiKey":"balance"]+": "+
                L[s.State==ConnectionState.Ready?"ready":s.State==ConnectionState.SignInRequired?"permissionRequired":s.State==ConnectionState.RateLimited?"rateLimited":"unavailable"]+
                (s.LastSuccess is { } success?" · "+L["updated"]+" "+success.ToLocalTime().ToString("g"):"")));
            var providerColor=settings.Theme=="light"?account.Provider switch { ProviderKind.Claude=>"#975727",ProviderKind.Codex=>"#087F6E",_=>"#6650AD" }:
                account.Provider switch { ProviderKind.Claude=>"#E6AE8C",ProviderKind.Codex=>"#78D8BD",_=>"#A9A0FF" };
            var card=new AccountCard(account.Label,account.Provider.ToString(),providerColor,statusText,updated,rows,money,resetText,settings.Compact,detail);
            Accounts.Add(card);if(!settings.HiddenAccounts.Contains(account.Id)) WidgetAccounts.Add(card);
        }
        WidgetDataChanged?.Invoke();
    }
    private string FormatDuration(TimeSpan value)=>value.TotalDays>=1?$"{(int)value.TotalDays} {L["dayUnit"]} {value.Hours} {L["hourUnit"]}":value.TotalHours>=1?$"{(int)value.TotalHours} {L["hourUnit"]} {value.Minutes} {L["minuteUnit"]}":$"{Math.Max(0,(int)value.TotalMinutes)} {L["minuteUnit"]}";
}
