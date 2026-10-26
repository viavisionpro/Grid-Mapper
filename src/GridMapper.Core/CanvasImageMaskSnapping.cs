namespace GridMapper.Core;

// Reference artwork uses fractional scene coordinates and a fixed aspect ratio.
// Keep the same anchors, target order and editor-space tolerance as screen snapping.
public static class CanvasImageMaskSnapping
{
    public sealed record Result(CanvasImageMask Mask, IReadOnlyList<CanvasEditing.SnapGuide> Guides);
    private sealed record Target(double X, double Y, double Width, double Height);

    public static Result Snap(ProjectModel project, CanvasImageMask proposed, double tolerance, (int X, int Y)? corner = null)
    {
        if (proposed.IsLocked || !project.Canvas.Snap.Enabled || !double.IsFinite(tolerance) || tolerance <= 0)
            return new(proposed, []);
        if (corner is { } c && (Math.Abs(c.X) != 1 || Math.Abs(c.Y) != 1)) throw new ArgumentOutOfRangeException(nameof(corner));
        var screens = project.Screens.ToDictionary(s => s.Id);
        var targets = project.Canvas.Placements.Where(p => !p.IsHidden).Select(p =>
        {
            var bounds = screens[p.ScreenId].Geometry.Bounds;
            return new Target(p.X, p.Y, bounds.Width, bounds.Height);
        }).Append(new(0, 0, project.Canvas.WidthPx, project.Canvas.HeightPx)).ToArray();
        var result = corner is { } handle ? Resize(proposed, targets, tolerance, handle) : Move(proposed, targets, tolerance);
        var guides = new List<CanvasEditing.SnapGuide>();
        // A proportional resize can align several anchors at once. Only show actual
        // coincidences after solving it, never a second line for an unsatisfied snap.
        foreach (var vertical in new[] { true, false })
        {
            var positions = new HashSet<double>();
            foreach (var target in targets)
                foreach (var anchor in Anchors(vertical ? target.X : target.Y, vertical ? target.Width : target.Height))
                    if (Anchors(vertical ? result.X : result.Y, vertical ? result.Width : result.Height).Any(a => Math.Abs(a - anchor) < .000001)
                        && positions.Add(anchor))
                        guides.Add(new(vertical, anchor, Math.Min(vertical ? result.Y : result.X, vertical ? target.Y : target.X),
                            Math.Max(vertical ? result.Y + result.Height : result.X + result.Width,
                                vertical ? target.Y + target.Height : target.X + target.Width)));
        }
        return new(result, guides.AsReadOnly());
    }

    private static double[] Anchors(double start, double size) => [start, start + size, start + size / 2];

    private static CanvasImageMask Move(CanvasImageMask mask, Target[] targets, double tolerance)
    {
        var x = mask.X; var y = mask.Y; var bestX = tolerance + 1; var bestY = tolerance + 1;
        void Match(double start, double size, double proposed, double movingSize, ref double best, ref double snapped)
        {
            foreach (var anchor in Anchors(start, size))
                foreach (var offset in Anchors(0, movingSize))
                {
                    var candidate = anchor - offset; var distance = Math.Abs(candidate - proposed);
                    if (Math.Abs(candidate) <= 100_000_000 && distance <= tolerance && distance < best)
                    { best = distance; snapped = candidate; }
                }
        }
        foreach (var target in targets)
        {
            Match(target.X, target.Width, mask.X, mask.Width, ref bestX, ref x);
            Match(target.Y, target.Height, mask.Y, mask.Height, ref bestY, ref y);
        }
        return mask.With(x: x, y: y);
    }

    private static CanvasImageMask Resize(CanvasImageMask mask, Target[] targets, double tolerance, (int X, int Y) corner)
    {
        var ratio = mask.Height / mask.Width;
        var fixedX = mask.X + (corner.X < 0 ? mask.Width : 0);
        var fixedY = mask.Y + (corner.Y < 0 ? mask.Height : 0);
        var width = mask.Width; var best = double.PositiveInfinity;
        foreach (var target in targets)
            foreach (var vertical in new[] { true, false })
                foreach (var anchor in Anchors(vertical ? target.X : target.Y, vertical ? target.Width : target.Height))
                    foreach (var fraction in new[] { 0d, 1d, .5 })
                    {
                        var coefficient = (fraction - ((vertical ? corner.X : corner.Y) < 0 ? 1 : 0)) * (vertical ? 1 : ratio);
                        if (coefficient == 0) continue; // The fixed opposite edge cannot change size.
                        var proposedAnchor = (vertical ? mask.X : mask.Y) + fraction * (vertical ? mask.Width : mask.Height);
                        if (Math.Abs(anchor - proposedAnchor) > tolerance) continue;
                        var candidate = (anchor - (vertical ? fixedX : fixedY)) / coefficient;
                        // Limit the displacement of the dragged corner on BOTH axes.
                        // A very flat/tall image must not jump on the other axis.
                        var distance = Math.Abs(candidate - mask.Width) * Math.Max(1, ratio);
                        if (distance > tolerance || distance >= best || candidate < .0001 || candidate > 1_000_000
                            || candidate * ratio > 1_000_000) continue;
                        var x = fixedX - (corner.X < 0 ? candidate : 0); var y = fixedY - (corner.Y < 0 ? candidate * ratio : 0);
                        if (Math.Abs(x) > 100_000_000 || Math.Abs(y) > 100_000_000) continue;
                        best = distance; width = candidate;
                    }
        return mask.With(width: width, x: fixedX - (corner.X < 0 ? width : 0), y: fixedY - (corner.Y < 0 ? width * ratio : 0));
    }
}
