# Player Model and Animation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the capsule-only player presentation with the supplied animated Synty male humanoid, a layered smoking gesture, and switchable first- and third-person cameras.

**Architecture:** Keep `PlayerController` authoritative for movement and delegate presentation to a replaceable `PlayerAvatar` and camera scene. A deterministic animation-state object drives a procedural bone animator, while a separate camera rig owns pitch and perspective selection; the controller passes post-movement state between them.

**Tech Stack:** Godot 4.7, C#/.NET 8, `CharacterBody3D`, `Skeleton3D`, `BoneAttachment3D`, `SpringArm3D`, and a dependency-free headless Godot test runner.

---

## File Map

- Create `Scenes/Player/PlayerAnimationState.cs` for deterministic state and blending.
- Create `Scenes/Player/PlayerAnimator.cs` for procedural bone poses and head hiding.
- Create `Scenes/Player/PlayerAvatar.tscn` as the replaceable visual wrapper.
- Create `Scenes/Player/PlayerCameraRig.cs` and `PlayerCameraRig.tscn` for both views.
- Create `Scenes/Player/SmokingProp.tscn` for the replaceable placeholder vape.
- Copy `Characters.fbx`, its palette, and its material under `Assets/Synty/PolygonStarter/`.
- Create `Tests/Player/PlayerFeatureTests.cs` and `.tscn` as the headless test suite.
- Modify `PlayerController.cs`, `Player.tscn`, and `project.godot` for integration.

### Task 1: Deterministic animation state and test harness

**Files:**
- Create: `Scenes/Player/PlayerAnimationState.cs`
- Create: `Tests/Player/PlayerFeatureTests.cs`
- Create: `Tests/Player/PlayerFeatureTests.tscn`

- [ ] **Step 1: Write the failing test**

Create `Tests/Player/PlayerFeatureTests.cs`:

```csharp
using System;
using Godot;

public partial class PlayerFeatureTests : Node
{
    private int _count;

    public override void _Ready()
    {
        try
        {
            TestAnimationState();
            GD.Print($"PASS PlayerFeatureTests ({_count} assertions)");
            GetTree().Quit();
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
        Equal(PlayerLocomotionMode.Idle, state.LocomotionMode, "starts idle");
        state.Advance(0.1f, 0.8f, true, false);
        Equal(PlayerLocomotionMode.Walk, state.LocomotionMode, "speed selects walk");
        True(state.CyclePhase > 0.0f, "walking advances cycle");
        state.Advance(0.1f, 0.8f, false, false);
        Equal(PlayerLocomotionMode.Airborne, state.LocomotionMode, "airborne overrides walk");
        state.Advance(0.1f, 0.0f, true, true);
        Near(0.5f, state.SmokeBlend, 0.001f, "smoke blends in");
        state.Advance(0.1f, 0.0f, true, false);
        Near(0.0f, state.SmokeBlend, 0.001f, "smoke blends out");
    }

    private void True(bool value, string message)
    {
        _count++;
        if (!value) throw new InvalidOperationException(message);
    }

    private void Equal<T>(T expected, T actual, string message) where T : notnull
    {
        _count++;
        if (!expected.Equals(actual))
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private void Near(float expected, float actual, float tolerance, string message)
    {
        _count++;
        if (Mathf.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}
```

Create `Tests/Player/PlayerFeatureTests.tscn`:

```ini
[gd_scene load_steps=2 format=3]
[ext_resource type="Script" path="res://Tests/Player/PlayerFeatureTests.cs" id="1"]
[node name="PlayerFeatureTests" type="Node"]
script = ExtResource("1")
```

- [ ] **Step 2: Verify the test fails**

Run `dotnet build VibeGame.csproj`.

Expected: compilation fails because `PlayerAnimationState` and `PlayerLocomotionMode` are undefined.

- [ ] **Step 3: Implement the minimal state model**

Create `Scenes/Player/PlayerAnimationState.cs`:

