using UnityEngine;

public static class MaterialAlphaUtility
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");

    public static void ApplyAlpha(Material mat, float alpha)
    {
        if (mat.HasProperty(BaseColorId))
        {
            Color c = mat.GetColor(BaseColorId);
            mat.SetColor(BaseColorId, new Color(c.r, c.g, c.b, alpha));
        }
        else if (mat.HasProperty(LegacyColorId))
        {
            Color c = mat.GetColor(LegacyColorId);
            mat.SetColor(LegacyColorId, new Color(c.r, c.g, c.b, alpha));
        }

        if (mat.HasProperty(SurfaceId))
        {
            if (alpha < 1f)
            {
                mat.SetFloat(SurfaceId, 1);
                mat.SetInt(SrcBlendId, (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt(DstBlendId, (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.renderQueue = 3000;
            }
            else
            {
                mat.SetFloat(SurfaceId, 0);
                mat.SetInt(SrcBlendId, (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt(DstBlendId, (int)UnityEngine.Rendering.BlendMode.Zero);
                mat.renderQueue = -1;
            }
        }
    }

    public static void ApplyAlpha(GameObject root, float alpha)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>())
            foreach (var mat in r.materials)
                ApplyAlpha(mat, alpha);
    }
}
