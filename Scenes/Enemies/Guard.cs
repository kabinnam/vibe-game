using Godot;

// A guard NPC with a vision cone and a four-state awareness model
// (Patrol -> Suspicious -> Targeting -> Searching). Detection is a gradual, distance-scaled
// meter; line of sight is verified by sampling several points on the player's body and
// raycasting, so cover genuinely hides the player. The guard patrols a waypoint loop,
// investigates, and chases using its NavigationAgent3D (with RVO avoidance).
//
// Organization note: this is one cohesive script for a single enemy type. If a second enemy
// type appears, the "SEAM" comments below mark the natural pieces to extract into reusable
// component nodes/scenes (Vision, Mover) or a node-based state machine.
public partial class Guard : CharacterBody3D
{
	#region Configuration & fields

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

	// --- Perception tuning (Inspector-editable) ---
	[Export] public float ViewDistance = 8f;    // how far the guard can see, in meters
	[Export] public float ViewAngleDeg = 28f;   // half-angle of the vision cone (full FOV is twice this)
	[Export] public float DetectRate = 1.5f;    // detection gained/sec at point-blank (scaled by distance)
	[Export] public float DecayRate = 0.5f;     // detection lost/sec while the player is not visible

	// --- Movement tuning (Inspector-editable) ---
	[Export] public float PatrolSpeed = 1.5f;      // walk speed on patrol (player is 5.0)
	[Export] public float SuspiciousSpeed = 1f;    // creep speed while investigating
	[Export] public float ChaseSpeed = 3.5f;       // pursuit speed
	[Export] public float SearchSpeed = 3f;        // travel speed to the last-seen spot

	[Export] public Godot.Collections.Array<Marker3D> Waypoints = new();  // per-guard patrol route (order matters)
	[Export] public float ScanSeconds = 2.0f;  // how long the guard dwells and scans at a waypoint
	[Export] public float GazeBlendRate = 8f;  // how fast the cone eases toward its target orientation

	// --- Head-tracking tuning (Inspector-editable) ---
	[Export] public float HeadTrackRate = 10f;   // how fast the head look eases in/out (visual only)
	[Export] public float BodyTurnRate = 6f;     // how fast the body turns (locomotion facing + re-centering the head)
	[Export] public float NeckLimitDeg = 70f;    // once the target passes this yaw from facing, the body turns to re-center

	// --- Search tuning (Inspector-editable) ---
	[Export] public float SearchThreshold = 0.5f;  // suspicion above which losing sight triggers a search
	[Export] public float SearchTimeout = 8f;       // give up searching after this many seconds (unreachable safety)

	// Points sampled on the player's body (feet / torso / head). Seeing ANY one counts as
	// "seen", so a player peeking over low cover is still detected.
	private static readonly Vector3[] SamplePoints =
	{
		new Vector3(0f, 0.2f, 0f),   // low
		new Vector3(0f, 0.9f, 0f),   // torso
		new Vector3(0f, 1.7f, 0f),   // head
	};

	private float _detection = 0f;       // awareness meter in [0, 1]; 1 = fully alerted
	private Vector3 _lastSeenPos;        // where the player was most recently seen (used by Searching)
	private bool _playerVisible;         // did the player pass the full see-check this frame?

	private int _wp = 0;                 // index of the current patrol waypoint
	private float _scanTimer = 0f;       // progress through the current look-around sweep
	private float _searchTimer = 0f;     // time spent in the current search (drives the timeout)
	private const float Gravity = 9.8f;  // downward accel so the guard stays on the floor

	private NavigationAgent3D _agent;    // pathfinding component (set in _Ready)
	private Node3D _eyes;                // vision origin (child node at eye height)
	private Node3D _player;              // cached player reference (found via the "player" group)
	private MeshInstance3D _cone;        // debug vision-cone mesh (generated in code)
	private StandardMaterial3D _coneMat; // debug cone material (recolored per state)
	private Label3D _label;              // debug readout floating above the guard
	private AnimationTree _animTree;     // drives the model's animation (state machine + locomotion blend)
	private AnimationNodeStateMachinePlayback _animState;  // handle used to switch between the "Move" and "Scan" states
  private Node3D _headGaze;            // offset node under the head bone; its -Z is the animated look direction
  private bool _scanning;              // true while standing and scanning (gaze should ride the head animation)
	private LookAtModifier3D _lookMod;   // WRITES the head bone to look at _lookTarget (visual head tracking)
	private Node3D _lookTarget;          // world-space point the head aims at (moved to the look point when aware)
	private bool _bodyTurning;           // hysteresis latch: true while the body is re-centering a far target

