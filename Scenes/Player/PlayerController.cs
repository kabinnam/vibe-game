using Godot;

public partial class PlayerController : CharacterBody3D
{
    [Export] public float Speed = 5.0f;
    [Export] public float JumpVelocity = 4.5f;
    [Export] public float MouseSensitivity = 0.002f;
    [Export] public float Gravity = 9.8f;

    private Node3D _head;
    private VapeArmController _vapeArm;

    public override void _Ready()
    {
        _head = GetNode<Node3D>("Head");
        _vapeArm = GetNode<VapeArmController>("Head/ArmPivot");
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion)
        {
            if (_vapeArm.IsVaping)
            {
                // While vaping, the mouse drives the arm instead of the camera.
                _vapeArm.AddMouseImpulse(-mouseMotion.Relative.Y);
            }
            else
            {
                RotateY(-mouseMotion.Relative.X * MouseSensitivity);
                _head.RotateX(-mouseMotion.Relative.Y * MouseSensitivity);
                var rot = _head.Rotation;
                rot.X = Mathf.Clamp(rot.X, Mathf.DegToRad(-90f), Mathf.DegToRad(90f));
                _head.Rotation = rot;
            }
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
    }
}
