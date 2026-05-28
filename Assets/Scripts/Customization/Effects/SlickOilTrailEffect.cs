using UnityEngine;

/// <summary>
/// Paints slick oil onto the tabletop while the pen moves. The visual layer is
/// exclusively the Splatoon-style splat map; invisible trigger patches only
/// apply temporary low friction.
/// </summary>
[CreateAssetMenu(fileName = "SlickOilTrailEffect", menuName = "GameData/Effects/Slick Oil Trail")]
public class SlickOilTrailEffect : PenPartEffect
{
    [Header("Trail")]
    public float PatchRadius = 0.24f;
    public float SpawnDistance = 0.28f;
    public float MinSpeed = 0.2f;
    public float TrailDuration = 3.8f;
    public float PatchLifetime = 8f;
    public int MaxPatchCount = 26;

    [Header("Friction")]
    public PhysicsMaterial LowFrictionMaterial;
    public float FrictionDebuffDuration = 1.15f;
    public float OwnerGraceTime = 0.8f;
    [Range(0f, 1f)] public float OwnerFrictionDurationMultiplier = 0f;

    [Header("Splat Visual")]
    public Texture2D WaterNormalTexture;
    public Texture2D SecondNormalTexture;
    public Texture2D FoamTexture;
    public Color WaterShallowColor = new(0.08f, 0.42f, 0.31f, 0.94f);
    public Color WaterDeepColor = new(0.012f, 0.07f, 0.052f, 0.98f);
    public Color WaterFoamColor = new(0.58f, 1f, 0.74f, 0.22f);
    public float FoamScale = 3.4f;
    public Vector2 MainNormalScale = new(0.95f, 0.95f);
    public Vector2 SecondNormalScale = new(1.65f, 1.65f);
    public Vector2 WaveDirection = new(1f, 0.55f);
    public float WaveSpeed = 0.22f;
    [Range(0f, 1f)] public float Smoothness = 0.95f;
    [Range(0f, 1f)] public float SplatEdgeBump = 0.62f;
    [Range(0f, 1f)] public float SplatTileBump = 0.48f;
    [Range(0f, 1f)] public float LiquidThinFilmStrength = 0.025f;
    [Range(0f, 2f)] public float LiquidSpecularIntensity = 1.45f;
    [Range(0f, 1f)] public float LiquidHighlightStrength = 0.48f;

    [Header("Pixel Style")]
    [Range(0.015f, 0.18f)] public float PixelBlockWorldSize = 0.055f;
    [Range(0f, 1f)] public float PixelDitherStrength = 0.45f;
    [Range(1f, 16f)] public float PixelHighlightFps = 6f;

    [Header("Splat Map")]
    [Range(256, 2048)] public int SplatMapResolution = 1024;
    [Range(0.5f, 1.8f)] public float SplatStampScale = 1.0f;
    [Range(0f, 1f)] public float SplatMapOpacity = 0.86f;
    [Range(0, 12)] public int SplatSatelliteCount = 6;
    [Range(0f, 0.5f)] public float SplatEdgeKeepoutWidth = 0.24f;
    [Range(0f, 0.25f)] public float SplatEdgeKeepoutNoise = 0.09f;
    [Range(0.01f, 0.25f)] public float SplatEdgeKeepoutFeather = 0.1f;
    [Range(0f, 0.35f)] public float SplatEdgeFlowDepth = 0.16f;
    [Range(0f, 1f)] public float SplatEdgeFlowOpacity = 0.32f;
    [Min(1)] public int SplatLifetimeRounds = 1;
    [Range(0f, 4f)] public float SplatRoundFadeDuration = 1.35f;
    [Range(0.001f, 0.08f)] public float SplatSurfaceOffset = 0.018f;

    public override void OnLaunch(PenEffectContext context, Vector3 direction, float force)
    {
        if (context == null || context.Entity == null) return;
        var runtime = context.Entity.GetComponent<SlickOilTrailRuntime>();
        if (runtime == null)
            runtime = context.Entity.gameObject.AddComponent<SlickOilTrailRuntime>();
        runtime.Begin(context.Entity, this);
    }

    public override void OnDetached(PenEffectContext context)
    {
        if (context == null || context.Entity == null) return;
        var runtime = context.Entity.GetComponent<SlickOilTrailRuntime>();
        if (runtime != null)
            SlickOilPatch.DestroyRuntimeObject(runtime);
    }

    public override void OnRoundEnd(PenEffectContext context)
    {
        SlickOilSplatMap.AdvanceRound(SplatLifetimeRounds, SplatRoundFadeDuration);
        SlickOilPatch.DestroyAllRuntimePatches();
    }
}

public class SlickOilTrailRuntime : MonoBehaviour
{
    private PenEntity _owner;
    private SlickOilTrailEffect _effect;
    private Vector3 _lastSpawnPosition;
    private float _trailEndsAt;
    private int _spawnedCount;

    public void Begin(PenEntity owner, SlickOilTrailEffect effect)
    {
        _owner = owner;
        _effect = effect;
        _trailEndsAt = Time.time + Mathf.Max(0f, effect.TrailDuration);
        _spawnedCount = 0;
        _lastSpawnPosition = owner != null && owner.rb != null ? owner.rb.position : transform.position;
        TrySpawnPatch();
    }

