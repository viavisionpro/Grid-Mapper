using Avalonia.Controls;
using Avalonia.Threading;
using GridMapper.Core;
using GridMapper.Rendering;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    private IDisposable? _canvasNoticeTimer;
    private bool _noticeOpen;
    private readonly Dictionary<TextBox, string> _canvasRejected = [];
    private void ShowNotice(string message)
    {
        if (_noticeOpen || !IsVisible) return;
        _noticeOpen = true;
        Dispatcher.UIThread.Post(async () =>
        {
            try { if (IsVisible) await _dialogs.ShowNoticeAsync(this, L.Error(message)); }
            finally { _noticeOpen = false; }
        });
    }
    private void ChangeHistory(bool undo)
    {
        _modeSelections[_document.Project.Mode] = _screenId;
        var previousMode = _document.Project.Mode;
        var previousId = _screenId;
        var previousSelection = _objectsSelected;
        if (!(undo ? _document.Undo() : _document.Redo())) return;
        try
        {
            EnsureScreenSelection();
            if (_document.Project.Mode != previousMode) _objectsSelected = HasScreens;
            var rendered = Render(_document.Project, SelectedScreen);
            LoadControls(); UsePreview(rendered); SetStatus(L.T(undo ? "ui100" : "ui101"));
        }
        catch (Exception error) when (error is RenderLimitException or OutOfMemoryException)
        {
            if (undo) _document.Redo(); else _document.Undo();
            _screenId = previousId; _objectsSelected = previousSelection; ResourceFailure(error);
        }
    }
    private void ResourceFailure(Exception error)
    {
        // Candidate renders run before Execute/Reset: retain the last valid document,
        // release failed candidates and restore editable fields instead of keeping a draft.
        LoadControls();
        var message = error is OutOfMemoryException ? L.T("safety.memory") : L.Error(error.Message);
        SetStatus(message); ShowNotice(message);
    }
    private void ValidatePreviewBudget(ProjectModel project, ScreenModel screen)
    {
        var sources = project.Mode == ProjectMode.Led && _viewMode.SelectedIndex == 1 ? project.Screens.Where(s => project.Canvas.Placements.Any(p => p.ScreenId == s.Id && !p.IsHidden)) : new[] { screen };
        var bytes = sources.Sum(s => (long)s.Geometry.Bounds.Width * s.Geometry.Bounds.Height * 4);
        RenderSafety.CheckPreparedBytes(bytes);
    }
    private bool IsCanvasDimension(TextBox? field) => field == _canvasWidth || field == _canvasHeight;
    private void ApplyCanvasDimension(TextBox field, bool commit)
    {
        _canvasNoticeTimer?.Dispose();
        var text = field.Text ?? "";
        if (_canvasRejected.GetValueOrDefault(field) != text) _canvasRejected.Remove(field);
        try
        {
            var value = NumericExpression.Evaluate(text);
            if (value < 1 || value > ModelLimits.MaxDimension)
            {
                Reject(L.F("canvas.limit", ModelLimits.MaxDimension), live: true); return;
            }
            if (decimal.Truncate(value) != value) throw new NumericExpressionException(L.T("canvas.integer"));
            var before = _document.Project;
            var next = CanvasEditing.Resize(before, field == _canvasWidth ? (int)value : before.Canvas.WidthPx,
                field == _canvasHeight ? (int)value : before.Canvas.HeightPx);
            if (_editBaseline is not null && SameCanvas(next.Canvas, _editBaseline.Canvas)
                && next.Screens.SequenceEqual(_editBaseline.Screens)) next = _editBaseline;
            if (!ReferenceEquals(before, next))
            {
                var rendered = Render(next, SelectedScreen);
                _document.Execute(new PreparedEdit(next), _editKey); UsePreview(rendered);
            }
            var index = field == _canvasWidth ? 26 : 27;
            _syncingControls = true;
            try
            {
                if (commit) { field.Text = ((int)value).ToString(System.Globalization.CultureInfo.InvariantCulture); field.CaretIndex = field.Text.Length; }
                if (_loadedText.Length > index) _loadedText[index] = field.Text ?? "";
            }
            finally { _syncingControls = false; }
            _canvasRejected.Remove(field); UpdateActions(); SetStatus(L.T("ui072"));
            if (commit) WarnCanvasCrop();
        }
        catch (NumericExpressionException error) { Reject(error.Message, live: false); }
        catch (Exception error) when (error is RenderLimitException or OutOfMemoryException) { ResourceFailure(error); }

        void Reject(string message, bool live)
        {
            SetStatus(message); UpdateActions();
            void Notify()
            {
                if (field.Text != text || _canvasRejected.GetValueOrDefault(field) == text) return;
                _canvasRejected[field] = text; ShowNotice(message);
            }
            if (commit) Notify();
            else if (live) _canvasNoticeTimer = DispatcherTimer.RunOnce(Notify, TimeSpan.FromMilliseconds(500));
        }
    }
    private void WarnCanvasCrop()
    {
        var project = _document.Project;
        var count = project.Screens.Count(s => project.Canvas.Placements.Any(p => p.ScreenId == s.Id && !p.IsHidden)
            && (s.Geometry.Bounds.Width > project.Canvas.WidthPx || s.Geometry.Bounds.Height > project.Canvas.HeightPx));
        if (count > 0) ShowNotice(L.F("canvas.crop", count));
    }
}
