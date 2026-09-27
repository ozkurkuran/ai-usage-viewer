using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AiUsageViewer.Core;
using AiUsageViewer.Infrastructure.Storage;

namespace AiUsageViewer.App;
public sealed class ActivityWindow : Window
{
    private readonly ActivityStore store;
    private readonly Localization l;
    private readonly ComboBox account=new() { DisplayMemberPath="Label",MinWidth=180 };
    private readonly TextBlock status=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,0) };
    private readonly ObservableCollection<Row> rows=[];
    private int generation;
    private sealed record Row(string Day,string Model,string Input,string Output,string Requests,string Spend,string Byok);
    public ActivityWindow(AppSettings settings,ActivityStore store,Func<Task> refresh)
    {
        this.store=store;l=new(settings.Language);Title="OpenRouter · "+l["activity"];
        Style=(Style)FindResource(typeof(Window));Width=1000;Height=620;MinWidth=750;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel { Margin=new Thickness(24) };Content=root;
        var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock { Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text=l["activityScope"],TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,18) });
        var controls=new StackPanel { Orientation=Orientation.Horizontal };header.Children.Add(controls);
        account.ItemsSource=settings.Accounts.Where(a=>a.Provider==ProviderKind.OpenRouter&&a.Enabled).ToList();controls.Children.Add(account);
        var button=new Button { Content=l["refresh"],Margin=new Thickness(12,0,0,0) };controls.Children.Add(button);
        button.Click+=async(_,_)=> { button.IsEnabled=false;try { await refresh();await LoadAsync(); } catch(Exception ex) when(ex is IOException or Microsoft.Data.Sqlite.SqliteException) { status.Text=l["unavailable"]; } finally { button.IsEnabled=true; } };
        DockPanel.SetDock(status,Dock.Bottom);root.Children.Add(status);
        var grid=new DataGrid { ItemsSource=rows,Margin=new Thickness(0,16,0,0) };root.Children.Add(grid);
        foreach(var (title,path) in new[]{(l["date"],"Day"),(l["models"],"Model"),("Input","Input"),("Output","Output"),(l["requests"],"Requests"),(l["reportedSpend"],"Spend"),("BYOK","Byok")})
            grid.Columns.Add(new DataGridTextColumn { Header=title,Binding=new Binding(path),Width=path=="Model"?new DataGridLength(1,DataGridLengthUnitType.Star):DataGridLength.Auto });
        account.SelectionChanged+=async(_,_)=> { try { await LoadAsync(); } catch(Exception ex) when(ex is IOException or Microsoft.Data.Sqlite.SqliteException) { status.Text=l["unavailable"]; } };account.SelectedIndex=0;
        if(account.SelectedItem is null) status.Text=l["addOpenRouter"];
    }
    internal async Task LoadAsync()
    {
        if(account.SelectedItem is not AccountProfile selected) return;
        var ticket=++generation;var saved=await store.StatusAsync(selected.Id);var data=await store.ReadAsync(selected.Id);
        if(ticket!=generation) return;rows.Clear();
        if(saved?.ConnectionKey!=(selected.SecretReference??"")) { status.Text=l["notChecked"];return; }
        foreach(var row in data) rows.Add(new(row.Day.ToString("yyyy-MM-dd"),row.Model,row.Input.ToString("N0"),row.Output.ToString("N0"),row.Requests.ToString("N0"),$"{row.Spend:N4} USD",$"{row.ByokSpend:N4} USD"));
        status.Text=(saved?.State==ConnectionState.Ready?l["ready"]:saved?.MessageCode=="history_management_key_required"?l["managementRequired"]:l["stale"])+
            (saved?.LastGood is { } good?$" · {l["updated"]}: {good.ToLocalTime():g} · UTC {saved.From:yyyy-MM-dd} – {saved.Through:yyyy-MM-dd}":"");
    }
}
