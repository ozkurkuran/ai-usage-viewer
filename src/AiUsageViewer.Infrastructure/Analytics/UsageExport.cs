using System.Globalization;
using System.Text;
using System.Text.Json;
using AiUsageViewer.Core;

namespace AiUsageViewer.Infrastructure.Analytics;
public static class UsageExport
{
    public static async Task WriteAsync(string path,IReadOnlyList<UsageEvent> events,CancellationToken ct=default)
    {
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            if(Path.GetExtension(path).Equals(".csv",StringComparison.OrdinalIgnoreCase))
            {
                await using var writer=new StreamWriter(temporary,false,new UTF8Encoding(true));
                await writer.WriteLineAsync("timestamp_utc,tool,model,project,session,input,output,cache_read,cache_write_5m,cache_write_1h,reasoning_subset,identity_quality");
                foreach(var e in events)
                {
                    ct.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(string.Join(",",new[]{e.Timestamp.ToUniversalTime().ToString("O"),e.Provider.ToString(),e.Model,e.Project,e.SessionId,
                        e.Tokens.Input.ToString(CultureInfo.InvariantCulture),e.Tokens.Output.ToString(CultureInfo.InvariantCulture),e.Tokens.CacheRead.ToString(CultureInfo.InvariantCulture),
                        e.Tokens.CacheWrite5m.ToString(CultureInfo.InvariantCulture),e.Tokens.CacheWrite1h.ToString(CultureInfo.InvariantCulture),e.Tokens.Reasoning.ToString(CultureInfo.InvariantCulture),e.Identity.ToString()}.Select(Cell)));
                }
            }
            else await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(new { schemaVersion=1,scope="local_device",events },new JsonSerializerOptions { WriteIndented=true }),ct);
            File.Move(temporary,path,true);
        }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string Cell(string value)
    {
        if(value.Length>0&&"=+-@\t\r\n".Contains(value[0])) value="'"+value;
        return "\""+value.Replace("\"","\"\"")+"\"";
    }
}
