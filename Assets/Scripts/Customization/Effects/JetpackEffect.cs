using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Watches for a table edge after launch, then fires one short reverse-thrust burst to brake the pen.
/// </summary>
[CreateAssetMenu(fileName = "JetpackEffect", menuName = "GameData/Effects/Jetpack")]
public class JetpackEffect : PenPartEffect
{
    [Tooltip("Below this launch force the jetpack stays off, so tiny taps remain precise.")]
    [Range(0f, 1f)] public float MinForce = 0.08f;

    [Tooltip("How long after launch the jetpack watches for a table edge.")]
    public float Duration = 1.45f;

    [Tooltip("How much watch time remains when force barely passes MinForce.")]
    [Range(0.1f, 1f)] public float MinimumDurationFactor = 0.55f;

    [Header("Table Edge Retro-Thrust")]
    public float EdgeProbeDistance = 0.46f;
    public float EdgeProbeRadius = 0.12f;
    public float EdgeLookAheadTime = 0.14f;
    public float MinHorizontalSpeed = 0.45f;
    public float RetroDuration = 0.42f;
    [FormerlySerializedAs("RetroVelocityChange")] public float RetroThrustAcceleration = 10.5f;
    [FormerlySerializedAs("RetroBrakeAcceleration")] public float RetroVelocityDamping = 1.35f;
    [FormerlySerializedAs("MaxSpeedAfterKick")] public float MaxReturnSpeed = 2.6f;

    [Header("Exhaust Visual")]
    public Color ExhaustCoreColor = new(0.50f, 0.88f, 1f, 0.86f);
    public Color ExhaustHotColor = new(1f, 0.62f, 0.20f, 0.74f);
    public Color ExhaustSmokeColor = new(0.45f, 0.58f, 0.66f, 0.28f);
    public float ExhaustLength = 0.58f;
    public float ExhaustRadius = 0.12f;
    public float ExhaustRate = 68f;

    public override void OnLaunch(PenEffectContext context, Vector3 direction, float force)
    {
        if (context == null || context.Entity == null) return;
        if (force < MinForce) return;

        float force01 = Mathf.Clamp01(force);
        float duration = Duration * Mathf.Lerp(MinimumDurationFactor, 1f, force01);

        var runtime = context.Entity.GetComponent<JetpackEdgeRetroRuntime>();
        if (runtime == null)
            runtime = context.Entity.gameObject.AddComponent<JetpackEdgeRetroRuntime>();
        runtime.Begin(context.Entity, this, direction, duration,
            EdgeProbeDistance, EdgeProbeRadius, EdgeLookAheadTime, MinHorizontalSpeed,
            RetroDuration, RetroThrustAcceleration, RetroVelocityDamping, MaxReturnSpeed,
            ExhaustLength, ExhaustRadius, ExhaustRate,
            ExhaustCoreColor, ExhaustHotColor, ExhaustSmokeColor);
    }

    public override void OnDetached(PenEffectContext context)
    {
        if (context == null || context.Entity == null) return;
        var runtime = context.Entity.GetComponent<JetpackEdgeRetroRuntime>();
        if (runtime != null)
        {
            if (Application.isPlaying)
                Object.Destroy(runtime);
            else
                Object.DestroyImmediate(runtime);
        }
    }
}

public class JetpackEdgeRetroRuntime : MonoBehaviour
{
    private const float RayOriginHeight = 0.8f;
    private const float RayDistance = 3f;
    private const float SupportGraceTime = 0.12f;

    private PenEntity _owner;
    private PenPartEffect _sourceEffect;
    private Rigidbody _rb;
    private Vector3 _launchDirection;
    private Vector3 _retroForceDirection;
    private float _monitorEndsAt;
    private float _lastSupportedAt;
    private float _edgeProbeDistance;
    private float _edgeProbeRadius;
    private float _edgeLookAheadTime;
    private float _minHorizontalSpeed;
    private float _retroDuration;
    private float _retroThrustAcceleration;
    private float _retroVelocityDamping;
    private float _maxReturnSpeed;
    private float _retroStartedAt;
    private float _retroEndsAt;
    private float _exhaustLength;
    private float _exhaustRadius;
    private float _exhaustRate;
    private Color _exhaustCoreColor;
    private Color _exhaustHotColor;
    private Color _exhaustSmokeColor;
    private GameObject _exhaustRoot;
    private JetpackThrusterVfx _thrusterVfx;
    private bool _hasTriggered;

    public bool Active => _owner != null && _hasTriggered && Time.time < _retroEndsAt;

