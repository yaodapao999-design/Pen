using UnityEngine;

/// <summary>
/// Dispatches physics collisions to equipped part effects. This stays separate from
/// visual feedback so enemy/test pens can run effects without double-playing Feel feedbacks.
/// </summary>
[RequireComponent(typeof(PenEntity))]
public class PenCollisionEffectDispatcher : MonoBehaviour
{
    [SerializeField] private string penTag = "Pen";
    [SerializeField] private string[] additionalTags;

    private PenEffectRunner _effectRunner;

    private void Awake()
    {
        _effectRunner = GetComponent<PenEffectRunner>();
    }

    public void Configure(string primaryTag, string[] extraTags)
    {
        penTag = primaryTag;
        additionalTags = extraTags;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsAcceptedCollision(collision)) return;

        if (_effectRunner == null)
            _effectRunner = GetComponent<PenEffectRunner>();
        _effectRunner?.NotifyCollision(collision);
    }

    private bool IsAcceptedCollision(Collision collision)
    {
        if (collision == null) return false;

        if (IsOtherPen(collision.rigidbody))
            return true;

        if (collision.collider != null && IsOtherPen(collision.collider.GetComponentInParent<PenEntity>()))
            return true;

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            if (IsOtherPen(contact.thisCollider) || IsOtherPen(contact.otherCollider))
                return true;
        }

        return IsAcceptedCollider(collision.gameObject) || HasAcceptedContactTag(collision);
    }

    private bool IsOtherPen(Rigidbody rb)
    {
        return rb != null && IsOtherPen(rb.GetComponent<PenEntity>());
    }

    private bool IsOtherPen(Collider col)
    {
        return col != null && IsOtherPen(col.GetComponentInParent<PenEntity>());
    }

    private bool IsOtherPen(PenEntity pen)
    {
        return pen != null && pen.gameObject != gameObject;
    }

    private bool HasAcceptedContactTag(Collision collision)
    {
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            if (IsAcceptedCollider(contact.thisCollider != null ? contact.thisCollider.gameObject : null) ||
                IsAcceptedCollider(contact.otherCollider != null ? contact.otherCollider.gameObject : null))
                return true;
        }

        return false;
    }

    private bool IsAcceptedCollider(GameObject go)
    {
        if (go == null || go == gameObject) return false;
        if (!string.IsNullOrEmpty(penTag) && go.CompareTag(penTag)) return true;
        if (additionalTags == null) return false;
        for (int i = 0; i < additionalTags.Length; i++)
        {
            var t = additionalTags[i];
            if (!string.IsNullOrEmpty(t) && go.CompareTag(t)) return true;
        }
        return false;
    }
}
