using System.Collections.Generic;
using Godot;

public partial class PlayerAnimator : Node3D
{
    private static readonly StringName[] Animated =
    {
        "Hips", "Spine_02", "Spine_03", "Shoulder_L", "Elbow_L",
        "Shoulder_R", "Elbow_R", "Hand_R", "UpperLeg_L", "LowerLeg_L",
        "Ankle_L", "UpperLeg_R", "LowerLeg_R", "Ankle_R"
    };

    private static readonly StringName[] HeadBones = { "Head", "Eyes", "Eyebrows" };

    private readonly Dictionary<StringName, int> _bones = new();
    private Quaternion[] _rotations = System.Array.Empty<Quaternion>();
    private Vector3[] _positions = System.Array.Empty<Vector3>();
    private Vector3[] _scales = System.Array.Empty<Vector3>();
    private Skeleton3D _skeleton = null!;

    public PlayerAnimationState State { get; } = new();
    public bool IsRigReady { get; private set; }

    public override void _Ready()
    {
        _skeleton = GetNodeOrNull<Skeleton3D>("CharacterModel/Skeleton3D");
        if (_skeleton == null)
        {
            GD.PushError("PlayerAnimator requires CharacterModel/Skeleton3D; movement remains enabled.");
            return;
        }

        GetNodeOrNull<AnimationPlayer>("CharacterModel/AnimationPlayer")?.Stop();

        var count = _skeleton.GetBoneCount();
        _rotations = new Quaternion[count];
        _positions = new Vector3[count];
        _scales = new Vector3[count];
        for (var index = 0; index < count; index++)
        {
            _rotations[index] = _skeleton.GetBonePoseRotation(index);
            _positions[index] = _skeleton.GetBonePosePosition(index);
            _scales[index] = _skeleton.GetBonePoseScale(index);
        }

        Cache(Animated);
        Cache(HeadBones);
        IsRigReady = true;
    }

    public void Advance(
        float delta,
        float speed,
        bool grounded,
        float verticalVelocity,
        bool smoking)
    {
        if (!IsRigReady)
            return;

        State.Advance(delta, speed, grounded, smoking);
        ResetPose();

        if (State.LocomotionMode == PlayerLocomotionMode.Idle)
            ApplyIdle();
        else if (State.LocomotionMode == PlayerLocomotionMode.Walk)
            ApplyWalk();
        else
            ApplyAirborne(verticalVelocity);

        ApplySmoking(State.SmokeBlend);
    }

    public void SetFirstPerson(bool firstPerson)
    {
        if (!IsRigReady)
            return;

        foreach (var name in HeadBones)
        {
            if (_bones.TryGetValue(name, out var index))
            {
                _skeleton.SetBonePoseScale(
                    index,
                    firstPerson ? Vector3.One * 0.001f : _scales[index]);
            }
        }
    }

    private void Cache(IEnumerable<StringName> names)
    {
        foreach (var name in names)
        {
            var index = _skeleton.FindBone(name);
            if (index < 0)
                GD.PushWarning($"PlayerAnimator: optional bone '{name}' is unavailable.");
            else
                _bones[name] = index;
        }
    }

    private void ResetPose()
    {
        foreach (var name in Animated)
        {
            if (_bones.TryGetValue(name, out var index))
            {
                _skeleton.SetBonePoseRotation(index, _rotations[index]);
                _skeleton.SetBonePosePosition(index, _positions[index]);
            }
        }
    }

    private void ApplyIdle()
    {
        var breath = Mathf.Sin(State.CyclePhase) * 0.035f;
        Rotate("Spine_02", new Vector3(breath, 0f, 0f));
        Rotate("Spine_03", new Vector3(breath * 0.6f, 0f, 0f));
        Rotate("Shoulder_L", new Vector3(0f, 0f, breath * 0.35f));
        Rotate("Shoulder_R", new Vector3(0f, 0f, -breath * 0.35f));
    }

    private void ApplyWalk()
    {
        var amplitude = Mathf.Lerp(0.15f, 0.72f, State.NormalizedSpeed);
        var left = Mathf.Sin(State.CyclePhase) * amplitude;
        var right = -left;

        Rotate("Hips", new Vector3(0f, Mathf.Sin(State.CyclePhase * 2f) * 0.06f, 0f));
        Rotate("UpperLeg_L", new Vector3(left, 0f, 0f));
        Rotate(
            "LowerLeg_L",
            new Vector3(Mathf.Max(0f, -Mathf.Sin(State.CyclePhase)) * amplitude * 0.8f, 0f, 0f));
        Rotate("Ankle_L", new Vector3(-left * 0.35f, 0f, 0f));
        Rotate("UpperLeg_R", new Vector3(right, 0f, 0f));
        Rotate(
            "LowerLeg_R",
            new Vector3(Mathf.Max(0f, Mathf.Sin(State.CyclePhase)) * amplitude * 0.8f, 0f, 0f));
        Rotate("Ankle_R", new Vector3(-right * 0.35f, 0f, 0f));
        Rotate("Shoulder_L", new Vector3(right * 0.65f, 0f, 0f));
        Rotate("Shoulder_R", new Vector3(left * 0.65f, 0f, 0f));
    }

    private void ApplyAirborne(float verticalVelocity)
    {
        Rotate("Hips", new Vector3(verticalVelocity > 0f ? -0.10f : 0.12f, 0f, 0f));
        Rotate("UpperLeg_L", new Vector3(0.30f, 0f, -0.08f));
        Rotate("LowerLeg_L", new Vector3(0.42f, 0f, 0f));
        Rotate("UpperLeg_R", new Vector3(-0.12f, 0f, 0.08f));
        Rotate("LowerLeg_R", new Vector3(0.20f, 0f, 0f));
        Rotate("Shoulder_L", new Vector3(-0.18f, 0f, 0.12f));
        Rotate("Shoulder_R", new Vector3(-0.18f, 0f, -0.12f));
    }

    private void ApplySmoking(float blend)
    {
        Rotate("Shoulder_R", new Vector3(-0.65f, -0.20f, -0.35f) * blend);
        Rotate("Elbow_R", new Vector3(-1.45f, 0.10f, 0.12f) * blend);
        Rotate("Hand_R", new Vector3(0.12f, 0.30f, -0.18f) * blend);
    }

    private void Rotate(StringName name, Vector3 offset)
    {
        if (!_bones.TryGetValue(name, out var index))
            return;

        _skeleton.SetBonePoseRotation(
            index,
            _skeleton.GetBonePoseRotation(index) * Quaternion.FromEuler(offset));
    }
}
