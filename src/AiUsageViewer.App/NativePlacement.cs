using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AiUsageViewer.Core;
using Forms=System.Windows.Forms;

namespace AiUsageViewer.App;

// Win32 monitor/work-area rectangles are physical pixels. WPF sizes and saved offsets are DIPs.
internal static class NativePlacement
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window,out RECT rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window,nint after,int x,int y,int width,int height,uint flags);
    private const uint NoActivate=0x10,NoZOrder=0x4,NoSize=0x1;
    private static double Scale(nint handle)=>Math.Max(96,GetDpiForWindow(handle))/96d;
    public static WindowPlacement Capture(Window window)
    {
        var handle=new WindowInteropHelper(window).EnsureHandle();GetWindowRect(handle,out var rect);
        var screen=Forms.Screen.FromHandle(handle);var scale=Scale(handle);
        return new(window.Left,window.Top,window.Width,window.Height,screen.DeviceName,
            (rect.Left-screen.WorkingArea.Left)/scale,(rect.Top-screen.WorkingArea.Top)/scale);
    }
    public static void Restore(Window window,WindowPlacement placement)
    {
        var handle=new WindowInteropHelper(window).EnsureHandle();
        var screen=Forms.Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==placement.MonitorDevice)??Forms.Screen.PrimaryScreen??Forms.Screen.AllScreens[0];
        var area=screen.WorkingArea;
        // Move first so GetDpiForWindow observes the destination monitor's DPI.
        SetWindowPos(handle,0,area.Left+20,area.Top+20,0,0,NoActivate|NoZOrder|NoSize);
        var scale=Scale(handle);
        Place(handle,area,area.Left+placement.OffsetX*scale,area.Top+placement.OffsetY*scale,window.Width*scale,window.Height*scale);
    }
    public static void EnsureVisible(Window window)
    {
        var handle=new WindowInteropHelper(window).EnsureHandle();if(!GetWindowRect(handle,out var rect)) return;
        Place(handle,Forms.Screen.FromHandle(handle).WorkingArea,rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top);
    }
    public static void AtCursor(Window window)
    {
        var handle=new WindowInteropHelper(window).EnsureHandle();var cursor=Forms.Cursor.Position;
        var area=Forms.Screen.FromPoint(cursor).WorkingArea;
        SetWindowPos(handle,0,area.Left+20,area.Top+20,0,0,NoActivate|NoZOrder|NoSize);
        var scale=Scale(handle);
        Place(handle,area,cursor.X-window.Width*scale,cursor.Y-window.Height*scale,window.Width*scale,window.Height*scale);
    }
    private static void Place(nint handle,System.Drawing.Rectangle area,double left,double top,double width,double height)
    {
        var w=Math.Clamp((int)Math.Round(width),1,area.Width);var h=Math.Clamp((int)Math.Round(height),1,area.Height);
        var x=Math.Clamp((int)Math.Round(left),area.Left,area.Right-w);var y=Math.Clamp((int)Math.Round(top),area.Top,area.Bottom-h);
        SetWindowPos(handle,0,x,y,w,h,NoActivate|NoZOrder);
    }
}
