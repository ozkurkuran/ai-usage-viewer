using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiUsageViewer.Core;

namespace AiUsageViewer.Infrastructure.Storage;

public sealed class SettingsStore(string directory)
{
    private static readonly JsonSerializerOptions Options=new() { WriteIndented=true };
    public string DirectoryPath=>directory;
    public AppSettings Load()
    {
        var path=Path.Combine(directory,"settings.json");
        return Validate(File.Exists(path)?JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))??new():DiscoverDefaults());
    }
    public static AppSettings Validate(AppSettings value)
    {
        if(value.Version!=1) throw new InvalidDataException("Unsupported settings version.");
        var placement=value.WidgetPlacement??new();
        static double Finite(double value,double fallback)=>double.IsFinite(value)?value:fallback;
        var accounts=(value.Accounts??[]).Where(a=>a is not null&&!string.IsNullOrWhiteSpace(a.Id)&&Enum.IsDefined(a.Provider)).DistinctBy(a=>a.Id).ToList();
        return value with { Language=value.Language=="en"?"en":"tr",Theme=value.Theme=="light"?"light":"dark",Opacity=Math.Clamp(Finite(value.Opacity,.97),.35,1),
            NotifyUsagePercent=Math.Clamp(value.NotifyUsagePercent,1,100),NotifyLowBalance=Math.Max(0,value.NotifyLowBalance),
            WidgetPlacement=placement with { Left=Finite(placement.Left,60),Top=Finite(placement.Top,60),Width=Math.Clamp(Finite(placement.Width,370),310,1000),
                Height=Math.Clamp(Finite(placement.Height,580),310,1400),OffsetX=Finite(placement.OffsetX,60),OffsetY=Finite(placement.OffsetY,60) },
            Accounts=accounts,Sources=(value.Sources??[]).Where(s=>s is not null&&!string.IsNullOrWhiteSpace(s.Directory)&&s.Provider is ProviderKind.Claude or ProviderKind.Codex).DistinctBy(s=>s.Directory,StringComparer.OrdinalIgnoreCase).ToList(),
            HiddenAccounts=(value.HiddenAccounts??[]).Where(id=>accounts.Any(a=>a.Id==id)).Distinct().ToList(),
            AccountOrder=(value.AccountOrder??[]).Where(id=>accounts.Any(a=>a.Id==id)).Distinct().ToList(),
            PriceOverrides=(value.PriceOverrides??[]).Where(p=>p is not null&&p.IsValid).ToList() };
    }
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"settings.json");
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(settings,Options));
        File.Move(temporary,path,true);
    }
    public static AppSettings DiscoverDefaults()
    {
        var user=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var claude=Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")??Path.Combine(user,".claude");
        var codex=Environment.GetEnvironmentVariable("CODEX_HOME")??Path.Combine(user,".codex");
        return new() {
            Accounts=[new() { Id="claude-default",Provider=ProviderKind.Claude,Label="Claude",ProfileDirectory=claude },
                new() { Id="codex-default",Provider=ProviderKind.Codex,Label="Codex",ProfileDirectory=codex }],
            Sources=[new("claude-projects",ProviderKind.Claude,Path.Combine(claude,"projects"),Optional:true),
                new("codex-sessions",ProviderKind.Codex,Path.Combine(codex,"sessions"),Optional:true),
                new("codex-archive",ProviderKind.Codex,Path.Combine(codex,"archived_sessions"),Optional:true)]
        };
    }
}

// Windows DPAPI binds stored credentials to the current Windows user.
public sealed class WindowsSecretStore(string directory) : ISecretStore
{
    private string PathFor(string reference)=>Path.Combine(directory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reference)))+".secrets");
    public string? Read(string reference)
    {
        var path=PathFor(reference);
        if(!File.Exists(path)) return null;
        var plain=ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Write(string reference,string value)
    {
        Directory.CreateDirectory(directory);
        var plain=Encoding.UTF8.GetBytes(value);
        try
        {
            var encrypted=ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser);
            var path=PathFor(reference); var temporary=path+".tmp";
            File.WriteAllBytes(temporary,encrypted); File.Move(temporary,path,true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Delete(string reference)=>File.Delete(PathFor(reference));
}