```csharp
using Godot;

public enum PlayerLocomotionMode { Idle, Walk, Airborne }

public sealed class PlayerAnimationState
{
    public PlayerLocomotionMode LocomotionMode { get; private set; } = PlayerLocomotionMode.Idle;
    public float CyclePhase { get; private set; }
    public float NormalizedSpeed { get; private set; }
    public float SmokeBlend { get; private set; }

    public void Advance(float delta, float normalizedSpeed, bool grounded, bool smoking)
    {
        NormalizedSpeed = Mathf.Clamp(normalizedSpeed, 0.0f, 1.0f);
        LocomotionMode = !grounded ? PlayerLocomotionMode.Airborne
            : NormalizedSpeed > 0.05f ? PlayerLocomotionMode.Walk : PlayerLocomotionMode.Idle;
        var cyclesPerSecond = LocomotionMode == PlayerLocomotionMode.Walk
            ? Mathf.Lerp(0.2f, 1.4f, NormalizedSpeed) : 0.2f;
        CyclePhase = Mathf.PosMod(CyclePhase + delta * cyclesPerSecond * Mathf.Tau, Mathf.Tau);
        SmokeBlend = Mathf.MoveToward(SmokeBlend, smoking ? 1.0f : 0.0f, 5.0f * delta);
    }
}
```

- [ ] **Step 4: Verify the test passes**

Run:

```bash
dotnet build VibeGame.csproj
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --editor --path . --quit
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --scene res://Tests/Player/PlayerFeatureTests.tscn
```

Expected: `PASS PlayerFeatureTests (6 assertions)`.

- [ ] **Step 5: Commit**

```bash
git add Scenes/Player/PlayerAnimationState.cs Tests/Player
git commit -m "test: add player presentation state coverage"
```

### Task 2: Import and wrap the supplied humanoid

**Files:**
- Create: `Assets/Synty/PolygonStarter/Models/Characters.fbx`
- Create: `Assets/Synty/PolygonStarter/Textures/PolygonStarter_Texture_01.png`
- Create: `Assets/Synty/PolygonStarter/Materials/PolygonStarter_Mat_01_mat.tres`
- Create: `Scenes/Player/SmokingProp.tscn`
- Create: `Scenes/Player/PlayerAvatar.tscn`
- Modify: `Tests/Player/PlayerFeatureTests.cs`

- [ ] **Step 1: Add a failing avatar scene test**

Call `TestAvatarScene();` after `TestAnimationState();`, then add:

```csharp
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
        True(avatar.GetNode<MeshInstance3D>("CharacterModel/Skeleton3D/SM_Character_Male_01").Visible, "male is visible");
        True(!avatar.GetNode<MeshInstance3D>("CharacterModel/Skeleton3D/SM_Character_Female_01").Visible, "female is hidden");
        var attachment = avatar.GetNode<BoneAttachment3D>("CharacterModel/Skeleton3D/RightHandAttachment");
        Equal(new StringName("Hand_R"), attachment.BoneName, "prop targets right hand");
        True(attachment.HasNode("SmokingProp"), "prop is attached");
        avatar.QueueFree();
    }
```

- [ ] **Step 2: Verify failure**

```bash
dotnet build VibeGame.csproj
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --scene res://Tests/Player/PlayerFeatureTests.tscn
```

Expected: exit 1 because `PlayerAvatar.tscn` does not exist.

- [ ] **Step 3: Copy the supplied source assets**

```bash
mkdir -p Assets/Synty/PolygonStarter/Models Assets/Synty/PolygonStarter/Textures Assets/Synty/PolygonStarter/Materials
cp /Users/kabinnam/Downloads/polygon-starter/Assets/Synty/PolygonStarter/Models/Characters.fbx Assets/Synty/PolygonStarter/Models/Characters.fbx
cp /Users/kabinnam/Downloads/polygon-starter/Assets/Synty/PolygonStarter/Textures/PolygonStarter_Texture_01.png Assets/Synty/PolygonStarter/Textures/PolygonStarter_Texture_01.png
```

Create `Assets/Synty/PolygonStarter/Materials/PolygonStarter_Mat_01_mat.tres`:

```ini
[gd_resource type="StandardMaterial3D" load_steps=2 format=3]
[ext_resource type="Texture2D" path="res://Assets/Synty/PolygonStarter/Textures/PolygonStarter_Texture_01.png" id="1"]
[resource]
albedo_texture = ExtResource("1")
roughness = 0.8
```

- [ ] **Step 4: Create the prop and avatar scenes**

Create `Scenes/Player/SmokingProp.tscn`:

```ini
[gd_scene load_steps=4 format=3]
[sub_resource type="StandardMaterial3D" id="Mat"]
albedo_color = Color(0.08, 0.1, 0.12, 1)
metallic = 0.65
roughness = 0.28
[sub_resource type="CylinderMesh" id="Body"]
material = SubResource("Mat")
top_radius = 0.018
bottom_radius = 0.022
height = 0.16
radial_segments = 10
[sub_resource type="CylinderMesh" id="Tip"]
top_radius = 0.011
bottom_radius = 0.018
height = 0.04
radial_segments = 10
[node name="SmokingProp" type="Node3D"]
[node name="Body" type="MeshInstance3D" parent="."]
mesh = SubResource("Body")
[node name="Mouthpiece" type="MeshInstance3D" parent="."]
position = Vector3(0, 0.1, 0)
mesh = SubResource("Tip")
```

Create `Scenes/Player/PlayerAvatar.tscn`:

```ini
[gd_scene load_steps=4 format=3]
[ext_resource type="PackedScene" path="res://Assets/Synty/PolygonStarter/Models/Characters.fbx" id="1"]
[ext_resource type="Material" path="res://Assets/Synty/PolygonStarter/Materials/PolygonStarter_Mat_01_mat.tres" id="2"]
[ext_resource type="PackedScene" path="res://Scenes/Player/SmokingProp.tscn" id="3"]
[node name="PlayerAvatar" type="Node3D"]
[node name="CharacterModel" parent="." instance=ExtResource("1")]
[node name="SM_Character_Male_01" parent="CharacterModel/Skeleton3D" index="0"]
material_override = ExtResource("2")
[node name="SM_Character_Female_01" parent="CharacterModel/Skeleton3D" index="1"]
visible = false
[node name="RightHandAttachment" type="BoneAttachment3D" parent="CharacterModel/Skeleton3D"]
bone_name = &"Hand_R"
[node name="SmokingProp" parent="CharacterModel/Skeleton3D/RightHandAttachment" instance=ExtResource("3")]
position = Vector3(0.02, 0.07, 0)
rotation = Vector3(0, 0, 1.5707964)
[editable path="CharacterModel"]
```

- [ ] **Step 5: Import and verify**

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --editor --path . --quit
dotnet build VibeGame.csproj
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --scene res://Tests/Player/PlayerFeatureTests.tscn
```

Expected: all 14 assertions pass.

- [ ] **Step 6: Commit**

```bash
git add Assets/Synty Scenes/Player/SmokingProp.tscn Scenes/Player/PlayerAvatar.tscn Tests/Player/PlayerFeatureTests.cs
git commit -m "feat: add Synty player avatar"
```

### Task 3: Procedural skeleton animation and first-person head hiding

**Files:**
- Create: `Scenes/Player/PlayerAnimator.cs`
- Modify: `Scenes/Player/PlayerAvatar.tscn`
- Modify: `Tests/Player/PlayerFeatureTests.cs`

- [ ] **Step 1: Add failing animator tests**

Call `TestAnimator();` after `TestAvatarScene();`, then add:

```csharp
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
        Near(visible, skeleton.GetBonePoseScale(head).Length(), 0.001f, "third person restores head");
        avatar.QueueFree();
    }
