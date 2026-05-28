using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Table-space paint buffer for Splatoon-style slick oil.
/// Visual ink accumulates in one texture mapped across the tabletop; invisible
/// SlickOilPatch triggers still handle temporary friction gameplay.
/// </summary>
public sealed class SlickOilSplatMap : MonoBehaviour
{
    private static readonly int SplatMapId = Shader.PropertyToID("_SplatMap");
    private static readonly int SplatColorId = Shader.PropertyToID("_SplatColor");
    private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
    private static readonly int FoamColorId = Shader.PropertyToID("_FoamColor");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int FoamTextureId = Shader.PropertyToID("_FoamTexture");
    private static readonly int FoamScaleId = Shader.PropertyToID("_FoamScale");
    private static readonly int NormalTextureId = Shader.PropertyToID("_NormalTexture");
    private static readonly int SecondNormalId = Shader.PropertyToID("_SecondNormal");
    private static readonly int MainNormalScaleId = Shader.PropertyToID("_MainNormalScale");
    private static readonly int SecondNormalScaleId = Shader.PropertyToID("_SecondNormalScale");
    private static readonly int WaveDirId = Shader.PropertyToID("_WaveDir");
    private static readonly int WaveSpeedId = Shader.PropertyToID("_WaveSpeed");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
    private static readonly int EdgeBumpId = Shader.PropertyToID("_SplatEdgeBump");
    private static readonly int TileBumpId = Shader.PropertyToID("_SplatTileBump");
    private static readonly int SpecularIntensityId = Shader.PropertyToID("_SpecularIntensity");
    private static readonly int ThinFilmStrengthId = Shader.PropertyToID("_ThinFilmStrength");
    private static readonly int HighlightStrengthId = Shader.PropertyToID("_HighlightStrength");
    private static readonly int PixelGridId = Shader.PropertyToID("_PixelGrid");
    private static readonly int DitherStrengthId = Shader.PropertyToID("_DitherStrength");
    private static readonly int HighlightFpsId = Shader.PropertyToID("_HighlightFps");
    private static readonly int SideFlowOpacityId = Shader.PropertyToID("_SideFlowOpacity");

    private const int MinimumResolution = 256;
    private const int MaximumResolution = 2048;
    private const float DefaultSurfacePadding = 0.03f;
    private const float SideFlowOutset = 0.065f;
    private const float SideFlowSampleInsetWorld = 0.12f;
    private const float SideFlowAlphaThreshold = 0.035f;
    private const float EdgeMaskPaintThreshold = 0.006f;

    private static SlickOilSplatMap _instance;

    private enum EdgePaintMode
    {
        RespectKeepout,
        IgnoreKeepout
    }

    private Bounds _surfaceBounds;
    private Texture2D _splatTexture;
    private Color32[] _pixels;
    private Mesh _surfaceMesh;
    private Mesh _sideFlowMesh;
    private MeshRenderer _renderer;
    private MeshRenderer _sideFlowRenderer;
    private MeshFilter _filter;
    private MeshFilter _sideFlowFilter;
    private Material _material;
    private Material _sideFlowMaterial;
    private int _resolution;
    private int _stampIndex;
    private float _edgeKeepoutWidth;
    private float _edgeKeepoutNoise;
    private float _edgeKeepoutFeather;
    private float _edgeFlowDepth;
    private float _sideFlowOpacity;
    private float _sideFlowPixelWorldSize;
    private Color _sideFlowDeepColor;
    private Color _sideFlowEdgeColor;
    private float _edgeNoiseSeed;
    private int _dirtyMinX;
    private int _dirtyMinY;
    private int _dirtyMaxX;
    private int _dirtyMaxY;
    private bool _hasDirtyPixels;
    private int _roundFadeStep = 128;
    private Color32[] _fadeSourcePixels;
    private Color32[] _fadeTargetPixels;
    private float _fadeStartedAt;
    private float _fadeDuration;
    private float _nextFadeUploadAt;
    private bool _roundFadeActive;

    private static int _lastRoundAdvanceFrame = -1;

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditorCleanup()
    {
        UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state != UnityEditor.PlayModeStateChange.ExitingPlayMode &&
            state != UnityEditor.PlayModeStateChange.EnteredEditMode)
            return;

        DestroyAllRuntimeMaps(immediate: true);
        SlickOilPatch.DestroyAllRuntimePatches(immediate: true);
    }
