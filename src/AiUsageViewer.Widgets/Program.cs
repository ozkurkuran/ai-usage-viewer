using System.Runtime.InteropServices;
using AiUsageViewer.Application.WindowsWidgets;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace AiUsageViewer.Widgets;

internal static class Program
{
    private static string? probeDirectory;
    [MTAThread]
    private static int Main(string[] args)
    {
        var initialized = CoInitializeEx(0, 0);
        if (initialized < 0) return 4;
        try { return Run(args); }
        finally { CoUninitialize(); }
    }

    private static int Run(string[] args)
    {
        var probeIndex = Array.IndexOf(args, "--probe-dir");
        probeDirectory = probeIndex >= 0 && probeIndex + 1 < args.Length ? args[probeIndex + 1] : null;
        // Package test calls this explicitly; no provider registration or account access.
        if (args.Contains("--validate-runtime"))
        {
            try { _ = new WidgetUpdateRequestOptions("runtime-probe"); Probe("runtime", 0); return 0; }
            catch (Exception ex) { Probe("runtime", ex.HResult); return 2; }
        }
        if (args.Contains("--validate-provider"))
        {
            nint pointer = 0;
            try
            {
                Marshal.ThrowExceptionForHR(CoCreateInstance(Guid.Parse(WidgetCard.ProviderClassId), 0, 4,
                    GuidGenerator.GetIID(typeof(IWidgetProvider)), out pointer));
                Probe("provider", 0);
                return 0;
            }
            catch (Exception ex) { Probe("provider", ex.HResult); return 3; }
            finally { if (pointer != 0) Marshal.Release(pointer); }
        }
        if (args.Contains("--validate-factory"))
        {
            var iid = GuidGenerator.GetIID(typeof(IWidgetProvider));
            var result = new WidgetFactory().CreateInstance(0, ref iid, out var pointer);
            Probe("factory", result);
            if (pointer != 0) Marshal.Release(pointer);
            return result == 0 ? 0 : 5;
        }
        if (args.Contains("--validate-class-factory"))
        {
            var unknown = Marshal.GetIUnknownForObject(new WidgetFactory());
            try
            {
                var result = Marshal.QueryInterface(unknown, typeof(IClassFactory).GUID, out var pointer);
                Probe("class-factory", result);
                if (pointer != 0) Marshal.Release(pointer);
                return result == 0 ? 0 : 6;
            }
            finally { Marshal.Release(unknown); }
        }
        var exportIndex = Array.IndexOf(args, "--export-preview");
        if (exportIndex >= 0 && exportIndex + 1 < args.Length)
        {
            var directory = args[exportIndex + 1]; Directory.CreateDirectory(directory);
            var now = DateTimeOffset.UtcNow;
            var snapshot = new WidgetSnapshot(1, "en", DateOnly.FromDateTime(DateTime.Now), now, now, "123,456", "≈ 0.80 USD",
                [new("Claude Pro", "Ready", ["Session: 38% used", "Weekly: 54% used"]),
                 new("ChatGPT Plus", "Ready", ["Session: 21% used", "Weekly: 67% used"])], true);
            foreach (var size in Enum.GetValues<BoardWidgetSize>())
                File.WriteAllText(Path.Combine(directory, size.ToString().ToLowerInvariant() + ".json"), WidgetCard.Render(snapshot, size, now));
            return 0;
        }
        uint cookie = 0;
        var factory = new WidgetFactory();
        try
        {
            Marshal.ThrowExceptionForHR(CoRegisterClassObject(Guid.Parse(WidgetCard.ProviderClassId),
                factory, 4, 1, out cookie));
            WidgetProvider.Empty.WaitOne();
            return 0;
        }
        catch (Exception ex) when (ex is COMException or TypeInitializationException) { return 1; }
        finally { if (cookie != 0) CoRevokeClassObject(cookie); GC.KeepAlive(factory); }
    }

    internal static void Probe(string kind, int hresult, string? stage = null)
    {
        var directory = probeDirectory ?? WidgetSnapshotFile.DefaultDirectory; Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "widget-" + kind + "-probe.json"),
            System.Text.Json.JsonSerializer.Serialize(new { hresult = "0x" + hresult.ToString("X8"),
                iid = GuidGenerator.GetIID(typeof(IWidgetProvider)), reflectedIid = typeof(IWidgetProvider).GUID, stage }));
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint apartment);
    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject([MarshalAs(UnmanagedType.LPStruct)] Guid clsid,
        [MarshalAs(UnmanagedType.IUnknown)] object factory, uint context, uint flags, out uint cookie);
    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint cookie);
    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance([MarshalAs(UnmanagedType.LPStruct)] Guid clsid, nint outer,
        uint context, [MarshalAs(UnmanagedType.LPStruct)] Guid iid, out nint instance);
}

[ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IClassFactory
{
    [PreserveSig] int CreateInstance(nint outer, ref Guid iid, out nint instance);
    [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool locked);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class WidgetFactory : IClassFactory
{
    private WidgetProvider? provider;
    private readonly object gate = new();
    public int CreateInstance(nint outer, ref Guid iid, out nint instance)
    {
        instance = 0;
        if (outer != 0) return unchecked((int)0x80040110);
        nint inspectable = 0;
        var stage = "construct";
        try
        {
            lock (gate)
            {
                provider ??= new WidgetProvider();
                stage = "marshal";
                inspectable = MarshalInspectable<IWidgetProvider>.FromManaged(provider);
            }
            stage = "query";
            var result = Marshal.QueryInterface(inspectable, in iid, out instance);
            if (result != 0) Program.Probe("factory-error", result, stage);
            return result;
        }
        catch (Exception ex) { Program.Probe("factory-error", ex.HResult, stage); return Marshal.GetHRForException(ex); }
        finally { if (inspectable != 0) Marshal.Release(inspectable); }
    }
    public int LockServer(bool locked) => 0;
}
