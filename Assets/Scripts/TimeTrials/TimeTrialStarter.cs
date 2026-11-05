using UnityEngine;

/// <summary>
/// The object in the world that starts a time trial. Touching it opens the "start the race?" prompt.
/// </summary>
/// <remarks>
/// Holds the course (hoop points), medal times, optional time limit and respawn rules, and shows a spinning marker above it.
/// Needs a <see cref="SphereCollider"/> on the same GameObject, which is set up as a trigger. Detects the player by its <see cref="PlayerSwinging"/>.
/// </remarks>

[RequireComponent(typeof(SphereCollider))]
public class TimeTrialStarter : MonoBehaviour
{
    [Header("Trial")]
    [Tooltip("unique per trial")]
    public string trialId = "trial_01";
    public string trialName = "Rooftop Run";

    [Header("Course")]
    [Tooltip("Empty transforms, in the order they must be flown through. Each point's blue (forward) arrow is the direction through the hoop. Scale a point to make that hoop bigger or smaller.")]
    public Transform[] hoopPoints;
    public float hoopRadius = 6f;

    [Header("medal times (sec)")]
    public float goldTime = 40f;
    public float silverTime = 55f;
    public float bronzeTime = 75f;

    [Header("time limit")]
    [Tooltip("0 = no time limit")]
    public float timeLimit = 0f;

    [Header("checkpoint respawn")]
    public bool allowRespawn = true;
    public float respawnPenalty = 3f;
    [Tooltip("Speed (m/s) the player is launched at, in the hoop's direction, when respawning at a hoop")]
    public float respawnSpeed = 20f;
    public Transform startRespawnPoint;

    [Header("Interaction")]
    public float triggerRadius = 3f;

    [Header("World marker")]
    public GameObject markerPrefab;
    public float markerScale = 1f;
    public float markerHeight = 1.8f;
    public float markerSpinSpeed = 70f;
    public float markerBobAmount = 0.15f;

    private Transform marker;
    private Renderer[] markerRenderers;

    /// <summary>True if at least one hoop point is assigned.</summary>
    public bool HasValidCourse
    {
        get
        {
            if (hoopPoints == null) return false;
            foreach (Transform point in hoopPoints)
                if (point != null) return true;
            return false;
        }
    }

    /// <summary>Where the player respawns before passing any hoop: the start respawn point if set, otherwise just above this object.</summary>
    public Vector3 StartRespawnPosition
    {
        get
        {
            if (startRespawnPoint != null)
            {
                return startRespawnPoint.position;
            }
            else
            {
                return transform.position + Vector3.up * 1.1f;
            }
        }
    }

    /// <summary>
    /// Gets the medal earned by a finishing time.
    /// </summary>
    /// <param name="time">Finishing time in seconds.</param>
    /// <returns>Gold, silver or bronze if the time is within the matching threshold, otherwise <see cref="TrialMedal.None"/>.</returns>
    public TrialMedal GetMedal(float time)
    {
        if (time <= goldTime) return TrialMedal.Gold;
        if (time <= silverTime) return TrialMedal.Silver;
        if (time <= bronzeTime) return TrialMedal.Bronze;
        return TrialMedal.None;
    }

    /// <summary>Unity editor message (on adding the component). Makes the sphere collider a trigger with <see cref="triggerRadius"/>.</summary>
    private void Reset()
    {
        var sphere = GetComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius = triggerRadius;
    }

    /// <summary>Unity message. Configures the trigger collider and builds the world marker if a prefab is assigned.</summary>
    private void Awake()
    {
        var sphere = GetComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius = triggerRadius;

        if (markerPrefab != null) BuildMarker();
    }

    /// <summary>Unity message. Tells the <see cref="TimeTrialManager"/> when the player enters the trigger.</summary>
    /// <param name="other">The collider that entered.</param>
    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<PlayerSwinging>() == null) return;
        if (TimeTrialManager.Instance != null)
            TimeTrialManager.Instance.OnPlayerEnteredStarter(this);
    }

    /// <summary>Unity message. Tells the <see cref="TimeTrialManager"/> when the player leaves the trigger.</summary>
    /// <param name="other">The collider that left.</param>
    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<PlayerSwinging>() == null) return;
        if (TimeTrialManager.Instance != null)
            TimeTrialManager.Instance.OnPlayerLeftStarter(this);
    }

    // ------------------------------------------------------------------------------ Marker
    private void BuildMarker()
    {
        var markerObject = new GameObject("TrialMarker");
        markerObject.transform.SetParent(transform, false);
        markerObject.transform.localPosition = Vector3.up * markerHeight;
        markerObject.transform.localScale = Vector3.one * markerScale;
        marker = markerObject.transform;

        GameObject model = Instantiate(markerPrefab, marker);
        RaceModelUtils.StripColliders(model);
        markerRenderers = model.GetComponentsInChildren<Renderer>(true);

        RefreshMarker();
    }

    /// <summary>
    /// Updates the marker colour: it keeps its own colour until a medal is earned here, then takes the colour of the best medal. Called after a race finishes.
    /// </summary>
    public void RefreshMarker()
    {
        if (markerRenderers == null) return;

        var best = (TrialMedal)PlayerPrefs.GetInt(TimeTrialManager.BestMedalKey(trialId), 0);
        if (best != TrialMedal.None)
            RaceModelUtils.Tint(markerRenderers, TimeTrialManager.MedalColor(best));
    }

    /// <summary>Unity message. Spins the marker and makes it bob up and down.</summary>
    private void Update()
    {
        if (marker == null) return;

        marker.localRotation = Quaternion.Euler(0f, Time.time * markerSpinSpeed, 0f);
        marker.localPosition = Vector3.up * (markerHeight + Mathf.Sin(Time.time * 2f) * markerBobAmount);
    }

    // -------------------------------------------------------------------- Editor preview

    /// <summary>
    /// Unity editor message. Draws the trigger radius and a preview of the course: lines between the points, a circle per hoop, its direction and its number.
    /// </summary>
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.1f, 0.9f, 1f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, triggerRadius);

        if (hoopPoints == null) return;

        Vector3 previous = transform.position;
        for (int i = 0; i < hoopPoints.Length; i++)
        {
            Transform point = hoopPoints[i];
            if (point == null) continue;

            float radius = hoopRadius * Mathf.Max(0.01f, point.lossyScale.x);
            Gizmos.DrawLine(previous, point.position);
            DrawCircle(point.position, point.rotation, radius);
            Gizmos.DrawLine(point.position, point.position + point.forward * Mathf.Min(radius, 3f));

#if UNITY_EDITOR
            UnityEditor.Handles.Label(point.position + Vector3.up * (radius + 0.5f), (i + 1).ToString());
#endif
            previous = point.position;
        }
    }

    /// <summary>Draws a gizmo circle in the local XY plane of the given rotation.</summary>
    /// <param name="center">Centre of the circle.</param>
    /// <param name="rotation">Orientation; the circle lies in the plane facing its forward axis.</param>
    /// <param name="radius">Circle radius.</param>
    private static void DrawCircle(Vector3 center, Quaternion rotation, float radius)
    {
        const int steps = 32;
        Vector3 last = center + rotation * new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= steps; i++)
        {
            float a = i / (float)steps * Mathf.PI * 2f;
            Vector3 next = center + rotation * new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
            Gizmos.DrawLine(last, next);
            last = next;
        }
    }
}
