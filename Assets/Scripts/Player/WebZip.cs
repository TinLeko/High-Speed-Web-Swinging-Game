using UnityEngine;
using UnityEngine.InputSystem;

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
    public float aimAssistRadius = 0.6f;

    [Header("Zip")]
    public float zipStartSpeed = 45f;
    public float zipTopSpeed = 90f;
    public float zipAcceleration = 250f;
    public float steerAcceleration = 600f;
    public float arriveDistance = 3f;
    public float maxZipTime = 1.5f;
    public float cooldown = 0.6f;
    public float ropeShootSpeed = 300f;
    public float consecutiveZips = 1f;

    [Header("Exit")]
    public float exitSpeedBonus = 12f;
    public float maxExitSpeed = 100f;
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

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        swinging = GetComponent<PlayerSwinging>();
        movement = GetComponent<PlayerMovement>();
        zipAction = InputSystem.actions.FindAction("Zip");
        jumpAction = InputSystem.actions.FindAction("Jump");
        aimCamera = Camera.main;
    }

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

        rb.linearVelocity = Vector3.MoveTowards(
            rb.linearVelocity, dir * zipSpeed, steerAcceleration * Time.fixedDeltaTime);

        rb.AddForce(-Physics.gravity, ForceMode.Acceleration);
    }

    private void LateUpdate()
    {
        if (!IsZipping) return;

        lineEnd = Vector3.MoveTowards(lineEnd, target, ropeShootSpeed * Time.deltaTime);
        zipLine.SetPosition(0, webTip.position);
        zipLine.SetPosition(1, lineEnd);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (IsZipping && Time.time - zipStartTime > 0.1f)
            FinishZip();
    }

    public void CancelForRespawn()
    {
        if (!IsZipping) return;
        IsZipping = false;
        zipLine.positionCount = 0;
        nextZipTime = Time.time + cooldown;
    }

    // aiming

    private Ray GetAimRay()
    {
        bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;
        if (cursorLocked || Mouse.current == null)
            return aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        return aimCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
    }

    private bool TryFindTarget(out Vector3 point)
    {
        point = Vector3.zero;
        Ray ray = GetAimRay();

        float skip = Mathf.Max(0f, Vector3.Dot(transform.position - ray.origin, ray.direction));
        Vector3 origin = ray.origin + ray.direction * skip;

        RaycastHit hit;
        bool found = Physics.Raycast(origin, ray.direction, out hit, maxZipDistance, zippable, QueryTriggerInteraction.Ignore) ||
            Physics.SphereCast(origin, aimAssistRadius, ray.direction, out hit, maxZipDistance, zippable, QueryTriggerInteraction.Ignore);

        if (!found || hit.distance <= 0f) return false;
        if (Vector3.Distance(transform.position, hit.point) < minZipDistance) return false;

        point = hit.point;
        return true;
    }

    // Zip

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
