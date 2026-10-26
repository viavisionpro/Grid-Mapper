using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Text;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    private readonly Queue<(string Path, string Key, TaskCompletionSource<bool> Completion)> _externalOpens = new();
    private bool _externalOpenRunning;
    private bool _externalOpenScheduled;
    private bool _launchClosed;
    private (string Path, string Key, TaskCompletionSource<bool> Completion)? _currentExternalOpen;

    internal Task<bool> OpenLaunchProjectAsync(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0) return Task.FromResult(true);
        if (arguments.Count != 1 || arguments[0].StartsWith("--", StringComparison.Ordinal))
        {
            SetStatus(L.T("ui018"));
            return Task.FromResult(false);
        }
        return RequestExternalProjectAsync(arguments[0]);
    }

    internal Task<bool> OpenActivatedFilesAsync(FileActivatedEventArgs args)
    {
        if (args.Files.Count != 1)
        {
            SetStatus(L.T("ui019"));
            return Task.FromResult(false);
        }
        var path = args.Files[0].TryGetLocalPath();
        if (path is null)
        {
            SetStatus(L.T("ui020"));
            return Task.FromResult(false);
        }
        return RequestExternalProjectAsync(path);
    }

    internal Task<bool> RequestExternalProjectAsync(string path)
    {
        if (_launchClosed) return Task.FromResult(false);
        string key;
        try
        {
            path = Path.GetFullPath(path);
            // AppKit returns decomposed Unicode paths, while CLI/dialog paths may
            // use composed characters. On macOS these identify the same file.
            key = OperatingSystem.IsMacOS() ? path.Normalize(NormalizationForm.FormC) : path;
        }
        catch (Exception error) when (IsFileError(error))
        { SetStatus(L.F("ui021", error.Message)); return Task.FromResult(false); }
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (_currentExternalOpen is { } current && string.Equals(current.Key, key, comparison)) return current.Completion.Task;
        foreach (var pending in _externalOpens)
            if (string.Equals(pending.Key, key, comparison)) return pending.Completion.Task;
        if (_externalOpens.Count >= 32)
        {
            SetStatus(L.T("ui022"));
            return Task.FromResult(false);
        }
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _externalOpens.Enqueue((path, key, completion));
        ScheduleExternalOpen();
        return completion.Task;
    }

    private void ScheduleExternalOpen()
    {
        if (_launchClosed || !IsVisible || _busy || HasPointerEdit || _externalOpenRunning || _externalOpenScheduled || _externalOpens.Count == 0) return;
        _externalOpenScheduled = true;
        Dispatcher.UIThread.Post(async () =>
        {
            _externalOpenScheduled = false;
            if (_launchClosed || !IsVisible || _busy || HasPointerEdit || _externalOpenRunning || _externalOpens.Count == 0) return;
            _externalOpenRunning = true;
            var request = _externalOpens.Dequeue(); _currentExternalOpen = request;
            try { request.Completion.TrySetResult(await OpenProjectAsync(request.Path)); }
            catch (Exception error) { request.Completion.TrySetException(error); }
            finally { _currentExternalOpen = null; _externalOpenRunning = false; ScheduleExternalOpen(); }
        }, DispatcherPriority.Background);
    }

    private void CancelExternalOpens()
    {
        _launchClosed = true;
        while (_externalOpens.TryDequeue(out var request)) request.Completion.TrySetResult(false);
    }
}
