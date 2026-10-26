using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace GridMapper.Desktop;

internal static class NdiLogo
{
    private static readonly Geometry[] Paths = Load();
    private static Geometry[] Load()
    {
        using var source = AssetLoader.Open(new Uri("avares://GridMapper.Desktop/Assets/NdiLogo.svg"));
        return XDocument.Load(source).Descendants(XName.Get("path", "http://www.w3.org/2000/svg"))
            .Select(p => Geometry.Parse(p.Attribute("d")!.Value)).ToArray();
    }
    internal static Control Create()
    {
        // Preserve the official artwork, its proportions and its clear space.
        var canvas = new Canvas { Width = 1047, Height = 550 };
        foreach (var path in Paths) canvas.Children.Add(new Avalonia.Controls.Shapes.Path { Data = path, Fill = Brush.Parse("#F1F1F1") });
        return new Viewbox { Name = "NdiOfficialLogo", Width = 32, Height = 18, Stretch = Stretch.Uniform, Child = canvas };
    }
}
