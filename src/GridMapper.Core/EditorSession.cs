namespace GridMapper.Core;

public sealed record DocumentCheckpoint(Guid DocumentId, Guid Revision, ProjectModel Project);

public sealed class EditorSession
{
    public const int MaxUndoSteps = 100;
    public const long MaxMaskHistoryBytes = 24 * 1024 * 1024;
    private sealed record Entry(string Description, DocumentCheckpoint Before, DocumentCheckpoint After, object? MergeKey);
    private readonly List<Entry> _history = [];
    private int _position;
    private Guid _savedRevision;
    private bool _allowMerge;
    public ProjectModel Project { get; private set; }
    public Guid DocumentId { get; private set; } = Guid.NewGuid();
    public Guid Revision { get; private set; } = Guid.NewGuid();
    public bool IsDirty => Revision != _savedRevision;
    public bool CanUndo => _position > 0;
    public bool CanRedo => _position < _history.Count;
    public string? UndoDescription => CanUndo ? _history[_position - 1].Description : null;
    public string? RedoDescription => CanRedo ? _history[_position].Description : null;

    public EditorSession(ProjectModel project)
    {
        ArgumentNullException.ThrowIfNull(project);
        Project = project;
        _savedRevision = Revision;
    }

    public DocumentCheckpoint Capture()
    {
        _allowMerge = false;
        return CurrentCheckpoint();
    }

    private DocumentCheckpoint CurrentCheckpoint() => new(DocumentId, Revision, Project);

    public bool Execute(IProjectCommand command, object? mergeKey = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        var next = command.Apply(Project);
        ArgumentNullException.ThrowIfNull(next);
        if (ReferenceEquals(next, Project)) return false;
        var before = CurrentCheckpoint();
        var after = new DocumentCheckpoint(DocumentId, Guid.NewGuid(), next);
        if (_allowMerge && mergeKey is not null && _position == _history.Count && _position > 0
            && Equals(_history[^1].MergeKey, mergeKey) && Revision != _savedRevision)
        {
            var entry = _history[^1];
            if (ReferenceEquals(next, entry.Before.Project))
            {
                _history.RemoveAt(--_position);
                Restore(entry.Before);
                _allowMerge = false;
            }
            else
            {
                _history[^1] = entry with { After = after };
                Restore(after);
            }
            return true;
        }
        if (_position < _history.Count) _history.RemoveRange(_position, _history.Count - _position);
        _history.Add(new(command.Description, before, after, mergeKey));
        _position++;
        if (_history.Count > MaxUndoSteps) { _history.RemoveAt(0); _position--; }
        Restore(after);
        TrimMaskHistory();
        _allowMerge = mergeKey is not null;
        return true;
    }

    private void TrimMaskHistory()
    {
        // Encoded sources are shared by all transform/opacity revisions. Repeated
        // replacement of large reference art must not retain 100 separate images.
        long Size() => _history.SelectMany(e => new[]
            {
                e.Before.Project.Canvas.ImageMask?.Image, e.After.Project.Canvas.ImageMask?.Image,
                e.Before.Project.InactiveLayout?.Canvas.ImageMask?.Image, e.After.Project.InactiveLayout?.Canvas.ImageMask?.Image
            })
            .OfType<EmbeddedLogo>().DistinctBy(i => i.Key).Sum(i => i.DataBase64.Length / 4L * 3);
        while (_history.Count > 1 && Size() > MaxMaskHistoryBytes)
        { _history.RemoveAt(0); _position--; }
    }

    public bool Undo()
    {
        _allowMerge = false;
        if (!CanUndo) return false;
        Restore(_history[--_position].Before);
        return true;
    }

    public bool Redo()
    {
        _allowMerge = false;
        if (!CanRedo) return false;
        Restore(_history[_position++].After);
        return true;
    }

    public bool MarkSaved(DocumentCheckpoint checkpoint)
    {
        if (checkpoint.DocumentId != DocumentId) return false;
        _allowMerge = false;
        _savedRevision = checkpoint.Revision;
        return true;
    }

    public void Reset(ProjectModel project)
    {
        ArgumentNullException.ThrowIfNull(project);
        Project = project;
        DocumentId = Guid.NewGuid();
        Revision = Guid.NewGuid();
        _savedRevision = Revision;
        _history.Clear();
        _position = 0;
        _allowMerge = false;
    }

    private void Restore(DocumentCheckpoint checkpoint)
    {
        Project = checkpoint.Project;
        Revision = checkpoint.Revision;
    }
}
