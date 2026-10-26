using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Layout;

namespace GridMapper.Desktop;

// Retains the ColorPicker API for existing controls and files; its popup is shared
// with the Solid profile and publishes only explicitly applied colours.
public sealed class LiveColorPicker : ColorPicker
{
    private DropDownButton? _button;
    private Border? _sample;
    private TextBlock? _label;
    private bool _committing;
    internal Flyout? ColourFlyout { get; private set; }
    internal CompactColourEditor? ColourEditor { get; private set; }
    public LiveColorPicker()
    {
        Template = new FuncControlTemplate<ColorPicker>((_, _) =>
        {
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*"), ColumnSpacing = 8 };
            var sample = new Grid { Width = 22, Height = 18, ClipToBounds = true }; sample.Children.Add(new TransparencyBackground());
            _sample = new Border(); sample.Children.Add(_sample); content.Children.Add(sample);
            _label = new TextBlock { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(_label, 1); content.Children.Add(_label);
            _button = new DropDownButton { Name = "ColourPickerButton", Content = content, HorizontalAlignment = HorizontalAlignment.Stretch };
            var editor = new CompactColourEditor(); ColourEditor = editor;
            var flyout = new Flyout { Content = editor, Placement = PlacementMode.Bottom }; ColourFlyout = flyout;
            flyout.Opened += (_, _) => editor.Color = Color;
            editor.Cancelled += (_, _) => flyout.Hide();
            editor.Applied += (_, _) =>
            {
                _committing = true;
                try { Color = editor.Color; }
                finally { _committing = false; flyout.Hide(); }
            };
            _button.Flyout = flyout; UpdateChip(); return _button;
        });
    }
    protected override Type StyleKeyOverride => typeof(ColorPicker);
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ColorProperty) return;
        if (!_committing && ColourFlyout?.IsOpen == true) ColourFlyout.Hide();
        UpdateChip();
    }
    private void UpdateChip()
    {
        if (_sample is not null) _sample.Background = new SolidColorBrush(Color);
        if (_label is not null) _label.Text = Color.A == 255 ? $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}" : $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}{Color.A:X2}";
    }
}
