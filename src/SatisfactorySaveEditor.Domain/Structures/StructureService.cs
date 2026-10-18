using System.Numerics;
using System.Text.RegularExpressions;
using SatisfactorySaveEditor.Core.Trailing;
using SatisfactorySaveEditor.Domain.Inventory;

namespace SatisfactorySaveEditor.Domain.Structures;

public enum StructureKind { Foundation, Ramp, Wall, Walkway, Stairs, Other }

/// <summary>
/// A lightweight buildable (foundation, wall, ramp...) with its footprint on the ground plane.
/// Width runs along the structure's local X axis, depth along local Y, both in centimetres.
/// </summary>
public sealed record Structure(StructureKind Kind, string ClassName, Vector3 Position, float Yaw, float Width, float Depth,
    LightweightInstance Instance)
{
    public string TypeName => ContainerService.FriendlyClassName(ClassName);
}

/// <summary>Foundations, walls, ramps and walkways from the lightweight buildable subsystem.</summary>
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
                    structures.Add(new Structure(kind, cls.ClassName, new Vector3((float)p.X, (float)p.Y, (float)p.Z),
                        (float)instance.Rotation.Yaw, width * (float)s.X, depth * (float)s.Y, instance));
                }
            }
            return structures;
        }
    }

    /// <summary>True when the save's lightweight buildables could not be decoded (they stay raw and unseen).</summary>
    public bool IsUndecoded =>
        editor.OfClass("FGLightweightBuildableSubsystem").FirstOrDefault() is { Trailing: null, TrailingData.Length: > 8 };

    public static StructureKind KindOf(string className) => className switch
    {
        _ when className.Contains("Foundation") => StructureKind.Foundation,
        _ when className.Contains("Ramp") && !className.Contains("Walkway") => StructureKind.Ramp,
        _ when className.Contains("Wall") => StructureKind.Wall,
        _ when className.Contains("Walkway") => StructureKind.Walkway,
        _ when className.Contains("Stair") => StructureKind.Stairs,
        _ => StructureKind.Other,
    };

    /// <summary>
    /// Ground footprint from the class name: "_8x4" means 8 m wide (and 4 m tall for foundations/walls).
    /// Foundations and ramps are square; walls are thin; walkways and stairs are 4 m.
    /// </summary>
    public static (float Width, float Depth) Footprint(StructureKind kind, string className)
    {
        var match = SizeRegex().Match(className);
        float width = match.Success ? int.Parse(match.Groups[1].Value) * 100f : 400f;
        return kind switch
        {
            StructureKind.Foundation or StructureKind.Ramp => (width, width),
            StructureKind.Wall => (width, WallThickness),
            _ => (400f, 400f),
        };
    }

    [GeneratedRegex(@"_(\d+)x(\d+)")]
    private static partial Regex SizeRegex();
}
