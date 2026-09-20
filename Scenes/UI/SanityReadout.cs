using System;
using Godot;
using VibeGame.Player;

namespace VibeGame.UI;

public partial class SanityReadout : Label
{
    [Export] private Sanity _sanity;

    public override void _Ready()
    {
        if (_sanity == null)
        {
            throw new InvalidOperationException("SanityReadout: assign the player's Sanity node in the Inspector.");
        }

        // SEAM: Replace this temp readout with a proper HUD meter element that reads
        // initial state and subscribes to ValueChanged. Keep gameplay rules in Sanity.
        _sanity.ValueChanged += UpdateReadout;
        UpdateReadout(_sanity.CurrentValue);
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_sanity))
        {
            _sanity.ValueChanged -= UpdateReadout;
        }
    }

    private void UpdateReadout(float currentValue)
    {
        Text = $"Sanity: {currentValue:0.0} / {_sanity.Max:0.0}";
    }
}
