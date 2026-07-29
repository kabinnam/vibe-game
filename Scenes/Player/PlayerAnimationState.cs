using System;

public enum PlayerLocomotionMode
{
    Idle,
    Walk,
    Airborne
}

public sealed class PlayerAnimationState
{
    private const float Tau = MathF.PI * 2f;

    public PlayerLocomotionMode LocomotionMode { get; private set; } = PlayerLocomotionMode.Idle;
    public float CyclePhase { get; private set; }
    public float NormalizedSpeed { get; private set; }
    public float SmokeBlend { get; private set; }

    public void Advance(float delta, float normalizedSpeed, bool grounded, bool smoking)
    {
        NormalizedSpeed = Math.Clamp(normalizedSpeed, 0f, 1f);
        LocomotionMode = !grounded
            ? PlayerLocomotionMode.Airborne
            : NormalizedSpeed > 0.05f
                ? PlayerLocomotionMode.Walk
                : PlayerLocomotionMode.Idle;

        var cyclesPerSecond = LocomotionMode == PlayerLocomotionMode.Walk
            ? Lerp(0.2f, 1.4f, NormalizedSpeed)
            : 0.2f;
        CyclePhase = (CyclePhase + cyclesPerSecond * delta * Tau) % Tau;

        SmokeBlend = MoveToward(SmokeBlend, smoking ? 1f : 0f, 5f * delta);
    }

    private static float Lerp(float from, float to, float weight) => from + (to - from) * weight;

    private static float MoveToward(float from, float to, float delta)
    {
        if (MathF.Abs(to - from) <= delta)
            return to;

        return from + MathF.Sign(to - from) * delta;
    }
}
