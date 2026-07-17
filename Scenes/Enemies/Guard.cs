using Godot;

public partial class Guard : CharacterBody3D
{
	public enum State { Patrol, Suspicious, Targeting, Searching }
	private State _state = State.Patrol;

	[Export] public float ViewDistance = 8f;
	[Export] public float ViewAngleDeg = 28f;   // half-angle of the cone
	[Export] public float DetectRate = 1.5f;    // meter/sec at point-blank
	[Export] public float DecayRate = 0.5f;     // meter/sec while unseen

	// Points sampled on the player's body; seeing any one of them counts as "seen"
	// (so peeking over low cover is detected).
	private static readonly Vector3[] SamplePoints =
	{
		new Vector3(0f, 0.2f, 0f),   // low
		new Vector3(0f, 0.9f, 0f),   // torso
		new Vector3(0f, 1.7f, 0f),   // head
	};

	private float _detection = 0f;
	private Vector3 _lastSeenPos;

	private Node3D _eyes;
	private Node3D _player;
	private MeshInstance3D _cone;
	private StandardMaterial3D _coneMat;
	private Label3D _label;

	public override void _Ready()
	{
		_eyes = GetNode<Node3D>("Eyes");
		_player = GetTree().GetFirstNodeInGroup("player") as Node3D;
		BuildVisionCone();
		BuildDebugLabel();
	}

	public override void _PhysicsProcess(double delta)
	{
		UpdatePerception(delta);
		ApplyBehavior();
		UpdateDebug();
	}

	// Four-state awareness. The first detection ramps up gradually in Suspicious; only the
	// post-Targeting Searching state re-locks instantly on re-sight.
	private void UpdatePerception(double delta)
	{
		bool seen = CanSeePlayer();
		float dt = (float)delta;
		if (seen) _lastSeenPos = _player.GlobalPosition;

		switch (_state)
		{
			case State.Patrol:
				if (seen) _state = State.Suspicious;
				break;

			case State.Suspicious:
				_detection += (seen ? DetectRate * DistanceFactor() : -DecayRate) * dt;
				if (_detection >= 1f) _state = State.Targeting;
				else if (_detection <= 0f) _state = State.Patrol;
				break;

			case State.Targeting:
				_detection = 1f;
				if (!seen) _state = State.Searching;
				break;

			case State.Searching:
				if (seen) { _detection = 1f; _state = State.Targeting; }
				else { _detection -= DecayRate * dt; if (_detection <= 0f) _state = State.Patrol; }
				break;
		}
		_detection = Mathf.Clamp(_detection, 0f, 1f);
	}

	private float DistanceFactor()   // closer -> faster
	{
		float d = _eyes.GlobalPosition.DistanceTo(_player.GlobalPosition);
		return Mathf.Clamp(1f - d / ViewDistance, 0.15f, 1f);
	}

	private void ApplyBehavior()
	{
		switch (_state)
		{
			case State.Patrol:
				Patrol();
				break;
			case State.Suspicious:
			case State.Targeting:
				if (_player != null) FaceToward(_player.GlobalPosition);
				break;
			case State.Searching:
				FaceToward(_lastSeenPos);
				break;
		}
	}

	// Turn to face a world position, staying upright.
	private void FaceToward(Vector3 worldPos)
	{
		worldPos.Y = GlobalPosition.Y;
		if (worldPos.IsEqualApprox(GlobalPosition)) return;
		LookAt(worldPos, Vector3.Up);
	}

	private bool CanSeePlayer()
	{
		if (_player == null) return false;

		var space = GetWorld3D().DirectSpaceState;
		Vector3 forward = -GlobalTransform.Basis.Z;

		foreach (Vector3 offset in SamplePoints)
		{
			Vector3 target = _player.GlobalPosition + offset;
			Vector3 toTarget = target - _eyes.GlobalPosition;

			if (toTarget.Length() > ViewDistance) continue;                         // 1. range
			if (forward.AngleTo(toTarget) > Mathf.DegToRad(ViewAngleDeg)) continue;  // 2. FOV angle

			var q = PhysicsRayQueryParameters3D.Create(_eyes.GlobalPosition, target);
			q.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
			var hit = space.IntersectRay(q);                                        // 3. line of sight
			if (hit.Count > 0 && (Node)hit["collider"] == _player)
				return true;
		}
		return false;
	}

	private void UpdateDebug()
	{
		Color c = StateColor();
		_coneMat.AlbedoColor = new Color(c.R, c.G, c.B, 0.15f);
		_label.Text = $"{_state} {(int)(_detection * 100f)}%";
		_label.Modulate = c;
	}

	private Color StateColor() => _state switch
	{
		State.Patrol => new Color(0.2f, 1f, 0.2f),
		State.Suspicious => new Color(1f, 0.9f, 0.2f),
		State.Searching => new Color(1f, 0.55f, 0.1f),
		State.Targeting => new Color(1f, 0.15f, 0.15f),
		_ => Colors.White,
	};

	// Debug vision cone, built in code so it never clutters the editor and always matches
	// ViewDistance / ViewAngleDeg (single source of truth).
	private void BuildVisionCone()
	{
		var mesh = new CylinderMesh
		{
			TopRadius = 0f,   // zero-radius top = a cone
			Height = ViewDistance,
			BottomRadius = ViewDistance * Mathf.Tan(Mathf.DegToRad(ViewAngleDeg)),
		};
		_coneMat = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		_cone = new MeshInstance3D
		{
			Mesh = mesh,
			MaterialOverride = _coneMat,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			RotationDegrees = new Vector3(90f, 0f, 0f),        // point the cone forward (-Z)
			Position = new Vector3(0f, 0f, -ViewDistance / 2f), // tip at the eyes
		};
		_eyes.AddChild(_cone);
	}

	// Debug readout floating above the guard.
	private void BuildDebugLabel()
	{
		_label = new Label3D
		{
			Text = "Patrol 0%",
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			NoDepthTest = true,
			FontSize = 48,
			Position = new Vector3(0f, 2.2f, 0f),
		};
		AddChild(_label);
	}

	private void Patrol() { }
}
