using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Speed-based camera feel: the faster the target moves, the wider the field of view and the farther the camera sits.
/// Both change with a frame-rate-independent exponential ease.
/// </summary>
public class CameraSmoothing : MonoBehaviour
{
    [SerializeField]
    private CinemachineCamera camera;
    [SerializeField]
    private CinemachineOrbitalFollow orbitalFollow;
    [SerializeField]
    private Rigidbody target;

    [SerializeField]
    private float maxSpeed = 48f;
    [SerializeField]
    private float minFov = 75f, maxFov = 100f;
    [SerializeField]
    private float minRadius = 5f, maxRadius = 8f;
    [SerializeField]
    private float smoothing = 5f;

    /// <summary>
    /// Unity message. Maps the target's speed (relative to <c>maxSpeed</c>) to a field of view and an orbit radius, then eases the camera
    /// lens and the Orbital Follow radius toward them.
    /// </summary>
    void LateUpdate()
    {
        float currentSpeed = target.linearVelocity.magnitude;
        float speedPercent = Mathf.Clamp01(currentSpeed / maxSpeed);
        float smoothingStep = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        float targetFov = Mathf.Lerp(minFov, maxFov, speedPercent);
        float targetRadius = Mathf.Lerp(minRadius, maxRadius, speedPercent);

        camera.Lens.FieldOfView = Mathf.Lerp(camera.Lens.FieldOfView, targetFov, smoothingStep);
        orbitalFollow.Radius = Mathf.Lerp(orbitalFollow.Radius, targetRadius, smoothingStep);
    }
}
