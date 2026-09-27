using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiUsageViewer.Core;

namespace AiUsageViewer.Infrastructure.Logs;

public sealed record ParserState
{
    public string SessionId { get; set; } = "unknown";
    public string Project { get; set; } = "unknown";
    public string Model { get; set; } = "unknown";
    public string? TurnId { get; set; }
    public bool Fork { get; set; }
    public TokenUsage? Cumulative { get; set; }
    public int Epoch { get; set; }
}

public sealed record ParsedLine(UsageEvent? Event = null, bool Invalid = false);

public static class UsageLogParser
{
    public const int Version = 1;
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static ParsedLine Parse(ProviderKind provider, string line, ParserState state, string sourceId, long offset)
    {
        if (string.IsNullOrWhiteSpace(line)) return new();
        // Most rollout bytes are messages and tool output, which are never needed.
        // Skip them before JSON allocation while preserving metadata and usage lines.
        if (provider == ProviderKind.Codex && !line.Contains("\"session_meta\"",StringComparison.Ordinal) &&
            !line.Contains("\"turn_context\"",StringComparison.Ordinal) && !line.Contains("\"token_count\"",StringComparison.Ordinal) && line.StartsWith('{')) return new();
        if (provider == ProviderKind.Claude && !line.Contains("\"usage\"",StringComparison.Ordinal) && line.StartsWith('{') && line.EndsWith('}')) return new();
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            return provider switch
            {
                ProviderKind.Claude => Claude(root, state, sourceId, offset),
                ProviderKind.Codex => Codex(root, state, sourceId, offset),
                _ => new()
            };
        }
        catch (JsonException) { return new(Invalid: true); }
    }

    private static ParsedLine Claude(JsonElement root, ParserState state, string source, long offset)
    {
        state.SessionId = root.Get("sessionId").Text() ?? state.SessionId;
        state.Project = ProjectName(root.Get("cwd").Text(), state.Project);
        if (root.Get("type").Text() != "assistant") return new();
        var message = root.Get("message");
        var usage = message.Get("usage");
        if (usage.ValueKind != JsonValueKind.Object) return new();
        var model = message.Get("model").Text() ?? "unknown";
        if (model.StartsWith('<')) return new();
        if (root.Get("timestamp").Time() is not { } timestamp) return new(Invalid: true);
        var cache = usage.Get("cache_creation");
        var longWrite = cache.Get("ephemeral_1h_input_tokens").Count();
        var writes = Math.Max(usage.Get("cache_creation_input_tokens").Count(),
            longWrite + cache.Get("ephemeral_5m_input_tokens").Count());
        var output = usage.Get("output_tokens").Count();
        var tokens = new TokenUsage(usage.Get("input_tokens").Count(), output,
            usage.Get("cache_read_input_tokens").Count(), writes - longWrite, longWrite,
            Math.Min(output, usage.Get("output_tokens_details").Get("thinking_tokens").Count()));
        if (tokens.Total == 0) return new();
        var id = message.Get("id").Text();
        // Message identity survives resume/copy and streaming updates. Request id is optional.
        var key = id is not null ? "claude:" + id : "claude:fallback:" + source + ":" + offset;
        return new(new(Hash(key), ProviderKind.Claude, state.SessionId, state.Project, model, timestamp, tokens,
            id is not null ? IdentityQuality.Verified : IdentityQuality.Fallback));
    }

    private static ParsedLine Codex(JsonElement root, ParserState state, string source, long offset)
    {
        var payload = root.Get("payload");
        var type = root.Get("type").Text();
        if (type == "session_meta")
        {
            state.SessionId = payload.Get("id").Text() ?? state.SessionId;
            state.Project = ProjectName(payload.Get("cwd").Text(), state.Project);
            state.Fork = payload.Get("forked_from_id").Text() is not null;
            return new();
        }
        if (type == "turn_context")
        {
            state.Model = payload.Get("model").Text() ?? state.Model;
            state.TurnId = payload.Get("turn_id").Text();
            state.Project = ProjectName(payload.Get("cwd").Text(), state.Project);
            return new();
        }
        if (type != "event_msg" || payload.Get("type").Text() != "token_count") return new();
        var info = payload.Get("info");
        if (info.ValueKind != JsonValueKind.Object) return new();
        if (root.Get("timestamp").Time() is not { } timestamp) return new(Invalid: true);
        var lastElement = info.Get("last_token_usage");
        var totalElement = info.Get("total_token_usage");
        TokenUsage tokens;
        string counter;
        if (totalElement.ValueKind == JsonValueKind.Object)
        {
            var total = CodexTokens(totalElement);
            if (state.Cumulative is { } previous)
            {
                if (total == previous) return new(); // Context-only repeat, even with a new timestamp.
                if (total.Total < previous.Total)
                {
                    state.Epoch++;
                    tokens = lastElement.ValueKind == JsonValueKind.Object ? CodexTokens(lastElement) : total;
                }
                else tokens = total.Difference(previous);
            }
            else tokens = lastElement.ValueKind == JsonValueKind.Object ? CodexTokens(lastElement) : total;
            state.Cumulative = total;
            counter = JsonSerializer.Serialize(total);
        }
        else if (lastElement.ValueKind == JsonValueKind.Object)
        {
            tokens = CodexTokens(lastElement);
            counter = timestamp.ToUnixTimeMilliseconds() + ":" + JsonSerializer.Serialize(tokens);
        }
        else return new();
        if (tokens.Total == 0) return new();
        // Turn ids are shared by copied history in newer rollouts. Older fork records
        // without them remain visible as uncertain and are excluded from verified totals.
        var quality = state.Fork && state.TurnId is null ? IdentityQuality.UncertainFork :
            state.TurnId is not null ? IdentityQuality.Verified : IdentityQuality.Fallback;
        var identity = state.TurnId is { } turn ? "turn:" + turn : "session:" + state.SessionId;
        if (state.SessionId == "unknown") identity = source;
        var key = state.TurnId is not null ? $"codex:{identity}:{counter}" : $"codex:{identity}:{state.Epoch}:{counter}";
        return new(new(Hash(key), ProviderKind.Codex, state.SessionId, state.Project, state.Model, timestamp, tokens, quality));
    }

    private static TokenUsage CodexTokens(JsonElement usage)
    {
        var input = usage.Get("input_tokens").Count();
        var cached = Math.Min(input, usage.Get("cached_input_tokens").Count());
        var output = usage.Get("output_tokens").Count();
        return new(input - cached, output, cached, usage.Get("cache_write_input_tokens").Count(), 0,
            Math.Min(output, usage.Get("reasoning_output_tokens").Count()));
    }

    private static string ProjectName(string? path, string fallback) => string.IsNullOrWhiteSpace(path)
        ? fallback : path.TrimEnd('/', '\\').Split('/', '\\').LastOrDefault() ?? fallback;
}