```

- [ ] **Step 2: Verify failure**

Run `dotnet build VibeGame.csproj`. Expected: `PlayerAnimator` is undefined.

- [ ] **Step 3: Implement the bone driver**

Create `Scenes/Player/PlayerAnimator.cs`:

```csharp
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

    public void Advance(float delta, float speed, bool grounded, float verticalVelocity, bool smoking)
    {
        if (!IsRigReady) return;
        State.Advance(delta, speed, grounded, smoking);
        ResetPose();
        if (State.LocomotionMode == PlayerLocomotionMode.Idle) ApplyIdle();
        else if (State.LocomotionMode == PlayerLocomotionMode.Walk) ApplyWalk();
        else ApplyAirborne(verticalVelocity);
        ApplySmoking(State.SmokeBlend);
    }

    public void SetFirstPerson(bool firstPerson)
    {
        if (!IsRigReady) return;
        foreach (var name in HeadBones)
            if (_bones.TryGetValue(name, out var index))
                _skeleton.SetBonePoseScale(index, firstPerson ? Vector3.One * 0.001f : _scales[index]);
    }

    private void Cache(IEnumerable<StringName> names)
    {
        foreach (var name in names)
        {
            var index = _skeleton.FindBone(name);
            if (index < 0) GD.PushWarning($"PlayerAnimator: optional bone '{name}' is unavailable.");
            else _bones[name] = index;
        }
    }

    private void ResetPose()
    {
        foreach (var name in Animated)
            if (_bones.TryGetValue(name, out var index))
            {
                _skeleton.SetBonePoseRotation(index, _rotations[index]);
                _skeleton.SetBonePosePosition(index, _positions[index]);
            }
    }

    private void ApplyIdle()
    {
        var breath = Mathf.Sin(State.CyclePhase) * 0.035f;
        Rotate("Spine_02", new Vector3(breath, 0, 0));
        Rotate("Spine_03", new Vector3(breath * 0.6f, 0, 0));
        Rotate("Shoulder_L", new Vector3(0, 0, breath * 0.35f));
        Rotate("Shoulder_R", new Vector3(0, 0, -breath * 0.35f));
    }

    private void ApplyWalk()
    {
        var amplitude = Mathf.Lerp(0.15f, 0.72f, State.NormalizedSpeed);
        var left = Mathf.Sin(State.CyclePhase) * amplitude;
        var right = -left;
        Rotate("Hips", new Vector3(0, Mathf.Sin(State.CyclePhase * 2) * 0.06f, 0));
        Rotate("UpperLeg_L", new Vector3(left, 0, 0));
        Rotate("LowerLeg_L", new Vector3(Mathf.Max(0, -Mathf.Sin(State.CyclePhase)) * amplitude * 0.8f, 0, 0));
        Rotate("Ankle_L", new Vector3(-left * 0.35f, 0, 0));
        Rotate("UpperLeg_R", new Vector3(right, 0, 0));
        Rotate("LowerLeg_R", new Vector3(Mathf.Max(0, Mathf.Sin(State.CyclePhase)) * amplitude * 0.8f, 0, 0));
        Rotate("Ankle_R", new Vector3(-right * 0.35f, 0, 0));
        Rotate("Shoulder_L", new Vector3(right * 0.65f, 0, 0));
        Rotate("Shoulder_R", new Vector3(left * 0.65f, 0, 0));
    }

    private void ApplyAirborne(float verticalVelocity)
    {
        Rotate("Hips", new Vector3(verticalVelocity > 0 ? -0.10f : 0.12f, 0, 0));
        Rotate("UpperLeg_L", new Vector3(0.30f, 0, -0.08f));
        Rotate("LowerLeg_L", new Vector3(0.42f, 0, 0));
        Rotate("UpperLeg_R", new Vector3(-0.12f, 0, 0.08f));
        Rotate("LowerLeg_R", new Vector3(0.20f, 0, 0));
        Rotate("Shoulder_L", new Vector3(-0.18f, 0, 0.12f));
        Rotate("Shoulder_R", new Vector3(-0.18f, 0, -0.12f));
    }

    private void ApplySmoking(float blend)
    {
        Rotate("Shoulder_R", new Vector3(-0.65f, -0.20f, -0.35f) * blend);
        Rotate("Elbow_R", new Vector3(-1.45f, 0.10f, 0.12f) * blend);
        Rotate("Hand_R", new Vector3(0.12f, 0.30f, -0.18f) * blend);
    }

    private void Rotate(StringName name, Vector3 offset)
    {
        if (!_bones.TryGetValue(name, out var index)) return;
        _skeleton.SetBonePoseRotation(index,
            _skeleton.GetBonePoseRotation(index) * Quaternion.FromEuler(offset));
    }
}
```

Add the script ext-resource to `PlayerAvatar.tscn`, increase `load_steps` to 5, and set the root:

```ini
[ext_resource type="Script" path="res://Scenes/Player/PlayerAnimator.cs" id="4"]
[node name="PlayerAvatar" type="Node3D"]
script = ExtResource("4")
```

- [ ] **Step 4: Verify and commit**

```bash
dotnet build VibeGame.csproj
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --editor --path . --quit
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --scene res://Tests/Player/PlayerFeatureTests.tscn
```

Expected: 19 assertions pass.

```bash
git add Scenes/Player/PlayerAnimator.cs Scenes/Player/PlayerAvatar.tscn Tests/Player/PlayerFeatureTests.cs
git commit -m "feat: animate player rig procedurally"
```

### Task 4: Switchable camera rig

**Files:**
- Create: `Scenes/Player/PlayerCameraRig.cs`
- Create: `Scenes/Player/PlayerCameraRig.tscn`
- Modify: `Tests/Player/PlayerFeatureTests.cs`

- [ ] **Step 1: Add the failing camera test**

Call `TestCameraRig();` after `TestAnimator();`, then add:

```csharp
    private void TestCameraRig()
    {
        var rig = GD.Load<PackedScene>("res://Scenes/Player/PlayerCameraRig.tscn").Instantiate<PlayerCameraRig>();
        AddChild(rig);
        True(rig.IsFirstPerson, "camera starts first person");
        True(rig.GetNode<Camera3D>("FirstPersonCamera").Current, "first camera is current");
        rig.ToggleMode();
        True(!rig.IsFirstPerson, "camera toggles third person");
        True(rig.GetNode<Camera3D>("ThirdPersonSpringArm/ThirdPersonCamera").Current, "third camera is current");
        rig.AddPitch(10.0f);
        Near(Mathf.DegToRad(85), rig.Pitch, 0.001f, "pitch clamps high");
        rig.AddPitch(-20.0f);
        Near(Mathf.DegToRad(-85), rig.Pitch, 0.001f, "pitch clamps low");
        rig.QueueFree();
    }
