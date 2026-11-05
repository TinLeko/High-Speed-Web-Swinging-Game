using UnityEngine;

/// <summary>
/// The look of one race hoop. Created by <see cref="TimeTrialManager"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Preview: the hoop after the next one, faint and slightly smaller.</description></item>
/// <item><description>Active: the hoop to fly through, bright and pulsing.</description></item>
/// <item><description>Collected: a quick expanding fade, then the object removes itself.</description></item>
/// </list>
/// </remarks>
public class RaceHoopVisual : MonoBehaviour
{
    /// <summary>Visual state of the hoop.</summary>
    public enum VisualState { Hidden, Preview, Active, Collected }

    /// <summary>The current visual state.</summary>
    public VisualState StateNow { get; private set; } = VisualState.Hidden;


    private static readonly Color ActiveTint = new Color(0.4f, 0.95f, 1f, 1f);
    private static readonly Color PreviewTint = new Color(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Color PassedTint = new Color(0.4f, 1f, 0.4f, 1f);

    private Transform holder;
    private Renderer[] renderers;
    private string tintProperty = "";
    private float stateTime;

    /// <summary>
    /// Builds the visual: instantiates the model (colliders removed) under a rotated holder and scales it so its opening matches the pass radius.
    /// Starts hidden.
    /// </summary>
    /// <param name="radius">The radius used for passing the hoop, in world units.</param>
    /// <param name="orientation">World rotation of the hoop; its forward is the direction of passing.</param>
    /// <param name="modelPrefab">The hoop model.</param>
    /// <param name="modelRadius">Opening radius of the model, or 0 to measure it automatically.</param>
    /// <param name="customTintProperty">Extra shader colour property to tint, or empty.</param>
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

    /// <summary>Switches state: resets the state timer, shows or hides the object and tints the model to match (cyan active, grey preview, green collected).</summary>
    /// <param name="state">The new state.</param>
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

    /// <summary>
    /// Unity message. Animates the holder scale: pulse when active, shrunk when preview, expand and shrink then self-destruct when collected.
    /// </summary>
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
