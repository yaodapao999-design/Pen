using System.Collections;
using UnityEngine;

/// <summary>
/// Turns a clean impact-driver tip hit into a short bite, drilling burst, and yaw destabilizer.
/// </summary>
[CreateAssetMenu(fileName = "ImpactDriverEffect", menuName = "GameData/Effects/Impact Driver")]
public class ImpactDriverEffect : PenPartEffect
{
    [Header("Hit Grading")]
    [Tooltip("Velocity change applied to the hit pen on a valid front-facing driver hit.")]
    public float KnockbackVelocity = 2.8f;

    [Tooltip("Minimum collision relative speed required to trigger.")]
    public float MinRelativeVelocity = 0.5f;

    [Tooltip("Relative speed required before a front-facing hit can become perfect.")]
    public float PerfectMinRelativeVelocity = 1.35f;

    [Tooltip("Tip forward must point this much toward the target center for a valid hit.")]
    [Range(0f, 1f)] public float MinTipForwardDot = 0.58f;

    [Tooltip("Tip forward must point this much toward the target center for a perfect hit.")]
    [Range(0f, 1f)] public float PerfectTipForwardDot = 0.86f;

    [Tooltip("Owner point velocity must travel this much along the tip forward direction for a valid hit.")]
    [Range(0f, 1f)] public float MinVelocityForwardDot = 0.42f;

    [Tooltip("Owner point velocity must travel this much along the tip forward direction for a perfect hit.")]
    [Range(0f, 1f)] public float PerfectVelocityForwardDot = 0.74f;

    [Tooltip("Small cooldown to avoid a single resting contact applying many knockbacks.")]
    public float Cooldown = 0.12f;

    [Tooltip("Fallback distance for matching a contact point to this tip's colliders.")]
    public float ContactTolerance = 0.035f;

    [Header("Burst Physics")]
    [Tooltip("Short pause between the bite spark and the directional burst.")]
    public float BiteHoldSeconds = 0.045f;

    [Tooltip("Small velocity change applied immediately while the tip appears to bite in.")]
    public float BiteVelocity = 0.28f;

    [Tooltip("Velocity change applied back to the owner after the burst.")]
    public float SelfReactionVelocity = 0.32f;

    [Tooltip("Yaw angular velocity change applied to the hit pen.")]
    public float YawTorqueVelocity = 3.6f;

    [Tooltip("Short decaying yaw torque after the burst so the target feels unstable.")]
    public float InstabilityDuration = 0.32f;

    [Tooltip("Decaying yaw torque acceleration applied while instability is active.")]
    public float InstabilityTorque = 18f;

    [Tooltip("Multiplier applied to knockback, torque, and visuals on perfect hits.")]
    public float PerfectHitMultiplier = 1.45f;

    [Header("Hit Visual")]
    public Color ShockwaveColor = new(0.58f, 0.90f, 1f, 0.72f);
    public Color SparkColor = new(1f, 0.82f, 0.32f, 0.92f);
    public Color CoreFlashColor = new(1f, 1f, 1f, 0.78f);
    public Color DrillRingColor = new(0.54f, 0.96f, 1f, 0.82f);
    public Color ArcColor = new(0.72f, 0.94f, 1f, 0.82f);
    public Color ScoreMarkColor = new(0.08f, 0.11f, 0.12f, 0.62f);
    public float HitVfxDuration = 0.26f;
    public float ShockwaveRadius = 0.42f;
    public float CoreFlashRadius = 0.12f;
    public float DrillRingRadius = 0.18f;
    public float DrillSpiralDepth = 0.22f;
    public float ArcLength = 0.34f;
    public float ScoreMarkRadius = 0.18f;
    public float SparkLength = 0.36f;
    [Range(4, 20)] public int SparkCount = 11;
    [Range(2, 5)] public int DrillRingCount = 3;
    [Range(2, 8)] public int ArcCount = 4;

    public override void OnAssembled(PenEffectContext context)
    {
        if (context == null || context.Entity == null) return;
        var runtime = context.Entity.GetComponent<ImpactDriverRuntime>();
        if (runtime == null)
            runtime = context.Entity.gameObject.AddComponent<ImpactDriverRuntime>();
        runtime.Configure(context.Entity, this);
    }

    public override void OnCollision(PenEffectContext context, Collision collision)
    {
        if (context == null || context.Entity == null) return;
        var runtime = context.Entity.GetComponent<ImpactDriverRuntime>();
        runtime?.TryApply(collision);
    }

    public override void OnDetached(PenEffectContext context)
    {
        if (context == null || context.Entity == null) return;
        var runtime = context.Entity.GetComponent<ImpactDriverRuntime>();
        if (runtime == null) return;
        if (Application.isPlaying)
            Destroy(runtime);
        else
            DestroyImmediate(runtime);
    }
}

public enum ImpactDriverHitGrade
{
    Graze,
    Solid,
    Perfect
}

