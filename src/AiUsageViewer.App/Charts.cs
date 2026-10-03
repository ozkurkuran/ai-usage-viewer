using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AiUsageViewer.Core;

namespace AiUsageViewer.App;

// Shared chart plumbing: theme brushes as resource references and a hover tooltip card.
public abstract class ChartBase : FrameworkElement
{
    private readonly Popup popup;
    private readonly StackPanel tip=new();
    private readonly Dictionary<string,DependencyProperty> brushes=[];
    // Theme brushes are dependency properties (so a theme swap re-renders), registered once per chart type.
    private static readonly Dictionary<(Type,string),DependencyProperty> Registered=[];
    protected int HoverIndex { get; private set; }=-1;
    public static readonly DependencyProperty FormatProperty=DependencyProperty.Register(nameof(Format),typeof(UiFormat),typeof(ChartBase),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public UiFormat? Format { get=>(UiFormat?)GetValue(FormatProperty);set=>SetValue(FormatProperty,value); }
    protected ChartBase(params string[] tokens)
    {
        foreach(var token in tokens.Concat(["Text.Muted","Text.Disabled","Text.Primary","Text.Secondary","Chart.Grid","Chart.Baseline","Chart.Highlight"]).Distinct())
        {
            DependencyProperty property;
            lock(Registered)
                if(!Registered.TryGetValue((GetType(),token),out property!))
                    Registered[(GetType(),token)]=property=DependencyProperty.Register(GetType().Name+"_"+token.Replace(".",""),typeof(Brush),GetType(),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
            brushes[token]=property;SetResourceReference(property,token);
        }
        var card=new Border { CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),Padding=new Thickness(12,10,12,10),Margin=new Thickness(0,0,12,12),Child=tip,MinWidth=150,
            Effect=new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=16,ShadowDepth=3,Opacity=0.3 } };
        card.SetResourceReference(Border.BackgroundProperty,"Bg.Tooltip");card.SetResourceReference(Border.BorderBrushProperty,"Border.Tooltip");
        TextElement.SetFontFamily(card,new FontFamily(new Uri("pack://application:,,,/"),"./Assets/Fonts/#Geist, Segoe UI"));
        Typography.SetNumeralAlignment(card,FontNumeralAlignment.Tabular);
        popup=new Popup { Child=card,AllowsTransparency=true,Placement=PlacementMode.Relative,PlacementTarget=this,IsHitTestVisible=false };
        MouseLeave+=(_,_)=>SetHover(-1);
    }
    protected Brush B(string token)=>(Brush?)GetValue(brushes[token])??Brushes.Gray;
    protected double Dpi=>VisualTreeHelper.GetDpi(this).PixelsPerDip;
    protected FormattedText Text(string value,double size,Brush brush,FontWeight? weight=null)=>
        new(value,Format?.Culture??CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily(new Uri("pack://application:,,,/"),"./Assets/Fonts/#Geist, Segoe UI"),FontStyles.Normal,weight??FontWeights.Normal,FontStretches.Normal),size,brush,Dpi);
    protected abstract int HitTest(Point point);
    protected abstract void FillTip(int index,StackPanel panel,Func<string,double,string,FontWeight?,TextBlock> line);
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e);SetHover(HitTest(e.GetPosition(this))); }
    private void SetHover(int index)
    {
        if(index==HoverIndex) { if(index>=0) Position(); return; }
        HoverIndex=index;InvalidateVisual();
        if(index<0) { popup.IsOpen=false;return; }
        tip.Children.Clear();
        FillTip(index,tip,(text,size,token,weight)=> { var block=new TextBlock { Text=text,FontSize=size,FontWeight=weight??FontWeights.Normal };block.SetResourceReference(TextBlock.ForegroundProperty,token);tip.Children.Add(block);return block; });
        popup.IsOpen=true;Position();
    }
    private void Position()
    {
        var mouse=Mouse.GetPosition(this);popup.HorizontalOffset=mouse.X+14;popup.VerticalOffset=Math.Max(0,mouse.Y-40);
    }
    // A tooltip row: swatch, label, right-aligned value.
    protected static FrameworkElement Row(Brush? swatch,string label,string value)
    {
        var grid=new Grid { Margin=new Thickness(0,4,0,0) };
        grid.ColumnDefinitions.Add(new() { Width=GridLength.Auto });grid.ColumnDefinitions.Add(new() { Width=new GridLength(1,GridUnitType.Star),MinWidth=60 });grid.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        if(swatch is not null) grid.Children.Add(new Border { Width=8,Height=8,CornerRadius=new CornerRadius(2),Background=swatch,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center });
        var name=new TextBlock { Text=label,FontSize=12 };name.SetResourceReference(TextBlock.ForegroundProperty,"Text.Secondary");Grid.SetColumn(name,1);grid.Children.Add(name);
        var amount=new TextBlock { Text=value,FontSize=12,Margin=new Thickness(16,0,0,0) };amount.SetResourceReference(TextBlock.ForegroundProperty,"Text.Primary");Grid.SetColumn(amount,2);grid.Children.Add(amount);
        return grid;
    }
}

