using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Wall run, climb and crawl.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Run key held, fast, hitting the wall at an angle: <see cref="WallMode.Running"/> (sideways, follows corners).</description></item>
/// <item><description>Run key held, fast, hitting the wall head-on: <see cref="WallMode.Climbing"/> (straight up).</description></item>
/// <item><description>Walking (or falling) slowly into a wall, no key needed: <see cref="WallMode.Crawling"/> (free movement on the wall).</description></item>
/// </list>
/// Jump leaps off the wall at any time. Entry and exit hand control to <see cref="PlayerSwinging"/>, which owns the post-wall momentum.
/// Requires <see cref="Rigidbody"/>, <see cref="PlayerSwinging"/>, <see cref="PlayerMovement"/> and <see cref="WebZip"/> on the same GameObject.
/// </remarks>
public class Wallrun : MonoBehaviour
{
    public enum WallMode { None, Running, Climbing, Crawling }

    [Header("References")]
    [SerializeField] private Transform cameraTransform;
    public LayerMask wallMask;

    [Header("Detection")]
    [Tooltip("Must be smaller than your collider's radius, or the probe starts inside the wall.")]
    [SerializeField] private float probeRadius = 0.3f;
    [SerializeField] private float probeDistance = 1.2f;
    [Tooltip("Surfaces whose normal tilts more than this up/down are not walls (0 = perfectly vertical).")]
    [Range(0f, 0.5f)] [SerializeField] private float maxWallNormalY = 0.25f;
    [Tooltip("Hit the wall within this many degrees of head-on and you climb instead of run.")]
    [SerializeField] private float headOnAngle = 35f;
    [Tooltip("How long the wall may be missing before you fall off or vault over the top.")]
    [SerializeField] private float lostContactGrace = 0.15f;
    [SerializeField] private float reattachDelay = 0.3f;
    [Tooltip("Smooths the wall normal so bumpy or faceted walls don't wobble the run direction. Lower = smoother.")]
    [SerializeField] private float normalSmoothing = 15f;

    [Header("Speed Thresholds (horizontal m/s)")]
    [Tooltip("Need at least this much speed (and the run key) to wall run or climb. Below it you crawl.")]
    [SerializeField] private float minRunSpeed = 18f;
    [Tooltip("A run or climb that decays below this turns into a crawl.")]
    [SerializeField] private float dropToCrawlSpeed = 10f;

    [Header("Wall Run (sideways)")]
    [SerializeField] private float maxRunSpeed = 70f;
    [Tooltip("m/s lost per second while running. 0 = lasts as long as the key is held.")]
    [SerializeField] private float runSpeedDecay = 1f;

    [Header("Wall Climb (head-on)")]
    [Tooltip("Fraction of your horizontal speed that becomes upward speed.")]
    [SerializeField] private float climbConversion = 0.9f;
    [SerializeField] private float maxClimbSpeed = 45f;
    [Tooltip("m/s lost per second while climbing. 0 = lasts as long as the key is held.")]
    [SerializeField] private float climbSpeedDecay = 0f;
    [SerializeField] private float climbSteerSpeed = 6f;

    [Header("Wall Crawl")]
    [SerializeField] private float crawlSpeed = 9f;
    [Tooltip("How close you must be when walking into a wall for the crawl to grab on.")]
    [SerializeField] private float crawlAttachDistance = 0.7f;
    [Tooltip("1 = must walk straight at the wall, 0 = any angle. Stops you sticking to walls you only brush past.")]
    [Range(0f, 1f)][SerializeField] private float crawlApproachDot = 0.5f;