public class ImpactDriverRuntime : MonoBehaviour
{
    private PenEntity _owner;
    private ImpactDriverEffect _effect;
    private float _contactTolerance;
    private float _lastHitTime = -999f;
    private float _lastGrazeVfxTime = -999f;
    private Coroutine _activeSequence;

    private struct ImpactDriverHit
    {
        public Rigidbody Target;
        public Vector3 ContactPoint;
        public Vector3 SurfaceNormal;
        public Vector3 BurstDirection;
        public Vector3 TipForward;
        public float RelativeSpeed;
        public float ForwardDot;
        public float VelocityDot;
        public float Quality;
        public float Multiplier;
        public float YawSign;
        public ImpactDriverHitGrade Grade;
    }

    public void Configure(PenEntity owner, ImpactDriverEffect effect)
    {
        _owner = owner;
        _effect = effect;
        _contactTolerance = effect != null ? Mathf.Max(0.001f, effect.ContactTolerance) : 0.035f;
    }

    public void TryApply(Collision collision)
    {
        if (_owner == null || _owner.rb == null || _effect == null || collision == null) return;
        if (!TryBuildHit(collision, out ImpactDriverHit hit)) return;

        if (hit.Grade == ImpactDriverHitGrade.Graze)
        {
            TrySpawnGrazeVfx(hit);
            return;
        }

        if (Time.time - _lastHitTime < Mathf.Max(0f, _effect.Cooldown))
            return;

        _lastHitTime = Time.time;
        if (_activeSequence != null)
            StopCoroutine(_activeSequence);
        _activeSequence = StartCoroutine(ApplyImpactSequence(hit));
    }

    private bool TryBuildHit(Collision collision, out ImpactDriverHit hit)
    {
        hit = default;
        Rigidbody otherRb = ResolveOtherRigidbody(collision);
        if (otherRb == null || otherRb == _owner.rb) return false;

        float relativeSpeed = collision.relativeVelocity.magnitude;
        float grazeSpeed = Mathf.Max(0.05f, _effect.MinRelativeVelocity * 0.35f);
        if (relativeSpeed < grazeSpeed) return false;

        if (!TryGetSourceTipContact(collision, out Vector3 contactPoint, out Vector3 contactNormal))
            return false;

        Vector3 tipForward = GetDriverTipForward(_owner, transform);
        if (tipForward.sqrMagnitude < 1e-6f) return false;

        Vector3 targetDirection = ProjectHorizontal(otherRb.worldCenterOfMass - contactPoint);
        if (targetDirection.sqrMagnitude < 1e-6f)
            targetDirection = ProjectHorizontal(otherRb.position - _owner.rb.position);
        if (targetDirection.sqrMagnitude < 1e-6f)
            targetDirection = tipForward;
        targetDirection.Normalize();

        Vector3 attackVelocity = ProjectHorizontal(_owner.rb.GetPointVelocity(contactPoint) - otherRb.GetPointVelocity(contactPoint));
        if (attackVelocity.sqrMagnitude < 1e-6f)
        {
            attackVelocity = ProjectHorizontal(collision.relativeVelocity);
            if (Vector3.Dot(attackVelocity, tipForward) < 0f)
                attackVelocity = -attackVelocity;
        }
        Vector3 attackDirection = attackVelocity.sqrMagnitude > 1e-6f ? attackVelocity.normalized : tipForward;

        float forwardDot = Vector3.Dot(tipForward, targetDirection);
        float velocityDot = Vector3.Dot(tipForward, attackDirection);
        float relaxedTipDot = Mathf.Max(-0.05f, _effect.MinTipForwardDot - 0.22f);
        float relaxedVelocityDot = Mathf.Max(-0.1f, _effect.MinVelocityForwardDot - 0.18f);
        bool validFrontHit = relativeSpeed >= _effect.MinRelativeVelocity
                             && forwardDot >= relaxedTipDot
                             && velocityDot >= relaxedVelocityDot
                             && (forwardDot >= _effect.MinTipForwardDot || velocityDot >= _effect.MinVelocityForwardDot);
        bool perfectFrontHit = validFrontHit
                               && relativeSpeed >= _effect.PerfectMinRelativeVelocity
                               && forwardDot >= _effect.PerfectTipForwardDot
                               && velocityDot >= _effect.PerfectVelocityForwardDot;

        Vector3 burstDirection = Vector3.Slerp(targetDirection, tipForward, validFrontHit ? 0.58f : 0.25f);
        if (burstDirection.sqrMagnitude < 1e-6f)
            burstDirection = tipForward;
        burstDirection.Normalize();

        float speedQuality = Mathf.InverseLerp(
            Mathf.Max(0.05f, _effect.MinRelativeVelocity),
            Mathf.Max(_effect.MinRelativeVelocity + 0.01f, _effect.PerfectMinRelativeVelocity),
            relativeSpeed);
        float tipQuality = Mathf.InverseLerp(_effect.MinTipForwardDot, _effect.PerfectTipForwardDot, forwardDot);
        float velocityQuality = Mathf.InverseLerp(_effect.MinVelocityForwardDot, _effect.PerfectVelocityForwardDot, velocityDot);
        float quality = Mathf.Clamp01((speedQuality + tipQuality + velocityQuality) / 3f);

        Vector3 readableNormal = contactNormal.sqrMagnitude > 1e-5f ? contactNormal.normalized : Vector3.up;
        if (Vector3.Dot(readableNormal, Vector3.up) < 0.45f)
            readableNormal = Vector3.up;

        hit.Target = otherRb;
        hit.ContactPoint = contactPoint;
        hit.SurfaceNormal = readableNormal;
        hit.BurstDirection = burstDirection;
        hit.TipForward = tipForward;
        hit.RelativeSpeed = relativeSpeed;
        hit.ForwardDot = forwardDot;
        hit.VelocityDot = velocityDot;
        hit.Quality = quality;
        hit.Grade = perfectFrontHit ? ImpactDriverHitGrade.Perfect : validFrontHit ? ImpactDriverHitGrade.Solid : ImpactDriverHitGrade.Graze;
        hit.Multiplier = hit.Grade == ImpactDriverHitGrade.Perfect
            ? Mathf.Max(1f, _effect.PerfectHitMultiplier)
            : Mathf.Lerp(1f, Mathf.Max(1f, _effect.PerfectHitMultiplier) * 0.82f, quality * 0.35f);
        hit.YawSign = ComputeYawSign(otherRb, contactPoint, burstDirection, targetDirection);
        return true;
    }

