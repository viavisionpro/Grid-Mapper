using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace GridMapper.Desktop;

internal sealed class ExportProgress : Window
{
    private bool _finished;
    public IProgress<double> Progress { get; }
    public ExportProgress(Window owner, CancellationTokenSource cancellation)
    {
        Name = "ExportProgress"; Title = "PNG"; Width = 380; SizeToContent = SizeToContent.Height;
        CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = owner.FontFamily;
        var text = new TextBlock { Text = L.F("export.progress", 0) };
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, IsIndeterminate = true, Height = 10 };
        var cancel = new Button { Name = "CancelExport", Content = L.T("export.cancel"), HorizontalAlignment = HorizontalAlignment.Right };
        var body = new StackPanel { Spacing = 16, Margin = new Thickness(24) }; body.Children.Add(text); body.Children.Add(bar); body.Children.Add(cancel); Content = body;
        cancel.Click += (_, _) => { cancellation.Cancel(); cancel.IsEnabled = false; };
        Closing += (_, e) => { if (!_finished) { cancellation.Cancel(); e.Cancel = true; } };
        Progress = new Progress<double>(value => { if (_finished) return; bar.IsIndeterminate = false; bar.Value = value; text.Text = L.F("export.progress", (int)(value * 100)); });
        Show(owner);
    }
    public void Finish() { _finished = true; Close(); }
}
