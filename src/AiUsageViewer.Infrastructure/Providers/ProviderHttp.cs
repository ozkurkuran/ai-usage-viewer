using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AiUsageViewer.Core;

namespace AiUsageViewer.Infrastructure.Providers;

public sealed record HttpOutcome(int Status, JsonElement Data = default, TimeSpan? RetryAfter = null, string? Error = null)
{
    public bool Success => Status is >= 200 and < 300 && Error is null;
    public ProviderResult Failure => new(Status switch { 401 or 403 => ConnectionState.SignInRequired,
        429 => ConnectionState.RateLimited, _ => ConnectionState.Unavailable }, MessageCode:Error ?? "http_"+Status, RetryAfter:RetryAfter);
}

public sealed class ProviderHttp : IDisposable
{
    private readonly HttpClient client;
    public ProviderHttp(HttpMessageHandler? handler = null) => client = new(handler ?? new SocketsHttpHandler {
        AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate
    }) { Timeout=TimeSpan.FromSeconds(25) };

    public async Task<HttpOutcome> GetAsync(string url, string secret, CancellationToken ct,
        IReadOnlyDictionary<string,string>? headers=null)
    {
        ct.ThrowIfCancellationRequested();
        if(string.IsNullOrWhiteSpace(secret)) return new(401,Error:"invalid_credential");
        secret=secret.Trim();
        if(secret.Length>16384||secret.Any(c=>c<'!'||c>'~')) return new(401,Error:"invalid_credential");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        var token=deadline.Token;
        using var request=new HttpRequestMessage(HttpMethod.Get,url);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",secret);
        request.Headers.Accept.Add(new("application/json"));
        request.Headers.UserAgent.ParseAdd("AIUsageViewer/0.3");
        if(headers is not null) foreach(var pair in headers) request.Headers.TryAddWithoutValidation(pair.Key,pair.Value);
        try
        {
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
            var delay=response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date?date-DateTimeOffset.UtcNow:null);
            if(!response.IsSuccessStatusCode) return new((int)response.StatusCode,RetryAfter:delay);
            const int maximum=2*1024*1024;
            if(response.Content.Headers.ContentLength>maximum) return new(0,Error:"response_too_large");
            await using var input=await response.Content.ReadAsStreamAsync(token);
            using var buffer=new MemoryStream(); var chunk=new byte[16*1024];
            int read;
            while((read=await input.ReadAsync(chunk,token))!=0)
            {
                if(buffer.Length+read>maximum) return new(0,Error:"response_too_large");
                buffer.Write(chunk,0,read);
            }
            using var json=JsonDocument.Parse(buffer.ToArray());
            return new((int)response.StatusCode,json.RootElement.Clone());
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested) { return new(0,Error:"timeout"); }
        catch(HttpRequestException) { return new(0,Error:"network_unavailable"); }
        catch(JsonException) { return new(0,Error:"invalid_response"); }
        catch(IOException) { return new(0,Error:"network_unavailable"); }
    }
    public void Dispose()=>client.Dispose();
}
