using UnityEngine;

/// <summary>
/// Floats above the player and points an arrow model at the next hoop, including up and down.
/// </summary>
/// <remarks>Created by <see cref="TimeTrialManager"/>, which calls <see cref="Setup"/>. If no arrow prefab is assigned, nothing is shown. The model must point along +Z.</remarks>
public class RaceNavigationArrow : MonoBehaviour
{
    public Transform follow;
    public float height = 2.8f;
    public float turnSharpness = 14f;

    private Vector3 targetPoint;
    private bool hasTarget;
    private GameObject modelRoot;
    private bool shown;

    /// <summary>Creates the arrow model (replacing any previous one), removes its colliders and keeps it hidden until a target is set.</summary>
    /// <param name="arrowPrefab">The arrow model; null means no arrow is shown.</param>
    /// <param name="arrowScale">Scale multiplier for the model.</param>
    /// <param name="arrowHeight">Height above the followed object.</param>
    public void Setup(GameObject arrowPrefab, float arrowScale, float arrowHeight)
    {
        height = arrowHeight;
        if (modelRoot != null)
            Destroy(modelRoot);
        modelRoot = null;
        shown = false;

        if (arrowPrefab == null) 
            return;

        modelRoot = Instantiate(arrowPrefab, transform);
        RaceModelUtils.StripColliders(modelRoot);
        modelRoot.transform.localScale *= arrowScale;
        modelRoot.SetActive(false);
    }

    /// <summary>Sets the world position the arrow points at and enables it.</summary>
    /// <param name="point">Target position.</param>
    public void SetTarget(Vector3 point)
    {
        targetPoint = point;
        hasTarget = true;
    }

    /// <summary>Removes the target and hides the arrow.</summary>
    public void Clear()
    {
        hasTarget = false;
        SetShown(false);
    }

    /// <summary>Shows or hides the model, only touching it when the state actually changes.</summary>
    /// <param name="show">Whether the model should be visible.</param>
    private void SetShown(bool show)
    {
        if (modelRoot == null || shown == show) 
            return;

        shown = show;
        modelRoot.SetActive(show);
    }

    /// <summary>
    /// Unity message. Hides the arrow if there is no target, followee or model. Otherwise it hovers above the player with a gentle bob and eases its rotation toward the target.
    /// </summary>
    private void LateUpdate()
    {
        if (!hasTarget || follow == null || modelRoot == null)
        {
            SetShown(false);
            return;
        }

        SetShown(true);

        Vector3 arrowPosition = follow.position + Vector3.up * (height + Mathf.Sin(Time.time * 3f) * 0.1f);
        transform.position = arrowPosition;

        Vector3 toTarget = targetPoint - arrowPosition;
        if (toTarget.sqrMagnitude < 0.01f) return;

        Quaternion wanted = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, wanted, 1f - Mathf.Exp(-turnSharpness * Time.deltaTime));
    }
}
