using System.ComponentModel;
using System.Windows;

namespace AiUsageViewer.App;
public partial class DashboardWindow : Window
{
    public bool AllowClose { get; set; }
    public DashboardWindow(DashboardViewModel model)
    {
        InitializeComponent();DataContext=model;UpdateHeaders();
        model.PropertyChanged+=(_,e)=> { if(e.PropertyName==nameof(model.L)) UpdateHeaders(); };
        void UpdateHeaders() { UsageGrid.Columns[0].Header=model.L["name"];UsageGrid.Columns[1].Header=model.L["total"]; }
    }
    protected override void OnClosing(CancelEventArgs e) { if(!AllowClose) { e.Cancel=true;Hide(); } base.OnClosing(e); }
    private void OpenGroup(object sender,System.Windows.Input.MouseButtonEventArgs e)
    { if(UsageGrid.SelectedItem is GroupRow row) ((DashboardViewModel)DataContext).OpenGroupCommand.Execute(row); }
    private void GroupKeyDown(object sender,System.Windows.Input.KeyEventArgs e)
    { if(e.Key==System.Windows.Input.Key.Enter&&UsageGrid.SelectedItem is GroupRow row) { ((DashboardViewModel)DataContext).OpenGroupCommand.Execute(row);e.Handled=true; } }
}
