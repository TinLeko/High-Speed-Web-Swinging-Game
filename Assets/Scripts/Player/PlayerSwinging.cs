using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using static UnityEditor.Searcher.SearcherWindow.Alignment;

/// <summary>
/// The core of the game and its largest script. It handles:
/// <list type="number">
/// <item><description>Finding a web anchor with a forgiving, scored search.</description></item>
/// <item><description>Swinging on a <see cref="SpringJoint"/> with reeling, extra gravity and steering.</description></item>
/// <item><description>Releasing the web: a small reward for a plain release, a large one for a well-timed swing jump.</description></item>
/// <item><description>Post-release flight: steering, extra gravity, drag and one air jump.</description></item>
/// <item><description>Diving, whose fall speed is converted into swing speed.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// Also acts as the momentum hub for <see cref="WebZip"/> and <see cref="Wallrun"/>, which call <see cref="BeginZipMomentum"/> and
/// <see cref="EndZip"/> to enter and leave scripted movement. Requires <see cref="Rigidbody"/>, <see cref="PlayerMovement"/> and
/// <see cref="WebZip"/> on the same GameObject.
/// </remarks>
public class PlayerSwinging : MonoBehaviour
{
    private InputActionAsset inputActions;
    private InputAction swingAction;
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction diveAction;

    [Header("References")]
    public LineRenderer lineRenderer;
    public Transform webTip;
    [FormerlySerializedAs("camera")] public Transform cameraTransform;
    public LayerMask grappable;

    [Header("Web Aiming")]
    public float maxSwingDistance = 55f;
    public float minAnchorDistance = 8f;
    public float minAnchorHeight = 5f;
    public float anchorSphereRadius = 1.5f;
    public float[] pitchAngles = { 55f, 45f, 65f, 35f, 75f };
    [Tooltip("Yaw offsets from camera forward. Positive = the side we're swinging toward.")]
    public float[] yawOffsets = { 20f, 0f, 40f, -20f, -40f, 70f, -70f };
    public float idealYaw = 20f;
    public float idealPitch = 55f;
    public float idealDistance = 25f;
    public float cameraAimBonus = 10f;
    public float retryInterval = 0.1f;

    [Header("Rope")]
    public float spring = 60f;
    public float damper = 6f;
    public float reelSpeed = 3f;
    public float minRopeLength = 8f;
    public float ropeShootSpeed = 200f;

    [Header("Swing Feel")]
    public float extraGravity = 15f;
    public float swingPushAcceleration = 12f;

    [Header("Swing cooldown")]
    public float swingCooldown;
    [SerializeField]
    private bool canSwing = true;

    [Header("Speed Progression combo")]
    public float baseMaxSpeed = 35f;
    public float topMaxSpeed = 80f;
    public int maxComboLevel = 6;
    public float comboWindow = 1.5f;
    public float maxPowerMultiplier = 1.5f;
    [Tooltip("A jump or release can briefly exceed the cap by this factor. Drag brings it back down.")]
    public float burstMultiplier = 1.25f;

    [Header("Plain Release (minor reward)")]
    public float minGoodSwingTime = 0.5f;
    public float minRisingSpeed = 2f;
    [Range(0.3f, 1f)] public float goodReleaseSpeedRatio = 0.7f;
    [Tooltip("Combo levels gained by a well-timed plain release.")]
    public float releaseComboGain = 0.25f;
    public float releaseSpeedBoost = 1.5f;

    [Header("Swing Jump (major reward)")]
    [Tooltip("Best launch angle above horizontal, i.e. how steeply you're rising when you jump.")]
    public float idealJumpAngle = 35f;
    [Tooltip("Half-width of the timing window in degrees. Smaller = stricter.")]
    public float jumpAngleWindow = 30f;
    [Tooltip("Below this fraction of the speed cap, the jump earns nothing.")]
    [Range(0f, 1f)] public float minJumpSpeedRatio = 0.5f;
    [Tooltip("At or above this fraction of the speed cap, speed counts fully.")]
    [Range(0f, 1f)] public float fullJumpSpeedRatio = 0.9f;
    [Range(0f, 1f)] public float minJumpTiming = 0.3f;
    [Tooltip("Combo levels gained by a perfect jump (scaled by timing).")]
    public float jumpComboGain = 1f;
    [Tooltip("Extra speed (m/s) along your velocity on a perfect jump (scaled by timing).")]
    public float jumpSpeedBoost = 12f;
    public float jumpSpeedBoostUp = 3f;
    public float jumpUpImpulse = 35f;
    [Tooltip("How much a perfect jump reduces air drag, fading out over Drag Relief Time.")]
    [Range(0f, 1f)] public float jumpDragRelief = 0.7f;
    public float dragReliefTime = 1.5f;
    public bool debugLogTiming = false;

