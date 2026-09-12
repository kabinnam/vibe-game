using Godot;

namespace VibeGame.Enemies;

// A reusable vision sensor: given where it is pointed, it answers "can I see this target?"
// It owns the view cone + line-of-sight raycasting and knows nothing about WHO is looking or
// WHERE to look - the owner points it each frame (see Guard.UpdateGaze). Drop it on any NPC,
// camera, or turret that needs sight.
[GlobalClass]
public partial class VisionSensor : Node3D
{
    [Export] public float ViewDistance { get; set; } = 8f;   // how far it can see, in meters
    [Export] public float ViewAngleDeg { get; set; } = 28f;  // half-angle of the cone (full FOV is twice this)

    // Points sampled on the target's body (low / torso / head). Seeing ANY one counts as
    // "seen", so a target peeking over low cover is still detected.
    private static readonly Vector3[] SampleOffsets =
    {
        new Vector3(0f, 0.2f, 0f),   // low
        new Vector3(0f, 0.9f, 0f),   // torso
        new Vector3(0f, 1.7f, 0f),   // head
    };

    private StandardMaterial3D _coneMat;   // debug cone material (recolored by the owner per state)

    public override void _Ready() => BuildVisionCone();

    // True if the target is within range, inside the view cone, and with a clear line of sight -
    // tested against several points on its body. excludeBody keeps the sensor's own owner (e.g. the
    // guard's collider) from blocking the ray.
    public bool CanSee(Node3D target, Rid excludeBody)
    {
        var space = GetWorld3D().DirectSpaceState;   // physics world used for raycasts
        Vector3 origin = GlobalPosition;
        Vector3 forward = -GlobalBasis.Z;            // facing direction (Godot faces -Z)

        foreach (Vector3 offset in SampleOffsets)
        {
            Vector3 point = target.GlobalPosition + offset;   // a point on the target's body
            Vector3 toPoint = point - origin;                 // sensor -> that point

            if (toPoint.Length() > ViewDistance) continue;                          // 1. out of range
            if (forward.AngleTo(toPoint) > Mathf.DegToRad(ViewAngleDeg)) continue;   // 2. outside FOV cone

            // 3. Line of sight: cast a ray from the sensor to the point, ignoring the owner's body.
            var q = PhysicsRayQueryParameters3D.Create(origin, point);
            q.Exclude = new Godot.Collections.Array<Rid> { excludeBody };
            var hit = space.IntersectRay(q);
            if (hit.Count > 0 && (Node)hit["collider"] == target)
                return true;   // this point is clearly visible -> the target is seen
        }
        return false;
    }

    // Proximity multiplier for detection speed: 1 up close, down to a 0.15 floor at max range.
    public float DistanceFactor(Vector3 targetPos)
    {
        float d = GlobalPosition.DistanceTo(targetPos);
        return Mathf.Clamp(1f - d / ViewDistance, 0.15f, 1f);
    }

    // Debug-only: recolor the cone (hue supplied by the owner; alpha kept faint).
    public void SetConeColor(Color color) =>
        _coneMat.AlbedoColor = new Color(color.R, color.G, color.B, 0.01f);

    // Builds the translucent debug cone in code (so it never clutters the editor), sized from
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
        AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = _coneMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            RotationDegrees = new Vector3(90f, 0f, 0f),           // tip the cone's axis to point forward (-Z)
            Position = new Vector3(0f, 0f, -ViewDistance / 2f),   // shift so the apex sits at the sensor
        });
    }
}
