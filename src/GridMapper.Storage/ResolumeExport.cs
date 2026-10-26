using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using GridMapper.Core;

namespace GridMapper.Storage;

/// <summary>Resolume Arena 7 Advanced Output preset: one virtual output, 1:1 Canvas slices.</summary>
public static class ResolumeExport
{
    public static byte[] Serialize(ProjectModel project, string name)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Mode != ProjectMode.Led) throw new ArgumentException("Resolume export requires LED Canvas.", nameof(project));
        var visible = project.Canvas.Placements.Where(p => !p.IsHidden).ToArray();
        if (visible.Length == 0) throw new ArgumentException("Canvas has no visible screens.", nameof(project));
        var screens = project.Screens.ToDictionary(s => s.Id);
        var canvas = project.Canvas;
        var layers = new XElement("layers");
        // Canvas order is bottom to top; Resolume layers use the same paint order.
        for (var i = 0; i < visible.Length; i++)
        {
            var placement = visible[i]; var screen = screens[placement.ScreenId];
            var bounds = screen.Geometry.Bounds;
            double x = placement.X, y = placement.Y, w = bounds.Width, h = bounds.Height;
            XElement Vertices() => new("vertices", Enumerable.Range(0, 16).Select(n => Vertex(x + w * (n % 4) / 3, y + h * (n / 4) / 3)));
            XElement Rect(string tag) => new(tag, new XAttribute("orientation", 0),
                Vertex(x, y), Vertex(x + w, y), Vertex(x + w, y + h), Vertex(x, y + h));
            var output = new XElement("Params", new XAttribute("name", "Output"), Param("Flip", "UINT8", 0));
            foreach (var channel in new[] { "Brightness", "Contrast", "Red", "Green", "Blue" }) output.Add(Range(channel, 0, -1, 1));
            output.Add(Param("Is Key", "BOOL", 0), Param("Black BG", "BOOL", 0));
            foreach (var channel in new[] { "BRed", "BGreen", "BBlue" }) output.Add(Range(channel, 0, 0, .4));
            layers.Add(new XElement("Slice", new XAttribute("uniqueId", i + 2),
                new XElement("Params", new XAttribute("name", "Common"), Param("Name", "STRING", screen.Name), Param("Enabled", "BOOL", 1)),
                new XElement("Params", new XAttribute("name", "Input"), Choice("Input Source", "0:1"),
                    Param("Input Opacity", "BOOL", 1), Param("Input Bypass/Solo", "BOOL", 1), Param("SoftEdgeEnable", "BOOL", 0)),
                output, Rect("InputRect"), Rect("OutputRect"),
                new XElement("Warper", new XElement("Params", new XAttribute("name", "Warper"), Choice("Point Mode", "PM_LINEAR"), Param("Flip", "UINT8", 0)),
                    new XElement("BezierWarper", new XAttribute("controlWidth", 4), new XAttribute("controlHeight", 4), Vertices()),
                    new XElement("Homography", new XElement("src", Rect("r").Elements()), new XElement("dst", Rect("r").Elements())))));
        }
        var screenOutput = new XElement("Params", new XAttribute("name", "Output"), Range("Opacity", 1, 0, 1));
        foreach (var channel in new[] { "Brightness", "Contrast", "Red", "Green", "Blue" }) screenOutput.Add(Range(channel, 0, -1, 1));
        var device = new XElement("OutputDeviceVirtual", new XAttribute("name", name), new XAttribute("deviceId", "VirtualScreen 1"),
            new XAttribute("width", canvas.WidthPx), new XAttribute("height", canvas.HeightPx),
            new XElement("Params", new XAttribute("name", "Params"), Range("Width", canvas.WidthPx, 1, 32768), Range("Height", canvas.HeightPx, 1, 32768)));
        var canvasScreen = new XElement("Screen", new XAttribute("name", name), new XAttribute("uniqueId", 1),
            new XElement("Params", new XAttribute("name", "Params"), Param("Name", "STRING", name), Param("Enabled", "BOOL", 1), Param("Hidden", "BOOL", 0)),
            screenOutput, new XElement("guides"), layers, new XElement("OutputDevice", device));
        var setup = new XElement("ScreenSetup", new XAttribute("name", "ScreenSetup"),
            new XElement("Params", new XAttribute("name", "ScreenSetupParams")),
            new XElement("CurrentCompositionTextureSize", new XAttribute("width", canvas.WidthPx), new XAttribute("height", canvas.HeightPx)),
            new XElement("screens", canvasScreen));
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("XmlState", new XAttribute("name", name),
            new XElement("versionInfo", new XAttribute("name", "Resolume Arena"), new XAttribute("majorVersion", 7), new XAttribute("minorVersion", 13), new XAttribute("microVersion", 2), new XAttribute("revision", 17774)), setup));
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true })) document.Save(writer);
        return stream.ToArray();
    }
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static XElement Vertex(double x, double y) => new("v", new XAttribute("x", Number(x)), new XAttribute("y", Number(y)));
    private static XElement Param(string name, string type, object value) => new("Param", new XAttribute("name", name), new XAttribute("T", type), new XAttribute("default", value), new XAttribute("value", value));
    private static XElement Choice(string name, string value) => new("ParamChoice", new XAttribute("name", name), new XAttribute("default", value), new XAttribute("value", value), new XAttribute("storeChoices", 0));
    private static XElement Range(string name, double value, double min, double max) => new("ParamRange", new XAttribute("name", name), new XAttribute("T", "DOUBLE"), new XAttribute("default", Number(value)), new XAttribute("value", Number(value)),
        new XElement("PhaseSourceStatic", new XAttribute("name", "PhaseSourceStatic"), new XAttribute("phase", Number((value - min) / (max - min)))),
        new XElement("BehaviourDouble", new XAttribute("name", "BehaviourDouble")),
        new[] { "defaultRange", "minMax", "startStop" }.Select(n => new XElement("ValueRange", new XAttribute("name", n), new XAttribute("min", Number(min)), new XAttribute("max", Number(max)))));
}
