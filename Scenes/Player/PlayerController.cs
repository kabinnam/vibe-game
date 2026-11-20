using System;
using Godot;

namespace VibeGame.Player;

public partial class PlayerController : CharacterBody3D
{
    // Exported Variables
    [Export] public float WalkingSpeed = 3.0f;
    [Export] public float RunningSpeed = 5.0f;
    [Export] public float JumpVelocity = 4.5f;
    [Export] public float MouseSensitivity = 0.002f;
    [Export] public float Gravity = 9.8f;
    [Export] public float VapingSanityRecoveryRate = 6f;
    // Camera tunables
    [Export] public float TiltLimitDegrees = 80.0f; // how far up and down the player can look--keeps us from inverting the camera and helps prevent looking inside the model.
    [Export] public float ThirdPersonSpringLength = 2.5f;
    [Export] public float ThirdPersonFov = 75.0f;
    [Export] public float FirstPersonFov = 90.0f;
    [Export] public Vector3 FirstPersonCameraOffset = new Vector3(0.0f, 0.16f, -0.22f); // manually tweaked and very specific to our ybot model. pushes first person camera right in front of the face.
    [Export] public float VapeArmRaiseSpeed = 4.0f; // 4 = about 0.25s to raise or lower the arm

    // Non-Exported Variables
    public float Speed = 3.0f;
    public bool is_running = false;

    private Node3D _visuals;
    private AnimationTree _animTree;
    private float _vapeArmBlend; // 0 = arm follows idle/walk/run, 1 = hand at mouth
    private Marker3D _lookAnchor; // LookAnchor marker in Player.tscn: where NPCs should aim their heads at us
    private Guid _vapingSanityModifierHandle;

    // Public API for observers (e.g. Guard, Main). The player owns knowledge of its own anatomy; move the LookAnchor node to change it.
    public Vector3 LookAnchorPosition => _lookAnchor.GlobalPosition;
    public bool IsVaping { get; private set; }
    public Sanity Sanity { get; private set; }
    public Lungs Lungs { get; private set; }

    private Node3D _springArmPivot;
    private SpringArm3D _springArm;
    private Camera3D _camera;
    private bool _firstPerson = false;

    // Node names authored in the AnimationTree in Player.tscn. They must match exactly.
    private static readonly StringName LocomotionRequestParam = "parameters/Locomotion/transition_request";
    private static readonly StringName VapeBlendParam = "parameters/VapeBlend/blend_amount";

    // Input actions defined in Project Settings > Input Map. They must match exactly.
    private static readonly StringName InhaleAction = "inhale";
    private static readonly StringName ExhaleAction = "exhale";
    private static readonly StringName RunAction = "run";
    private static readonly StringName JumpAction = "ui_accept";
    private static readonly StringName MoveLeftAction = "move_left";
    private static readonly StringName MoveRightAction = "move_right";
    private static readonly StringName MoveForwardAction = "move_forward";
    private static readonly StringName MoveBackwardAction = "move_backward";
    private static readonly StringName TogglePerspectiveAction = "toggle_perspective";

    public override void _Ready()
    {
        if (!float.IsFinite(VapingSanityRecoveryRate) || VapingSanityRecoveryRate < 0f)
        {
            throw new InvalidOperationException("PlayerController: VapingSanityRecoveryRate must be finite and non-negative.");
        }
        Sanity = GetNode<Sanity>("Sanity");
        Lungs = GetNode<Lungs>("Lungs");
        Lungs.StateChanged += OnLungsStateChanged;

        Input.MouseMode = Input.MouseModeEnum.Captured;
        _visuals = GetNode<Node3D>("Visuals");
        _animTree = GetNode<AnimationTree>("Visuals/YBot/AnimationTree");
        _animTree.Active = true;
        _lookAnchor = GetNode<Marker3D>("%LookAnchor");

        _springArmPivot = GetNode<Node3D>("SpringArmPivot");
        _springArm = GetNode<SpringArm3D>("SpringArmPivot/SpringArm3D");
        _camera = GetNode<Camera3D>("SpringArmPivot/SpringArm3D/CameraBoom/Camera3D"); // Camera3D intentionally not direct child of SpringArm3D because the spring arm wants to overwrite the position of its direct child.
        _camera.Current = true;

        ApplyPerspective();
    }

