using System;
using Godot;

namespace VibeGame.Player;

public partial class Lungs : Node
{
    public enum BreathState
    {
        Idle,
        Inhaling,
        Holding,
        Exhaling,
    }

    [Signal] public delegate void StateChangedEventHandler(BreathState state);
    [Signal] public delegate void VolumeChangedEventHandler(float volume);
    [Signal] public delegate void BreathHoldTimeChangedEventHandler(float seconds);

    [ExportGroup("Config")]
    [Export] public float Capacity { get; set; } = 100f;
    [Export] public float InhaleRate { get; set; } = 25f;            // volume per second
    [Export] public float ExhaleRate { get; set; } = 50f;            // volume per second
    [Export] public float MaxBreathHoldSeconds { get; set; } = 8f;   // counted from inhale start

    public BreathState State { get; private set; } = BreathState.Idle;
    private bool _inhaleRequested;

    private float _volume;
    public float Volume
    {
        get => _volume;
        private set
        {
            float nextValue = Mathf.Clamp(value, 0f, Capacity);

            if (nextValue == _volume)
            {
                return;
            }

            _volume = nextValue;
            EmitSignal(SignalName.VolumeChanged, nextValue);
        }
    }

    private float _breathHoldTime;
    public float BreathHoldTime
    {
        get => _breathHoldTime;
        private set
        {
            float nextValue = Mathf.Clamp(value, 0f, MaxBreathHoldSeconds);

            if (nextValue == _breathHoldTime)
            {
                return;
            }

            _breathHoldTime = nextValue;
            EmitSignal(SignalName.BreathHoldTimeChanged, nextValue);
        }
    }

    public override void _Ready()
    {
        SetPhysicsProcess(false); // Leave processing disabled if configuration validation fails.
        ValidateConfig();
        SetPhysicsProcess(true);
    }

    public void SetInhaleRequested(bool requested)
    {
        _inhaleRequested = requested;
    }

    public void Exhale()
    {
        if (State is not (BreathState.Inhaling or BreathState.Holding) || Volume <= 0f)
        {
            return;
        }

        SetState(BreathState.Exhaling);
    }

    private void ValidateConfig()
    {
        Validate(float.IsFinite(Capacity) && Capacity > 0f,
            "Capacity must be finite and greater than zero.");

        Validate(float.IsFinite(InhaleRate) && InhaleRate > 0f,
            "InhaleRate must be finite and greater than zero.");

        Validate(float.IsFinite(ExhaleRate) && ExhaleRate > 0f,
            "ExhaleRate must be finite and greater than zero.");

        Validate(float.IsFinite(MaxBreathHoldSeconds)
            && MaxBreathHoldSeconds > Capacity / InhaleRate,
            "MaxBreathHoldSeconds must be finite and longer than Capacity / InhaleRate.");

        static void Validate(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Lungs: {message}");
            }
        }
    }

    private void SetState(BreathState next)
    {
        if (next == State)
        {
            return;
        }

        State = next;

        if (next == BreathState.Exhaling)
        {
            BreathHoldTime = 0f;
        }

        EmitSignal(SignalName.StateChanged, Variant.From(next));
    }

    public override void _PhysicsProcess(double delta)
    {
        float elapsed = (float)delta;

        switch (State)
        {
            case BreathState.Idle:
                if (_inhaleRequested)
                {
                    SetState(BreathState.Inhaling);
                }
                break;

            case BreathState.Inhaling:
                if (!_inhaleRequested)
                {
                    SetState(Volume > 0f ? BreathState.Holding : BreathState.Idle);
                    break;
                }

                Volume += InhaleRate * elapsed;
                BreathHoldTime += elapsed;

                if (BreathHoldTime >= MaxBreathHoldSeconds)
                {
                    Exhale();
                }
                else if (Volume >= Capacity)
                {
                    SetState(BreathState.Holding);
                }
                break;

            case BreathState.Holding:
                BreathHoldTime += elapsed;

                if (BreathHoldTime >= MaxBreathHoldSeconds)
                {
                    Exhale();
                }
                break;

            case BreathState.Exhaling:
                Volume -= ExhaleRate * elapsed;

                if (Volume <= 0f)
                {
                    SetState(BreathState.Idle);
                }
                break;
        }
    }
}
