using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Web Zip ability: fires a web at whatever the crosshair (or free cursor) points at, pulls the player
/// toward it, and launches them out the other side with a burst of speed.
/// </summary>
/// <remarks>
/// <para>Flow: <c>Update</c> detects the zip input and calls <c>StartZip</c>; <c>FixedUpdate</c> bends the
/// Rigidbody velocity toward the target every physics step until the player arrives, overshoots, times out,
/// hits something or presses jump; <c>FinishZip</c> then hands the exit velocity to
/// <see cref="PlayerSwinging.EndZip"/>.</para>
/// <para>While a zip is active, <see cref="PlayerSwinging"/> and <see cref="Wallrun"/> stand down.</para>
/// <para>Requires <see cref="Rigidbody"/>, <see cref="PlayerSwinging"/> and <see cref="PlayerMovement"/> on the same
/// GameObject, a camera tagged <c>MainCamera</c>, and the input actions "Zip" (falls back to the F key) and "Jump".</para>
/// </remarks>
public class WebZip : MonoBehaviour
{
    [Header("References")]
    private Camera aimCamera;
    public Transform webTip;
    public LineRenderer zipLine;
    public LayerMask zippable;

    [Header("Aiming")]
    public float maxZipDistance = 70f;
    public float minZipDistance = 6f;
    [Tooltip("If the exact cursor ray misses, a fat ray of this radius is tried (aim assist).")]
    public float aimAssistRadius = 0.6f;

    [Header("Zip")]
    [Tooltip("Minimum speed toward the target the instant the zip fires: the 'snap'.")]
    public float zipStartSpeed = 45f;
    public float zipTopSpeed = 90f;
    [Tooltip("How fast the pull speed ramps up (m/s per second).")]
    public float zipAcceleration = 250f;
    [Tooltip("How hard velocity is bent toward the target (m/s per second). Lower = keeps more old momentum.")]
    public float steerAcceleration = 600f;
    public float arriveDistance = 3f;
    public float maxZipTime = 1.5f;
    public float cooldown = 0.6f;
    public float ropeShootSpeed = 300f; // visual only
    public float consecutiveZips = 1f;

    [Header("Exit")]
    [Tooltip("Extra speed added when the zip completes: the 'quick impulse'.")]
    public float exitSpeedBonus = 12f;
    public float maxExitSpeed = 100f;
    [Tooltip("Tilts the exit direction upward so you pop over ledges.")]
    [Range(0f, 0.5f)] public float exitUpBias = 0.15f;
    public float comboGain = 0.5f;
    [Range(0f, 1f)] public float dragRelief = 1f;

    public bool IsZipping { get; private set; }

    private Rigidbody rb;
    private PlayerSwinging swinging;
    private PlayerMovement movement;
    private InputAction zipAction;
    private InputAction jumpAction;

    private Vector3 target;
    private Vector3 launchDir;
    private Vector3 lineEnd;
    private float zipSpeed;
    private float zipStartTime;
    private float nextZipTime;

    /// <summary>
    /// True on the frame the zip button was pressed. Uses the "Zip" input action, or the F key if the action is missing.
    /// </summary>
    private bool ZipPressed
    {
        get
        {
            if (zipAction != null)
            {
                return zipAction.WasPressedThisFrame();
            }
            else
            {
                return Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
            }
        }
    }