    [Header("Corners (wall run)")]
    [Tooltip("How far ahead / around the edge to look for the next wall.")]
    [SerializeField] private float cornerReach = 2f;
    [Tooltip("Extra steering right after a corner so the turn is tight even at high speed.")]
    [SerializeField] private float cornerSteerAcceleration = 1500f;
    [SerializeField] private float cornerSteerTime = 0.25f;
    [Tooltip("Minimum time between two corner turns. Stops back-and-forth flipping on busy facades.")]
    [SerializeField] private float cornerCooldown = 0.6f;
    [Tooltip("The wall must be gone this long before a convex corner is attempted (skips window gaps).")]
    [SerializeField] private float outerCornerConfirm = 0.06f;
    [Tooltip("A wall ahead only counts as a corner if it also exists this far above and below (rejects pillars, balconies).")]
    [SerializeField] private float cornerHeightCheck = 1.5f;

    [Header("Camera")]
    [Tooltip("How much of the wall run's turning is handed to the camera. 0 = none, 1 = fully follows.")]
    [Range(0f, 1f)][SerializeField] private float cameraFollow = 1f;
    [Tooltip("Higher = the camera catches up with the turn faster.")]
    [SerializeField] private float cameraTurnSharpness = 8f;
    [Tooltip("Ignore wall-heading changes smaller than this per physics step (degrees). Filters mesh noise.")]
    [SerializeField] private float cameraDeadzone = 0.05f;
    [Tooltip("Fastest the camera may be turned by a wall run (degrees per second).")]
    [SerializeField] private float maxCameraTurnRate = 240f;

    [Header("Sticking")]
    [Tooltip("Constant speed into the wall so you stay in contact.")]
    [SerializeField] private float stickSpeed = 3f;
    [Tooltip("How quickly velocity bends toward the wall velocity (m/s per second).")]
    [SerializeField] private float steerAcceleration = 400f;
    [Tooltip("A run/climb that is blocked (ceiling, corner) for this long drops you off.")]
    [SerializeField] private float stuckTime = 0.4f;

    [Header("Leap (jump off the wall)")]
    [SerializeField] private float leapAwaySpeed = 14f;
    [SerializeField] private float leapUpSpeed = 14f;
    [Tooltip("Fraction of your speed along the wall that survives the leap.")]
    [Range(0f, 1f)] [SerializeField] private float leapMomentumKeep = 0.8f;
    [Tooltip("Upward speed above this is moved away from the wall instead (stops rocket-straight-up leaps).")]
    [SerializeField] private float maxLeapUpSpeed = 28f;
    [SerializeField] private float maxExitSpeed = 100f;
    [SerializeField] private float leapComboGain = 0.5f;
    [Range(0f, 1f)][SerializeField] private float leapDragRelief = 0.8f;

    [Header("Ledge Vault")]
    [Tooltip("When a climb or crawl reaches the top of the wall, extra speed over the edge.")]
    [SerializeField] private float ledgeVaultSpeed = 8f;

    // Read these from other scripts (camera, animation, UI...)

    /// <summary>True while the player is attached to a wall in any mode.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The current <see cref="WallMode"/>, or <see cref="WallMode.None"/> when not on a wall.</summary>
    public WallMode Mode { get; private set; } = WallMode.None;

    /// <summary>The (smoothed) normal of the wall the player is attached to.</summary>
    public Vector3 WallNormal => wallNormal;

    /// <summary>The direction of travel along the wall while running.</summary>
    public Vector3 RunDirection => runDirection;

    private Rigidbody rigidBody;
    private PlayerSwinging swinging;
    private PlayerMovement movement;
    private WebZip zip;
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction runAction;
    private Vector3 wallNormal;
    private Vector3 runDirection;
    private float wallSpeed;
    private float attachTime;
    private float nextAttachTime;
    private float lostContactTimer;
    private float stuckTimer;
    private float cornerTimer;
    private float pendingYaw;
    private float lastCornerTime = -999f;

    /// <summary>True while the run ("Sprint") action is held.</summary>
    private bool RunHeld => runAction != null && runAction.IsPressed();