    private IEnumerator ApplyImpactSequence(ImpactDriverHit hit)
    {
        ImpactDriverHitVfx.Spawn(hit.ContactPoint, hit.BurstDirection, hit.SurfaceNormal, _effect, hit.Grade, hit.Quality);
        ApplyBite(hit);

        float biteSeconds = Mathf.Max(0f, _effect.BiteHoldSeconds);
        if (biteSeconds > 0f)
            yield return new WaitForSeconds(biteSeconds);
        else
            yield return new WaitForFixedUpdate();

        ApplyBurst(hit);
        _activeSequence = null;
    }

    private void ApplyBite(ImpactDriverHit hit)
    {
        if (hit.Target == null || _owner == null || _owner.rb == null || _effect == null) return;
        float biteVelocity = Mathf.Max(0f, _effect.BiteVelocity) * hit.Multiplier;
        if (biteVelocity <= 0f) return;

        hit.Target.AddForceAtPosition(hit.BurstDirection * biteVelocity, hit.ContactPoint, ForceMode.VelocityChange);
        _owner.rb.AddForceAtPosition(-hit.BurstDirection * biteVelocity * 0.35f, hit.ContactPoint, ForceMode.VelocityChange);
    }

    private void ApplyBurst(ImpactDriverHit hit)
    {
        if (hit.Target == null || _owner == null || _owner.rb == null || _effect == null) return;

        float knockback = Mathf.Max(0f, _effect.KnockbackVelocity) * hit.Multiplier;
        if (knockback > 0f)
            hit.Target.AddForceAtPosition(hit.BurstDirection * knockback, hit.ContactPoint, ForceMode.VelocityChange);

        float yawVelocity = Mathf.Max(0f, _effect.YawTorqueVelocity) * hit.Multiplier * hit.YawSign;
        if (Mathf.Abs(yawVelocity) > 0.001f)
            hit.Target.AddTorque(Vector3.up * yawVelocity, ForceMode.VelocityChange);

        float reaction = Mathf.Max(0f, _effect.SelfReactionVelocity) * Mathf.Lerp(0.75f, 1f, hit.Quality);
        if (reaction > 0f)
            _owner.rb.AddForceAtPosition(-hit.BurstDirection * reaction, hit.ContactPoint, ForceMode.VelocityChange);

        ImpactDriverInstabilityRuntime.Begin(hit.Target, hit.YawSign,
            _effect.InstabilityDuration, _effect.InstabilityTorque * hit.Multiplier);
    }

    private void TrySpawnGrazeVfx(ImpactDriverHit hit)
    {
        float grazeCooldown = Mathf.Max(0.05f, _effect.Cooldown * 0.45f);
        if (Time.time - _lastGrazeVfxTime < grazeCooldown) return;
        ImpactDriverHitVfx.Spawn(hit.ContactPoint, hit.BurstDirection, hit.SurfaceNormal, _effect, ImpactDriverHitGrade.Graze, hit.Quality);
        _lastGrazeVfxTime = Time.time;
    }

    private static Vector3 GetDriverTipForward(PenEntity owner, Transform fallbackTransform)
    {
        Vector3 forward = owner != null ? owner.GetPenAxis() : fallbackTransform.right;
        forward = ProjectHorizontal(forward);
        if (forward.sqrMagnitude < 1e-6f)
            forward = ProjectHorizontal(fallbackTransform.right);
        if (forward.sqrMagnitude < 1e-6f)
            forward = Vector3.right;
        return forward.normalized;
    }

