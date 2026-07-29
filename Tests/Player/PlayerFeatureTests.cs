using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

public partial class PlayerFeatureTests : Node
{
    private int _count;

    public override async void _Ready()
    {
        try
        {
            TestAnimationState();
            TestAvatarScene();
            TestAnimator();
            TestCameraRig();
            await TestPlayerIntegration();
            await TestPlayerSpringArmAvoidsSelfCollision();
            await TestPlayerSpringArmShortensForObstacle();
            TestFirstPersonSmokingClearance();
            TestAnimatorDoesNotDrivePlayer();
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

        var releasingSmoke = new PlayerAnimationState();
        releasingSmoke.Advance(0.2f, 0f, true, true);
        Near(1f, releasingSmoke.SmokeBlend, "held smoke reaches full blend");
        releasingSmoke.Advance(0.1f, 0f, true, false);
        True(
            releasingSmoke.SmokeBlend > 0f && releasingSmoke.SmokeBlend < 1f,
            "released smoke passes through an intermediate blend");
        releasingSmoke.Advance(0.1f, 0f, true, false);
        Near(0f, releasingSmoke.SmokeBlend, "released smoke reaches zero after blending out");

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
        Near(
            Mathf.Pi,
            Mathf.Abs(avatar.Rotation.Y),
            "avatar faces controller-forward negative Z",
            0.001f);
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

    private async Task TestPlayerIntegration()
    {
        True(InputMap.HasAction("toggle_camera"), "toggle camera input exists");
        True(InputMap.HasAction("smoke"), "smoke input exists");
        True(
            ActionHasPhysicalKey("toggle_camera", Key.V),
            "toggle camera input uses physical V");
        True(ActionHasPhysicalKey("smoke", Key.F), "smoke input uses physical F");

        var player = GD.Load<PackedScene>("res://Scenes/Player/Player.tscn")
            .Instantiate<PlayerController>();
        var animator = player.GetNode<PlayerAnimator>("PlayerAvatar");
        var cameraRig = player.GetNode<PlayerCameraRig>("PlayerCameraRig");
        var skeleton = animator.GetNode<Skeleton3D>("CharacterModel/Skeleton3D");
        var head = skeleton.FindBone("Head");
        var visibleHeadScale = skeleton.GetBonePoseScale(head);
        AddChild(player);

        True(
            player.GetNodeOrNull<PlayerAnimator>("PlayerAvatar") != null,
            "player composes avatar");
        True(
            player.GetNodeOrNull<PlayerCameraRig>("PlayerCameraRig") != null,
            "player composes cameras");
        True(player.HasNode("CollisionShape3D"), "player keeps collision");

        var toggleCamera = new InputEventAction
        {
            Action = "toggle_camera",
            Pressed = true
        };
        player._UnhandledInput(toggleCamera);
        True(
            cameraRig.GetNode<Camera3D>("ThirdPersonSpringArm/ThirdPersonCamera").Current,
            "toggle input activates third-person camera");
        VectorNear(
            visibleHeadScale,
            skeleton.GetBonePoseScale(head),
            "toggle input restores exact third-person head scale",
            0f);

        player._UnhandledInput(toggleCamera);
        True(
            cameraRig.GetNode<Camera3D>("FirstPersonCamera").Current,
            "second toggle input activates first-person camera");
        VectorNear(
            Vector3.One * 0.001f,
            skeleton.GetBonePoseScale(head),
            "second toggle input hides the head again",
            0f);

        var playerRotationBeforeMouse = player.Rotation;
        var cameraRotationBeforeMouse = cameraRig.Rotation;
        var mouseMotion = new InputEventMouseMotion
        {
            Relative = new Vector2(12f, -8f)
        };
        player._UnhandledInput(mouseMotion);
        Near(
            playerRotationBeforeMouse.Y - (12f * player.MouseSensitivity),
            player.Rotation.Y,
            "positive mouse X yaws the player negatively");
        Near(
            playerRotationBeforeMouse.X,
            player.Rotation.X,
            "mouse pitch does not rotate the player root");
        Near(
            cameraRotationBeforeMouse.X + (8f * player.MouseSensitivity),
            cameraRig.Pitch,
            "negative mouse Y pitches the camera rig positively");
        Near(
            cameraRotationBeforeMouse.Y,
            cameraRig.Rotation.Y,
            "mouse yaw does not rotate the camera rig");

        var smokeBlendBefore = animator.State.SmokeBlend;
        Input.ActionPress("smoke");
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            True(
                animator.State.SmokeBlend > smokeBlendBefore,
                "controller forwards held smoke input to animator");
        }
        finally
        {
            Input.ActionRelease("smoke");
        }

        player.QueueFree();
    }

    private async Task TestPlayerSpringArmAvoidsSelfCollision()
    {
        var player = GD.Load<PackedScene>("res://Scenes/Player/Player.tscn")
            .Instantiate<CharacterBody3D>();
        player.Position = new Vector3(100f, 0f, 0f);
        AddChild(player);
        player.GetNode<PlayerCameraRig>("PlayerCameraRig").ToggleMode();
        var springArm = player.GetNode<SpringArm3D>(
            "PlayerCameraRig/ThirdPersonSpringArm");
        springArm.Position = new Vector3(0f, -0.7f, -0.5f);

        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        Near(
            springArm.SpringLength,
            springArm.GetHitLength(),
            "integrated spring arm ignores the player capsule",
            0.001f);
        player.QueueFree();
    }

    private async Task TestPlayerSpringArmShortensForObstacle()
    {
        var obstacle = new StaticBody3D
        {
            Position = new Vector3(200f, 1.6f, 1.6f)
        };
        var obstacleShape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(2f, 2f, 0.2f) }
        };
        obstacle.AddChild(obstacleShape);
        AddChild(obstacle);

