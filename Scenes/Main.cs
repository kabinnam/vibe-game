using Godot;
using VibeGame;

public partial class Main : Node3D
{
    [Export] public float SanityDurationSeconds { get; set; } = 60f;
    [Export] public float LevelDurationSeconds { get; set; } = 300f; // 5 minutes

    private SanityMeter _sanity;
    private HUD _hud;
    private float _timeRemaining;
    private bool _ended;

    public override void _Ready()
    {
        _sanity = new SanityMeter();
        _hud = GetNode<HUD>("HUD");
        _timeRemaining = Mathf.Max(LevelDurationSeconds, 0.01f);
        _hud.SetSanity(_sanity.Value);
        _hud.SetTimeRemaining(_timeRemaining);
    }

    public override void _Process(double delta)
    {
        if (_ended)
            return;

        float duration = Mathf.Max(SanityDurationSeconds, 0.01f);
        float perSecond = 1f / duration;
        _sanity.Decay((float)delta, perSecond);
        _timeRemaining = Mathf.Max(0f, _timeRemaining - (float)delta);

        _hud.SetSanity(_sanity.Value);
        _hud.SetTimeRemaining(_timeRemaining);

        if (_sanity.IsEmpty || _timeRemaining <= 0f)
        {
            _ended = true;
            GetTree().ChangeSceneToFile("res://Scenes/UI/GameOver.tscn");
        }
    }
}
