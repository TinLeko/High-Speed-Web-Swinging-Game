using UnityEngine;
using UnityEngine.InputSystem;

/// Wall run / climb / crawl
/// Run key held, fast, hitting the wall at an angle -> running (sideways, follows corners)
/// Run key held, fast, hitting the wall head-on -> climbing (straight up)
/// Walk slowly into a wall, no key needed -> crawling (move freely on the wall)
/// Jump leaps off the wall at any time
public class Wallrun : MonoBehaviour
{
    public enum WallMode { None, Running, Climbing, Crawling }

    [Header("References")]
    public Transform cameraTransform;
    public LayerMask wallMask;

    [Header("Detection")]
    public float probeRadius = 0.3f;
    public float probeDistance = 1.2f;
    [Range(0f, 0.5f)] public float maxWallNormalY = 0.25f;
    public float headOnAngle = 35f;
    public float lostContactGrace = 0.15f;
    public float reattachDelay = 0.3f;
    [Tooltip("lower = smoother")]
    public float normalSmoothing = 15f;

    [Header("Speed Thresholds (horizontal m/s)")]
    public float minRunSpeed = 18f;
    public float dropToCrawlSpeed = 10f;

    [Header("Wall Run (sideways)")]
    public float maxRunSpeed = 70f;
    public float runSpeedDecay = 1f;

    [Header("Wall Climb (head-on)")]
    public float climbConversion = 0.9f;
    public float maxClimbSpeed = 45f;
    public float climbSpeedDecay = 0f;
    public float climbSteerSpeed = 6f;

    [Header("Wall Crawl")]
    public float crawlSpeed = 9f;
    public float crawlAttachDistance = 0.7f;
    [Tooltip("1 = must walk straight at the wall, 0 = any angle")]
    [Range(0f, 1f)] public float crawlApproachDot = 0.5f;

    [Header("Corners (wall run)")]
    public float cornerReach = 2f;
    public float cornerSteerAcceleration = 1500f;
    public float cornerSteerTime = 0.25f;
    public float cornerCooldown = 0.6f;
    public float outerCornerConfirm = 0.06f;
    public float cornerHeightCheck = 1.5f;

    [Header("Camera")]
    [Range(0f, 1f)] public float cameraFollow = 1f;
    public float cameraTurnSharpness = 8f;
    public float cameraDeadzone = 0.05f;
    public float maxCameraTurnRate = 240f;

    [Header("Sticking")]
    public float stickSpeed = 3f;
    public float steerAcceleration = 400f;
    public float stuckTime = 0.4f;

    [Header("jump off the wall")]
    public float leapAwaySpeed = 14f;
    public float leapUpSpeed = 14f;
    [Range(0f, 1f)] public float leapMomentumKeep = 0.8f;
    public float maxLeapUpSpeed = 28f;
    public float maxExitSpeed = 100f;
    public float leapComboGain = 0.5f;
    [Range(0f, 1f)] public float leapDragRelief = 0.8f;

    [Header("Ledge Vault")]
    public float ledgeVaultSpeed = 8f;

    public bool IsActive { get; private set; }
    public WallMode Mode { get; private set; } = WallMode.None;
    public Vector3 WallNormal => wallNormal;
    public Vector3 RunDirection => runDir;

    private Rigidbody rb;
    private PlayerSwinging swinging;
    private PlayerMovement movement;
    private WebZip zip;
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction runAction;

    private Vector3 wallNormal;
    private Vector3 runDir;
    private float wallSpeed;
    private float attachTime;
    private float nextAttachTime;
    private float lostContactTimer;
    private float stuckTimer;
    private float cornerTimer;
    private float pendingYaw;
    private float lastCornerTime = -999f;

    private bool RunHeld => runAction != null && runAction.IsPressed();

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        swinging = GetComponent<PlayerSwinging>();
        movement = GetComponent<PlayerMovement>();
        zip = GetComponent<WebZip>();

