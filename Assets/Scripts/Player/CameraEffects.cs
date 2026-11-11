using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Camera effects for a Cinemachine 3 Orbital Follow camera. Put on any object in the scene.
///  - Turns the camera with wall-run corners and swings it around when you jump off a wall
///    (WallRun.ConsumeYawDelta supplies both).
///  - Tilts the view down while diving, and up while running straight up a wall.
/// </summary>
public class CameraEffects : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private Wallrun wallRun;
    [SerializeField]
    private PlayerSwinging swinging;
    [SerializeField]
    private CinemachineOrbitalFollow orbital;

    [Header("Tilt")]
    [SerializeField]
    [Tooltip("Degrees the view tilts down while diving.")]
    private float diveTiltDown = 12f;
    [SerializeField]
    [Tooltip("Degrees the view tilts up while running straight up a wall.")]
    private float climbTiltUp = 0f;
    [SerializeField]
    [Tooltip("Higher = the tilt arrives and leaves faster.")]
    private float tiltSharpness = 6f;
    [SerializeField]
    [Tooltip("Degrees currently applied, positive = down, ngeative = up")]
    private float pitchOffset = 0f;

    /// <summary>Unity message. Subscribes to <c>TimeTrialManager.PlayerTeleported</c> so the camera follows teleports without a visible swoosh.</summary>
    private void OnEnable()
    {
        TimeTrialManager.PlayerTeleported += OnPlayerTeleported;
    }

    /// <summary>Unity message. Unsubscribes from <c>TimeTrialManager.PlayerTeleported</c>.</summary>
    private void OnDisable()
    {
        TimeTrialManager.PlayerTeleported -= OnPlayerTeleported;
    }

    /// <summary>Event handler. Tells Cinemachine the follow target was warped so it moves the camera by the same offset instead of interpolating.</summary>
    /// <param name="target">The transform that was teleported.</param>
    /// <param name="delta">The world-space displacement of the teleport.</param>
    private void OnPlayerTeleported(Transform target, Vector3 delta)
    {
        CinemachineCore.OnTargetObjectWarped(target, delta);
    }

    /// <summary>
    /// Unity message. Eases a pitch offset toward the target tilt (dive tilts down, climb optionally tilts up), then applies this frame's
    /// change in tilt to the orbital Vertical Axis and the wall-run yaw delta to the Horizontal Axis. The horizontal axis wraps around its
    /// range; the vertical axis is clamped to it.
    /// </summary>
    private void Update()
    {
        float targetPitch = 0f;
        if (swinging != null && swinging.IsDiving)
            targetPitch += diveTiltDown;
        if (wallRun != null && wallRun.IsActive && wallRun.Mode == Wallrun.WallMode.Climbing)
            targetPitch -= climbTiltUp;

        // Apply the change in tilt each frame, so mouse look keeps working and nothing is overwritten.
        float newOffset = Mathf.Lerp(pitchOffset, targetPitch, 1f - Mathf.Exp(-tiltSharpness * Time.deltaTime));
        float pitchDelta = (newOffset - pitchOffset);
        pitchOffset = newOffset;

        float yawDelta;
        if (wallRun != null)
        {
            yawDelta = wallRun.ConsumeYawDelta();
        }
        else
        {
            yawDelta = 0f;
        }

        if (!Mathf.Approximately(yawDelta, 0f))
        {
            float min = orbital.HorizontalAxis.Range.x;
            float max = orbital.HorizontalAxis.Range.y;
            orbital.HorizontalAxis.Value = Mathf.Repeat(orbital.HorizontalAxis.Value + yawDelta - min, max - min) + min;
        }

        if (!Mathf.Approximately(pitchDelta, 0f))
        {
            orbital.VerticalAxis.Value = Mathf.Clamp(orbital.VerticalAxis.Value + pitchDelta, orbital.VerticalAxis.Range.x, orbital.VerticalAxis.Range.y);
        }
    }
}
