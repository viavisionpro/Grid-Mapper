using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    private void AddProjectionResolutionPair(StackPanel parent)
    {
        var row=new Grid { ColumnDefinitions=new("*,*,28"),ColumnSpacing=8 };
        var width=new StackPanel { Spacing=4 };AddField(width,L.T("projection.widthPx"),_projectionWidthPx);row.Children.Add(width);
        var height=new StackPanel { Spacing=4 };AddField(height,L.T("projection.heightPx"),_projectionHeightPx);Grid.SetColumn(height,1);row.Children.Add(height);
        var choices=new StackPanel { Spacing=8,Width=302 };
        var flyout=new Flyout { Content=choices,Placement=PlacementMode.Right };
        var button=new Button { Name="ProjectionResolutionPresets",Content=ActionIcon("Profiles"),Width=28,Height=32,MinHeight=32,
            Padding=new Thickness(6),VerticalAlignment=VerticalAlignment.Bottom,Flyout=flyout };
        DescribeIcon(button,L.T("projection.presets"));Grid.SetColumn(button,2);row.Children.Add(button);parent.Children.Add(row);
        flyout.Opening+=(_,_)=>
        {
            choices.Children.Clear();choices.Children.Add(new TextBlock { Text=L.T("projection.presets"),FontSize=17,FontWeight=FontWeight.SemiBold });
            var screenId=_screenId;
            foreach(var preset in new[]{("FHD","FHD",1920,1080),("WUXGA","WUXGA",1920,1200),("4KUHD","4K UHD",3840,2160),("WQUXGA","WQUXGA",3840,2400),("4KDCI","4K DCI",4096,2160)})
            {
                var label=new Grid { ColumnDefinitions=new("96,*"),ColumnSpacing=10 };
                label.Children.Add(new TextBlock { Text=preset.Item2,FontWeight=FontWeight.SemiBold,VerticalAlignment=VerticalAlignment.Center });
                var dimensions=new TextBlock { Text=$"{preset.Item3} \u00d7 {preset.Item4} px",FontSize=12,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right };
                Grid.SetColumn(dimensions,1);label.Children.Add(dimensions);
                var choice=new Button { Name="ProjectionPreset_"+preset.Item1,Content=label,HorizontalContentAlignment=HorizontalAlignment.Stretch,
                    HorizontalAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(12,9) };
                choice.Classes.Set("active-profile",Projection.WidthPx==preset.Item3 && Projection.HeightPx==preset.Item4);
                choice.Click+=(_,_)=>
                {
                    if(_busy || HasPointerEdit || !IsProjection || !_objectsSelected || screenId!=_screenId)return;
                    EndEditGroup();_projectionSync=true;
                    try { _projectionWidthPx.Text=preset.Item3.ToString(CultureInfo.InvariantCulture);_projectionHeightPx.Text=preset.Item4.ToString(CultureInfo.InvariantCulture); }
                    finally { _projectionSync=false; }
                    ApplyProjectionAutomatic(normalize:true);flyout.Hide();
                };
                choices.Children.Add(choice);
            }
        };
    }
}
