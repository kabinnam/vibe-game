using Godot;

public partial class GameOver : Control
{
	private Button _restartButton;
	private Button _quitButton;

	public override void _Ready()
	{
		_restartButton = GetNode<Button>("CenterContainer/VBoxContainer/RestartButton");
		_quitButton = GetNode<Button>("CenterContainer/VBoxContainer/QuitButton");

		_restartButton.Pressed += OnRestartPressed;
		_quitButton.Pressed += OnQuitPressed;
	}

	private void OnRestartPressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
	}

	private void OnQuitPressed()
	{
		GetTree().Quit();
	}
}