    [Header("After Release: Flight")]
    [Tooltip("Upward speed is capped at this on release. Extra goes into forward speed instead.")]
    public float maxLaunchUpSpeed = 10f;
    [Tooltip("Extra downward acceleration for the whole flight.")]
    public float postReleaseGravity = 10f;
    [Tooltip("More downward acceleration once you're falling.")]
    public float fallGravity = 12f;

    [Header("After Release: Air Jump")]
    [Tooltip("Jumps allowed in the air after each web release. Refills on every release.")]
    public int maxAirJumps = 1;
    [Tooltip("Upward speed of the air jump. If you're already rising faster, that is kept.")]
    public float airJumpUpSpeed = 11f;
    [Tooltip("Fraction of horizontal speed kept when you air jump. 0.85 = lose 15%.")]
    [Range(0.3f, 1f)] public float airJumpMomentumKeep = 0.85f;

    [Header("Dive")]
    [Tooltip("Downward speed you snap to when a dive starts.")]
    public float diveStartDownSpeed = 10f;
    public float diveAcceleration = 45f;
    public float maxDiveSpeed = 70f;
    [Tooltip("Dives shorter than this give no swing bonus.")]
    public float minDiveTime = 0.3f;
    [Tooltip("Dives this long (or longer) give the full swing bonus.")]
    public float fullDiveTime = 1.2f;
    [Tooltip("You can let go of dive and still get the bonus if you swing within this time.")]
    public float diveCarryTime = 0.5f;
    [Tooltip("How much of your fall speed becomes swing speed (at a full-length dive).")]
    [Range(0f, 1.5f)] public float diveSwingConversion = 0.9f;
    public float maxDiveSwingSpeed = 90f;
    [Tooltip("The raised speed cap from a dive-swing fades back to normal at this rate (m/s per second).")]
    public float diveCapDecay = 12f;

    [Header("After Release: Air Control & Drag")]
    public float airControlDuration = 1.2f;
    public float steerDegreesPerSecond = 120f;
    public float airAcceleration = 10f;
    public float airDrag = 0.3f;
    public float airDragQuadratic = 0.025f;

    // Read these from other scripts (camera, UI, etc.)
    /// <summary>The current combo level, rounded down.</summary>
    public int ComboLevel => Mathf.FloorToInt(comboLevel);

    /// <summary>The combo level as a 0 to 1 fraction of <see cref="maxComboLevel"/>.</summary>
    public float ComboT => maxComboLevel > 0 ? comboLevel / maxComboLevel : 0f;

    /// <summary>The speed cap from the combo alone, between <see cref="baseMaxSpeed"/> and <see cref="topMaxSpeed"/>.</summary>
    public float CurrentMaxSpeed => Mathf.Lerp(baseMaxSpeed, topMaxSpeed, ComboT);

    /// <summary>The timing score (0 to 1) of the most recent swing jump.</summary>
    public float LastJumpTiming { get; private set; }

    /// <summary>Air jumps still available since the last web release.</summary>
    public int AirJumpsRemaining => Mathf.Max(0, maxAirJumps - airJumpsUsed);

    /// <summary>True while a dive is in progress.</summary>
    public bool IsDiving => isDiving;

    /// <summary>The speed cap in use: the combo cap or a temporarily raised cap from a dive swing, boost or landing carry, whichever is higher.</summary>
    private float EffectiveMaxSpeed => Mathf.Max(CurrentMaxSpeed, swingSpeedCap);

    /// <summary>The short-term speed limit for releases and jumps: <see cref="EffectiveMaxSpeed"/> times <see cref="burstMultiplier"/>.</summary>
    private float BurstMaxSpeed => EffectiveMaxSpeed * burstMultiplier;

    /// <summary>Multiplier (1 to <see cref="maxPowerMultiplier"/>) that scales reeling, gravity and steering with the combo.</summary>
    private float Power => Mathf.Lerp(1f, maxPowerMultiplier, ComboT);

    private Rigidbody rigidBody;
    private PlayerMovement playerMovement;
    private SpringJoint springJoint;
    private Vector3 swingPoint;
    private Vector3 currentGrapplePosition;
    private int swingSide = 1;
    private float airControlTimer;
    private bool wantsSwing;
    private float nextRetryTime;

    private float comboLevel;
    private float swingStartTime;
    private float lastReleaseTime = -999f;
    private float dragReliefStrength;
    private bool airborneAfterSwing;
    private int airJumpsUsed;

    private bool isDiving;
    private float diveStartTime;
    private float lastDiveTime = -999f;
    private float lastDiveDuration;
    private float swingSpeedCap;

    private WebZip zip;

