using System.Globalization;
using System.Numerics;
using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Properties;

namespace SatisfactorySaveEditor.Domain.Map;

/// <summary>
/// World-space path of a spline-based buildable (conveyor belts, pipes...) from its mSplineData: control
/// points relative to the actor, joined by cubic Hermite curves using the stored tangents, as Unreal does.
/// </summary>
public static class ConveyorPath
{
    private const int SamplesPerCurve = 8;

    /// <summary>The sampled path, or null when the actor has no spline data.</summary>
    public static IReadOnlyList<Vector3>? Of(ActorObject actor)
    {
        if (!actor.IsParsed || actor.Properties.Get<ArrayValue>("mSplineData") is not { Elements.Count: >= 2 } spline)
            return null;
        var points = spline.Elements.OfType<PropertyListStruct>()
            .Select(p => (Location: VectorOf(p, "Location"), Arrive: VectorOf(p, "ArriveTangent"), Leave: VectorOf(p, "LeaveTangent")))
            .ToList();
        if (points.Count < 2 || points.Any(p => p.Location is null))
            return null;

        var path = new List<Vector3> { ToWorld(actor, points[0].Location!.Value) };
        for (int i = 0; i < points.Count - 1; i++)
        {
            var p0 = points[i].Location!.Value;
            var p1 = points[i + 1].Location!.Value;
            var m0 = points[i].Leave ?? Vector3.Zero;
            var m1 = points[i + 1].Arrive ?? Vector3.Zero;
            // Straight pieces (tangents along the chord) need only their end point.
            int samples = IsStraight(p0, p1, m0, m1) ? 1 : SamplesPerCurve;
            for (int s = 1; s <= samples; s++)
                path.Add(ToWorld(actor, Hermite(p0, m0, p1, m1, s / (float)samples)));
        }
        return path;
    }

    private static Vector3 Hermite(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1;
    }

    private static bool IsStraight(Vector3 p0, Vector3 p1, Vector3 m0, Vector3 m1)
    {
        var chord = p1 - p0;
        if (chord.LengthSquared() < 1)
            return true;
        var dir = Vector3.Normalize(chord);
        return Aligned(m0, dir) && Aligned(m1, dir);

        static bool Aligned(Vector3 tangent, Vector3 dir) =>
            tangent.LengthSquared() < 1e-6f || Vector3.Dot(Vector3.Normalize(tangent), dir) > 0.9995f;
    }

    private static Vector3 ToWorld(ActorObject actor, Vector3 local) =>
        actor.Position + Vector3.Transform(local * actor.Scale, actor.Rotation);

    private static Vector3? VectorOf(PropertyListStruct point, string field) =>
        point.Properties.Get<NativeStruct>(field) is { } v
            ? new Vector3(Convert.ToSingle(v["X"], CultureInfo.InvariantCulture), Convert.ToSingle(v["Y"], CultureInfo.InvariantCulture), Convert.ToSingle(v["Z"], CultureInfo.InvariantCulture))
            : null;
}