    public void Begin(
        PenEntity owner,
        PenPartEffect sourceEffect,
        Vector3 direction,
        float duration,
        float edgeProbeDistance,
        float edgeProbeRadius,
        float edgeLookAheadTime,
        float minHorizontalSpeed,
        float retroDuration,
        float retroThrustAcceleration,
        float retroVelocityDamping,
        float maxReturnSpeed,
        float exhaustLength,
        float exhaustRadius,
        float exhaustRate,
        Color exhaustCoreColor,
        Color exhaustHotColor,
        Color exhaustSmokeColor)
    {
        _owner = owner;
        _sourceEffect = sourceEffect;
        _rb = owner != null ? owner.rb : null;
        _launchDirection = PenEffectVfxAlignment.ProjectHorizontal(direction);
        if (_launchDirection.sqrMagnitude > 1e-6f)
            _launchDirection.Normalize();
        _monitorEndsAt = Time.time + Mathf.Max(0f, duration);
        _lastSupportedAt = float.NegativeInfinity;
        _edgeProbeDistance = Mathf.Max(0.05f, edgeProbeDistance);
        _edgeProbeRadius = Mathf.Max(0f, edgeProbeRadius);
        _edgeLookAheadTime = Mathf.Max(0f, edgeLookAheadTime);
        _minHorizontalSpeed = Mathf.Max(0.05f, minHorizontalSpeed);
        _retroDuration = Mathf.Max(0.05f, retroDuration);
        _retroThrustAcceleration = Mathf.Max(0f, retroThrustAcceleration);
        _retroVelocityDamping = Mathf.Max(0f, retroVelocityDamping);
        _maxReturnSpeed = Mathf.Max(0.1f, maxReturnSpeed);
        _retroStartedAt = 0f;
        _retroEndsAt = 0f;
        _retroForceDirection = Vector3.zero;
        _hasTriggered = false;
        _exhaustLength = Mathf.Max(0.08f, exhaustLength);
        _exhaustRadius = Mathf.Max(0.02f, exhaustRadius);
        _exhaustRate = Mathf.Max(0f, exhaustRate);
        _exhaustCoreColor = exhaustCoreColor;
        _exhaustHotColor = exhaustHotColor;
        _exhaustSmokeColor = exhaustSmokeColor;
        enabled = true;
    }

    private void FixedUpdate()
    {
        if (_owner == null || _rb == null)
        {
            StopRuntime();
            return;
        }

        if (Active)
        {
            ApplyRetroThrust();
            return;
        }

        if (_hasTriggered || Time.time >= _monitorEndsAt)
        {
            StopRuntime();
            return;
        }

        if (ShouldTriggerRetroThrust(out Vector3 forceDirection))
            StartRetroThrust(forceDirection);
    }

    private void LateUpdate()
    {
        if (Active) UpdateExhaustVisual();
        else if (_thrusterVfx != null)
        {
            _thrusterVfx.SetIntensity(0f);
        }
    }

    private bool ShouldTriggerRetroThrust(out Vector3 forceDirection)
    {
        forceDirection = Vector3.zero;

        Vector3 flatVelocity = PenEffectVfxAlignment.ProjectHorizontal(_rb.linearVelocity);
        if (flatVelocity.sqrMagnitude < _minHorizontalSpeed * _minHorizontalSpeed)
            return false;

        Vector3 center = _rb.worldCenterOfMass;
        bool supportedNow = HasSurfaceBelow(center);
        if (supportedNow)
            _lastSupportedAt = Time.time;
        bool recentlySupported = supportedNow || Time.time - _lastSupportedAt <= SupportGraceTime;
        if (!recentlySupported)
            return false;

        Vector3 travelDirection = flatVelocity.normalized;
        float lookAhead = Mathf.Max(_edgeProbeDistance, flatVelocity.magnitude * _edgeLookAheadTime);
        Vector3 ahead = center + travelDirection * lookAhead;
        Vector3 side = Vector3.Cross(Vector3.up, travelDirection);
        if (side.sqrMagnitude > 1e-6f)
            side = side.normalized * _edgeProbeRadius;

        int missingSamples = 0;
        if (!HasSurfaceBelow(ahead)) missingSamples++;
        if (_edgeProbeRadius > 0.001f)
        {
            if (!HasSurfaceBelow(ahead + side)) missingSamples++;
            if (!HasSurfaceBelow(ahead - side)) missingSamples++;
        }

        int requiredMissing = _edgeProbeRadius > 0.001f ? 2 : 1;
        if (missingSamples < requiredMissing)
            return false;

        forceDirection = -travelDirection;
        return true;
    }