```

- [ ] **Step 2: Verify failure**

Run the build. Expected: `PlayerCameraRig` is undefined.

- [ ] **Step 3: Implement camera state and scene**

Create `Scenes/Player/PlayerCameraRig.cs`:

```csharp
using Godot;

public partial class PlayerCameraRig : Node3D
{
    private Camera3D? _first;
    private Camera3D? _third;
    public bool IsFirstPerson { get; private set; } = true;
    public float Pitch => Rotation.X;

    public override void _Ready()
    {
        _first = GetNodeOrNull<Camera3D>("FirstPersonCamera");
        _third = GetNodeOrNull<Camera3D>("ThirdPersonSpringArm/ThirdPersonCamera");
        if (_first == null || _third == null)
        {
            GD.PushError("PlayerCameraRig requires both cameras.");
            return;
        }
        ApplyMode();
    }

    public void AddPitch(float radians)
    {
        var rotation = Rotation;
        rotation.X = Mathf.Clamp(rotation.X + radians, Mathf.DegToRad(-85), Mathf.DegToRad(85));
        Rotation = rotation;
    }

    public void ToggleMode()
    {
        if (_first == null || _third == null) return;
        IsFirstPerson = !IsFirstPerson;
        ApplyMode();
    }

    private void ApplyMode()
    {
        _first!.Current = IsFirstPerson;
        _third!.Current = !IsFirstPerson;
    }
}
```

Create `Scenes/Player/PlayerCameraRig.tscn`:

```ini
[gd_scene load_steps=2 format=3]
[ext_resource type="Script" path="res://Scenes/Player/PlayerCameraRig.cs" id="1"]
[node name="PlayerCameraRig" type="Node3D"]
position = Vector3(0, 1.6, 0)
script = ExtResource("1")
[node name="FirstPersonCamera" type="Camera3D" parent="."]
current = true
near = 0.03
[node name="ThirdPersonSpringArm" type="SpringArm3D" parent="."]
spring_length = 3.2
margin = 0.15
collision_mask = 1
[node name="ThirdPersonCamera" type="Camera3D" parent="ThirdPersonSpringArm"]
near = 0.08
```

- [ ] **Step 4: Verify and commit**

```bash
dotnet build VibeGame.csproj
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --editor --path . --quit
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --scene res://Tests/Player/PlayerFeatureTests.tscn
```

Expected: 25 assertions pass.

```bash
git add Scenes/Player/PlayerCameraRig.cs Scenes/Player/PlayerCameraRig.tscn Tests/Player/PlayerFeatureTests.cs
git commit -m "feat: add switchable player cameras"
```

### Task 5: Integrate inputs, movement, cameras, and avatar

**Files:**
- Modify: `project.godot`
- Modify: `Scenes/Player/PlayerController.cs`
- Modify: `Scenes/Player/Player.tscn`
- Modify: `Tests/Player/PlayerFeatureTests.cs`

- [ ] **Step 1: Add the failing integration test**

Call `TestPlayerIntegration();` after `TestCameraRig();`, then add:

```csharp
    private void TestPlayerIntegration()
    {
        True(InputMap.HasAction("toggle_camera"), "toggle camera input exists");
        True(InputMap.HasAction("smoke"), "smoke input exists");
        var player = GD.Load<PackedScene>("res://Scenes/Player/Player.tscn").Instantiate<CharacterBody3D>();
        AddChild(player);
        True(player.GetNodeOrNull<PlayerAnimator>("PlayerAvatar") != null, "player composes avatar");
        True(player.GetNodeOrNull<PlayerCameraRig>("PlayerCameraRig") != null, "player composes cameras");
        True(player.HasNode("CollisionShape3D"), "player keeps collision");
        player.QueueFree();
    }
