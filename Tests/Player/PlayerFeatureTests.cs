using System;
using System.Collections.Generic;
using Godot;

public partial class PlayerFeatureTests : Node
{
    private int _count;

    public override void _Ready()
    {
        try
        {
            TestAnimationState();
            TestAvatarScene();
            TestAnimator();
            TestCameraRig();
            TestPlayerIntegration();
            TestAnimatorPoseOutputs();
            GD.Print($"PASS PlayerFeatureTests ({_count} assertions)");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"FAIL PlayerFeatureTests: {exception}");
            GetTree().Quit(1);
        }
    }

    private void TestAnimationState()
    {
        var state = new PlayerAnimationState();

        Equal(PlayerLocomotionMode.Idle, state.LocomotionMode, "new states start idle");

        state.Advance(0.1f, 0.8f, true, false);
        Equal(PlayerLocomotionMode.Walk, state.LocomotionMode, "grounded movement selects walk");
        True(state.CyclePhase > 0f, "walking advances the cycle phase");

        state.Advance(0.1f, 0.8f, false, false);
        Equal(PlayerLocomotionMode.Airborne, state.LocomotionMode, "ungrounded movement selects airborne");

        state.Advance(0.1f, 0f, true, true);
        Near(0.5f, state.SmokeBlend, "smoking fades in at five units per second");

        state.Advance(0.1f, 0f, true, false);
        Near(0f, state.SmokeBlend, "stopping smoking fades out at five units per second");

        var clampedLowSpeed = new PlayerAnimationState();
        clampedLowSpeed.Advance(0.1f, -1f, true, false);
        Near(0f, clampedLowSpeed.NormalizedSpeed, "negative speed clamps to zero");
        Equal(PlayerLocomotionMode.Idle, clampedLowSpeed.LocomotionMode, "clamped low speed selects idle");

        var clampedHighSpeed = new PlayerAnimationState();
        clampedHighSpeed.Advance(0.1f, 2f, true, false);
        Near(1f, clampedHighSpeed.NormalizedSpeed, "speed above one clamps to one");

        var wrappedPhase = new PlayerAnimationState();
        wrappedPhase.Advance(10.1f, 1f, true, false);
        True(
            wrappedPhase.CyclePhase >= 0f && wrappedPhase.CyclePhase < MathF.Tau,
            "large positive deltas keep cycle phase within one turn");
    }

    private void TestAvatarScene()
    {
        var packed = GD.Load<PackedScene>("res://Scenes/Player/PlayerAvatar.tscn");
        True(packed != null, "avatar scene loads");
        var avatar = packed.Instantiate<Node3D>();
        AddChild(avatar);
        var skeleton = avatar.GetNodeOrNull<Skeleton3D>("CharacterModel/Skeleton3D");
        True(skeleton != null, "avatar contains skeleton");
        True(skeleton.FindBone("Hand_R") >= 0, "right hand bone exists");
        True(skeleton.FindBone("Head") >= 0, "head bone exists");
        True(
            avatar.GetNode<MeshInstance3D>("CharacterModel/Skeleton3D/SM_Character_Male_01").Visible,
            "male is visible");
        True(
            !avatar.GetNode<MeshInstance3D>("CharacterModel/Skeleton3D/SM_Character_Female_01").Visible,
            "female is hidden");
        var attachment = avatar.GetNode<BoneAttachment3D>(
            "CharacterModel/Skeleton3D/RightHandAttachment");
        Equal("Hand_R", attachment.BoneName, "prop targets right hand");
        True(attachment.HasNode("SmokingProp"), "prop is attached");
        var propBody = attachment.GetNodeOrNull<MeshInstance3D>("SmokingProp/Body");
        True(propBody?.Mesh is CylinderMesh, "prop body uses a cylinder mesh");
        var propMouthpiece = attachment.GetNodeOrNull<MeshInstance3D>("SmokingProp/Mouthpiece");
        True(propMouthpiece?.Mesh is CylinderMesh, "prop mouthpiece uses a cylinder mesh");
        True(
            propMouthpiece?.Mesh is CylinderMesh { Material: not null },
            "prop mouthpiece cylinder has an intentional material");
        avatar.QueueFree();
    }

    private void TestAnimator()
    {
        var avatar = GD.Load<PackedScene>("res://Scenes/Player/PlayerAvatar.tscn").Instantiate<Node3D>();
        AddChild(avatar);
        var animator = avatar as PlayerAnimator;
        True(animator != null && animator.IsRigReady, "animator resolves rig");
        animator.Advance(0.1f, 1.0f, true, 0.0f, true);
        Equal(PlayerLocomotionMode.Walk, animator.State.LocomotionMode, "animator receives walk");
        True(animator.State.SmokeBlend > 0.0f, "animator receives smoke");
        var skeleton = avatar.GetNode<Skeleton3D>("CharacterModel/Skeleton3D");
        var head = skeleton.FindBone("Head");
        var visible = skeleton.GetBonePoseScale(head).Length();
        animator.SetFirstPerson(true);
        True(skeleton.GetBonePoseScale(head).Length() < visible, "first person hides head");
        animator.SetFirstPerson(false);
        Near(
            visible,
            skeleton.GetBonePoseScale(head).Length(),
            "third person restores head",
            0.001f);
        avatar.QueueFree();
    }

    private void TestCameraRig()
    {
        var rig = GD.Load<PackedScene>("res://Scenes/Player/PlayerCameraRig.tscn")
            .Instantiate<PlayerCameraRig>();
        AddChild(rig);

        True(rig.IsFirstPerson, "camera rig starts in first person");
        True(
            rig.GetNode<Camera3D>("FirstPersonCamera").Current,
            "first-person camera starts current");

        rig.ToggleMode();
        True(!rig.IsFirstPerson, "camera rig toggles to third person");
        True(
            rig.GetNode<Camera3D>("ThirdPersonSpringArm/ThirdPersonCamera").Current,
            "third-person camera becomes current");

        rig.AddPitch(10f);
        Near(
            Mathf.DegToRad(85f),
            rig.Pitch,
            "camera pitch clamps at positive 85 degrees",
            0.001f);

        rig.AddPitch(-20f);
        Near(
            Mathf.DegToRad(-85f),
            rig.Pitch,
            "camera pitch clamps at negative 85 degrees",
            0.001f);

        rig.QueueFree();
    }

    private void TestPlayerIntegration()
    {
        True(InputMap.HasAction("toggle_camera"), "toggle camera input exists");
        True(InputMap.HasAction("smoke"), "smoke input exists");
        var player = GD.Load<PackedScene>("res://Scenes/Player/Player.tscn")
            .Instantiate<CharacterBody3D>();
        AddChild(player);
        True(
            player.GetNodeOrNull<PlayerAnimator>("PlayerAvatar") != null,
            "player composes avatar");
        True(
            player.GetNodeOrNull<PlayerCameraRig>("PlayerCameraRig") != null,
            "player composes cameras");
        True(player.HasNode("CollisionShape3D"), "player keeps collision");
        player.QueueFree();
    }

    private void TestAnimatorPoseOutputs()
    {
        var (idleAnimator, idleSkeleton) = CreateAnimator();
        var spine = idleSkeleton.FindBone("Spine_02");
        var idleSpineBaseline = idleSkeleton.GetBonePoseRotation(spine);
        idleAnimator.Advance(0.1f, 0f, true, 0f, false);
        QuaternionChanged(
            idleSpineBaseline,
            idleSkeleton.GetBonePoseRotation(spine),
            "idle animates Spine_02");
        idleAnimator.QueueFree();

        var (walkAnimator, walkSkeleton) = CreateAnimator();
        var upperLeg = walkSkeleton.FindBone("UpperLeg_L");
        var walkLegBaseline = walkSkeleton.GetBonePoseRotation(upperLeg);
        walkAnimator.Advance(0.1f, 1f, true, 0f, false);
        QuaternionChanged(
            walkLegBaseline,
            walkSkeleton.GetBonePoseRotation(upperLeg),
            "walk animates UpperLeg_L");
        walkAnimator.QueueFree();

        var (airborneAnimator, airborneSkeleton) = CreateAnimator();
        var hips = airborneSkeleton.FindBone("Hips");
        var airborneHipsBaseline = airborneSkeleton.GetBonePoseRotation(hips);
        airborneAnimator.Advance(0.1f, 0f, false, 1f, false);
        QuaternionChanged(
            airborneHipsBaseline,
            airborneSkeleton.GetBonePoseRotation(hips),
            "airborne animates Hips");
        airborneAnimator.QueueFree();

        var (smokeAnimator, smokeSkeleton) = CreateAnimator();
        var elbow = smokeSkeleton.FindBone("Elbow_R");
        var smokeElbowBaseline = smokeSkeleton.GetBonePoseRotation(elbow);
        smokeAnimator.Advance(0.2f, 0f, true, 0f, true);
        QuaternionChanged(
            smokeElbowBaseline,
            smokeSkeleton.GetBonePoseRotation(elbow),
            "smoking animates Elbow_R");
        smokeAnimator.QueueFree();

        var (layeredAnimator, layeredSkeleton) = CreateAnimator();
        var shoulder = layeredSkeleton.FindBone("Shoulder_R");
        var shoulderBaseline = layeredSkeleton.GetBonePoseRotation(shoulder);
        layeredAnimator.Advance(0.2f, 1f, true, 0f, true);
        var smokingOnlyShoulder = shoulderBaseline * Quaternion.FromEuler(
            new Vector3(-0.65f, -0.20f, -0.35f));
        QuaternionNear(
            smokingOnlyShoulder,
            layeredSkeleton.GetBonePoseRotation(shoulder),
            "full smoke overrides walking right shoulder",
            0.001f);
        layeredAnimator.QueueFree();

        var (stableAnimator, stableSkeleton) = CreateAnimator();
        var stableLeg = stableSkeleton.FindBone("UpperLeg_L");
        stableAnimator.Advance(0.1f, 0.8f, true, 0f, false);
        var firstPose = stableSkeleton.GetBonePoseRotation(stableLeg);
        stableAnimator.Advance(0f, 0.8f, true, 0f, false);
        QuaternionNear(
            firstPose,
            stableSkeleton.GetBonePoseRotation(stableLeg),
            "repeated animator advances do not accumulate drift");
        stableAnimator.QueueFree();

        var (headAnimator, headSkeleton) = CreateAnimator();
        var head = headSkeleton.FindBone("Head");
        var headBaseline = headSkeleton.GetBonePoseScale(head);
        headAnimator.SetFirstPerson(true);
        headAnimator.Advance(0.1f, 1f, true, 0f, false);
        VectorNear(
            Vector3.One * 0.001f,
            headSkeleton.GetBonePoseScale(head),
            "first-person head remains hidden across Advance");
        headAnimator.SetFirstPerson(false);
        VectorNear(
            headBaseline,
            headSkeleton.GetBonePoseScale(head),
            "third-person restores the full head scale vector",
            0f);
        headAnimator.QueueFree();
    }

    private (PlayerAnimator Animator, Skeleton3D Skeleton) CreateAnimator()
    {
        var animator = GD.Load<PackedScene>("res://Scenes/Player/PlayerAvatar.tscn")
            .Instantiate<PlayerAnimator>();
        AddChild(animator);
        return (animator, animator.GetNode<Skeleton3D>("CharacterModel/Skeleton3D"));
    }

    private void True(bool condition, string context)
    {
        _count++;
        if (!condition)
            throw new InvalidOperationException($"{context}: Expected condition to be true.");
    }

    private void Equal<T>(T expected, T actual, string context)
    {
        _count++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{context}: Expected {expected}, got {actual}.");
    }

    private void Near(float expected, float actual, string context, float tolerance = 0.0001f)
    {
        _count++;
        if (!float.IsFinite(actual))
            throw new InvalidOperationException($"{context}: Expected a finite value, got {actual}.");

        if (MathF.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{context}: Expected {expected} +/- {tolerance}, got {actual}.");
    }

    private void QuaternionChanged(
        Quaternion baseline,
        Quaternion actual,
        string context,
        float tolerance = 0.0001f)
    {
        _count++;
        var angle = baseline.AngleTo(actual);
        if (!float.IsFinite(angle) || angle <= tolerance)
        {
            throw new InvalidOperationException(
                $"{context}: Expected an angular change greater than {tolerance}, got {angle}.");
        }
    }

    private void QuaternionNear(
        Quaternion expected,
        Quaternion actual,
        string context,
        float tolerance = 0.0001f)
    {
        _count++;
        var angle = expected.AngleTo(actual);
        if (!float.IsFinite(angle) || angle > tolerance)
        {
            throw new InvalidOperationException(
                $"{context}: Expected angular difference <= {tolerance}, got {angle}.");
        }
    }

    private void VectorNear(
        Vector3 expected,
        Vector3 actual,
        string context,
        float tolerance = 0.0001f)
    {
        _count++;
        var distance = expected.DistanceTo(actual);
        if (!float.IsFinite(distance) || distance > tolerance)
        {
            throw new InvalidOperationException(
                $"{context}: Expected {expected} within {tolerance}, got {actual}.");
        }
    }
}