    private bool HasSurfaceBelow(Vector3 samplePoint)
    {
        Vector3 origin = samplePoint + Vector3.up * RayOriginHeight;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, RayDistance, ~0, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.normal.y < 0.35f) continue;
            if (hit.collider.GetComponentInParent<PenEntity>() != null) continue;
            return true;
        }

        return false;
    }

    private void StartRetroThrust(Vector3 forceDirection)
    {
        if (forceDirection.sqrMagnitude < 1e-6f) return;

        _hasTriggered = true;
        _retroForceDirection = forceDirection.normalized;
        _retroStartedAt = Time.time;
        _retroEndsAt = Time.time + _retroDuration;

        EnsureExhaust();
        UpdateExhaustVisual();
    }

    private void ApplyRetroThrust()
    {
        Vector3 flatVelocity = PenEffectVfxAlignment.ProjectHorizontal(_rb.linearVelocity);
        float returnSpeed = Vector3.Dot(flatVelocity, _retroForceDirection);
        float elapsed01 = Mathf.Clamp01((Time.time - _retroStartedAt) / Mathf.Max(0.05f, _retroDuration));
        float thrustEnvelope = Mathf.Sin(elapsed01 * Mathf.PI);
        float speedLimitFade = Mathf.Clamp01((_maxReturnSpeed - returnSpeed) / Mathf.Max(0.1f, _maxReturnSpeed));
        float thrust = _retroThrustAcceleration * thrustEnvelope * speedLimitFade;

        if (thrust > 0.01f && _retroForceDirection.sqrMagnitude > 1e-6f)
        {
            if (TryGetNozzleFrame(out Vector3 nozzle, out _, out _))
                _rb.AddForceAtPosition(_retroForceDirection * thrust, nozzle, ForceMode.Acceleration);
            else
                _rb.AddForce(_retroForceDirection * thrust, ForceMode.Acceleration);
        }

        Vector3 lateralVelocity = flatVelocity - _retroForceDirection * returnSpeed;
        if (_retroVelocityDamping > 0f && lateralVelocity.sqrMagnitude > 0.001f)
            _rb.AddForce(-lateralVelocity * _retroVelocityDamping, ForceMode.Acceleration);
    }

    private void StopRuntime()
    {
        if (_thrusterVfx != null)
            _thrusterVfx.SetIntensity(0f);
        enabled = false;
    }

    private void UpdateExhaustVisual()
    {
        if (_owner == null || _thrusterVfx == null) return;
        if (!TryGetNozzleFrame(out Vector3 nozzle, out Vector3 exhaustDirection, out Vector3 up)) return;

        _exhaustRoot.transform.position = nozzle + exhaustDirection * 0.035f + up * 0.035f;
        _exhaustRoot.transform.rotation = Quaternion.LookRotation(exhaustDirection, up);

        float elapsed01 = Mathf.Clamp01((Time.time - _retroStartedAt) / Mathf.Max(0.05f, _retroDuration));
        float thrustEnvelope = Mathf.Sin(elapsed01 * Mathf.PI);
        float pulse = 0.78f + 0.22f * Mathf.Sin(Time.time * 38f);
        _thrusterVfx.SetIntensity(Mathf.Clamp01(pulse * Mathf.Lerp(0.22f, 1f, thrustEnvelope)));
    }

    private bool TryGetNozzleFrame(out Vector3 nozzle, out Vector3 exhaustDirection, out Vector3 up)
    {
        nozzle = transform.position;
        up = Vector3.up;

        Vector3 axis = _owner != null ? _owner.GetPenAxis() : transform.right;
        axis = PenEffectVfxAlignment.ProjectHorizontal(axis);
        if (axis.sqrMagnitude < 1e-6f)
            axis = PenEffectVfxAlignment.ProjectHorizontal(_launchDirection);
        if (axis.sqrMagnitude < 1e-6f)
            axis = Vector3.right;
        axis.Normalize();

        exhaustDirection = _retroForceDirection.sqrMagnitude > 1e-6f ? -_retroForceDirection.normalized : axis;

        if (PenEffectVfxAlignment.TryFindEffectPart(_owner, _sourceEffect, out var sourcePart, PartType.Accessory))
        {
            Bounds bounds = CalculateBounds(sourcePart.GameObject);
            float extent = ProjectedExtent(bounds.extents, exhaustDirection);
            nozzle = bounds.center + exhaustDirection * extent;

            Transform partTransform = sourcePart.GameObject != null ? sourcePart.GameObject.transform : null;
            if (partTransform != null && partTransform.up.sqrMagnitude > 1e-5f)
                up = Mathf.Abs(Vector3.Dot(partTransform.up.normalized, exhaustDirection)) > 0.92f
                    ? Vector3.up
                    : partTransform.up.normalized;
            return true;
        }

        Vector3 center = _rb != null ? _rb.worldCenterOfMass : transform.position;
        float halfLength = 0.45f;
        CapsuleCollider capsule = _owner != null ? _owner.penCollider : null;
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
            halfLength = Mathf.Max(0.08f, capsule.height * axisScale * 0.5f);
        }
        float sideSign = Vector3.Dot(axis, exhaustDirection) >= 0f ? 1f : -1f;
        nozzle = center + axis * halfLength * sideSign;
        return true;
    }

    private static Bounds CalculateBounds(GameObject root)
    {
        if (root == null)
            return new Bounds(Vector3.zero, Vector3.one * 0.05f);

        Collider[] colliders = root.GetComponentsInChildren<Collider>(false);
        if (colliders != null && colliders.Length > 0)
        {
            Bounds bounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    bounds.Encapsulate(colliders[i].bounds);
            }
            return bounds;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
        if (renderers != null && renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        return new Bounds(root.transform.position, Vector3.one * 0.05f);
    }

    private static float ProjectedExtent(Vector3 extents, Vector3 direction)
    {
        direction = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
        return Mathf.Max(0.02f, Vector3.Dot(extents, direction));
    }

    private void EnsureExhaust()
    {
        if (_exhaustRoot != null) return;
        _exhaustRoot = new GameObject("JetpackThrusterVFX");
        _exhaustRoot.hideFlags = HideFlags.DontSave;
        _thrusterVfx = _exhaustRoot.AddComponent<JetpackThrusterVfx>();
        _thrusterVfx.Configure(_exhaustCoreColor, _exhaustHotColor, _exhaustSmokeColor,
            _exhaustLength, _exhaustRadius, _exhaustRate);
    }

    private void OnDestroy()
    {
        DestroyRuntimeObject(_exhaustRoot);
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

public class JetpackThrusterVfx : MonoBehaviour
{
    private ParticleSystem _hotCore;
    private ParticleSystem _coldPlume;
    private ParticleSystem _smoke;
    private LineRenderer[] _streaks;
    private float _coreRate;
    private float _plumeRate;
    private float _smokeRate;
    private float _length;
    private float _radius;
    private Color _coreColor;
    private Color _hotColor;
    private Material _coreMaterial;
    private Material _plumeMaterial;
    private Material _smokeMaterial;
    private Material _streakMaterial;

    public void Configure(Color coreColor, Color hotColor, Color smokeColor, float length, float radius, float rate)
    {
        _coreRate = Mathf.Max(0f, rate * 0.65f);
        _plumeRate = Mathf.Max(0f, rate);
        _smokeRate = Mathf.Max(0f, rate * 0.28f);
        _length = Mathf.Max(0.1f, length);
        _radius = Mathf.Max(0.02f, radius);
        _coreColor = coreColor;
        _hotColor = hotColor;

        _coreMaterial = CreateParticleMaterial("M_Jetpack_HotCore", hotColor);
        _plumeMaterial = CreateParticleMaterial("M_Jetpack_BluePlume", coreColor);
        _smokeMaterial = CreateParticleMaterial("M_Jetpack_Smoke", smokeColor);
        _streakMaterial = CreateParticleMaterial("M_Jetpack_Streaks", coreColor);

        _hotCore = CreateSystem("HotCore", hotColor, 0.10f, 0.18f, length * 2.1f, radius * 0.28f, 8f, _coreRate, _coreMaterial);
        _coldPlume = CreateSystem("IonPlume", coreColor, 0.16f, 0.28f, length * 1.65f, radius * 0.58f, 14f, _plumeRate, _plumeMaterial);
        _smoke = CreateSystem("CoolingSmoke", smokeColor, 0.28f, 0.44f, length * 0.85f, radius * 0.9f, 22f, _smokeRate, _smokeMaterial);
        _streaks = CreateStreaks(_streakMaterial);

        SetIntensity(0f);
    }

    public void SetIntensity(float intensity)
    {
        float value = Mathf.Clamp01(intensity);
        SetEmission(_hotCore, _coreRate * value);
        SetEmission(_coldPlume, _plumeRate * value);
        SetEmission(_smoke, _smokeRate * value);

        if (value > 0.01f)
        {
            PlayIfNeeded(_hotCore);
            PlayIfNeeded(_coldPlume);
            PlayIfNeeded(_smoke);
        }
        else
        {
            StopIfNeeded(_hotCore);
            StopIfNeeded(_coldPlume);
            StopIfNeeded(_smoke);
        }

        UpdateStreaks(value);
    }

    public void SimulatePreview(float seconds)
    {
        _hotCore?.Simulate(seconds, true, true, true);
        _coldPlume?.Simulate(seconds, true, true, true);
        _smoke?.Simulate(seconds, true, true, true);
    }

    private ParticleSystem CreateSystem(
        string name,
        Color color,
        float minLifetime,
        float maxLifetime,
        float speed,
        float radius,
        float angle,
        float rate,
        Material material)
    {
        var go = new GameObject(name);
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(minLifetime, maxLifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.72f, speed * 1.18f);
        main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.42f, radius);
        main.startColor = color;
        main.maxParticles = 220;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = rate;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.radius = Mathf.Max(0.001f, radius);
        shape.angle = angle;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(CreateFadeGradient(color));

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, CreateSizeCurve());

        var velocityOverLifetime = ps.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.space = ParticleSystemSimulationSpace.Local;
        velocityOverLifetime.radial = new ParticleSystem.MinMaxCurve(radius * 0.8f, radius * 1.6f);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingFudge = 2f;

        return ps;
    }

    private LineRenderer[] CreateStreaks(Material material)
    {
        const int count = 7;
        var streaks = new LineRenderer[count];
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"ThrustStreak_{i:00}");
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.material = material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = 3;
            streaks[i] = line;
        }
        return streaks;
    }

    private void UpdateStreaks(float intensity)
    {
        if (_streaks == null) return;
        float value = Mathf.Clamp01(intensity);
        for (int i = 0; i < _streaks.Length; i++)
        {
            LineRenderer line = _streaks[i];
            if (line == null) continue;
            line.enabled = value > 0.01f;
            if (!line.enabled) continue;

            float phase = Time.time * 21f + i * 1.73f;
            float angle = i / (float)_streaks.Length * Mathf.PI * 2f + Mathf.Sin(phase) * 0.18f;
            float ring = _radius * (0.18f + 0.42f * Mathf.Abs(Mathf.Sin(i * 2.31f)));
            Vector3 start = new(Mathf.Cos(angle) * ring, Mathf.Sin(angle) * ring, 0f);
            Vector3 mid = start * 1.35f + Vector3.forward * (_length * (0.28f + 0.08f * Mathf.Sin(phase)));
            Vector3 end = start * (1.9f + 0.35f * Mathf.Sin(phase * 0.7f)) + Vector3.forward * (_length * (0.78f + 0.16f * Mathf.Sin(phase)));

            line.SetPosition(0, start);
            line.SetPosition(1, mid);
            line.SetPosition(2, end);
            float width = _radius * Mathf.Lerp(0.10f, 0.23f, value) * (0.72f + 0.28f * Mathf.Abs(Mathf.Sin(phase)));
            line.startWidth = width;
            line.endWidth = width * 0.08f;

            Color startColor = i % 3 == 0 ? _hotColor : _coreColor;
            Color endColor = _coreColor;
            startColor.a *= value;
            endColor.a = 0f;
            line.startColor = startColor;
            line.endColor = endColor;
        }
    }

    private static Gradient CreateFadeGradient(Color color)
    {
        var gradient = new Gradient();
        Color bright = color;
        bright.a = Mathf.Clamp01(color.a);
        Color fade = color;
        fade.a = 0f;
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(bright, 0f),
                new GradientColorKey(color, 0.42f),
                new GradientColorKey(color, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(bright.a, 0.12f),
                new GradientAlphaKey(color.a * 0.45f, 0.55f),
                new GradientAlphaKey(0f, 1f)
            });
        return gradient;
    }

    private static AnimationCurve CreateSizeCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0.18f),
            new Keyframe(0.16f, 1f),
            new Keyframe(1f, 0.22f));
    }

    private static void SetEmission(ParticleSystem ps, float rate)
    {
        if (ps == null) return;
        var emission = ps.emission;
        emission.rateOverTime = Mathf.Max(0f, rate);
    }

    private static void PlayIfNeeded(ParticleSystem ps)
    {
        if (ps != null && !ps.isPlaying)
            ps.Play(true);
    }

    private static void StopIfNeeded(ParticleSystem ps)
    {
        if (ps != null && ps.isPlaying)
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private static Material CreateParticleMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
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

    private void OnDestroy()
    {
        DestroyRuntimeObject(_coreMaterial);
        DestroyRuntimeObject(_plumeMaterial);
        DestroyRuntimeObject(_smokeMaterial);
        DestroyRuntimeObject(_streakMaterial);
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
