using System.Collections.ObjectModel;
using AiUsageViewer.Core;
using AiUsageViewer.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiUsageViewer.App;

public sealed record SpendBar(DateOnly Day,decimal Spend,long Requests,decimal Byok,string Label,string Title,string SpendText,string RequestsText,string ByokText);
public sealed record ActivityTableRow(string Day,string Model,string Input,string Output,string Requests,string Spend,string Byok);

// OpenRouter page: account-wide activity for the last 30 completed UTC days, read from the local activity store.
public sealed partial class OpenRouterViewModel(ActivityStore? store,AppSettings settings,Func<Task> refresh,bool demo) : ObservableObject
{
    private AppSettings settings=settings;
    private int generation;
    public Localization L { get; private set; }=new(settings.Language);
    private UiFormat format=UiFormat.For(settings.Language);
    public ObservableCollection<AccountProfile> AccountList { get; }=new(settings.Accounts.Where(a=>a.Provider==ProviderKind.OpenRouter&&a.Enabled));
    [ObservableProperty] private AccountProfile? selectedAccount=settings.Accounts.FirstOrDefault(a=>a.Provider==ProviderKind.OpenRouter&&a.Enabled);
    [ObservableProperty] private string rangeText="";
    [ObservableProperty] private string spend="—";
    [ObservableProperty] private string byok="—";
    [ObservableProperty] private string requests="—";
    [ObservableProperty] private string requestsNote="";
    [ObservableProperty] private string tokens="—";
    [ObservableProperty] private string tokensNote="";
    [ObservableProperty] private string updatedText="";
    [ObservableProperty] private string statusText="";
    [ObservableProperty] private IReadOnlyList<SpendBar> bars=[];
    [ObservableProperty] private IReadOnlyList<ActivityTableRow> rows=[];
    public bool HasAccount=>AccountList.Count>0;
    public bool HasStatus=>StatusText.Length>0;
    public Func<Task> Refresh { get; }=refresh;
    public bool Demo { get; }=demo;
    partial void OnSelectedAccountChanged(AccountProfile? value)=>_=LoadAsync();
    partial void OnStatusTextChanged(string value)=>OnPropertyChanged(nameof(HasStatus));
    public void ApplySettings(AppSettings value)
    {
        settings=value;L=new(value.Language);format=UiFormat.For(value.Language);OnPropertyChanged(nameof(L));
        var selected=SelectedAccount?.Id;AccountList.Clear();
        foreach(var account in value.Accounts.Where(a=>a.Provider==ProviderKind.OpenRouter&&a.Enabled)) AccountList.Add(account);
        OnPropertyChanged(nameof(HasAccount));
        SelectedAccount=AccountList.FirstOrDefault(a=>a.Id==selected)??AccountList.FirstOrDefault();
    }
    // The smoke suite shows a synthetic account without saving it to settings.
    public void UseAccounts(IEnumerable<AccountProfile> accounts)
    {
        AccountList.Clear();foreach(var account in accounts) AccountList.Add(account);
        OnPropertyChanged(nameof(HasAccount));SelectedAccount=AccountList.FirstOrDefault();
    }
    public async Task LoadAsync()
    {
        var ticket=++generation;
        if(SelectedAccount is not { } account||store is null) { Clear(L["addOpenRouter"]);return; }
        var saved=await store.StatusAsync(account.Id);var data=await store.ReadAsync(account.Id);
        if(ticket!=generation) return;
        if(saved?.ConnectionKey!=(account.SecretReference??"")) { Clear(L["notChecked"]);return; }
        var through=saved.Through??DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);var from=through.AddDays(-29);
        var recent=data.Where(r=>r.Day>=from&&r.Day<=through).ToList();
        RangeText=$"{from.ToString("MMM d",format.Culture)} – {through.ToString("MMM d, yyyy",format.Culture)} · UTC";
        Spend=format.Money(recent.Sum(r=>r.Spend));Byok=format.Money(recent.Sum(r=>r.ByokSpend));
        Requests=format.Number(recent.Sum(r=>r.Requests));
        var models=recent.Select(r=>r.Model).Distinct().Count();
        RequestsNote=string.Format(format.Culture,L[models==1?"modelCountOne":"modelCountMany"],models);
        Tokens=format.Number(recent.Sum(r=>r.Input+r.Output));
        TokensNote=string.Format(format.Culture,L["inOut"],format.Number(recent.Sum(r=>r.Input)),format.Number(recent.Sum(r=>r.Output)));
        var byDay=recent.GroupBy(r=>r.Day).ToDictionary(g=>g.Key,g=>g.ToList());
        Bars=Enumerable.Range(0,30).Select(i=>from.AddDays(i)).Select((day,index)=> {
            var dayRows=byDay.GetValueOrDefault(day)??[];
            var spend=dayRows.Sum(r=>r.Spend);var requests=dayRows.Sum(r=>r.Requests);var byok=dayRows.Sum(r=>r.ByokSpend);
            return new SpendBar(day,spend,requests,byok,index%7==0||index==29?day.ToString("MMM d",format.Culture):"",day.ToString("ddd, MMM d",format.Culture),
                format.Money(spend),format.Number(requests),format.Money(byok));
        }).ToList();
        Rows=data.Where(r=>r.Requests>0||r.Spend>0).OrderByDescending(r=>r.Day).ThenByDescending(r=>r.Spend).Select(r=>new ActivityTableRow(r.Day.ToString("ddd, MMM d",format.Culture),r.Model,
            format.Number(r.Input),format.Number(r.Output),format.Number(r.Requests),format.Money(r.Spend,r.Currency,4),format.Money(r.ByokSpend,r.Currency,4))).ToList();
        UpdatedText=saved.LastGood is { } good?string.Format(format.Culture,L["updatedAt"],good.ToLocalTime().ToString("HH:mm",format.Culture)):"";
        StatusText=saved.State==ConnectionState.Ready?"":saved.MessageCode=="history_management_key_required"?L["managementRequired"]:L["stale"];
    }
    private void Clear(string message)
    {
        RangeText="";Spend=Byok=Requests=Tokens="—";RequestsNote=TokensNote=UpdatedText="";Bars=[];Rows=[];StatusText=message;
    }
}
