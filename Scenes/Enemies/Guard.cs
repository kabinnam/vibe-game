using Godot;

namespace VibeGame.Enemies;

// A guard NPC with a vision cone and a four-state awareness model
// (Patrol -> Suspicious -> Targeting -> Searching). Detection is a gradual, distance-scaled
// meter; line of sight is verified by sampling several points on the player's body and
// raycasting, so cover genuinely hides the player. The guard patrols a waypoint loop,
// investigates, and chases using its NavigationAgent3D (with RVO avoidance).
//
// Organization note: this is one cohesive script for a single enemy type. Perception lives in the
// reusable VisionSensor node and everything model-specific (animation, head look-at) behind the
// GuardRig API on GuardModel.tscn; the remaining "SEAM" comments below mark the next natural
// pieces to extract (Mover) or a node-based state machine.
public partial class Guard : CharacterBody3D
{
    // Awareness levels, in escalating order.
    // SEAM (state machine): if states grow numerous/complex, this enum + the transitions in
    // UpdatePerception + the actions in ApplyBehavior could become a node-based StateMachine
    // (one child node per state) instead of switch statements.
    public enum State
    {
        Patrol,      // unaware; walking the waypoint loop
        Suspicious,  // first-time ramp: meter fills gradually while the player is seen
        Targeting,   // fully spotted (meter == 1)
        Searching,   // post-Targeting cooldown after losing sight; re-sight snaps back instantly
    }

    private State _state = State.Patrol;

    [ExportGroup("Detection")]
    [Export] public float DetectRate { get; set; } = 1.5f;   // detection gained/sec at point-blank (scaled by distance)
    [Export] public float DecayRate { get; set; } = 0.5f;    // detection lost/sec while the player is not visible

    [ExportGroup("Movement")]
    [Export] public float PatrolSpeed { get; set; } = 1.5f;    // walk speed on patrol (player is 5.0)
    [Export] public float SuspiciousSpeed { get; set; } = 1f;  // creep speed while investigating
    [Export] public float ChaseSpeed { get; set; } = 3.5f;     // pursuit speed
    [Export] public float SearchSpeed { get; set; } = 3f;      // travel speed to the last-seen spot
    [Export] public float ArriveSlack { get; set; } = 0.5f;    // once arrived, the target must drift this far past the agent's arrival distance before we move again

    [ExportGroup("Patrol")]
    [Export] public Godot.Collections.Array<Marker3D> Waypoints { get; set; } = new();  // per-guard patrol route (order matters)
    [Export] public float ScanSeconds { get; set; } = 2.0f;  // how long the guard dwells and scans at a waypoint

    // Everything here hangs off ONE fact per frame: is the guard attending to a point (LookPoint() != null)?
    // If so the look-at owns the head and the body/sensor follow it exactly; if not the clip owns the head.
    [ExportGroup("Attention")]
    [Export] public float BodyTurnRate { get; set; } = 6f;    // how fast the body yaws (travel facing + re-centering the head)
    // Body re-centering thresholds as FRACTIONS of the head's reach (GuardRig.HeadYawLimitDeg, from the
    // LookAtModifier3D limit in GuardModel.tscn), so they can't drift from it. Start < 1: the body begins
    // helping while the head still has reach, so the head never pins short of the target.
    [Export(PropertyHint.Range, "0,1")] public float BodyTurnStartFraction { get; set; } = 0.65f;   // start turning past this share of head reach
    [Export(PropertyHint.Range, "0,1")] public float BodyTurnStopFraction { get; set; } = 0.2f;     // stop once within this share (hysteresis)
    [Export] public float GazeSmoothRate { get; set; } = 8f;  // sensor low-pass on the walk cycle's head sway; ONLY while no point is tracked

    [ExportGroup("Search")]
    [Export] public float SearchThreshold { get; set; } = 0.5f;  // suspicion above which losing sight triggers a search
    [Export] public float SearchTimeout { get; set; } = 8f;      // give up searching after this many seconds (unreachable safety)