        var player = GD.Load<PackedScene>("res://Scenes/Player/Player.tscn")
            .Instantiate<CharacterBody3D>();
        player.Position = new Vector3(200f, 0f, 0f);
        AddChild(player);
        player.GetNode<PlayerCameraRig>("PlayerCameraRig").ToggleMode();
        var springArm = player.GetNode<SpringArm3D>(
            "PlayerCameraRig/ThirdPersonSpringArm");

        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        var hitLength = springArm.GetHitLength();
        True(hitLength > 0f, $"obstacle leaves a positive camera distance (got {hitLength})");
        True(
            hitLength < springArm.SpringLength,
            $"obstacle shortens camera below {springArm.SpringLength} (got {hitLength})");
        player.QueueFree();
        obstacle.QueueFree();
    }

    private void TestFirstPersonSmokingClearance()
    {
        var player = GD.Load<PackedScene>("res://Scenes/Player/Player.tscn")
            .Instantiate<PlayerController>();
        AddChild(player);
        var animator = player.GetNode<PlayerAnimator>("PlayerAvatar");
        animator.Advance(0.2f, 0f, true, 0f, true);
        var skeleton = animator.GetNode<Skeleton3D>("CharacterModel/Skeleton3D");
        var handPose = skeleton.GetBoneGlobalPose(skeleton.FindBone("Hand_R"));
        var handPosition = skeleton.GlobalTransform * handPose.Origin;
        var headPose = skeleton.GetBoneGlobalPose(skeleton.FindBone("Head"));
        var headPosition = skeleton.GlobalTransform * headPose.Origin;
        var cameraPosition = player.GetNode<Camera3D>(
            "PlayerCameraRig/FirstPersonCamera").GlobalPosition;
        True(
            cameraPosition.Y - headPosition.Y >= 0.08f,
            "first-person camera sits above the hidden head for downward visibility");
        var clearance = cameraPosition.DistanceTo(handPosition);
        True(
            clearance >= 0.38f,
            $"first-person smoking hand clears the camera by 0.38 (got {clearance})");
        player.QueueFree();
    }

    private void TestAnimatorDoesNotDrivePlayer()
    {
        var player = GD.Load<PackedScene>("res://Scenes/Player/Player.tscn")
            .Instantiate<PlayerController>();
        player.Position = new Vector3(50f, 2f, -3f);
        player.Velocity = new Vector3(1f, 2f, 3f);
        AddChild(player);
        var positionBefore = player.Position;
        var velocityBefore = player.Velocity;

        player.GetNode<PlayerAnimator>("PlayerAvatar")
            .Advance(0.1f, 0f, false, player.Velocity.Y, false);

        VectorNear(
            positionBefore,
            player.Position,
            "airborne animation does not change player position",
            0f);
        VectorNear(
            velocityBefore,
            player.Velocity,
            "airborne animation does not change controller velocity",
            0f);
        player.QueueFree();
    }

    private void TestAnimatorPoseOutputs()
    {
        var (restAnimator, restSkeleton) = CreateAnimator();
        restAnimator.Advance(0.1f, 0f, true, 0f, false);
        BoneBelow(
            restSkeleton,
            "Shoulder_L",
            "Hand_L",
            0.25f,
            "idle left hand rests below its shoulder");
        BoneBelow(
            restSkeleton,
            "Shoulder_R",
            "Hand_R",
            0.25f,
            "idle right hand rests below its shoulder");
        restAnimator.QueueFree();

        var (mouthAnimator, mouthSkeleton) = CreateAnimator();
        mouthAnimator.Advance(0.2f, 0f, true, 0f, true);
        BonesNear(
            mouthSkeleton,
            "Head",
            "Hand_R",
            0.35f,
            "smoking raises the right hand to the head");
        mouthAnimator.QueueFree();

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
        var smokingOnlyShoulder = shoulderBaseline
            * Quaternion.FromEuler(new Vector3(0f, 0f, -1.15f))
            * Quaternion.FromEuler(new Vector3(-0.65f, -0.44f, 1.60f));
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

    private static bool ActionHasPhysicalKey(StringName action, Key physicalKey)
    {
        foreach (var @event in InputMap.ActionGetEvents(action))
        {
            if (@event is InputEventKey keyEvent && keyEvent.PhysicalKeycode == physicalKey)
                return true;
        }

        return false;
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

    private void BoneBelow(
        Skeleton3D skeleton,
        StringName upperBone,
        StringName lowerBone,
        float minimumDrop,
        string context)
    {
        _count++;
        var upper = skeleton.GetBoneGlobalPose(skeleton.FindBone(upperBone)).Origin;
        var lower = skeleton.GetBoneGlobalPose(skeleton.FindBone(lowerBone)).Origin;
        var drop = upper.Y - lower.Y;
        if (!float.IsFinite(drop) || drop < minimumDrop)
        {
            throw new InvalidOperationException(
                $"{context}: Expected a drop of at least {minimumDrop}, got {drop}.");
        }
    }

    private void BonesNear(
        Skeleton3D skeleton,
        StringName firstBone,
        StringName secondBone,
        float maximumDistance,
        string context)
    {
        _count++;
        var first = skeleton.GetBoneGlobalPose(skeleton.FindBone(firstBone)).Origin;
        var second = skeleton.GetBoneGlobalPose(skeleton.FindBone(secondBone)).Origin;
        var distance = first.DistanceTo(second);
        if (!float.IsFinite(distance) || distance > maximumDistance)
        {
            throw new InvalidOperationException(
                $"{context}: Expected distance <= {maximumDistance}, got {distance}.");
        }
    }
}
