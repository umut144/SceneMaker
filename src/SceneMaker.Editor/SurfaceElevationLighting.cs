namespace SceneMaker.Editor;

/// <summary>
/// Maps an absolute surface elevation onto the brightness used by the ordinary
/// Canvas view. It deliberately knows no colours or rendering engine: the App
/// keeps an Asset's hue and multiplies its channels by this answer.
/// </summary>
public static class SurfaceElevationLighting
{
    public const decimal AnchorElevationMeters = 1m;
    public const double MinimumBrightness = 0.25;
    public const double AnchorBrightness = 0.45;
    public const double MaximumBrightness = 1.0;

    /// <summary>
    /// Returns a stable brightness for one elevation within the complete
    /// Scene's represented range. Values below the 1 m anchor interpolate from
    /// the range's low end to 45%; values above it interpolate from 45% to the
    /// range's high end. Supplying the complete range rather than a clipped
    /// view range keeps colours stable while a later Section plane moves.
    /// </summary>
    public static double Brightness(
        decimal elevationMeters,
        decimal lowestElevationMeters,
        decimal highestElevationMeters)
    {
        if (lowestElevationMeters > highestElevationMeters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lowestElevationMeters),
                "Surface elevation range must run from low to high.");
        }

        if (elevationMeters == AnchorElevationMeters) return AnchorBrightness;

        if (elevationMeters < AnchorElevationMeters)
        {
            if (lowestElevationMeters >= AnchorElevationMeters) return AnchorBrightness;

            var position = (double)((elevationMeters - lowestElevationMeters)
                / (AnchorElevationMeters - lowestElevationMeters));
            return Mix(MinimumBrightness, AnchorBrightness, position);
        }

        if (highestElevationMeters <= AnchorElevationMeters) return AnchorBrightness;

        var upperPosition = (double)((elevationMeters - AnchorElevationMeters)
            / (highestElevationMeters - AnchorElevationMeters));
        return Mix(AnchorBrightness, MaximumBrightness, upperPosition);
    }

    private static double Mix(double low, double high, double position) =>
        low + (Math.Clamp(position, 0.0, 1.0) * (high - low));
}
