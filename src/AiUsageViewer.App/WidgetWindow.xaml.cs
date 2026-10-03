using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using AiUsageViewer.Core;

namespace AiUsageViewer.App;
public partial class WidgetWindow : Window
{
    private bool locked;
    public bool AllowClose { get; set; }
    public event Action? DetailsRequested;
    public WidgetWindow(DashboardViewModel model)
    {
        InitializeComponent();DataContext=model;
        Loaded+=(_,_)=>LimitHeight();
    }
    // Width follows the layout (360 standard, 300 compact); height follows the content.
    public void ApplySettings(AppSettings settings,bool reposition=false)
    {
        Topmost=settings.AlwaysOnTop;locked=settings.LockPosition;Opacity=Math.Clamp(settings.Opacity,0.35,1);
        Width=settings.Compact?300:360;
        if(reposition) NativePlacement.Restore(this,settings.WidgetPlacement);
        LimitHeight();
    }
    // Content scrolls only when it would not fit on the monitor's work area.
    private void LimitHeight()
    {
        var area=SystemParameters.WorkArea.Height;
        AccountScroll.MaxHeight=Math.Max(160,area-300);CompactScroll.MaxHeight=Math.Max(120,area-120);
    }
    internal bool AccountsScroll=>(Standard.IsVisible?AccountScroll:CompactScroll).ComputedVerticalScrollBarVisibility==Visibility.Visible;
    public void EnsureVisible()=>NativePlacement.EnsureVisible(this);
    private void DragHeader(object sender,MouseButtonEventArgs e) { if(!locked && e.LeftButton==MouseButtonState.Pressed&&e.OriginalSource is not System.Windows.Controls.Button) DragMove(); }
    private void HideClick(object sender,RoutedEventArgs e)=>Hide();
    private void DetailsClick(object sender,RoutedEventArgs e)=>DetailsRequested?.Invoke();
    protected override void OnClosing(CancelEventArgs e) { if(!AllowClose) { e.Cancel=true;Hide(); } base.OnClosing(e); }
}
