using System.ComponentModel;
using System.Windows;

namespace AiUsageViewer.App;
public partial class TrayPanel : Window
{
    public bool AllowClose { get; set; }
    public event Action? DetailsRequested;
    public TrayPanel(DashboardViewModel model) { InitializeComponent();DataContext=model;Deactivated+=(_,_)=>Hide(); }
    private void DetailsClick(object sender,RoutedEventArgs e) { Hide();DetailsRequested?.Invoke(); }
    public void ShowAtCursor()
    {
        NativePlacement.AtCursor(this);
        Show();Activate();
    }
    protected override void OnClosing(CancelEventArgs e) { if(!AllowClose) { e.Cancel=true;Hide(); } base.OnClosing(e); }
}
