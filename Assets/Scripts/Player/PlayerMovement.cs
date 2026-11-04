using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField]
    private float moveSpeed;
    private float walkSpeed = 80f;
    private float sprintSpeed = 140f;
    private float swingSpeed = 120f;
    public float airDrag = 0.3f;
    public float airDragQuadratic = 0.025f;
    public float postReleaseGravity = 10f;
    public float fallGravity = 12f;

    public Transform orientation;

    [SerializeField]
    Vector3 moveDirection;

    Rigidbody rb;

    public InputActionAsset inputActions;
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction sprintAction;

    [Header("Ground check")]
    public float playerHeight;
    public LayerMask groundMask;
    [SerializeField]
    bool grounded;
    public float groundDrag;

    [Header("Jumping")]
    [SerializeField]
    private float currentJumpForce;
    public float minJumpForce;
    public float maxJumpForce;
    public float jumpCooldown;
    public float airMultiplier;
    public bool canJump = true;
    [SerializeField]
    private bool isCharging = false;
    [SerializeField]
    private float chargeRate = 12.5f;

    [Header("Slope Handling")]
    public float maxSlopeAngle;
    private RaycastHit slopeHit;
    private bool exitingSlope;
    private float slopeGravity = 80f;

    [Header("Landing Momentum")]
    [Range(0f, 1f)] public float landingSpeedKeep = 0.8f; // horizontal speed kept at touchdown
    public float carryDuration = 1.5f; // seconds until normal ground rules return
    [Range(0f, 1f)] public float carryDragRatio = 0.1f; // starting drag as a fraction of groundDrag
    private float carryTimer;
    private float carryCap;
    public float CarrySpeed => carryCap;

    public MovementState currentState;

    // Set by PlayerSwinging.
    public bool isSwinging = false; // rope is attached
    public bool swingMomentum = false; // rope attached or player is in air-control after web release
    public bool jumpConsumed = false; // set by PlayerSwinging when the jump press was used for a swing jump

    public bool IsGrounded => grounded;

    private WebZip zip;
    private PlayerSwinging playerSwinging;

    public enum MovementState
    {
        walking,
        sprinting,
        air,
        swinging
    }

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
        moveAction = InputSystem.actions.FindAction("Move");
        jumpAction = InputSystem.actions.FindAction("Jump");
        sprintAction = InputSystem.actions.FindAction("Sprint");
        zip = GetComponent<WebZip>();
        playerSwinging = GetComponent<PlayerSwinging>();
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        currentJumpForce = minJumpForce;
    }

    private void MovePlayer()
    {
        if (swingMomentum)
            return;

        zip.consecutiveZips = 0;
        if (OnSlope() && grounded && !exitingSlope)
        {
            rb.AddForce(GetSlopeMoveDirection() * moveSpeed, ForceMode.Force);
            if (rb.linearVelocity.y > 0)
            {
                rb.AddForce(Vector3.down * slopeGravity, ForceMode.Force);
            }
        }
        else if (grounded && !OnSlope())
        {
            rb.AddForce(moveDirection.normalized * moveSpeed, ForceMode.Force);
        }
        else
        {
            rb.AddForce(moveDirection.normalized * moveSpeed * airMultiplier, ForceMode.Force);
            AirDrag();
        }
    }

    private void FixedUpdate()
    {
        MovePlayer();
        grounded = Physics.Raycast(transform.position, Vector3.down, playerHeight * 0.5f + 0.2f, groundMask);
    }

    void Update()
    {
        JumpControl();

        bool jumpReleased = jumpAction.WasReleasedThisFrame();

        if (jumpReleased && canJump && grounded && !jumpConsumed)
        {
            canJump = false;
            Jump();
            Invoke(nameof(ResetJump), jumpCooldown);
        }

        if (jumpReleased)
        {
            currentJumpForce = minJumpForce;
            jumpConsumed = false;
        }

        SpeedControl();

        if (carryTimer > 0f) carryTimer -= Time.deltaTime;

        if (grounded && !swingMomentum)
            rb.linearDamping = CurrentGroundDrag();
        else
            rb.linearDamping = 0f;

        Vector2 input = moveAction.ReadValue<Vector2>();
        moveDirection = orientation.forward * input.y + orientation.right * input.x;
        StateHandler();

        rb.useGravity = swingMomentum || !(OnSlope() && grounded);
    }

    private void SpeedControl()
    {
        if (swingMomentum)
            return;

        float cap = CurrentSpeedCap();

        if (OnSlope() && !exitingSlope)
        {
            if (rb.linearVelocity.magnitude > cap)
            {
                rb.linearVelocity = rb.linearVelocity.normalized * cap;
            }
        }
        else
        {
            Vector3 flatVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            if (flatVelocity.magnitude > cap)
            {
                Vector3 limited = flatVelocity.normalized * cap;
                rb.linearVelocity = new Vector3(limited.x, rb.linearVelocity.y, limited.z);
            }
        }
    }

    private void JumpControl()
    {
        isCharging = jumpAction.IsPressed() && !jumpConsumed;
        if (isCharging)
        {
            currentJumpForce += chargeRate * Time.deltaTime;

            currentJumpForce = Mathf.Clamp(currentJumpForce, minJumpForce, maxJumpForce);
        }
    }

    private void Jump()
    {
        exitingSlope = true;
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(transform.up * currentJumpForce, ForceMode.Impulse);
    }

    private void ResetJump()
    {
        canJump = true;
        exitingSlope = false;
    }

    private void StateHandler()
    {
        if (isSwinging)
        {
            currentState = MovementState.swinging;
            moveSpeed = swingSpeed;
        }
        else if (grounded && sprintAction.IsPressed())
        {
            currentState = MovementState.sprinting;
            moveSpeed = sprintSpeed;
        }
        else if (grounded)
        {
            currentState = MovementState.walking;
            moveSpeed = walkSpeed;
        }
        else
        {
            currentState = MovementState.air;
        }
    }

    private bool OnSlope()
    {
        if (Physics.Raycast(transform.position, Vector3.down, out slopeHit, playerHeight * 0.5f + 0.3f))
        {
            float angle = Vector3.Angle(Vector3.up, slopeHit.normal);
            return angle < maxSlopeAngle && angle != 0f;
        }

        return false;
    }

    private Vector3 GetSlopeMoveDirection()
    {
        return Vector3.ProjectOnPlane(moveDirection, slopeHit.normal).normalized;
    }

    public void BeginLandingCarry()
    {
        Vector3 v = rb.linearVelocity;
        Vector3 flat = new Vector3(v.x, 0f, v.z) * landingSpeedKeep;
        rb.linearVelocity = new Vector3(flat.x, v.y, flat.z);
        carryCap = flat.magnitude;
        carryTimer = carryDuration;
    }

    private float CurrentSpeedCap()
    {
        if (carryTimer <= 0f) return moveSpeed;
        return Mathf.Max(moveSpeed, Mathf.Lerp(moveSpeed, carryCap, carryTimer / carryDuration));
    }

    private float CurrentGroundDrag()
    {
        if (carryTimer <= 0f) return groundDrag;
        float t = 1f - carryTimer / carryDuration;
        return Mathf.Lerp(groundDrag * carryDragRatio, groundDrag, t * t);
    }

    private void AirDrag()
    {
        float extra = postReleaseGravity;
        if (rb.linearVelocity.y < 0f)
            extra += fallGravity;

        rb.AddForce(Vector3.down * extra, ForceMode.Acceleration);

        Vector3 velocity = rb.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
        float speed = horizontal.magnitude;
        if (speed < 0.01f) return;

        float dragRate = airDrag + airDragQuadratic * speed;
        horizontal *= 1f - Mathf.Clamp01(dragRate * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
    }
}
