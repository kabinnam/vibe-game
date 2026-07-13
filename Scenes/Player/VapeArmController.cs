using Godot;

/// <summary>
/// QWOP-style vape arm (issue #2). While the "vape" action (RMB) is held,
/// mouse Y applies torque to an unstable arm. Holding the vape inside the
/// sweet-spot angle band fills a meter; filling it spawns a smoke puff.
/// Dropping the arm or releasing early drains the meter.
/// </summary>
public partial class VapeArmController : Node3D
{
    [Export] public float MouseTorque = 0.005f;
    [Export] public float GravityTorque = 1.8f;
    [Export] public float WobbleStrength = 1.2f;
    [Export] public float Damping = 2.0f;
    [Export] public float FillRate = 0.35f;
    [Export] public float DrainRate = 0.25f;
    [Export] public float FastDrainRate = 1.5f;
    [Export] public float SweetSpotMin = 1.25f;
    [Export] public float SweetSpotMax = 1.55f;
    [Export] public float FailAngle = 0.4f;
    [Export] public float MaxAngle = 1.75f;
    [Export] public float LowerSpeed = 4.0f;

    private float _armAngle;
    private float _armVelocity;
    private float _meter;
    private float _time;
    private bool _armed;

    private readonly FastNoiseLite _noise = new();
    private GpuParticles3D _smokePuff;
    private ProgressBar _meterBar;

    public bool IsVaping => Input.IsActionPressed("vape");

    public override void _Ready()
    {
        _smokePuff = GetNode<GpuParticles3D>("../SmokePuff");
        _meterBar = GetNode<ProgressBar>("../../HUD/VapeMeter");
        _noise.Seed = (int)GD.Randi();
        _noise.Frequency = 1.0f;
    }

    /// <summary>Called by PlayerController with upward-positive mouse motion while vaping.</summary>
    public void AddMouseImpulse(float amount)
    {
        _armVelocity += amount * MouseTorque;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _time += dt;

        bool inSweetSpot = false;

        if (IsVaping)
        {
            // Pendulum-style gravity pulls the arm back down, wobble noise
            // keeps it unstable so the player has to keep correcting.
            _armVelocity -= GravityTorque * Mathf.Sin(_armAngle) * dt;
            _armVelocity += _noise.GetNoise1D(_time * 90f) * WobbleStrength * dt;
            _armVelocity -= _armVelocity * Damping * dt;
            _armAngle += _armVelocity * dt;

            if (_armAngle <= 0f)
            {
                _armAngle = 0f;
                _armVelocity = 0f;
            }
            else if (_armAngle >= MaxAngle)
            {
                _armAngle = MaxAngle;
                _armVelocity = Mathf.Min(_armVelocity, 0f);
            }

            if (_armAngle > FailAngle)
                _armed = true;

            inSweetSpot = _armAngle >= SweetSpotMin && _armAngle <= SweetSpotMax;

            if (inSweetSpot)
                _meter += FillRate * dt;
            else if (_armed && _armAngle < FailAngle)
                _meter -= FastDrainRate * dt; // dropped the arm mid-attempt
            else
                _meter -= DrainRate * dt;

            if (_meter >= 1f)
            {
                _smokePuff.Restart();
                _meter = 0f;
                _armed = false;
            }
        }
        else
        {
            _armAngle = Mathf.MoveToward(_armAngle, 0f, LowerSpeed * dt);
            _armVelocity = 0f;
            _armed = false;
            _meter -= FastDrainRate * dt;
        }

        _meter = Mathf.Clamp(_meter, 0f, 1f);
        Rotation = new Vector3(_armAngle, 0f, 0f);
        UpdateMeterUi(inSweetSpot);
    }

    private void UpdateMeterUi(bool inSweetSpot)
    {
        _meterBar.Visible = IsVaping || _meter > 0f;
        _meterBar.Value = _meter;
        _meterBar.Modulate = inSweetSpot ? new Color(0.5f, 1f, 0.5f) : Colors.White;
    }
}