    /// <summary>Unity message. Caches sibling components and looks up the "Move", "Jump" and "Sprint" input actions.</summary>
    private void Awake()
    {
        rigidBody = GetComponent<Rigidbody>();
        swinging = GetComponent<PlayerSwinging>();
        movement = GetComponent<PlayerMovement>();
        zip = GetComponent<WebZip>();
        moveAction = InputSystem.actions.FindAction("Move");
        jumpAction = InputSystem.actions.FindAction("Jump");
        runAction = InputSystem.actions.FindAction("Sprint");
    }

    /// <summary>Unity message. Triggers a leap when jump is pressed while attached to a wall.</summary>
    private void Update()
    {
        if (IsActive && jumpAction != null && jumpAction.WasPressedThisFrame())
            Leap();
    }

    /// <summary>Unity message. Steps the wall physics while attached, otherwise checks whether to attach.</summary>
    private void FixedUpdate()
    {
        if (IsActive) 
            StepWall();
        else TryAttach();
    }

    /// <summary>
    /// Gives the yaw the camera should turn this frame (degrees) to follow a wall-run corner.
    /// (see <c>CameraEffects</c>). The stored turn is eased out exponentially and limited by <see cref="maxCameraTurnRate"/>.
    /// </summary>
    /// <returns>Degrees to add to the camera yaw this frame; zero when no turn is pending.</returns>
    public float ConsumeYawDelta()
    {
        float step = pendingYaw * (1f - Mathf.Exp(-cameraTurnSharpness * Time.deltaTime));
        step = Mathf.Clamp(step, -(maxCameraTurnRate * Time.deltaTime), maxCameraTurnRate * Time.deltaTime);
        pendingYaw -= step;
        return step;
    }

    /// <summary>
    /// Leaves the wall state without touching velocity or player state, and starts the re-attach delay.
    /// Used when swinging or zipping takes over.
    /// </summary>
    public void Cancel()
    {
        IsActive = false;
        Mode = WallMode.None;
        nextAttachTime = Time.time + reattachDelay;
    }

    // ------------------------------------------------------------------------------- Attach

    /// <summary>
    /// Checks whether to attach to a wall this physics step. Fast movement (with run held) probes ahead and to both sides and picks
    /// Climbing (head-on, within <see cref="headOnAngle"/>) or Running (grounded players cannot start a run). Slow, grounded movement into a wall
    /// within <see cref="crawlAttachDistance"/> starts Crawling. On success it sets the initial wall speed and state, then calls
    /// <see cref="PlayerSwinging.BeginZipMomentum"/>. Does nothing during the re-attach delay, a swing or a zip.
    /// </summary>
    private void TryAttach()
    {
        if (Time.time < nextAttachTime) 
            return;
        if (movement.isSwinging || (zip != null && zip.IsZipping)) 
            return;

        Vector3 flatVelocity = new Vector3(rigidBody.linearVelocity.x, 0f, rigidBody.linearVelocity.z);
        float flatSpeed = flatVelocity.magnitude;

        // Wall run / climb need the run key. Crawling doesn't, just walk into the wall.
        if ((flatSpeed >= minRunSpeed) && !RunHeld) 
            return;

        RaycastHit hit;
        if (flatSpeed >= minRunSpeed)
        {
            // Probe ahead far enough to catch the wall before collision eats speed
            Vector3 forward = flatVelocity / flatSpeed;
            if (!ProbeWall(forward, probeDistance + flatSpeed * Time.fixedDeltaTime, out hit))
            {
                // Running parallel to a wall: check both sides.
                if (!ProbeWall(Vector3.Cross(Vector3.up, forward), probeDistance, out hit) && !ProbeWall(-Vector3.Cross(Vector3.up, forward), probeDistance, out hit))
                    return;
            }
        }
        else
        {
            if (!movement.IsGrounded) 
                return;

            Vector3 inputDirection = GetInputDirection();
            if (inputDirection == Vector3.zero) 
                return;

            if (!ProbeWall(inputDirection, crawlAttachDistance, out hit)) 
                return;

            if (Vector3.Dot(inputDirection, -new Vector3(hit.normal.x, 0f, hit.normal.z).normalized) < crawlApproachDot) 
                return;
        }

        WallMode mode;
        if (!(flatSpeed >= minRunSpeed))
        {
            mode = WallMode.Crawling;
        }
        else
        {
            if (Vector3.Angle(-new Vector3(hit.normal.x, 0f, hit.normal.z).normalized, flatVelocity) <= headOnAngle)
            {
                mode = WallMode.Climbing;
            }
            else
            {
                mode = WallMode.Running;
            }
        }

        if (mode == WallMode.Running && movement.IsGrounded) 
            return;

        if (mode == WallMode.Running)
        {
            Vector3 along = Vector3.ProjectOnPlane(flatVelocity, hit.normal);
            if (along.sqrMagnitude < 0.0001f) return;
            runDirection = along.normalized;
            wallSpeed = Mathf.Min(flatSpeed, maxRunSpeed);
        }
        else if (mode == WallMode.Climbing)
        {
            wallSpeed = Mathf.Min(flatSpeed * climbConversion, maxClimbSpeed);
        }
        else
        {
            wallSpeed = 0f;
        }

        wallNormal = hit.normal;
        Mode = mode;
        IsActive = true;
        attachTime = Time.time;
        lostContactTimer = 0f;
        stuckTimer = 0f;
        cornerTimer = 0f;
        pendingYaw = 0f;
        lastCornerTime = -999f;
        swinging.BeginZipMomentum();
    }

