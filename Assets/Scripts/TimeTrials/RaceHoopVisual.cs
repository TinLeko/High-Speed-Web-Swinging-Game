using UnityEngine;

/// <summary>
/// The look of one objective hoop. Created by TimeTrialManager
///   Preview   = the hoop after the next one, faint
///   Active    = the hoop to fly through, bright and pulsing, with a tall beam so it is visible from far away
///   Collected = quick expanding fade, then it removes itself
/// </summary>
public class RaceHoopVisual : MonoBehaviour
{
    public enum VisualState { Hidden, Preview, Active, Collected }
    public VisualState StateNow { get; private set; } = VisualState.Hidden;

    private static readonly Color ActiveTint = new Color(0.4f, 0.95f, 1f, 1f);
    private static readonly Color PreviewTint = new Color(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Color PassedTint = new Color(0.4f, 1f, 0.4f, 1f);

    private Transform holder;
    private Renderer[] renderers;
    private string tintProperty = "";
    private float stateTime;

    public void Init(float radius, Quaternion orientation, GameObject modelPrefab, float modelRadius, string customTintProperty)
    {
        tintProperty = customTintProperty;

        var holderObject = new GameObject("Holder");
        holderObject.transform.SetParent(transform, false);
        holderObject.transform.localRotation = orientation;
        holder = holderObject.transform;

        GameObject instance = Instantiate(modelPrefab, holder);
        RaceModelUtils.StripColliders(instance);

        // scale the model so its opening matches the radius that is used for passing the hoop
        float measured;
        if (modelRadius > 0f)
        {
            measured = modelRadius;
        }
        else
        {
            measured = RaceModelUtils.MeasureRadius(holder);
        }

        float factor;
        if (measured > 0.0001f)
        {
            factor = radius / measured;
        }
        else
        {
            factor = 1f;
        }
        instance.transform.localScale *= factor;

        renderers = instance.GetComponentsInChildren<Renderer>(true);
        SetState(VisualState.Hidden);
    }

    public void SetState(VisualState state)
    {
        StateNow = state;
        stateTime = 0f;
        gameObject.SetActive(state != VisualState.Hidden);

        if (renderers == null) return;
        switch (state)
        {
            case VisualState.Active:    RaceModelUtils.Tint(renderers, ActiveTint, tintProperty); break;
            case VisualState.Preview:   RaceModelUtils.Tint(renderers, PreviewTint, tintProperty); break;
            case VisualState.Collected: RaceModelUtils.Tint(renderers, PassedTint, tintProperty); break;
        }
    }

    private void Update()
    {
        if (holder == null) return;
        stateTime += Time.deltaTime;

        switch (StateNow)
        {
            case VisualState.Active:
                holder.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(Time.time * 6f));
                break;

            case VisualState.Preview:
                holder.localScale = Vector3.one * 0.85f;
                break;

            case VisualState.Collected:
                float k = Mathf.Clamp01(stateTime / 0.35f);
                holder.localScale = Vector3.one * (Mathf.Lerp(1f, 1.6f, k) * (1f - k * k));
                if (k >= 1f) Destroy(gameObject);
                break;
        }
    }
}