	#endregion

	#region Lifecycle

	// Runs once when the guard enters the scene tree (Godot lifecycle).
	public override void _Ready()
	{
		_eyes = GetNode<Node3D>("Eyes");
		_animTree = GetNode<AnimationTree>("GuardModel/AnimationTree");
		_animState = _animTree.Get("parameters/playback").As<AnimationNodeStateMachinePlayback>();
	_headGaze = GetNode<Node3D>("GuardModel/GeneralSkeleton/HeadAttach/HeadGaze");
		_lookMod = GetNode<LookAtModifier3D>("GuardModel/GeneralSkeleton/LookAtModifier3D");
		_lookTarget = GetNode<Node3D>("LookTarget");
		_lookMod.TargetNode = _lookMod.GetPathTo(_lookTarget);   // tell the modifier which node to aim the head at
		_lookMod.Influence = 0f;                                 // start off; UpdateHeadTracking ramps it when aware
		_player = GetTree().GetFirstNodeInGroup("player") as Node3D;
		BuildVisionCone();
		BuildDebugLabel();

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
	UpdateGaze((float)delta);
		UpdateDebug();
		UpdateLocomotionAnimation();
		UpdateHeadTracking((float)delta);   // visual head-tracking + body re-center (runs after AI + anim state)
	}

	// Drives the model's animation to match what the AI is already doing (read-only: it observes
	// state/speed, it never moves the guard). Two parts:
	//   1. Pick the state-machine state: "Scan" (the look-around) only while standing at a Patrol/
	//      Search waypoint (_scanning); "Move" otherwise.
	//   2. Inside "Move", feed horizontal speed into the blend space so it blends stand/walk/jog.
	private void UpdateLocomotionAnimation()
	{
		_animState.Travel(_scanning ? "Scan" : "Move");

		float speed = new Vector2(Velocity.X, Velocity.Z).Length();   // horizontal speed only (ignore gravity on Y)
		_animTree.Set("parameters/Move/blend_position", speed);       // 0 -> stand idle, ~1.5 -> walk, ~3.5 -> jog
	}

	#endregion

	#region Perception
	// SEAM (Vision): when you add more enemy types, the perception here (the vision cone + the
	// line-of-sight sampling in CanSeePlayer) is the natural piece to extract into a reusable
	// Vision sensor node/scene that any NPC can attach.

	// Advances the awareness meter and state machine. Detection ramps up gradually; the meter value
	// is carried into Searching as "memory", so a re-sight resumes filling (effectively instant when
	// the meter was still ~1, a quick ramp when it was only suspicious).
	private void UpdatePerception(double delta)
	{
		bool seen = CanSeePlayer();
		_playerVisible = seen;
		float dt = (float)delta;                        // seconds elapsed this frame
		if (seen) _lastSeenPos = _player.GlobalPosition;

		switch (_state)
		{
			case State.Patrol:
				// Unaware until the player is spotted, then begin the gradual ramp.
				if (seen) _state = State.Suspicious;
				break;

			case State.Suspicious:
				if (seen)
				{
					_detection += DetectRate * DistanceFactor() * dt;   // gradual first-contact ramp
					if (_detection >= 1f) _state = State.Targeting;
				}
				else if (_detection > SearchThreshold)
				{
					EnterSearching();                                   // was pretty sure -> go investigate
				}
				else
				{
					_detection -= DecayRate * dt;                       // fleeting glimpse -> forget it
					if (_detection <= 0f) _state = State.Patrol;
				}
				break;

			case State.Targeting:
				// Fully alerted; drops to a search the moment line of sight is lost.
				_detection = 1f;
				if (!seen) EnterSearching();
				break;

			case State.Searching:
				if (seen)
				{
					_detection += DetectRate * DistanceFactor() * dt;   // resume filling (instant when ~1)
					if (_detection >= 1f) _state = State.Targeting;
				}
				else
				{
					_searchTimer += dt;
					if (_agent.IsNavigationFinished())                  // only decay once we've reached the spot
						_detection -= DecayRate * dt;
					if (_detection <= 0f || _searchTimer >= SearchTimeout)
						_state = State.Patrol;                          // give up: found nothing, or timed out
				}
				break;
		}

		_detection = Mathf.Clamp(_detection, 0f, 1f);   // keep within [0, 1]
	}

	// Enter the active search: keep the current meter value (memory) and reset the search timer.
	// Called from both Suspicious (lost sight while > threshold) and Targeting (lost sight).
	private void EnterSearching()
	{
		_state = State.Searching;
		_searchTimer = 0f;
	}

