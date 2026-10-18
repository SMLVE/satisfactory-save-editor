using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SatisfactorySaveEditor.Domain.Map;
using SatisfactorySaveEditor.Domain.Structures;

namespace SatisfactorySaveEditor.App.Controls;

/// <summary>
/// Top-down map of save markers with mouse-wheel zoom (around the cursor), drag to pan and click to select.
/// World X maps to screen X and world Y to screen Y; units are centimetres. Markers outside the view are
/// culled and dense layers draw at most one marker per screen pixel cell, so tens of thousands stay fast.
/// </summary>
internal sealed class MapControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<MapMarker>?> MarkersProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlyList<MapMarker>?>(nameof(Markers));

    public static readonly StyledProperty<MapMarker?> SelectedMarkerProperty =
        AvaloniaProperty.Register<MapControl, MapMarker?>(nameof(SelectedMarker),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<IReadOnlyList<MapLine>?> LinesProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlyList<MapLine>?>(nameof(Lines));

    public static readonly StyledProperty<IReadOnlyList<Structure>?> StructuresProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlyList<Structure>?>(nameof(Structures));

    private static readonly Dictionary<StructureKind, IBrush> StructureBrushes = new()
    {
        [StructureKind.Foundation] = new SolidColorBrush(Color.FromArgb(0xA0, 0x55, 0x60, 0x6E)),
        [StructureKind.Ramp] = new SolidColorBrush(Color.FromArgb(0xA0, 0x6A, 0x76, 0x86)),
        [StructureKind.Wall] = new SolidColorBrush(Color.FromArgb(0xD0, 0xA9, 0xB4, 0xC2)),
        [StructureKind.Walkway] = new SolidColorBrush(Color.FromArgb(0xA0, 0x7C, 0x6F, 0x5E)),
        [StructureKind.Stairs] = new SolidColorBrush(Color.FromArgb(0xA0, 0x7C, 0x6F, 0x5E)),
        [StructureKind.Pillar] = new SolidColorBrush(Color.FromArgb(0xE0, 0xB8, 0xBF, 0xC8)),
        [StructureKind.Beam] = new SolidColorBrush(Color.FromArgb(0xE0, 0xC9, 0x9B, 0x5A)),
        [StructureKind.Other] = new SolidColorBrush(Color.FromArgb(0xA0, 0x60, 0x60, 0x60)),
    };

    /// <summary>Legend color for the foundations layer.</summary>
    public static IBrush StructureBrush => StructureBrushes[StructureKind.Foundation];

    /// <summary>Color used for power lines (also shown in the layer legend).</summary>
    public static readonly IBrush PowerLineBrush = new SolidColorBrush(Color.FromArgb(0xB0, 0xF2, 0xD3, 0x4F));
    private static readonly IPen PowerLinePen = new Pen(PowerLineBrush, 1);
    private static readonly IPen SelectedLinePen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)), 2.5);

    public static readonly StyledProperty<IReadOnlySet<MapMarker>?> BoxMarkersProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlySet<MapMarker>?>(nameof(BoxMarkers));

    public static readonly StyledProperty<IReadOnlySet<Structure>?> BoxStructuresProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlySet<Structure>?>(nameof(BoxStructures));

    /// <summary>Raised when the user finishes a Shift+drag box; the rectangle is in world units (cm).</summary>
    public event EventHandler<BoxSelectedEventArgs>? BoxSelected;

    private static readonly IBrush BoxFill = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xD7, 0x00));
    private static readonly IPen BoxPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)), 1.5, new DashStyle([4, 3], 0));
    private static readonly IBrush SelectedStructureBrush = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xC4, 0x00));
    private static readonly IPen SelectedMarkerPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)), 2);

    private static readonly IPen ConveyorPen = new Pen(new SolidColorBrush(Color.FromArgb(0xD0, 0x4F, 0xC9, 0xB8)), 1.5);
    private static readonly IPen SelectedPathPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)), 3.5);

    private const double ClickRadius = 9;
    private const double MinScale = 1e-5, MaxScale = 0.2; // pixels per centimetre

    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x23, 0x2A));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1);
    private static readonly IPen MarkerOutline = new Pen(Brushes.White, 1.5);
    private static readonly IPen SelectionPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)), 2.5);
    private static readonly IBrush LabelBrush = Brushes.White;
    private static readonly IBrush LabelBackground = new SolidColorBrush(Color.FromArgb(0xC0, 0x10, 0x10, 0x10));
    private static readonly Typeface LabelFace = new(FontFamily.Default);

    private static readonly Dictionary<MapMarkerKind, (IBrush Brush, double Size, bool Round)> MarkerStyles = new()
    {
        [MapMarkerKind.Building] = (new SolidColorBrush(Color.FromArgb(0x90, 0x9A, 0xA4, 0xB0)), 2, false),
        [MapMarkerKind.Conveyor] = (new SolidColorBrush(Color.FromRgb(0x4F, 0xC9, 0xB8)), 2, false),
        [MapMarkerKind.Logistics] = (new SolidColorBrush(Color.FromRgb(0xC9, 0xA2, 0x27)), 2.5, false),
        [MapMarkerKind.Machine] = (new SolidColorBrush(Color.FromRgb(0xF0, 0x8A, 0x24)), 3.5, false),
        [MapMarkerKind.World] = (new SolidColorBrush(Color.FromRgb(0x4C, 0xC3, 0x8A)), 5, true),
        [MapMarkerKind.Storage] = (new SolidColorBrush(Color.FromRgb(0x3D, 0x9B, 0xF0)), 5.5, false),
        [MapMarkerKind.Vehicle] = (new SolidColorBrush(Color.FromRgb(0xB0, 0x7C, 0xF0)), 7, true),
        [MapMarkerKind.Player] = (new SolidColorBrush(Color.FromRgb(0xE8, 0x3E, 0x3E)), 10, true),
    };

    public static IBrush BrushFor(MapMarkerKind kind) => MarkerStyles[kind].Brush;

    private double _scale = 0.001;
    private Point _origin; // screen position of world (0, 0)
    private bool _fitted;
    private Point? _pressPoint;
    private Point _lastPoint;
    private bool _dragging;
    private bool _boxing;
    private MapMarker? _hover;

    static MapControl()
    {
        AffectsRender<MapControl>(MarkersProperty, SelectedMarkerProperty, LinesProperty, StructuresProperty,
            BoxMarkersProperty, BoxStructuresProperty);
        FocusableProperty.OverrideDefaultValue<MapControl>(true);
        ClipToBoundsProperty.OverrideDefaultValue<MapControl>(true);
    }

    public IReadOnlyList<MapMarker>? Markers
    {
        get => GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    public MapMarker? SelectedMarker
    {
        get => GetValue(SelectedMarkerProperty);
        set => SetValue(SelectedMarkerProperty, value);
    }

    /// <summary>Markers in the current box selection (highlighted).</summary>
    public IReadOnlySet<MapMarker>? BoxMarkers
    {
        get => GetValue(BoxMarkersProperty);
        set => SetValue(BoxMarkersProperty, value);
    }

    /// <summary>Structures in the current box selection (highlighted).</summary>
    public IReadOnlySet<Structure>? BoxStructures
    {
        get => GetValue(BoxStructuresProperty);
        set => SetValue(BoxStructuresProperty, value);
    }

    /// <summary>Foundations, walls and ramps drawn as footprints beneath everything else.</summary>
    public IReadOnlyList<Structure>? Structures
    {
        get => GetValue(StructuresProperty);
        set => SetValue(StructuresProperty, value);
    }

    /// <summary>Power lines drawn beneath the markers; lines touching the selected building are highlighted.</summary>
    public IReadOnlyList<MapLine>? Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public Point WorldToScreen(Vector3 world) => new(_origin.X + world.X * _scale, _origin.Y + world.Y * _scale);

    public Vector2 ScreenToWorld(Point screen) => new((float)((screen.X - _origin.X) / _scale), (float)((screen.Y - _origin.Y) / _scale));

    /// <summary>Zooms and pans so every marker is visible.</summary>
    public void FitToMarkers()
    {
        var markers = Markers;
        if (markers is not { Count: > 0 } || Bounds.Width < 1 || Bounds.Height < 1)
            return;
        float minX = markers.Min(m => m.Position.X), maxX = markers.Max(m => m.Position.X);
        float minY = markers.Min(m => m.Position.Y), maxY = markers.Max(m => m.Position.Y);
        double width = Math.Max(maxX - minX, 10_000), height = Math.Max(maxY - minY, 10_000);
        _scale = Math.Clamp(Math.Min(Bounds.Width / width, Bounds.Height / height) * 0.9, MinScale, MaxScale);
        double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
        _origin = new Point(Bounds.Width / 2 - cx * _scale, Bounds.Height / 2 - cy * _scale);
        _fitted = true;
        InvalidateVisual();
    }

    /// <summary>Centers the view on a marker without changing the zoom.</summary>
    public void CenterOn(MapMarker marker)
    {
        _origin = new Point(Bounds.Width / 2 - marker.Position.X * _scale, Bounds.Height / 2 - marker.Position.Y * _scale);
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkersProperty && !_fitted)
            FitToMarkers();
        if (change.Property == BoundsProperty && !_fitted)
            FitToMarkers();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Background, bounds);
        DrawGrid(context, bounds);
        DrawStructures(context, bounds);
        DrawLines(context, bounds);

        var markers = Markers;
        if (markers is null)
            return;

        var occupied = new HashSet<long>();
        var labels = new List<(MapMarker Marker, Point At)>();
        foreach (var marker in markers)
        {
            if (marker.Path is { } path && DrawPath(context, bounds, marker, path, occupied))
                continue;
            var p = WorldToScreen(marker.Position);
            if (p.X < -10 || p.Y < -10 || p.X > bounds.Width + 10 || p.Y > bounds.Height + 10)
                continue;
            var (brush, size, round) = MarkerStyles[marker.Kind];
            if (marker.Kind <= MapMarkerKind.Machine)
            {
                // Dense layers: one marker per 2×2 pixel cell per kind is visually identical and much cheaper.
                long cell = ((long)marker.Kind << 42) | ((long)(p.X / 2) << 21) | (long)(p.Y / 2);
                if (!occupied.Add(cell))
                    continue;
            }
            if (round)
                context.DrawEllipse(brush, marker.Kind == MapMarkerKind.Player ? MarkerOutline : null, p, size / 2, size / 2);
            else
                context.FillRectangle(brush, new Rect(p.X - size / 2, p.Y - size / 2, size, size));
            if (marker.Kind == MapMarkerKind.Player)
                labels.Add((marker, p));
        }

        DrawBoxSelection(context, bounds);

        if (SelectedMarker is { } selected)
        {
            if (selected.Path is { } selectedPath)
                for (int i = 1; i < selectedPath.Count; i++)
                    context.DrawLine(SelectedPathPen, WorldToScreen(selectedPath[i - 1]), WorldToScreen(selectedPath[i]));
            var p = WorldToScreen(selected.Position);
            context.DrawEllipse(null, SelectionPen, p, 9, 9);
            labels.Add((selected, p));
        }
        if (_hover is { } hover && hover != SelectedMarker)
            labels.Add((hover, WorldToScreen(hover.Position)));

        foreach (var (marker, at) in labels.DistinctBy(l => l.Marker))
            DrawLabel(context, marker.Label, at);
        DrawScaleBar(context, bounds);
    }

    private void DrawGrid(DrawingContext context, Rect bounds)
    {
        double spacing = NiceDistance(80 / _scale);
        var topLeft = ScreenToWorld(new Point(0, 0));
        var bottomRight = ScreenToWorld(new Point(bounds.Width, bounds.Height));
        for (double x = Math.Floor(topLeft.X / spacing) * spacing; x <= bottomRight.X; x += spacing)
        {
            double sx = _origin.X + x * _scale;
            context.DrawLine(GridPen, new Point(sx, 0), new Point(sx, bounds.Height));
        }
        for (double y = Math.Floor(topLeft.Y / spacing) * spacing; y <= bottomRight.Y; y += spacing)
        {
            double sy = _origin.Y + y * _scale;
            context.DrawLine(GridPen, new Point(0, sy), new Point(bounds.Width, sy));
        }
    }

    /// <summary>
    /// Draws a belt or pipe along its path. Returns true when handled (drawn or culled); false when the path is
    /// too small on screen, so the caller draws a dot instead.
    /// </summary>
    private bool DrawPath(DrawingContext context, Rect bounds, MapMarker marker, IReadOnlyList<Vector3> path, HashSet<long> occupied)
    {
        var min = WorldToScreen(new Vector3(marker.PathBounds.Min, 0));
        var max = WorldToScreen(new Vector3(marker.PathBounds.Max, 0));
        if (max.X < 0 || max.Y < 0 || min.X > bounds.Width || min.Y > bounds.Height)
            return true; // entirely off screen
        if (max.X - min.X < 3 && max.Y - min.Y < 3)
            return false; // a dot is enough
        for (int i = 1; i < path.Count; i++)
            context.DrawLine(ConveyorPen, WorldToScreen(path[i - 1]), WorldToScreen(path[i]));
        return true;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        double t = lengthSquared == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        return Distance(p, new Point(a.X + t * dx, a.Y + t * dy));
    }

    private void DrawStructures(DrawingContext context, Rect bounds)
    {
        if (Structures is not { } structures)
            return;
        var occupied = new HashSet<long>();
        foreach (var s in structures)
        {
            var p = WorldToScreen(s.Position);
            double w = s.Width * _scale, d = s.Depth * _scale;
            double reach = Math.Max(w, d);
            if (p.X < -reach || p.Y < -reach || p.X > bounds.Width + reach || p.Y > bounds.Height + reach)
                continue;
            var brush = StructureBrushes[s.Kind];

            if (reach < 3)
            {
                // Too small to show a shape: one dot per 2×2 pixel cell.
                long cell = ((long)(p.X / 2) << 21) | (long)(p.Y / 2);
                if (occupied.Add(cell))
                    context.FillRectangle(brush, new Rect(p.X - 1, p.Y - 1, 2, 2));
                continue;
            }
            DrawFootprint(context, s, p, w, d, brush);
        }
    }

    private static void DrawFootprint(DrawingContext context, Structure s, Point p, double w, double d, IBrush brush)
    {
        // Most structures are rotated in 90° steps: draw them axis-aligned, swapping sides on odd quarters.
        double quarter = s.Yaw / (Math.PI / 2);
        double nearest = Math.Round(quarter);
        if (Math.Abs(quarter - nearest) < 0.01)
        {
            bool swap = ((long)nearest & 1) != 0;
            double rw = swap ? d : w, rd = swap ? w : d;
            context.FillRectangle(brush, new Rect(p.X - rw / 2, p.Y - rd / 2, rw, rd));
        }
        else
        {
            using (context.PushTransform(Matrix.CreateRotation(s.Yaw) * Matrix.CreateTranslation(p.X, p.Y)))
                context.FillRectangle(brush, new Rect(-w / 2, -d / 2, w, d));
        }
    }

    /// <summary>Highlights the box selection: gold footprints for structures, gold outlines for markers and belts.</summary>
    private void DrawBoxSelection(DrawingContext context, Rect bounds)
    {
        if (BoxStructures is { Count: > 0 } structures)
        {
            foreach (var s in structures)
            {
                var p = WorldToScreen(s.Position);
                if (p.X < -50 || p.Y < -50 || p.X > bounds.Width + 50 || p.Y > bounds.Height + 50)
                    continue;
                DrawFootprint(context, s, p, Math.Max(s.Width * _scale, 2), Math.Max(s.Depth * _scale, 2), SelectedStructureBrush);
            }
        }
        if (BoxMarkers is { Count: > 0 } markers)
        {
            foreach (var m in markers)
            {
                if (m.Path is { } path)
                {
                    for (int i = 1; i < path.Count; i++)
                        context.DrawLine(SelectedMarkerPen, WorldToScreen(path[i - 1]), WorldToScreen(path[i]));
                    continue;
                }
                var p = WorldToScreen(m.Position);
                if (p.X >= -10 && p.Y >= -10 && p.X <= bounds.Width + 10 && p.Y <= bounds.Height + 10)
                    context.DrawEllipse(null, SelectedMarkerPen, p, 5, 5);
            }
        }
        if (_boxing && _pressPoint is { } start)
        {
            var rect = new Rect(start, _lastPoint).Normalize();
            context.FillRectangle(BoxFill, rect);
            context.DrawRectangle(BoxPen, rect);
        }
    }

    private void DrawLines(DrawingContext context, Rect bounds)
    {
        if (Lines is not { } lines)
            return;
        var selected = SelectedMarker?.Actor;
        var highlighted = new List<(Point, Point)>();
        foreach (var line in lines)
        {
            var a = WorldToScreen(line.From);
            var b = WorldToScreen(line.To);
            // Skip segments entirely to one side of the view.
            if ((a.X < 0 && b.X < 0) || (a.Y < 0 && b.Y < 0)
                || (a.X > bounds.Width && b.X > bounds.Width) || (a.Y > bounds.Height && b.Y > bounds.Height))
                continue;
            if (selected is not null && (line.FromBuilding == selected || line.ToBuilding == selected || line.Line == selected))
                highlighted.Add((a, b));
            else
                context.DrawLine(PowerLinePen, a, b);
        }
        foreach (var (a, b) in highlighted)
            context.DrawLine(SelectedLinePen, a, b);
    }

    private void DrawScaleBar(DrawingContext context, Rect bounds)
    {
        double distance = NiceDistance(120 / _scale);
        double length = distance * _scale;
        var start = new Point(16, bounds.Height - 20);
        context.DrawLine(MarkerOutline, start, start + new Point(length, 0));
        context.DrawLine(MarkerOutline, start, start + new Point(0, -6));
        context.DrawLine(MarkerOutline, start + new Point(length, 0), start + new Point(length, -6));
        string text = distance >= 100_000 ? $"{distance / 100_000:0.#} km" : $"{distance / 100:0} m";
        var formatted = Format(text);
        context.DrawText(formatted, start + new Point(length / 2 - formatted.Width / 2, -22));
    }

    private static void DrawLabel(DrawingContext context, string text, Point at)
    {
        var formatted = Format(text);
        var origin = new Point(at.X + 9, at.Y - formatted.Height - 4);
        context.FillRectangle(LabelBackground, new Rect(origin.X - 4, origin.Y - 2, formatted.Width + 8, formatted.Height + 4), 3);
        context.DrawText(formatted, origin);
    }

    private static FormattedText Format(string text) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 12, LabelBrush);

    /// <summary>Rounds a distance (cm) to 1, 2 or 5 × a power of ten.</summary>
    private static double NiceDistance(double atLeast)
    {
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(atLeast, 1))));
        foreach (double step in new[] { 1, 2, 5, 10 })
            if (step * magnitude >= atLeast)
                return step * magnitude;
        return 10 * magnitude;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var at = e.GetPosition(this);
        var world = ScreenToWorld(at);
        _scale = Math.Clamp(_scale * Math.Pow(1.25, e.Delta.Y), MinScale, MaxScale);
        _origin = new Point(at.X - world.X * _scale, at.Y - world.Y * _scale);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _pressPoint = _lastPoint = e.GetPosition(this);
        _dragging = false;
        _boxing = e.KeyModifiers.HasFlag(KeyModifiers.Shift); // Shift+drag selects a box instead of panning
        e.Pointer.Capture(this);
        Focus();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_pressPoint is { } press)
        {
            if (!_dragging && Distance(p, press) > 4)
                _dragging = true;
            if (_dragging)
            {
                if (!_boxing)
                    _origin += p - _lastPoint;
                _lastPoint = p;
                InvalidateVisual();
            }
            return;
        }
        var hover = HitTest(p);
        if (hover != _hover)
        {
            _hover = hover;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressPoint is not { } press)
            return;
        var end = e.GetPosition(this);
        if (_boxing && _dragging)
        {
            var a = ScreenToWorld(press);
            var b = ScreenToWorld(end);
            BoxSelected?.Invoke(this, new BoxSelectedEventArgs(Vector2.Min(a, b), Vector2.Max(a, b)));
        }
        else if (!_dragging)
        {
            SelectedMarker = HitTest(end);
        }
        _pressPoint = null;
        _dragging = false;
        _boxing = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover is not null)
        {
            _hover = null;
            InvalidateVisual();
        }
    }

    /// <summary>The topmost marker within the click radius of <paramref name="screen"/>, preferring higher layers.</summary>
    public MapMarker? HitTest(Point screen)
    {
        var markers = Markers;
        if (markers is null)
            return null;
        MapMarker? best = null;
        double bestDistance = ClickRadius;
        for (int i = markers.Count - 1; i >= 0; i--)
        {
            var marker = markers[i];
            if (best is not null && marker.Kind < best.Kind)
                break; // markers are ordered by layer; lower layers only win if nothing above was in range
            double d = marker.Path is { } path ? DistanceToPath(marker, path, screen) : Distance(WorldToScreen(marker.Position), screen);
            if (d <= bestDistance)
            {
                best = marker;
                bestDistance = d;
            }
        }
        return best;
    }

    /// <summary>Screen distance from a point to a belt's path (quickly rejected via the path bounds).</summary>
    private double DistanceToPath(MapMarker marker, IReadOnlyList<Vector3> path, Point screen)
    {
        var min = WorldToScreen(new Vector3(marker.PathBounds.Min, 0));
        var max = WorldToScreen(new Vector3(marker.PathBounds.Max, 0));
        if (screen.X < min.X - ClickRadius || screen.Y < min.Y - ClickRadius || screen.X > max.X + ClickRadius || screen.Y > max.Y + ClickRadius)
            return double.MaxValue;
        double best = double.MaxValue;
        for (int i = 1; i < path.Count; i++)
            best = Math.Min(best, DistanceToSegment(screen, WorldToScreen(path[i - 1]), WorldToScreen(path[i])));
        return best;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