    private void Update()
    {
        if (_owner == null || _owner.rb == null || _effect == null) return;
        if (Time.time > _trailEndsAt) return;
        if (_spawnedCount >= _effect.MaxPatchCount) return;
        if (_owner.rb.linearVelocity.magnitude < _effect.MinSpeed) return;

        Vector3 current = _owner.rb.position;
        Vector3 flatDelta = current - _lastSpawnPosition;
        flatDelta.y = 0f;
        if (flatDelta.magnitude < Mathf.Max(0.01f, _effect.SpawnDistance)) return;

        TrySpawnPatch();
        _lastSpawnPosition = current;
    }

    private void TrySpawnPatch()
    {
        Vector3 origin = _owner != null && _owner.rb != null ? _owner.rb.worldCenterOfMass : transform.position;
        if (PenEffectVfxAlignment.TryGetEffectAnchor(_owner, _effect, out Vector3 partAnchor, out _, out _, PartType.Refill))
            origin = partAnchor;

        if (!PenEffectVfxAlignment.TryProjectToSurface(origin, _owner, out Vector3 point, out Vector3 normal))
        {
            point = _owner != null && _owner.rb != null ? _owner.rb.position : transform.position;
            normal = Vector3.up;
        }

        Vector3 flowDirection = _owner != null && _owner.rb != null ? _owner.rb.linearVelocity : Vector3.zero;
        flowDirection -= normal * Vector3.Dot(flowDirection, normal);
        if (flowDirection.sqrMagnitude < 1e-5f && _owner != null)
            flowDirection = _owner.GetPenAxis();
        flowDirection -= normal * Vector3.Dot(flowDirection, normal);

        SlickOilSplatMap.PaintAt(point, normal, flowDirection, _effect);

        var patch = new GameObject("SlickOilFrictionPatch");
        patch.transform.position = point + normal * 0.008f;
        patch.transform.rotation = PenEffectVfxAlignment.SurfaceRotation(flowDirection, normal);

        var trigger = patch.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(_effect.PatchRadius * 2.2f, 0.08f, _effect.PatchRadius * 1.45f);

        var oil = patch.AddComponent<SlickOilPatch>();
        oil.Init(_owner, _effect.LowFrictionMaterial, _effect.FrictionDebuffDuration,
            _effect.PatchLifetime, _effect.OwnerGraceTime, _effect.OwnerFrictionDurationMultiplier);

        _spawnedCount++;
    }
}

public class SlickOilPatch : MonoBehaviour
{
    private PenEntity _owner;
    private PhysicsMaterial _lowFrictionMaterial;
    private float _duration;
    private float _createdAt;
    private float _lifetime;
    private float _ownerGraceTime;
    private float _ownerDurationMultiplier;

    public void Init(
        PenEntity owner,
        PhysicsMaterial lowFrictionMaterial,
        float duration,
        float lifetime,
        float ownerGraceTime,
        float ownerDurationMultiplier)
    {
        _owner = owner;
        _lowFrictionMaterial = lowFrictionMaterial != null ? lowFrictionMaterial : CreateFallbackLowFrictionMaterial();
        _duration = Mathf.Max(0.1f, duration);
        _lifetime = Mathf.Max(0.5f, lifetime);
        _ownerGraceTime = Mathf.Max(0f, ownerGraceTime);
        _ownerDurationMultiplier = Mathf.Clamp01(ownerDurationMultiplier);
        _createdAt = Time.time;
    }

    private void Update()
    {
        if (Time.time - _createdAt >= _lifetime)
            DestroyRuntimeObject(gameObject);
    }

    private void OnTriggerEnter(Collider other) => TryApply(other);
    private void OnTriggerStay(Collider other) => TryApply(other);

    private void TryApply(Collider other)
    {
        if (other == null) return;
        PenEntity pen = other.GetComponentInParent<PenEntity>();
        if (pen == null) return;
        bool isOwner = pen == _owner;
        if (isOwner) return;

        var modifier = pen.GetComponent<PenTemporaryFrictionModifier>();
        if (modifier == null)
            modifier = pen.gameObject.AddComponent<PenTemporaryFrictionModifier>();

        float duration = isOwner ? _duration * _ownerDurationMultiplier : _duration;
        if (duration > 0.05f)
            modifier.Apply(_lowFrictionMaterial, duration);
    }

    private static PhysicsMaterial CreateFallbackLowFrictionMaterial()
    {
        var material = new PhysicsMaterial("Slick Oil Low Friction")
        {
            dynamicFriction = 0.02f,
            staticFriction = 0.02f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };
        return material;
    }

    public static void DestroyRuntimeObject(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    public static void DestroyAllRuntimePatches(bool immediate = false)
    {
        SlickOilPatch[] patches = Resources.FindObjectsOfTypeAll<SlickOilPatch>();
        for (int i = 0; i < patches.Length; i++)
        {
            if (patches[i] == null) continue;
            Object target = patches[i].gameObject;
            if (Application.isPlaying && !immediate)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }
}
