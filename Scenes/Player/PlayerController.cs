#nullable enable

using Godot;

public partial class PlayerController : CharacterBody3D
{
    [Export] public float Speed = 5.0f;
    [Export] public float JumpVelocity = 4.5f;
    [Export] public float MouseSensitivity = 0.002f;
    [Export] public float Gravity = 9.8f;

    private PlayerAnimator? _animator;
    private PlayerCameraRig? _cameraRig;

    public override void _Ready()
    {
        _animator = GetNodeOrNull<PlayerAnimator>("PlayerAvatar");
        _cameraRig = GetNodeOrNull<PlayerCameraRig>("PlayerCameraRig");

        if (_animator == null)
            GD.PushError("PlayerAvatar is missing; movement remains enabled.");

        if (_cameraRig == null)
            GD.PushError("PlayerCameraRig is missing; movement remains enabled.");

        _animator?.SetFirstPerson(_cameraRig?.IsFirstPerson ?? true);
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            RotateY(-motion.Relative.X * MouseSensitivity);
            _cameraRig?.AddPitch(-motion.Relative.Y * MouseSensitivity);
        }

        if (@event.IsActionPressed("toggle_camera"))
        {
            _cameraRig?.ToggleMode();
            _animator?.SetFirstPerson(_cameraRig?.IsFirstPerson ?? true);
        }

        if (@event.IsActionPressed("ui_cancel"))
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _PhysicsProcess(double delta)
    {
        var velocity = Velocity;

        if (!IsOnFloor())
            velocity.Y -= Gravity * (float)delta;

        if (Input.IsActionJustPressed("jump") && IsOnFloor())
            velocity.Y = JumpVelocity;

        var inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        var direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();

        if (direction != Vector3.Zero)
        {
            velocity.X = direction.X * Speed;
            velocity.Z = direction.Z * Speed;
        }
        else
        {
            velocity.X = Mathf.MoveToward(velocity.X, 0, Speed);
            velocity.Z = Mathf.MoveToward(velocity.Z, 0, Speed);
        }

        Velocity = velocity;
        MoveAndSlide();

        var horizontalSpeed = new Vector2(Velocity.X, Velocity.Z).Length();
        var normalizedSpeed = Speed > 0f ? horizontalSpeed / Speed : 0f;
        _animator?.Advance(
            (float)delta,
            normalizedSpeed,
            IsOnFloor(),
            Velocity.Y,
            Input.IsActionPressed("smoke"));
    }
}