    /// <summary>True while a <see cref="WebZip"/> is in progress.</summary>
    private bool Zipping => zip != null && zip.IsZipping;

    private Wallrun wall;

    /// <summary>True while a <see cref="Wallrun"/> is active.</summary>
    private bool OnWall => wall != null && wall.IsActive;

    /// <summary>True while a zip or wall run owns the player's movement.</summary>
    private bool isBusy => Zipping || OnWall;

    /// <summary>Unity message. Looks up the input actions and caches sibling components.</summary>
    private void Awake()
    {
        swingAction = InputSystem.actions.FindAction("Swing");
        moveAction = InputSystem.actions.FindAction("Move");
        jumpAction = InputSystem.actions.FindAction("Jump");
        diveAction = InputSystem.actions.FindAction("Dive");
        playerMovement = GetComponent<PlayerMovement>();
        zip = GetComponent<WebZip>();
        rigidBody = GetComponent<Rigidbody>();
        wall = GetComponent<Wallrun>();
    }

    /// <summary>
    /// Unity message. Handles input: jump during a swing cuts the web and launches (a swing jump); jump in the air after a release does the air jump;
    /// the dive button starts a dive; the swing button sets or clears the wish to swing, releasing the web on button release. While a swing is wanted
    /// but no web is attached, retries <see cref="StartSwing"/> every <see cref="retryInterval"/> seconds.
    /// </summary>
    private void Update()
    {
        // Jumping during a swing cuts the web and launches you.
        if (jumpAction != null && jumpAction.WasPressedThisFrame())
        {
            if (springJoint != null)
            {
                wantsSwing = false; // press swing again to re-attach
                ReleaseWeb(true);
                return;
            }

            if (CanAirJump() && !isBusy)
                AirJump();
        }

        if (DivePressedThisFrame && CanDive())
            StartDive();

        if (swingAction != null && swingAction.WasPressedThisFrame())
        {
            wantsSwing = true;
            nextRetryTime = 0f;
        }
        if (swingAction != null && swingAction.WasReleasedThisFrame())
        {
            wantsSwing = false;
            ReleaseWeb(false);
        }

        if (wantsSwing && springJoint == null && Time.time >= nextRetryTime)
        {
            StartSwing();
            nextRetryTime = Time.time + retryInterval;
        }
    }

    /// <summary>
    /// Unity message. Decays the raised speed cap, then (unless a zip or wall run is active) runs swing physics while attached, or the dive,
    /// post-release air control, air gravity and drag. Handles landing (which resets combo and carries momentum) and resets the combo after
    /// <see cref="comboWindow"/> seconds without a release or dive.
    /// </summary>
    private void FixedUpdate()
    {
        if (swingSpeedCap > CurrentMaxSpeed)
            swingSpeedCap = Mathf.Max(CurrentMaxSpeed, swingSpeedCap - diveCapDecay * Time.fixedDeltaTime);

        if (isBusy) 
            return;

        if (springJoint != null)
        {
            SwingPhysics();
            return;
        }

        UpdateDive();

        if (airControlTimer > 0f)
        {
            AirControl();
            airControlTimer -= Time.fixedDeltaTime;
            if (airControlTimer <= 0f)
                EndMomentum();
        }

        if (airborneAfterSwing)
        {
            ApplyAirGravity();
            ApplyAirDrag();
        }

        if ((playerMovement.IsGrounded && Time.time - lastReleaseTime > 0.25f) && (airborneAfterSwing || comboLevel > 0 || playerMovement.swingMomentum))
        {
            LandWithMomentum();
        }
        else if (!isDiving && comboLevel > 0f && Time.time - Mathf.Max(lastReleaseTime, lastDiveTime) > comboWindow)
        {
            comboLevel = 0f;
        }
    }

    /// <summary>Unity message. Draws the rope after movement has been applied.</summary>
    private void LateUpdate()
    {
        DrawRope();
    }

    // -------------------------------------------------------------------------------------- Swing

    /// <summary>
    /// Tries to attach a web. Blocked while zipping, already swinging or on cooldown; cancels any wall run. If an anchor is found it creates a
    /// <see cref="SpringJoint"/>, sets scripted-momentum flags, converts dive speed into swing speed, starts the rope visual and alternates the swing side.
    /// </summary>
    private void StartSwing()
    {
        if (Zipping) return;
        if (springJoint != null) return;
        if (!canSwing) return;
        if (OnWall) wall.Cancel();
        if (!TryFindSwingPoint(out swingPoint)) return;

        if (zip != null)
            zip.consecutiveZips = 0f;

        playerMovement.isSwinging = true;
        playerMovement.swingMomentum = true;
        airControlTimer = 0f;
        airborneAfterSwing = false;
        swingStartTime = Time.time;

        springJoint = gameObject.AddComponent<SpringJoint>();
        springJoint.autoConfigureConnectedAnchor = false;
        springJoint.connectedAnchor = swingPoint;

        springJoint.maxDistance = Vector3.Distance(transform.position, swingPoint);
        springJoint.minDistance = 0f;
        springJoint.spring = spring;
        springJoint.damper = damper;
        springJoint.massScale = 1f;

        ApplyDiveConversion();
        lineRenderer.positionCount = 2;
        currentGrapplePosition = webTip.position;

        swingSide = -swingSide;
    }