    /// <summary>Sphere-casts from the player to find a wall. Rejects hits that start inside a collider and surfaces tilted more than <see cref="maxWallNormalY"/>.</summary>
    /// <param name="dir">Direction to probe.</param>
    /// <param name="distance">Maximum probe distance.</param>
    /// <param name="hit">The hit information when a wall was found.</param>
    /// <returns><c>true</c> if a valid wall was hit.</returns>
    private bool ProbeWall(Vector3 dir, float distance, out RaycastHit hit)
    {
        // hit.distance == 0 means the sphere started inside a collider, normal is meaningless then
        return Physics.SphereCast(rigidBody.position, probeRadius, dir, out hit, distance, wallMask,
                   QueryTriggerInteraction.Ignore)
               && hit.distance > 0f
               && Mathf.Abs(hit.normal.y) <= maxWallNormalY;
    }

    // ------------------------------------------------------------------------ While on wall

    /// <summary>
    /// Per-physics-step update while on a wall: cancels if swing or zip took over; releases if the run key (or, when crawling, any input) is gone;
    /// tries inner corners; re-probes and smooths the wall normal; handles losing the wall (outer corner, or vaulting over the top with
    /// <see cref="ledgeVaultSpeed"/>); builds the target wall velocity for the current mode with speed decay and downgrade to crawling;
    /// drops the player if blocked for <see cref="stuckTime"/>; ends on reaching the ground; then steers velocity toward the target
    /// (with extra steering just after a corner) while cancelling gravity.
    /// </summary>
    private void StepWall()
    {
        // swing or zip took over
        if (movement.isSwinging || (zip != null && zip.IsZipping))
        {
            Cancel();
            return;
        }

        if (zip != null)
            zip.consecutiveZips = 0;

        Vector2 input;
        if (moveAction != null)
        {
            input = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
        }
        else
        {
            input = Vector2.zero;
        }

        bool keepClinging;
        if (Mode == WallMode.Crawling)
        {
            keepClinging = RunHeld || input.sqrMagnitude > 0.01f;
        }
        else
        {
            keepClinging = RunHeld;
        }

        if (!keepClinging)
        {
            EndWall(rigidBody.linearVelocity, 0f, 0f);
            return;
        }

        Vector3 runBefore = runDirection;

        // Inner corner: a wall right ahead while running.
        if (Mode == WallMode.Running)
            TryInnerCorner();

        // Re-probe so the wall normal follows curves (smoothed, so bumpy walls don't wobble the run).
        if (ProbeWall(-wallNormal, probeDistance, out RaycastHit hit))
        {
            wallNormal = Vector3.Slerp(wallNormal, hit.normal, (1f - Mathf.Exp(-normalSmoothing * Time.fixedDeltaTime))).normalized;
            lostContactTimer = 0f;
        }
        else
        {
            lostContactTimer += Time.fixedDeltaTime;

            // Wall gone: only look for a convex corner once it is clearly more than a gap.
            if (Mode == WallMode.Running && lostContactTimer >= outerCornerConfirm && TryOuterCorner())
            {
                lostContactTimer = 0f;
            }
            else if (lostContactTimer > lostContactGrace)
            {
                // Reached the top of the wall
                Vector3 exit = rigidBody.linearVelocity;
                if ((Mode == WallMode.Climbing || Mode == WallMode.Crawling) && exit.y > 1f)
                    exit += -wallNormal * ledgeVaultSpeed;
                EndWall(exit, 0f, 0f);
                return;
            }
        }

        // Wall-plane basis. Facing the wall: wallRight is your right, wallUp is up along the wall.
        Vector3 wallRight = Vector3.Cross(wallNormal, Vector3.up).normalized;
        Vector3 wallUp = Vector3.Cross(wallRight, wallNormal);

        Vector3 targetWallVelocity;
        switch (Mode)
        {
            case WallMode.Running:
                wallSpeed = Mathf.Max(0f, wallSpeed - runSpeedDecay * Time.fixedDeltaTime);
                runDirection = Vector3.ProjectOnPlane(runDirection, wallNormal).normalized;
                targetWallVelocity = runDirection * wallSpeed;
                AddCameraYaw(runBefore, runDirection);
                if (wallSpeed < dropToCrawlSpeed) Mode = WallMode.Crawling;
                break;

            case WallMode.Climbing:
                wallSpeed = Mathf.Max(0f, wallSpeed - climbSpeedDecay * Time.fixedDeltaTime);
                targetWallVelocity = wallUp * wallSpeed + wallRight * (input.x * climbSteerSpeed);
                if (wallSpeed < dropToCrawlSpeed) Mode = WallMode.Crawling;
                break;

            default: // crawling
                targetWallVelocity = (wallUp * input.y + wallRight * input.x) * crawlSpeed;
                break;
        }

        // Blocked by a ceiling or a corner that could not be followed: let go.
        if ((Mode == WallMode.Running || Mode == WallMode.Climbing) && Time.time - attachTime > 0.3f)
        {
            Vector3 direction;
            if (targetWallVelocity.sqrMagnitude > 0.01f)
            {
                direction = targetWallVelocity.normalized;
            }
            else
            {
                direction = Vector3.zero;
            }

            if (Vector3.Dot(rigidBody.linearVelocity, direction) < wallSpeed * 0.2f)
            {
                stuckTimer = stuckTimer + Time.fixedDeltaTime;
            }
            else
            {
                stuckTimer = 0f;
            }

            if (stuckTimer > stuckTime)
            {
                EndWall(rigidBody.linearVelocity, 0f, 0f);
                return;
            }
        }

        // Reached the ground (and not climbing away from it).
        if (movement.IsGrounded && Time.time - attachTime > 0.2f && rigidBody.linearVelocity.y <= 0.5f)
        {
            EndWallOnGround();
            return;
        }

        targetWallVelocity -= wallNormal * stickSpeed;

        cornerTimer -= Time.fixedDeltaTime;

        float currentAcceleration;
        if (cornerTimer > 0f)
        {
            currentAcceleration = cornerSteerAcceleration;
        }
        else
        {
            currentAcceleration = steerAcceleration;
        }

        rigidBody.linearVelocity = Vector3.MoveTowards(rigidBody.linearVelocity, targetWallVelocity, currentAcceleration * Time.fixedDeltaTime);

        // PlayerMovement re-enables gravity every frame while swingMomentum is set, so cancelling it here.
        rigidBody.AddForce(-Physics.gravity, ForceMode.Acceleration);
    }