    /// <summary>
    /// Unity message. Caches sibling components, looks up the "Zip" and "Jump" input actions and grabs the main camera.
    /// </summary>
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        swinging = GetComponent<PlayerSwinging>();
        movement = GetComponent<PlayerMovement>();
        zipAction = InputSystem.actions.FindAction("Zip");
        jumpAction = InputSystem.actions.FindAction("Jump");
        aimCamera = Camera.main;
    }

    /// <summary>
    /// Unity message. While zipping, a jump press cancels the zip early (and marks the jump as consumed so it is not
    /// reused as a ground jump). Otherwise starts a zip if the button is pressed, the cooldown has elapsed and a valid target is found.
    /// </summary>
    private void Update()
    {
        if (IsZipping)
        {
            if (jumpAction != null && jumpAction.WasPressedThisFrame())
            {
                movement.jumpConsumed = true;
                FinishZip();
            }
            return;
        }

        if (ZipPressed && Time.time >= nextZipTime && TryFindTarget(out Vector3 point))
            StartZip(point);
    }

    /// <summary>
    /// Unity message. Drives the zip physics: checks the end conditions (arrived, overshot, timed out), ramps the pull speed up to
    /// <see cref="zipTopSpeed"/>, bends the current velocity toward the target at <see cref="steerAcceleration"/> so old momentum is
    /// partly kept, and cancels gravity.
    /// </summary>
    private void FixedUpdate()
    {
        if (!IsZipping) return;

        Vector3 toTarget = target - rb.position;
        float dist = toTarget.magnitude;

        bool arrived = dist <= arriveDistance;
        bool overshot = Vector3.Dot(toTarget, launchDir) < 0f;
        bool timedOut = Time.time - zipStartTime > maxZipTime;
        if (arrived || overshot || timedOut)
        {
            FinishZip();
            return;
        }

        Vector3 dir = toTarget / dist;
        zipSpeed = Mathf.Min(zipSpeed + zipAcceleration * Time.fixedDeltaTime, zipTopSpeed);

        // Bend velocity toward the target rather than replacing it, so the pull has weight.
        rb.linearVelocity = Vector3.MoveTowards(
            rb.linearVelocity, dir * zipSpeed, steerAcceleration * Time.fixedDeltaTime);

        // PlayerMovement re-enables gravity every frame while swingMomentum is set, so cancelling it here.
        rb.AddForce(-Physics.gravity, ForceMode.Acceleration);
    }

    /// <summary>
    /// Unity message. Animates the web line shooting out to the target and keeps its start glued to the web tip.
    /// </summary>
    private void LateUpdate()
    {
        if (!IsZipping) return;

        lineEnd = Vector3.MoveTowards(lineEnd, target, ropeShootSpeed * Time.deltaTime);
        zipLine.SetPosition(0, webTip.position);
        zipLine.SetPosition(1, lineEnd);
    }

    /// <summary>
    /// Unity message. Ends the zip if the player bumps into something solid (after a 0.1 s grace period at the start).
    /// The speed is kept but the exit bonus is not applied.
    /// </summary>
    /// <param name="collision">Collision data (unused).</param>
    private void OnCollisionEnter(Collision collision)
    {
        // Something solid is in the way: stop pulling, keep the speed, no reward.
        if (IsZipping && Time.time - zipStartTime > 0.1f)
            FinishZip();
    }

    /// <summary>
    /// Aborts an active zip without applying any exit velocity and hides the line. Called when the player is respawned.
    /// Does nothing if no zip is active.
    /// </summary>
    public void CancelForRespawn()
    {
        if (!IsZipping) return;
        IsZipping = false;
        zipLine.positionCount = 0;
        nextZipTime = Time.time + cooldown;
    }

    // -------------------------------------------------------------------------------- Aiming

    /// <summary>
    /// Builds the aiming ray: from the screen centre when the cursor is locked (or no mouse exists), otherwise from the mouse position.
    /// </summary>
    /// <returns>A world-space ray from the main camera.</returns>
    private Ray GetAimRay()
    {
        bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;
        if (cursorLocked || Mouse.current == null)
            return aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        return aimCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
    }

    /// <summary>
    /// Finds a zip target along the aim ray. The ray starts level with the player (so walls between a third-person camera and the player
    /// are ignored) and tries an exact raycast first, then a sphere cast of <see cref="aimAssistRadius"/> as aim assist.
    /// Hits closer than <see cref="minZipDistance"/> or inside a collider are rejected.
    /// </summary>
    /// <param name="point">The world-space hit point, or <see cref="Vector3.zero"/> if nothing valid was found.</param>
    /// <returns><c>true</c> if a valid target was found.</returns>
    private bool TryFindTarget(out Vector3 point)
    {
        point = Vector3.zero;
        Ray ray = GetAimRay();

        // Third-person cameras sit behind the player. Start the ray level with the player so
        // walls between the camera and the player can't be picked as targets.
        float skip = Mathf.Max(0f, Vector3.Dot(transform.position - ray.origin, ray.direction));
        Vector3 origin = ray.origin + ray.direction * skip;

        RaycastHit hit;
        bool found = Physics.Raycast(origin, ray.direction, out hit, maxZipDistance, zippable, QueryTriggerInteraction.Ignore) ||
            Physics.SphereCast(origin, aimAssistRadius, ray.direction, out hit, maxZipDistance, zippable, QueryTriggerInteraction.Ignore);

        // distance 0 means the sphere started inside a collider and hit.point is meaningless.
        if (!found || hit.distance <= 0f) return false;
        if (Vector3.Distance(transform.position, hit.point) < minZipDistance) return false;

        point = hit.point;
        return true;
    }

    // ------------------------------------------------------------------------------------ Zip

    /// <summary>
    /// Begins a zip toward <paramref name="point"/>: increments the chained-zip counter, cancels any swing, switches to scripted momentum,
    /// sets the initial pull speed (at least <see cref="zipStartSpeed"/>) and starts the line animation.
    /// </summary>
    /// <param name="point">World-space target position.</param>
    private void StartZip(Vector3 point)
    {
        consecutiveZips++;
        target = point;
        IsZipping = true;
        zipStartTime = Time.time;

        swinging.CancelSwingForZip();
        swinging.BeginZipMomentum();

        launchDir = (target - rb.position).normalized;
        zipSpeed = Mathf.Max(zipStartSpeed, Vector3.Dot(rb.linearVelocity, launchDir));

        lineEnd = webTip.position;
        zipLine.positionCount = 2;
    }

    /// <summary>
    /// Ends the zip and launches the player. Tilts the direction upward by <see cref="exitUpBias"/>, adds <see cref="exitSpeedBonus"/>
    /// (capped at <see cref="maxExitSpeed"/>), divides the result by <see cref="consecutiveZips"/> so chained zips give diminishing speed,
    /// and passes the exit velocity to <see cref="PlayerSwinging.EndZip"/>. Also starts the cooldown and hides the line.
    /// </summary>
    private void FinishZip()
    {
        IsZipping = false;
        nextZipTime = Time.time + cooldown;
        zipLine.positionCount = 0;

        Vector3 velocity = rb.linearVelocity;
        float speed = velocity.magnitude;
        Vector3 dir = speed > 0.1f ? velocity / speed : launchDir;

        dir = (dir + Vector3.up * exitUpBias).normalized;
        speed = Mathf.Min(speed + exitSpeedBonus, maxExitSpeed);

        Vector3 exitVelocity = dir * speed / consecutiveZips;

        swinging.EndZip(exitVelocity, comboGain, dragRelief);
    }
}