	// Proximity multiplier for detection speed: 1 at the guard, down to a 0.15 floor at max range.
	private float DistanceFactor()
	{
		float d = _eyes.GlobalPosition.DistanceTo(_player.GlobalPosition);
		return Mathf.Clamp(1f - d / ViewDistance, 0.15f, 1f);
	}

	// TODO (perf - revisit when spawning many guards): throttle this vision check instead of
	// running it every physics frame.
	//   What: today _PhysicsProcess -> UpdatePerception calls CanSeePlayer() ~60x/sec, and each
	//   call fires up to 3 raycasts (one per body sample) = ~180 server raycasts/sec PER guard.
	//   Why: individually these server-side raycasts are cheap, but they add up fast with many
	//   guards. The standard guidance is to run perception on a Timer at ~0.1-0.2s intervals.
	//   How (so behavior stays identical): integrate the detection meter over the ACTUAL elapsed
	//   interval (not the per-frame delta), and consider keeping FaceToward() per-frame for smooth
	//   tracking. Left per-frame for now: a single guard is cheap and per-frame gives the smoothest facing.
	//   Refs: Godot ray-casting docs - https://docs.godotengine.org/en/stable/tutorials/physics/ray-casting.html
	//         Stealth FOV guide (recommends a 0.1-0.2s Timer) - https://uhiyama-lab.com/en/notes/godot/stealth-fov-system/
	//
	// True if the player is currently visible: within range, inside the view cone, and with a clear
	// line of sight - tested against several points on the player's body.
	private bool CanSeePlayer()
	{
		if (_player == null) return false;

		var space = GetWorld3D().DirectSpaceState;    // physics world used for raycasts
		Vector3 forward = -_eyes.GlobalTransform.Basis.Z;   // guard's forward direction (Godot faces -Z)

		foreach (Vector3 offset in SamplePoints)
		{
			Vector3 target = _player.GlobalPosition + offset;   // a point on the player's body
			Vector3 toTarget = target - _eyes.GlobalPosition;   // eyes -> that point

			if (toTarget.Length() > ViewDistance) continue;                          // 1. out of range
			if (forward.AngleTo(toTarget) > Mathf.DegToRad(ViewAngleDeg)) continue;   // 2. outside FOV cone

			// 3. Line of sight: cast a ray from the eyes to the point, ignoring our own body.
			var q = PhysicsRayQueryParameters3D.Create(_eyes.GlobalPosition, target);
			q.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
			var hit = space.IntersectRay(q);
			if (hit.Count > 0 && (Node)hit["collider"] == _player)
				return true;   // this point is clearly visible -> the player is seen
		}
		return false;
	}

	#endregion

	#region Movement
	// SEAM (Mover): the navmesh steering here could become a reusable Mover node/scene shared by
	// any NPC; ApplyBehavior/PatrolStep/SearchStep are the per-state actions half of the FSM seam.

	// Picks a movement target + speed per state (or scans when arrived).
	private void ApplyBehavior()
	{
	_scanning = false;   // assume "not scanning" each frame; the scan branches below set it true
		switch (_state)
		{
			case State.Patrol:
				PatrolStep();
				break;
			case State.Suspicious:
				MoveTo(_lastSeenPos, SuspiciousSpeed);   // creep toward the last-seen spot (= the player while visible)
				break;
	  case State.Targeting:
		if (_player != null)
		  MoveTo(_player.GlobalPosition, ChaseSpeed);
		break;
			case State.Searching:
				SearchStep();
				break;
		}
	}

	// Patrol: walk to the current waypoint; on arrival, sweep-look, then advance to the next.
	private void PatrolStep()
	{
		if (Waypoints.Count == 0) { StandStill(); return; }
		Vector3 target = Waypoints[_wp].GlobalPosition;
		_agent.TargetPosition = target;
		if (_agent.IsNavigationFinished())
		{
			StandStill();
	  _scanning = true;
			if (Scan()) _wp = (_wp + 1) % Waypoints.Count;   // sweep done -> next waypoint (wraps around)
		}
		else
		{
			MoveTo(target, PatrolSpeed);
		}
	}

	// Searching: travel to the last-seen spot, then look around while the meter decays toward giving up.
	private void SearchStep()
	{
		_agent.TargetPosition = _lastSeenPos;
		if (_agent.IsNavigationFinished())
		{
			StandStill();
	  _scanning = true;
			Scan();   // keep sweeping; UpdatePerception's decay is what eventually returns us to Patrol
		}
		else
		{
			MoveTo(_lastSeenPos, SearchSpeed);
		}
	}

