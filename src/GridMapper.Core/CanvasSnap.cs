namespace GridMapper.Core;

public sealed record CanvasSnap
{
    public bool Enabled { get; }
    public int GridSize { get; }
    public CanvasSnap(bool enabled = false, int gridSize = 8)
    {
        ModelLimits.Require(gridSize is >= 1 and <= ModelLimits.MaxDimension, ModelError.InvalidSnap, "Шаг привязки: от 1 до 16384 px.");
        Enabled = enabled; GridSize = gridSize;
    }
    public (int X, int Y) Apply(int x, int y) => Enabled ? (Round(x), Round(y)) : (x, y);
    private int Round(int value) => (int)Math.Clamp(Math.Round((decimal)value / GridSize, MidpointRounding.AwayFromZero) * GridSize, int.MinValue, int.MaxValue);
}
