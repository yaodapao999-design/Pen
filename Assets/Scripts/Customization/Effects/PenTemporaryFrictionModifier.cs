using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Temporarily overrides every physical collider on a pen with a lower-friction material.
/// The original materials are restored when the timer expires.
/// </summary>
public class PenTemporaryFrictionModifier : MonoBehaviour
{
    private readonly Dictionary<Collider, PhysicsMaterial> _originalMaterials = new();
    private PhysicsMaterial _activeMaterial;
    private float _expiresAt;

    public void Apply(PhysicsMaterial material, float duration)
    {
        if (material == null || duration <= 0f) return;

        _activeMaterial = material;
        _expiresAt = Mathf.Max(_expiresAt, Time.time + duration);

        foreach (Collider col in GetComponentsInChildren<Collider>(false))
        {
            if (col == null || col.isTrigger) continue;
            if (!_originalMaterials.ContainsKey(col))
                _originalMaterials[col] = col.sharedMaterial;
            col.sharedMaterial = _activeMaterial;
        }
    }

    private void Update()
    {
        if (_activeMaterial == null) return;
        if (Time.time < _expiresAt) return;
        Restore();
    }

    private void OnDisable()
    {
        Restore();
    }

    private void Restore()
    {
        foreach (var kv in _originalMaterials)
        {
            if (kv.Key != null)
                kv.Key.sharedMaterial = kv.Value;
        }
        _originalMaterials.Clear();
        _activeMaterial = null;
        _expiresAt = 0f;
    }
}