	// Steers the guard toward a target across the navmesh, feeding a desired velocity to avoidance.
	private void MoveTo(Vector3 targetPos, float speed)
	{
		_agent.TargetPosition = targetPos;
		if (_agent.IsNavigationFinished())
		{
			StandStill();   // arrived: request stop (gravity is still applied in OnVelocityComputed)
			return;
		}
		Vector3 dir = _agent.GetNextPathPosition() - GlobalPosition;  // toward the next point on the path
		dir.Y = 0;                                                    // horizontal only; gravity handles vertical
		dir = dir.Normalized();
		FaceToward(GlobalPosition + dir);                             // face the travel direction
		_agent.Velocity = dir * speed;                                // desired velocity -> triggers OnVelocityComputed
	}

	// Requests zero horizontal velocity. Still routed through the agent so gravity/MoveAndSlide
	// happen in the callback even when the guard isn't actively walking (e.g. while scanning).
	private void StandStill() => _agent.Velocity = Vector3.Zero;

	// Applies the avoidance-adjusted (safe) velocity plus gravity, then moves. Fired by the agent's signal.
	private void OnVelocityComputed(Vector3 safeVelocity)
	{
		Vector3 v = Velocity;
		v.X = safeVelocity.X;                                  // avoidance-adjusted horizontal velocity
		v.Z = safeVelocity.Z;
		v.Y -= Gravity * (float)GetPhysicsProcessDeltaTime();  // keep the guard on the floor
		Velocity = v;
		MoveAndSlide();                                        // the ONE place we move (avoidance is on)
	}

  // Times how long the guard has been standing and scanning. Returns true once a full
  // scan interval has elapsed. The visible look-around + cone motion now come from the
  // IdleScanning animation (via the head bone), so this no longer moves the gaze itself.
  private bool Scan()
  {
	_scanTimer += (float)GetPhysicsProcessDeltaTime();
	if (_scanTimer >= ScanSeconds) { _scanTimer = 0f; return true; }
	return false;
  }

	// Turns the guard's body to face a world position, smoothed (eased yaw) so travel turns and the
	// head re-center never snap. This is the deferred body-turn smoothing.
	private void FaceToward(Vector3 worldPos) => TurnBodyToward(worldPos, (float)GetPhysicsProcessDeltaTime());

	// Smoothly eases the body's yaw toward a world point (horizontal only, no tilt).
	private void TurnBodyToward(Vector3 worldPos, float dt)
	{
		worldPos.Y = GlobalPosition.Y;                        // level: turn, don't tilt
		if (worldPos.IsEqualApprox(GlobalPosition)) return;   // nothing to face -> skip
		Basis desired = GlobalTransform.LookingAt(worldPos, Vector3.Up).Basis;
		float t = 1f - Mathf.Exp(-BodyTurnRate * dt);         // frame-rate-independent ease
		Basis blended = GlobalTransform.Basis.Orthonormalized().Slerp(desired.Orthonormalized(), t);
		GlobalTransform = new Transform3D(blended, GlobalPosition);
	}

	// The single "where is the guard attending" source, shared by the cone (UpdateGaze) and the head.
	// null means "no specific point" (patrol / scanning) -> callers fall back to their default.
	private Vector3? LookPoint()
	{
		if (_state == State.Targeting && _player != null) return _player.GlobalPosition;  // live player
		if (_state == State.Suspicious) return _lastSeenPos;                              // the spot being investigated
		return null;
	}

	// Visual head-tracking. The LookAtModifier WRITES the head bone to look at _lookTarget; the cone
	// and detection are unaffected (they stay code-owned). While aware: aim the head at the look point
	// and ease the modifier in. If the target passes the neck limit while the guard is standing still,
	// turn the body to re-center it - with hysteresis so it doesn't shuffle-step at the edge.
	private void UpdateHeadTracking(float dt)
	{
		float ease = 1f - Mathf.Exp(-HeadTrackRate * dt);

		if (LookPoint() is Vector3 point)
		{
			Vector3 aim = point;
			aim.Y += 1.5f;                                    // aim at head/upper body, not the feet
			_lookTarget.GlobalPosition = aim;
			_lookMod.Influence = Mathf.Lerp(_lookMod.Influence, 1f, ease);

			// Re-center only while essentially stopped; when moving, MoveTo already faces the body toward travel.
			bool stationary = new Vector2(Velocity.X, Velocity.Z).Length() < 0.3f;
			float yawDeg = Mathf.RadToDeg(FlatAngleTo(point));
			if (!stationary) _bodyTurning = false;
			else if (yawDeg > NeckLimitDeg) _bodyTurning = true;             // target past the neck -> start turning
			else if (yawDeg < NeckLimitDeg * 0.5f) _bodyTurning = false;     // comfortably in front -> stop (hysteresis)
			if (_bodyTurning) TurnBodyToward(point, dt);
		}
		else
		{
			_lookMod.Influence = Mathf.Lerp(_lookMod.Influence, 0f, ease);   // not aware -> hand the head back to the clip
			_bodyTurning = false;
		}
	}

