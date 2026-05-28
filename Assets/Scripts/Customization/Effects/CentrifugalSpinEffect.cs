using UnityEngine;

/// <summary>
/// Adds a charged boomerang return after launch. Used by cap-style parts that should make
/// the pen feel like a rotating hammer instead of a flat stat bonus.
/// </summary>
[CreateAssetMenu(fileName = "CentrifugalSpinEffect", menuName = "GameData/Effects/Centrifugal Spin")]
public class CentrifugalSpinEffect : PenPartEffect
{
    [Tooltip("Drag force required before max-pull charging can start.")]
    [Range(0f, 1f)] public float ChargeForceThreshold = 0.96f;

    [Tooltip("Seconds of holding at max pull required for full charge.")]
    public float FullChargeSeconds = 0.75f;

    [Tooltip("Only the opposite end from the hammer cap can charge. 0.2 = final 20% of body length.")]
    [Range(0.05f, 0.45f)] public float OppositeEndRatio = 0.2f;

    [Tooltip("Minimum charge consumed at release before the special spin fires.")]
    [Range(0f, 1f)] public float MinReleaseCharge = 0.18f;

    [Tooltip("Yaw angular velocity added at full charge.")]
    public float ChargedSpinAngularVelocity = 4.2f;

    [Tooltip("Small tangential velocity injected around the hammer cap pivot at full charge.")]
    public float PivotKickVelocity = 0.55f;

    [Tooltip("Move the pivot a little from the cap towards body center so it feels physical instead of pinned.")]
    [Range(0f, 0.35f)] public float PivotCenterBlend = 0.12f;

    [Tooltip("Upper guardrail for added horizontal spin.")]
    public float MaxYawAngularVelocity = 9f;

    [Header("Boomerang Motion")]
    [Tooltip("Free-flight time before the return steering starts.")]
    public float BoomerangOutwardSeconds = 0.38f;

    [Tooltip("Distance from the launch point that can also trigger the return steering.")]
    public float BoomerangTurnDistance = 1.45f;

    [Tooltip("Maximum duration of the whole boomerang assist.")]
    public float BoomerangMaxDuration = 2.15f;

    [Tooltip("Target horizontal speed while returning to the launch point.")]
    public float BoomerangReturnSpeed = 6.8f;

    [Tooltip("How quickly the assist bends the current velocity toward the return vector. Lower values keep collisions more influential.")]
    public float BoomerangReturnResponsiveness = 3.25f;

    [Tooltip("Maximum horizontal acceleration used by the return steering.")]
    public float BoomerangReturnAcceleration = 13.5f;

    [Tooltip("Side force that gives the return path a visible curling shape.")]
    public float BoomerangCurlAcceleration = 1.8f;

    [Tooltip("The assist ends after the pen comes this close to its launch point.")]
    public float BoomerangFinishDistance = 0.34f;

    [Tooltip("Extra spin torque while the boomerang assist is active.")]
    public float BoomerangSpinTorque = 28f;

    [Header("Boomerang Visual")]
    public Color SpinRingColor = new(0.62f, 0.92f, 1f, 0.62f);
    public Color ReturnArcColor = new(1f, 0.86f, 0.34f, 0.54f);
    public float SpinRingRadius = 0.42f;
    public float SpinRingWidth = 0.025f;
    public float ReturnArcWidth = 0.026f;

    [Header("Charge Bar")]
    public Vector3 BarOffset = new(0f, 0.26f, 0f);
    public Vector2 BarSize = new(0.42f, 0.045f);
    public Color BarBackColor = new(0.02f, 0.07f, 0.10f, 0.55f);
    public Color BarFillColor = new(0.60f, 0.90f, 1.00f, 0.88f);
    public Color BarReadyColor = new(1.00f, 0.82f, 0.36f, 0.95f);

    public override void OnAssembled(PenEffectContext context)
    {
        if (context == null || context.Entity == null) return;
        var runtime = context.Entity.GetComponent<CentrifugalChargeRuntime>();
        if (runtime == null)
            runtime = context.Entity.gameObject.AddComponent<CentrifugalChargeRuntime>();
        runtime.Configure(context.Entity, this);
    }

