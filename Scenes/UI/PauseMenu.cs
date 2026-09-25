using Godot;

public partial class PauseMenu : CanvasLayer
{
    public override void _Ready()
    {
        GetNode<Button>("CenterContainer/VBoxContainer/ResumeButton").Pressed += Resume;
        GetNode<Button>("CenterContainer/VBoxContainer/QuitButton").Pressed += () => GetTree().Quit();
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
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void Resume()
    {
        GetTree().Paused = false;
        Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }
}