	// Yaw angle (radians) between the guard's forward (-Z) and the flattened direction to a world point.
	private float FlatAngleTo(Vector3 worldPos)
	{
		Vector3 to = worldPos - GlobalPosition;
		to.Y = 0f;
		if (to.LengthSquared() < 0.0001f) return 0f;
		return (-GlobalTransform.Basis.Z).AngleTo(to);
	}

  // Single owner of the gaze sensor's orientation. Chooses a target each frame and eases toward it:
  //  - scanning  -> follow the animated head bone (immersive, matches the mocap)
  //  - targeting -> code-aim at the live player position (deterministic)
  //  - otherwise -> aligned with the body's facing
  private void UpdateGaze(float dt)
  {
	Basis desired;
  if (_scanning)
	  desired = _headGaze.GlobalTransform.Basis;   // scan: cone rides the animated head
	else if (LookPoint() is Vector3 look)
	  desired = LookBasisAt(look);                 // aware: aim at the shared look point (player / last-seen)
	else
	  desired = GlobalTransform.Basis;             // else: aligned with the body's facing

	float t = 1f - Mathf.Exp(-GazeBlendRate * dt);                     // frame-rate-independent ease factor
	// Slerp converts each basis to a quaternion internally, which REQUIRES normalized (scale-free)
	// bases. Bone/body/look-at bases can carry scale or float drift, so clean BOTH operands first.
	Basis from = _eyes.GlobalTransform.Basis.Orthonormalized();
	Basis to = desired.Orthonormalized();
	_eyes.GlobalTransform = new Transform3D(from.Slerp(to, t), _eyes.GlobalPosition);
  }

  // Builds a yaw-only orientation that looks from the eyes toward a world point.
  private Basis LookBasisAt(Vector3 worldPos)
  {
	worldPos.Y = _eyes.GlobalPosition.Y;                             // flatten so we don't tilt up/down
	if (worldPos.IsEqualApprox(_eyes.GlobalPosition)) return _eyes.GlobalTransform.Basis;
	return _eyes.GlobalTransform.LookingAt(worldPos, Vector3.Up).Basis;
  }

	#endregion

	#region Debug visualization

	// Updates the debug cone color and the floating state/percent readout.
	private void UpdateDebug()
	{
		Color c = StateColor();
		_coneMat.AlbedoColor = new Color(c.R, c.G, c.B, 0.01f);   // hue by state, alpha kept faint
		_label.Text = $"{_state} {(int)(_detection * 100f)}%";
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

	// Builds the translucent debug vision cone in code (so it never clutters the editor), sized from
	// ViewDistance/ViewAngleDeg (base radius = height * tan(half-angle)) so it matches the detection math.
	private void BuildVisionCone()
	{
		var mesh = new CylinderMesh
		{
			TopRadius = 0f,                                                          // zero top -> a cone
			Height = ViewDistance,
			BottomRadius = ViewDistance * Mathf.Tan(Mathf.DegToRad(ViewAngleDeg)),   // r = H * tan(theta)
		};
		_coneMat = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,    // honor the alpha channel
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,   // flat color, ignore lighting
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,         // visible from both sides
		};
		_cone = new MeshInstance3D
		{
			Mesh = mesh,
			MaterialOverride = _coneMat,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			RotationDegrees = new Vector3(90f, 0f, 0f),           // tip the cone's axis to point forward (-Z)
			Position = new Vector3(0f, 0f, -ViewDistance / 2f),   // shift so the apex sits at the eyes
		};
		_eyes.AddChild(_cone);
	}

	// Builds the billboarded debug label that floats above the guard.
	private void BuildDebugLabel()
	{
		_label = new Label3D
		{
			Text = "Patrol 0%",
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,   // always face the camera
			NoDepthTest = true,                                     // draw on top of geometry
			FontSize = 48,
			Position = new Vector3(0f, 2.2f, 0f),                   // float above the guard
		};
		AddChild(_label);
	}

	#endregion
}