    public override void OnDetached(PenEffectContext context)
    {
        if (context == null || context.Entity == null) return;
        var runtime = context.Entity.GetComponent<CentrifugalChargeRuntime>();
        if (runtime != null)
            runtime.RemoveEffect(this);

        var boomerang = context.Entity.GetComponent<CentrifugalBoomerangRuntime>();
        if (boomerang != null)
        {
            boomerang.CancelAssist();
            CentrifugalChargeRuntime.DestroyRuntimeObject(boomerang);
        }
    }

    public override void OnRoundEnd(PenEffectContext context)
    {
        if (context == null || context.Entity == null) return;

        var runtime = context.Entity.GetComponent<CentrifugalChargeRuntime>();
        if (runtime != null)
            runtime.CancelCharge(this);

        var boomerang = context.Entity.GetComponent<CentrifugalBoomerangRuntime>();
        if (boomerang != null)
            boomerang.CancelAssist();
    }

    public override void OnLaunch(PenEffectContext context, Vector3 direction, float force)
    {
        if (context == null || context.Rigidbody == null || context.Entity == null) return;
        if (Mathf.Approximately(ChargedSpinAngularVelocity, 0f)) return;

        var runtime = context.Entity.GetComponent<CentrifugalChargeRuntime>();
        float charge = runtime != null
            ? runtime.ConsumeCharge(this, context.Entity.LastLaunchSnapshot)
            : EstimateFallbackCharge(context.Entity, force);
        if (charge < MinReleaseCharge) return;

        Vector3 flatDirection = direction;
        flatDirection.y = 0f;
        if (flatDirection.sqrMagnitude < 1e-6f) return;
        flatDirection.Normalize();

        Vector3 axis = context.Entity.GetPenAxis();
        axis.y = 0f;
        if (axis.sqrMagnitude < 1e-6f)
            axis = context.Transform.right;
        axis.Normalize();

        PenLaunchPhysicsSnapshot launch = context.Entity.LastLaunchSnapshot;
        float signedSpin = context.Rigidbody.angularVelocity.y;
        if (Mathf.Abs(signedSpin) < 0.05f && launch.IsValid)
            signedSpin = launch.EstimatedYawAngularVelocity;
        if (Mathf.Abs(signedSpin) < 0.05f && launch.IsValid)
            signedSpin = launch.AngularImpulseY;
        if (Mathf.Abs(signedSpin) < 0.05f)
            signedSpin = Vector3.Dot(Vector3.Cross(axis, flatDirection), Vector3.up);
        if (Mathf.Abs(signedSpin) < 0.05f)
            signedSpin = 1f;
        float spinSign = Mathf.Sign(signedSpin);

        Vector3 angularVelocity = context.Rigidbody.angularVelocity;
        angularVelocity.y += spinSign * ChargedSpinAngularVelocity * charge;
        angularVelocity.y = Mathf.Clamp(angularVelocity.y, -MaxYawAngularVelocity, MaxYawAngularVelocity);
        context.Rigidbody.angularVelocity = angularVelocity;

        if (PivotKickVelocity > 0f && TryGetHammerPivot(context.Entity, out Vector3 pivot))
        {
            Vector3 radius = context.Rigidbody.worldCenterOfMass - pivot;
            radius.y = 0f;
            if (radius.sqrMagnitude > 1e-6f)
            {
                Vector3 tangent = Vector3.Cross(Vector3.up * spinSign, radius).normalized;
                if (Vector3.Dot(tangent, flatDirection) > 0f)
                    context.Rigidbody.AddForce(tangent * PivotKickVelocity * charge, ForceMode.VelocityChange);
            }
        }

        var boomerang = context.Entity.GetComponent<CentrifugalBoomerangRuntime>();
        if (boomerang == null)
            boomerang = context.Entity.gameObject.AddComponent<CentrifugalBoomerangRuntime>();

        Vector3 home = context.Entity.LastLaunchSnapshot.IsValid
            ? context.Entity.LastLaunchSnapshot.CenterOfMassWorld
            : context.Rigidbody.worldCenterOfMass;
        boomerang.Begin(context.Entity, this, charge, flatDirection, home, spinSign);
    }

