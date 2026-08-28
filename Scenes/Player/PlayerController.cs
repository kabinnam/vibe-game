using Godot;

public partial class PlayerController : CharacterBody3D
{
	// Exported Variables
	[Export] public float WalkingSpeed = 3.0f;
	[Export] public float RunningSpeed = 5.0f;
	[Export] public float JumpVelocity = 4.5f;
	[Export] public float MouseSensitivity = 0.002f;
	[Export] public float Gravity = 9.8f;

	// Camera tunables
	[Export] public float TiltLimitDegrees = 80.0f; // how far up and down the player can look--keeps us from inverting the camera and helps prevent looking inside the model.

	// Non-Exported Variables
	public float Speed = 3.0f;
	public bool is_running = false;
	public bool is_locked = false; // If the player is locked, they cannot move or rotate (Useful for actions like kicking or maybe smoking?)

	private Node3D _visuals;
	private AnimationPlayer _animation_player;

	private Node3D _springArmPivot;
	private Camera3D _camera;

	public override void _Ready()
	{
		Input.MouseMode = Input.MouseModeEnum.Captured;
		_visuals = GetNode<Node3D>("Visuals");
		_animation_player = GetNode<AnimationPlayer>("Visuals/YBot/AnimationPlayer");

		_springArmPivot = GetNode<Node3D>("SpringArmPivot");
		_camera = GetNode<Camera3D>("SpringArmPivot/SpringArm3D/Camera3D");
		_camera.Current = true;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		// Handle mouse look: both yaw and pitch live on the pivot
		if (@event is InputEventMouseMotion mouseMotion)
		{
			Vector3 rot = _springArmPivot.Rotation;
			rot.X -= mouseMotion.ScreenRelative.Y * MouseSensitivity;
			rot.X = Mathf.Clamp(rot.X, Mathf.DegToRad(-TiltLimitDegrees), Mathf.DegToRad(TiltLimitDegrees));
			rot.Y -= mouseMotion.ScreenRelative.X * MouseSensitivity;
			_springArmPivot.Rotation = rot;
		}

		// Toggle the mouse cursor: first press frees it, next press recaptures it
		if (@event.IsActionPressed("ui_cancel"))
		{
			Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
				? Input.MouseModeEnum.Visible
				: Input.MouseModeEnum.Captured;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		// Handle kick

		if(!_animation_player.IsPlaying())
		{
			is_locked = false;
		}

		// Handle running
		if (Input.IsActionPressed("run"))
		{
			Speed = RunningSpeed;
			is_running = true;
		}
		else
		{
			Speed = WalkingSpeed;
			is_running = false;
		}

		Vector3 velocity = Velocity;

		// Add the gravity.
		if (!IsOnFloor())
		{
			velocity += GetGravity() * (float)delta;
		}

		// Handle Jump.
		if (Input.IsActionJustPressed("ui_accept") && IsOnFloor())
		{
			velocity.Y = JumpVelocity;
		}

		// Get the input direction (relative to where the camera is looking) and handle movement/deceleration.
		Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
		Vector3 direction = new Vector3(inputDir.X, 0, inputDir.Y)
			.Rotated(Vector3.Up, _springArmPivot.GlobalRotation.Y)
			.Normalized();
		if (direction != Vector3.Zero)
		{
			if(!is_locked)
			{
				if (is_running)
				{
					if(_animation_player.GetCurrentAnimation() != "running")
					{
						_animation_player.Play("Jogging/mixamo_com");
					}
				}
				else
				{
					if(_animation_player.GetCurrentAnimation() != "walking")
					{
						_animation_player.Play("Walking/mixamo_com");
					}
				}

				// Face the movement direction (model authored facing +Z, so aim away from it)
				_visuals.LookAt(_visuals.GlobalPosition - direction, Vector3.Up);
			}

			velocity.X = direction.X * Speed;
			velocity.Z = direction.Z * Speed;
		}
		else
		{
			if(!is_locked && _animation_player.GetCurrentAnimation() != "idle")
			{
				_animation_player.Play("StandIdle/mixamo_com");
			}

			velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed);
			velocity.Z = Mathf.MoveToward(Velocity.Z, 0, Speed);
		}

		Velocity = velocity;
		if(!is_locked)
		{
			MoveAndSlide();
		}
	}
}
