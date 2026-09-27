using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AiUsageViewer.Core;

namespace AiUsageViewer.App;
public sealed class UsageChart : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty=DependencyProperty.Register(nameof(ItemsSource),typeof(IReadOnlyList<DailyUsage>),typeof(UsageChart),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<DailyUsage>? ItemsSource { get=>(IReadOnlyList<DailyUsage>?)GetValue(ItemsSourceProperty);set=>SetValue(ItemsSourceProperty,value); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var data=ItemsSource;if(data is null || ActualWidth<80 || ActualHeight<50) return;
        var text=(Brush)FindResource("MutedBrush");var line=new Pen((Brush)FindResource("StrokeBrush"),1);
        var plotWidth=ActualWidth-54;var plotHeight=ActualHeight-28;
        if(data.Count==0) return;
        var first=data.Min(x=>x.Day);var last=data.Max(x=>x.Day);var days=last.DayNumber-first.DayNumber+1;
        var buckets=Math.Min(days,90);var totals=new long[buckets];
        foreach(var entry in data) totals[Math.Min(buckets-1,(entry.Day.DayNumber-first.DayNumber)*buckets/days)]+=entry.Tokens.Total;
        var maximum=Math.Max(1,totals.Max());
        for(var i=0;i<4;i++)
        {
            var y=i*plotHeight/3;
            dc.DrawLine(line,new(42,y),new(ActualWidth,y));
            DrawText(dc,Compact((long)(maximum*(1-i/3d))),new(0,y-5),text,10);
        }
        var step=plotWidth/buckets;
        var brush=(Brush)FindResource("AccentBrush");
        for(var i=0;i<buckets;i++)
        {
            var height=totals[i]/(double)maximum*plotHeight;
            var width=Math.Clamp(step-3,2,36);
            dc.DrawRoundedRectangle(brush,null,new(44+i*step+(step-width)/2,plotHeight-height,width,height),3,3);
        }
        DrawText(dc,first.ToString("dd MMM"),new(44,plotHeight+9),text,10);
        if(days>1) DrawText(dc,last.ToString("dd MMM"),new(ActualWidth-45,plotHeight+9),text,10);
    }
    private void DrawText(DrawingContext dc,string value,Point point,Brush brush,double size)=>dc.DrawText(new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip),point);
    private static string Compact(long value)=>value>=1_000_000?$"{value/1_000_000d:0.#}m":value>=1000?$"{value/1000d:0.#}k":value.ToString(CultureInfo.InvariantCulture);
}
