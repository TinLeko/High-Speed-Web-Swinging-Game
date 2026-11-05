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
    public Wallrun wallRun;
    public PlayerSwinging swinging;
    public CinemachineOrbitalFollow orbital;

    [Header("Direction")]
    public float yawDirection = 1f;
    [Tooltip("+1 means a larger Vertical Axis value tilts the view down. -1 tilts it up instead.")]
    public float pitchDirection = 1f;

    [Header("Tilt")]
    [Tooltip("Degrees the view tilts down while diving.")]
    public float diveTiltDown = 12f;
    [Tooltip("Optional, 0 = off. Degrees the view tilts up while running straight up a wall.")]
    public float climbTiltUp = 0f;
    [Tooltip("Higher = the tilt arrives and leaves faster.")]
    public float tiltSharpness = 6f;

    private float pitchOffset; // degrees currently applied, positive = down, ngeative = up

    private void OnEnable()
    {
        TimeTrialManager.PlayerTeleported += OnPlayerTeleported;
    }

    private void OnDisable()
    {
        TimeTrialManager.PlayerTeleported -= OnPlayerTeleported;
    }

    private void OnPlayerTeleported(Transform target, Vector3 delta)
    {
        CinemachineCore.OnTargetObjectWarped(target, delta);
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        float targetPitch = 0f;
        if (swinging != null && swinging.IsDiving)
            targetPitch += diveTiltDown;
        if (wallRun != null && wallRun.IsActive && wallRun.Mode == Wallrun.WallMode.Climbing)
            targetPitch -= climbTiltUp;

        // Apply the change in tilt each frame, so mouse look keeps working and nothing is overwritten.
        float newOffset = Mathf.Lerp(pitchOffset, targetPitch, 1f - Mathf.Exp(-tiltSharpness * dt));
        float pitchDelta = (newOffset - pitchOffset) * pitchDirection;
        pitchOffset = newOffset;
        float yawDelta = wallRun != null ? wallRun.ConsumeYawDelta() * yawDirection : 0f;

        if (!Mathf.Approximately(yawDelta, 0f))
        {
            var h = orbital.HorizontalAxis;
            float min = h.Range.x;
            float max = h.Range.y;
            h.Value = Mathf.Repeat(h.Value + yawDelta - min, max - min) + min;
            orbital.HorizontalAxis = h;
        }

        if (!Mathf.Approximately(pitchDelta, 0f))
        {
            var v = orbital.VerticalAxis;
            v.Value = Mathf.Clamp(v.Value + pitchDelta, v.Range.x, v.Range.y);
            orbital.VerticalAxis = v;
        }
    }
}
