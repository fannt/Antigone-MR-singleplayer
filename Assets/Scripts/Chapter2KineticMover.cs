using UnityEngine;

[DisallowMultipleComponent]
public class Chapter2KineticMover : MonoBehaviour, ICueTriggeredReceiver
{
    public enum MotionMode
    {
        Orbit,
        Drift
    }

    [Header("Cue Triggers")]
    public bool initializeOnEnable = true;
    public bool initializeOnCue = true;

    [Header("Audience")]
    public Transform audience;

    [Header("Motion")]
    public MotionMode motionMode = MotionMode.Drift;

    [Header("Orbit")]
    public float orbitRadius = 4f;
    public float orbitSpeedDegrees = 20f;
    public float orbitHeight = 0f;
    public bool randomizeOrbitAngle = true;

    [Header("Drift")]
    public float driftSpeed = 1.5f;
    public float driftStartDistance = 6f;
    public float driftHeight = 0f;
    public Vector3 driftDirection = new Vector3(1f, 0f, 0f);
    public bool randomizeDriftDirection = true;
    public bool alignToDrift = false;
    public bool loopDrift = false;
    public float driftResetDistance = 12f;

    [Header("Roll")]
    public Vector3 rollDegreesPerSecond = new Vector3(0f, 0f, 180f);
    public bool randomizeRollDirection = true;

    private Transform _audience;
    private Vector3 _driftDir = Vector3.right;
    private Vector3 _roll = Vector3.zero;
    private Vector3 _rollEuler = Vector3.zero;
    private int _lastInitFrame = -1;

    private void OnEnable()
    {
        if (initializeOnEnable)
            InitializeMotion();
    }

    public void OnCueTriggered(Cue cue)
    {
        if (!initializeOnCue)
            return;

        InitializeMotion();
    }

    private void Update()
    {
        if (!isActiveAndEnabled)
            return;

        if (_audience == null)
            _audience = ResolveAudience();

        if (_audience == null)
            return;

        if (motionMode == MotionMode.Orbit)
        {
            var center = _audience.position + Vector3.up * orbitHeight;
            transform.RotateAround(center, Vector3.up, orbitSpeedDegrees * Time.deltaTime);
        }
        else
        {
            transform.position += _driftDir * driftSpeed * Time.deltaTime;
            if (alignToDrift && _driftDir.sqrMagnitude > 0.001f)
            {
                _rollEuler += _roll * Time.deltaTime;
                var look = Quaternion.LookRotation(_driftDir.normalized, Vector3.up);
                transform.rotation = look * Quaternion.Euler(_rollEuler);
            }

            if (loopDrift)
            {
                var distance = Vector3.Distance(transform.position, _audience.position);
                if (distance > driftResetDistance)
                    InitializeMotion();
            }
        }

        if (!alignToDrift && _roll.sqrMagnitude > 0.0001f)
            transform.Rotate(_roll * Time.deltaTime, Space.Self);
    }

    private void InitializeMotion()
    {
        if (!isActiveAndEnabled)
            return;

        if (_lastInitFrame == Time.frameCount)
            return;

        _lastInitFrame = Time.frameCount;

        _audience = ResolveAudience();
        if (_audience == null)
            return;

        if (randomizeRollDirection)
        {
            var sign = Random.value < 0.5f ? -1f : 1f;
            _roll = rollDegreesPerSecond * sign;
        }
        else
        {
            _roll = rollDegreesPerSecond;
        }
        _rollEuler = Vector3.zero;

        if (motionMode == MotionMode.Orbit)
        {
            var angle = randomizeOrbitAngle ? Random.Range(0f, 360f) : 0f;
            var rad = angle * Mathf.Deg2Rad;
            var offset = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * orbitRadius;
            transform.position = _audience.position + Vector3.up * orbitHeight + offset;
        }
        else
        {
            _driftDir = randomizeDriftDirection ? RandomDirectionOnPlane() : driftDirection;
            if (_driftDir.sqrMagnitude < 0.001f)
                _driftDir = Vector3.right;

            _driftDir.Normalize();

            // In drift mode, reuse orbitRadius as a "safe distance" offset from the audience.
            var side = Vector3.Cross(Vector3.up, _driftDir);
            if (side.sqrMagnitude < 0.001f)
                side = Vector3.right;
            side.Normalize();
            if (Random.value < 0.5f)
                side = -side;

            var safeDistance = Mathf.Max(0f, orbitRadius);
            var start = _audience.position
                - _driftDir * driftStartDistance
                + side * safeDistance
                + Vector3.up * driftHeight;
            transform.position = start;

            if (alignToDrift)
                transform.rotation = Quaternion.LookRotation(_driftDir, Vector3.up);
        }
    }

    private Transform ResolveAudience()
    {
        if (audience != null)
            return audience;

        var cam = Camera.main;
        if (cam != null)
            audience = cam.transform;

        return audience;
    }

    private static Vector3 RandomDirectionOnPlane()
    {
        var angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
    }
}
