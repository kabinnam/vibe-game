using System;
using System.Collections.Generic;
using Godot;

public partial class PlayerFeatureTests : Node
{
    private int _count;

    public override void _Ready()
    {
        try
        {
            TestAnimationState();
            GD.Print($"PASS PlayerFeatureTests ({_count} assertions)");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"FAIL PlayerFeatureTests: {exception}");
            GetTree().Quit(1);
        }
    }

    private void TestAnimationState()
    {
        var state = new PlayerAnimationState();

        Equal(PlayerLocomotionMode.Idle, state.LocomotionMode);

        state.Advance(0.1f, 0.8f, true, false);
        Equal(PlayerLocomotionMode.Walk, state.LocomotionMode);
        True(state.CyclePhase > 0f);

        state.Advance(0.1f, 0.8f, false, false);
        Equal(PlayerLocomotionMode.Airborne, state.LocomotionMode);

        state.Advance(0.1f, 0f, true, true);
        Near(0.5f, state.SmokeBlend);

        state.Advance(0.1f, 0f, true, false);
        Near(0f, state.SmokeBlend);
    }

    private void True(bool condition)
    {
        _count++;
        if (!condition)
            throw new InvalidOperationException("Expected condition to be true.");
    }

    private void Equal<T>(T expected, T actual)
    {
        _count++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private void Near(float expected, float actual, float tolerance = 0.0001f)
    {
        _count++;
        if (MathF.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"Expected {expected} +/- {tolerance}, got {actual}.");
    }
}
