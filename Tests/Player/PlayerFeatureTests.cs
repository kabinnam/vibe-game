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
        avatar.QueueFree();
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
}