    public bool IsChargeEligible(PenEntity entity, Vector3 contactPointWorld, out Vector3 barAnchorWorld)
    {
        barAnchorWorld = contactPointWorld;
        if (!TryGetBodyFrame(entity, out Vector3 axis, out Vector3 center, out float halfLength))
            return false;

        float hammerSign = TryGetHammerPivot(entity, out Vector3 hammerPivot)
            ? Mathf.Sign(Vector3.Dot(hammerPivot - center, axis))
            : -1f;
        if (Mathf.Approximately(hammerSign, 0f))
            hammerSign = -1f;

        float oppositeSign = -hammerSign;
        float threshold = halfLength * Mathf.Lerp(1f, 0f, OppositeEndRatio * 2f);
        float contactAlong = Vector3.Dot(contactPointWorld - center, axis);
        bool eligible = oppositeSign > 0f ? contactAlong >= threshold : contactAlong <= -threshold;
        barAnchorWorld = center + axis * (oppositeSign * halfLength) + BarOffset;
        return eligible;
    }

    public bool TryGetHammerPivot(PenEntity entity, out Vector3 pivot)
    {
        pivot = default;
        if (entity == null || entity.Assembly == null) return false;

        foreach (var part in entity.Assembly.BattleParts)
        {
            if (part == null || part.Data == null || part.GameObject == null) continue;
            if (!PenEffectVfxAlignment.PartHasEffect(part.Data, this)) continue;

            pivot = part.GameObject.transform.position;
            if (PivotCenterBlend > 0f && TryGetBodyFrame(entity, out _, out Vector3 center, out _))
                pivot = Vector3.Lerp(pivot, center, PivotCenterBlend);
            return true;
        }

        return false;
    }

    private float EstimateFallbackCharge(PenEntity entity, float force)
    {
        if (force < ChargeForceThreshold) return 0f;
        if (!IsChargeEligible(entity, entity != null ? entity.LastLaunchSnapshot.ContactPointWorld : Vector3.zero, out _))
            return 0f;
        return Mathf.InverseLerp(ChargeForceThreshold, 1f, force);
    }

    private static bool TryGetBodyFrame(PenEntity entity, out Vector3 axis, out Vector3 center, out float halfLength)
    {
        axis = Vector3.right;
        center = entity != null ? entity.transform.position : Vector3.zero;
        halfLength = 0.5f;
        if (entity == null) return false;

        axis = entity.GetPenAxis();
        axis.y = 0f;
        if (axis.sqrMagnitude < 1e-6f)
            axis = entity.transform.right;
        axis.Normalize();

        var capsule = entity.penCollider;
        if (capsule != null)
        {
            center = capsule.transform.TransformPoint(capsule.center);
            Vector3 scale = capsule.transform.lossyScale;
            float axisScale = capsule.direction switch
            {
                0 => Mathf.Abs(scale.x),
                1 => Mathf.Abs(scale.y),
                2 => Mathf.Abs(scale.z),
                _ => Mathf.Abs(scale.x)
            };
            halfLength = Mathf.Max(0.05f, capsule.height * axisScale * 0.5f);
            return true;
        }

        return true;
    }
}

public class CentrifugalChargeRuntime : MonoBehaviour
{
    private PenEntity _owner;
    private CentrifugalSpinEffect _effect;
    private AimPhaseChannelSO _channel;
    private float _charge;
    private float _releasedCharge;
    private bool _releasePending;
    private GameObject _barRoot;
    private Transform _fill;
    private Material _backMaterial;
    private Material _fillMaterial;
    private Material _readyMaterial;
    private bool _subscribed;

    public void Configure(PenEntity owner, CentrifugalSpinEffect effect)
    {
        _owner = owner;
        _effect = effect;
        enabled = true;
    }

    public void SetChannel(AimPhaseChannelSO channel)
    {
        if (_channel == channel)
        {
            Subscribe();
            return;
        }

        Unsubscribe();
        _channel = channel;
        Subscribe();
    }

    public void RemoveEffect(CentrifugalSpinEffect effect)
    {
        if (_effect != effect) return;
        _effect = null;
        _charge = 0f;
        _releasedCharge = 0f;
        _releasePending = false;
        HideBar();
        Unsubscribe();
        DestroyBar();
        _owner = null;
        enabled = false;
    }

