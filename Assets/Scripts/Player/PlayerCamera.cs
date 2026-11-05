using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Computes the camera-relative orientation transform and turns the player model.
/// </summary>
/// <remarks>
/// While wall-running the model faces along the wall (running) or into the wall (climbing/crawling); otherwise it turns toward the direction
/// produced by the "Look" input relative to the camera. Also locks and hides the cursor.
/// </remarks>
public class PlayerCamera : MonoBehaviour
{
    public InputActionAsset inputActions;
    public InputAction lookAction;
    public Transform orientation;
    public Transform player;
    public Transform playerObj;
    public Rigidbody rb;
    public float rotationSpeed;
    public Wallrun wallRun;

    /// <summary>Unity message. Enables the "Player" input action map.</summary>
    private void OnEnable()
    {
        inputActions.FindActionMap("Player").Enable();
    }

    /// <summary>Unity message. Disables the "Player" input action map.</summary>
    private void OnDisable()
    {
        inputActions.FindActionMap("Player").Disable();
    }

    /// <summary>Unity message. Looks up the "Look" input action.</summary>
    private void Awake()
    {
        lookAction = InputSystem.actions.FindAction("Look");
    }

    /// <summary>Unity message. Locks and hides the mouse cursor.</summary>
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    /// <summary>
    /// Unity message. Sets <c>orientation.forward</c> to the horizontal direction from the camera to the player, rotates the player model smoothly
    /// toward the look-input direction, and, while a wall run is active, overrides that to face along the wall or into it.
    /// </summary>
    private void Update()
    {
        Vector3 viewDir = player.position - new Vector3(transform.position.x, player.position.y, transform.position.z);
        orientation.forward = viewDir.normalized;
        Vector3 inputDir = orientation.forward * lookAction.ReadValue<Vector2>().y + orientation.right * lookAction.ReadValue<Vector2>().x;
        if (inputDir != Vector3.zero)
        {
            playerObj.forward = Vector3.Slerp(playerObj.forward, inputDir.normalized, Time.deltaTime * rotationSpeed);
        }

        if (wallRun != null && wallRun.IsActive)
        {
            Vector3 face;
            if (wallRun.Mode == Wallrun.WallMode.Running)
            {
                face = wallRun.RunDirection; // face along the wall
            }
            else
            {
                face = -wallRun.WallNormal;
            }
            playerObj.forward = Vector3.Slerp(playerObj.forward, face, Time.deltaTime * rotationSpeed);
            return;
        }
    }

}
