using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AiUsageViewer.App;

// Attached properties read by the shared control templates in Theme/Controls.xaml.
public static class Ui
{
    public static readonly DependencyProperty IconProperty=DependencyProperty.RegisterAttached("Icon",typeof(Geometry),typeof(Ui),new FrameworkPropertyMetadata(null));
    public static Geometry? GetIcon(DependencyObject d)=>(Geometry?)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d,Geometry? value)=>d.SetValue(IconProperty,value);
    public static readonly DependencyProperty TrailingIconProperty=DependencyProperty.RegisterAttached("TrailingIcon",typeof(Geometry),typeof(Ui),new FrameworkPropertyMetadata(null));
    public static Geometry? GetTrailingIcon(DependencyObject d)=>(Geometry?)d.GetValue(TrailingIconProperty);
    public static void SetTrailingIcon(DependencyObject d,Geometry? value)=>d.SetValue(TrailingIconProperty,value);
    public static readonly DependencyProperty IsActiveProperty=DependencyProperty.RegisterAttached("IsActive",typeof(bool),typeof(Ui),new FrameworkPropertyMetadata(false));
    public static bool GetIsActive(DependencyObject d)=>(bool)d.GetValue(IsActiveProperty);
    public static void SetIsActive(DependencyObject d,bool value)=>d.SetValue(IsActiveProperty,value);
    public static readonly DependencyProperty PrefixProperty=DependencyProperty.RegisterAttached("Prefix",typeof(string),typeof(Ui),new FrameworkPropertyMetadata(null));
    public static string? GetPrefix(DependencyObject d)=>(string?)d.GetValue(PrefixProperty);
    public static void SetPrefix(DependencyObject d,string? value)=>d.SetValue(PrefixProperty,value);
    public static readonly DependencyProperty SuffixProperty=DependencyProperty.RegisterAttached("Suffix",typeof(string),typeof(Ui),new FrameworkPropertyMetadata(null));
    public static string? GetSuffix(DependencyObject d)=>(string?)d.GetValue(SuffixProperty);
    public static void SetSuffix(DependencyObject d,string? value)=>d.SetValue(SuffixProperty,value);
    public static readonly DependencyProperty PlaceholderProperty=DependencyProperty.RegisterAttached("Placeholder",typeof(string),typeof(Ui),new FrameworkPropertyMetadata(null));
    public static string? GetPlaceholder(DependencyObject d)=>(string?)d.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject d,string? value)=>d.SetValue(PlaceholderProperty,value);
}

