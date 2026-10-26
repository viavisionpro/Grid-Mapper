using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GridMapper.Core;
using GridMapper.Rendering;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    private readonly Dictionary<Guid, NdiOutput> _ndiOutputs = [];
    private readonly HashSet<Guid> _ndiTransitions = [];
    private readonly Dictionary<Guid, string> _ndiErrors = [];
    private readonly List<Task> _ndiRetiring = [];
    private bool _ndiStoppingAll;
    private bool _ndiClosed;
    internal Func<string, INdiSender> NdiSenderFactory { get; set; } = name => NdiRuntime.Get().CreateSender(name);
    internal int NdiOutputCount => _ndiOutputs.Count;
    // Native codec memory is additional. Bound the application's persistent raw
    // buffers; edits temporarily require one extra raster per changing output.
    private const long NdiBufferBudget = 256L * 1024 * 1024;
    private static long NdiBytes(ScreenModel screen) => (long)screen.Geometry.Bounds.Width * screen.Geometry.Bounds.Height * 4;
    private NdiSnapshot NdiSnapshotFor(ScreenModel screen, ProjectModel? project = null)
    {
        project ??= _document.Project;
        var ownerMode = project.Screens.Any(s => s.Id == screen.Id) ? project.Mode
            : project.Mode == ProjectMode.Led ? ProjectMode.Projection : ProjectMode.Led;
        var projection=ownerMode == ProjectMode.Projection ? screen.Projection??ProjectionSettings.Default : null;
        if ((projection?.WidthPx??screen.Geometry.Bounds.Width) % 2 != 0)
            throw new InvalidOperationException(L.T("ui243"));
        _logoImages.TryGetValue(screen.Style.Logo.SourcePath, out var logo);
        if (projection is null && screen.Style.Logo.Enabled && screen.Style.Logo.SourcePath.Length > 0 && logo is null)
            throw new InvalidOperationException(L.T("ui244"));
        var total = _ndiOutputs.Where(p => p.Key != screen.Id).Sum(p => p.Value.Latest.Bytes) + (projection is null ? NdiBytes(screen) : (long)projection.WidthPx*projection.HeightPx*4);
        if (total > NdiBufferBudget) throw new InvalidOperationException(L.T("ui245"));
        return new(screen, projection is null ? logo : null, projection);
    }
    private async Task ToggleNdiAsync(Guid id)
    {
        if (_ndiClosed || _ndiStoppingAll || _busy || HasPointerEdit || _ndiTransitions.Contains(id)) return;
        _ndiErrors.Remove(id); _ndiTransitions.Add(id); UpdateLayerEditor(id);
        try
        {
            if (_ndiOutputs.Remove(id, out var previous))
            {
                await RetireNdi(previous); SetStatus(L.T("ui246")); return;
            }
            if (HasDraft && !ApplyAutomatic()) return;
            var screen = _document.Project.Screens.FirstOrDefault(s => s.Id == id); if (screen is null) return;
            var output = new NdiOutput(NdiSnapshotFor(screen), NdiSenderFactory, Preferences.Current.NdiFrameRate);
            _ndiOutputs.Add(id, output); UpdateLayerEditor(id); _ = MonitorNdi(id, output);
            await output.Started;
            if (_ndiOutputs.GetValueOrDefault(id) == output) SetStatus(L.F("ndi.started", output.Name, output.Latest.Width, output.Latest.Height, output.FrameRate));
        }
        catch (Exception error)
        { _ndiErrors[id] = error is OutOfMemoryException ? L.T("ui247") : error.Message; SetStatus("NDI: " + _ndiErrors[id]); }
        finally { _ndiTransitions.Remove(id); UpdateLayerEditor(id); }
    }
    private async Task MonitorNdi(Guid id, NdiOutput output)
    {
        string? failure = null;
        try { await output.Completion.ConfigureAwait(false); }
        catch (Exception error) { failure = error.Message; }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_ndiOutputs.GetValueOrDefault(id) != output) return;
            _ndiOutputs.Remove(id); _ndiTransitions.Remove(id); UpdateLayerEditor(id);
            if (!_ndiClosed && failure is not null) { _ndiErrors[id] = failure; SetStatus("NDI: " + failure); UpdateLayerEditor(id); }
        });
    }
    private Task RetireNdi(NdiOutput output)
    {
        output.RequestStop();
        async Task Observe() { try { await output.Completion.ConfigureAwait(false); } catch { /* Reported by MonitorNdi. */ } }
        var task = Observe(); _ndiRetiring.RemoveAll(t => t.IsCompleted); _ndiRetiring.Add(task); _ = ReapNdi(task); return task;
    }
    private async Task ReapNdi(Task task)
    {
        await task.ConfigureAwait(false);
        Dispatcher.UIThread.Post(() => _ndiRetiring.Remove(task));
    }
    private async Task StopAllNdiAsync()
    {
        _ndiStoppingAll = true;
        var outputs = _ndiOutputs.Values.ToArray(); _ndiOutputs.Clear(); _ndiTransitions.Clear(); _ndiErrors.Clear();
        foreach (var output in outputs) _ = RetireNdi(output);
        foreach (var id in _layerEditors.Keys.ToArray()) UpdateLayerEditor(id);
        try { await Task.WhenAll(_ndiRetiring.ToArray()); }
        finally { _ndiRetiring.Clear(); _ndiStoppingAll = false; }
    }
    private void SyncNdiOutputs(ProjectModel project)
    {
        if (_ndiOutputs.Count == 0) return;
        var screens = project.AllScreens.ToDictionary(s => s.Id);
        foreach (var (id, output) in _ndiOutputs.ToArray())
        {
            try
            {
                if (!screens.TryGetValue(id, out var screen)) { _ndiOutputs.Remove(id); _ndiErrors.Remove(id); RetireNdi(output); continue; }
                var snapshot = NdiSnapshotFor(screen, project); _ndiErrors.Remove(id);
                if (!ReferenceEquals(output.Latest.Screen, screen) || !ReferenceEquals(output.Latest.Logo, snapshot.Logo) || output.Latest.Projection != snapshot.Projection) output.Update(snapshot);
            }
            catch (InvalidOperationException error)
            { _ndiErrors[id] = error.Message + L.T("ui248"); SetStatus("NDI: " + _ndiErrors[id]); }
            UpdateLayerEditor(id);
        }
    }
    private void UpdateNdiButton(Guid id, Button button)
    {
        var active = _ndiOutputs.ContainsKey(id); var transition = _ndiTransitions.Contains(id);
        button.IsEnabled = !_ndiClosed && !_ndiStoppingAll && !_busy && !HasPointerEdit && !transition;
        button.Classes.Set("ndi-active", active && !transition);
        button.Background = Brush.Parse(active ? "#646080" : "#303039");
        button.BorderBrush = Brush.Parse(_ndiErrors.ContainsKey(id) ? "#B66A82" : active ? "#A99BC8" : "#50505C");
        var screen = _document.Project.AllScreens.FirstOrDefault(s => s.Id == id);
        var description = transition ? L.T("ui249") : active ? L.F("ui250", NdiSnapshot.SourceName(screen!), Preferences.Current.NdiFrameRate) : L.F("ui251", Preferences.Current.NdiFrameRate);
        if (_ndiErrors.TryGetValue(id, out var error)) description = "NDI: " + error + (active ? "" : L.T("ui252"));
        DescribeIcon(button, description);
        Avalonia.Automation.AutomationProperties.SetHelpText(button, "NDI® is a registered trademark of Vizrt NDI AB. https://ndi.video/");
    }
}
