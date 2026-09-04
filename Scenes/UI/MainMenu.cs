using Godot;

/// <summary>
/// Bare-minimum main menu: Start Game loads the 3D level; Quit closes the window.
/// Attach this script to the root Control of MainMenu.tscn.
/// </summary>
public partial class MainMenu : Control
{
	private Button _startButton;
	private Button _quitButton;

	public override void _Ready()
	{
		// Node paths are relative to this script's node (the scene root "MainMenu").
		// They must match the names you give the buttons in the editor.
		_startButton = GetNode<Button>("CenterContainer/VBoxContainer/StartButton");
		_quitButton = GetNode<Button>("CenterContainer/VBoxContainer/QuitButton");

		// Godot's "pressed" signal is exposed as a C# event. += wires a handler;
		// when the button is clicked, our method runs.
		_startButton.Pressed += OnStartPressed;
		_quitButton.Pressed += OnQuitPressed;
	}

	private void OnStartPressed()
	{
		// Swap the entire scene tree to Main.tscn (Arena + Player).
		// The Player's _Ready will capture the mouse for FPS controls.
		GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
	}

	private void OnQuitPressed()
	{
		GetTree().Quit();
	}
}