// Tabler outline glyph in a 24-unit box, stroked at 1.75 and scaled to Size.
public sealed class Icon : Control
{
    public static readonly DependencyProperty DataProperty=DependencyProperty.Register(nameof(Data),typeof(Geometry),typeof(Icon),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsMeasure|FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SizeProperty=DependencyProperty.Register(nameof(Size),typeof(double),typeof(Icon),new FrameworkPropertyMetadata(18d,FrameworkPropertyMetadataOptions.AffectsMeasure|FrameworkPropertyMetadataOptions.AffectsRender));
    static Icon()
    {
        FocusableProperty.OverrideMetadata(typeof(Icon),new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(Icon),new FrameworkPropertyMetadata(false));
        ForegroundProperty.OverrideMetadata(typeof(Icon),new FrameworkPropertyMetadata(Brushes.Gray,FrameworkPropertyMetadataOptions.Inherits|FrameworkPropertyMetadataOptions.AffectsRender));
    }
    public Geometry? Data { get=>(Geometry?)GetValue(DataProperty);set=>SetValue(DataProperty,value); }
    public double Size { get=>(double)GetValue(SizeProperty);set=>SetValue(SizeProperty,value); }
    protected override Size MeasureOverride(Size constraint)=>Data is null?new(0,0):new(Size,Size);
    protected override Size ArrangeOverride(Size arrangeBounds)=>Data is null?new(0,0):new(Size,Size);
    protected override void OnRender(DrawingContext dc)
    {
        if(Data is null) return;
        var scale=Size/24;
        var pen=new Pen(Foreground,1.75) { StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round };
        dc.PushTransform(new ScaleTransform(scale,scale));dc.DrawGeometry(null,pen,Data);dc.Pop();
    }
}

// Brand mark: diamond outline with a filled inner diamond, in Accent.Text.
public sealed class LogoMark : FrameworkElement
{
    public static readonly DependencyProperty SizeProperty=DependencyProperty.Register(nameof(Size),typeof(double),typeof(LogoMark),new FrameworkPropertyMetadata(20d,FrameworkPropertyMetadataOptions.AffectsMeasure|FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty BrushProperty=DependencyProperty.Register("Brush",typeof(Brush),typeof(LogoMark),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly Geometry Outer=Geometry.Parse("M 12 2.5 L 21.5 12 L 12 21.5 L 2.5 12 Z"),Inner=Geometry.Parse("M 12 8 L 16 12 L 12 16 L 8 12 Z");
    public LogoMark() { SetResourceReference(BrushProperty,"Accent.Text");Focusable=false; }
    public double Size { get=>(double)GetValue(SizeProperty);set=>SetValue(SizeProperty,value); }
    protected override Size MeasureOverride(Size availableSize)=>new(Size,Size);
    protected override void OnRender(DrawingContext dc)
    {
        var brush=(Brush)GetValue(BrushProperty);var scale=Size/24;
        dc.PushTransform(new ScaleTransform(scale,scale));
        dc.DrawGeometry(null,new Pen(brush,1.9) { LineJoin=PenLineJoin.Round },Outer);dc.DrawGeometry(brush,null,Inner);dc.Pop();
    }
}

// Quota bar: track, status fill and an optional pace marker that rises above the track.
public sealed class QuotaBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty=Register(nameof(Value),0d);
    public static readonly DependencyProperty MarkerProperty=Register(nameof(Marker),double.NaN);
    public static readonly DependencyProperty BarHeightProperty=Register(nameof(BarHeight),8d,FrameworkPropertyMetadataOptions.AffectsMeasure);
    public static readonly DependencyProperty MarkerHeightProperty=Register(nameof(MarkerHeight),14d,FrameworkPropertyMetadataOptions.AffectsMeasure);
    public static readonly DependencyProperty FillProperty=DependencyProperty.Register(nameof(Fill),typeof(Brush),typeof(QuotaBar),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackProperty=DependencyProperty.Register(nameof(Track),typeof(Brush),typeof(QuotaBar),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MarkerBrushProperty=DependencyProperty.Register(nameof(MarkerBrush),typeof(Brush),typeof(QuotaBar),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    private static DependencyProperty Register(string name,double value,FrameworkPropertyMetadataOptions extra=FrameworkPropertyMetadataOptions.None)=>
        DependencyProperty.Register(name,typeof(double),typeof(QuotaBar),new FrameworkPropertyMetadata(value,FrameworkPropertyMetadataOptions.AffectsRender|extra));
    public QuotaBar() { SetResourceReference(TrackProperty,"Bg.Track");SetResourceReference(MarkerBrushProperty,"Pace.Marker");SnapsToDevicePixels=true; }
    public double Value { get=>(double)GetValue(ValueProperty);set=>SetValue(ValueProperty,value); }
    // Percent of the bar width; NaN hides the marker.
    public double Marker { get=>(double)GetValue(MarkerProperty);set=>SetValue(MarkerProperty,value); }
    public double BarHeight { get=>(double)GetValue(BarHeightProperty);set=>SetValue(BarHeightProperty,value); }
    public double MarkerHeight { get=>(double)GetValue(MarkerHeightProperty);set=>SetValue(MarkerHeightProperty,value); }
    public Brush? Fill { get=>(Brush?)GetValue(FillProperty);set=>SetValue(FillProperty,value); }
    public Brush? Track { get=>(Brush?)GetValue(TrackProperty);set=>SetValue(TrackProperty,value); }
    public Brush? MarkerBrush { get=>(Brush?)GetValue(MarkerBrushProperty);set=>SetValue(MarkerBrushProperty,value); }
    protected override Size MeasureOverride(Size availableSize)=>new(0,Math.Max(BarHeight,double.IsNaN(Marker)?BarHeight:MarkerHeight));
    protected override void OnRender(DrawingContext dc)
    {
        var width=ActualWidth;if(width<=0) return;
        var top=(ActualHeight-BarHeight)/2;var radius=Math.Min(4,BarHeight/2);
        dc.DrawRoundedRectangle(Track,null,new Rect(0,top,width,BarHeight),radius,radius);
        var fill=Math.Clamp(Value,0,100)/100*width;
        if(fill>0) dc.DrawRoundedRectangle(Fill,null,new Rect(0,top,Math.Max(fill,BarHeight),BarHeight),radius,radius);
        if(!double.IsNaN(Marker))
        {
            var x=Math.Clamp(Marker,0,100)/100*width;x=Math.Clamp(x-1,0,width-2);
            dc.DrawRoundedRectangle(MarkerBrush,null,new Rect(x,(ActualHeight-MarkerHeight)/2,2,MarkerHeight),1,1);
        }
    }
}

// Stacked input / cache / output bar with 3 px gaps and rounded ends.
public sealed class MixBar : FrameworkElement
{
    public static readonly DependencyProperty InputProperty=Register(nameof(Input));
    public static readonly DependencyProperty CacheProperty=Register(nameof(Cache));
    public static readonly DependencyProperty OutputProperty=Register(nameof(Output));
    public static readonly DependencyProperty BarHeightProperty=Register(nameof(BarHeight),10);
    private static readonly DependencyProperty InputBrushProperty=RegisterBrush("InputBrush"),CacheBrushProperty=RegisterBrush("CacheBrush"),
        OutputBrushProperty=RegisterBrush("OutputBrush"),TrackBrushProperty=RegisterBrush("TrackBrush");
    private static DependencyProperty Register(string name,double value=0)=>DependencyProperty.Register(name,typeof(double),typeof(MixBar),new FrameworkPropertyMetadata(value,FrameworkPropertyMetadataOptions.AffectsRender|FrameworkPropertyMetadataOptions.AffectsMeasure));
    private static DependencyProperty RegisterBrush(string name)=>DependencyProperty.Register(name,typeof(Brush),typeof(MixBar),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public MixBar()
    {
        SetResourceReference(InputBrushProperty,"Chart.Input");SetResourceReference(CacheBrushProperty,"Chart.Cache");
        SetResourceReference(OutputBrushProperty,"Chart.Output");SetResourceReference(TrackBrushProperty,"Bg.Track");
    }
    public double Input { get=>(double)GetValue(InputProperty);set=>SetValue(InputProperty,value); }
    public double Cache { get=>(double)GetValue(CacheProperty);set=>SetValue(CacheProperty,value); }
    public double Output { get=>(double)GetValue(OutputProperty);set=>SetValue(OutputProperty,value); }
    public double BarHeight { get=>(double)GetValue(BarHeightProperty);set=>SetValue(BarHeightProperty,value); }
    protected override Size MeasureOverride(Size availableSize)=>new(0,BarHeight);
    protected override void OnRender(DrawingContext dc)
    {
        var total=Input+Cache+Output;var radius=BarHeight/2;
        if(total<=0) { dc.DrawRoundedRectangle((Brush)GetValue(TrackBrushProperty),null,new Rect(0,0,ActualWidth,BarHeight),radius,radius);return; }
        var parts=new[]{(Input,(Brush)GetValue(InputBrushProperty)),(Cache,(Brush)GetValue(CacheBrushProperty)),(Output,(Brush)GetValue(OutputBrushProperty))}.Where(p=>p.Item1>0).ToList();
        var available=ActualWidth-3*(parts.Count-1);var x=0d;
        foreach(var (value,brush) in parts)
        {
            var width=Math.Max(BarHeight,value/total*available);
            dc.DrawRoundedRectangle(brush,null,new Rect(x,0,Math.Min(width,ActualWidth-x),BarHeight),radius,radius);x+=width+3;
        }
    }
}

// CheckBox rendered as a switch; reports itself to assistive technology as a toggle switch.
public sealed class ToggleSwitch : CheckBox
{
    protected override AutomationPeer OnCreateAutomationPeer()=>new SwitchPeer(this);
    private sealed class SwitchPeer(ToggleSwitch owner):CheckBoxAutomationPeer(owner)
    {
        protected override string GetLocalizedControlTypeCore()=>"toggle switch";
    }
}

public sealed class StatusPill : Control
{
    public static readonly DependencyProperty KindProperty=DependencyProperty.Register(nameof(Kind),typeof(string),typeof(StatusPill),new FrameworkPropertyMetadata("empty"));
    public static readonly DependencyProperty TextProperty=DependencyProperty.Register(nameof(Text),typeof(string),typeof(StatusPill),new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty SmallProperty=DependencyProperty.Register(nameof(Small),typeof(bool),typeof(StatusPill),new FrameworkPropertyMetadata(false));
    static StatusPill() { FocusableProperty.OverrideMetadata(typeof(StatusPill),new FrameworkPropertyMetadata(false)); }
    // ok · refreshing · stale · empty
    public string Kind { get=>(string)GetValue(KindProperty);set=>SetValue(KindProperty,value); }
    public string Text { get=>(string)GetValue(TextProperty);set=>SetValue(TextProperty,value); }
    public bool Small { get=>(bool)GetValue(SmallProperty);set=>SetValue(SmallProperty,value); }
    protected override AutomationPeer OnCreateAutomationPeer()=>new FrameworkElementAutomationPeer(this);
}

public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture)=>Equals(value?.ToString(),parameter?.ToString());
    public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>Binding.DoNothing;
}
public sealed class InverseBoolVisibilityConverter : IValueConverter
{
    public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture)=>value is true?Visibility.Collapsed:Visibility.Visible;
    public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>Binding.DoNothing;
}
public sealed class TextVisibilityConverter : IValueConverter
{
    public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture)=>string.IsNullOrEmpty(value as string)?Visibility.Collapsed:Visibility.Visible;
    public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>Binding.DoNothing;
}
// Resolves a theme token key (for example "Claude.Text") from view-model data.
public sealed class TokenBrushConverter : IValueConverter
{
    public object? Convert(object? value,Type targetType,object? parameter,CultureInfo culture)=>
        value is string key?System.Windows.Application.Current.TryFindResource(key) as Brush:null;
    public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>Binding.DoNothing;
}
