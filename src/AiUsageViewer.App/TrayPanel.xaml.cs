using System.ComponentModel;
using System.Windows;

namespace AiUsageViewer.App;
public partial class TrayPanel : Window
{
    public bool AllowClose { get; set; }
    public event Action? DetailsRequested;
    public event Action? QuitRequested;
    public TrayPanel(DashboardViewModel model)
    {
        InitializeComponent();DataContext=model;Deactivated+=(_,_)=>Hide();
        Loaded+=(_,_)=>AccountScroll.MaxHeight=Math.Max(200,SystemParameters.WorkArea.Height-260);
    }
    private void DetailsClick(object sender,RoutedEventArgs e) { Hide();DetailsRequested?.Invoke(); }
    private void SettingsClick(object sender,RoutedEventArgs e) { Hide();((DashboardViewModel)DataContext).OpenSettingsCommand.Execute(null); }
    private void QuitClick(object sender,RoutedEventArgs e)=>QuitRequested?.Invoke();
    public void ShowAtCursor()
    {
        // Measure off-screen first so the flyout can sit fully above the cursor.
        Left=-10000;Top=-10000;Show();UpdateLayout();
        NativePlacement.AtCursor(this);Activate();
    }
    protected override void OnClosing(CancelEventArgs e) { if(!AllowClose) { e.Cancel=true;Hide(); } base.OnClosing(e); }
}
