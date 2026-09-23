using System;

// Pure chase rules, independent of the camera and obstacle approach speed.
public sealed class ChaseState
{
    public float Speed { get; private set; }
    public float Distance { get; private set; }
    public int Hits { get; private set; }
    public bool IsCaught => Distance <= 0f;

    public ChaseState(float speed, float distance)
    {
        Speed = Math.Max(0f, speed);
        Distance = Math.Max(0f, distance);
    }

    public void Tick(float seconds, float pursuerSpeed, float distanceScale, float maxDistance)
    {
        if (IsCaught) return;
        Distance = Math.Max(0f, Math.Min(Math.Max(0f, maxDistance),
            Distance + (Speed - pursuerSpeed) * Math.Max(0f, seconds) * Math.Max(0f, distanceScale)));
    }

    public void Hit(float speedLoss, float minSpeed, float distanceLoss)
    {
        if (IsCaught) return;
        Hits++;
        Speed = Math.Max(Math.Max(0f, minSpeed), Speed - Math.Max(0f, speedLoss));
        Distance = Math.Max(0f, Distance - Math.Max(0f, distanceLoss));
    }

    public void Dodge(float speedGain, float maxSpeed)
    {
        if (IsCaught) return;
        Speed = Math.Min(Math.Max(0f, maxSpeed), Speed + Math.Max(0f, speedGain));
    }
}