    private static float ComputeYawSign(Rigidbody target, Vector3 contactPoint, Vector3 burstDirection, Vector3 targetDirection)
    {
        Vector3 lever = ProjectHorizontal(contactPoint - target.worldCenterOfMass);
        float sign = lever.sqrMagnitude > 1e-6f
            ? Vector3.Cross(lever.normalized, burstDirection).y
            : 0f;
        if (Mathf.Abs(sign) < 0.05f)
            sign = Vector3.Cross(burstDirection, targetDirection).y;
        if (Mathf.Abs(sign) < 0.05f)
            sign = 1f;
        return Mathf.Sign(sign);
    }

    private static Vector3 ProjectHorizontal(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }

    private void OnDisable()
    {
        _activeSequence = null;
    }

    private void OnDestroy()
    {
        _activeSequence = null;
    }

    private bool TryGetSourceTipContact(Collision collision, out Vector3 point, out Vector3 normal)
    {
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            if (IsSourceTipCollider(contact.thisCollider) || IsSourceTipCollider(contact.otherCollider))
            {
                point = contact.point;
                normal = contact.normal.sqrMagnitude > 1e-5f ? contact.normal.normalized : Vector3.up;
                return true;
            }
        }

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            if (IsNearSourceTipCollider(contact.point))
            {
                point = contact.point;
                normal = contact.normal.sqrMagnitude > 1e-5f ? contact.normal.normalized : Vector3.up;
                return true;
            }
        }

        point = default;
        normal = Vector3.up;
        return false;
    }

    private Rigidbody ResolveOtherRigidbody(Collision collision)
    {
        if (collision == null || _owner == null)
            return null;

        Rigidbody rb = collision.rigidbody;
        if (rb != null && rb != _owner.rb)
            return rb;

        rb = collision.collider != null ? collision.collider.attachedRigidbody : null;
        if (rb != null && rb != _owner.rb)
            return rb;

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            rb = contact.thisCollider != null ? contact.thisCollider.attachedRigidbody : null;
            if (rb != null && rb != _owner.rb)
                return rb;

            rb = contact.otherCollider != null ? contact.otherCollider.attachedRigidbody : null;
            if (rb != null && rb != _owner.rb)
                return rb;
        }

        return null;
    }

    private bool IsSourceTipCollider(Collider collider)
    {
        if (collider == null || _owner == null || _owner.Assembly == null) return false;

        foreach (var part in _owner.Assembly.BattleParts)
        {
            if (part == null || part.Data == null || part.GameObject == null) continue;
            if (part.Data.Category != PartType.Tip) continue;
            if (!PartHasEffect(part.Data)) continue;
            if (collider.transform == part.GameObject.transform || collider.transform.IsChildOf(part.GameObject.transform))
                return true;
        }

        return false;
    }

    private bool IsNearSourceTipCollider(Vector3 worldPoint)
    {
        if (_owner == null || _owner.Assembly == null) return false;

        foreach (var part in _owner.Assembly.BattleParts)
        {
            if (part == null || part.Data == null || part.GameObject == null) continue;
            if (part.Data.Category != PartType.Tip) continue;
            if (!PartHasEffect(part.Data)) continue;
            foreach (Collider col in part.GameObject.GetComponentsInChildren<Collider>(false))
            {
                Vector3 closest = col.ClosestPoint(worldPoint);
                if ((closest - worldPoint).sqrMagnitude <= _contactTolerance * _contactTolerance)
                    return true;
            }
        }

        return false;
    }

    private bool PartHasEffect(PenPartData data)
    {
        if (data == null || data.Effects == null) return false;
        foreach (var effect in data.Effects)
        {
            if (effect == _effect)
                return true;
        }
        return false;
    }
}

public class ImpactDriverInstabilityRuntime : MonoBehaviour
{
    private Rigidbody _rb;
    private float _endsAt;
    private float _duration;
    private float _yawSign;
    private float _torque;
    private float _seed;

    public static void Begin(Rigidbody rb, float yawSign, float duration, float torque)
    {
        if (rb == null || duration <= 0f || torque <= 0f) return;

        var runtime = rb.GetComponent<ImpactDriverInstabilityRuntime>();
        if (runtime == null)
            runtime = rb.gameObject.AddComponent<ImpactDriverInstabilityRuntime>();
        runtime.Configure(rb, yawSign, duration, torque);
    }

    private void Configure(Rigidbody rb, float yawSign, float duration, float torque)
    {
        _rb = rb;
        _yawSign = Mathf.Abs(yawSign) > 0.001f ? Mathf.Sign(yawSign) : 1f;
        _duration = Mathf.Max(0.02f, duration);
        _endsAt = Time.time + _duration;
        _torque = Mathf.Max(_torque, torque);
        _seed = Random.value * 10f;
        enabled = true;
    }