    // ------------------------------------------------------------------------------- Corners

    /// <summary>
    /// Detects a concave corner (a wall right ahead while running). Requires a roughly head-on face that also exists above and below the player
    /// (rejects pillars and balconies), then turns onto it, heading away from the old wall. Respects <see cref="cornerCooldown"/>.
    /// </summary>
    /// <returns><c>true</c> if the run switched to the new wall.</returns>
    private bool TryInnerCorner()
    {
        if (Time.time - lastCornerTime < cornerCooldown) 
            return false;

        float reachDistance = cornerReach + wallSpeed * 0.1f;
        if (!ProbeWall(runDirection, reachDistance, out RaycastHit ahead)) 
            return false;

        if (Vector3.Dot(ahead.normal, -runDirection) < 0.5f) 
            return false;

        for (int i = -1; i <= 1; i += 2)
        {
            if (!Physics.Raycast(rigidBody.position + (Vector3.up * cornerHeightCheck) * i, runDirection, out RaycastHit extra, reachDistance + probeRadius, wallMask, QueryTriggerInteraction.Ignore) || Vector3.Dot(extra.normal, ahead.normal) < 0.9f)
                return false;
        }

        SwitchWall(ahead.normal, true);
        return true;
    }

    /// <summary>
    /// Detects a convex corner after the wall ends: sphere-casts back around the edge for a wall that faces the direction of travel
    /// and switches to it. Respects <see cref="cornerCooldown"/>.
    /// </summary>
    /// <returns><c>true</c> if the run switched to the wall around the corner.</returns>
    private bool TryOuterCorner()
    {
        if (Time.time - lastCornerTime < cornerCooldown)
            return false;

        float reachDistance = cornerReach + 1f + wallSpeed * lostContactTimer;
        if (!Physics.SphereCast(rigidBody.position - wallNormal * cornerReach + runDirection * 0.5f, probeRadius, -runDirection, out RaycastHit around, reachDistance, wallMask, QueryTriggerInteraction.Ignore))
            return false;

        if (around.distance <= 0f || Mathf.Abs(around.normal.y) > maxWallNormalY) 
            return false;

        if (Vector3.Dot(around.normal, runDirection) < 0.3f) 
            return false; // must face the way we are heading

        SwitchWall(around.normal, false);
        return true;
    }

