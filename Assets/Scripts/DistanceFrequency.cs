using System;

// Shared odometer-based cooldown scaling. It never scales warnings or movement.
public static class DistanceFrequency
{
    [Serializable]
    public sealed class Settings
    {
        public bool enabled = true;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Tooltip("Cooldowns continuously shrink by Interval Multiplier Per Step over this many odometer meters.")]
#endif
        public float metersPerStep = 100f;
        public float intervalMultiplierPerStep = .85f;
        public float minimumMultiplier = .35f;
        public void Validate()
        {
            metersPerStep = Math.Max(1f, metersPerStep);
            intervalMultiplierPerStep = Math.Max(.1f, Math.Min(1f, intervalMultiplierPerStep));
            minimumMultiplier = Math.Max(.1f, Math.Min(1f, minimumMultiplier));
        }
    }
    public static float Multiplier(double meters, Settings settings)
    {
        if (settings == null || !settings.enabled) return 1f;
        return Math.Max(settings.minimumMultiplier, (float)Math.Pow(settings.intervalMultiplierPerStep,
            Math.Max(0d, meters) / Math.Max(1f, settings.metersPerStep)));
    }
}
