using UnityEngine;

/// <summary>
/// Floats above the player and points arrow model at the next hoop (including up and down).
/// Created by TimeTrialManager, which calls Setup. If no arrow prefab is assigned, nothing is shown.
/// Model must point along +Z.
/// </summary>
public class RaceNavigationArrow : MonoBehaviour
{
    public Transform follow;
    public float height = 2.8f;
    public float turnSharpness = 14f;

    private Vector3 targetPoint;
    private bool hasTarget;
    private GameObject modelRoot;
    private bool shown;

    public void Setup(GameObject prefab, float scale, float arrowHeight)
    {
        height = arrowHeight;
        if (modelRoot != null) Destroy(modelRoot);
        modelRoot = null;
        shown = false;

        if (prefab == null) return;

        modelRoot = Instantiate(prefab, transform);
        RaceModelUtils.StripColliders(modelRoot);
        modelRoot.transform.localScale *= scale;
        modelRoot.SetActive(false);
    }

    public void SetTarget(Vector3 point)
    {
        targetPoint = point;
        hasTarget = true;
    }

    public void Clear()
    {
        hasTarget = false;
        SetShown(false);
    }

    private void SetShown(bool show)
    {
        if (modelRoot == null || shown == show) return;
        shown = show;
        modelRoot.SetActive(show);
    }

    private void LateUpdate()
    {
        if (!hasTarget || follow == null || modelRoot == null)
        {
            SetShown(false);
            return;
        }

        SetShown(true);

        Vector3 position = follow.position + Vector3.up * (height + Mathf.Sin(Time.time * 3f) * 0.1f);
        transform.position = position;

        Vector3 toTarget = targetPoint - position;
        if (toTarget.sqrMagnitude < 0.01f) return;

        Quaternion wanted = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, wanted, 1f - Mathf.Exp(-turnSharpness * Time.deltaTime));
    }
}