    public void CancelCharge(CentrifugalSpinEffect effect)
    {
        if (_effect != effect) return;
        _charge = 0f;
        _releasedCharge = 0f;
        _releasePending = false;
        HideBar();
    }

    public float ConsumeCharge(CentrifugalSpinEffect effect, PenLaunchPhysicsSnapshot snapshot)
    {
        if (_effect != effect) return 0f;
        float result = _releasePending ? _releasedCharge : _charge;
        _charge = 0f;
        _releasedCharge = 0f;
        _releasePending = false;
        HideBar();
        return Mathf.Clamp01(result);
    }

    private void OnEnable() => Subscribe();

    private void OnDisable()
    {
        Unsubscribe();
        HideBar();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        DestroyBar();
    }

    private void Subscribe()
    {
        if (_subscribed || _channel == null) return;
        _channel.OnUpdated += HandleAimUpdated;
        _channel.OnReleased += HandleReleased;
        _channel.OnCancelled += HandleCancelled;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed || _channel == null) return;
        _channel.OnUpdated -= HandleAimUpdated;
        _channel.OnReleased -= HandleReleased;
        _channel.OnCancelled -= HandleCancelled;
        _subscribed = false;
    }

    private void HandleAimUpdated(AimSample sample)
    {
        if (_effect == null || _owner == null || !sample.IsArmed)
        {
            DecayCharge();
            HideBar();
            return;
        }

        bool eligible = _effect.IsChargeEligible(_owner, sample.ContactPointWorld, out Vector3 anchor);
        bool charging = eligible && sample.Force >= _effect.ChargeForceThreshold;
        if (charging)
        {
            _charge = Mathf.MoveTowards(_charge, 1f, Time.deltaTime / Mathf.Max(0.05f, _effect.FullChargeSeconds));
            ShowBar(anchor, _charge);
        }
        else
        {
            DecayCharge();
            if (eligible && sample.Force > 0.65f)
                ShowBar(anchor, _charge);
            else
                HideBar();
        }
    }

    private void HandleReleased(float _)
    {
        _releasedCharge = _charge;
        _releasePending = true;
        HideBar();
    }

    private void HandleCancelled()
    {
        _charge = 0f;
        _releasedCharge = 0f;
        _releasePending = false;
        HideBar();
    }

    private void DecayCharge()
    {
        _charge = Mathf.MoveTowards(_charge, 0f, Time.deltaTime * 1.8f);
    }

    private void ShowBar(Vector3 anchor, float charge)
    {
        EnsureBar();
        if (_barRoot == null) return;

        _barRoot.SetActive(true);
        _barRoot.transform.position = anchor;
        Camera cam = Camera.main;
        if (cam != null)
            _barRoot.transform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
        else
            _barRoot.transform.rotation = Quaternion.identity;

        float fill = Mathf.Clamp01(charge);
        if (_fill != null)
        {
            Vector3 scale = _fill.localScale;
            scale.x = Mathf.Max(0.001f, _effect.BarSize.x * fill);
            scale.y = _effect.BarSize.y;
            _fill.localScale = scale;
            _fill.localPosition = new Vector3(-_effect.BarSize.x * 0.5f + scale.x * 0.5f, 0f, -0.002f);
        }

        var renderer = _fill != null ? _fill.GetComponent<MeshRenderer>() : null;
        if (renderer != null)
            renderer.sharedMaterial = fill >= 0.99f ? _readyMaterial : _fillMaterial;
    }

    private void HideBar()
    {
        if (_barRoot != null)
            _barRoot.SetActive(false);
    }

    private void EnsureBar()
    {
        if (_barRoot != null) return;
        if (_effect == null) return;

        _backMaterial = CreateMaterial("M_CentrifugalCharge_Back", _effect.BarBackColor);
        _fillMaterial = CreateMaterial("M_CentrifugalCharge_Fill", _effect.BarFillColor);
        _readyMaterial = CreateMaterial("M_CentrifugalCharge_Ready", _effect.BarReadyColor);

        _barRoot = new GameObject("CentrifugalChargeBar");
        _barRoot.hideFlags = HideFlags.DontSave;

        Transform back = CreateBarQuad("Back", _barRoot.transform, _effect.BarSize, _backMaterial);
        _fill = CreateBarQuad("Fill", _barRoot.transform, _effect.BarSize, _fillMaterial);
        back.localPosition = Vector3.zero;
        _fill.localPosition = Vector3.zero;
        HideBar();
    }

    private static Transform CreateBarQuad(string name, Transform parent, Vector2 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(size.x, size.y, 0.012f);
        var col = go.GetComponent<Collider>();
        if (col != null)
            DestroyRuntimeObject(col);
        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer != null)
            renderer.sharedMaterial = material;
        return go.transform;
    }

    private static Material CreateMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        ConfigureTransparent(mat);
        mat.renderQueue = 3100;
        return mat;
    }

    private static void ConfigureTransparent(Material mat)
    {
        if (mat == null) return;
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
        mat.SetOverrideTag("RenderType", "Transparent");
    }

    private void DestroyBar()
    {
        DestroyRuntimeObject(_barRoot);
        DestroyRuntimeObject(_backMaterial);
        DestroyRuntimeObject(_fillMaterial);
        DestroyRuntimeObject(_readyMaterial);
        _barRoot = null;
        _fill = null;
        _backMaterial = null;
        _fillMaterial = null;
        _readyMaterial = null;
    }

    public static void DestroyRuntimeObject(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }
}

