using UnityEngine;

/// <summary>
/// Single source of truth for launch-time physics numbers.
/// Real launch, prediction, and aim readability all read the same snapshot so tuning one value does not
/// quietly desync the others.
/// </summary>
public readonly struct PenLaunchPhysicsSnapshot
{
    public readonly bool IsValid;
    public readonly Vector3 Direction;
    public readonly float Force01;
    public readonly Vector3 ContactPointWorld;
    public readonly Vector3 EffectiveContactPointWorld;
    public readonly Vector3 CenterOfMassWorld;
    public readonly Vector3 MomentArmWorld;
    public readonly Vector3 Impulse;
    public readonly float LaunchVelocity;
    public readonly float MassKg;
    public readonly float DynamicFriction;
    public readonly float Friction01;
    public readonly float AngularImpulseY;
    public readonly float EstimatedYawAngularVelocity;

    public PenLaunchPhysicsSnapshot(
        bool isValid,
        Vector3 direction,
        float force01,
        Vector3 contactPointWorld,
        Vector3 effectiveContactPointWorld,
        Vector3 centerOfMassWorld,
        Vector3 momentArmWorld,
        Vector3 impulse,
        float launchVelocity,
        float massKg,
        float dynamicFriction,
        float friction01,
        float angularImpulseY,
        float estimatedYawAngularVelocity)
    {
        IsValid = isValid;
        Direction = direction;
        Force01 = force01;
        ContactPointWorld = contactPointWorld;
        EffectiveContactPointWorld = effectiveContactPointWorld;
        CenterOfMassWorld = centerOfMassWorld;
        MomentArmWorld = momentArmWorld;
        Impulse = impulse;
        LaunchVelocity = launchVelocity;
        MassKg = massKg;
        DynamicFriction = dynamicFriction;
        Friction01 = friction01;
        AngularImpulseY = angularImpulseY;
        EstimatedYawAngularVelocity = estimatedYawAngularVelocity;
    }
}

public static class PenLaunchPhysics
{
    public const float DefaultDynamicFriction = 0.42f;

    private const float MIN_FRICTION = 0.04f;
    private const float MAX_FRICTION = 2.5f;

    public static PenLaunchPhysicsSnapshot Build(
        PenEntity pen,
        Vector3 direction,
        float force,
        Vector3 contactPointWorld)
    {
        Rigidbody rb = pen != null ? pen.rb : null;
        Vector3 flatDirection = direction;
        flatDirection.y = 0f;
        bool valid = pen != null && rb != null && flatDirection.sqrMagnitude > 1e-6f;
        if (flatDirection.sqrMagnitude > 1e-6f)
            flatDirection.Normalize();

        float force01 = Mathf.Clamp01(force);
        Vector3 center = rb != null ? rb.worldCenterOfMass : (pen != null ? pen.transform.position : contactPointWorld);
        Vector3 effectiveContact = pen != null ? pen.GetEffectiveLaunchContactPoint(contactPointWorld) : contactPointWorld;
        Vector3 momentArm = effectiveContact - center;
        momentArm.y = 0f;

        float mass = rb != null ? Mathf.Max(rb.mass, 1e-4f) : 0f;
        float launchVelocity = valid ? Mathf.Max(0f, pen.EstimateLaunchVelocity(force01)) : 0f;
        Vector3 impulse = valid ? flatDirection * (launchVelocity * mass) : Vector3.zero;
        float angularImpulseY = valid ? Vector3.Dot(Vector3.Cross(momentArm, impulse), Vector3.up) : 0f;
        float inertiaY = rb != null ? EstimateInertiaAroundWorldAxis(rb, Vector3.up) : 1f;
        float yawVelocity = valid ? angularImpulseY / inertiaY : 0f;
        float dynamicFriction = EstimateDynamicFriction(pen, contactPointWorld);
        float friction01 = Mathf.InverseLerp(0.15f, 1.2f, dynamicFriction);

        return new PenLaunchPhysicsSnapshot(
            valid,
            flatDirection,
            force01,
            contactPointWorld,
            effectiveContact,
            center,
            momentArm,
            impulse,
            launchVelocity,
            mass,
            dynamicFriction,
            Mathf.Clamp01(friction01),
            angularImpulseY,
            yawVelocity);
    }

    public static float EstimateDynamicFriction(PenEntity pen, Vector3 contactWorld)
    {
        if (pen == null)
            return DefaultDynamicFriction;

        Collider[] colliders = pen.GetComponentsInChildren<Collider>();
        if (colliders == null || colliders.Length == 0)
            return DefaultDynamicFriction;

        float weightedFriction = 0f;
        float totalWeight = 0f;

        foreach (Collider col in colliders)
        {
            if (col == null || !col.enabled || col.isTrigger)
                continue;

            float friction = GetColliderDynamicFriction(col);
            Bounds b = col.bounds;
            Vector3 size = b.size;
            float volumeWeight = Mathf.Max(0.001f, Mathf.Sqrt(Mathf.Max(size.x * size.y * size.z, 0.0001f)));
            float contactDistance = Vector3.Distance(col.ClosestPoint(contactWorld), contactWorld);
            float localWeight = 1f / Mathf.Max(0.12f, contactDistance + 0.12f);
            float weight = volumeWeight * localWeight;

            weightedFriction += friction * weight;
            totalWeight += weight;
        }

        if (totalWeight <= 1e-5f)
            return DefaultDynamicFriction;

        return Mathf.Clamp(weightedFriction / totalWeight, MIN_FRICTION, MAX_FRICTION);
    }

    public static float EstimateInertiaAroundWorldAxis(Rigidbody rb, Vector3 worldAxis)
    {
        if (rb == null)
            return 1f;

        Vector3 axis = worldAxis.sqrMagnitude > 1e-6f ? worldAxis.normalized : Vector3.up;
        Quaternion principalRotation = rb.rotation * rb.inertiaTensorRotation;
        Vector3 localAxis = Quaternion.Inverse(principalRotation) * axis;
        Vector3 inertia = rb.inertiaTensor;

        float value =
            inertia.x * localAxis.x * localAxis.x +
            inertia.y * localAxis.y * localAxis.y +
            inertia.z * localAxis.z * localAxis.z;

        return Mathf.Max(value, 1e-4f);
    }

    private static float GetColliderDynamicFriction(Collider col)
    {
        PhysicsMaterial mat = col != null ? col.sharedMaterial : null;
        if (mat == null)
            return DefaultDynamicFriction;

        return Mathf.Clamp(mat.dynamicFriction, MIN_FRICTION, MAX_FRICTION);
    }
}
