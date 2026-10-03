using System.Windows;
using System.Windows.Controls;
using AiUsageViewer.Core;

namespace AiUsageViewer.App;
public partial class OpenRouterView : UserControl
{
    public static readonly DependencyProperty FormatProperty=DependencyProperty.Register(nameof(Format),typeof(UiFormat),typeof(OpenRouterView),new FrameworkPropertyMetadata(null));
    public UiFormat? Format { get=>(UiFormat?)GetValue(FormatProperty);set=>SetValue(FormatProperty,value); }
    public OpenRouterView()
    {
        InitializeComponent();
        DataContextChanged+=(_,_)=>UpdateFormat();
        SizeChanged+=(_,_)=>Summary.Columns=ActualWidth<820?2:4;
    }
    private void UpdateFormat()
    {
        if(DataContext is OpenRouterViewModel model) { Format=UiFormat.For(model.L.Language);model.PropertyChanged+=(_,e)=> { if(e.PropertyName==nameof(model.L)) Format=UiFormat.For(model.L.Language); }; }
    }
}