public class CentrifugalBoomerangRuntime : MonoBehaviour
{
    private const int RingSegments = 64;
    private const int ArcSegments = 18;
    private const float CollisionInfluenceSeconds = 0.48f;

    private PenEntity _owner;
    private Rigidbody _rb;
    private CentrifugalSpinEffect _effect;
    private Vector3 _home;
    private Vector3 _launchPosition;
    private Vector3 _launchDirection;
    private float _charge;
    private float _spinSign;
    private float _startedAt;
    private bool _returning;
    private GameObject _visualRoot;
    private LineRenderer _ringA;
    private LineRenderer _ringB;
    private LineRenderer _returnArc;
    private Material _ringMaterial;
    private Material _arcMaterial;
    private float _lastCollisionAt = -999f;

    public bool Active => _effect != null && _rb != null && Time.time - _startedAt <= _effect.BoomerangMaxDuration;

    public void Begin(
        PenEntity owner,
        CentrifugalSpinEffect effect,
        float charge,
        Vector3 launchDirection,
        Vector3 home,
        float spinSign)
    {
        _owner = owner;
        _rb = owner != null ? owner.rb : null;
        _effect = effect;
        _charge = Mathf.Clamp01(charge);
        _launchDirection = launchDirection;
        _launchDirection.y = 0f;
        if (_launchDirection.sqrMagnitude > 1e-6f)
            _launchDirection.Normalize();
        _home = home;
        _home.y = _rb != null ? _rb.worldCenterOfMass.y : home.y;
        _launchPosition = _rb != null ? _rb.worldCenterOfMass : home;
        _spinSign = Mathf.Approximately(spinSign, 0f) ? 1f : Mathf.Sign(spinSign);
        _startedAt = Time.time;
        _lastCollisionAt = -999f;
        _returning = false;
        enabled = true;

        EnsureVisuals();
        UpdateVisuals();
    }