    private void FixedUpdate()
    {
        if (_rb == null)
        {
            DestroyRuntimeObject(this);
            return;
        }

        float remaining = _endsAt - Time.time;
        if (remaining <= 0f)
        {
            DestroyRuntimeObject(this);
            return;
        }

        float t = Mathf.Clamp01(remaining / _duration);
        float wobble = 0.68f + 0.32f * Mathf.Sin((Time.time + _seed) * 34f);
        _rb.AddTorque(Vector3.up * (_yawSign * _torque * t * wobble), ForceMode.Acceleration);
    }

    private static void DestroyRuntimeObject(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }
}

public class ImpactDriverHitVfx : MonoBehaviour
{
    private const int RingSegments = 56;
    private const int SpiralSegments = 64;
    private const int ArcSegments = 5;

    private ImpactDriverEffect _effect;
    private ImpactDriverHitGrade _grade;
    private Vector3 _direction;
    private Vector3 _surfaceNormal;
    private float _startedAt;
    private float _duration;
    private float _quality;
    private float _visualScale;
    private LineRenderer _shockwave;
    private LineRenderer[] _sparks;
    private LineRenderer[] _drillRings;
    private LineRenderer[] _arcs;
    private LineRenderer[] _scoreMarks;
    private MeshRenderer _flashRenderer;
    private MeshFilter _flashFilter;
    private Material _shockwaveMaterial;
    private Material _sparkMaterial;
    private Material _flashMaterial;
    private Material _drillMaterial;
    private Material _arcMaterial;
    private Material _scoreMaterial;

    public static ImpactDriverHitVfx Spawn(Vector3 contactPoint, Vector3 direction, ImpactDriverEffect effect)
    {
        return Spawn(contactPoint, direction, Vector3.up, effect);
    }

    public static ImpactDriverHitVfx Spawn(Vector3 contactPoint, Vector3 direction, Vector3 surfaceNormal, ImpactDriverEffect effect)
    {
        return Spawn(contactPoint, direction, surfaceNormal, effect, ImpactDriverHitGrade.Solid, 0.5f);
    }

    public static ImpactDriverHitVfx Spawn(
        Vector3 contactPoint,
        Vector3 direction,
        Vector3 surfaceNormal,
        ImpactDriverEffect effect,
        ImpactDriverHitGrade grade,
        float quality)
    {
        if (effect == null) return null;
        Vector3 flatDirection = direction;
        surfaceNormal = surfaceNormal.sqrMagnitude > 1e-5f ? surfaceNormal.normalized : Vector3.up;
        flatDirection -= surfaceNormal * Vector3.Dot(flatDirection, surfaceNormal);
        if (flatDirection.sqrMagnitude < 1e-5f)
            flatDirection = Vector3.Cross(surfaceNormal, Vector3.right);
        if (flatDirection.sqrMagnitude < 1e-5f)
            flatDirection = Vector3.forward;
        flatDirection.Normalize();

        var root = new GameObject("ImpactDriverHitVFX");
        root.hideFlags = HideFlags.DontSave;
        root.transform.position = contactPoint + surfaceNormal * (grade == ImpactDriverHitGrade.Graze ? 0.018f : 0.026f);
        root.transform.rotation = PenEffectVfxAlignment.SurfaceRotation(flatDirection, surfaceNormal);
        var vfx = root.AddComponent<ImpactDriverHitVfx>();
        vfx.Init(effect, flatDirection, surfaceNormal, grade, quality);
        return vfx;
    }

