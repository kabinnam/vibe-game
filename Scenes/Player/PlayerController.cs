using Godot;

public partial class PlayerController : CharacterBody3D
{
	// Exported Variables
	[Export] public float WalkingSpeed = 3.0f;
	[Export] public float RunningSpeed = 5.0f;
	[Export] public float JumpVelocity = 4.5f;
	[Export] public float MouseSensitivity = 0.002f;
	[Export] public float Gravity = 9.8f;

	// Non-Exported Variables
	public float Speed = 3.0f;
	public bool is_running = false;
	public bool is_locked = false;

	private Node3D _camera_mount;
	private Node3D _visuals;
	private AnimationPlayer _animation_player;

	public override void _Ready()
	{
		Input.MouseMode = Input.MouseModeEnum.Captured;
		_camera_mount = GetNode<Node3D>("CameraMount");
		_visuals = GetNode<Node3D>("Visuals");
		_animation_player = GetNode<AnimationPlayer>("Visuals/YBot/AnimationPlayer");
	}

	// public override void _UnhandledInput(InputEvent @event)
	// {
	// 	if (@event is InputEventMouseMotion mouseMotion)
	// 	{
	// 		RotateY(-mouseMotion.Relative.X * MouseSensitivity);
	// 		_head.RotateX(-mouseMotion.Relative.Y * MouseSensitivity);
	// 		var rot = _head.Rotation;
	// 		rot.X = Mathf.Clamp(rot.X, Mathf.DegToRad(-90f), Mathf.DegToRad(90f));
	// 		_head.Rotation = rot;
	// 	}

	// 	if (@event.IsActionPressed("ui_cancel"))
	// 		Input.MouseMode = Input.MouseModeEnum.Visible;
	// }

	// public override void _PhysicsProcess(double delta)
	// {
	// 	var velocity = Velocity;

	// 	if (!IsOnFloor())
	// 		velocity.Y -= Gravity * (float)delta;

	// 	if (Input.IsActionJustPressed("jump") && IsOnFloor())
	// 		velocity.Y = JumpVelocity;

	// 	var inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
	// 	var direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();

	// 	if (direction != Vector3.Zero)
	// 	{
	// 		velocity.X = direction.X * Speed;
	// 		velocity.Z = direction.Z * Speed;
	// 	}
	// 	else
	// 	{
	// 		velocity.X = Mathf.MoveToward(velocity.X, 0, Speed);
	// 		velocity.Z = Mathf.MoveToward(velocity.Z, 0, Speed);
	// 	}

	// 	Velocity = velocity;
	// 	MoveAndSlide();
	// }

	public override void _Input(InputEvent @event)
	{
		// Handle mouse movement
		if (@event is InputEventMouseMotion mouseMotion)
		{
			// Rotate the player horizontally based on the mouse movement
			RotateY(-mouseMotion.Relative.X * MouseSensitivity);

			// Rotate visuals horizontally opposite to the mouse movement, to appear static
			_visuals.RotateY(mouseMotion.Relative.X * MouseSensitivity);

			// Rotate the camera vertically based on the mouse movement,
			_camera_mount.RotateX(-mouseMotion.Relative.Y * MouseSensitivity);

			// Clamp the camera mount's vertical rotation to prevent it from going too high or low
			var rot = _camera_mount.Rotation;
			rot.X = Mathf.Clamp(rot.X, Mathf.DegToRad(-90f), Mathf.DegToRad(90f));
			_camera_mount.Rotation = rot;
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

		// Get the input direction and handle the movement/deceleration.
		// As good practice, you should replace UI actions with custom gameplay actions.
		Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
		Vector3 direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();
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

				_visuals.LookAt(_visuals.GlobalPosition + -direction, Vector3.Up);
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