    /// <summary>
    /// Makes <paramref name="newNormal"/> the current wall: computes the run direction along it (away from the old wall for inner corners,
    /// around into it for outer corners) and starts the temporary high-steering window and corner cooldown.
    /// </summary>
    /// <param name="newNormal">Normal of the new wall.</param>
    /// <param name="isConcave"><c>true</c> for a concave (inner) corner, <c>false</c> for a convex (outer) one.</param>
    private void SwitchWall(Vector3 newNormal, bool isConcave)
    {
        // Along the new wall, heading away from the old wall (inner) or around into it (outer).
        Vector3 tangent = Vector3.Cross(newNormal, Vector3.up).normalized;
        Vector3 reference;
        if (isConcave)
        {
            reference = wallNormal;
        }
        else
        {
            reference = -wallNormal;
        }

        if (Vector3.Dot(tangent, reference) < 0f) 
            tangent = -tangent;

        wallNormal = newNormal;
        runDirection = tangent;
        stuckTimer = 0f;
        cornerTimer = cornerSteerTime;
        lastCornerTime = Time.time;
    }

    /// <summary>
    /// Adds the horizontal heading change between two run directions to the pending camera yaw, scaled by <see cref="cameraFollow"/>.
    /// Changes below <see cref="cameraDeadzone"/> are ignored to filter mesh noise.
    /// </summary>
    /// <param name="before">Run direction before the step.</param>
    /// <param name="after">Run direction after the step.</param>
    private void AddCameraYaw(Vector3 before, Vector3 after)
    {
        Vector3 a = new Vector3(before.x, 0f, before.z);
        Vector3 b = new Vector3(after.x, 0f, after.z);

        if (a.sqrMagnitude < 0.0001f || b.sqrMagnitude < 0.0001f)
            return;

        float delta = Vector3.SignedAngle(a, b, Vector3.up);

        if (Mathf.Abs(delta) < cameraDeadzone)
            return;

        pendingYaw += delta * cameraFollow;
    }

