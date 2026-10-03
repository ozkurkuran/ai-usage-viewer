using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace AiUsageViewer.App;
public partial class DashboardWindow : Window
{
    public bool AllowClose { get; set; }
    private readonly DashboardViewModel model;
    public DashboardWindow(DashboardViewModel model)
    {
        InitializeComponent();DataContext=this.model=model;
        model.PropertyChanged+=(_,e)=> { if(e.PropertyName is nameof(model.ShowGettingStarted) or nameof(model.Page) or nameof(model.L) or nameof(model.HasPeriodUsage)) UpdateState(); };
        PageScroll.SizeChanged+=(_,_)=>UpdateLayoutMode();
        UpdateState();
    }
    // First run swaps live values for placeholders; the export action follows the page.
    private void UpdateState()
    {
        var firstRun=model.ShowGettingStarted;
        Hero.SetResourceReference(TextBlock.ForegroundProperty,firstRun?"Text.Disabled":"Text.Primary");
        Cost.SetResourceReference(TextBlock.ForegroundProperty,firstRun?"Text.Disabled":"Accent.TextStrong");
        Mix.Visibility=firstRun?Visibility.Collapsed:Visibility.Visible;
        HeroNote.Visibility=firstRun?Visibility.Visible:Visibility.Collapsed;
        HistoryEmpty.Visibility=firstRun?Visibility.Visible:Visibility.Collapsed;
        History.Visibility=firstRun?Visibility.Hidden:Visibility.Visible;
        HistoryLegend.Visibility=firstRun?Visibility.Collapsed:Visibility.Visible;
        LowerCards.Visibility=firstRun?Visibility.Collapsed:Visibility.Visible;
        FilterRow.Visibility=model.Page is "overview" or "models" or "projects" or "sessions"&&!firstRun?Visibility.Visible:Visibility.Collapsed;
        ExportButton.Content=model.L[model.Page=="overview"?"export":"exportCsv"];
        ExportButton.Visibility=firstRun&&model.Page=="overview"||model.Page=="subscriptions"?Visibility.Collapsed:Visibility.Visible;
        AutomationProperties.SetName(ExportButton,(string)ExportButton.Content);
    }
    // Two columns with a 340 px rail; one column when the page is narrow.
    private void UpdateLayoutMode()
    {
        var width=PageScroll.ActualWidth-64;
        var narrow=width<940;
        Grid.SetColumn(Rail,narrow?0:2);Grid.SetRow(Rail,narrow?1:0);Grid.SetColumnSpan(MainColumn,narrow?3:1);
        Rail.Margin=new Thickness(0,narrow?24:0,0,0);
        var stackCards=(narrow?width:width-360)<660;
        Grid.SetColumn(ModelCard,stackCards?0:2);Grid.SetRow(ModelCard,stackCards?1:0);Grid.SetColumnSpan(HeatCard,stackCards?3:1);Grid.SetColumnSpan(ModelCard,stackCards?3:1);
        ModelCard.Margin=new Thickness(0,stackCards?20:0,0,0);
        SummaryCards.Columns=width<820?2:4;
    }
    protected override void OnClosing(CancelEventArgs e) { if(!AllowClose) { e.Cancel=true;Hide(); } base.OnClosing(e); }
    private void OpenGroup(object sender,System.Windows.Input.MouseButtonEventArgs e)
    { if(UsageGrid.SelectedItem is GroupRow row) model.OpenGroupCommand.Execute(row); }
    private void GroupKeyDown(object sender,System.Windows.Input.KeyEventArgs e)
    { if(e.Key==System.Windows.Input.Key.Enter&&UsageGrid.SelectedItem is GroupRow row) { model.OpenGroupCommand.Execute(row);e.Handled=true; } }
}
