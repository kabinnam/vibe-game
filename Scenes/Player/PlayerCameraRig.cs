#nullable enable

using Godot;

public partial class PlayerCameraRig : Node3D
{
    private Camera3D? _firstPersonCamera;
    private Camera3D? _thirdPersonCamera;

    public bool IsFirstPerson { get; private set; } = true;
    public float Pitch => Rotation.X;

    public override void _Ready()
    {
        _firstPersonCamera = GetNodeOrNull<Camera3D>("FirstPersonCamera");
        _thirdPersonCamera = GetNodeOrNull<Camera3D>(
            "ThirdPersonSpringArm/ThirdPersonCamera");

        if (_firstPersonCamera == null || _thirdPersonCamera == null)
        {
            GD.PushError(
                "PlayerCameraRig requires FirstPersonCamera and " +
                "ThirdPersonSpringArm/ThirdPersonCamera.");
            return;
        }

        ApplyMode();
    }

    public void AddPitch(float radians)
    {
        var rotation = Rotation;
        rotation.X = Mathf.Clamp(
            rotation.X + radians,
            Mathf.DegToRad(-85f),
            Mathf.DegToRad(85f));
        Rotation = rotation;
    }

    public void ToggleMode()
    {
        if (_firstPersonCamera == null || _thirdPersonCamera == null)
            return;

        IsFirstPerson = !IsFirstPerson;
        ApplyMode();
    }

    private void ApplyMode()
    {
        if (_firstPersonCamera == null || _thirdPersonCamera == null)
            return;

        _firstPersonCamera.Current = IsFirstPerson;
        _thirdPersonCamera.Current = !IsFirstPerson;
    }
}