    // ----------------------------------------------------------------------- Leaving the wall

    /// <summary>
    /// Jumps off the wall. Keeps part of the speed along the wall, adds push away from and up off the wall, redirects excessive upward speed
    /// away from the wall, clamps to <see cref="maxExitSpeed"/> and marks the jump press as consumed. Running and climbing leaps earn combo
    /// and drag relief; crawl leaps do not.
    /// </summary>
    private void Leap()
    {
        Vector3 exit = (Vector3.ProjectOnPlane(rigidBody.linearVelocity, wallNormal) * leapMomentumKeep) + wallNormal * leapAwaySpeed + Vector3.up * leapUpSpeed;

        // A fast climb would otherwise launch you straight up: send the excess away from the wall.
        if (exit.y > maxLeapUpSpeed)
        {
            float excess = exit.y - maxLeapUpSpeed;
            exit.y = maxLeapUpSpeed;
            exit += new Vector3(wallNormal.x, 0f, wallNormal.z).normalized * excess;
        }

        exit = Vector3.ClampMagnitude(exit, maxExitSpeed);
        bool wasFast = Mode != WallMode.Crawling;
        movement.jumpConsumed = true; // this press must not also trigger an air jump or ground jump
        EndWall(exit, wasFast ? leapComboGain : 0f, wasFast ? leapDragRelief : 0f);
    }

    /// <summary>Leaves the wall in the air, starts the re-attach delay and hands the exit velocity and rewards to <see cref="PlayerSwinging.EndZip"/>.</summary>
    /// <param name="exitVelocity">Velocity to leave with.</param>
    /// <param name="combo">Combo levels to award.</param>
    /// <param name="dragRelief">Air drag reduction (0 to 1) for the flight that follows.</param>
    private void EndWall(Vector3 exitVelocity, float combo, float dragRelief)
    {
        IsActive = false;
        Mode = WallMode.None;
        nextAttachTime = Time.time + reattachDelay;
        swinging.EndZip(exitVelocity, combo, dragRelief);
    }

    /// <summary>
    /// Steps off onto the ground: hands control straight back to <see cref="PlayerMovement"/> through <see cref="PlayerSwinging.EndWallOnGround"/>.
    /// </summary>
    private void EndWallOnGround()
    {
        IsActive = false;
        Mode = WallMode.None;
        nextAttachTime = Time.time + reattachDelay;
        swinging.EndWallOnGround();
    }

    // ---------------------------------------------------------------------------- Input

    /// <summary>
    /// Reads the move input as a horizontal direction relative to the camera.</summary>
    /// <returns>A normalised direction, or <see cref="Vector3.zero"/> when there is no input.
    /// </returns>
    private Vector3 GetInputDirection()
    {
        if (moveAction == null) 
            return Vector3.zero;

        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        Vector3 direction = forward * moveAction.ReadValue<Vector2>().y + right * moveAction.ReadValue<Vector2>().x;
        if (direction.sqrMagnitude > 0.01f)
        {
            return direction.normalized;
        }
        else
        {
            return Vector3.zero;
        }
    }
}
