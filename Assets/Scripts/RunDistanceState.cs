using System;
using System.Globalization;

// Distance from calibrated running pace and sprint effort, independent of screen position.
public sealed class RunDistanceState
{
    public double Meters { get; private set; }
    public void Reset() => Meters = 0d;
    public void LimitTo(double meters) => Meters = Math.Min(Meters, Math.Max(0d, meters));

    public void Add(float runningTravel, float extraTravel, float metersPerUnit)
    {
        // Both inputs are calibrated travel amounts, not changes in screen position.
        Meters += Math.Max(0d, (double)runningTravel + extraTravel) * Math.Max(0f, metersPerUnit);
    }

    public void AddAtHumanPace(float runningTravel, float baseRunningSpeed, float sprintEquivalentSeconds,
        float jogSpeedKmh, float fastRunSpeedKmh, float distanceMultiplier)
    {
        float jog = Math.Max(0f, jogSpeedKmh);
        float fast = Math.Max(jog, fastRunSpeedKmh);
        float runningMeters = Math.Max(0f, runningTravel) * (jog / 3.6f) / Math.Max(.1f, baseRunningSpeed);
        // Screen position is a gameplay limit, not a limit on running effort.
        // Active sprint continues to count at the boundary; drifting back does not undo it.
        float sprintMeters = Math.Max(0f, sprintEquivalentSeconds) * ((fast - jog) / 3.6f);
        Add(runningMeters, sprintMeters, distanceMultiplier);
    }

    public string Display => Meters < 1000d
        ? Math.Floor(Meters).ToString("0", CultureInfo.InvariantCulture) + " m"
        : (Meters / 1000d).ToString("0.00", CultureInfo.InvariantCulture) + " km";
}
