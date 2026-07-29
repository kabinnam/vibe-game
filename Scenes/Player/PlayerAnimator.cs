using Godot;

public partial class PlayerAnimator : Node3D
{
    private const float ShoulderRestAngle = 1.15f;

    private enum BoneSlot
    {
        Hips,
        Spine02,
        Spine03,
        ShoulderL,
        ElbowL,
        ShoulderR,
        ElbowR,
        HandR,
        UpperLegL,
        LowerLegL,
        AnkleL,
        UpperLegR,
        LowerLegR,
        AnkleR,
        Head,
        Eyes,
        Eyebrows,
        Count
    }

    private static readonly StringName[] AnimatedNames =
    {
        "Hips", "Spine_02", "Spine_03", "Shoulder_L", "Elbow_L",
        "Shoulder_R", "Elbow_R", "Hand_R", "UpperLeg_L", "LowerLeg_L",
        "Ankle_L", "UpperLeg_R", "LowerLeg_R", "Ankle_R"
    };

    private static readonly StringName[] HeadNames = { "Head", "Eyes", "Eyebrows" };

    private readonly int[] _boneIndices = new int[(int)BoneSlot.Count];
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

        System.Array.Fill(_boneIndices, -1);
        Cache(AnimatedNames, 0);
        Cache(HeadNames, AnimatedNames.Length);
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
        ApplyArmRest();

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

        for (var slot = (int)BoneSlot.Head; slot < (int)BoneSlot.Count; slot++)
        {
            var index = _boneIndices[slot];
            if (index >= 0)
            {
                _skeleton.SetBonePoseScale(
                    index,
                    firstPerson ? Vector3.One * 0.001f : _scales[index]);
            }
        }
    }

    private void Cache(StringName[] names, int slotOffset)
    {
        for (var offset = 0; offset < names.Length; offset++)
        {
            var name = names[offset];
            var index = _skeleton.FindBone(name);
            if (index < 0)
                GD.PushWarning($"PlayerAnimator: optional bone '{name}' is unavailable.");
            else
                _boneIndices[slotOffset + offset] = index;
        }
    }

    private void ResetPose()
    {
        for (var slot = 0; slot < AnimatedNames.Length; slot++)
        {
            var index = _boneIndices[slot];
            if (index >= 0)
            {
                _skeleton.SetBonePoseRotation(index, _rotations[index]);
                _skeleton.SetBonePosePosition(index, _positions[index]);
            }
        }
    }

    private void ApplyIdle()
    {
        var breath = Mathf.Sin(State.CyclePhase) * 0.035f;
        var freeRightArm = 1f - State.SmokeBlend;
        Rotate(BoneSlot.Spine02, new Vector3(breath, 0f, 0f));
        Rotate(BoneSlot.Spine03, new Vector3(breath * 0.6f, 0f, 0f));
        Rotate(BoneSlot.ShoulderL, new Vector3(0f, 0f, breath * 0.35f));
        Rotate(BoneSlot.ShoulderR, new Vector3(0f, 0f, -breath * 0.35f * freeRightArm));
    }

    private void ApplyArmRest()
    {
        Rotate(BoneSlot.ShoulderL, new Vector3(0f, 0f, -ShoulderRestAngle));
        Rotate(BoneSlot.ShoulderR, new Vector3(0f, 0f, -ShoulderRestAngle));
    }

    private void ApplyWalk()
    {
        var amplitude = Mathf.Lerp(0.15f, 0.72f, State.NormalizedSpeed);
        var left = Mathf.Sin(State.CyclePhase) * amplitude;
        var right = -left;
        var freeRightArm = 1f - State.SmokeBlend;

        Rotate(BoneSlot.Hips, new Vector3(0f, Mathf.Sin(State.CyclePhase * 2f) * 0.06f, 0f));
        Rotate(BoneSlot.UpperLegL, new Vector3(left, 0f, 0f));
        Rotate(
            BoneSlot.LowerLegL,
            new Vector3(Mathf.Max(0f, -Mathf.Sin(State.CyclePhase)) * amplitude * 0.8f, 0f, 0f));
        Rotate(BoneSlot.AnkleL, new Vector3(-left * 0.35f, 0f, 0f));
        Rotate(BoneSlot.UpperLegR, new Vector3(right, 0f, 0f));
        Rotate(
            BoneSlot.LowerLegR,
            new Vector3(Mathf.Max(0f, Mathf.Sin(State.CyclePhase)) * amplitude * 0.8f, 0f, 0f));
        Rotate(BoneSlot.AnkleR, new Vector3(-right * 0.35f, 0f, 0f));
        Rotate(BoneSlot.ShoulderL, new Vector3(right * 0.65f, 0f, 0f));
        Rotate(BoneSlot.ShoulderR, new Vector3(left * 0.65f * freeRightArm, 0f, 0f));
    }

    private void ApplyAirborne(float verticalVelocity)
    {
        var freeRightArm = 1f - State.SmokeBlend;
        Rotate(BoneSlot.Hips, new Vector3(verticalVelocity > 0f ? -0.10f : 0.12f, 0f, 0f));
        Rotate(BoneSlot.UpperLegL, new Vector3(0.30f, 0f, -0.08f));
        Rotate(BoneSlot.LowerLegL, new Vector3(0.42f, 0f, 0f));
        Rotate(BoneSlot.UpperLegR, new Vector3(-0.12f, 0f, 0.08f));
        Rotate(BoneSlot.LowerLegR, new Vector3(0.20f, 0f, 0f));
        Rotate(BoneSlot.ShoulderL, new Vector3(-0.18f, 0f, 0.12f));
        Rotate(BoneSlot.ShoulderR, new Vector3(-0.18f, 0f, -0.12f) * freeRightArm);
    }

    private void ApplySmoking(float blend)
    {
        Rotate(BoneSlot.ShoulderR, new Vector3(-0.65f, -0.20f, 1.60f) * blend);
        Rotate(BoneSlot.ElbowR, new Vector3(0f, 0f, 2.20f) * blend);
        Rotate(BoneSlot.HandR, new Vector3(0.12f, 0.30f, -0.18f) * blend);
    }

    private void Rotate(BoneSlot slot, Vector3 offset)
    {
        var index = _boneIndices[(int)slot];
        if (index < 0)
            return;

        _skeleton.SetBonePoseRotation(
            index,
            _skeleton.GetBonePoseRotation(index) * Quaternion.FromEuler(offset));
    }
}
