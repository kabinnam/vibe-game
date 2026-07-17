using Godot;

/// <summary>
/// A guard NPC with a vision cone and a four-state awareness model
/// (Patrol -> Suspicious -> Targeting -> Searching). Detection is a gradual,
/// distance-scaled meter; line of sight is verified by sampling several points on
/// the player's body and raycasting, so cover genuinely hides the player.
/// Locomotion (patrol/investigate movement) is added in a later navigation pass.
/// </summary>
public partial class Guard : CharacterBody3D
{
	/// <summary>Awareness levels, in escalating order.</summary>
	public enum State
	{
		Patrol,      // unaware; idle / patrol
		Suspicious,  // first-time ramp: meter fills gradually while the player is seen
		Targeting,   // fully spotted (meter == 1)
		Searching,   // post-Targeting cooldown after losing sight; re-sight snaps back instantly
	}

	private State _state = State.Patrol;

	/// <summary>How far the guard can see, in meters.</summary>
	[Export] public float ViewDistance = 8f;

	/// <summary>Half-angle of the vision cone, in degrees (full field of view is twice this).</summary>
	[Export] public float ViewAngleDeg = 28f;

	/// <summary>Detection gained per second at point-blank range (scaled down with distance).</summary>
	[Export] public float DetectRate = 1.5f;

	/// <summary>Detection lost per second while the player is not visible.</summary>
	[Export] public float DecayRate = 0.5f;

	/// <summary>
	/// Local offsets sampled on the player's body (feet / torso / head). Seeing ANY one of
	/// them counts as "seen", so a player peeking over low cover is still detected.
	/// </summary>
	private static readonly Vector3[] SamplePoints =
	{
		new Vector3(0f, 0.2f, 0f),   // low
		new Vector3(0f, 0.9f, 0f),   // torso
		new Vector3(0f, 1.7f, 0f),   // head
	};

	/// <summary>Awareness meter in the range [0, 1]; 1 means fully alerted.</summary>
	private float _detection = 0f;

	/// <summary>World position where the player was most recently seen (used by Searching).</summary>
	private Vector3 _lastSeenPos;

	/// <summary>Whether the player passed the full see-check this frame.</summary>
	private bool _playerVisible;

	private Node3D _eyes;                 // vision origin (child node at eye height)
	private Node3D _player;               // cached player reference (found via the "player" group)
	private MeshInstance3D _cone;         // debug vision-cone mesh (generated in code)
	private StandardMaterial3D _coneMat;  // debug cone material (recolored per state)
	private Label3D _label;               // debug readout floating above the guard

	/// <summary>Godot lifecycle: runs once when the node enters the scene tree.</summary>
	public override void _Ready()
	{
		_eyes = GetNode<Node3D>("Eyes");
		_player = GetTree().GetFirstNodeInGroup("player") as Node3D;
		BuildVisionCone();
		BuildDebugLabel();
	}

	/// <summary>Godot lifecycle: fixed-timestep tick (~60/sec) for physics and AI logic.</summary>
	public override void _PhysicsProcess(double delta)
	{
		UpdatePerception(delta);
		ApplyBehavior();
		UpdateDebug();
	}

	/// <summary>
	/// Advances the awareness meter and state machine for this frame. The first detection ramps
	/// up gradually in <see cref="State.Suspicious"/>; only <see cref="State.Searching"/> re-locks
	/// instantly to <see cref="State.Targeting"/> on re-sight.
	/// </summary>
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

	/// <summary>
	/// Proximity multiplier for detection speed: 1 at the guard, falling off linearly to a
	/// 0.15 floor at the edge of <see cref="ViewDistance"/> (closer = detected faster).
	/// </summary>
	private float DistanceFactor()
	{
		float d = _eyes.GlobalPosition.DistanceTo(_player.GlobalPosition);
		return Mathf.Clamp(1f - d / ViewDistance, 0.15f, 1f);
	}

	/// <summary>Per-state behavior. Movement is added with the navigation milestone.</summary>
	private void ApplyBehavior()
	{
		switch (_state)
		{
			case State.Patrol:
				Patrol();
				break;
			case State.Suspicious:
			case State.Targeting:
			case State.Searching:
				// Track the live player only while actually visible; otherwise face the last
				// known position so the guard can't watch the player through walls.
				if (_playerVisible && _player != null) FaceToward(_player.GlobalPosition);
				else FaceToward(_lastSeenPos);
				break;
		}
	}

	/// <summary>Yaws the guard to face a world position, staying upright (ignores height).</summary>
	private void FaceToward(Vector3 worldPos)
	{
		worldPos.Y = GlobalPosition.Y;                        // level out so we only turn, not tilt
		if (worldPos.IsEqualApprox(GlobalPosition)) return;   // no direction to face -> skip
		LookAt(worldPos, Vector3.Up);
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
	/// <summary>
	/// True if the player is currently visible: within range, inside the view cone, and with an
	/// unobstructed line of sight - tested against several points on the player's body.
	/// </summary>
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

	/// <summary>Updates the debug cone color and the floating state/percent readout.</summary>
	private void UpdateDebug()
	{
		Color c = StateColor();
		_coneMat.AlbedoColor = new Color(c.R, c.G, c.B, 0.01f);   // hue by state, alpha kept faint
		_label.Text = $"{_state} {(int)(_detection * 100f)}%";
		_label.Modulate = c;
	}

	/// <summary>Debug color per awareness state (green -> yellow -> orange -> red).</summary>
	private Color StateColor() => _state switch
	{
		State.Patrol => new Color(0.2f, 1f, 0.05f),
		State.Suspicious => new Color(1f, 0.9f, 0.05f),
		State.Searching => new Color(1f, 0.55f, 0.05f),
		State.Targeting => new Color(1f, 0.15f, 0.05f),
		_ => Colors.White,
	};

	/// <summary>
	/// Builds the translucent debug vision cone in code (so it never clutters the editor) and
	/// sizes it from <see cref="ViewDistance"/> / <see cref="ViewAngleDeg"/> so it always matches
	/// the detection math. Base radius = height * tan(half-angle).
	/// </summary>
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

	/// <summary>Builds the billboarded debug label that floats above the guard.</summary>
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

	/// <summary>Patrol behavior placeholder; navmesh movement is added in the navigation milestone.</summary>
	private void Patrol() { }
}