// Stacked input / cache / output columns with nice Y ticks and an optional dashed "Now" line.
public sealed class UsageHistoryChart : ChartBase
{
    public static readonly DependencyProperty BarsProperty=DependencyProperty.Register(nameof(Bars),typeof(IReadOnlyList<ChartBar>),typeof(UsageHistoryChart),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty NowProperty=DependencyProperty.Register(nameof(Now),typeof(double),typeof(UsageHistoryChart),new FrameworkPropertyMetadata(double.NaN,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LabelsProperty=DependencyProperty.Register(nameof(Labels),typeof(Localization),typeof(UsageHistoryChart),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public UsageHistoryChart():base("Chart.Input","Chart.Cache","Chart.Output") { }
    public IReadOnlyList<ChartBar>? Bars { get=>(IReadOnlyList<ChartBar>?)GetValue(BarsProperty);set=>SetValue(BarsProperty,value); }
    public double Now { get=>(double)GetValue(NowProperty);set=>SetValue(NowProperty,value); }
    public Localization? Labels { get=>(Localization?)GetValue(LabelsProperty);set=>SetValue(LabelsProperty,value); }
    private const double Left=44,Top=22,Bottom=26;
    private double Step=>Bars is { Count: >0 } bars?(ActualWidth-Left)/bars.Count:0;
    protected override int HitTest(Point point)
    {
        if(Bars is not { Count: >0 } bars||point.X<Left||point.Y>ActualHeight-Bottom) return -1;
        var index=(int)((point.X-Left)/Step);return index>=0&&index<bars.Count&&bars[index].Total>0?index:-1;
    }
    protected override void OnRender(DrawingContext dc)
    {
        var bars=Bars;if(bars is null||bars.Count==0||ActualWidth<120||Format is null) return;
        var plotHeight=ActualHeight-Top-Bottom;var ticks=UiFormat.NiceTicks(bars.Max(b=>b.Total));var max=ticks[^1];
        var grid=new Pen(B("Chart.Grid"),1);var baseline=new Pen(B("Chart.Baseline"),1);
        foreach(var tick in ticks)
        {
            var y=Math.Round(Top+plotHeight-tick/max*plotHeight)+0.5;
            dc.DrawLine(tick==0?baseline:grid,new(Left,y),new(ActualWidth,y));
            var label=Text(Format.Axis(tick),11,B("Text.Muted"));dc.DrawText(label,new(Left-10-label.Width,y-label.Height/2));
        }
        var step=Step;var width=Math.Clamp(step*0.72,3,34);
        if(HoverIndex>=0) dc.DrawRoundedRectangle(B("Chart.Highlight"),null,new Rect(Left+HoverIndex*step+1,Top,step-2,plotHeight),6,6);
        for(var i=0;i<bars.Count;i++)
        {
            var bar=bars[i];var x=Left+i*step+(step-width)/2;var y=Top+plotHeight;
            foreach(var (value,token) in new[]{(bar.Input,"Chart.Input"),(bar.Cache,"Chart.Cache"),(bar.Output,"Chart.Output")})
            {
                if(value<=0) continue;var h=value/max*plotHeight;y-=h;
                dc.DrawRectangle(B(token),null,new Rect(x,y,width,Math.Max(h-1,0.5)));
            }
            if(bar.Label.Length>0)
            {
                var label=Text(bar.Label,11,B(bar.IsFuture?"Text.Disabled":"Text.Muted"));
                dc.DrawText(label,new(Math.Clamp(x+width/2-label.Width/2,Left,ActualWidth-label.Width),Top+plotHeight+8));
            }
        }
        if(!double.IsNaN(Now))
        {
            var x=Math.Round(Left+Now*step)+0.5;
            dc.DrawLine(new Pen(B("Text.Muted"),1) { DashStyle=new DashStyle([3,3],0) },new(x,Top),new(x,Top+plotHeight));
            var label=Text(Labels?["now"]??"Now",11,B("Text.Muted"));dc.DrawText(label,new(x+4,Top-label.Height-2));
        }
    }
    protected override void FillTip(int index,StackPanel panel,Func<string,double,string,FontWeight?,TextBlock> line)
    {
        var bar=Bars![index];var l=Labels;
        line(bar.Range,12,"Text.Secondary",null);
        line(string.Format(Format!.Culture,l?["tokensCount"]??"{0} tokens",Format.Number(bar.Total)),15,"Text.Primary",FontWeights.SemiBold).Margin=new Thickness(0,2,0,4);
        panel.Children.Add(Row(B("Chart.Input"),l?["input"]??"Input",Format.Number(bar.Input)));
        panel.Children.Add(Row(B("Chart.Cache"),l?["cache"]??"Cache",Format.Number(bar.Cache)));
        panel.Children.Add(Row(B("Chart.Output"),l?["output"]??"Output",Format.Number(bar.Output)));
    }
}

// 13 Monday-first weeks × 7 days; today ringed, future days outlined with dashes.
public sealed class HeatmapChart : ChartBase
{
    public static readonly DependencyProperty DaysProperty=DependencyProperty.Register(nameof(Days),typeof(IReadOnlyList<HeatDay>),typeof(HeatmapChart),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public HeatmapChart():base("Heat.0","Heat.1","Heat.2","Heat.3","Heat.4","Border.Control") { }
    public IReadOnlyList<HeatDay>? Days { get=>(IReadOnlyList<HeatDay>?)GetValue(DaysProperty);set=>SetValue(DaysProperty,value); }
    private const double Cell=16,Gap=4,Left=36,Top=22;
    protected override Size MeasureOverride(Size availableSize)=>new(Left+13*(Cell+Gap),Top+7*(Cell+Gap));
    protected override int HitTest(Point point)
    {
        var column=(int)((point.X-Left)/(Cell+Gap));var row=(int)((point.Y-Top)/(Cell+Gap));
        return point.X<Left||point.Y<Top||column is <0 or >12||row is <0 or >6||Days is null?-1:column*7+row<Days.Count?column*7+row:-1;
    }
    protected override void OnRender(DrawingContext dc)
    {
        var days=Days;if(days is null||days.Count==0||Format is null) return;
        var culture=Format.Culture;
        foreach(var (row,day) in new[]{(0,DayOfWeek.Monday),(2,DayOfWeek.Wednesday),(4,DayOfWeek.Friday)})
        {
            var label=Text(culture.DateTimeFormat.GetAbbreviatedDayName(day),11,B("Text.Muted"));
            dc.DrawText(label,new(0,Top+row*(Cell+Gap)+(Cell-label.Height)/2));
        }
        for(var i=0;i<days.Count;i++)
        {
            var day=days[i];var column=i/7;var row=i%7;var rect=new Rect(Left+column*(Cell+Gap),Top+row*(Cell+Gap),Cell,Cell);
            if(day.Day.Day==1) { var month=Text(day.Day.ToString("MMM",culture),11,B("Text.Muted"));dc.DrawText(month,new(rect.X,0)); }
            if(day.IsFuture) { dc.DrawRoundedRectangle(null,new Pen(B("Border.Control"),1) { DashStyle=new DashStyle([2,2],0) },new Rect(rect.X+0.5,rect.Y+0.5,Cell-1,Cell-1),4,4);continue; }
            dc.DrawRoundedRectangle(B("Heat."+day.Level),null,rect,4,4);
            if(day.IsToday||i==HoverIndex) dc.DrawRoundedRectangle(null,new Pen(B("Text.Primary"),1.5),new Rect(rect.X-1.5,rect.Y-1.5,Cell+3,Cell+3),5,5);
        }
    }
    protected override void FillTip(int index,StackPanel panel,Func<string,double,string,FontWeight?,TextBlock> line)=>line(Days![index].Tooltip,12,"Text.Primary",null);
}

// OpenRouter daily spend: one bar per completed UTC day; empty days get a 2 px stub.
public sealed class SpendChart : ChartBase
{
    public static readonly DependencyProperty BarsProperty=DependencyProperty.Register(nameof(Bars),typeof(IReadOnlyList<SpendBar>),typeof(SpendChart),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LabelsProperty=DependencyProperty.Register(nameof(Labels),typeof(Localization),typeof(SpendChart),new FrameworkPropertyMetadata(null));
    public SpendChart():base("Chart.Input","Bg.Track") { }
    public IReadOnlyList<SpendBar>? Bars { get=>(IReadOnlyList<SpendBar>?)GetValue(BarsProperty);set=>SetValue(BarsProperty,value); }
    public Localization? Labels { get=>(Localization?)GetValue(LabelsProperty);set=>SetValue(LabelsProperty,value); }
    private const double Left=52,Top=10,Bottom=26;
    private double Step=>Bars is { Count: >0 } bars?(ActualWidth-Left)/bars.Count:0;
    protected override int HitTest(Point point)
    {
        if(Bars is not { Count: >0 } bars||point.X<Left) return -1;
        var index=(int)((point.X-Left)/Step);return index>=0&&index<bars.Count?index:-1;
    }
    protected override void OnRender(DrawingContext dc)
    {
        var bars=Bars;if(bars is null||bars.Count==0||Format is null||ActualWidth<120) return;
        var plotHeight=ActualHeight-Top-Bottom;var ticks=UiFormat.NiceTicks((double)bars.Max(b=>b.Spend));var max=ticks[^1];
        var grid=new Pen(B("Chart.Grid"),1);var baseline=new Pen(B("Chart.Baseline"),1);
        foreach(var tick in ticks)
        {
            var y=Math.Round(Top+plotHeight-tick/max*plotHeight)+0.5;dc.DrawLine(tick==0?baseline:grid,new(Left,y),new(ActualWidth,y));
            var label=Text(Format.Money((decimal)tick),11,B("Text.Muted"));dc.DrawText(label,new(Left-10-label.Width,y-label.Height/2));
        }
        var step=Step;var width=Math.Clamp(step*0.7,3,30);
        if(HoverIndex>=0) dc.DrawRoundedRectangle(B("Chart.Highlight"),null,new Rect(Left+HoverIndex*step+1,Top,step-2,plotHeight),6,6);
        for(var i=0;i<bars.Count;i++)
        {
            var bar=bars[i];var x=Left+i*step+(step-width)/2;
            var h=bar.Spend<=0?2:Math.Max(2,(double)bar.Spend/max*plotHeight);
            dc.DrawRoundedRectangle(B(bar.Spend<=0?"Bg.Track":"Chart.Input"),null,new Rect(x,Top+plotHeight-h,width,h),2,2);
            if(bar.Label.Length>0) { var label=Text(bar.Label,11,B("Text.Muted"));dc.DrawText(label,new(Math.Clamp(x+width/2-label.Width/2,Left,ActualWidth-label.Width),Top+plotHeight+8)); }
        }
    }
    protected override void FillTip(int index,StackPanel panel,Func<string,double,string,FontWeight?,TextBlock> line)
    {
        var bar=Bars![index];
        line(bar.Title,12,"Text.Secondary",null);line(bar.SpendText,15,"Text.Primary",FontWeights.SemiBold).Margin=new Thickness(0,2,0,4);
        panel.Children.Add(Row(null,Labels?["requests"]??"Requests",bar.RequestsText));panel.Children.Add(Row(null,"BYOK",bar.ByokText));
    }
}
