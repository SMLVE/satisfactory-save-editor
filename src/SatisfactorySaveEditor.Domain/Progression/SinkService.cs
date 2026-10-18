using SatisfactorySaveEditor.Core.Model;
using SatisfactorySaveEditor.Core.Properties;

namespace SatisfactorySaveEditor.Domain.Progression;

/// <summary>The AWESOME Sink's two point tracks: standard items and DNA (alien remains).</summary>
public enum SinkTrack { Standard = 0, Dna = 1 }

/// <summary>
/// AWESOME Sink state on FGResourceSinkSubsystem: total points per track, coupon levels reached per track and
/// coupons waiting to be printed. Values that are still at their default are absent from the save and are
/// created on first edit.
/// </summary>
public sealed class SinkService(SaveEditor editor)
{
    private const string TotalPoints = "mTotalPoints";
    private const string PointLevels = "mCurrentPointLevels";
    private const string Coupons = "mNumResourceSinkCoupons";
    private const int TrackCount = 2;

    private SaveObject Subsystem => SaveEditor.Editable(editor.Single("FGResourceSinkSubsystem"));

    public long PointsOf(SinkTrack track) =>
        Subsystem.Properties.Get<ArrayValue>(TotalPoints)?.Elements.ElementAtOrDefault((int)track) is Int64Value v ? v.Value : 0;

    /// <summary>Coupon levels reached on a track (how many coupons its points have earned so far).</summary>
    public int LevelOf(SinkTrack track) =>
        Subsystem.Properties.Get<ArrayValue>(PointLevels)?.Elements.ElementAtOrDefault((int)track) is IntValue v ? v.Value : 0;

    /// <summary>Coupons earned but not printed yet (printed at the AWESOME Sink).</summary>
    public int CouponsToPrint
    {
        get => Subsystem.Properties.Get<IntValue>(Coupons)?.Value ?? 0;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            Subsystem.Properties.GetOrAdd(Coupons, PropertyTypes.Int, () => new IntValue(0)).Value = value;
        }
    }

    /// <summary>
    /// Sets a track's total points. Coupon levels are left as they are: if points go up the game awards the
    /// coupons for the levels crossed, as it does while sinking items.
    /// </summary>
    public void SetPoints(SinkTrack track, long points)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(points);
        var array = Subsystem.Properties.GetOrAdd(TotalPoints, PropertyTypes.ArrayOf(PropertyTypes.Int64),
            () => new ArrayValue(PropertyTypes.Int64));
        while (array.Elements.Count < TrackCount)
            array.Elements.Add(new Int64Value(0));
        ((Int64Value)array.Elements[(int)track]).Value = points;
    }
}
