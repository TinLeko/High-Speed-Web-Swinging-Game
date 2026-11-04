using UnityEngine;
using UnityEngine.InputSystem;

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
        lookAction = InputSystem.actions.FindAction("Look");
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

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
                face = wallRun.RunDirection;
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
