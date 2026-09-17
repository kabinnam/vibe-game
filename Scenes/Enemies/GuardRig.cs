using Godot;

namespace VibeGame.Enemies;

// The guard model's public API. Sits on the root of GuardModel.tscn and owns everything that is
// specific to THIS skeleton and animation tree: the AnimationTree, the head look-at modifier, and
// the head-gaze probe. The guard's AI talks only to these methods, so it never needs bone names,
// node paths inside the model, or AnimationTree parameter strings. Swap the model and only this
// file (and the scene) change.
[GlobalClass]
public partial class GuardRig : Node3D
{
    [Export] public float HeadTrackRate { get; set; } = 10f;   // how fast the head look eases in/out

    // Names authored in the AnimationTree (GuardModel.tscn). Declared once, here, so the rest of the
    // project never sees them. StringName is Godot's interned string type; pre-building these avoids
    // a string -> StringName conversion on every call.
    private static readonly StringName MoveState = "Move";                          // locomotion blend space
    private static readonly StringName ScanState = "Scan";                          // standing look-around clip
    private static readonly StringName MoveBlendParam = "parameters/Move/blend_position";
    private static readonly StringName PlaybackParam = "parameters/playback";

    private AnimationTree _animTree;                         // state machine ("Move" / "Scan") + locomotion blend
    private AnimationNodeStateMachinePlayback _animState;    // handle used to switch state-machine states
    private Node3D _headGaze;                                // rides the head bone; its -Z is the animated look direction
    private LookAtModifier3D _lookMod;                       // WRITES the head bone to look at _lookTarget
    private Node3D _lookTarget;                              // world-space point the head aims at

    public override void _Ready()
    {
        // "%Name" = scene-unique name: resolved by name within this scene regardless of depth,
        // so the skeleton hierarchy can be rearranged without touching this code.
        _animTree = GetNode<AnimationTree>("%AnimationTree");
        _animState = _animTree.Get(PlaybackParam).As<AnimationNodeStateMachinePlayback>();
        _headGaze = GetNode<Node3D>("%HeadGaze");
        _lookMod = GetNode<LookAtModifier3D>("%LookAtModifier3D");
        _lookTarget = GetNode<Node3D>("%LookTarget");
        _lookMod.Influence = 0f;   // start with the clip in control; SetHeadTarget ramps this up when aware
    }

    // Where the animated head is looking, in world space. Owners aim sensors with this.
    public Vector3 HeadForward => -_headGaze.GlobalBasis.Z;   // Godot faces -Z

    // How far (degrees, per side) the head can yaw before the look-at clamps it. Single source of
    // truth is the LookAtModifier3D's primary_limit_angle in GuardModel.tscn; with symmetry_limitation
    // on, the engine treats that value as a TOTAL arc split evenly left/right, hence the halving.
    public float HeadYawLimitDeg => Mathf.RadToDeg(_lookMod.PrimaryLimitAngle) * 0.5f;

    // Feeds horizontal speed into the locomotion blend space: 0 -> stand, ~1.5 -> walk, ~3.5 -> jog.
    public void SetMoveSpeed(float speed) => _animTree.Set(MoveBlendParam, speed);

    // Scan = the standing look-around clip; Move = the locomotion blend space. Safe to call every
    // frame with the current desire: Travel is a no-op when already in the requested state.
    public void SetScanning(bool scanning) => _animState.Travel(scanning ? ScanState : MoveState);

    // Aim the head at an exact world point (look-at eased in), or pass null to hand the head back to
    // the animation clip (eased out). Visual only: detection is the owner's business, and so is
    // choosing the point (e.g. a target's head rather than its feet).
    public void SetHeadTarget(Vector3? worldPoint, float dt)
    {
        float ease = 1f - Mathf.Exp(-HeadTrackRate * dt);   // frame-rate-independent smoothing step
        if (worldPoint is Vector3 point)
        {
            _lookTarget.GlobalPosition = point;
            _lookMod.Influence = Mathf.Lerp(_lookMod.Influence, 1f, ease);
        }
        else
        {
            _lookMod.Influence = Mathf.Lerp(_lookMod.Influence, 0f, ease);
        }
    }
}