    private void Init(ImpactDriverEffect effect, Vector3 direction, Vector3 surfaceNormal, ImpactDriverHitGrade grade, float quality)
    {
        _effect = effect;
        _grade = grade;
        _direction = direction;
        _surfaceNormal = surfaceNormal.sqrMagnitude > 1e-5f ? surfaceNormal.normalized : Vector3.up;
        _startedAt = Time.time;
        _quality = Mathf.Clamp01(quality);
        _duration = Mathf.Max(0.04f, effect.HitVfxDuration) * GradeDurationMultiplier(grade);
        _visualScale = GradeVisualScale(grade, _quality, effect);

        _shockwaveMaterial = CreateMaterial("M_ImpactDriver_Shockwave", effect.ShockwaveColor);
        _sparkMaterial = CreateMaterial("M_ImpactDriver_Sparks", effect.SparkColor);
        _flashMaterial = CreateMaterial("M_ImpactDriver_CoreFlash", effect.CoreFlashColor);
        _drillMaterial = CreateMaterial("M_ImpactDriver_DrillRings", effect.DrillRingColor);
        _arcMaterial = CreateMaterial("M_ImpactDriver_ElectricArcs", effect.ArcColor);
        _scoreMaterial = CreateMaterial("M_ImpactDriver_ScoreMark", effect.ScoreMarkColor);

        _shockwave = CreateLine("Shockwave", transform, _shockwaveMaterial, true);
        int sparkCount = Mathf.Clamp(Mathf.RoundToInt(effect.SparkCount * (_grade == ImpactDriverHitGrade.Graze ? 0.55f : _grade == ImpactDriverHitGrade.Perfect ? 1.2f : 1f)), 3, 24);
        _sparks = new LineRenderer[sparkCount];
        for (int i = 0; i < _sparks.Length; i++)
            _sparks[i] = CreateLine("Spark", transform, _sparkMaterial, false);

        int drillCount = Mathf.Clamp(_grade == ImpactDriverHitGrade.Graze ? 1 : effect.DrillRingCount, 1, 6);
        _drillRings = new LineRenderer[drillCount];
        for (int i = 0; i < _drillRings.Length; i++)
            _drillRings[i] = CreateLine("DrillSpiral", transform, _drillMaterial, false);

        int arcCount = Mathf.Clamp(effect.ArcCount + (_grade == ImpactDriverHitGrade.Perfect ? 1 : 0) - (_grade == ImpactDriverHitGrade.Graze ? 2 : 0), 1, 9);
        _arcs = new LineRenderer[arcCount];
        for (int i = 0; i < _arcs.Length; i++)
            _arcs[i] = CreateLine("ElectricArc", transform, _arcMaterial, false);

        int markCount = _grade == ImpactDriverHitGrade.Perfect ? 8 : _grade == ImpactDriverHitGrade.Solid ? 6 : 3;
        _scoreMarks = new LineRenderer[markCount];
        for (int i = 0; i < _scoreMarks.Length; i++)
            _scoreMarks[i] = CreateLine("StarScore", transform, _scoreMaterial, false);

        var flash = new GameObject("CoreFlash");
        flash.hideFlags = HideFlags.DontSave;
        flash.transform.SetParent(transform, false);
        _flashFilter = flash.AddComponent<MeshFilter>();
        _flashFilter.sharedMesh = CreateFlashMesh(effect.CoreFlashRadius * _visualScale);
        _flashRenderer = flash.AddComponent<MeshRenderer>();
        _flashRenderer.sharedMaterial = _flashMaterial;

        UpdateVisuals(0f);
    }

    private void Update()
    {
        if (_effect == null)
        {
            DestroyRuntimeObject(gameObject);
            return;
        }

        float age = Time.time - _startedAt;
        float t = Mathf.Clamp01(age / _duration);
        if (t >= 1f)
        {
            DestroyRuntimeObject(gameObject);
            return;
        }

        UpdateVisuals(t, age);
    }

    private void UpdateVisuals(float t, float age = 0f)
    {
        if (_effect == null) return;
        float alpha = 1f - Mathf.SmoothStep(0f, 1f, t);
        float bite01 = 1f - Mathf.Clamp01(t / 0.42f);
        float pulse = 0.82f + 0.18f * Mathf.Sin(age * (_grade == ImpactDriverHitGrade.Perfect ? 96f : 72f));
        float radius = Mathf.Lerp(_effect.CoreFlashRadius * 0.8f, _effect.ShockwaveRadius * _visualScale, Mathf.SmoothStep(0f, 1f, t));

        UpdateShockwave(radius, alpha);
        UpdateDrillRings(t, alpha * pulse, bite01, age);
        UpdateArcs(t, alpha, age);
        UpdateSparks(t, alpha);
        UpdateScoreMarks(t, alpha);

        if (_flashRenderer != null)
        {
            Color color = _effect.CoreFlashColor;
            color.a *= alpha * (1f - t) * (_grade == ImpactDriverHitGrade.Graze ? 0.42f : 1f);
            _flashRenderer.sharedMaterial = _flashMaterial;
            SetMaterialColor(_flashMaterial, color);
            float flashScale = Mathf.Lerp(0.9f, _grade == ImpactDriverHitGrade.Perfect ? 1.9f : 1.45f, t);
            _flashRenderer.transform.localScale = new Vector3(flashScale, flashScale, flashScale);
        }
    }

    private void UpdateShockwave(float radius, float alpha)
    {
        if (_shockwave == null || _effect == null) return;
        _shockwave.positionCount = RingSegments;
        float gradeAlpha = _grade == ImpactDriverHitGrade.Graze ? 0.22f : _grade == ImpactDriverHitGrade.Perfect ? 1.15f : 0.82f;
        _shockwave.startWidth = Mathf.Lerp(0.04f, 0.006f, 1f - alpha) * (_grade == ImpactDriverHitGrade.Perfect ? 1.15f : 1f);
        _shockwave.endWidth = _shockwave.startWidth;
        Color color = _effect.ShockwaveColor;
        color.a *= alpha * gradeAlpha;
        _shockwave.startColor = color;
        _shockwave.endColor = color;

        Vector3 center = transform.position;
        for (int i = 0; i < RingSegments; i++)
        {
            float a = i / (float)RingSegments * Mathf.PI * 2f;
            float directionalStretch = 1f + Mathf.Max(0f, Mathf.Sin(a)) * 0.32f;
            Vector3 local = transform.right * (Mathf.Cos(a) * radius * 0.82f)
                            + transform.forward * (Mathf.Sin(a) * radius * directionalStretch);
            _shockwave.SetPosition(i, center + local);
        }
    }