    /// <summary>Invoked after <see cref="swingCooldown"/>. Allows swinging again.</summary>
    private void ResetSwing()
    {
        canSwing = true;
    }

    /// <summary>
    /// Cuts the web. A plain release only gives a small reward when it is well timed (long enough swing, rising, fast enough). A swing jump gives a larger one
    /// scaled by <see cref="EvaluateJumpTiming"/>: combo gain, speed boost, drag relief and an upward impulse. Limits upward launch speed, clamps to the burst
    /// speed cap and starts the post-release flight with air control and a refilled air jump.
    /// </summary>
    /// <param name="jumped"><c>true</c> if the release was triggered by a jump press.</param>
    private void ReleaseWeb(bool jumped)
    {
        if (springJoint == null) 
            return;

        playerMovement.isSwinging = false;
        lineRenderer.positionCount = 0;
        Destroy(springJoint);
        springJoint = null;
        canSwing = false;
        Invoke(nameof(ResetSwing), swingCooldown);

        Vector3 velocity = rigidBody.linearVelocity;
        dragReliefStrength = 0f;

        if (jumped)
        {
            float timing;

            if (Time.time - swingStartTime >= minGoodSwingTime)
            {
                timing = EvaluateJumpTiming(velocity);
            }
            else
            {
                timing = 0f;
            }

            LastJumpTiming = timing;

            if (timing >= minJumpTiming)
            {
                comboLevel = Mathf.Min(comboLevel + jumpComboGain * timing, maxComboLevel);
                dragReliefStrength = timing;
            }

            velocity += velocity.normalized * jumpSpeedBoost * timing;
            velocity += Vector3.up * jumpSpeedBoostUp;
            rigidBody.AddForce(Vector3.up * jumpUpImpulse, ForceMode.Impulse);

            playerMovement.jumpConsumed = true; // stopping PlayerMovement treating this press as a ground jump
        }
        else
        {
            if ((Time.time - swingStartTime >= minGoodSwingTime) && velocity.y >= minRisingSpeed && velocity.magnitude >= goodReleaseSpeedRatio * CurrentMaxSpeed)
            {
                comboLevel = Mathf.Min(comboLevel + releaseComboGain, maxComboLevel);
                velocity += velocity.normalized * releaseSpeedBoost;
            }
        }

        velocity = LimitUpwardVelocity(velocity);
        rigidBody.linearVelocity = Vector3.ClampMagnitude(velocity, BurstMaxSpeed);

        lastReleaseTime = Time.time;
        airControlTimer = airControlDuration;
        airborneAfterSwing = true;
        airJumpsUsed = 0;
    }

    /// <summary>
    /// Scores a swing jump from 0 to 1. The score is the product of how close the launch angle is to <see cref="idealJumpAngle"/>
    /// (within <see cref="jumpAngleWindow"/>) and how close the speed is to the cap. Best just after the bottom of the arc while moving fast.
    /// </summary>
    /// <param name="velocity">Velocity at the moment of release.</param>
    /// <returns>The timing score, 0 (poor) to 1 (perfect).</returns>
    private float EvaluateJumpTiming(Vector3 velocity)
    {
        if (velocity.magnitude < 0.1f) 
            return 0f;

        float elevation = Mathf.Asin(Mathf.Clamp(velocity.y / velocity.magnitude, -1f, 1f)) * Mathf.Rad2Deg;
        float angleFactor = 1f - Mathf.Clamp01(Mathf.Abs(elevation - idealJumpAngle) / jumpAngleWindow);
        float speedRatio = velocity.magnitude / CurrentMaxSpeed;
        float speedFactor = Mathf.InverseLerp(minJumpSpeedRatio, fullJumpSpeedRatio, speedRatio);
        float timing = angleFactor * speedFactor;

        if (debugLogTiming)
            Debug.Log($"Swing jump timing {timing:F2} | angle {elevation:F0} degrees (factor {angleFactor:F2}) | speed {speedRatio:P0} of cap (factor {speedFactor:F2})");

        return timing;
    }

