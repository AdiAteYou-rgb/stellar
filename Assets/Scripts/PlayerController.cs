using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    private CharacterController controller;
    private Vector3 playerVelocity;
    private bool groundedPlayer;

    [Header("Movement Settings")]
    [SerializeField] private float playerSpeed = 5.0f;
    [SerializeField] private float sprintMultiplier = 1.5f;
    [SerializeField] private float jumpHeight = 1.0f;
    [SerializeField] private float gravityValue = -9.81f;

    private void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    private void Update()
    {
        groundedPlayer = controller.isGrounded;
        if (groundedPlayer && playerVelocity.y < 0)
        {
            // Keep a small downward velocity so CharacterController reliably detects the ground
            playerVelocity.y = -2f;
        }

        // Get keyboard inputs (WASD / Arrows)
        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");
        Vector3 move = new Vector3(moveX, 0, moveZ);

        // Handle Sprinting (Shift key)
        float currentSpeed = playerSpeed;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            currentSpeed *= sprintMultiplier;
        }

        // Handle Jumping (Spacebar)
        if ((Input.GetKeyDown(KeyCode.Space) || Input.GetButtonDown("Jump")) && groundedPlayer)
        {
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -2.0f * gravityValue);
        }

        // Apply Gravity
        playerVelocity.y += gravityValue * Time.deltaTime;

        // Combine horizontal movement and vertical velocity into a single Move call
        Vector3 finalMove = (move * currentSpeed) + new Vector3(0, playerVelocity.y, 0);
        controller.Move(finalMove * Time.deltaTime);
    }
}