    private AwarenessMeter _meter;       // analog awareness in [0, 1]; the FSM below decides when to fill/decay
    private Vector3 _lastSeenPos;        // where the player was most recently seen: a floor point, used as a nav target
    private Vector3 _lastSeenLookPos;    // the player's look anchor at that moment: where the head aims while investigating

    private int _wp = 0;                 // index of the current patrol waypoint
    private float _scanTimer = 0f;       // progress through the current look-around sweep
    private float _searchTimer = 0f;     // time spent in the current search (drives the timeout)

    private NavigationAgent3D _agent;    // pathfinding component (set in _Ready)
    private VisionSensor _vision;        // reusable perception sensor (cone + line-of-sight); owner points it each frame
    private PlayerController _player;    // cached player reference (found via the "player" group); its script is the player's API
    private Label3D _label;              // debug readout floating above the guard (StateLabel node in Guard.tscn)
    private GuardRig _rig;               // the model's API: animation state, locomotion blend, head look-at, head aim
    private bool _scanning;              // true while standing and scanning (gaze should ride the head animation)
    private bool _bodyTurning;           // hysteresis latch: true while the body is re-centering a far target
    private bool _arrived;               // hysteresis latch: true from reaching the nav target until it drifts ArriveSlack past arrival distance

    // Runs once when the guard enters the scene tree (Godot lifecycle).
    public override void _Ready()
    {
        _vision = GetNode<VisionSensor>("VisionSensor");
        _rig = GetNode<GuardRig>("GuardModel");
        _player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
        _meter = new AwarenessMeter(DetectRate, DecayRate);
        _label = GetNode<Label3D>("StateLabel");   // billboard/font/position are configured in Guard.tscn

        // The agent's static avoidance settings (avoidance_enabled / radius / height) now live on
        // the NavigationAgent3D node in Guard.tscn (scene = config). Here we only do the code-side
        // wiring: a derived value and the signal hookup.
        _agent = GetNode<NavigationAgent3D>("NavigationAgent3D");
        _agent.MaxSpeed = ChaseSpeed;                   // derived from a tunable export, so kept in sync here
        _agent.VelocityComputed += OnVelocityComputed;  // "here's your collision-safe velocity" signal
    }

    // Fixed-timestep tick (~60/sec) for physics and AI (Godot lifecycle).
    public override void _PhysicsProcess(double delta)
    {
        UpdatePerception(delta);
        ApplyBehavior();
        _rig.SetScanning(_scanning);        // tell the model what the AI decided: scan pose or locomotion blend
        _rig.SetMoveSpeed(HorizontalSpeed);
        UpdateAttention((float)delta);      // head -> body re-center -> sensor, all from one look point (after AI + anim state)
        UpdateDebug();
    }

