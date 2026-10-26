using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using GridMapper.Core;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    internal Flyout PhysicalPresetsFlyout { get; private set; } = null!;
    internal Flyout ResolutionPresetsFlyout { get; private set; } = null!;

    private void AddGeometryPair(string leftLabel, TextBox left, string rightLabel, TextBox right, bool? physical)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,28"), ColumnSpacing = 8 };
        var first = new StackPanel { Spacing = 4 }; AddField(first, leftLabel, left); row.Children.Add(first);
        var second = new StackPanel { Spacing = 4 }; AddField(second, rightLabel, right); Grid.SetColumn(second, 1); row.Children.Add(second);
        if (physical is { } millimetres)
        {
            var content = new StackPanel { Spacing = 12, Width = 302 };
            var flyout = new Flyout { Content = content, Placement = PlacementMode.Right };
            if (millimetres) PhysicalPresetsFlyout = flyout; else ResolutionPresetsFlyout = flyout;
            var button = new Button { Name = millimetres ? "PhysicalPresets" : "ResolutionPresets", Content = ActionIcon("Profiles"),
                Width = 28, Height = 32, MinHeight = 32, Padding = new Thickness(6), VerticalAlignment = VerticalAlignment.Bottom, Flyout = flyout };
            DescribeIcon(button, L.T(millimetres ? "presets.physical" : "presets.resolution"));
            flyout.Opening += (_, _) => BuildPresetChoices(content, millimetres, flyout);
            Grid.SetColumn(button, 2); row.Children.Add(button);
        }
        _gridPanel.Children.Add(row);
    }

    private void BuildPresetChoices(StackPanel content, bool physical, Flyout flyout)
    {
        content.Children.Clear();
        content.Children.Add(new TextBlock { Text = L.T(physical ? "presets.physical" : "presets.resolution"), FontSize = 17, FontWeight = FontWeight.SemiBold });
        if (!HasScreens || RegularGrid.Detect(SelectedScreen) is not { } grid) return;
        if (physical)
        {
            var choices = new WrapPanel { Orientation = Orientation.Horizontal, Width = 302 };
            foreach (var preset in PanelPresets.Sizes)
            {
                var card = new StackPanel { Spacing = 7, HorizontalAlignment = HorizontalAlignment.Center };
                var shape = new Grid { Width = 70, Height = 30 };
                var factor = Math.Min(48d / (double)preset.WidthMm, 30d / (double)preset.HeightMm);
                shape.Children.Add(new Border { Width = (double)preset.WidthMm * factor, Height = (double)preset.HeightMm * factor,
                    Background = Brush.Parse("#646080"), BorderBrush = Brush.Parse("#B3A9CF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(1),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                card.Children.Add(shape);
                card.Children.Add(new TextBlock { Text = SizeLabel(preset.WidthMm, preset.HeightMm), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
                var button = new Button { Name = "SizePreset_" + preset.Id, Content = card, Width = 94, Height = 76, Margin = new Thickness(3), Padding = new Thickness(5) };
                button.Classes.Set("active-profile", grid.Type.WidthMm == preset.WidthMm && grid.Type.HeightMm == preset.HeightMm);
                var settings = PanelPresets.ForSize(grid.Settings, preset);
                PreparePresetButton(button, settings, flyout); choices.Children.Add(button);
            }
            content.Children.Add(choices);
        }
        else
        {
            content.Children.Add(new TextBlock { Text = SizeLabel(grid.Type.WidthMm, grid.Type.HeightMm) + " mm", Classes = { "muted" } });
            var presets = PanelPresets.Resolutions(grid.Type.WidthMm, grid.Type.HeightMm);
            if (presets.Count == 0)
                content.Children.Add(new TextBlock { Name = "NoResolutionPresets", Text = L.T("presets.custom"), TextWrapping = TextWrapping.Wrap, MaxWidth = 302, Classes = { "muted" } });
            foreach (var preset in presets)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("96,*"), ColumnSpacing = 10 };
                row.Children.Add(new TextBlock { Text = "P" + preset.Pitch, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                var dimensions = new TextBlock { Text = $"{preset.WidthPx} × {preset.HeightPx} px", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
                Grid.SetColumn(dimensions, 1); row.Children.Add(dimensions);
                var button = new Button { Name = "ResolutionPreset_" + preset.Id, Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 9) };
                button.Classes.Set("active-profile", grid.Type.WidthPx == preset.WidthPx && grid.Type.HeightPx == preset.HeightPx);
                PreparePresetButton(button, grid.Settings with { WidthPx = preset.WidthPx, HeightPx = preset.HeightPx }, flyout);
                content.Children.Add(button);
            }
        }
    }

    private void PreparePresetButton(Button button, GridSettings settings, Flyout flyout)
    {
        var id = _screenId;
        try { ModelLimits.ValidateRaster((long)settings.WidthPx * settings.Columns, (long)settings.HeightPx * settings.Rows); }
        catch (ArgumentException) { button.IsEnabled = false; ToolTip.SetTip(button, L.T("presets.limit")); }
        button.Click += (_, _) =>
        {
            if (_busy || HasPointerEdit || !_objectsSelected || id != _screenId || !button.IsEnabled) return;
            if (RegularGrid.Detect(SelectedScreen) is not { } current) return;
            try { ModelLimits.ValidateRaster((long)settings.WidthPx * current.Columns, (long)settings.HeightPx * current.Rows); }
            catch (ArgumentException) { SetStatus(L.T("presets.limit")); return; }
            EndEditGroup();
            // Set the complete pair before validation/rendering: no intermediate
            // square frame, half-applied preset or extra history step reaches NDI.
            _syncingControls = true;
            try
            {
                _gridFields[0].Text = settings.WidthMm.ToString(CultureInfo.InvariantCulture);
                _gridFields[1].Text = settings.HeightMm.ToString(CultureInfo.InvariantCulture);
                _gridFields[2].Text = settings.WidthPx.ToString(CultureInfo.InvariantCulture);
                _gridFields[3].Text = settings.HeightPx.ToString(CultureInfo.InvariantCulture);
            }
            finally { _syncingControls = false; }
            ApplyAutomatic(); flyout.Hide();
        };
    }

    private static string SizeLabel(decimal width, decimal height) => width.ToString("0.###", CultureInfo.InvariantCulture) + " × " + height.ToString("0.###", CultureInfo.InvariantCulture);
}
