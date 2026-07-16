using Godot;

public partial class Guard : CharacterBody3D
{
	public enum State { Patrol, Alert }
	private State _state = State.Patrol;

	[Export] public float ViewDistance = 8f;
	[Export] public float ViewAngleDeg = 28f;   // half-angle of the cone

	// Points sampled on the player's body; seeing any one of them counts as "seen"
	// (so peeking over low cover is detected).
	private static readonly Vector3[] SamplePoints =
	{
		new Vector3(0f, 0.2f, 0f),   // low
		new Vector3(0f, 0.9f, 0f),   // torso
		new Vector3(0f, 1.7f, 0f),   // head
	};

	private Node3D _eyes;
	private Node3D _player;
	private MeshInstance3D _cone;
	private StandardMaterial3D _coneMat;

	public override void _Ready()
	{
		_eyes = GetNode<Node3D>("Eyes");
		_player = GetTree().GetFirstNodeInGroup("player") as Node3D;
		BuildVisionCone();
	}

	public override void _PhysicsProcess(double delta)
	{
		bool seen = CanSeePlayer();
		_coneMat.AlbedoColor = seen ? new Color(1, 0, 0, 0.01f)    // red-ish when it sees you
									: new Color(0, 1, 0, 0.01f);   // faint yellow otherwise

		if (_state == State.Patrol)
			Patrol();
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

	private void Patrol() { }
}