    private void FixedUpdate()
    {
        if (!Active)
        {
            End();
            return;
        }

        if (_effect == null || _rb == null)
            return;

        float age = Time.time - _startedAt;
        Vector3 current = _rb.worldCenterOfMass;
        Vector3 flatFromHome = current - _home;
        flatFromHome.y = 0f;
        float distanceFromHome = flatFromHome.magnitude;

        if (!_returning && ShouldBeginReturn(age, distanceFromHome, current))
        {
            _returning = true;
        }

        ApplySpin();
        float collisionInfluence = GetRecentCollisionInfluence();

        if (!_returning)
        {
            ApplyOutwardCurl(collisionInfluence);
            return;
        }

        if (distanceFromHome <= _effect.BoomerangFinishDistance && age > _effect.BoomerangOutwardSeconds)
        {
            End();
            return;
        }

        Vector3 toHome = _home - current;
        toHome.y = 0f;
        if (toHome.sqrMagnitude < 1e-5f)
            return;

        Vector3 homeDirection = toHome.normalized;
        float distance01 = Mathf.InverseLerp(
            _effect.BoomerangFinishDistance,
            Mathf.Max(_effect.BoomerangFinishDistance + 0.1f, _effect.BoomerangTurnDistance * 1.85f),
            distanceFromHome);
        Vector3 flatVelocity = _rb.linearVelocity;
        flatVelocity.y = 0f;
        float awaySpeed = Mathf.Max(0f, -Vector3.Dot(flatVelocity, homeDirection));
        float desiredSpeed = _effect.BoomerangReturnSpeed
            * Mathf.Lerp(0.65f, 1.18f, _charge)
            * Mathf.Lerp(0.95f, 1.38f, distance01);
        float away01 = Mathf.InverseLerp(0f, Mathf.Max(0.1f, desiredSpeed), awaySpeed);
        Vector3 desiredVelocity = homeDirection * desiredSpeed;

        float responsiveness = _effect.BoomerangReturnResponsiveness
            * Mathf.Lerp(1.05f, 1.65f, distance01)
            * Mathf.Lerp(1f, 1.45f, away01)
            * Mathf.Lerp(1f, 0.42f, collisionInfluence);
        float maxAcceleration = _effect.BoomerangReturnAcceleration
            * Mathf.Lerp(1.1f, 2.05f, distance01)
            * Mathf.Lerp(1f, 1.7f, away01)
            * Mathf.Lerp(1f, 0.55f, collisionInfluence);
        Vector3 steering = (desiredVelocity - flatVelocity) * responsiveness;
        if (steering.magnitude > maxAcceleration)
            steering = steering.normalized * maxAcceleration;

        Vector3 travelDirection = GetTravelDirection(toHome.normalized);
        Vector3 curl = GetCurlDirection(travelDirection)
            * (_spinSign * _effect.BoomerangCurlAcceleration * _charge * Mathf.Lerp(1f, 0.55f, collisionInfluence));
        _rb.AddForce(steering + curl, ForceMode.Acceleration);
    }

    private void ApplyOutwardCurl(float collisionInfluence)
    {
        if (_effect == null || _rb == null || _launchDirection.sqrMagnitude < 1e-6f)
            return;

        float curlStrength = _effect.BoomerangCurlAcceleration
            * Mathf.Lerp(0.8f, 1.35f, _charge)
            * Mathf.Lerp(1f, 0.45f, collisionInfluence);
        Vector3 force = GetCurlDirection(_launchDirection) * (_spinSign * curlStrength);

        Vector3 flatVelocity = _rb.linearVelocity;
        flatVelocity.y = 0f;
        float forwardSpeed = Vector3.Dot(flatVelocity, _launchDirection);
        if (forwardSpeed < -0.05f)
        {
            float preserveForward = Mathf.Min(_effect.BoomerangReturnAcceleration * 0.35f, -forwardSpeed * 1.6f);
            force += _launchDirection * (preserveForward * Mathf.Lerp(1f, 0.25f, collisionInfluence));
        }

        _rb.AddForce(force, ForceMode.Acceleration);
    }

    private bool ShouldBeginReturn(float age, float distanceFromHome, Vector3 current)
    {
        if (_effect == null)
            return false;

        float outwardSeconds = Mathf.Max(0.05f, _effect.BoomerangOutwardSeconds);
        if (age < outwardSeconds)
            return false;

        float turnDistance = Mathf.Max(0.05f, _effect.BoomerangTurnDistance) * Mathf.Lerp(0.65f, 1.15f, _charge);
        float outwardProgress = GetOutwardProgress(current);
        float requiredForwardProgress = turnDistance * Mathf.Lerp(0.45f, 0.72f, _charge);
        if (outwardProgress >= requiredForwardProgress)
            return true;

        if (distanceFromHome >= turnDistance && outwardProgress >= turnDistance * 0.25f)
            return true;

        float fallbackDelay = Mathf.Min(0.9f, Mathf.Max(0.38f, _effect.BoomerangMaxDuration * 0.35f));
        return age >= outwardSeconds + fallbackDelay;
    }

    private float GetOutwardProgress(Vector3 current)
    {
        Vector3 flatFromLaunch = current - _launchPosition;
        flatFromLaunch.y = 0f;
        if (_launchDirection.sqrMagnitude < 1e-6f)
            return flatFromLaunch.magnitude;

        return Vector3.Dot(flatFromLaunch, _launchDirection);
    }

