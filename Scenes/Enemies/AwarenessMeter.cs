using System;

namespace VibeGame.Enemies;

// The analog "awareness" value in [0, 1] (0 = oblivious, 1 = fully alerted).
// Deliberately Godot-free and state-free: it only knows how to fill (scaled by how
// close the target is), decay, and stay clamped. The guard's FSM decides WHEN to do
// each, which keeps this reusable by other NPCs and testable without the engine.
public class AwarenessMeter
{
    private readonly float _detectRate;   // gained per second at point-blank (proximity == 1)
    private readonly float _decayRate;    // lost per second while decaying

    public AwarenessMeter(float detectRate, float decayRate)
    {
        _detectRate = detectRate;
        _decayRate = decayRate;
    }

    // Current awareness in [0, 1].
    public float Value { get; private set; }

    // Ramp up while the target is visible; proximity (1 up close, toward 0 far away) scales the rate.
    public void Fill(float dt, float proximity) => Set(Value + _detectRate * proximity * dt);

    // Bleed down while the target is not visible.
    public void Decay(float dt) => Set(Value - _decayRate * dt);

    // Snap to fully alerted (used while actively targeting the player).
    public void Pin() => Value = 1f;

    private void Set(float value) => Value = Math.Clamp(value, 0f, 1f);
}
