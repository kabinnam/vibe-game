using System;
using Godot;

namespace VibeGame.Player;

public partial class Sanity : Node
{
    [ExportGroup("Config")]
    [Export] public float Max { get; set; } = 100f;
    [Export] public float StartingValue { get; set; } = 100f;
    [Export] public float BaseDepletionRate { get; set; } = 2f;

    private float _currentValue;
    public float CurrentValue
    {
        get => _currentValue;
        private set => _currentValue = Mathf.Clamp(value, 0f, Max);
    }

    public override void _Ready()
    {
        SetPhysicsProcess(false); // Leave processing disabled if configuration validation fails.
        ValidateConfig();
        CurrentValue = StartingValue;
        // SEAM: Add public start/stop methods if session flow needs to delay or
        // suspend sanity updates during intros or countdowns; preserve the current value.
        SetPhysicsProcess(true);
    }

    private void ValidateConfig()
    {
        Validate(float.IsFinite(Max) && Max > 0f,
            "Max must be finite and greater than zero.");

        Validate(float.IsFinite(StartingValue)
            && StartingValue >= 0f && StartingValue <= Max,
            "StartingValue must be finite and between zero and Max.");

        Validate(float.IsFinite(BaseDepletionRate) && BaseDepletionRate >= 0f,
            "BaseDepletionRate must be finite and non-negative.");

        static void Validate(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Sanity: {message}");
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        CurrentValue -= BaseDepletionRate * (float)delta;
    }
}