```

- [ ] **Step 2: Verify failure**

```bash
dotnet build VibeGame.csproj
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --scene res://Tests/Player/PlayerFeatureTests.tscn
```

Expected: missing input actions or composed nodes produce exit 1.

- [ ] **Step 3: Add input actions**

Append within `[input]` in `project.godot`:

```ini
toggle_camera={
"deadzone": 0.5,
"events": [Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":-1,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,"keycode":0,"physical_keycode":86,"key_label":0,"unicode":118,"location":0,"echo":false,"script":null)
]
}
smoke={
"deadzone": 0.5,
"events": [Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":-1,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,"keycode":0,"physical_keycode":70,"key_label":0,"unicode":102,"location":0,"echo":false,"script":null)
]
}
```

- [ ] **Step 4: Refactor the controller**

Replace `Scenes/Player/PlayerController.cs`:

```csharp
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
        if (_animator == null) GD.PushError("PlayerAvatar is missing; movement remains enabled.");
        if (_cameraRig == null) GD.PushError("PlayerCameraRig is missing; movement remains enabled.");
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
        if (!IsOnFloor()) velocity.Y -= Gravity * (float)delta;
        if (Input.IsActionJustPressed("jump") && IsOnFloor()) velocity.Y = JumpVelocity;

        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        var direction = (Transform.Basis * new Vector3(input.X, 0, input.Y)).Normalized();
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
        _animator?.Advance((float)delta, Speed > 0 ? horizontalSpeed / Speed : 0,
            IsOnFloor(), Velocity.Y, Input.IsActionPressed("smoke"));
    }
}
```

- [ ] **Step 5: Compose the player scene**

Replace `Scenes/Player/Player.tscn`:

```ini
[gd_scene load_steps=5 format=3 uid="uid://bplayer001"]
[ext_resource type="Script" path="res://Scenes/Player/PlayerController.cs" id="1"]
[ext_resource type="PackedScene" path="res://Scenes/Player/PlayerAvatar.tscn" id="2"]
[ext_resource type="PackedScene" path="res://Scenes/Player/PlayerCameraRig.tscn" id="3"]
[sub_resource type="CapsuleShape3D" id="Capsule"]
radius = 0.35
height = 1.8
[node name="Player" type="CharacterBody3D"]
script = ExtResource("1")
[node name="CollisionShape3D" type="CollisionShape3D" parent="."]
position = Vector3(0, 0.9, 0)
shape = SubResource("Capsule")
[node name="PlayerAvatar" parent="." instance=ExtResource("2")]
[node name="PlayerCameraRig" parent="." instance=ExtResource("3")]
```

- [ ] **Step 6: Verify and commit**

Run:

```bash
dotnet build VibeGame.csproj
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --editor --path . --quit
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --scene res://Tests/Player/PlayerFeatureTests.tscn
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --quit-after 5
```

Expected: 30 assertions pass and the main scene has no script or resource errors.

```bash
git add project.godot Scenes/Player/PlayerController.cs Scenes/Player/Player.tscn Tests/Player/PlayerFeatureTests.cs
git commit -m "feat: integrate animated player controls"
```

### Task 6: Runtime visual verification and bounded tuning

**Files:**
- Modify if evidence requires it: `Scenes/Player/PlayerAvatar.tscn`
- Modify if evidence requires it: `Scenes/Player/PlayerAnimator.cs`
- Modify if evidence requires it: `Scenes/Player/PlayerCameraRig.tscn`
- Modify if evidence requires it: `Scenes/Player/SmokingProp.tscn`
- Modify for any discovered regression: `Tests/Player/PlayerFeatureTests.cs`

- [ ] **Step 1: Launch the game**

Run `/Applications/Godot_mono.app/Contents/MacOS/Godot --path .`.

Expected: the existing greybox opens with first-person view and captured mouse.

- [ ] **Step 2: Complete the visual checklist**

```text
[ ] First person starts at eye height without head, eye, or eyebrow clipping.
[ ] Looking down shows the shared torso and legs.
[ ] Idle breathing remains subtle and does not drift.
[ ] WASD cycles both legs and swings the free arm.
[ ] Space shows a readable rising/falling pose without changing trajectory.
[ ] Holding F raises the visible prop to the mouth.
[ ] Holding F while walking keeps the leg cycle active.
[ ] Releasing F lowers the arm smoothly.
[ ] V shows the complete third-person body without resetting state.
[ ] The spring arm prevents the third-person camera entering walls.
[ ] V returns to first person and hides the head again.
[ ] Escape releases the mouse.
```

- [ ] **Step 3: Tune only observed problems**

Use these bounds:

```text
Vertical placement: adjust avatar position.y by at most 0.15 per iteration.
Backward facing: set avatar rotation.y to 3.1415927.
Wrong limb bend: invert only the incorrect local Euler component.
Prop alignment: adjust only the prop transform below RightHandAttachment.
Mouth alignment: tune Shoulder_R, Elbow_R, then Hand_R in that order.
First-person framing: move the camera by at most 0.08 per axis.
Third-person framing: keep spring_length within 2.6–4.0 and height within 1.35–1.8.
```

After each adjustment, rerun the build and headless tests before continuing.

- [ ] **Step 4: Run final verification**

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --editor --path . --quit
dotnet build VibeGame.csproj
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --scene res://Tests/Player/PlayerFeatureTests.tscn
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --quit-after 5
git diff --check
git status --short
```

Expected: all commands exit 0, tests print `PASS PlayerFeatureTests`, and status lists only intentional feature changes.

- [ ] **Step 5: Commit evidence-driven tuning if files changed**

```bash
git add Scenes/Player/PlayerAvatar.tscn Scenes/Player/PlayerAnimator.cs Scenes/Player/PlayerCameraRig.tscn Scenes/Player/SmokingProp.tscn Tests/Player/PlayerFeatureTests.cs
git commit -m "fix: tune player animation and cameras"
```

Skip this commit if no tuning was necessary.

- [ ] **Step 6: Stop before publishing**

Report verification evidence, the branch name, local commits, and any visual limitations. Do not push or open a pull request. Ask the user whether to publish the branch and open a PR whose body includes `Closes #1`.
