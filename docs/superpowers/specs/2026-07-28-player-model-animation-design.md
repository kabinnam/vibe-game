# Player Model and Animation Design

## Summary

Replace the invisible capsule-only player presentation with the supplied Synty male humanoid and a self-contained procedural animation system. The player will support runtime switching between first- and third-person views, use the same full-body rig in both views, and layer a right-hand smoking gesture over locomotion.

This feature addresses [GitHub issue #1](https://github.com/kabinnam/vibe-game/issues/1), which calls for a main player model while leaving room to evaluate first person for interviews and third person for multiplayer. The implementation branch is `feature/1-player-model-animation`, based on `origin/develop`.

## Goals

- Show the supplied Synty male humanoid as the controlled player.
- Animate idle, walking, airborne movement, and a hand-to-mouth smoking gesture without external animation dependencies.
- Allow runtime switching between first- and third-person cameras.
- Use one full-body rig in both camera modes.
- Keep the torso, legs, arms, and held prop visible in first person while preventing the head from clipping into the camera.
- Allow the smoking gesture and locomotion to play at the same time.
- Make the character model, animation implementation, and smoking prop independently replaceable.

## Non-goals

- Smoke or vapor particles.
- Inhalation gameplay effects, inventory, consumables, or nicotine systems.
- Multiplayer synchronization or networking.
- Sprinting, crouching, climbing, or combat animation.
- Polished motion-capture animation clips.
- A final production pipe or vape model.

## Selected Approach

Use one shared full-body Synty rig with a runtime procedural bone driver.

This approach is self-contained and supports independent upper- and lower-body motion without creating a separate first-person rig. It also preserves clear replacement seams: authored clips and an `AnimationTree` can replace the procedural driver later without changing movement or camera responsibilities.

Alternatives considered:

1. Hand-authored Godot animation resources with an `AnimationTree`. This would be more editor-friendly but requires substantially more initial setup for the first pass.
2. Separate first-person arms and third-person body rigs. This can produce shooter-specific polish but doubles rig and animation maintenance.

## Assets

Copy only the required source assets from `/Users/kabinnam/Downloads/polygon-starter` into the repository:

- `Characters.fbx`
- `PolygonStarter_Texture_01.png`
- `PolygonStarter_Mat_01_mat.tres`, with its texture path adjusted to the in-repository location

Godot-generated `.godot` import artifacts will not be committed. `PlayerAvatar.tscn` will wrap the imported scene, expose only the male mesh, and provide stable node paths for the rest of the player implementation.

## Architecture

### PlayerController

`PlayerController` remains the movement authority. It owns movement input, gravity, jumping, mouse capture, and `CharacterBody3D` velocity. It does not directly manipulate skeleton bones or camera placement.

After each movement update, it supplies presentation state:

- Horizontal speed normalized against configured movement speed.
- Grounded state.
- Vertical velocity.
- Whether the smoking action is held.

The controller forwards mouse-look input to the camera rig and handles the perspective-toggle action.

### PlayerAvatar

`PlayerAvatar.tscn` is the replaceable visual layer. It contains the imported Synty rig, the procedural animator, and the right-hand prop attachment point. It is a child of the existing player body and has no collision or movement authority.

The avatar faces the same forward direction as the player body. Its scale and vertical offset are adjusted in the wrapper scene rather than in movement code.

### PlayerAnimator

`PlayerAnimator.cs` resolves required skeleton bones at startup, stores their baseline local poses, and applies additive procedural offsets each frame.

The base pose has three mutually exclusive locomotion states:

- **Idle:** subtle breathing and weight shift.
- **Walk:** a speed-scaled cycle for hips, upper and lower legs, feet, shoulders, and arms.
- **Airborne:** a stable jump or falling silhouette selected whenever the player is not grounded.

Actual horizontal velocity advances and scales the walk cycle. The system does not use root motion, so animation never changes the authoritative player transform.

The smoking layer blends independently from zero to one while the smoking action is held. It raises the right shoulder, elbow, and hand toward the mouth while leaving the locomotion layer active. Releasing the action smoothly returns the affected bones to the base locomotion pose.

Bone offsets are composed from stored baseline poses every frame so animation does not accumulate drift.

### Smoking Prop

`SmokingProp.tscn` is a small code-native placeholder resembling a vape. It is attached under a `BoneAttachment3D` targeting the right hand. Its transform is configured in the prop scene or attachment node, keeping prop alignment out of animation code.

Replacing the prop requires changing only the packed scene reference or attachment child.

### PlayerCameraRig

`PlayerCameraRig.cs` owns camera mode, pitch, and active-camera selection.

- **First person:** a camera at eye level follows the look pivot. The shared full body remains active. Head, eyes, and eyebrow bones are scaled down for the local first-person presentation so the camera does not render inside the head.
- **Third person:** a camera follows behind and above the player through `SpringArm3D`, allowing Godot to shorten the camera distance near walls.

Yaw continues to rotate the player body, matching current controller behavior. Pitch rotates only the shared look pivot and remains clamped to prevent overturning. Switching views does not reset yaw, pitch, movement, or animation state.

## Inputs

Existing controls remain unchanged:

- `WASD`: movement
- Space: jump
- Mouse: look
- Escape: release captured mouse

New named input actions:

- `toggle_camera`, bound to `V`: switch between first- and third-person views.
- `smoke`, bound to `F`: hold to raise the prop; release to lower it.

The initial mode remains first person to preserve current project behavior.

## Runtime Data Flow

1. Input updates movement, look, camera-toggle, and smoking intent.
2. `PlayerController` calculates velocity and calls `MoveAndSlide`.
3. The resulting velocity and grounded state are sent to `PlayerAnimator`.
4. `PlayerAnimator` calculates the base locomotion pose, then blends the smoking offsets over the upper body.
5. `PlayerCameraRig` updates the active camera and first-person head visibility independently of animation state.

Camera selection and smoking are orthogonal: changing perspective never interrupts the smoking gesture, and smoking never changes the active camera.

## Error Handling

- Required child nodes are exported or resolved through stable wrapper-scene paths.
- The animator validates each required bone at startup.
- A missing optional bone logs a clear Godot warning and disables only its associated pose channel.
- A missing skeleton or camera dependency logs an error and disables that presentation component without disabling player movement.
- Camera switching is ignored safely if one camera is unavailable.
- The placeholder prop remains optional; missing prop art does not prevent the arm gesture.

## Performance

- Cache node and bone indexes during `_Ready`; do not perform tree or bone-name searches every frame.
- Reuse pose state and avoid per-frame collections, delegates, or resource allocation.
- Animate only the bones required by the current procedural layers.
- Keep the existing capsule collision unchanged.

## Verification

### Automated

- C# compilation succeeds with no warnings introduced by the feature.
- Animation-state tests cover idle, walking, airborne selection, speed-scaled cycle progression, smoking blend-in, and smoking blend-out.
- Camera-state tests cover initial first-person mode and repeated perspective toggling.
- A headless Godot scene test loads the player and verifies required nodes, skeleton bones, both cameras, input actions, and the right-hand prop attachment.
- The main scene starts headlessly without script or resource errors.

### Runtime visual checks

- The male humanoid appears at the correct scale and position inside the existing capsule.
- Idle movement is subtle and stable.
- Walking visibly moves both legs and coordinates the remaining arm swing.
- Jumping and falling use an airborne pose without affecting collision or trajectory.
- Holding `F` raises the visible prop to the mouth, including while walking; releasing it lowers smoothly.
- First person shows the shared torso, arms, legs, and prop without rendering the head around the camera.
- Third person shows the complete model, follows player yaw and pitch appropriately, and avoids clipping through level geometry.
- Repeated camera switching preserves movement and animation state.

## Acceptance Criteria

The feature is complete when the supplied male model replaces the capsule-only presentation, all four procedural motion behaviors are visible, smoking layers over locomotion with a held prop, both camera modes work at runtime, first person uses the shared body without head clipping, third person handles nearby walls, and automated plus runtime verification passes without player-breaking errors.