    private Vector3 GetTravelDirection(Vector3 fallback)
    {
        Vector3 flatVelocity = _rb != null ? _rb.linearVelocity : Vector3.zero;
        flatVelocity.y = 0f;
        if (flatVelocity.sqrMagnitude > 1e-5f)
            return flatVelocity.normalized;

        fallback.y = 0f;
        if (fallback.sqrMagnitude > 1e-5f)
            return fallback.normalized;

        return _launchDirection.sqrMagnitude > 1e-5f ? _launchDirection : Vector3.forward;
    }

    private static Vector3 GetCurlDirection(Vector3 travelDirection)
    {
        travelDirection.y = 0f;
        if (travelDirection.sqrMagnitude < 1e-5f)
            travelDirection = Vector3.forward;
        else
            travelDirection.Normalize();

        Vector3 curl = Vector3.Cross(Vector3.up, travelDirection);
        return curl.sqrMagnitude > 1e-5f ? curl.normalized : Vector3.right;
    }

    private void OnCollisionEnter(Collision collision) => RegisterCollision(collision);
    private void OnCollisionStay(Collision collision) => RegisterCollision(collision);

    private void RegisterCollision(Collision collision)
    {
        if (!Active || collision == null)
            return;

        _lastCollisionAt = Time.time;
    }

    private float GetRecentCollisionInfluence()
    {
        float age = Time.time - _lastCollisionAt;
        if (age >= CollisionInfluenceSeconds)
            return 0f;

        return 1f - Mathf.Clamp01(age / Mathf.Max(0.05f, CollisionInfluenceSeconds));
    }

    private void LateUpdate()
    {
        if (Active)
            UpdateVisuals();
        else
            End();
    }

    private void ApplySpin()
    {
        if (_effect == null || _rb == null) return;
        float torque = _effect.BoomerangSpinTorque * Mathf.Lerp(0.4f, 1f, _charge) * _spinSign;
        _rb.AddTorque(Vector3.up * torque, ForceMode.Acceleration);

        Vector3 angular = _rb.angularVelocity;
        angular.y = Mathf.Clamp(angular.y, -_effect.MaxYawAngularVelocity, _effect.MaxYawAngularVelocity);
        _rb.angularVelocity = angular;
    }

    private void EnsureVisuals()
    {
        if (_visualRoot != null) return;
        _ringMaterial = CreateMaterial("M_CentrifugalBoomerang_Ring", _effect != null ? _effect.SpinRingColor : Color.cyan);
        _arcMaterial = CreateMaterial("M_CentrifugalBoomerang_Arc", _effect != null ? _effect.ReturnArcColor : Color.yellow);

        _visualRoot = new GameObject("CentrifugalBoomerangVFX");
        _visualRoot.hideFlags = HideFlags.DontSave;
        _ringA = CreateLine("SpinRingA", _visualRoot.transform, _ringMaterial, true);
        _ringB = CreateLine("SpinRingB", _visualRoot.transform, _ringMaterial, true);
        _returnArc = CreateLine("ReturnArc", _visualRoot.transform, _arcMaterial, false);
    }