    // Advances the awareness meter and state machine. Detection ramps up gradually; the meter value
    // is carried into Searching as "memory", so a re-sight resumes filling (effectively instant when
    // the meter was still ~1, a quick ramp when it was only suspicious).
    private void UpdatePerception(double delta)
    {
        // TODO (perf - revisit when spawning many guards): throttle this vision check instead of
        // running it every physics frame.
        //   What: _vision.CanSee() runs here ~60x/sec, and each call fires up to 3 raycasts (one per
        //   body sample) = ~180 server raycasts/sec PER guard.
        //   Why: each raycast is cheap, but they add up fast with many guards. Standard guidance is to
        //   run perception on a Timer at ~0.1-0.2s intervals.
        //   How (behavior-preserving): integrate the meter over the ACTUAL elapsed interval (not the
        //   per-frame delta) and keep TurnBodyToward per-frame for smooth facing. Left per-frame for
        //   now: a couple of guards are cheap and per-frame gives the smoothest tracking.
        //   Refs: Godot ray-casting - https://docs.godotengine.org/en/stable/tutorials/physics/ray-casting.html
        //         Stealth FOV guide (0.1-0.2s Timer) - https://uhiyama-lab.com/en/notes/godot/stealth-fov-system/
        bool seen = _player != null && _vision.CanSee(_player);
        float dt = (float)delta;                        // seconds elapsed this frame
        if (seen)
        {
            _lastSeenPos = _player.GlobalPosition;            // feet: where to walk to
            _lastSeenLookPos = _player.LookAnchorPosition;    // upper body: where to look
        }

        switch (_state)
        {
            case State.Patrol:
                // Unaware until the player is spotted, then begin the gradual ramp.
                if (seen) _state = State.Suspicious;
                break;

            case State.Suspicious:
                if (seen)
                {
                    _meter.Fill(dt, _vision.DistanceFactor(_player.GlobalPosition));   // gradual first-contact ramp
                    if (_meter.Value >= 1f) _state = State.Targeting;
                }
                else if (_meter.Value > SearchThreshold)
                {
                    EnterSearching();                                   // was pretty sure -> go investigate
                }
                else
                {
                    _meter.Decay(dt);                                   // fleeting glimpse -> forget it
                    if (_meter.Value <= 0f) _state = State.Patrol;
                }
                break;

            case State.Targeting:
                // Fully alerted; drops to a search the moment line of sight is lost.
                _meter.Pin();
                if (!seen) EnterSearching();
                break;

            case State.Searching:
                if (seen)
                {
                    _meter.Fill(dt, _vision.DistanceFactor(_player.GlobalPosition));   // resume filling (instant when ~1)
                    if (_meter.Value >= 1f) _state = State.Targeting;
                }
                else
                {
                    _searchTimer += dt;
                    if (_arrived)                                       // only decay once we've reached the spot
                        _meter.Decay(dt);
                    if (_meter.Value <= 0f || _searchTimer >= SearchTimeout)
                        _state = State.Patrol;                          // give up: found nothing, or timed out
                }
                break;
        }
    }

    // Enter the active search: keep the current meter value (memory) and reset the search timer.
    // Called from both Suspicious (lost sight while > threshold) and Targeting (lost sight).
    private void EnterSearching()
    {
        _state = State.Searching;
        _searchTimer = 0f;
    }

    // SEAM (Mover): the navmesh steering here could become a reusable Mover node/scene shared by
    // any NPC; ApplyBehavior/GoToAndScan are the per-state actions half of the FSM seam.

    // Picks a movement target + speed per state (or scans when arrived).
    private void ApplyBehavior()
    {
        _scanning = false;   // assume "not scanning" each frame; the scan branches below set it true
        switch (_state)
        {
            case State.Patrol:
                if (Waypoints.Count == 0)
                    StandStill();
                else if (GoToAndScan(Waypoints[_wp].GlobalPosition, PatrolSpeed))
                    _wp = (_wp + 1) % Waypoints.Count;   // sweep done -> next waypoint (wraps around)
                break;
            case State.Suspicious:
                MoveTo(_lastSeenPos, SuspiciousSpeed);   // creep toward the last-seen spot (= the player while visible)
                break;
            case State.Targeting:
                if (_player != null)
                    MoveTo(_player.GlobalPosition, ChaseSpeed);
                break;
            case State.Searching:
                GoToAndScan(_lastSeenPos, SearchSpeed);   // decay in UpdatePerception eventually returns us to Patrol
                break;
        }
    }

    // Travels to a world target across the navmesh; on arrival, stands and scans in place (the visible
    // look-around comes from the IdleScanning clip). Returns true once a full scan interval completes:
    // Patrol uses that to advance its waypoint; Search ignores it and lets the awareness decay end the search.
    private bool GoToAndScan(Vector3 target, float speed)
    {
        if (!MoveTo(target, speed)) return false;   // still traveling
        _scanning = true;
        _scanTimer += (float)GetPhysicsProcessDeltaTime();
        if (_scanTimer < ScanSeconds) return false;
        _scanTimer = 0f;
        return true;
    }

