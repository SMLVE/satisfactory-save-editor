using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using SatisfactorySaveEditor.Core.Trailing;
using SatisfactorySaveEditor.Domain.Inventory;

namespace SatisfactorySaveEditor.Domain.Structures;

public enum StructureKind { Foundation, Ramp, Wall, Walkway, Stairs, Pillar, Beam, Other }

/// <summary>
/// A lightweight buildable (foundation, wall, ramp...) with its footprint on the ground plane.
/// Width runs along the structure's local X axis, depth along local Y, both in centimetres.
/// </summary>
public sealed record Structure(StructureKind Kind, string ClassName, Vector3 Position, float Yaw, float Width, float Depth,
    LightweightInstance Instance, LightweightClass Class)
{
    public string TypeName => ContainerService.FriendlyClassName(ClassName);
}

/// <summary>Foundations, walls, ramps, walkways, pillars and beams from the lightweight buildable subsystem.</summary>
public sealed partial class StructureService(SaveEditor editor)
{
    private const float WallThickness = 50; // cm; walls are drawn as thin bars

    public IReadOnlyList<Structure> All
    {
        get
        {
            var subsystem = editor.OfClass("FGLightweightBuildableSubsystem").FirstOrDefault();
            if (subsystem?.Trailing is not LightweightBuildablesTrailing data)
                return [];
            var structures = new List<Structure>(data.InstanceCount);
            foreach (var cls in data.Classes)
            {
                var kind = KindOf(cls.ClassName);
                var (width, depth) = Footprint(kind, cls.ClassName);
                foreach (var instance in cls.Instances)
                {
                    var p = instance.Position;
                    var s = instance.Scale;
                    if (kind == StructureKind.Beam)
                    {
                        structures.Add(BeamFootprint(cls, instance));
                        continue;
                    }
                    structures.Add(new Structure(kind, cls.ClassName, new Vector3((float)p.X, (float)p.Y, (float)p.Z),
                        (float)instance.Rotation.Yaw, width * (float)s.X, depth * (float)s.Y, instance, cls));
                }
            }
            return structures;
        }
    }

    /// <summary>
    /// Removes structures. Classes left without instances are dropped, as the game does not write empty ones.
    /// Lightweight buildables are not referenced by other objects, so nothing else needs cleaning up.
    /// </summary>
    public int Delete(IEnumerable<Structure> structures)
    {
        var data = Data() ?? throw new InvalidOperationException("This save's foundations and walls could not be read.");
        int removed = 0;
        foreach (var structure in structures)
            if (structure.Class.Instances.Remove(structure.Instance))
                removed++;
        data.Classes.RemoveAll(c => c.Instances.Count == 0);
        return removed;
    }

    /// <summary>Moves structures by an offset in centimetres (world axes; Z is up).</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Part of the instance API alongside the other structure edits.")]
    public void Move(IEnumerable<Structure> structures, Vector3 offset)
    {
        foreach (var instance in structures.Select(s => s.Instance).Distinct())
        {
            var p = instance.Position;
            instance.Position = new DVector3(p.X + offset.X, p.Y + offset.Y, p.Z + offset.Z);
        }
    }

    private LightweightBuildablesTrailing? Data() =>
        editor.OfClass("FGLightweightBuildableSubsystem").FirstOrDefault()?.Trailing as LightweightBuildablesTrailing;

    /// <summary>True when the save's lightweight buildables could not be decoded (they stay raw and unseen).</summary>
    public bool IsUndecoded =>
        editor.OfClass("FGLightweightBuildableSubsystem").FirstOrDefault() is { Trailing: null, TrailingData.Length: > 8 };

    /// <summary>
    /// A beam starts at its position and runs BeamLength along its local X axis, which may point in any
    /// direction (vertical beams project to a small square). The footprint is centred on the beam's midpoint.
    /// </summary>
    private static Structure BeamFootprint(LightweightClass cls, LightweightInstance instance)
    {
        var q = instance.Rotation;
        double length = instance.BeamLength ?? 400;
        // Local X axis rotated by the quaternion.
        double dx = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
        double dy = 2 * (q.X * q.Y + q.W * q.Z);
        double dz = 2 * (q.X * q.Z - q.W * q.Y);
        double horizontal = Math.Sqrt(dx * dx + dy * dy);
        var p = instance.Position;
        var center = new Vector3((float)(p.X + dx * length / 2), (float)(p.Y + dy * length / 2), (float)(p.Z + dz * length / 2));
        float yaw = horizontal > 0.01 ? (float)Math.Atan2(dy, dx) : 0f;
        return new Structure(StructureKind.Beam, cls.ClassName, center, yaw,
            (float)Math.Max(length * horizontal, BeamThickness), BeamThickness, instance, cls);
    }

    private const float BeamThickness = 50; // cm

    public static StructureKind KindOf(string className) => className switch
    {
        _ when className.Contains("Beam", StringComparison.Ordinal) => StructureKind.Beam,
        _ when className.Contains("Pillar", StringComparison.Ordinal) => StructureKind.Pillar,
        _ when className.Contains("Foundation", StringComparison.Ordinal) => StructureKind.Foundation,
        _ when className.Contains("Ramp", StringComparison.Ordinal) && !className.Contains("Walkway", StringComparison.Ordinal) => StructureKind.Ramp,
        _ when className.Contains("Wall", StringComparison.Ordinal) => StructureKind.Wall,
        _ when className.Contains("Walkway", StringComparison.Ordinal) => StructureKind.Walkway,
        _ when className.Contains("Stair", StringComparison.Ordinal) => StructureKind.Stairs,
        _ => StructureKind.Other,
    };

    /// <summary>
    /// Ground footprint from the class name: "_8x4" means 8 m wide (and 4 m tall for foundations/walls).
    /// Foundations and ramps are square; walls are thin; walkways and stairs are 4 m.
    /// </summary>
    public static (float Width, float Depth) Footprint(StructureKind kind, string className)
    {
        var match = SizeRegex().Match(className);
        float width = match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 100f : 400f;
        return kind switch
        {
            StructureKind.Foundation or StructureKind.Ramp => (width, width),
            StructureKind.Wall => (width, WallThickness),
            StructureKind.Pillar => className.Contains("Small", StringComparison.Ordinal) ? (100f, 100f) : (200f, 200f),
            _ => (400f, 400f),
        };
    }

    [GeneratedRegex(@"_(\d+)x(\d+)")]
    private static partial Regex SizeRegex();
}
