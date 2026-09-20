using Godot;

public partial class HUD : CanvasLayer
{
    private ProgressBar _bar;
    private Label _timerLabel;

    public override void _Ready()
    {
        _bar = GetNode<ProgressBar>("Control/SanityBar");
        _timerLabel = GetNode<Label>("Control/LevelTimerLabel");
        SetSanity(1f);
    }

    public void SetSanity(float sanity01)
    {
        _bar.Value = sanity01 * (float)_bar.MaxValue;
    }

    public void SetTimeRemaining(float seconds)
    {
        int display = Mathf.Max(0, Mathf.CeilToInt(seconds));
        _timerLabel.Text = $"Countdown: {display}";
    }
}
