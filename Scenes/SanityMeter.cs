using System;

namespace VibeGame;

public class SanityMeter
{
    public float Value { get; private set; } = 1f;

    public bool IsEmpty => Value <= 0f;

    public void Decay(float dt, float perSecond)
    {
        Value = Math.Clamp(Value - perSecond * dt, 0f, 1f);
    }
}