    private void UpdateDrillRings(float t, float alpha, float bite01, float age)
    {
        if (_drillRings == null || _effect == null) return;

        Vector3 origin = transform.position + _surfaceNormal * 0.012f;
        float collapse = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.5f));
        float gradeAlpha = _grade == ImpactDriverHitGrade.Graze ? 0.48f : _grade == ImpactDriverHitGrade.Perfect ? 1.2f : 0.88f;
        float coilCount = _grade == ImpactDriverHitGrade.Perfect ? 2.85f : 2.15f;

        for (int i = 0; i < _drillRings.Length; i++)
        {
            LineRenderer ring = _drillRings[i];
            if (ring == null) continue;

            ring.positionCount = SpiralSegments;
            ring.startWidth = Mathf.Lerp(0.026f, 0.006f, t) * (_grade == ImpactDriverHitGrade.Perfect ? 1.15f : 1f);
            ring.endWidth = Mathf.Lerp(0.014f, 0.003f, t);
            Color color = _effect.DrillRingColor;
            color.a *= alpha * gradeAlpha;
            ring.startColor = color;
            ring.endColor = color;

            float phase = age * (_grade == ImpactDriverHitGrade.Perfect ? 24f : 17f) + i * Mathf.PI * 2f / Mathf.Max(1, _drillRings.Length);
            float ringOffset = i * 0.015f;
            for (int p = 0; p < SpiralSegments; p++)
            {
                float s = p / (float)(SpiralSegments - 1);
                float angle = phase + s * Mathf.PI * 2f * coilCount;
                float radius = _effect.DrillRingRadius * _visualScale * Mathf.Lerp(1.05f, 0.22f, s) * Mathf.Lerp(1f, 0.38f, collapse);
                Vector3 center = origin
                                 + _direction * (_effect.DrillSpiralDepth * _visualScale * s * Mathf.Lerp(0.4f, 1f, collapse))
                                 + _surfaceNormal * (ringOffset + s * 0.018f + bite01 * 0.01f);
                Vector3 radial = transform.right * Mathf.Cos(angle) + transform.forward * Mathf.Sin(angle) * 0.72f;
                ring.SetPosition(p, center + radial * radius);
            }
        }
    }

    private void UpdateArcs(float t, float alpha, float age)
    {
        if (_arcs == null || _effect == null) return;

        Vector3 origin = transform.position + _surfaceNormal * 0.035f;
        float activeAlpha = alpha * (1f - Mathf.SmoothStep(0.62f, 1f, t));
        float gradeAlpha = _grade == ImpactDriverHitGrade.Graze ? 0.42f : _grade == ImpactDriverHitGrade.Perfect ? 1.15f : 0.82f;

        for (int i = 0; i < _arcs.Length; i++)
        {
            LineRenderer arc = _arcs[i];
            if (arc == null) continue;

            arc.positionCount = ArcSegments;
            arc.startWidth = Mathf.Lerp(0.018f, 0.004f, t);
            arc.endWidth = 0.002f;
            float flicker = 0.58f + 0.42f * Mathf.Abs(Mathf.Sin(age * 95f + i * 2.37f));
            Color color = _effect.ArcColor;
            color.a *= activeAlpha * gradeAlpha * flicker;
            arc.startColor = color;
            color.a *= 0.25f;
            arc.endColor = color;

            float side = Mathf.Lerp(-1f, 1f, (i + 0.5f) / _arcs.Length);
            float length = _effect.ArcLength * _visualScale * (0.65f + 0.35f * Mathf.Abs(Mathf.Sin(i * 1.91f)));
            for (int p = 0; p < ArcSegments; p++)
            {
                float s = p / (float)(ArcSegments - 1);
                float noise = Mathf.Sin(age * 70f + i * 8.13f + p * 2.21f);
                Vector3 point = origin
                                + _direction * (length * s)
                                + transform.right * (side * 0.045f * (1f + s) + noise * 0.025f)
                                + _surfaceNormal * (0.012f + Mathf.Abs(noise) * 0.018f);
                arc.SetPosition(p, point);
            }
        }
    }

    private void UpdateSparks(float t, float alpha)
    {
        if (_sparks == null || _effect == null) return;
        Vector3 origin = transform.position + _surfaceNormal * 0.02f;
        float gradeScale = _grade == ImpactDriverHitGrade.Perfect ? 1.3f : _grade == ImpactDriverHitGrade.Graze ? 0.58f : 1f;
        for (int i = 0; i < _sparks.Length; i++)
        {
            LineRenderer spark = _sparks[i];
            if (spark == null) continue;
            float spread01 = _sparks.Length <= 1 ? 0.5f : i / (float)(_sparks.Length - 1);
            float spread = Mathf.Lerp(-28f, 28f, spread01);
            float jitter = Mathf.Sin(i * 9.73f) * 11f;
            Vector3 dir = Quaternion.AngleAxis(spread + jitter, _surfaceNormal) * _direction;
            float length = _effect.SparkLength * gradeScale * (0.65f + 0.45f * Mathf.Abs(Mathf.Sin(i * 2.11f))) * Mathf.Lerp(0.25f, 1f, alpha);
            Vector3 end = origin + dir * length + _surfaceNormal * (0.02f + 0.05f * spread01);

            spark.positionCount = 2;
            spark.startWidth = Mathf.Lerp(0.028f, 0.006f, t) * gradeScale;
            spark.endWidth = 0.002f;
            Color color = _effect.SparkColor;
            color.a *= alpha * (_grade == ImpactDriverHitGrade.Graze ? 0.5f : 1f);
            spark.startColor = color;
            color.a = 0f;
            spark.endColor = color;
            spark.SetPosition(0, Vector3.Lerp(origin, end, t * 0.45f));
            spark.SetPosition(1, end);
        }
    }

    private void UpdateScoreMarks(float t, float alpha)
    {
        if (_scoreMarks == null || _effect == null) return;

        Vector3 origin = transform.position + _surfaceNormal * 0.006f;
        float markAlpha = (1f - Mathf.SmoothStep(0.46f, 1f, t)) * (_grade == ImpactDriverHitGrade.Graze ? 0.42f : 1f);
        float radius = _effect.ScoreMarkRadius * _visualScale;

        for (int i = 0; i < _scoreMarks.Length; i++)
        {
            LineRenderer mark = _scoreMarks[i];
            if (mark == null) continue;

            float angle = i / (float)_scoreMarks.Length * 360f + (i % 2 == 0 ? 0f : 10f);
            Vector3 dir = Quaternion.AngleAxis(angle, _surfaceNormal) * _direction;
            float inner = _effect.CoreFlashRadius * (0.25f + 0.12f * (i % 2));
            float outer = radius * (0.72f + 0.28f * Mathf.Abs(Mathf.Sin(i * 1.73f)));

            mark.positionCount = 2;
            mark.startWidth = Mathf.Lerp(0.018f, 0.006f, t);
            mark.endWidth = Mathf.Lerp(0.006f, 0.002f, t);
            Color color = _effect.ScoreMarkColor;
            color.a *= markAlpha * alpha;
            mark.startColor = color;
            color.a *= 0.2f;
            mark.endColor = color;
            mark.SetPosition(0, origin + dir * inner);
            mark.SetPosition(1, origin + dir * outer);
        }
    }

    private static LineRenderer CreateLine(string name, Transform parent, Material material, bool loop)
    {
        var go = new GameObject(name);
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = loop;
        line.alignment = LineAlignment.TransformZ;
        line.textureMode = LineTextureMode.Stretch;
        line.material = material;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        go.transform.rotation = Quaternion.LookRotation(parent.up, parent.forward);
        return line;
    }

    private static Mesh CreateFlashMesh(float radius)
    {
        const int segments = 16;
        Vector3[] vertices = new Vector3[segments + 1];
        int[] triangles = new int[segments * 3];
        vertices[0] = Vector3.zero;
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            float r = i % 2 == 0 ? radius : radius * 0.42f;
            vertices[i + 1] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }
        for (int i = 0; i < segments; i++)
        {
            int tri = i * 3;
            triangles[tri] = 0;
            triangles[tri + 1] = i + 1;
            triangles[tri + 2] = i == segments - 1 ? 1 : i + 2;
        }

        var mesh = new Mesh { name = "ImpactDriverCoreFlashMesh" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static float GradeDurationMultiplier(ImpactDriverHitGrade grade)
    {
        return grade switch
        {
            ImpactDriverHitGrade.Perfect => 1.18f,
            ImpactDriverHitGrade.Graze => 0.68f,
            _ => 1f
        };
    }

    private static float GradeVisualScale(ImpactDriverHitGrade grade, float quality, ImpactDriverEffect effect)
    {
        return grade switch
        {
            ImpactDriverHitGrade.Perfect => Mathf.Max(1.05f, effect.PerfectHitMultiplier),
            ImpactDriverHitGrade.Graze => Mathf.Lerp(0.48f, 0.66f, quality),
            _ => Mathf.Lerp(0.86f, 1.12f, quality)
        };
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material == null) return;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
    }

    private static Material CreateMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
        SetMaterialColor(mat, color);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
        if (mat.HasProperty("_ZTest")) mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = 3000;
        return mat;
    }

    private void OnDestroy()
    {
        if (_flashFilter != null && _flashFilter.sharedMesh != null)
            DestroyRuntimeObject(_flashFilter.sharedMesh);
        DestroyRuntimeObject(_shockwaveMaterial);
        DestroyRuntimeObject(_sparkMaterial);
        DestroyRuntimeObject(_flashMaterial);
        DestroyRuntimeObject(_drillMaterial);
        DestroyRuntimeObject(_arcMaterial);
        DestroyRuntimeObject(_scoreMaterial);
    }

    private static void DestroyRuntimeObject(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }
}
