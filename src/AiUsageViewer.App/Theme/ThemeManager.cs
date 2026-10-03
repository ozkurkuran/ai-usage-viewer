using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AiUsageViewer.App;

// Swaps the color token dictionary (Dark / Light / System) and keeps native title bars in step.
public static class ThemeManager
{
    public static bool IsDark { get; private set; }=true;
    public static string Resolve(string theme)=>theme switch { "light"=>"light","system"=>SystemUsesLight()?"light":"dark",_=>"dark" };
    public static void Apply(string theme)
    {
        IsDark=Resolve(theme)=="dark";
        var merged=System.Windows.Application.Current.Resources.MergedDictionaries;
        merged[0]=new ResourceDictionary { Source=new Uri($"pack://application:,,,/Theme/Tokens.{(IsDark?"Dark":"Light")}.xaml",UriKind.Absolute) };
        foreach(Window window in System.Windows.Application.Current.Windows) ApplyTitleBar(window);
    }
    public static void Register()=>EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>ApplyTitleBar((Window)sender)));
    private static void ApplyTitleBar(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;if(handle==0) return;
        var dark=IsDark?1:0;
        try { _=DwmSetWindowAttribute(handle,20,ref dark,sizeof(int)); } catch(DllNotFoundException) {} catch(EntryPointNotFoundException) {}
    }
    private static bool SystemUsesLight()
    {
        using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value&&value!=0;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
}
