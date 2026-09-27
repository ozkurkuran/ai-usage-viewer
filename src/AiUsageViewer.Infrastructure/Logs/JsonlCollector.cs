using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AiUsageViewer.Core;
using AiUsageViewer.Infrastructure.Storage;
using Microsoft.Win32.SafeHandles;

namespace AiUsageViewer.Infrastructure.Logs;

public sealed class JsonlCollector(UsageDatabase database) : IUsageCollector, IDisposable
{
    private readonly SemaphoreSlim scanLock = new(1,1);
    private readonly LogHandles handles=new();
    public async Task<CollectionProgress> CollectAsync(IReadOnlyList<SourceLocation> sources, CancellationToken cancellationToken)
    {
        await scanLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await Task.Run(()=>ScanAsync(sources,cancellationToken),cancellationToken).ConfigureAwait(false); }
        finally { scanLock.Release(); }
    }

    private async Task<CollectionProgress> ScanAsync(IReadOnlyList<SourceLocation> sources, CancellationToken ct)
    {
        int visited=0,changed=0,count=0,invalid=0,uncertain=0;
        long bytesRead=0;
        var totalStarted=Stopwatch.GetTimestamp();
        var timing=new Dictionary<string,double>();
        void Record(string name,long started)=>timing[name]=timing.GetValueOrDefault(name)+Stopwatch.GetElapsedTime(started).TotalSeconds;
        var warnings=new HashSet<string>();
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dbStarted=Stopwatch.GetTimestamp();
        var cursors=await database.ReadCursorsAsync(ct);Record("database_read",dbStarted);
        foreach(var source in sources.Where(s=>s.Enabled && s.Provider is ProviderKind.Claude or ProviderKind.Codex))
        {
            if (!Directory.Exists(source.Directory)) { if(!source.Optional) warnings.Add("source_missing:"+source.Id); continue; }
            var options=new EnumerationOptions { RecurseSubdirectories=true,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint };
            foreach(var path in Directory.EnumerateFiles(source.Directory,"*.jsonl",options))
            {
                ct.ThrowIfCancellationRequested();
                var normalized=Path.GetFullPath(path);
                if(!seen.Add(normalized)) continue;
                visited++;
                try
                {
                    var metadataStarted=Stopwatch.GetTimestamp();
                    var info=new FileInfo(normalized);
                    var length=info.Length;
                    var modified=info.LastWriteTimeUtc.Ticks;
                    var identity=LogHandles.Identity(normalized);
                    Record("metadata",metadataStarted);
                    cursors.TryGetValue(normalized,out var cursor);
                    if(cursor is not null && cursor.FileIdentity==identity && cursor.ScanComplete && cursor.ParserVersion==UsageLogParser.Version && cursor.Length==length && cursor.ModifiedTicks==modified) continue;
                    changed++;
                    var opened=Stopwatch.GetTimestamp();var handle=handles.Get(normalized,identity);Record("content_handle",opened);
                    var reusable=cursor is not null && cursor.ParserVersion==UsageLogParser.Version && cursor.Offset<=length &&
                        (cursor.FileIdentity is null || cursor.FileIdentity==identity) &&
                        !(cursor.Length==length && cursor.ModifiedTicks!=modified) &&
                        await TimedFingerprintAsync(handle,0,cursor.PrefixLength)==cursor.PrefixHash &&
                        await TimedFingerprintAsync(handle,Math.Max(0,cursor.Offset-128),(int)Math.Min(128,cursor.Offset))==cursor.TailHash;
                    var offset=reusable?cursor!.Offset:0;
                    var state=reusable?cursor!.State:new ParserState { SessionId=Path.GetFileNameWithoutExtension(path) };
                    var prefixLength=(int)Math.Min(256,length);
                    var prefix=await TimedFingerprintAsync(handle,0,prefixLength);
                    var events=new List<UsageEvent>();
                    var sourceIdentity=UsageLogParser.Hash(normalized.ToLowerInvariant());
                    var checkpoint=offset;
                    var initialOffset=offset;var linesStarted=Stopwatch.GetTimestamp();
                    await foreach(var line in ReadLinesAsync(handle,offset,length,ct))
                    {
                        var parsed=line.Text is null?new ParsedLine(Invalid:true):UsageLogParser.Parse(source.Provider,line.Text,state,sourceIdentity,line.Start);
                        if(parsed.Invalid) invalid++;
                        if(parsed.Event is { } e)
                        {
                            events.Add(e); count++;
                            if(e.Identity==IdentityQuality.UncertainFork) uncertain++;
                        }
                        offset=line.End;
                        if(events.Count>=512 || offset-checkpoint>=4*1024*1024)
                        {
                            await CommitAsync(false); events.Clear(); checkpoint=offset;
                        }
                    }
                    Record("line_loop_including_checkpoints",linesStarted);bytesRead+=offset-initialOffset;
                    await CommitAsync(true);
                    async Task CommitAsync(bool complete)
                    {
                        var tail=await TimedFingerprintAsync(handle,Math.Max(0,offset-128),(int)Math.Min(128,offset));
                        var commitStarted=Stopwatch.GetTimestamp();
                        await database.CommitBatchAsync(events,new(normalized,offset,length,modified,prefixLength,prefix,tail,state with {},ScanComplete:complete,FileIdentity:identity),ct);
                        Record("database_write",commitStarted);
                    }
                }
                catch(IOException) { warnings.Add("source_busy:"+source.Id); }
                catch(UnauthorizedAccessException) { warnings.Add("source_access_denied:"+source.Id); }
            }
        }
        Record("total",totalStarted);
        return new(visited,changed,count,invalid,uncertain,warnings.ToList(),bytesRead,timing);

        async Task<string> TimedFingerprintAsync(SafeFileHandle handle,long offset,int length)
        {
            var started=Stopwatch.GetTimestamp();var result=await FingerprintAsync(handle,offset,length,ct);
            Record("fingerprints",started);return result;
        }
    }

    private sealed record Line(long Start,long End,string? Text);
    private static async IAsyncEnumerable<Line> ReadLinesAsync(SafeFileHandle handle,long offset,long limit,[EnumeratorCancellation] CancellationToken ct)
    {
        var buffer=new byte[64*1024];
        using var line=new MemoryStream();
        var position=offset;
        var start=offset;
        var oversized=false;
        while(position<limit)
        {
            var read=await RandomAccess.ReadAsync(handle,buffer.AsMemory(0,(int)Math.Min(buffer.Length,limit-position)),position,ct);
            if(read==0) break;
            var segment=0;
            while(segment<read)
            {
                var newline=Array.IndexOf(buffer,(byte)10,segment,read-segment);
                var end=newline<0?read:newline;
                var count=end-segment;
                if(!oversized)
                {
                    if(line.Length+count>8*1024*1024) { oversized=true;line.SetLength(0); }
                    else line.Write(buffer,segment,count);
                }
                position+=count;
                if(newline>=0)
                {
                    position++;
                    yield return new(start,position,oversized?null:Encoding.UTF8.GetString(line.GetBuffer(),0,(int)line.Length).TrimEnd('\r'));
                    line.SetLength(0); oversized=false; start=position;
                }
                segment=end+1;
            }
            ct.ThrowIfCancellationRequested();
        }
        // An unterminated last line is retried on the next append, never committed.
    }

    private static async Task<string> FingerprintAsync(SafeFileHandle handle,long offset,int count,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var bytes=new byte[count];
        var read=0;
        while(read<count) { var n=await RandomAccess.ReadAsync(handle,bytes.AsMemory(read),offset+read,ct);if(n==0) break;read+=n; }
        return Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0,read)));
    }
    public void Dispose() { scanLock.Wait();try { handles.Dispose(); } finally { scanLock.Release(); } }
}