    private static LineRenderer CreateLine(string name, Transform parent, Material material, bool loop)
    {
        var go = new GameObject(name);
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = loop;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.material = material;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    private void UpdateVisuals()
    {
        if (_effect == null || _rb == null || _visualRoot == null) return;
        _visualRoot.SetActive(true);

        float age = Time.time - _startedAt;
        float life01 = Mathf.Clamp01(age / Mathf.Max(0.05f, _effect.BoomerangMaxDuration));
        float alpha = Mathf.Clamp01(1f - Mathf.SmoothStep(0.72f, 1f, life01));
        float pulse = 0.86f + 0.12f * Mathf.Sin(Time.time * 24f);
        GetVisualFrame(out Vector3 center, out Vector3 forward, out Vector3 side, out _);
        float radius = _effect.SpinRingRadius * Mathf.Lerp(0.72f, 1.18f, _charge) * pulse;

        UpdateRing(_ringA, center, forward, side, radius, Time.time * 220f * _spinSign, alpha);
        UpdateRing(_ringB, center, forward, side, radius * 0.72f, Time.time * -160f * _spinSign + 90f, alpha * 0.72f);
        UpdateReturnArc(center, alpha);
    }

    private void GetVisualFrame(out Vector3 center, out Vector3 forward, out Vector3 side, out Vector3 up)
    {
        up = Vector3.up;
        center = _rb != null ? _rb.worldCenterOfMass : transform.position;
        if (PenEffectVfxAlignment.TryGetEffectAnchor(_owner, _effect, out Vector3 anchor, out _, out Vector3 partUp, PartType.Cap))
        {
            center = anchor;
            if (Mathf.Abs(Vector3.Dot(partUp, Vector3.up)) > 0.25f)
                up = partUp;
        }

        forward = _owner != null ? _owner.GetPenAxis() : transform.right;
        forward -= up * Vector3.Dot(forward, up);
        if (forward.sqrMagnitude < 1e-5f)
            forward = Vector3.right;
        forward.Normalize();
        side = Vector3.Cross(up, forward);
        if (side.sqrMagnitude < 1e-5f)
            side = Vector3.forward;
        side.Normalize();
        center += up * 0.07f;
    }

    private void UpdateRing(LineRenderer line, Vector3 center, Vector3 forward, Vector3 side, float radius, float phaseDegrees, float alpha)
    {
        if (line == null || _effect == null) return;
        line.positionCount = RingSegments;
        line.startWidth = _effect.SpinRingWidth;
        line.endWidth = _effect.SpinRingWidth * 0.4f;
        Color color = _effect.SpinRingColor;
        color.a *= alpha;
        line.startColor = color;
        line.endColor = color;

        for (int i = 0; i < RingSegments; i++)
        {
            float a = i / (float)RingSegments * Mathf.PI * 2f;
            float phase = phaseDegrees * Mathf.Deg2Rad;
            float ca = Mathf.Cos(a + phase);
            float sa = Mathf.Sin(a + phase);
            Vector3 local = forward * (ca * radius) + side * (sa * radius * 0.56f);
            line.SetPosition(i, center + local);
        }
    }

    private void UpdateReturnArc(Vector3 current, float alpha)
    {
        if (_returnArc == null || _effect == null) return;

        _returnArc.enabled = _returning;
        if (!_returning) return;

        _returnArc.positionCount = ArcSegments;
        _returnArc.startWidth = _effect.ReturnArcWidth;
        _returnArc.endWidth = _effect.ReturnArcWidth * 0.35f;
        Color start = _effect.ReturnArcColor;
        Color end = _effect.ReturnArcColor;
        start.a *= alpha * 0.9f;
        end.a = 0f;
        _returnArc.startColor = start;
        _returnArc.endColor = end;

        Vector3 toHome = _home - current;
        toHome.y = 0f;
        Vector3 side = toHome.sqrMagnitude > 1e-5f
            ? Vector3.Cross(Vector3.up, toHome.normalized) * _spinSign
            : Vector3.right;
        float arcHeight = Mathf.Clamp(toHome.magnitude * 0.34f, 0.12f, 0.85f);

        for (int i = 0; i < ArcSegments; i++)
        {
            float t = i / (float)(ArcSegments - 1);
            Vector3 point = Vector3.Lerp(current, _home + Vector3.up * 0.07f, t);
            point += side * (Mathf.Sin(t * Mathf.PI) * arcHeight);
            point.y += Mathf.Sin(t * Mathf.PI) * 0.07f;
            _returnArc.SetPosition(i, point);
        }
    }

    private static Material CreateMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = 3150;
        return mat;
    }

    private void End()
    {
        if (_visualRoot != null)
            _visualRoot.SetActive(false);
        enabled = false;
    }

    public void CancelAssist()
    {
        End();
        _returning = false;
        _effect = null;
        _owner = null;
        _rb = null;
    }

    private void OnDestroy()
    {
        CentrifugalChargeRuntime.DestroyRuntimeObject(_visualRoot);
        CentrifugalChargeRuntime.DestroyRuntimeObject(_ringMaterial);
        CentrifugalChargeRuntime.DestroyRuntimeObject(_arcMaterial);
    }
}
