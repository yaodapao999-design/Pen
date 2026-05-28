using UnityEngine;

public static class MaterialAlphaUtility
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
    private static readonly int DiffuseColorId = Shader.PropertyToID("_DiffuseColor");
    private static readonly int ShadowDiffuseColorId = Shader.PropertyToID("_ShadowDiffuseColor");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

    public sealed class Snapshot
    {
        private readonly MaterialState[] _states;

        private Snapshot(MaterialState[] states)
        {
            _states = states;
        }

        internal static Snapshot Create(MaterialState[] states) => new Snapshot(states);

        public void Restore()
        {
            if (_states == null) return;
            for (int i = 0; i < _states.Length; i++)
                _states[i].Restore();
        }
    }

    internal struct MaterialState
    {
        private Material _material;
        private bool _hasBaseColor;
        private bool _hasLegacyColor;
        private bool _hasDiffuseColor;
        private bool _hasShadowDiffuseColor;
        private Color _baseColor;
        private Color _legacyColor;
        private Color _diffuseColor;
        private Color _shadowDiffuseColor;
        private bool _hasSurface;
        private bool _hasSrcBlend;
        private bool _hasDstBlend;
        private bool _hasZWrite;
        private float _surface;
        private float _srcBlend;
        private float _dstBlend;
        private float _zWrite;
        private int _renderQueue;
        private string _renderTypeTag;

        public MaterialState(Material material)
        {
            _material = material;
            _hasBaseColor = material != null && material.HasProperty(BaseColorId);
            _hasLegacyColor = material != null && material.HasProperty(LegacyColorId);
            _hasDiffuseColor = material != null && material.HasProperty(DiffuseColorId);
            _hasShadowDiffuseColor = material != null && material.HasProperty(ShadowDiffuseColorId);
            _baseColor = _hasBaseColor ? material.GetColor(BaseColorId) : default;
            _legacyColor = _hasLegacyColor ? material.GetColor(LegacyColorId) : default;
            _diffuseColor = _hasDiffuseColor ? material.GetColor(DiffuseColorId) : default;
            _shadowDiffuseColor = _hasShadowDiffuseColor ? material.GetColor(ShadowDiffuseColorId) : default;
            _hasSurface = material != null && material.HasProperty(SurfaceId);
            _hasSrcBlend = material != null && material.HasProperty(SrcBlendId);
            _hasDstBlend = material != null && material.HasProperty(DstBlendId);
            _hasZWrite = material != null && material.HasProperty(ZWriteId);
            _surface = _hasSurface ? material.GetFloat(SurfaceId) : 0f;
            _srcBlend = _hasSrcBlend ? material.GetFloat(SrcBlendId) : 0f;
            _dstBlend = _hasDstBlend ? material.GetFloat(DstBlendId) : 0f;
            _zWrite = _hasZWrite ? material.GetFloat(ZWriteId) : 0f;
            _renderQueue = material != null ? material.renderQueue : -1;
            _renderTypeTag = material != null ? material.GetTag("RenderType", false, string.Empty) : string.Empty;
        }

        public void Restore()
        {
            if (_material == null) return;
            if (_hasBaseColor) _material.SetColor(BaseColorId, _baseColor);
            if (_hasLegacyColor) _material.SetColor(LegacyColorId, _legacyColor);
            if (_hasDiffuseColor) _material.SetColor(DiffuseColorId, _diffuseColor);
            if (_hasShadowDiffuseColor) _material.SetColor(ShadowDiffuseColorId, _shadowDiffuseColor);
            if (_hasSurface) _material.SetFloat(SurfaceId, _surface);
            if (_hasSrcBlend) _material.SetFloat(SrcBlendId, _srcBlend);
            if (_hasDstBlend) _material.SetFloat(DstBlendId, _dstBlend);
            if (_hasZWrite) _material.SetFloat(ZWriteId, _zWrite);
            _material.renderQueue = _renderQueue;
            _material.SetOverrideTag("RenderType", _renderTypeTag);
        }
    }

    public static Snapshot Capture(GameObject root)
    {
        if (root == null) return Snapshot.Create(System.Array.Empty<MaterialState>());

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var states = new System.Collections.Generic.List<MaterialState>();
        foreach (var r in renderers)
        {
            foreach (var mat in GetEditableMaterials(r))
            {
                if (mat != null)
                    states.Add(new MaterialState(mat));
            }
        }

        return Snapshot.Create(states.ToArray());
    }

    public static void ApplyAlpha(Material mat, float alpha)
    {
        if (mat == null) return;

        ApplyColorAlpha(mat, BaseColorId, alpha);
        ApplyColorAlpha(mat, LegacyColorId, alpha);
        ApplyColorAlpha(mat, DiffuseColorId, alpha);
        ApplyColorAlpha(mat, ShadowDiffuseColorId, alpha);

        if (mat.HasProperty(SurfaceId))
        {
            if (alpha < 1f)
            {
                mat.SetFloat(SurfaceId, 1);
                mat.SetInt(SrcBlendId, (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt(DstBlendId, (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty(ZWriteId))
                    mat.SetInt(ZWriteId, 0);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = 3000;
            }
            else
            {
                mat.SetFloat(SurfaceId, 0);
                mat.SetInt(SrcBlendId, (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt(DstBlendId, (int)UnityEngine.Rendering.BlendMode.Zero);
                if (mat.HasProperty(ZWriteId))
                    mat.SetInt(ZWriteId, 1);
                mat.SetOverrideTag("RenderType", string.Empty);
                mat.renderQueue = -1;
            }
        }
    }

    public static void ApplyAlpha(GameObject root, float alpha)
    {
        if (root == null) return;

        foreach (var r in root.GetComponentsInChildren<Renderer>())
            foreach (var mat in GetEditableMaterials(r))
                ApplyAlpha(mat, alpha);
    }

    public static void Restore(Snapshot snapshot)
    {
        snapshot?.Restore();
    }

    private static void ApplyColorAlpha(Material mat, int propertyId, float alpha)
    {
        if (mat == null || !mat.HasProperty(propertyId)) return;
        Color c = mat.GetColor(propertyId);
        mat.SetColor(propertyId, new Color(c.r, c.g, c.b, alpha));
    }

    private static Material[] GetEditableMaterials(Renderer renderer)
    {
        if (renderer == null) return System.Array.Empty<Material>();
        return Application.isPlaying ? renderer.materials : renderer.sharedMaterials;
    }
}
