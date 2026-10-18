using System.Numerics;

namespace SatisfactorySaveEditor.App.Controls;

/// <summary>Rectangle of a finished Shift+drag box selection, in world units (cm).</summary>
internal sealed class BoxSelectedEventArgs(Vector2 min, Vector2 max) : EventArgs
{
    public Vector2 Min { get; } = min;
    public Vector2 Max { get; } = max;
}