        moveAction = InputSystem.actions.FindAction("Move");
        jumpAction = InputSystem.actions.FindAction("Jump");
        runAction = InputSystem.actions.FindAction("Sprint");
    }

    private void Update()
    {
        if (IsActive && jumpAction != null && jumpAction.WasPressedThisFrame())
            Leap();
    }

    private void FixedUpdate()
    {
        if (IsActive) StepWall();
        else TryAttach();
    }

    public float ConsumeYawDelta()
    {
        float step = pendingYaw * (1f - Mathf.Exp(-cameraTurnSharpness * Time.deltaTime));
        float maxStep = maxCameraTurnRate * Time.deltaTime;
        step = Mathf.Clamp(step, -maxStep, maxStep);
        pendingYaw -= step;
        return step;
    }

    public void Cancel()
    {
        IsActive = false;
        Mode = WallMode.None;
        nextAttachTime = Time.time + reattachDelay;
    }

    private void TryAttach()
    {
        if (Time.time < nextAttachTime) return;
        if (movement.isSwinging || (zip != null && zip.IsZipping)) return;

        Vector3 velocity = rb.linearVelocity;
        Vector3 flatVel = new Vector3(velocity.x, 0f, velocity.z);
        float flatSpeed = flatVel.magnitude;
        bool fast = flatSpeed >= minRunSpeed;

        if (fast && !RunHeld) return;

        RaycastHit hit;
        if (fast)
        {
            // Probe ahead far enough to catch the wall before collision eats speed
            Vector3 forward = flatVel / flatSpeed;
            float reach = probeDistance + flatSpeed * Time.fixedDeltaTime;
            if (!ProbeWall(forward, reach, out hit))
            {
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                if (!ProbeWall(right, probeDistance, out hit) && !ProbeWall(-right, probeDistance, out hit))
                    return;
            }
        }
        else
        {
            if (!movement.IsGrounded) return;

            Vector3 input = GetInputDirection();
            if (input == Vector3.zero) return;
            if (!ProbeWall(input, crawlAttachDistance, out hit)) return;

            Vector3 hitFlat = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
            if (Vector3.Dot(input, -hitFlat) < crawlApproachDot) return;
        }

        Vector3 n = hit.normal;
        Vector3 flatN = new Vector3(n.x, 0f, n.z).normalized;

        WallMode mode;
        if (!fast)
        {
            mode = WallMode.Crawling;
        }
        else
        {
            float incidence = Vector3.Angle(-flatN, flatVel);
            mode = incidence <= headOnAngle ? WallMode.Climbing : WallMode.Running;
        }

        if (mode == WallMode.Running && movement.IsGrounded) return;

        if (mode == WallMode.Running)
        {
            Vector3 along = Vector3.ProjectOnPlane(flatVel, n);
            if (along.sqrMagnitude < 0.0001f) return;
            runDir = along.normalized;
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

        wallNormal = n;
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

    private bool ProbeWall(Vector3 dir, float distance, out RaycastHit hit)
    {
        // hit.distance == 0 means the sphere started inside a collider, normal is meaningless then
        return Physics.SphereCast(rb.position, probeRadius, dir, out hit, distance, wallMask,
                   QueryTriggerInteraction.Ignore)
               && hit.distance > 0f
               && Mathf.Abs(hit.normal.y) <= maxWallNormalY;
    }

    // while on wall

    private void StepWall()
    {
        float dt = Time.fixedDeltaTime;

        // swing or zip took over
        if (movement.isSwinging || (zip != null && zip.IsZipping))
        {
            Cancel();
            return;
        }
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

        bool hasInput = input.sqrMagnitude > 0.01f;
        bool runHeld = RunHeld;

        bool keepClinging;
        if (Mode == WallMode.Crawling)
        {
            keepClinging = runHeld || hasInput;
        }
        else
        {
            keepClinging = runHeld;
        }

        if (!keepClinging)
        {
            EndWall(rb.linearVelocity, 0f, 0f);
            return;
        }

        Vector3 runBefore = runDir;

        if (Mode == WallMode.Running)
            TryInnerCorner();

        if (ProbeWall(-wallNormal, probeDistance, out RaycastHit hit))
        {
            float k = 1f - Mathf.Exp(-normalSmoothing * dt);
            wallNormal = Vector3.Slerp(wallNormal, hit.normal, k).normalized;
            lostContactTimer = 0f;
        }
        else
        {
            lostContactTimer += dt;

            bool turned = Mode == WallMode.Running && lostContactTimer >= outerCornerConfirm && TryOuterCorner();

            if (turned)
            {
                lostContactTimer = 0f;
            }
            else if (lostContactTimer > lostContactGrace)
            {
                // Reached the top of the wall
                Vector3 exit = rb.linearVelocity;
                if ((Mode == WallMode.Climbing || Mode == WallMode.Crawling) && exit.y > 1f)
                    exit += -wallNormal * ledgeVaultSpeed;
                EndWall(exit, 0f, 0f);
                return;
            }
        }

        Vector3 wallRight = Vector3.Cross(wallNormal, Vector3.up).normalized;
        Vector3 wallUp = Vector3.Cross(wallRight, wallNormal);

        Vector3 target;
        switch (Mode)
        {
            case WallMode.Running:
                wallSpeed = Mathf.Max(0f, wallSpeed - runSpeedDecay * dt);
                runDir = Vector3.ProjectOnPlane(runDir, wallNormal).normalized;
                target = runDir * wallSpeed;
                AddCameraYaw(runBefore, runDir);
                if (wallSpeed < dropToCrawlSpeed) Mode = WallMode.Crawling;
                break;

            case WallMode.Climbing:
                wallSpeed = Mathf.Max(0f, wallSpeed - climbSpeedDecay * dt);
                target = wallUp * wallSpeed + wallRight * (input.x * climbSteerSpeed);
                if (wallSpeed < dropToCrawlSpeed) Mode = WallMode.Crawling;
                break;

            default: // crawling
                target = (wallUp * input.y + wallRight * input.x) * crawlSpeed;
                break;
        }

        if ((Mode == WallMode.Running || Mode == WallMode.Climbing) && Time.time - attachTime > 0.3f)
        {
            Vector3 dir = target.sqrMagnitude > 0.01f ? target.normalized : Vector3.zero;
            float along = Vector3.Dot(rb.linearVelocity, dir);
            stuckTimer = along < wallSpeed * 0.2f ? stuckTimer + dt : 0f;
            if (stuckTimer > stuckTime)
            {
                EndWall(rb.linearVelocity, 0f, 0f);
                return;
            }
        }

        if (movement.IsGrounded && Time.time - attachTime > 0.2f && rb.linearVelocity.y <= 0.5f)
        {
            EndWallOnGround();
            return;
        }

        target -= wallNormal * stickSpeed;

        cornerTimer -= dt;
        float accel = cornerTimer > 0f ? cornerSteerAcceleration : steerAcceleration;
        rb.linearVelocity = Vector3.MoveTowards(rb.linearVelocity, target, accel * dt);

        rb.AddForce(-Physics.gravity, ForceMode.Acceleration);
    }

    // corners
    private bool TryInnerCorner()
    {
        if (Time.time - lastCornerTime < cornerCooldown) return false;

        float reach = cornerReach + wallSpeed * 0.1f;
        if (!ProbeWall(runDir, reach, out RaycastHit ahead)) return false;
        if (Vector3.Dot(ahead.normal, -runDir) < 0.5f) return false;

        Vector3 lift = Vector3.up * cornerHeightCheck;
        float rayLength = reach + probeRadius;
        for (int i = -1; i <= 1; i += 2)
        {
            if (!Physics.Raycast(rb.position + lift * i, runDir, out RaycastHit extra, rayLength, wallMask, QueryTriggerInteraction.Ignore) || Vector3.Dot(extra.normal, ahead.normal) < 0.9f)
                return false;
        }

        SwitchWall(ahead.normal, true);
        return true;
    }

    private bool TryOuterCorner()
    {
        if (Time.time - lastCornerTime < cornerCooldown) return false;


        Vector3 origin = rb.position - wallNormal * cornerReach + runDir * 0.5f;
        float distance = cornerReach + 1f + wallSpeed * lostContactTimer;
        if (!Physics.SphereCast(origin, probeRadius, -runDir, out RaycastHit around, distance, wallMask, QueryTriggerInteraction.Ignore))
            return false;

        if (around.distance <= 0f || Mathf.Abs(around.normal.y) > maxWallNormalY) return false;
        if (Vector3.Dot(around.normal, runDir) < 0.3f) return false;

        SwitchWall(around.normal, false);
        return true;
    }

    private void SwitchWall(Vector3 newNormal, bool inner)
    {
        Vector3 tangent = Vector3.Cross(newNormal, Vector3.up).normalized;
        Vector3 reference = inner ? wallNormal : -wallNormal;
        if (Vector3.Dot(tangent, reference) < 0f) tangent = -tangent;

        wallNormal = newNormal;
        runDir = tangent;
        stuckTimer = 0f;
        cornerTimer = cornerSteerTime;
        lastCornerTime = Time.time;
    }

    private void AddCameraYaw(Vector3 before, Vector3 after)
    {
        Vector3 a = new Vector3(before.x, 0f, before.z);
        Vector3 b = new Vector3(after.x, 0f, after.z);
        if (a.sqrMagnitude < 0.0001f || b.sqrMagnitude < 0.0001f) return;
        float delta = Vector3.SignedAngle(a, b, Vector3.up);
        if (Mathf.Abs(delta) < cameraDeadzone) return;
        pendingYaw += delta * cameraFollow;
    }

    // leaving the wall

    private void Leap()
    {
        Vector3 along = Vector3.ProjectOnPlane(rb.linearVelocity, wallNormal) * leapMomentumKeep;
        Vector3 exit = along + wallNormal * leapAwaySpeed + Vector3.up * leapUpSpeed;

        if (exit.y > maxLeapUpSpeed)
        {
            float excess = exit.y - maxLeapUpSpeed;
            exit.y = maxLeapUpSpeed;
            exit += new Vector3(wallNormal.x, 0f, wallNormal.z).normalized * excess;
        }

        exit = Vector3.ClampMagnitude(exit, maxExitSpeed);

        bool wasFast = Mode != WallMode.Crawling;
        movement.jumpConsumed = true;

        EndWall(exit, wasFast ? leapComboGain : 0f, wasFast ? leapDragRelief : 0f);
    }

    private void EndWall(Vector3 exitVelocity, float combo, float dragRelief)
    {
        IsActive = false;
        Mode = WallMode.None;
        nextAttachTime = Time.time + reattachDelay;
        swinging.EndZip(exitVelocity, combo, dragRelief);
    }

    private void EndWallOnGround()
    {
        IsActive = false;
        Mode = WallMode.None;
        nextAttachTime = Time.time + reattachDelay;
        swinging.EndWallOnGround();
    }

    private Vector3 GetInputDirection()
    {
        if (moveAction == null) return Vector3.zero;
        Vector2 input = moveAction.ReadValue<Vector2>();

        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        Vector3 dir = forward * input.y + right * input.x;
        if (dir.sqrMagnitude > 0.01f)
        {
            return dir.normalized;
        }
        else
        {
            return Vector3.zero;
        }
    }
}
