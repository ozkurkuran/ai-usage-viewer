namespace AiUsageViewer.Core;

public sealed record WindowPlacement(double Left = 60, double Top = 60, double Width = 370, double Height = 640,
    string? MonitorDevice = null,double OffsetX = 60,double OffsetY = 60);
public sealed record AppSettings
{
    public int Version { get; init; } = 1;
    // First run follows the Windows display language; saved settings keep the user's choice.
    public string Language { get; init; } = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="tr"?"tr":"en";
    public string Theme { get; init; } = "dark";
    public bool ShowRemaining { get; init; }
    public bool AlwaysOnTop { get; init; } = true;
    public bool LockPosition { get; init; }
    public bool Compact { get; init; }
    public bool ShowEstimatedCost { get; init; } = true;
    public double Opacity { get; init; } = 0.97;
    public bool StartWithWindows { get; init; }
    public bool ShowWidgetOnLaunch { get; init; } = true;
    public bool NotificationsEnabled { get; init; }
    public bool NotifyResets { get; init; }
    public decimal NotifyUsagePercent { get; init; } = 90;
    public decimal NotifyLowBalance { get; init; } = 5;
    public WindowPlacement WidgetPlacement { get; init; } = new();
    public List<AccountProfile> Accounts { get; init; } = [];
    public List<SourceLocation> Sources { get; init; } = [];
    public List<string> HiddenAccounts { get; init; } = [];
    public List<string> AccountOrder { get; init; } = [];
    public List<ModelPrice> PriceOverrides { get; init; } = [];
}