    // Steers the guard toward a target across the navmesh (feeding a desired velocity to avoidance)
    // and reports whether we're there. Arrival has hysteresis: the agent's IsNavigationFinished is a
    // hard threshold at TargetDesiredDistance, which flaps every frame when the target hovers at that
    // edge (a player strafing beside a parked guard) - one frame of 3.5 m/s, one frame of 0, and the
    // locomotion blend stutters idle/jog. So once arrived we stay arrived until the target has clearly
    // moved away (ArriveSlack beyond the arrival distance).
    private bool MoveTo(Vector3 target, float speed)
    {
        _agent.TargetPosition = target;
        if (_agent.IsNavigationFinished()) _arrived = true;
        else if (_agent.DistanceToTarget() > _agent.TargetDesiredDistance + ArriveSlack) _arrived = false;
        if (_arrived)
        {
            StandStill();   // arrived: request stop (gravity is still applied in OnVelocityComputed)
            return true;
        }

        Vector3 dir = _agent.GetNextPathPosition() - GlobalPosition;  // toward the next point on the path
        dir.Y = 0;                                                    // horizontal only; gravity handles vertical
        dir = dir.Normalized();
        TurnBodyToward(GlobalPosition + dir, (float)GetPhysicsProcessDeltaTime());   // face the travel direction
        _agent.Velocity = dir * speed;                                // desired velocity -> triggers OnVelocityComputed
        return false;
    }

    // Requests zero horizontal velocity. Still routed through the agent so gravity/MoveAndSlide
    // happen in the callback even when the guard isn't actively walking (e.g. while scanning).
    private void StandStill() => _agent.Velocity = Vector3.Zero;

    // Horizontal speed only (ignores vertical/gravity on Y).
    private float HorizontalSpeed => new Vector2(Velocity.X, Velocity.Z).Length();

    // Frame-rate-independent smoothing: how far to ease toward a target in one frame of length dt.
    private static float EaseFactor(float rate, float dt) => 1f - Mathf.Exp(-rate * dt);

    // Applies the avoidance-adjusted (safe) velocity plus gravity, then moves. Fired by the agent's signal.
    private void OnVelocityComputed(Vector3 safeVelocity)
    {
        Vector3 v = Velocity;
        v.X = safeVelocity.X;                                  // avoidance-adjusted horizontal velocity
        v.Z = safeVelocity.Z;
        v += GetGravity() * (float)GetPhysicsProcessDeltaTime();   // keep the guard on the floor (project gravity + any Area3D override)
        Velocity = v;
        MoveAndSlide();                                        // the ONE place we move (avoidance is on)
    }

    // Smoothly eases the body's yaw toward a world point (horizontal only, no tilt).
    // InterpolateWith blends rotation robustly (it re-normalizes internally), and since both
    // transforms share our origin, position is untouched.
    private void TurnBodyToward(Vector3 worldPos, float dt)
    {
        worldPos.Y = GlobalPosition.Y;                        // level: turn, don't tilt
        if (worldPos.IsEqualApprox(GlobalPosition)) return;   // nothing to face -> skip
        Transform3D facing = GlobalTransform.LookingAt(worldPos, Vector3.Up);
        GlobalTransform = GlobalTransform.InterpolateWith(facing, EaseFactor(BodyTurnRate, dt));
    }

    // "Where is the guard attending", as the exact world point to aim the head at. The player decides
    // where "look at me" means (its LookAnchor), so this never assumes the player's shape. null means
    // "no specific point" (patrol / scanning) -> the head is handed back to the clip.
    private Vector3? LookPoint()
    {
        if (_state == State.Targeting && _player != null) return _player.LookAnchorPosition;   // live player
        if (_state == State.Suspicious) return _lastSeenLookPos;                               // the spot being investigated
        return null;
    }

    // Everything downstream of the guard's attention this frame, in dependency order: aim the head,
    // re-center the body if the head is running out of reach, then align the sensor to wherever the
    // head ended up. The look point is computed once so the three can't disagree.
    private void UpdateAttention(float dt)
    {
        Vector3? point = LookPoint();
        _rig.SetHeadTarget(point);
        ReCenterBody(point, dt);
        AimSensor(tracking: point.HasValue, dt);
    }

