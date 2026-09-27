using System.Text.Json;
using AiUsageViewer.Core;
using AiUsageViewer.Infrastructure.Logs;

namespace AiUsageViewer.Tests;

public class ParserTests
{
    internal static string Claude(string id="msg-1",int input=100,int output=30) => JsonSerializer.Serialize(new {
        type="assistant",timestamp="2026-09-27T09:00:00Z",sessionId="session-1",cwd=@"C:\projects\sample",
        message=new { id,model="claude-test",usage=new { input_tokens=input,output_tokens=output,
            cache_read_input_tokens=50,cache_creation_input_tokens=20,
            cache_creation=new { ephemeral_5m_input_tokens=10,ephemeral_1h_input_tokens=10 },
            output_tokens_details=new { thinking_tokens=12 } } }
    });
    internal static string Codex(int totalInput,int totalOutput,int lastInput=100,int lastOutput=20,string timestamp="2026-09-27T09:00:00Z") => JsonSerializer.Serialize(new {
        type="event_msg",timestamp,payload=new { type="token_count",info=new {
            total_token_usage=new { input_tokens=totalInput,output_tokens=totalOutput,cached_input_tokens=totalInput/2,reasoning_output_tokens=totalOutput/2 },
            last_token_usage=new { input_tokens=lastInput,output_tokens=lastOutput,cached_input_tokens=lastInput/2,reasoning_output_tokens=lastOutput/2 }
        } }
    });

    [Fact] public void ClaudeBucketsAreDisjointAndReasoningIsIncludedInOutput()
    {
        var result=UsageLogParser.Parse(ProviderKind.Claude,Claude(),new(),"source",0).Event!;
        Assert.Equal(200,result.Tokens.Total);
        Assert.Equal(new TokenUsage(100,30,50,10,10,12),result.Tokens);
        Assert.Equal("sample",result.Project);
        Assert.Null(result.AccountId);
    }
    [Fact] public void ClaudeIdentitySurvivesCopiedFilesAndStreamingUpdates()
    {
        var a=UsageLogParser.Parse(ProviderKind.Claude,Claude(),new(),"first",0).Event!;
        var b=UsageLogParser.Parse(ProviderKind.Claude,Claude(output:80),new(),"copy",456).Event!;
        Assert.Equal(a.Id,b.Id);
        Assert.Equal(250,b.Tokens.Total);
    }
    [Fact] public void CodexUsesCumulativeDeltaAndIgnoresContextOnlyRepeats()
    {
        var state=new ParserState { SessionId="session",Model="codex-test" };
        var first=UsageLogParser.Parse(ProviderKind.Codex,Codex(100,20),state,"source",0).Event!;
        var repeated=UsageLogParser.Parse(ProviderKind.Codex,Codex(100,20,timestamp:"2026-09-27T09:01:00Z"),state,"source",100);
        var next=UsageLogParser.Parse(ProviderKind.Codex,Codex(250,50,150,30),state,"source",200).Event!;
        Assert.Equal(120,first.Tokens.Total);
        Assert.Null(repeated.Event);
        Assert.Equal(180,next.Tokens.Total);
        Assert.Equal(75,next.Tokens.Input);
        Assert.Equal(75,next.Tokens.CacheRead);
    }
    [Fact] public void CounterResetStartsANewEpoch()
    {
        var state=new ParserState { SessionId="session" };
        var first=UsageLogParser.Parse(ProviderKind.Codex,Codex(100,20),state,"source",0).Event!;
        UsageLogParser.Parse(ProviderKind.Codex,Codex(200,40),state,"source",10);
        var reset=UsageLogParser.Parse(ProviderKind.Codex,Codex(100,20),state,"source",20).Event!;
        Assert.Equal(120,reset.Tokens.Total);
        Assert.NotEqual(first.Id,reset.Id);
    }
    [Fact] public void ForkWithoutStableTurnIdentityIsExplicitlyUncertain()
    {
        var state=new ParserState { SessionId="fork",Fork=true };
        Assert.Equal(IdentityQuality.UncertainFork,UsageLogParser.Parse(ProviderKind.Codex,Codex(100,20),state,"source",0).Event!.Identity);
    }
    [Fact] public void CopiedTurnHasSameIdentityAcrossForkSessions()
    {
        var parent=new ParserState { SessionId="parent",TurnId="turn-1" };
        var child=new ParserState { SessionId="child",TurnId="turn-1",Fork=true };
        Assert.Equal(UsageLogParser.Parse(ProviderKind.Codex,Codex(100,20),parent,"source",0).Event!.Id,
            UsageLogParser.Parse(ProviderKind.Codex,Codex(100,20),child,"copy",200).Event!.Id);
    }
    [Theory]
    [InlineData("{")]
    [InlineData("not json")]
    public void MalformedLinesDoNotThrow(string line) => Assert.True(UsageLogParser.Parse(ProviderKind.Claude,line,new(),"s",0).Invalid);

    [Fact] public void NullUsageIsNotZeroUsage() => Assert.Null(UsageLogParser.Parse(ProviderKind.Codex,
        "{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":null}}",new(),"s",0).Event);
}
