using System;
using System.Collections.Generic; // For Dictionary
using Godot;

namespace VibeGame.Player;

public partial class Sanity : Node
{
    [Signal] public delegate void ValueChangedEventHandler(float currentValue);
    [Signal] public delegate void ExhaustedEventHandler();
    [ExportGroup("Config")]
    [Export] public float Max { get; set; } = 100f;
    [Export] public float StartingValue { get; set; } = 100f;
    [Export] public float BaseDepletionRate { get; set; } = 2f;

    private readonly Dictionary<Guid, float> _rateModifiers = new();
    private float _currentValue;
    public float CurrentValue
    {
        get => _currentValue;
        private set
        {
            float nextValue = Mathf.Clamp(value, 0f, Max);

            if (nextValue == _currentValue)
            {
                return;
            }

            _currentValue = nextValue;
            EmitSignal(SignalName.ValueChanged, nextValue);

            if (nextValue == 0f)
            {
                EmitSignal(SignalName.Exhausted);
            }
        }
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

    public Guid AddRateModifier(float rate)
    {
        if (!float.IsFinite(rate))
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "Rate must be finite.");
        }
        Guid handle = Guid.NewGuid();
        _rateModifiers.Add(handle, rate);
        return handle;
    }

    public bool RemoveRateModifier(Guid handle)
    {
        return _rateModifiers.Remove(handle);
    }

    public void AdjustOnce(float amount)
    {
        if (!float.IsFinite(amount))
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be finite.");
        }
        CurrentValue += amount;
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

    private float CalculateNetRate()
    {
        float netRate = -BaseDepletionRate;
        foreach (float modifier in _rateModifiers.Values)
        {
            netRate += modifier;
        }
        return netRate;
    }

    public override void _PhysicsProcess(double delta)
    {
        CurrentValue += CalculateNetRate() * (float)delta;
    }
}
