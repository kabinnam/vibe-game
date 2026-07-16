using Godot;

public partial class Guard : CharacterBody3D
{
	public enum State { Patrol, Alert }
	private State _state = State.Patrol;

	private Node3D _eyes;
	private Node3D _player;

	public override void _Ready()
	{
		_eyes = GetNode<Node3D>("Eyes");
		_player = GetTree().GetFirstNodeInGroup("player") as Node3D;
	}

	public override void _PhysicsProcess(double delta)
	{
		// Perception (Milestones 2-3) and patrol movement (Milestone 5) plug in here.
		if (_state == State.Patrol)
			Patrol();
	}

	private void Patrol() { }   // filled in at Milestone 5
}
