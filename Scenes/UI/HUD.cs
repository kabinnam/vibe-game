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
        int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
        int minutes = total / 60;
        int secs = total % 60;
        _timerLabel.Text = $"{minutes:D2}:{secs:D2}";
    }
}