    // While standing still, the body shares the turn: it starts re-centering as the head NEARS its
    // reach and continues until the target is nearly centered (hysteresis, so no shuffle-step at the
    // edge). Visual only: the cone and detection are unaffected (they stay code-owned).
    private void ReCenterBody(Vector3? lookPoint, float dt)
    {
        if (lookPoint is not Vector3 point)
        {
            _bodyTurning = false;   // not aware -> nothing to re-center on
            return;
        }

        // Re-center only while essentially stopped; when moving, MoveTo already faces the body toward travel.
        bool stationary = HorizontalSpeed < 0.3f;
        float targetYawDeg = Mathf.RadToDeg(FlatAngleTo(point));   // how far off body-forward the target is
        float headReachDeg = _rig.HeadYawLimitDeg;                 // how far the head alone can turn
        if (!stationary) _bodyTurning = false;
        else if (targetYawDeg > headReachDeg * BodyTurnStartFraction) _bodyTurning = true;    // head nearing its reach -> body helps
        else if (targetYawDeg < headReachDeg * BodyTurnStopFraction) _bodyTurning = false;    // re-centered -> stop
        if (_bodyTurning) TurnBodyToward(point, dt);
    }

    // Yaw angle (radians) between the guard's forward (-Z) and the flattened direction to a world point.
    private float FlatAngleTo(Vector3 worldPos)
    {
        Vector3 to = worldPos - GlobalPosition;
        to.Y = 0f;
        if (to.LengthSquared() < 0.0001f) return 0f;
        return (-GlobalTransform.Basis.Z).AngleTo(to);
    }

    // Points the vision sensor along the head's aim; the head is the authoritative "where is the guard
    // looking". Who owns the head decides how:
    //  - tracking a point: the cone must EQUAL the head. No easing - the sensor is a child of the body,
    //    so any lag lets a body turn carry the cone off the target (cone swinging past the head on
    //    direction reversals; lost sight).
    //  - clip owns the head (patrol, scan): ease, purely to low-pass the walk cycle's head sway. There
    //    is no target to lose, so lag costs nothing here.
    // Pitch rides along only when it is bob-safe and useful (tracking a target up stairs, or the
    // standing scan sweep); otherwise it is flattened so the walk cycle's head bob can't tilt the cone.
    // Only rotation changes; the sensor stays pinned at eye height.
    private void AimSensor(bool tracking, float dt)
    {
        Vector3 forward = _rig.HeadForward;            // where the head is looking
        bool trackVertically = _scanning || _state == State.Targeting;
        if (!trackVertically) forward.Y = 0f;         // yaw-only: flatten to level -> kills walk-cycle bob
        if (forward.IsZeroApprox()) return;           // no usable direction -> keep last aim

        Transform3D aim = _vision.GlobalTransform.LookingAt(_vision.GlobalPosition + forward, Vector3.Up);
        _vision.GlobalTransform = tracking
            ? aim
            : _vision.GlobalTransform.InterpolateWith(aim, EaseFactor(GazeSmoothRate, dt));
    }

    // Updates the debug cone color and the floating state/percent readout.
    private void UpdateDebug()
    {
        Color c = StateColor();
        _vision.SetConeColor(c);                                  // recolor the debug cone by state
        _label.Text = $"{_state} {(int)(_meter.Value * 100f)}%";
        _label.Modulate = c;
    }

    // Debug color per awareness state (green -> yellow -> orange -> red).
    private Color StateColor() => _state switch
    {
        State.Patrol => new Color(0.2f, 1f, 0.05f),
        State.Suspicious => new Color(1f, 0.9f, 0.05f),
        State.Searching => new Color(1f, 0.55f, 0.05f),
        State.Targeting => new Color(1f, 0.15f, 0.05f),
        _ => Colors.White,
    };

}
