using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AiUsageViewer.Infrastructure.Logs;

// Metadata-only handles identify replaced files without opening their contents.
// Content handles are bounded and reused; some active logs have slow read-open
// latency on Windows. RandomAccess reads bypass stale FileStream read buffers.
internal sealed class LogHandles : IDisposable
{
    private sealed record Entry(SafeFileHandle Handle,string Identity,DateTimeOffset Used);
    private readonly Dictionary<string,Entry> entries=new(StringComparer.OrdinalIgnoreCase);
    public SafeFileHandle Get(string path,string identity)
    {
        if(entries.TryGetValue(path,out var entry))
        {
            if(entry.Identity==identity) { entries[path]=entry with { Used=DateTimeOffset.UtcNow };return entry.Handle; }
            entry.Handle.Dispose();entries.Remove(path);
        }
        foreach(var expired in entries.Where(x=>DateTimeOffset.UtcNow-x.Value.Used>TimeSpan.FromMinutes(3)).Select(x=>x.Key).ToArray())
        { entries[expired].Handle.Dispose();entries.Remove(expired); }
        if(entries.Count>=32)
        {
            var oldest=entries.MinBy(x=>x.Value.Used);oldest.Value.Handle.Dispose();entries.Remove(oldest.Key);
        }
        var handle=File.OpenHandle(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,FileOptions.Asynchronous|FileOptions.SequentialScan);
        try
        {
            if(Identity(handle)!=identity) throw new IOException("Log was replaced while opening.");
            entries[path]=new(handle,identity,DateTimeOffset.UtcNow);return handle;
        }
        catch { handle.Dispose();throw; }
    }
    public static string Identity(string path)
    {
        var full=Path.GetFullPath(path);
        if(!full.StartsWith(@"\\?\",StringComparison.Ordinal)) full=full.StartsWith(@"\\",StringComparison.Ordinal)?@"\\?\UNC\"+full[2..]:@"\\?\"+full;
        using var metadata=CreateFile(full,0,7,IntPtr.Zero,3,128,IntPtr.Zero);
        if(metadata.IsInvalid) throw new IOException("Log metadata unavailable.",new Win32Exception(Marshal.GetLastWin32Error()));
        return Identity(metadata);
    }
    private static string Identity(SafeFileHandle handle)
    {
        if(!GetFileInformationByHandle(handle,out var info)) throw new IOException("Log identity unavailable.");
        return $"{info.Volume:X8}{info.IndexHigh:X8}{info.IndexLow:X8}{info.CreatedHigh:X8}{info.CreatedLow:X8}";
    }
    public void Dispose() { foreach(var entry in entries.Values) entry.Handle.Dispose();entries.Clear(); }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfoNative
    {
        public uint Attributes,CreatedLow,CreatedHigh,AccessLow,AccessHigh,WriteLow,WriteHigh,Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;
    }
    [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle,out FileInfoNative information);
}