#endif

    public static bool PaintAt(Vector3 worldPosition, Vector3 surfaceNormal, Vector3 flowDirection, SlickOilTrailEffect effect)
    {
        if (effect == null)
            return false;

        SlickOilSplatMap map = Ensure(effect, worldPosition);
        if (map == null || map._splatTexture == null)
            return false;

        map.ConfigureMaterial(effect);
        map.ConfigureRoundFade(effect.SplatLifetimeRounds);
        map.PaintSplat(worldPosition, surfaceNormal, flowDirection, effect);
        return true;
    }

    public static void AdvanceRound(int lifetimeRounds, float fadeDuration = 1.25f)
    {
        if (_lastRoundAdvanceFrame == Time.frameCount)
            return;

        _lastRoundAdvanceFrame = Time.frameCount;
        int safeLifetime = Mathf.Max(1, lifetimeRounds);
        int fadeStep = Mathf.Clamp(Mathf.CeilToInt(255f / safeLifetime), 1, 255);
        SlickOilSplatMap[] maps = Resources.FindObjectsOfTypeAll<SlickOilSplatMap>();
        for (int i = 0; i < maps.Length; i++)
        {
            if (maps[i] == null) continue;
            maps[i].BeginRoundFade(fadeStep, fadeDuration);
        }
    }

    public static void DestroyAllRuntimeMaps(bool immediate = false)
    {
        SlickOilSplatMap[] maps = Resources.FindObjectsOfTypeAll<SlickOilSplatMap>();
        for (int i = 0; i < maps.Length; i++)
        {
            if (maps[i] == null) continue;
            DestroyRuntimeObject(maps[i].gameObject, immediate);
        }

        _instance = null;
        _lastRoundAdvanceFrame = -1;
    }

    public void Clear()
    {
        if (_pixels == null || _splatTexture == null) return;

        for (int i = 0; i < _pixels.Length; i++)
            _pixels[i] = new Color32(0, 0, 0, 0);

        _splatTexture.SetPixels32(_pixels);
        _splatTexture.Apply(false, false);
        if (_renderer != null)
            _renderer.enabled = false;
        ClearSideFlowMesh();
        _roundFadeActive = false;
        _fadeSourcePixels = null;
        _fadeTargetPixels = null;
    }

    private static SlickOilSplatMap Ensure(SlickOilTrailEffect effect, Vector3 fallbackPoint)
    {
        if (_instance == null)
            _instance = FindFirstObjectByType<SlickOilSplatMap>();

        if (_instance == null)
        {
            var go = new GameObject("SlickOilSplatMap_Runtime");
            go.hideFlags = HideFlags.DontSave;
            _instance = go.AddComponent<SlickOilSplatMap>();
        }

        int resolution = Mathf.Clamp(effect.SplatMapResolution, MinimumResolution, MaximumResolution);
        if (!_instance.TryResolveTableSurface(fallbackPoint, out Bounds bounds))
            return null;

        bool needsRebuild = _instance._splatTexture == null
            || _instance._resolution != resolution
            || !Mathf.Approximately(_instance._edgeFlowDepth, Mathf.Max(0f, effect.SplatEdgeFlowDepth))
            || BoundsChanged(_instance._surfaceBounds, bounds);

        if (needsRebuild)
            _instance.Build(bounds, resolution, effect);

        return _instance;
    }

    private void ConfigureRoundFade(int lifetimeRounds)
    {
        int safeLifetime = Mathf.Max(1, lifetimeRounds);
        _roundFadeStep = Mathf.Clamp(Mathf.CeilToInt(255f / safeLifetime), 1, 255);
    }

    private void Update()
    {
        if (_roundFadeActive)
            UpdateRoundFade();
    }

    private static bool BoundsChanged(Bounds a, Bounds b)
    {
        return (a.center - b.center).sqrMagnitude > 0.0001f || (a.size - b.size).sqrMagnitude > 0.0001f;
    }

    private bool TryResolveTableSurface(Vector3 fallbackPoint, out Bounds bounds)
    {
        BoxCollider tableCollider = FindTableCollider();
        if (tableCollider != null)
        {
            bounds = tableCollider.bounds;
            if (TryFindTableVisualSurface(tableCollider, out Bounds visualBounds))
            {
                Vector3 min = bounds.min;
                Vector3 max = bounds.max;
                min.x = Mathf.Min(min.x, visualBounds.min.x);
                min.z = Mathf.Min(min.z, visualBounds.min.z);
                max.x = Mathf.Max(max.x, visualBounds.max.x);
                max.z = Mathf.Max(max.z, visualBounds.max.z);
                max.y = Mathf.Max(max.y, visualBounds.max.y);
                bounds.SetMinMax(min, max);
            }
            bounds.Expand(new Vector3(DefaultSurfacePadding, 0f, DefaultSurfacePadding));
            return bounds.size.x > 0.1f && bounds.size.z > 0.1f;
        }

        bounds = new Bounds(new Vector3(fallbackPoint.x, fallbackPoint.y - 0.02f, fallbackPoint.z), new Vector3(8f, 0.08f, 5f));
        return true;
    }

    private static BoxCollider FindTableCollider()
    {
        GameObject table = GameObject.Find("Table");
        if (table == null)
            return null;

        BoxCollider best = null;
        float bestArea = 0f;
        BoxCollider[] colliders = table.GetComponentsInChildren<BoxCollider>(false);
        for (int i = 0; i < colliders.Length; i++)
        {
            BoxCollider candidate = colliders[i];
            if (candidate == null || !candidate.enabled || candidate.isTrigger)
                continue;

            Bounds b = candidate.bounds;
            float area = Mathf.Max(0f, b.size.x) * Mathf.Max(0f, b.size.z);
            if (area <= bestArea)
                continue;

            best = candidate;
            bestArea = area;
        }

        return best;
    }

    private static bool TryFindTableVisualSurface(BoxCollider topCollider, out Bounds visualBounds)
    {
        visualBounds = default;
        GameObject table = GameObject.Find("Table");
        if (table == null || topCollider == null)
            return false;

        Renderer[] renderers = table.GetComponentsInChildren<Renderer>(false);
        float topY = topCollider.bounds.max.y;
        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            Bounds candidate = renderer.bounds;
            if (candidate.size.x < 0.1f || candidate.size.z < 0.1f)
                continue;
            if (candidate.max.y < topY - 0.25f)
                continue;

            if (!hasBounds)
            {
                visualBounds = candidate;
                hasBounds = true;
            }
            else
            {
                visualBounds.Encapsulate(candidate);
            }
        }

        return hasBounds;
    }

    private void Build(Bounds surfaceBounds, int resolution, SlickOilTrailEffect effect)
    {
        _surfaceBounds = surfaceBounds;
        _resolution = resolution;
        _edgeKeepoutWidth = Mathf.Max(0f, effect.SplatEdgeKeepoutWidth);
        _edgeKeepoutNoise = Mathf.Max(0f, effect.SplatEdgeKeepoutNoise);
        _edgeKeepoutFeather = Mathf.Max(0.01f, effect.SplatEdgeKeepoutFeather);
        _edgeFlowDepth = Mathf.Max(0f, effect.SplatEdgeFlowDepth);
        _sideFlowOpacity = Mathf.Clamp01(effect.SplatEdgeFlowOpacity);
        _sideFlowPixelWorldSize = Mathf.Max(0.025f, effect.PixelBlockWorldSize);
        _sideFlowDeepColor = effect.WaterDeepColor;
        _sideFlowEdgeColor = effect.WaterShallowColor;
        _edgeNoiseSeed = ComputeEdgeNoiseSeed(surfaceBounds);
        _stampIndex = 0;
        EnsureComponents();
        BuildTexture(resolution);
        BuildMesh(effect);
        ConfigureMaterial(effect);
    }

    private void EnsureComponents()
    {
        if (_filter == null)
        {
            _filter = gameObject.GetComponent<MeshFilter>();
            if (_filter == null)
                _filter = gameObject.AddComponent<MeshFilter>();
        }
        if (_renderer == null)
        {
            _renderer = gameObject.GetComponent<MeshRenderer>();
            if (_renderer == null)
                _renderer = gameObject.AddComponent<MeshRenderer>();
        }

        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.allowOcclusionWhenDynamic = false;
    }

    private void BuildTexture(int resolution)
    {
        DestroyRuntimeObject(_splatTexture);
        _splatTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
        {
            name = "T_SlickOil_SplatMap_Runtime",
            hideFlags = HideFlags.DontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        _pixels = new Color32[resolution * resolution];
        Clear();
    }

    private void BuildMesh(SlickOilTrailEffect effect)
    {
        DestroyRuntimeObject(_surfaceMesh);

        float y = _surfaceBounds.max.y + Mathf.Max(0.001f, effect.SplatSurfaceOffset);
        Vector3 min = _surfaceBounds.min;
        Vector3 max = _surfaceBounds.max;
        _surfaceMesh = new Mesh { name = "SlickOilSplatMap_Surface", hideFlags = HideFlags.DontSave };
        _surfaceMesh.vertices = new[]
        {
            new Vector3(min.x, y, min.z),
            new Vector3(max.x, y, min.z),
            new Vector3(max.x, y, max.z),
            new Vector3(min.x, y, max.z)
        };
        _surfaceMesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        _surfaceMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        _surfaceMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        _surfaceMesh.RecalculateNormals();
        _surfaceMesh.RecalculateBounds();
        _filter.sharedMesh = _surfaceMesh;
    }

    private void ConfigureMaterial(SlickOilTrailEffect effect)
    {
        _edgeKeepoutWidth = Mathf.Max(0f, effect.SplatEdgeKeepoutWidth);
        _edgeKeepoutNoise = Mathf.Max(0f, effect.SplatEdgeKeepoutNoise);
        _edgeKeepoutFeather = Mathf.Max(0.01f, effect.SplatEdgeKeepoutFeather);
        _edgeFlowDepth = Mathf.Max(0f, effect.SplatEdgeFlowDepth);
        _sideFlowOpacity = Mathf.Clamp01(effect.SplatEdgeFlowOpacity);
        _sideFlowPixelWorldSize = Mathf.Max(0.025f, effect.PixelBlockWorldSize);
        _sideFlowDeepColor = effect.WaterDeepColor;
        _sideFlowEdgeColor = effect.WaterShallowColor;

        if (_material == null)
        {
            Shader shader = Shader.Find("Pen/Slick Oil Splat Map");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            _material = new Material(shader)
            {
                name = "M_SlickOil_SplatMap_Runtime",
                hideFlags = HideFlags.DontSave,
                renderQueue = 3060
            };
            _renderer.sharedMaterial = _material;
        }

        if (_material.HasProperty(SplatMapId)) _material.SetTexture(SplatMapId, _splatTexture);
        if (_material.HasProperty(SplatColorId)) _material.SetColor(SplatColorId, effect.WaterDeepColor);
        if (_material.HasProperty(EdgeColorId)) _material.SetColor(EdgeColorId, effect.WaterShallowColor);
        if (_material.HasProperty(FoamColorId)) _material.SetColor(FoamColorId, effect.WaterFoamColor);
        if (_material.HasProperty(AlphaId)) _material.SetFloat(AlphaId, Mathf.Clamp01(effect.SplatMapOpacity));
        if (_material.HasProperty(FoamTextureId) && effect.FoamTexture != null) _material.SetTexture(FoamTextureId, effect.FoamTexture);
        if (_material.HasProperty(NormalTextureId) && effect.WaterNormalTexture != null) _material.SetTexture(NormalTextureId, effect.WaterNormalTexture);
        if (_material.HasProperty(SecondNormalId))
            _material.SetTexture(SecondNormalId, effect.SecondNormalTexture != null ? effect.SecondNormalTexture : effect.WaterNormalTexture);
        if (_material.HasProperty(FoamScaleId)) _material.SetFloat(FoamScaleId, Mathf.Max(0.01f, effect.FoamScale));
        if (_material.HasProperty(MainNormalScaleId)) _material.SetVector(MainNormalScaleId, new Vector4(effect.MainNormalScale.x, effect.MainNormalScale.y, 0f, 0f));
        if (_material.HasProperty(SecondNormalScaleId)) _material.SetVector(SecondNormalScaleId, new Vector4(effect.SecondNormalScale.x, effect.SecondNormalScale.y, 0f, 0f));
        if (_material.HasProperty(WaveDirId)) _material.SetVector(WaveDirId, new Vector4(effect.WaveDirection.x, effect.WaveDirection.y, 0f, 0f));
        if (_material.HasProperty(WaveSpeedId)) _material.SetFloat(WaveSpeedId, effect.WaveSpeed);
        if (_material.HasProperty(SmoothnessId)) _material.SetFloat(SmoothnessId, Mathf.Clamp01(effect.Smoothness));
        if (_material.HasProperty(EdgeBumpId)) _material.SetFloat(EdgeBumpId, Mathf.Clamp01(effect.SplatEdgeBump));
        if (_material.HasProperty(TileBumpId)) _material.SetFloat(TileBumpId, Mathf.Clamp01(effect.SplatTileBump));
        if (_material.HasProperty(SpecularIntensityId)) _material.SetFloat(SpecularIntensityId, effect.LiquidSpecularIntensity);
        if (_material.HasProperty(ThinFilmStrengthId)) _material.SetFloat(ThinFilmStrengthId, effect.LiquidThinFilmStrength);
        if (_material.HasProperty(HighlightStrengthId)) _material.SetFloat(HighlightStrengthId, Mathf.Clamp01(effect.LiquidHighlightStrength));
        if (_material.HasProperty(SideFlowOpacityId)) _material.SetFloat(SideFlowOpacityId, Mathf.Clamp01(effect.SplatEdgeFlowOpacity));
        if (_material.HasProperty(PixelGridId))
        {
            float pixelWorld = Mathf.Max(0.005f, effect.PixelBlockWorldSize);
            _material.SetVector(PixelGridId, new Vector4(
                Mathf.Max(1f, _surfaceBounds.size.x / pixelWorld),
                Mathf.Max(1f, _surfaceBounds.size.z / pixelWorld),
                0f,
                0f));
        }
        if (_material.HasProperty(DitherStrengthId)) _material.SetFloat(DitherStrengthId, Mathf.Clamp01(effect.PixelDitherStrength));
        if (_material.HasProperty(HighlightFpsId)) _material.SetFloat(HighlightFpsId, Mathf.Max(1f, effect.PixelHighlightFps));

        ConfigureSideFlowMaterial();
    }

    private void ConfigureSideFlowMaterial()
    {
        if (_edgeFlowDepth <= 0.001f || _sideFlowOpacity <= 0.001f)
        {
            if (_sideFlowRenderer != null)
                _sideFlowRenderer.enabled = false;
            return;
        }

        EnsureSideFlowComponents();
        if (_sideFlowMaterial == null)
        {
            Shader shader = Shader.Find("Pen/Slick Oil Side Flow");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            _sideFlowMaterial = new Material(shader)
            {
                name = "M_SlickOil_SideFlow_Runtime",
                hideFlags = HideFlags.DontSave,
                renderQueue = 3070
            };
            _sideFlowRenderer.sharedMaterial = _sideFlowMaterial;
        }
    }

    private void EnsureSideFlowComponents()
    {
        if (_sideFlowFilter != null && _sideFlowRenderer != null)
            return;

        Transform child = transform.Find("SlickOilSideFlow");
        GameObject sideObject = child != null ? child.gameObject : new GameObject("SlickOilSideFlow");
        sideObject.hideFlags = HideFlags.DontSave;
        sideObject.transform.SetParent(transform, false);
        sideObject.transform.localPosition = Vector3.zero;
        sideObject.transform.localRotation = Quaternion.identity;
        sideObject.transform.localScale = Vector3.one;

        _sideFlowFilter = sideObject.GetComponent<MeshFilter>();
        if (_sideFlowFilter == null)
            _sideFlowFilter = sideObject.AddComponent<MeshFilter>();

        _sideFlowRenderer = sideObject.GetComponent<MeshRenderer>();
        if (_sideFlowRenderer == null)
            _sideFlowRenderer = sideObject.AddComponent<MeshRenderer>();

        _sideFlowRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _sideFlowRenderer.receiveShadows = false;
        _sideFlowRenderer.allowOcclusionWhenDynamic = false;
    }

    private void RebuildSideFlowMesh()
    {
        if (_pixels == null || _resolution <= 0 || _edgeFlowDepth <= 0.001f || _sideFlowOpacity <= 0.001f)
        {
            ClearSideFlowMesh();
            return;
        }

        EnsureSideFlowComponents();
        if (_sideFlowMesh == null)
        {
            _sideFlowMesh = new Mesh
            {
                name = "SlickOilSideFlow_Surface",
                hideFlags = HideFlags.DontSave,
                indexFormat = IndexFormat.UInt32
            };
            _sideFlowFilter.sharedMesh = _sideFlowMesh;
        }
        else
        {
            _sideFlowMesh.Clear();
        }

        var vertices = new List<Vector3>(512);
        var colors = new List<Color>(512);
        var triangles = new List<int>(768);
        float topY = _surfaceBounds.max.y + 0.004f;
        float depth = Mathf.Max(0.01f, _edgeFlowDepth);
        float pixelWorld = Mathf.Max(0.025f, _sideFlowPixelWorldSize);
        int segmentsX = Mathf.Clamp(Mathf.CeilToInt(_surfaceBounds.size.x / pixelWorld), 8, 192);
        int segmentsZ = Mathf.Clamp(Mathf.CeilToInt(_surfaceBounds.size.z / pixelWorld), 8, 192);
        int insetX = Mathf.Clamp(Mathf.CeilToInt(SideFlowSampleInsetWorld / Mathf.Max(0.01f, _surfaceBounds.size.x) * _resolution), 1, Mathf.Max(2, _resolution / 8));
        int insetY = Mathf.Clamp(Mathf.CeilToInt(SideFlowSampleInsetWorld / Mathf.Max(0.01f, _surfaceBounds.size.z) * _resolution), 1, Mathf.Max(2, _resolution / 8));

        for (int i = 0; i < segmentsX; i++)
        {
            int x0 = Mathf.FloorToInt((i / (float)segmentsX) * (_resolution - 1));
            int x1 = Mathf.CeilToInt(((i + 1) / (float)segmentsX) * (_resolution - 1));
            float worldX0 = Mathf.Lerp(_surfaceBounds.min.x, _surfaceBounds.max.x, i / (float)segmentsX);
            float worldX1 = Mathf.Lerp(_surfaceBounds.min.x, _surfaceBounds.max.x, (i + 1) / (float)segmentsX);

            float frontAlpha = SampleAlphaBand(x0, x1, 0, insetY);
            AddSideFlowQuad(vertices, colors, triangles,
                new Vector3(worldX0, topY, _surfaceBounds.min.z - SideFlowOutset),
                new Vector3(worldX1, topY, _surfaceBounds.min.z - SideFlowOutset),
                depth, frontAlpha, i, 0);

            float backAlpha = SampleAlphaBand(x0, x1, _resolution - insetY - 1, _resolution - 1);
            AddSideFlowQuad(vertices, colors, triangles,
                new Vector3(worldX1, topY, _surfaceBounds.max.z + SideFlowOutset),
                new Vector3(worldX0, topY, _surfaceBounds.max.z + SideFlowOutset),
                depth, backAlpha, i, 1);
        }

        for (int i = 0; i < segmentsZ; i++)
        {
            int y0 = Mathf.FloorToInt((i / (float)segmentsZ) * (_resolution - 1));
            int y1 = Mathf.CeilToInt(((i + 1) / (float)segmentsZ) * (_resolution - 1));
            float worldZ0 = Mathf.Lerp(_surfaceBounds.min.z, _surfaceBounds.max.z, i / (float)segmentsZ);
            float worldZ1 = Mathf.Lerp(_surfaceBounds.min.z, _surfaceBounds.max.z, (i + 1) / (float)segmentsZ);

            float rightAlpha = SampleAlphaBand(_resolution - insetX - 1, _resolution - 1, y0, y1);
            AddSideFlowQuad(vertices, colors, triangles,
                new Vector3(_surfaceBounds.max.x + SideFlowOutset, topY, worldZ0),
                new Vector3(_surfaceBounds.max.x + SideFlowOutset, topY, worldZ1),
                depth, rightAlpha, i, 2);

            float leftAlpha = SampleAlphaBand(0, insetX, y0, y1);
            AddSideFlowQuad(vertices, colors, triangles,
                new Vector3(_surfaceBounds.min.x - SideFlowOutset, topY, worldZ1),
                new Vector3(_surfaceBounds.min.x - SideFlowOutset, topY, worldZ0),
                depth, leftAlpha, i, 3);
        }

        if (vertices.Count == 0)
        {
            ClearSideFlowMesh();
            return;
        }

        _sideFlowMesh.SetVertices(vertices);
        _sideFlowMesh.SetColors(colors);
        _sideFlowMesh.SetTriangles(triangles, 0);
        _sideFlowMesh.RecalculateBounds();
        _sideFlowRenderer.enabled = true;
    }

    private float SampleAlphaBand(int x0, int x1, int y0, int y1)
    {
        x0 = Mathf.Clamp(x0, 0, _resolution - 1);
        x1 = Mathf.Clamp(x1, 0, _resolution - 1);
        y0 = Mathf.Clamp(y0, 0, _resolution - 1);
        y1 = Mathf.Clamp(y1, 0, _resolution - 1);
        if (x1 < x0 || y1 < y0)
            return 0f;

        int count = 0;
        float maxAlpha = 0f;
        float sumAlpha = 0f;
        for (int y = y0; y <= y1; y++)
        {
            int row = y * _resolution;
            for (int x = x0; x <= x1; x++)
            {
                float alpha = _pixels[row + x].a / 255f;
                if (alpha <= 0f)
                    continue;

                count++;
                sumAlpha += alpha;
                if (alpha > maxAlpha)
                    maxAlpha = alpha;
            }
        }

        if (count == 0)
            return 0f;

        float average = sumAlpha / count;
        return Mathf.Clamp01(maxAlpha * 0.72f + average * 0.28f);
    }

    private void AddSideFlowQuad(
        List<Vector3> vertices,
        List<Color> colors,
        List<int> triangles,
        Vector3 topLeft,
        Vector3 topRight,
        float depth,
        float alpha,
        int segmentIndex,
        int edgeIndex)
    {
        if (alpha <= SideFlowAlphaThreshold)
            return;

        Vector3 along = topRight - topLeft;
        float segmentLength = along.magnitude;
        if (segmentLength <= 0.001f)
            return;

        along /= segmentLength;
        float rimCoverage = Mathf.Lerp(0.55f, 0.95f, Mathf.Sqrt(alpha));
        float rimInset = segmentLength * (1f - rimCoverage) * 0.5f;
        Vector3 rimLeft = topLeft + along * rimInset;
        Vector3 rimRight = topRight - along * rimInset;
        float rimHeight = Mathf.Min(depth * 0.18f, 0.055f) * Mathf.Lerp(0.72f, 1.15f, alpha);
        float rimTopAlpha = Mathf.Clamp01(alpha * _sideFlowOpacity * 0.50f);
        Color rimTop = Color.Lerp(_sideFlowDeepColor, _sideFlowEdgeColor, 0.08f);
        Color rimBottom = _sideFlowDeepColor;
        rimTop.a = rimTopAlpha;
        rimBottom.a = rimTopAlpha * 0.22f;
        AppendSideFlowQuad(vertices, colors, triangles,
            rimLeft,
            rimRight,
            rimRight + Vector3.down * rimHeight,
            rimLeft + Vector3.down * rimHeight,
            rimTop,
            rimTop,
            rimBottom,
            rimBottom);

        float dripChance = Mathf.Lerp(0.12f, 0.72f, Mathf.Clamp01(alpha));
        if (Hash01(segmentIndex + 71, edgeIndex + 23, 29) > dripChance)
            return;

        float widthFraction = Mathf.Lerp(0.22f, 0.58f, Hash01(segmentIndex + 101, edgeIndex + 5, 13));
        float dripWidth = segmentLength * widthFraction;
        float center = Mathf.Lerp(dripWidth * 0.5f, segmentLength - dripWidth * 0.5f, Hash01(segmentIndex + 37, edgeIndex + 41, 17));
        Vector3 dripLeft = topLeft + along * (center - dripWidth * 0.5f);
        Vector3 dripRight = topLeft + along * (center + dripWidth * 0.5f);
        float startDrop = rimHeight * Mathf.Lerp(0.15f, 0.95f, Hash01(segmentIndex + 31, edgeIndex + 11, 43));
        float dripHeight = depth * Mathf.Lerp(0.26f, 0.95f, Mathf.Pow(alpha, 0.7f))
            * Mathf.Lerp(0.72f, 1.22f, Hash01(segmentIndex + 17, edgeIndex + 3, 11));
        float dripTopAlpha = Mathf.Clamp01(alpha * _sideFlowOpacity * 0.58f);
        Color dripTop = Color.Lerp(_sideFlowDeepColor, _sideFlowEdgeColor, 0.035f);
        Color dripBottom = _sideFlowDeepColor;
        dripTop.a = dripTopAlpha;
        dripBottom.a = dripTopAlpha * Mathf.Lerp(0.04f, 0.16f, Hash01(segmentIndex + 53, edgeIndex + 7, 19));
        AppendSideFlowQuad(vertices, colors, triangles,
            dripLeft + Vector3.down * startDrop,
            dripRight + Vector3.down * startDrop,
            dripRight + Vector3.down * dripHeight,
            dripLeft + Vector3.down * dripHeight,
            dripTop,
            dripTop,
            dripBottom,
            dripBottom);
    }

    private static void AppendSideFlowQuad(
        List<Vector3> vertices,
        List<Color> colors,
        List<int> triangles,
        Vector3 topLeft,
        Vector3 topRight,
        Vector3 bottomRight,
        Vector3 bottomLeft,
        Color topLeftColor,
        Color topRightColor,
        Color bottomRightColor,
        Color bottomLeftColor)
    {
        int start = vertices.Count;
        vertices.Add(topLeft);
        vertices.Add(topRight);
        vertices.Add(bottomRight);
        vertices.Add(bottomLeft);
        colors.Add(topLeftColor);
        colors.Add(topRightColor);
        colors.Add(bottomRightColor);
        colors.Add(bottomLeftColor);
        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    private void ClearSideFlowMesh()
    {
        if (_sideFlowMesh != null)
            _sideFlowMesh.Clear();
        if (_sideFlowRenderer != null)
            _sideFlowRenderer.enabled = false;
    }

    private void PaintSplat(Vector3 worldPosition, Vector3 surfaceNormal, Vector3 flowDirection, SlickOilTrailEffect effect)
    {
        if (_renderer != null)
            _renderer.enabled = true;
        if (_roundFadeActive)
            CommitRoundFadeNow();

        Vector2 forward = new Vector2(flowDirection.x, flowDirection.z);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector2.right;
        forward.Normalize();

        float radius = Mathf.Max(0.03f, effect.PatchRadius * Mathf.Max(0.05f, effect.SplatStampScale));
        float speedStretch = Mathf.Clamp(flowDirection.magnitude * 0.035f, 0f, 0.34f);
        int seed = ++_stampIndex * 92821 + Mathf.Abs(Mathf.RoundToInt(worldPosition.x * 113f + worldPosition.z * 271f));
        float mainRadiusAlong = radius * (1.22f + speedStretch);
        float mainRadiusSide = radius * 0.78f;
        Vector3 originalCenter = worldPosition;
        Vector3 paintCenter = ConstrainCenterToEdgeKeepout(originalCenter, Mathf.Max(mainRadiusAlong, mainRadiusSide), seed, out Vector3 edgeNormal, out float edgePressure);

        ResetDirtyRect();
        if (edgePressure > 0.01f && edgeNormal.sqrMagnitude > 0.0001f)
        {
            Vector3 tangent = new Vector3(-edgeNormal.z, 0f, edgeNormal.x).normalized;
            float pressure = Smooth01(0f, 0.85f, edgePressure);
            float inwardJitter = radius * Mathf.Lerp(0.12f, 0.62f, pressure)
                * Mathf.Lerp(0.55f, 1.35f, Hash01(seed + 83, 5, 7));
            float tangentJitter = radius * Mathf.Lerp(0.12f, 0.82f, pressure)
                * (Hash01(seed + 107, 11, 13) - 0.5f) * 2f;
            paintCenter += edgeNormal.normalized * inwardJitter + tangent * tangentJitter;
            paintCenter = ConstrainCenterToEdgeKeepout(paintCenter, Mathf.Max(mainRadiusAlong, mainRadiusSide) * 0.65f, seed + 211, out _, out _);
            float mainScale = Mathf.Lerp(0.92f, 0.44f, pressure);
            float mainIntensity = Mathf.Lerp(0.92f, 0.58f, pressure);
            PaintBlob(paintCenter, forward, mainRadiusAlong * mainScale, mainRadiusSide * mainScale, mainIntensity, seed, 0.18f, 0.94f);
            PaintEdgeBreakupLobes(paintCenter, edgeNormal.normalized, forward, radius, pressure, seed);
        }
        else
        {
            PaintBlob(paintCenter, forward, mainRadiusAlong, mainRadiusSide, 1f, seed, 0.16f, 1.0f);
        }

        int satelliteCount = Mathf.Clamp(effect.SplatSatelliteCount, 0, 12);
        for (int i = 0; i < satelliteCount; i++)
        {
            float r0 = Hash01(seed, i, 0);
            float r1 = Hash01(seed, i, 1);
            float r2 = Hash01(seed, i, 2);
            float r3 = Hash01(seed, i, 3);
            float angle = Mathf.Atan2(forward.y, forward.x) + Mathf.Lerp(-0.92f, 0.92f, r0);
            if (i > satelliteCount * 0.66f)
                angle = Mathf.Lerp(-Mathf.PI, Mathf.PI, r0);

            float distance = radius * Mathf.Lerp(0.45f, 1.75f, r1);
            float dropletRadius = radius * Mathf.Lerp(0.10f, 0.24f, r2);
            Vector3 dropletCenter = paintCenter + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
            dropletCenter = ConstrainCenterToEdgeKeepout(dropletCenter, dropletRadius * 1.25f, seed + i * 113, out _, out _);
            Vector2 dropletForward = Rotate(forward, Mathf.Lerp(-0.6f, 0.6f, r3));
            PaintBlob(dropletCenter, dropletForward, dropletRadius * Mathf.Lerp(1.0f, 1.75f, r1), dropletRadius * 0.72f, 0.84f, seed + i * 379, 0.22f, 0.78f);
        }

        if (edgePressure > 0.01f)
            PaintEdgeSpray(originalCenter, paintCenter, edgeNormal, forward, radius, edgePressure, seed);

        UploadDirtyRect();
    }

    private void PaintEdgeBreakupLobes(Vector3 paintCenter, Vector3 edgeNormal, Vector2 forward, float radius, float pressure, int seed)
    {
        if (edgeNormal.sqrMagnitude < 0.0001f)
            return;

        Vector3 tangent = new Vector3(-edgeNormal.z, 0f, edgeNormal.x).normalized;
        int lobeCount = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1f, 4f, pressure)), 1, 4);
        for (int i = 0; i < lobeCount; i++)
        {
            float r0 = Hash01(seed + i * 41, 19, 23);
            float r1 = Hash01(seed + i * 41, 29, 31);
            float r2 = Hash01(seed + i * 41, 37, 43);
            Vector3 center = paintCenter
                + tangent * Mathf.Lerp(-radius * 1.1f, radius * 1.1f, r0)
                + edgeNormal * Mathf.Lerp(radius * 0.08f, radius * 0.52f, r1);
            float lobeRadius = radius * Mathf.Lerp(0.18f, 0.38f, r2) * Mathf.Lerp(0.8f, 1.05f, pressure);
            center = ConstrainCenterToEdgeKeepout(center, lobeRadius * 1.15f, seed + i * 613, out _, out _);
            Vector2 lobeForward = Rotate(forward, Mathf.Lerp(-1.0f, 1.0f, r1));
            PaintBlob(center, lobeForward, lobeRadius * Mathf.Lerp(1.0f, 1.8f, r0), lobeRadius * Mathf.Lerp(0.62f, 0.92f, r2),
                Mathf.Lerp(0.52f, 0.74f, pressure), seed + i * 811, 0.2f, 0.78f);
        }
    }

    private Vector3 ConstrainCenterToEdgeKeepout(Vector3 center, float radius, int seed, out Vector3 edgeNormal, out float edgePressure)
    {
        edgeNormal = Vector3.zero;
        edgePressure = 0f;
        if (_edgeKeepoutWidth <= 0.001f)
            return center;

        float broadNoise = Mathf.PerlinNoise(center.x * 0.87f + seed * 0.011f, center.z * 0.87f - seed * 0.017f);
        float detailNoise = Mathf.PerlinNoise(center.x * 2.8f - seed * 0.007f, center.z * 2.8f + seed * 0.013f);
        float margin = _edgeKeepoutWidth
            + Mathf.Max(0.02f, radius * 0.82f)
            + (broadNoise - 0.5f) * _edgeKeepoutNoise
            + (detailNoise - 0.5f) * _edgeKeepoutNoise * 0.35f;
        margin = Mathf.Max(0.03f, margin);

        Vector3 constrained = center;
        constrained.x = Mathf.Clamp(constrained.x, _surfaceBounds.min.x + margin, _surfaceBounds.max.x - margin);
        constrained.z = Mathf.Clamp(constrained.z, _surfaceBounds.min.z + margin, _surfaceBounds.max.z - margin);
        Vector3 displacement = constrained - center;
        if (displacement.sqrMagnitude <= 0.000001f)
            return center;

        edgePressure = Mathf.Clamp01(displacement.magnitude / Mathf.Max(0.01f, radius + _edgeKeepoutFeather));
        edgeNormal = displacement.normalized;
        Vector3 tangent = new Vector3(-edgeNormal.z, 0f, edgeNormal.x);
        float tangentJitter = (Hash01(seed + 31, Mathf.RoundToInt(center.x * 17f), Mathf.RoundToInt(center.z * 19f)) - 0.5f)
            * _edgeKeepoutNoise * 0.65f;
        constrained += tangent * tangentJitter * edgePressure;
        constrained.x = Mathf.Clamp(constrained.x, _surfaceBounds.min.x + margin, _surfaceBounds.max.x - margin);
        constrained.z = Mathf.Clamp(constrained.z, _surfaceBounds.min.z + margin, _surfaceBounds.max.z - margin);
        return constrained;
    }

    private void PaintEdgeSpray(Vector3 originalCenter, Vector3 paintCenter, Vector3 edgeNormal, Vector2 forward, float radius, float edgePressure, int seed)
    {
        if (edgeNormal.sqrMagnitude < 0.0001f)
            return;

        Vector3 tangent3 = new Vector3(-edgeNormal.z, 0f, edgeNormal.x).normalized;
        int sprayCount = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(2f, 5f, edgePressure)), 1, 6);
        for (int i = 0; i < sprayCount; i++)
        {
            float r0 = Hash01(seed + i * 29, 3, 5);
            float r1 = Hash01(seed + i * 29, 7, 11);
            float r2 = Hash01(seed + i * 29, 13, 17);
            Vector3 center = Vector3.Lerp(originalCenter, paintCenter, Mathf.Lerp(0.10f, 0.34f, r0));
            center += tangent3 * Mathf.Lerp(-radius * 0.82f, radius * 0.82f, r1);
            center += edgeNormal * Mathf.Lerp(-radius * 0.07f, radius * 0.04f, r2);
            float dropletRadius = radius * Mathf.Lerp(0.030f, 0.080f, r2);
            center = ConstrainCenterToSurfaceBounds(center, dropletRadius * 0.35f);
            Vector2 dropletForward = Rotate(forward, Mathf.Lerp(-1.2f, 1.2f, r1));
            PaintBlob(center, dropletForward, dropletRadius * Mathf.Lerp(1.0f, 1.55f, r0), dropletRadius * 0.74f,
                Mathf.Lerp(0.18f, 0.36f, edgePressure), seed + i * 997, 0.18f, 0.48f, EdgePaintMode.IgnoreKeepout, 0.32f);
        }
    }

    private void BeginRoundFade(int fadeStepOverride, float fadeDuration)
    {
        if (_pixels == null || _splatTexture == null)
            return;

        int step = Mathf.Clamp(fadeStepOverride > 0 ? fadeStepOverride : _roundFadeStep, 1, 255);
        float duration = Mathf.Max(0f, fadeDuration);
        if (duration <= 0.001f)
        {
            ApplyRoundFadeImmediate(step);
            return;
        }

        _fadeSourcePixels = (Color32[])_pixels.Clone();
        _fadeTargetPixels = new Color32[_pixels.Length];
        for (int i = 0; i < _pixels.Length; i++)
        {
            Color32 p = _pixels[i];
            if (p.a == 0)
            {
                _fadeTargetPixels[i] = p;
                continue;
            }

            byte alpha = (byte)Mathf.Max(0, p.a - step);
            byte wetness = (byte)Mathf.Min(alpha, Mathf.Max(0, p.b - step));
            _fadeTargetPixels[i] = new Color32(alpha, alpha, wetness, alpha);
        }

        _fadeStartedAt = Time.time;
        _fadeDuration = duration;
        _nextFadeUploadAt = 0f;
        _roundFadeActive = true;
        if (_renderer != null)
            _renderer.enabled = true;
    }

    private void ApplyRoundFadeImmediate(int step)
    {
        bool hasInk = false;
        for (int i = 0; i < _pixels.Length; i++)
        {
            Color32 p = _pixels[i];
            if (p.a == 0)
                continue;

            byte alpha = (byte)Mathf.Max(0, p.a - step);
            byte wetness = (byte)Mathf.Min(alpha, Mathf.Max(0, p.b - step));
            _pixels[i] = new Color32(alpha, alpha, wetness, alpha);
            hasInk |= alpha > 0;
        }
        _splatTexture.SetPixels32(_pixels);
        _splatTexture.Apply(false, false);
        RebuildSideFlowMesh();
        if (_renderer != null)
            _renderer.enabled = hasInk;
    }

    private void UpdateRoundFade()
    {
        if (_fadeSourcePixels == null || _fadeTargetPixels == null || _splatTexture == null)
        {
            _roundFadeActive = false;
            return;
        }

        if (Time.time < _nextFadeUploadAt)
            return;

        float t = Mathf.Clamp01((Time.time - _fadeStartedAt) / Mathf.Max(0.001f, _fadeDuration));
        t = t * t * (3f - 2f * t);
        bool hasInk = false;
        for (int i = 0; i < _pixels.Length; i++)
        {
            Color32 from = _fadeSourcePixels[i];
            Color32 to = _fadeTargetPixels[i];
            byte alpha = (byte)Mathf.RoundToInt(Mathf.Lerp(from.a, to.a, t));
            byte wetness = (byte)Mathf.RoundToInt(Mathf.Lerp(from.b, to.b, t));
            _pixels[i] = new Color32(alpha, alpha, wetness, alpha);
            hasInk |= alpha > 0;
        }

        _splatTexture.SetPixels32(_pixels);
        _splatTexture.Apply(false, false);
        RebuildSideFlowMesh();
        if (_renderer != null)
            _renderer.enabled = hasInk;

        if (t >= 1f)
        {
            _roundFadeActive = false;
            _fadeSourcePixels = null;
            _fadeTargetPixels = null;
            return;
        }

        _nextFadeUploadAt = Time.time + 1f / 24f;
    }

    private void CommitRoundFadeNow()
    {
        if (!_roundFadeActive)
            return;

        _nextFadeUploadAt = 0f;
        UpdateRoundFade();
        _roundFadeActive = false;
        _fadeSourcePixels = null;
        _fadeTargetPixels = null;
    }

    private void PaintBlob(
        Vector3 center,
        Vector2 forward,
        float radiusAlong,
        float radiusSide,
        float intensity,
        int seed,
        float softness,
        float wetness,
        EdgePaintMode edgeMode = EdgePaintMode.RespectKeepout,
        float maxAlpha = 1f)
    {
        if (_pixels == null || _resolution <= 0) return;
        if (!WorldToUv(center, out Vector2 centerUv)) return;

        Vector2 right = new Vector2(-forward.y, forward.x);
        float maxRadius = Mathf.Max(radiusAlong, radiusSide) * 1.18f;
        int x0 = Mathf.Clamp(Mathf.FloorToInt((centerUv.x - maxRadius / _surfaceBounds.size.x) * _resolution), 0, _resolution - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((centerUv.x + maxRadius / _surfaceBounds.size.x) * _resolution), 0, _resolution - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt((centerUv.y - maxRadius / _surfaceBounds.size.z) * _resolution), 0, _resolution - 1);
        int y1 = Mathf.Clamp(Mathf.CeilToInt((centerUv.y + maxRadius / _surfaceBounds.size.z) * _resolution), 0, _resolution - 1);

        for (int y = y0; y <= y1; y++)
        {
            float worldZ = Mathf.Lerp(_surfaceBounds.min.z, _surfaceBounds.max.z, (y + 0.5f) / _resolution);
            for (int x = x0; x <= x1; x++)
            {
                float worldX = Mathf.Lerp(_surfaceBounds.min.x, _surfaceBounds.max.x, (x + 0.5f) / _resolution);
                Vector2 delta = new Vector2(worldX - center.x, worldZ - center.z);
                float along = Vector2.Dot(delta, forward) / Mathf.Max(0.001f, radiusAlong);
                float side = Vector2.Dot(delta, right) / Mathf.Max(0.001f, radiusSide);
                float dist = Mathf.Sqrt(along * along + side * side);

                float noiseA = Mathf.PerlinNoise(worldX * 3.8f + seed * 0.031f, worldZ * 3.8f - seed * 0.027f);
                float noiseB = Mathf.PerlinNoise(worldX * 12.0f - seed * 0.017f, worldZ * 11.4f + seed * 0.019f);
                float raggedEdge = 0.82f + (noiseA - 0.5f) * 0.32f + (noiseB - 0.5f) * 0.10f;
                float paint = 1f - Smooth01(raggedEdge, raggedEdge + softness, dist);
                float centerFill = 1f - Smooth01(0.0f, 0.48f, dist);
                paint = Mathf.Max(paint, centerFill * 0.70f);
                paint *= intensity;
                if (paint <= 0.004f)
                    continue;

                float edgeMask = 1f;
                if (edgeMode == EdgePaintMode.RespectKeepout)
                {
                    edgeMask = EvaluateIrregularEdgeMask(worldX, worldZ);
                    if (edgeMask <= EdgeMaskPaintThreshold)
                        continue;

                    paint *= edgeMask;
                    if (paint <= 0.004f)
                        continue;
                }

                int index = y * _resolution + x;
                float existing = _pixels[index].a / 255f;
                float combined = Mathf.Clamp01(existing + paint * (1f - existing));
                if (edgeMode == EdgePaintMode.RespectKeepout)
                {
                    existing = Mathf.Min(existing, edgeMask);
                    combined = Mathf.Min(edgeMask, Mathf.Clamp01(existing + paint * (1f - existing)));
                }
                else
                {
                    combined = Mathf.Min(combined, Mathf.Max(existing, Mathf.Clamp01(maxAlpha)));
                }
                byte alpha = (byte)Mathf.RoundToInt(combined * 255f);
                float existingWetness = _pixels[index].b / 255f;
                if (edgeMode == EdgePaintMode.RespectKeepout)
                    existingWetness = Mathf.Min(existingWetness, edgeMask);
                float cappedWetness = Mathf.Min(combined, Mathf.Max(existingWetness, paint * wetness));
                byte wet = (byte)Mathf.RoundToInt(Mathf.Clamp01(cappedWetness) * 255f);
                _pixels[index] = new Color32(alpha, alpha, wet, alpha);
                MarkDirty(x, y);
            }
        }
    }

    private float EvaluateIrregularEdgeMask(float worldX, float worldZ)
    {
        if (_edgeKeepoutWidth <= 0.001f)
            return 1f;

        float left = worldX - _surfaceBounds.min.x;
        float right = _surfaceBounds.max.x - worldX;
        float bottom = worldZ - _surfaceBounds.min.z;
        float top = _surfaceBounds.max.z - worldZ;
        float distanceToEdge = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top));

        float edgeCoord;
        float edgeIndex;
        if (distanceToEdge == left)
        {
            edgeCoord = Mathf.InverseLerp(_surfaceBounds.min.z, _surfaceBounds.max.z, worldZ);
            edgeIndex = 0f;
        }
        else if (distanceToEdge == right)
        {
            edgeCoord = Mathf.InverseLerp(_surfaceBounds.min.z, _surfaceBounds.max.z, worldZ);
            edgeIndex = 1f;
        }
        else if (distanceToEdge == bottom)
        {
            edgeCoord = Mathf.InverseLerp(_surfaceBounds.min.x, _surfaceBounds.max.x, worldX);
            edgeIndex = 2f;
        }
        else
        {
            edgeCoord = Mathf.InverseLerp(_surfaceBounds.min.x, _surfaceBounds.max.x, worldX);
            edgeIndex = 3f;
        }

        float edgeSeed = _edgeNoiseSeed + edgeIndex * 19.371f;
        float broadNoise = Mathf.PerlinNoise(edgeCoord * 3.2f + edgeSeed, edgeSeed * 0.37f);
        float contourNoise = Mathf.PerlinNoise(edgeCoord * 9.4f - edgeSeed * 0.41f, edgeSeed * 0.73f + 2.17f);
        float nickNoise = Mathf.PerlinNoise(edgeCoord * 28.0f + edgeSeed * 0.19f, edgeSeed * 1.31f - 4.6f);
        float breakupNoise = Mathf.PerlinNoise(worldX * 9.6f + _edgeNoiseSeed * 0.47f, worldZ * 8.9f - _edgeNoiseSeed * 0.31f);

        float keepout = _edgeKeepoutWidth
            + (broadNoise - 0.5f) * _edgeKeepoutNoise * 1.75f
            + (contourNoise - 0.5f) * _edgeKeepoutNoise * 0.82f
            + (nickNoise - 0.5f) * _edgeKeepoutNoise * 0.38f;
        keepout = Mathf.Max(0.015f, keepout);

        float feather = Mathf.Max(0.012f, _edgeKeepoutFeather);
        float mask = Smooth01(keepout, keepout + feather, distanceToEdge);

        if (mask < 0.999f)
        {
            float speckle = Smooth01(0.22f, 0.88f, breakupNoise);
            float edgeZone = 1f - mask;
            mask *= Mathf.Lerp(0.58f + speckle * 0.42f, 1f, mask);
            mask = Mathf.Max(mask, speckle * 0.18f * edgeZone * Smooth01(keepout - feather * 0.55f, keepout + feather * 0.15f, distanceToEdge));
        }

        return Mathf.Clamp01(mask);
    }

    private static float ComputeEdgeNoiseSeed(Bounds bounds)
    {
        float value = bounds.center.x * 12.9898f
            + bounds.center.z * 78.233f
            + bounds.size.x * 37.719f
            + bounds.size.z * 11.137f;
        return Mathf.Abs(Mathf.Sin(value) * 17.371f);
    }

    private Vector3 ConstrainCenterToSurfaceBounds(Vector3 center, float margin)
    {
        margin = Mathf.Max(0.002f, margin);
        margin = Mathf.Min(margin, _surfaceBounds.size.x * 0.45f, _surfaceBounds.size.z * 0.45f);
        center.x = Mathf.Clamp(center.x, _surfaceBounds.min.x + margin, _surfaceBounds.max.x - margin);
        center.z = Mathf.Clamp(center.z, _surfaceBounds.min.z + margin, _surfaceBounds.max.z - margin);
        return center;
    }

    private bool WorldToUv(Vector3 world, out Vector2 uv)
    {
        uv = new Vector2(
            Mathf.InverseLerp(_surfaceBounds.min.x, _surfaceBounds.max.x, world.x),
            Mathf.InverseLerp(_surfaceBounds.min.z, _surfaceBounds.max.z, world.z));
        return uv.x >= -0.08f && uv.x <= 1.08f && uv.y >= -0.08f && uv.y <= 1.08f;
    }

    private void ResetDirtyRect()
    {
        _dirtyMinX = _resolution;
        _dirtyMinY = _resolution;
        _dirtyMaxX = -1;
        _dirtyMaxY = -1;
        _hasDirtyPixels = false;
    }

    private void MarkDirty(int x, int y)
    {
        _dirtyMinX = Mathf.Min(_dirtyMinX, x);
        _dirtyMinY = Mathf.Min(_dirtyMinY, y);
        _dirtyMaxX = Mathf.Max(_dirtyMaxX, x);
        _dirtyMaxY = Mathf.Max(_dirtyMaxY, y);
        _hasDirtyPixels = true;
    }

    private void UploadDirtyRect()
    {
        if (!_hasDirtyPixels || _splatTexture == null) return;

        int width = _dirtyMaxX - _dirtyMinX + 1;
        int height = _dirtyMaxY - _dirtyMinY + 1;
        var block = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            int sourceIndex = (_dirtyMinY + y) * _resolution + _dirtyMinX;
            int targetIndex = y * width;
            System.Array.Copy(_pixels, sourceIndex, block, targetIndex, width);
        }

        _splatTexture.SetPixels32(_dirtyMinX, _dirtyMinY, width, height, block);
        _splatTexture.Apply(false, false);
        RebuildSideFlowMesh();
    }

    private static Vector2 Rotate(Vector2 vector, float radians)
    {
        float s = Mathf.Sin(radians);
        float c = Mathf.Cos(radians);
        return new Vector2(vector.x * c - vector.y * s, vector.x * s + vector.y * c).normalized;
    }

    private static float Hash01(int seed, int a, int b)
    {
        float value = Mathf.Sin(seed * 12.9898f + a * 78.233f + b * 37.719f) * 43758.5453f;
        return value - Mathf.Floor(value);
    }

    private static float Smooth01(float from, float to, float value)
    {
        if (Mathf.Approximately(from, to)) return value >= to ? 1f : 0f;
        float t = Mathf.Clamp01((value - from) / (to - from));
        return t * t * (3f - 2f * t);
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;

        DestroyRuntimeObject(_splatTexture);
        DestroyRuntimeObject(_surfaceMesh);
        DestroyRuntimeObject(_sideFlowMesh);
        DestroyRuntimeObject(_material);
        DestroyRuntimeObject(_sideFlowMaterial);
        _fadeSourcePixels = null;
        _fadeTargetPixels = null;
    }

    private static void DestroyRuntimeObject(Object target, bool immediate = false)
    {
        if (target == null) return;
        if (Application.isPlaying && !immediate)
            Destroy(target);
        else
            DestroyImmediate(target);
    }
}
