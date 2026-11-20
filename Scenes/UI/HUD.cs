using Godot;

namespace VibeGame.UI;

public partial class HUD : CanvasLayer
{
    private Range _sanityMeter;
    private Range _lungsMeter;
    private Label _levelTimerLabel;

    public override void _Ready()
    {
        _sanityMeter = GetNode<Range>("%SanityMeter");
        _lungsMeter = GetNode<Range>("%LungsMeter");
        _levelTimerLabel = GetNode<Label>("%LevelTimerLabel");
    }

    public float SanityMax
    {
        get => (float)_sanityMeter.MaxValue;
        set => _sanityMeter.MaxValue = value;
    }

    public void SetSanity(float current)
    {
        _sanityMeter.Value = current;
    }

    public float LungsCapacity
    {
        get => (float)_lungsMeter.MaxValue;
        set => _lungsMeter.MaxValue = value;
    }

    public void SetLungsVolume(float volume)
    {
        _lungsMeter.Value = volume;
    }

    public void SetTimeRemaining(float seconds)
    {
        int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
        int minutes = total / 60;
        int secs = total % 60;
        _levelTimerLabel.Text = $"{minutes:D2}:{secs:D2}";
    }
}
