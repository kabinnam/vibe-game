using System;
using Godot;

namespace VibeGame.Player;

public partial class Sanity : Node
{
    [ExportGroup("Config")]
    [Export] public float Max { get; set; } = 100f;
    [Export] public float StartingValue { get; set; } = 100f;
    [Export] public float BaseDepletionRate { get; set; } = 2f;

    public float CurrentValue { get; private set; }

    public override void _Ready()
    {
        ValidateConfiguration();
        CurrentValue = StartingValue;
    }

    private void ValidateConfiguration()
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
}