    /// <summary>
    /// Caps upward speed at <see cref="maxLaunchUpSpeed"/> while keeping total speed: whatever is cut from the vertical part is moved into horizontal speed.
    /// Stops "rocket straight up" launches.
    /// </summary>
    /// <param name="velocity">The launch velocity.</param>
    /// <returns>The adjusted velocity.</returns>
    private Vector3 LimitUpwardVelocity(Vector3 velocity)
    {
        if (velocity.y <= maxLaunchUpSpeed) 
            return velocity;

        Vector3 flatVelocity = new Vector3(velocity.x, 0f, velocity.z);

        Vector3 direction;
        if (flatVelocity.sqrMagnitude > 0.01f)
        {
            direction = flatVelocity.normalized;
        }
        else
        {
            direction = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        }

        return direction * (Mathf.Sqrt(Mathf.Max(0f, velocity.magnitude * velocity.magnitude - maxLaunchUpSpeed * maxLaunchUpSpeed))) + Vector3.up * maxLaunchUpSpeed;
    }

    /// <summary>Ends the post-release control phase and gives movement back to <see cref="PlayerMovement"/>.</summary>
    private void EndMomentum()
    {
        airControlTimer = 0f;
        playerMovement.swingMomentum = false;
    }

    /// <summary>Checks whether an air jump is allowed: no web, airborne after a swing, jumps left, and the jump press not already consumed.</summary>
    /// <returns><c>true</c> if an air jump can be performed.</returns>
    private bool CanAirJump()
    {
        return springJoint == null && airborneAfterSwing && !playerMovement.IsGrounded && airJumpsUsed < maxAirJumps && !playerMovement.jumpConsumed;
    }

    /// <summary>
    /// Mid-air hop after a web release. Sets upward speed to at least <see cref="airJumpUpSpeed"/> at the cost of some horizontal momentum, and ends any dive.
    /// </summary>
    private void AirJump()
    {
        airJumpsUsed++;
        EndDive(false);

        Vector3 currentHorizontalVelocity = new Vector3(rigidBody.linearVelocity.x, 0f, rigidBody.linearVelocity.z) * airJumpMomentumKeep;
        rigidBody.linearVelocity = new Vector3(currentHorizontalVelocity.x, Mathf.Max(rigidBody.linearVelocity.y, airJumpUpSpeed), currentHorizontalVelocity.z);

        // stops PlayerMovement from treating this press as a ground jump when landing
        playerMovement.jumpConsumed = true;
    }

    /// <summary>
    /// Searches for a web anchor. It sphere-casts along many combinations of yaw (towards the swing side) and pitch from the camera heading, plus one wider cast along
    /// the camera aim. Hits too close or too low are discarded; the rest are scored by distance from the ideal yaw, pitch and distance (the camera cast gets a bonus),
    /// and the lowest score wins.
    /// </summary>
    /// <param name="point">The chosen anchor position, or <see cref="Vector3.zero"/> if none was found.</param>
    /// <returns><c>true</c> if an anchor was found.</returns>
    private bool TryFindSwingPoint(out Vector3 point)
    {
        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f) 
            forward = transform.forward;
        forward.Normalize();

        int side = swingSide;
        Vector2 moveValue;
        if (moveAction != null)
        {
            moveValue = moveAction.ReadValue<Vector2>();
        }
        else
        {
            moveValue = Vector2.zero;
        }

        if (Mathf.Abs(moveValue.x) > 0.3f)
            side = (int)Mathf.Sign(moveValue.x);

        float bestScore = float.MaxValue;
        Vector3 bestPoint = Vector3.zero;
        bool foundSwingPoint = false;

        /// <summary>Local function. Casts one candidate direction and keeps the hit if it is valid and scores better than the best so far.</summary>
        /// <param name="direction">World direction to cast.</param>
        /// <param name="radius">Sphere cast radius.</param>
        /// <param name="yaw">Yaw of the candidate in degrees, used for scoring.</param>
        /// <param name="pitch">Pitch of the candidate in degrees, used for scoring.</param>
        /// <param name="bonus">Score reduction applied to preferred candidates.</param>
        void ConsiderSwingPoint(Vector3 direction, float radius, float yaw, float pitch, float bonus)
        {
            if (!Physics.SphereCast(transform.position + Vector3.up, radius, direction, out RaycastHit hit, maxSwingDistance, grappable)) 
                return;
            if (hit.distance < minAnchorDistance) 
                return;
            if (hit.point.y - transform.position.y < minAnchorHeight) 
                return;

            float score = Mathf.Abs(yaw - idealYaw) * 0.5f + Mathf.Abs(pitch - idealPitch) * 0.3f + Mathf.Abs(hit.distance - idealDistance) * 0.4f - bonus;
            if (score < bestScore)
            {
                bestScore = score;
                bestPoint = hit.point;
                foundSwingPoint = true;
            }
        }

