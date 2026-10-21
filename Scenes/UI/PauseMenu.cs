using Godot;

public partial class PauseMenu : CanvasLayer
{
    private Button _resumeButton;
    private Button _quitButton;

    public override void _Ready()
    {
        _resumeButton = GetNode<Button>("CenterContainer/VBoxContainer/ResumeButton");
        _resumeButton.Pressed += Resume;
        _quitButton = GetNode<Button>("CenterContainer/VBoxContainer/QuitButton");
        _quitButton.Pressed += () => GetTree().Quit();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel"))
            return;
        if (GetTree().Paused) Resume(); else Pause();
        GetViewport().SetInputAsHandled();
    }

    private void Pause()
    {
        GetTree().Paused = true;
        Visible = true;
        _resumeButton.GrabFocus();
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void Resume()
    {
        GetTree().Paused = false;
        Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }
}