    public override void _ExitTree()
    {
        Lungs.StateChanged -= OnLungsStateChanged;
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

        // Toggle between first- and third-person view
        if (@event.IsActionPressed(TogglePerspectiveAction))
        {
            _firstPerson = !_firstPerson;
            ApplyPerspective();
        }

        // Exhale the current breath
        if (@event.IsActionPressed(ExhaleAction))
        {
            Lungs.Exhale();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // Handle vaping
        HandleVaping((float)delta);

        // Handle running
        if (Input.IsActionPressed(RunAction))
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
        if (Input.IsActionJustPressed(JumpAction) && IsOnFloor())
        {
            velocity.Y = JumpVelocity;
        }

        // Get the input direction (relative to where the camera is looking) and handle movement/deceleration.
        Vector2 inputDir = Input.GetVector(MoveLeftAction, MoveRightAction, MoveForwardAction, MoveBackwardAction);
        Vector3 direction = new Vector3(inputDir.X, 0, inputDir.Y)
            .Rotated(Vector3.Up, _springArmPivot.GlobalRotation.Y)
            .Normalized();
        if (direction != Vector3.Zero)
        {
            velocity.X = direction.X * Speed;
            velocity.Z = direction.Z * Speed;
        }
        else
        {
            velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed);
            velocity.Z = Mathf.MoveToward(Velocity.Z, 0, Speed);
        }

        string locomotionState = direction == Vector3.Zero ? "Idle" : (is_running ? "Run" : "Walk");
        _animTree.Set(LocomotionRequestParam, locomotionState);

        Velocity = velocity;
        UpdateCharacterFacing(direction);
        MoveAndSlide();
    }

    private void HandleVaping(float delta)
    {
        IsVaping = GetWindow().HasFocus() && Input.IsActionPressed(InhaleAction);
        _vapeArmBlend = Mathf.MoveToward(_vapeArmBlend, IsVaping ? 1f : 0f, VapeArmRaiseSpeed * delta);
        _animTree.Set(VapeBlendParam, Mathf.SmoothStep(0f, 1f, _vapeArmBlend));
        Lungs.SetInhaleRequested(IsVaping && _vapeArmBlend >= 1f);
    }

    private void OnLungsStateChanged(Lungs.BreathState state)
    {
        bool shouldRecoverSanity = state == Lungs.BreathState.Inhaling;
        bool isRecoveringSanity = _vapingSanityModifierHandle != Guid.Empty;

        if (shouldRecoverSanity == isRecoveringSanity)
        {
            return;
        }

        if (shouldRecoverSanity)
        {
            _vapingSanityModifierHandle = Sanity.AddRateModifier(VapingSanityRecoveryRate);
        }
        else
        {
            Sanity.RemoveRateModifier(_vapingSanityModifierHandle);
            _vapingSanityModifierHandle = Guid.Empty;
        }
    }

    private void ApplyPerspective()
    {
        if (_firstPerson)
        {
            _springArm.SpringLength = 0f;
            _camera.Position = FirstPersonCameraOffset;
            _camera.Fov = FirstPersonFov;
        }
        else
        {
            _springArm.SpringLength = ThirdPersonSpringLength;
            _camera.Position = Vector3.Zero;
            _camera.Fov = ThirdPersonFov;
        }
    }

    private void UpdateCharacterFacing(Vector3 moveDirection)
    {
        Vector3 faceDirection;
        if (_firstPerson)
        {
            // First person: face where we are looking (pivot forward, flattened)
            faceDirection = -_springArmPivot.GlobalTransform.Basis.Z;
            faceDirection.Y = 0f;
            faceDirection = faceDirection.Normalized();
        }
        else if (moveDirection != Vector3.Zero)
        {
            // Third person: face the movement direction
            faceDirection = moveDirection;
        }
        else
        {
            // Third person and standing still: keep current facing
            return;
        }

        _visuals.LookAt(_visuals.GlobalPosition + faceDirection, Vector3.Up);
    }
}
