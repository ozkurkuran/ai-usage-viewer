using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using AiUsageViewer.Core;

namespace AiUsageViewer.App;
public partial class WidgetWindow : Window
{
    private bool locked;
    private bool compact;
    public bool AllowClose { get; set; }
    public event Action? DetailsRequested;
    public WidgetWindow(DashboardViewModel model) { InitializeComponent();DataContext=model; }
    public void ApplySettings(AppSettings settings,bool reposition=false)
    {
        var compactChanged=compact!=settings.Compact;compact=settings.Compact;
        Topmost=settings.AlwaysOnTop;locked=settings.LockPosition;Opacity=Math.Clamp(settings.Opacity,0.35,1);
        ResizeMode=locked?ResizeMode.NoResize:ResizeMode.CanResizeWithGrip;
        if(reposition)
        {
            Width=Math.Clamp(settings.WidgetPlacement.Width,MinWidth,1000);Height=Math.Clamp(settings.WidgetPlacement.Height,MinHeight,1400);
            NativePlacement.Restore(this,settings.WidgetPlacement);
        }
        if(compact&&(compactChanged||reposition)) Height=Math.Clamp(300+settings.Accounts.Count(a=>a.Enabled&&!settings.HiddenAccounts.Contains(a.Id))*160,MinHeight,720);
        else if(compactChanged) Height=Math.Max(Height,640);
    }
    public void EnsureVisible()=>NativePlacement.EnsureVisible(this);
    private void DragHeader(object sender,MouseButtonEventArgs e) { if(!locked && e.LeftButton==MouseButtonState.Pressed) DragMove(); }
    private void HideClick(object sender,RoutedEventArgs e)=>Hide();
    private void DetailsClick(object sender,RoutedEventArgs e)=>DetailsRequested?.Invoke();
    protected override void OnClosing(CancelEventArgs e) { if(!AllowClose) { e.Cancel=true;Hide(); } base.OnClosing(e); }
}
