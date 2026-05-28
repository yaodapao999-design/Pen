using UnityEngine;

/// <summary>
/// Shared helpers for anchoring effect visuals to the part that owns the effect.
/// Keeps VFX placement in the same coordinate system as sockets and battle prefabs.
/// </summary>
public static class PenEffectVfxAlignment
{
    public static bool TryFindEffectPart(
        PenEntity entity,
        PenPartEffect effect,
        out PenPartInstance instance,
        PartType preferredType = PartType.Accessory,
        bool requirePreferredType = false)
    {
        instance = null;
        if (entity == null || entity.Assembly == null || effect == null) return false;

        PenPartInstance fallback = null;
        foreach (var part in entity.Assembly.BattleParts)
        {
            if (part == null || part.Data == null || part.GameObject == null) continue;
            if (!PartHasEffect(part.Data, effect)) continue;

            if (part.Data.Category == preferredType)
            {
                instance = part;
                return true;
            }

            fallback ??= part;
        }

        if (!requirePreferredType && fallback != null)
        {
            instance = fallback;
            return true;
        }

        return false;
    }

    public static bool TryGetEffectPartTransform(
        PenEntity entity,
        PenPartEffect effect,
        out Transform partTransform,
        PartType preferredType = PartType.Accessory,
        bool requirePreferredType = false)
    {
        partTransform = null;
        if (!TryFindEffectPart(entity, effect, out var part, preferredType, requirePreferredType))
            return false;

        partTransform = part.GameObject != null ? part.GameObject.transform : null;
        return partTransform != null;
    }

    public static bool TryGetEffectAnchor(
        PenEntity entity,
        PenPartEffect effect,
        out Vector3 position,
        out Vector3 forward,
        out Vector3 up,
        PartType preferredType = PartType.Accessory,
        bool requirePreferredType = false)
    {
        position = entity != null && entity.rb != null ? entity.rb.worldCenterOfMass : Vector3.zero;
        forward = entity != null ? entity.GetPenAxis() : Vector3.right;
        up = Vector3.up;

        if (!TryGetEffectPartTransform(entity, effect, out Transform partTransform, preferredType, requirePreferredType))
            return false;

        position = partTransform.position;
        forward = ProjectHorizontal(partTransform.forward);
        if (forward.sqrMagnitude < 1e-5f && entity != null)
            forward = ProjectHorizontal(entity.GetPenAxis());
        if (forward.sqrMagnitude < 1e-5f)
            forward = Vector3.right;
        forward.Normalize();

        up = partTransform.up.sqrMagnitude > 1e-5f ? partTransform.up.normalized : Vector3.up;
        if (Mathf.Abs(Vector3.Dot(up, forward)) > 0.95f)
            up = Vector3.up;

        return true;
    }

    public static bool TryProjectToSurface(
        Vector3 origin,
        PenEntity owner,
        out Vector3 point,
        out Vector3 normal,
        float upOffset = 0.7f,
        float distance = 2.5f)
    {
        point = origin;
        normal = Vector3.up;
        RaycastHit[] hits = Physics.RaycastAll(origin + Vector3.up * upOffset, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null) continue;
            if (owner != null && hit.collider.GetComponentInParent<PenEntity>() == owner) continue;
            point = hit.point;
            normal = hit.normal.sqrMagnitude > 1e-5f ? hit.normal.normalized : Vector3.up;
            return true;
        }

        return false;
    }

    public static Quaternion SurfaceRotation(Vector3 forward, Vector3 normal)
    {
        normal = normal.sqrMagnitude > 1e-5f ? normal.normalized : Vector3.up;
        forward -= normal * Vector3.Dot(forward, normal);
        if (forward.sqrMagnitude < 1e-5f)
            forward = Vector3.Cross(normal, Vector3.right);
        if (forward.sqrMagnitude < 1e-5f)
            forward = Vector3.forward;
        return Quaternion.LookRotation(forward.normalized, normal);
    }

    public static Vector3 ProjectHorizontal(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }

    public static bool PartHasEffect(PenPartData data, PenPartEffect effect)
    {
        if (data == null || effect == null || data.Effects == null) return false;
        foreach (var candidate in data.Effects)
        {
            if (candidate == effect)
                return true;
        }
        return false;
    }
}
