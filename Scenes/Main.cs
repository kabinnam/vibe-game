using Godot;
using VibeGame.Player;

public partial class Main : Node3D
{
    [Export] public float LevelDurationSeconds { get; set; } = 300f; // 5 minutes

    private Sanity _sanity;
    private HUD _hud;
    private float _timeRemaining;
    private bool _ended;

    public override void _Ready()
    {
        _sanity = GetNode<PlayerController>("Player").Sanity;
        _hud = GetNode<HUD>("HUD");

        _sanity.ValueChanged += _hud.SetSanity;
        _sanity.Exhausted += EndRun;

        // Children _Ready first, so Sanity's initial ValueChanged already fired; seed by hand.
        _hud.SanityMax = _sanity.Max;
        _hud.SetSanity(_sanity.CurrentValue);
        
        _timeRemaining = Mathf.Max(LevelDurationSeconds, 0.01f);
        _hud.SetTimeRemaining(_timeRemaining);
    }

    public override void _ExitTree()
    {
        _sanity.ValueChanged -= _hud.SetSanity;
        _sanity.Exhausted -= EndRun;
    }

    public override void _Process(double delta)
    {
        _timeRemaining = Mathf.Max(0f, _timeRemaining - (float)delta);
        _hud.SetTimeRemaining(_timeRemaining);

        if (_timeRemaining <= 0f)
        {
            EndRun();
        }
    }

    private void EndRun()
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        GetTree().ChangeSceneToFile("res://Scenes/UI/GameOver.tscn");
    }
}
