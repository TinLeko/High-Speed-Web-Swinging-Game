using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public class PlayerSwinging : MonoBehaviour
{
    public InputActionAsset inputActions;
    public InputAction swingAction;
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
    public float burstMultiplier = 1.25f;

    [Header("Plain Release (minor reward)")]
    public float minGoodSwingTime = 0.5f;
    public float minRisingSpeed = 2f;
    [Range(0.3f, 1f)] public float goodReleaseSpeedRatio = 0.7f;
    [Tooltip("Ccombo levels gained by a well-timed plain release.")]
    public float releaseComboGain = 0.25f;
    public float releaseSpeedBoost = 1.5f;

    [Header("Swing Jump (major reward)")]
    public float idealJumpAngle = 35f;
    [Tooltip("Strictness")]
    public float jumpAngleWindow = 30f;
    [Range(0f, 1f)] public float minJumpSpeedRatio = 0.5f;
    [Range(0f, 1f)] public float fullJumpSpeedRatio = 0.9f;
    [Range(0f, 1f)] public float minJumpTiming = 0.3f;
    public float jumpComboGain = 1f;
    public float jumpSpeedBoost = 12f;
    public float jumpSpeedBoostUp = 3f;
    public float jumpUpImpulse = 35f;
    [Range(0f, 1f)] public float jumpDragRelief = 0.7f;
    public float dragReliefTime = 1.5f;
    public bool debugLogTiming = false;

    [Header("After Release: Flight")]
    [Tooltip("extra goes into forward speed instead")]
    public float maxLaunchUpSpeed = 10f;
    public float postReleaseGravity = 10f;
    public float fallGravity = 12f;

    [Header("After Release: Air Jump")]
    public int maxAirJumps = 1;
    public float airJumpUpSpeed = 11f;
    [Range(0.3f, 1f)] public float airJumpMomentumKeep = 0.85f;

    [Header("Dive")]
    public float diveStartDownSpeed = 10f;
    public float diveAcceleration = 45f;
    public float maxDiveSpeed = 70f;
    [Tooltip("Ddives shorter than this give no swing bonus")]
    public float minDiveTime = 0.3f;
    [Tooltip("Dives this long give full swing bonus")]
    public float fullDiveTime = 1.2f;
    public float diveCarryTime = 0.5f;
    [Range(0f, 1.5f)] public float diveSwingConversion = 0.9f;
    public float maxDiveSwingSpeed = 90f;
    public float diveCapDecay = 12f;

    [Header("After Release: Air Control & Drag")]
    public float airControlDuration = 1.2f;
    public float steerDegreesPerSecond = 120f;
    public float airAcceleration = 10f;
    public float airDrag = 0.3f;
    public float airDragQuadratic = 0.025f;

    public int ComboLevel => Mathf.FloorToInt(comboLevel);
    public float ComboT => maxComboLevel > 0 ? comboLevel / maxComboLevel : 0f;
    public float CurrentMaxSpeed => Mathf.Lerp(baseMaxSpeed, topMaxSpeed, ComboT);
    public float LastJumpTiming { get; private set; }
    public int AirJumpsRemaining => Mathf.Max(0, maxAirJumps - airJumpsUsed);
    public bool IsDiving => isDiving;
    private float EffectiveMaxSpeed => Mathf.Max(CurrentMaxSpeed, swingSpeedCap);
    private float BurstMaxSpeed => EffectiveMaxSpeed * burstMultiplier;
    private float Power => Mathf.Lerp(1f, maxPowerMultiplier, ComboT);

    private Rigidbody rb;
    private PlayerMovement playerMovement;
    private SpringJoint joint;
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
    private bool Zipping => zip != null && zip.IsZipping;

    private Wallrun wall;
    private bool OnWall => wall != null && wall.IsActive;
    private bool Busy => Zipping || OnWall;

    private void OnEnable()
    {
        inputActions.FindActionMap("Player").Enable();
    }

    private void OnDisable()
    {
        inputActions.FindActionMap("Player").Disable();
    }

    private void Awake()
    {
        swingAction = InputSystem.actions.FindAction("Swing");
        moveAction = InputSystem.actions.FindAction("Move");
        jumpAction = InputSystem.actions.FindAction("Jump");
        diveAction = InputSystem.actions.FindAction("Dive");
        playerMovement = GetComponent<PlayerMovement>();
        zip = GetComponent<WebZip>();
        rb = GetComponent<Rigidbody>();
        wall = GetComponent<Wallrun>();
    }

    private void Update()
    {
        if (jumpAction != null && jumpAction.WasPressedThisFrame())
        {
            if (joint != null)
            {
                wantsSwing = false;
                ReleaseWeb(true);
                return;
            }

            if (CanAirJump() && !Busy)
                AirJump();
        }

        if (DivePressedThisFrame && CanDive())
            StartDive();
        if (swingAction.WasPressedThisFrame())
        {
            wantsSwing = true;
            nextRetryTime = 0f;
        }
        if (swingAction.WasReleasedThisFrame())
        {
            wantsSwing = false;
            ReleaseWeb(false);
        }

        if (wantsSwing && joint == null && Time.time >= nextRetryTime)
        {
            StartSwing();
            nextRetryTime = Time.time + retryInterval;
        }
    }

    private void FixedUpdate()
    {
        if (swingSpeedCap > CurrentMaxSpeed)
            swingSpeedCap = Mathf.Max(CurrentMaxSpeed, swingSpeedCap - diveCapDecay * Time.fixedDeltaTime);
        if (Busy) return;
        if (joint != null)
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

        bool landed = playerMovement.IsGrounded && Time.time - lastReleaseTime > 0.25f;
        if (landed && (airborneAfterSwing || comboLevel > 0 || playerMovement.swingMomentum))
        {
            LandWithMomentum();
        }
        else if (!isDiving && comboLevel > 0f
                 && Time.time - Mathf.Max(lastReleaseTime, lastDiveTime) > comboWindow)
        {
            comboLevel = 0f;
        }
    }

    private void LateUpdate()
    {
        DrawRope();
    }

    private void StartSwing()
    {
        if (Zipping) return;
        if (joint != null) return;
        if (!canSwing) return;
        if (OnWall) wall.Cancel();
        if (!TryFindSwingPoint(out swingPoint)) return;

        zip.consecutiveZips = 0f;
        playerMovement.isSwinging = true;
        playerMovement.swingMomentum = true;
        airControlTimer = 0f;
        airborneAfterSwing = false;
        swingStartTime = Time.time;

        joint = gameObject.AddComponent<SpringJoint>();
        joint.autoConfigureConnectedAnchor = false;
        joint.connectedAnchor = swingPoint;

        float distance = Vector3.Distance(transform.position, swingPoint);
        joint.maxDistance = distance;
        joint.minDistance = 0f;
        joint.spring = spring;
        joint.damper = damper;
        joint.massScale = 1f;

        ApplyDiveConversion();
        lineRenderer.positionCount = 2;
        currentGrapplePosition = webTip.position;

        swingSide = -swingSide;
    }

    private void ResetSwing()
    {
        canSwing = true;
    }

    private void ReleaseWeb(bool jumped)
    {
        if (joint == null) return;

        playerMovement.isSwinging = false;
        lineRenderer.positionCount = 0;
        Destroy(joint);
        joint = null;
        canSwing = false;
        Invoke(nameof(ResetSwing), swingCooldown);

        Vector3 velocity = rb.linearVelocity;
        float speed = velocity.magnitude;
        bool longEnough = Time.time - swingStartTime >= minGoodSwingTime;
        dragReliefStrength = 0f;

        if (jumped)
        {
            float timing = longEnough ? EvaluateJumpTiming(velocity) : 0f;
            LastJumpTiming = timing;

            if (timing >= minJumpTiming)
            {
                comboLevel = Mathf.Min(comboLevel + jumpComboGain * timing, maxComboLevel);
                dragReliefStrength = timing;
            }

            velocity += velocity.normalized * jumpSpeedBoost * timing;
            velocity += Vector3.up * jumpSpeedBoostUp;
            rb.AddForce(Vector3.up * jumpUpImpulse, ForceMode.Impulse);

            playerMovement.jumpConsumed = true; // stopping PlayerMovement treating this press as a ground jump
        }
        else
        {
            bool good = longEnough
                        && velocity.y >= minRisingSpeed
                        && speed >= goodReleaseSpeedRatio * CurrentMaxSpeed;
            if (good)
            {
                comboLevel = Mathf.Min(comboLevel + releaseComboGain, maxComboLevel);
                velocity += velocity.normalized * releaseSpeedBoost;
            }
        }

        velocity = LimitUpwardVelocity(velocity);
        rb.linearVelocity = Vector3.ClampMagnitude(velocity, BurstMaxSpeed);

        lastReleaseTime = Time.time;
        airControlTimer = airControlDuration;
        airborneAfterSwing = true;
        airJumpsUsed = 0;
    }

    private float EvaluateJumpTiming(Vector3 velocity)
    {
        float speed = velocity.magnitude;
        if (speed < 0.1f) return 0f;

        float elevation = Mathf.Asin(Mathf.Clamp(velocity.y / speed, -1f, 1f)) * Mathf.Rad2Deg;
        float angleFactor = 1f - Mathf.Clamp01(Mathf.Abs(elevation - idealJumpAngle) / jumpAngleWindow);

        float speedRatio = speed / CurrentMaxSpeed;
        float speedFactor = Mathf.InverseLerp(minJumpSpeedRatio, fullJumpSpeedRatio, speedRatio);

        float timing = angleFactor * speedFactor;

        if (debugLogTiming)
            Debug.Log($"Swing jump timing {timing:F2} | angle {elevation:F0} deg (factor {angleFactor:F2}) | speed {speedRatio:P0} of cap (factor {speedFactor:F2})");

        return timing;
    }

    private Vector3 LimitUpwardVelocity(Vector3 velocity)
    {
        if (velocity.y <= maxLaunchUpSpeed) return velocity;

        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        Vector3 dir = flat.sqrMagnitude > 0.01f
            ? flat.normalized
            : Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;

        float total = velocity.magnitude;
        float horizontal = Mathf.Sqrt(Mathf.Max(0f, total * total - maxLaunchUpSpeed * maxLaunchUpSpeed));
        return dir * horizontal + Vector3.up * maxLaunchUpSpeed;
    }

    private void EndMomentum()
    {
        airControlTimer = 0f;
        playerMovement.swingMomentum = false;
    }

    private bool CanAirJump()
    {
        return joint == null
            && airborneAfterSwing
            && !playerMovement.IsGrounded
            && airJumpsUsed < maxAirJumps
            && !playerMovement.jumpConsumed;
    }
    private void AirJump()
    {
        airJumpsUsed++;
        EndDive(false);

        Vector3 velocity = rb.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z) * airJumpMomentumKeep;

        // replace vertical speed
        float vertical = Mathf.Max(velocity.y, airJumpUpSpeed);

        rb.linearVelocity = new Vector3(horizontal.x, vertical, horizontal.z);

        // stops PlayerMovement from treating this press as a ground jump when landing
        playerMovement.jumpConsumed = true;
    }

    private bool TryFindSwingPoint(out Vector3 point)
    {
        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f) forward = transform.forward;
        forward.Normalize();

        int side = swingSide;
        Vector2 move = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
        if (Mathf.Abs(move.x) > 0.3f) side = (int)Mathf.Sign(move.x);

        Vector3 origin = transform.position + Vector3.up;
        float bestScore = float.MaxValue;
        Vector3 bestPoint = Vector3.zero;
        bool found = false;

        void Consider(Vector3 dir, float radius, float yaw, float pitch, float bonus)
        {
            if (!Physics.SphereCast(origin, radius, dir, out RaycastHit hit, maxSwingDistance, grappable)) return;
            if (hit.distance < minAnchorDistance) return;
            if (hit.point.y - transform.position.y < minAnchorHeight) return;

            float score = Mathf.Abs(yaw - idealYaw) * 0.5f
                        + Mathf.Abs(pitch - idealPitch) * 0.3f
                        + Mathf.Abs(hit.distance - idealDistance) * 0.4f
                        - bonus;
            if (score < bestScore)
            {
                bestScore = score;
                bestPoint = hit.point;
                found = true;
            }
        }

        foreach (float yaw in yawOffsets)
        {
            Vector3 flat = Quaternion.AngleAxis(side * yaw, Vector3.up) * forward;
            foreach (float pitch in pitchAngles)
            {
                float rad = pitch * Mathf.Deg2Rad;
                Vector3 dir = flat * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad);
                Consider(dir, anchorSphereRadius, yaw, pitch, 0f);
            }
        }

        Vector3 aim = cameraTransform.forward;
        float aimPitch = Mathf.Asin(Mathf.Clamp(aim.y, -1f, 1f)) * Mathf.Rad2Deg;
        Consider(aim, anchorSphereRadius * 2f, idealYaw, aimPitch, cameraAimBonus);

        point = bestPoint;
        return found;
    }

    private void SwingPhysics()
    {
        float power = Power;

        joint.maxDistance = Mathf.Max(minRopeLength, joint.maxDistance - reelSpeed * power * Time.fixedDeltaTime);

        rb.AddForce(Vector3.down * extraGravity * power, ForceMode.Acceleration);

        Vector3 desired = GetCameraRelativeInput();
        if (desired.sqrMagnitude > 0.01f)
            rb.AddForce(desired * swingPushAcceleration * power, ForceMode.Acceleration);

        float cap = EffectiveMaxSpeed;
        if (rb.linearVelocity.magnitude > cap)
            rb.linearVelocity = rb.linearVelocity.normalized * cap;
    }

    public void ApplyAirGravity()
    {
        float extra = postReleaseGravity;
        if (rb.linearVelocity.y < 0f)
            extra += fallGravity;

        rb.AddForce(Vector3.down * extra, ForceMode.Acceleration);
    }

    private void AirControl()
    {
        Vector3 desired = GetCameraRelativeInput();
        if (desired.sqrMagnitude < 0.01f) return;

        Vector3 velocity = rb.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
        float speed = horizontal.magnitude;

        if (speed < 1f)
        {
            horizontal += desired * airAcceleration * Time.fixedDeltaTime;
        }
        else
        {
            horizontal = Vector3.RotateTowards(
                horizontal,
                desired * speed,
                steerDegreesPerSecond * Mathf.Deg2Rad * Time.fixedDeltaTime,
                0f);
            horizontal += desired * airAcceleration * Time.fixedDeltaTime;
        }

        horizontal = Vector3.ClampMagnitude(horizontal, BurstMaxSpeed);
        rb.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
    }

    public void ApplyAirDrag()
    {
        Vector3 velocity = rb.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
        float speed = horizontal.magnitude;
        if (speed < 0.01f) return;

        float dragRate = airDrag + airDragQuadratic * speed;

        // A well-timed jump keeps its speed longer
        float reliefFade = Mathf.Clamp01(1f - (Time.time - lastReleaseTime) / dragReliefTime);
        dragRate *= 1f - jumpDragRelief * dragReliefStrength * reliefFade;

        horizontal *= 1f - Mathf.Clamp01(dragRate * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
    }

    // Dive

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

    private bool CanDive()
    {
        return joint == null && !isDiving && !playerMovement.IsGrounded && !Busy;  
    }

    private void StartDive()
    {
        isDiving = true;
        diveStartTime = Time.time;

        // cancel any upward motion
        Vector3 velocity = rb.linearVelocity;
        rb.linearVelocity = new Vector3(velocity.x, Mathf.Min(velocity.y, -diveStartDownSpeed), velocity.z);
    }

    private void UpdateDive()
    {
        if (!isDiving) return;

        if (playerMovement.IsGrounded) { EndDive(false); return; } // landing: no swing bonus
        if (!DiveHeld) { EndDive(true); return; }

        lastDiveTime = Time.time; // keeps the carry window and combo timer fresh while diving

        rb.AddForce(Vector3.down * diveAcceleration, ForceMode.Acceleration);

        Vector3 velocity = rb.linearVelocity;
        if (velocity.y < -maxDiveSpeed)
            rb.linearVelocity = new Vector3(velocity.x, -maxDiveSpeed, velocity.z);
    }

    private void EndDive(bool keepCarry)
    {
        if (isDiving)
        {
            isDiving = false;
            lastDiveDuration = Time.time - diveStartTime;
        }
        lastDiveTime = keepCarry ? Time.time : -999f;
    }

    private void ApplyDiveConversion()
    {
        if (!isDiving && Time.time - lastDiveTime > diveCarryTime) return;

        float duration = isDiving ? Time.time - diveStartTime : lastDiveDuration;
        EndDive(false); // a dive can only be spent once

        if (duration < minDiveTime) return;
        float scale = Mathf.InverseLerp(minDiveTime, fullDiveTime, duration);

        Vector3 velocity = rb.linearVelocity;
        Vector3 ropeDir = (swingPoint - transform.position).normalized; // points to the anchor

        float outward = Mathf.Max(0f, Vector3.Dot(velocity, -ropeDir));
        if (outward < 1f) return;

        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        Vector3 forward = flat.sqrMagnitude > 1f
            ? flat.normalized
            : Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;

        // Direction of the arc
        Vector3 tangent = Vector3.ProjectOnPlane(forward, ropeDir);
        if (tangent.sqrMagnitude < 0.001f) return;
        tangent.Normalize();

        Vector3 kept = Vector3.ProjectOnPlane(velocity, ropeDir); // velocity without the outward part
        Vector3 newVelocity = kept + tangent * (outward * diveSwingConversion * scale);
        newVelocity = Vector3.ClampMagnitude(newVelocity, maxDiveSwingSpeed);

        rb.linearVelocity = newVelocity;
        swingSpeedCap = Mathf.Max(CurrentMaxSpeed, newVelocity.magnitude);
    }

    public void CancelSwingForZip()
    {
        if (joint == null) return;
        playerMovement.isSwinging = false;
        lineRenderer.positionCount = 0;
        Destroy(joint);
        joint = null;
        wantsSwing = false;
    }

    public void BeginZipMomentum()
    {
        playerMovement.swingMomentum = true;
        airControlTimer = 0f;
        airborneAfterSwing = false;
        EndDive(false);
    }

    public void EndZip(Vector3 exitVelocity, float combo, float dragRelief)
    {
        comboLevel = Mathf.Min(comboLevel + combo, maxComboLevel);
        swingSpeedCap = Mathf.Max(swingSpeedCap, exitVelocity.magnitude); // boost exists, then decays
        rb.linearVelocity = Vector3.ClampMagnitude(exitVelocity, BurstMaxSpeed);
        dragReliefStrength = dragRelief;
        lastReleaseTime = Time.time;
        airControlTimer = airControlDuration;
        airborneAfterSwing = true;
        airJumpsUsed = 0;
    }

    public void EndWallOnGround()
    {
        airborneAfterSwing = false;
        comboLevel = 0f;   // same as landing
        LandWithMomentum();
    }

    private void LandWithMomentum()
    {
        airborneAfterSwing = false;
        playerMovement.BeginLandingCarry();
        swingSpeedCap = Mathf.Max(swingSpeedCap, playerMovement.CarrySpeed); // a swing right after landing keeps it
        EndMomentum();
        comboLevel = 0f;
    }

    private Vector3 GetCameraRelativeInput()
    {
        if (moveAction == null) return Vector3.zero;
        Vector2 input = moveAction.ReadValue<Vector2>();

        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        return (forward * input.y + right * input.x).normalized;
    }

    // Rope

    private void DrawRope()
    {
        if (joint == null) return;

        currentGrapplePosition = Vector3.MoveTowards(
            currentGrapplePosition, swingPoint, ropeShootSpeed * Time.deltaTime);

        lineRenderer.SetPosition(0, webTip.position);
        lineRenderer.SetPosition(1, currentGrapplePosition);
    }

    public void ResetForRespawn(Vector3 velocity)
    {
        CancelInvoke(nameof(ResetSwing));
        canSwing = true;

        if (joint != null) { Destroy(joint); joint = null; }
        lineRenderer.positionCount = 0;
        wantsSwing = false;
        playerMovement.isSwinging = false;

        swingSpeedCap = 0f;
        comboLevel = 0f;
        BeginZipMomentum();
        EndZip(velocity, 0f, 0f);
    }
}
