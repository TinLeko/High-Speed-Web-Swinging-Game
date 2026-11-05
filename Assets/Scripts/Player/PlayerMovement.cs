using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Baseline movement controller for everything that is not a special ability: walking, sprinting, charge-up jumping, slopes, ground friction and air movement.
/// </summary>
/// <remarks>
/// When another system takes over (<see cref="swingMomentum"/> is true: swinging, zipping, wall-running or post-release flight)
/// this controller applies no movement force and no speed cap. Requires a <see cref="Rigidbody"/>, <see cref="WebZip"/> and
/// <see cref="PlayerSwinging"/> on the same GameObject. Inputs: "Move", "Jump" (hold to charge, release to jump) and "Sprint".
/// </remarks>

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
    public float carryDuration = 1.5f;                    // seconds until normal ground rules return
    [Range(0f, 1f)] public float carryDragRatio = 0.1f;   // starting drag as a fraction of groundDrag
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

    /// <summary>High-level movement state, used to pick the move speed.</summary>
    public enum MovementState
    {
        walking,
        sprinting,
        air,
        swinging
    }

    /// <summary>
    /// Unity message. Enables the "Player" input action map.
    /// </summary>
    private void OnEnable()
    {
        inputActions.FindActionMap("Player").Enable();
    }

    /// <summary>
    /// Unity message. Disables the "Player" input action map.
    /// </summary>
    private void OnDisable()
    {
        inputActions.FindActionMap("Player").Disable();
    }

    /// <summary>
    /// Unity message. Looks up the input actions and caches 
    /// the <see cref="WebZip"/> and <see cref="PlayerSwinging"/> components.
    /// </summary>
    private void Awake()
    {
        moveAction = InputSystem.actions.FindAction("Move");
        jumpAction = InputSystem.actions.FindAction("Jump");
        sprintAction = InputSystem.actions.FindAction("Sprint");
        zip = GetComponent<WebZip>();
        playerSwinging = GetComponent<PlayerSwinging>();
    }

    /// <summary>
    /// Unity message. Caches the Rigidbody, 
    /// freezes its rotation and initialises the jump charge to the 
    /// minimum force.
    /// </summary>
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        currentJumpForce = minJumpForce;
    }

    /// <summary>
    /// Applies movement forces for the current surface: along the slope plane 
    /// when on a walkable slope (with extra downward force while moving up),
    /// full force on flat ground, reduced force plus air drag in the air. 
    /// Does nothing while <see cref="swingMomentum"/> is set.
    /// Also resets the chained-zip counter.
    /// </summary>
    private void MovePlayer()
    {
        // PlayerSwinging owns all movement while swinging and right after release.
        if (swingMomentum)
            return;

        if (zip != null)
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

    /// <summary>
    /// Unity message. Applies movement forces, then updates the grounded 
    /// flag with a downward raycast.
    /// </summary>
    private void FixedUpdate()
    {
        MovePlayer();
        grounded = Physics.Raycast(transform.position, Vector3.down, playerHeight * 0.5f + 0.2f, groundMask);
    }

    /// <summary>
    /// Unity message. Handles the charge-up jump (jump fires on button release while grounded), 
    /// clamps speed, counts down the landing carry timer,
    /// sets ground drag, reads the move input into a camera-relative direction,
    /// updates the movement state and toggles gravity
    /// (off on walkable slopes so the player does not slide).
    /// </summary>
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

        // Ground drag would eat awaz speed if you release a swing close to the ground.
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

    /// <summary>
    /// Limits speed to the current cap: total speed on slopes, horizontal speed elsewhere (vertical velocity is preserved so jumps and falls are unaffected).
    /// Skipped while <see cref="swingMomentum"/> is set.
    /// </summary>
    private void SpeedControl()
    {
        // Don't cap speed while swinging, beacuse PlayerSwinging has its own maxSpeed.
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
                // Keeping the vertical velocity. Overwriting it with a flat vector kills jumps and falls.
                rb.linearVelocity = new Vector3(limited.x, rb.linearVelocity.y, limited.z);
            }
        }
    }

    /// <summary>
    /// Grows the jump force while the jump button is held 
    /// (unless the press was already consumed), 
    /// between <see cref="minJumpForce"/> and 
    /// <see cref="maxJumpForce"/>.
    /// </summary>
    private void JumpControl()
    {
        isCharging = jumpAction.IsPressed() && !jumpConsumed;
        if (isCharging)
        {
            currentJumpForce += chargeRate * Time.deltaTime;

            currentJumpForce = Mathf.Clamp(currentJumpForce, minJumpForce, maxJumpForce);
        }
    }

    /// <summary>
    /// Performs the jump: zeroes vertical velocity and applies the charged upward impulse.
    /// </summary>
    private void Jump()
    {
        exitingSlope = true;
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(transform.up * currentJumpForce, ForceMode.Impulse);
    }

    /// <summary>
    /// Invoked after <see cref="jumpCooldown"/>. Allows jumping again and ends the slope-exit state.
    /// </summary>
    private void ResetJump()
    {
        canJump = true;
        exitingSlope = false;
    }

    /// <summary>
    /// Chooses the <see cref="MovementState"/> (swinging, sprinting, walking or air) and sets the matching move speed.
    /// </summary>
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

    /// <summary>
    /// Checks whether the surface below is a slope steeper than flat but shallower than <see cref="maxSlopeAngle"/>. Stores the hit in <c>slopeHit</c>.
    /// </summary>
    /// <returns><c>true</c> if the player is standing on a walkable slope.</returns>
    private bool OnSlope()
    {
        if (Physics.Raycast(transform.position, Vector3.down, out slopeHit, playerHeight * 0.5f + 0.3f))
        {
            float angle = Vector3.Angle(Vector3.up, slopeHit.normal);
            return angle < maxSlopeAngle && angle != 0f;
        }

        return false;
    }

    /// <summary>
    /// Projects the movement direction onto the slope surface.
    /// </summary>
    /// <returns>A normalised direction parallel to the slope.</returns>
    private Vector3 GetSlopeMoveDirection()
    {
        return Vector3.ProjectOnPlane(moveDirection, slopeHit.normal).normalized;
    }

    /// <summary>
    /// Called by <see cref="PlayerSwinging"/> on touchdown. Keeps <see cref="landingSpeedKeep"/> of the 
    /// horizontal speed and starts a timer during which
    /// the speed cap and ground drag ease back to normal, so a fast landing is not stopped instantly.
    /// </summary>
    public void BeginLandingCarry()
    {
        Vector3 v = rb.linearVelocity;
        Vector3 flat = new Vector3(v.x, 0f, v.z) * landingSpeedKeep;
        rb.linearVelocity = new Vector3(flat.x, v.y, flat.z);
        carryCap = flat.magnitude;
        carryTimer = carryDuration;
    }

    /// <summary>
    /// Gets the horizontal speed cap, which fades from the carried landing speed down to the normal move speed over <see cref="carryDuration"/>.
    /// </summary>
    /// <returns>The speed cap in m/s.</returns>
    private float CurrentSpeedCap()
    {
        if (carryTimer <= 0f) return moveSpeed;
        return Mathf.Max(moveSpeed, Mathf.Lerp(moveSpeed, carryCap, carryTimer / carryDuration));
    }

    /// <summary>
    /// Gets the ground drag. It starts low right after a landing and 
    /// rises quadratically to <see cref="groundDrag"/> as the carry ends.
    /// </summary>
    private float CurrentGroundDrag()
    {
        if (carryTimer <= 0f) return groundDrag;
        float t = 1f - carryTimer / carryDuration; // 0 at landing, 1 when the carry ends
        return Mathf.Lerp(groundDrag * carryDragRatio, groundDrag, t * t);
    }

    /// <summary>
    /// Airborne handling: adds extra gravity (more while falling) and applies horizontal drag with a constant and a speed-squared-like term.
    /// Vertical velocity is untouched.
    /// </summary>
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
