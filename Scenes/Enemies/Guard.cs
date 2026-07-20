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
	[Export] public float ScanArcDeg = 70f;    // how far left/right the "look around" sweep turns
	[Export] public float ScanSeconds = 2.0f;  // how long one sweep lasts

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
	private float _baseYaw;              // facing captured when a scan starts (sweep centers on it)
	private const float Gravity = 9.8f;  // downward accel so the guard stays on the floor

	private NavigationAgent3D _agent;    // pathfinding component (set in _Ready)
	private Node3D _eyes;                // vision origin (child node at eye height)
	private Node3D _player;              // cached player reference (found via the "player" group)
	private MeshInstance3D _cone;        // debug vision-cone mesh (generated in code)
	private StandardMaterial3D _coneMat; // debug cone material (recolored per state)
	private Label3D _label;              // debug readout floating above the guard

	#endregion

	#region Lifecycle

	// Runs once when the guard enters the scene tree (Godot lifecycle).
	public override void _Ready()
	{
		_eyes = GetNode<Node3D>("Eyes");
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
		UpdateDebug();
	}

	#endregion

	#region Perception
	// SEAM (Vision): when you add more enemy types, the perception here (the vision cone + the
	// line-of-sight sampling in CanSeePlayer) is the natural piece to extract into a reusable
	// Vision sensor node/scene that any NPC can attach.

	// Advances the awareness meter and state machine. First detection ramps up gradually in
	// Suspicious; only Searching re-locks instantly to Targeting on re-sight.
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
				// Integrate the meter over time: rate (per second) * dt (seconds this frame).
				// Fill (scaled by proximity) while seen, decay while not. No instant snap here.
				_detection += (seen ? DetectRate * DistanceFactor() : -DecayRate) * dt;
				if (_detection >= 1f) _state = State.Targeting;
				else if (_detection <= 0f) _state = State.Patrol;
				break;

			case State.Targeting:
				// Fully alerted; drops to Searching the moment line of sight is lost.
				_detection = 1f;
				if (!seen) _state = State.Searching;
				break;

			case State.Searching:
				// Cooldown after being spotted: re-seeing the player snaps straight back to
				// Targeting; otherwise the meter decays until the guard gives up (-> Patrol).
				if (seen) { _detection = 1f; _state = State.Targeting; }
				else { _detection -= DecayRate * dt; if (_detection <= 0f) _state = State.Patrol; }
				break;
		}

		_detection = Mathf.Clamp(_detection, 0f, 1f);   // keep within [0, 1]
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
		Vector3 forward = -GlobalTransform.Basis.Z;   // guard's forward direction (Godot faces -Z)

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
		switch (_state)
		{
			case State.Patrol:
				PatrolStep();
				break;
			case State.Suspicious:
				MoveTo(_lastSeenPos, SuspiciousSpeed);   // creep toward the last-seen spot (= the player while visible)
				break;
			case State.Targeting:
				if (_player != null) MoveTo(_player.GlobalPosition, ChaseSpeed);   // chase the live position
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

	// Sweeps the guard's facing left/right around its arrival heading. Returns true after one full sweep.
	private bool Scan()
	{
		if (_scanTimer == 0f) _baseYaw = Rotation.Y;    // center the sweep on the current facing
		_scanTimer += (float)GetPhysicsProcessDeltaTime();
		float phase = _scanTimer / ScanSeconds;          // 0 -> 1 over the sweep
		// Sin over one full cycle gives a smooth right-then-left-then-back swing.
		Rotation = new Vector3(0, _baseYaw + Mathf.Sin(phase * Mathf.Tau) * Mathf.DegToRad(ScanArcDeg), 0);
		if (_scanTimer >= ScanSeconds) { _scanTimer = 0f; return true; }
		return false;
	}

	// Yaws the guard to face a world position, staying upright (ignores height).
	private void FaceToward(Vector3 worldPos)
	{
		worldPos.Y = GlobalPosition.Y;                        // level out so we only turn, not tilt
		if (worldPos.IsEqualApprox(GlobalPosition)) return;   // no direction to face -> skip
		LookAt(worldPos, Vector3.Up);
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