        foreach (float yaw in yawOffsets)
        {
            Vector3 flat = Quaternion.AngleAxis(side * yaw, Vector3.up) * forward;
            foreach (float pitch in pitchAngles)
            {
                float pitchInRadians = pitch * Mathf.Deg2Rad;
                ConsiderSwingPoint(flat * Mathf.Cos(pitchInRadians) + Vector3.up * Mathf.Sin(pitchInRadians), anchorSphereRadius, yaw, pitch, 0f);
            }
        }

        ConsiderSwingPoint(cameraTransform.forward, anchorSphereRadius * 2f, idealYaw, Mathf.Asin(Mathf.Clamp(cameraTransform.forward.y, -1f, 1f)) * Mathf.Rad2Deg, cameraAimBonus);

        point = bestPoint;
        return foundSwingPoint;
    }

    /// <summary>
    /// Per-physics-step swing update: shortens the rope (reeling), adds extra gravity, applies camera-relative steering and clamps speed to
    /// <see cref="EffectiveMaxSpeed"/>. All scaled by the combo <see cref="Power"/>.
    /// </summary>
    private void SwingPhysics()
    {
        springJoint.maxDistance = Mathf.Max(minRopeLength, springJoint.maxDistance - reelSpeed * Power * Time.fixedDeltaTime);
        rigidBody.AddForce(Vector3.down * extraGravity * Power, ForceMode.Acceleration);

        if (GetCameraRelativeInput().sqrMagnitude > 0.01f)
            rigidBody.AddForce(GetCameraRelativeInput() * swingPushAcceleration * Power, ForceMode.Acceleration);

        if (rigidBody.linearVelocity.magnitude > EffectiveMaxSpeed)
            rigidBody.linearVelocity = rigidBody.linearVelocity.normalized * EffectiveMaxSpeed;
    }

    // ---------------------------------------------------------------------------Flight after release

    /// <summary>Adds extra downward acceleration during post-release flight (more once falling).</summary>
    public void ApplyAirGravity()
    {
        float extraGravity = postReleaseGravity;
        if (rigidBody.linearVelocity.y < 0f)
            extraGravity += fallGravity;

        rigidBody.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);
    }

    /// <summary>
    /// Steers the horizontal velocity toward the input direction during post-release flight, rotating at <see cref="steerDegreesPerSecond"/> and adding
    /// <see cref="airAcceleration"/>. Speed is clamped to the burst cap.
    /// </summary>
    private void AirControl()
    {
        if (GetCameraRelativeInput().sqrMagnitude < 0.01f) 
            return;

        Vector3 horizontalVelocity = new Vector3(rigidBody.linearVelocity.x, 0f, rigidBody.linearVelocity.z);

        if (horizontalVelocity.magnitude < 1f)
        {
            horizontalVelocity += GetCameraRelativeInput() * airAcceleration * Time.fixedDeltaTime;
        }
        else
        {
            horizontalVelocity = Vector3.RotateTowards(horizontalVelocity, GetCameraRelativeInput() * horizontalVelocity.magnitude, steerDegreesPerSecond * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);
            horizontalVelocity += GetCameraRelativeInput() * airAcceleration * Time.fixedDeltaTime;
        }

        horizontalVelocity = Vector3.ClampMagnitude(horizontalVelocity, BurstMaxSpeed);
        rigidBody.linearVelocity = new Vector3(horizontalVelocity.x, rigidBody.linearVelocity.y, horizontalVelocity.z);
    }

    /// <summary>
    /// Applies horizontal air drag (constant plus speed-dependent). A well-timed swing jump reduces the drag, fading over <see cref="dragReliefTime"/>.
    /// </summary>
    public void ApplyAirDrag()
    {
        Vector3 horizontalVelocity = new Vector3(rigidBody.linearVelocity.x, 0f, rigidBody.linearVelocity.z);
        if (horizontalVelocity.magnitude < 0.01f)
            return;

        float horizontalDragRate = airDrag + airDragQuadratic * horizontalVelocity.magnitude;

        // A well-timed jump keeps its speed longer
        horizontalDragRate *= 1f - jumpDragRelief * dragReliefStrength * Mathf.Clamp01(1f - (Time.time - lastReleaseTime) / dragReliefTime);
        horizontalVelocity *= 1f - Mathf.Clamp01(horizontalDragRate * Time.fixedDeltaTime);
        rigidBody.linearVelocity = new Vector3(horizontalVelocity.x, rigidBody.linearVelocity.y, horizontalVelocity.z);
    }

    // ------------------------------------------------------------------------------------ Dive

    /// <summary>True on the frame the dive button was pressed (falls back to Left Ctrl if the action is missing).</summary>
    private bool DivePressedThisFrame
    {
        get
        {
            if (diveAction != null)
            {
                return diveAction.WasPressedThisFrame();
            }
            else
            {
                return Keyboard.current != null && Keyboard.current.leftCtrlKey.wasPressedThisFrame;
            }
        }
    }

    /// <summary>True while the dive button is held (falls back to Left Ctrl if the action is missing).</summary>
    private bool DiveHeld
    {
        get
        {
            if (diveAction != null)
            {
                return diveAction.IsPressed();
            }
            else
            {
                return Keyboard.current != null && Keyboard.current.leftCtrlKey.isPressed;
            }
        }
    }

    /// <summary>Checks whether a dive may start: airborne, not swinging, not already diving, and not zipping or wall-running.</summary>
    /// <returns><c>true</c> if a dive can start.</returns>
    private bool CanDive()
    {
        return springJoint == null && !isDiving && !playerMovement.IsGrounded && !isBusy;  
    }

    /// <summary>Starts a dive: records the start time and snaps vertical velocity to at least <see cref="diveStartDownSpeed"/> downward.</summary>
    private void StartDive()
    {
        isDiving = true;
        diveStartTime = Time.time;

        // Cancel any upward motion and snap into a downward dive.
        rigidBody.linearVelocity = new Vector3(rigidBody.linearVelocity.x, Mathf.Min(rigidBody.linearVelocity.y, -diveStartDownSpeed), rigidBody.linearVelocity.z);
    }

    /// <summary>
    /// Per-physics-step dive update. Ends the dive without bonus on landing, ends it with the carry bonus when the button is released,
    /// otherwise accelerates downward up to <see cref="maxDiveSpeed"/>.
    /// </summary>
    private void UpdateDive()
    {
        if (!isDiving)
            return;

        if (playerMovement.IsGrounded)
        {
            EndDive(false);
            return;
        } // landing: no swing bonus
        if (!DiveHeld)
        { 
            EndDive(true);
            return;
        }

        lastDiveTime = Time.time; // keeps the carry window and combo timer fresh while diving

        rigidBody.AddForce(Vector3.down * diveAcceleration, ForceMode.Acceleration);

        if (rigidBody.linearVelocity.y < -maxDiveSpeed)
            rigidBody.linearVelocity = new Vector3(rigidBody.linearVelocity.x, -maxDiveSpeed, rigidBody.linearVelocity.z);
    }

    /// <summary>Ends the dive and records its duration. Sets the carry timer so a swing started soon after still gets the dive bonus.</summary>
    /// <param name="keepCarry">If true, a swing started shortly after still gets the dive bonus.</param>
    public void EndDive(bool keepCarry)
    {
        if (isDiving)
        {
            isDiving = false;
            lastDiveDuration = Time.time - diveStartTime;
        }

        if (keepCarry)
        {
            lastDiveTime = Time.time;
        }
        else
        {
            lastDiveTime = -999f;
        }
    }

    /// <summary>
    /// Called when a web attaches. If the player was diving (or just stopped), the speed that was pulling away from the anchor is turned into speed along the arc
    /// instead of being absorbed by the rope. Longer dives convert more. The speed cap is raised temporarily and decays afterwards.
    /// </summary>
    private void ApplyDiveConversion()
    {
        if (!isDiving && Time.time - lastDiveTime > diveCarryTime) 
            return;

        float diveDuration;
        if (isDiving)
        {
            diveDuration = Time.time - diveStartTime;
        }
        else
        {
            diveDuration = lastDiveDuration;
        }

        EndDive(false); // a dive can only be spent once

        if (diveDuration < minDiveTime) 
            return;

        Vector3 ropeDirection = (swingPoint - transform.position).normalized; // points to the anchor

        if (Mathf.Max(0f, Vector3.Dot(rigidBody.linearVelocity, -ropeDirection)) < 1f) 
            return;

        Vector3 flatVelocity = new Vector3(rigidBody.linearVelocity.x, 0f, rigidBody.linearVelocity.z);
        Vector3 forward;

        if (flatVelocity.sqrMagnitude > 1f)
        {
            forward = flatVelocity.normalized;
        }
        else
        {
            forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        }

        // Direction of the arc: forward and down, perpendicular to the rope.
        Vector3 tangent = Vector3.ProjectOnPlane(forward, ropeDirection);
        if (tangent.sqrMagnitude < 0.001f) 
            return;
        tangent.Normalize();

        Vector3 newVelocity = Vector3.ProjectOnPlane(rigidBody.linearVelocity, ropeDirection) + tangent * (Mathf.Max(0f, Vector3.Dot(rigidBody.linearVelocity, -ropeDirection)) * diveSwingConversion * Mathf.InverseLerp(minDiveTime, fullDiveTime, diveDuration));
        newVelocity = Vector3.ClampMagnitude(newVelocity, maxDiveSwingSpeed);

        rigidBody.linearVelocity = newVelocity;
        swingSpeedCap = Mathf.Max(CurrentMaxSpeed, newVelocity.magnitude); // let the boost exist, then decay
    }

    /// <summary>Destroys the swing joint and hides the rope without any release reward. Called by <see cref="WebZip"/> when a zip starts.</summary>
    public void CancelSwingForZip()
    {
        if (springJoint == null) return;
        playerMovement.isSwinging = false;
        lineRenderer.positionCount = 0;
        Destroy(springJoint);
        springJoint = null;
        wantsSwing = false;
    }

    /// <summary>
    /// Puts the player into scripted-momentum mode (gravity handled externally, normal movement disabled) and ends any dive. Used by zips and wall runs.
    /// </summary>
    public void BeginZipMomentum()
    {
        playerMovement.swingMomentum = true;
        airControlTimer = 0f;
        airborneAfterSwing = false;
        EndDive(false);
    }

    /// <summary>
    /// Finishes a zip or wall run in the air: awards combo, raises the speed cap to the exit speed (it then decays), applies the clamped exit velocity,
    /// and starts post-release flight with air control and a refilled air jump.
    /// </summary>
    /// <param name="exitVelocity">Velocity to leave with.</param>
    /// <param name="combo">Combo levels to award.</param>
    /// <param name="dragRelief">Air drag reduction (0 to 1) for the flight that follows.</param>
    public void EndZip(Vector3 exitVelocity, float combo, float dragRelief)
    {
        comboLevel = Mathf.Min(comboLevel + combo, maxComboLevel);
        swingSpeedCap = Mathf.Max(swingSpeedCap, exitVelocity.magnitude); // boost exists, then decays
        rigidBody.linearVelocity = Vector3.ClampMagnitude(exitVelocity, BurstMaxSpeed);
        dragReliefStrength = dragRelief;
        lastReleaseTime = Time.time;
        airControlTimer = airControlDuration;
        airborneAfterSwing = true;
        airJumpsUsed = 0;
    }

    /// <summary>Finishes a wall run that ended on the ground: resets the combo and lands with momentum.</summary>
    public void EndWallOnGround()
    {
        airborneAfterSwing = false;
        comboLevel = 0f;   // same as landing
        LandWithMomentum();
    }

    /// <summary>
    /// Handles touchdown: starts the landing carry in <see cref="PlayerMovement"/>, keeps the carried speed as a cap, ends scripted momentum and resets the combo.
    /// </summary>
    private void LandWithMomentum()
    {
        airborneAfterSwing = false;
        playerMovement.BeginLandingCarry();
        swingSpeedCap = Mathf.Max(swingSpeedCap, playerMovement.CarrySpeed); // a swing right after landing keeps it
        EndMomentum();
        comboLevel = 0f;
    }

    /// <summary>Reads the move input as a horizontal direction relative to the camera.</summary>
    /// <returns>A normalised direction, or <see cref="Vector3.zero"/> when there is no input.</returns>
    private Vector3 GetCameraRelativeInput()
    {
        if (moveAction == null) 
            return Vector3.zero;

        return (Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized * moveAction.ReadValue<Vector2>().y + Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized * moveAction.ReadValue<Vector2>().x).normalized;
    }

    // ------------------------------------------------------------------------------ Rope

    /// <summary>Animates the rope shooting toward the anchor and keeps its start at the web tip. Does nothing when no web is attached.</summary>
    private void DrawRope()
    {
        if (springJoint == null)
            return;

        currentGrapplePosition = Vector3.MoveTowards(currentGrapplePosition, swingPoint, ropeShootSpeed * Time.deltaTime);
        lineRenderer.SetPosition(0, webTip.position);
        lineRenderer.SetPosition(1, currentGrapplePosition);
    }

    /// <summary>
    /// Resets all swing, combo and speed-cap state for a respawn and applies the given starting velocity.
    /// </summary>
    /// <param name="velocity">Velocity the player respawns with.</param>
    public void ResetForRespawn(Vector3 velocity)
    {
        CancelInvoke(nameof(ResetSwing));
        canSwing = true;

        if (springJoint != null) 
        {
            Destroy(springJoint); springJoint = null; 
        }

        lineRenderer.positionCount = 0;
        wantsSwing = false;
        playerMovement.isSwinging = false;
        swingSpeedCap = 0f;
        comboLevel = 0f;
        BeginZipMomentum();
        EndZip(velocity, 0f, 0f);
    }
}
