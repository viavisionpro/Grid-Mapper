using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;

namespace GridMapper.Desktop;

// All edits are local to the popup. Only Applied publishes a colour to the owner.
internal sealed class CompactColourEditor : StackPanel
{
    private readonly ColorSpectrum _spectrum = new() { Name = "ColourSpectrum", Components = ColorSpectrumComponents.SaturationValue, Width = 240, Height = 124 };
    private readonly ColorSlider _hue = new() { Name = "ColourHue", ColorModel = ColorModel.Hsva, ColorComponent = ColorComponent.Component1, Height = 20, IsRoundingEnabled = false, IsPerceptive = true, IsAlphaVisible = false };
    private readonly TextBox _hex = new() { Name = "ColourHex", PlaceholderText = "RRGGBBAA", MaxLength = 9 };
    private readonly NumericUpDown _alpha = new() { Name = "ColourOpacity", Minimum = 0, Maximum = 100, Increment = 1, FormatString = "0", Width = 64, ShowButtonSpinner = false };
    private readonly Border _sample = new() { Width = 32, Height = 32, CornerRadius = new CornerRadius(2) };
    private readonly Button _apply = new() { Name = "ApplyColour", Content = L.T("ui172") };
    private bool _syncing;
    private Color _colour;
    private HsvColor _hsv;
    public event EventHandler? Applied;
    public event EventHandler? Cancelled;
    public Color Color { get => _colour; set => SetColour(value, value.ToHsv()); }
    public CompactColourEditor()
    {
        Name = "CompactColourEditor"; Width = 240; Spacing = 8;
        Children.Add(_spectrum); Children.Add(_hue);
        var hexRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,32"), ColumnSpacing = 8 };
        hexRow.Children.Add(_hex); var sample = new Grid(); sample.Children.Add(new TransparencyBackground()); sample.Children.Add(_sample);
        Grid.SetColumn(sample, 1); hexRow.Children.Add(sample); Children.Add(hexRow);
        ToolTip.SetTip(_hex, L.T("ui173"));
        var alphaRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,64,Auto"), ColumnSpacing = 6 };
        alphaRow.Children.Add(new TextBlock { Text = L.T("ui081"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_alpha, 1); alphaRow.Children.Add(_alpha);
        var percent = new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(percent, 2); alphaRow.Children.Add(percent); Children.Add(alphaRow);
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        var cancel = new Button { Name = "CancelColour", Content = L.T("ui029"), HorizontalAlignment = HorizontalAlignment.Stretch };
        _apply.HorizontalAlignment = HorizontalAlignment.Stretch; actions.Children.Add(cancel); Grid.SetColumn(_apply, 1); actions.Children.Add(_apply); Children.Add(actions);
        cancel.Click += (_, _) => Cancelled?.Invoke(this, EventArgs.Empty);
        _apply.Click += (_, _) => { if (_apply.IsEnabled) Applied?.Invoke(this, EventArgs.Empty); };
        _spectrum.ColorChanged += (_, _) => { if (!_syncing) SetColour(_spectrum.HsvColor.ToRgb(), _spectrum.HsvColor); };
        _hue.ColorChanged += (_, _) => { if (!_syncing) { var hsv = new HsvColor(_hsv.A, _hue.HsvColor.H, _hsv.S, _hsv.V); SetColour(hsv.ToRgb(), hsv); } };
        _alpha.ValueChanged += (_, _) => { if (!_syncing && _alpha.Value is { } alpha) { var color = Avalonia.Media.Color.FromArgb((byte)Math.Round(alpha * 255 / 100), _colour.R, _colour.G, _colour.B); SetColour(color, new HsvColor(color.A / 255d, _hsv.H, _hsv.S, _hsv.V)); } };
        _hex.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            var text = (_hex.Text ?? "").Trim().TrimStart('#');
            var valid = text.Length is 6 or 8 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
            _apply.IsEnabled = valid; _hex.BorderBrush = Brush.Parse(valid ? "#66666E" : "#B94D60");
            if (!valid) return;
            var value = uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var colour = text.Length == 8 ? Avalonia.Media.Color.FromArgb((byte)value, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8))
                : Avalonia.Media.Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
            SetColour(colour, colour.ToHsv(), keepHex: true);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Cancelled?.Invoke(this, EventArgs.Empty); e.Handled = true; }
            else if (e.Key == Key.Enter) { if (_apply.IsEnabled) Applied?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        };
    }
    private void SetColour(Color colour, HsvColor hsv, bool keepHex = false)
    {
        _syncing = true;
        try
        {
            _colour = colour; _hsv = hsv; _spectrum.HsvColor = hsv; _hue.HsvColor = hsv;
            _sample.Background = new SolidColorBrush(colour); _alpha.Value = colour.A * 100m / 255;
            if (!keepHex) { _hex.Text = $"{colour.R:X2}{colour.G:X2}{colour.B:X2}{colour.A:X2}"; _apply.IsEnabled = true; _hex.BorderBrush = Brush.Parse("#66666E"); }
        }
        finally { _syncing = false; }
    }
}
