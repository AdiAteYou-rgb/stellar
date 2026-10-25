using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    private CharacterController controller;
    private Vector3 playerVelocity;
    private bool groundedPlayer;
    private bool wasGroundedLastFrame = true;
    private float airborneTime = 0f;

    [Header("Movement Settings")]
    [SerializeField] private float playerSpeed = 5.5f;
    [SerializeField] private float sprintMultiplier = 1.5f;
    [SerializeField] private float jumpHeight = 1.25f;
    [SerializeField] private float gravityValue = -9.81f;

    [Header("Landing Impact Slowdown (0.5s)")]
    [SerializeField] private float landingSlowDuration = 0.5f;
    [SerializeField] private float landingSpeedMultiplier = 0.45f;
    private float landingSlowTimer = 0f;
    private float landingCameraDip = 0f;

    [Header("First-Person Camera & Strafe Tilt")]
    public bool isFirstPersonArenaActive = false;
    [HideInInspector] public bool inputFrozen = false;
    [HideInInspector] public float immobilizedTimer = 0f;
    [HideInInspector] public bool isDurianRolling = false;
    public string currentOpponentRoleName = "OPPONENT";
    public Camera playerCamera;
    [SerializeField] private float mouseSensitivity = 2.2f;
    [SerializeField] private float eyeHeight = 0.72f;
    [SerializeField] private float strafeTiltAngle = 4.5f;
    [SerializeField] private float strafeTiltSpeed = 10.0f;

    [Header("Durian Roll Camera & Turn Settings")]
    [SerializeField] private float normalFov = 75f;
    [SerializeField] private float durianRollFov = 58f;
    [SerializeField] private float durianRollTurnMultiplier = 0.38f;
    [SerializeField] private float durianRollSpeedMultiplier = 1.65f;

    public bool IsImmobilized => immobilizedTimer > 0f;

    private float cameraPitch = 0f;
    private float currentRollTilt = 0f;

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }

        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.enabled = isFirstPersonArenaActive;
        }
    }

    public void EnterFirstPersonArena(Camera cam, Vector3 spawnPosition, Quaternion spawnRotation, string opponentName = "OPPONENT")
    {
        if (controller == null)
        {
            controller = GetComponent<CharacterController>();
        }

        playerCamera = cam;
        isFirstPersonArenaActive = true;
        currentOpponentRoleName = opponentName;

        controller.enabled = false;
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        controller.enabled = true;

        playerVelocity = Vector3.zero;
        wasGroundedLastFrame = true;
        airborneTime = 0f;
        landingSlowTimer = 0f;
        cameraPitch = 0f;
        currentRollTilt = 0f;
        immobilizedTimer = 0f;
        isDurianRolling = false;

        // Hide player's own capsule mesh in first-person so it doesn't block camera view
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }

        if (playerCamera != null)
        {
            playerCamera.orthographic = false;
            playerCamera.fieldOfView = normalFov;
            playerCamera.rect = new Rect(0f, 0f, 1f, 1f);
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void ExitFirstPersonArena()
    {
        isFirstPersonArenaActive = false;
        isDurianRolling = false;
        immobilizedTimer = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.enabled = false;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }

    public void Immobilize(float duration)
    {
        immobilizedTimer = Mathf.Max(immobilizedTimer, duration);
        isDurianRolling = false;
    }

    private void Update()
    {
        // Only process 3D player movement when in the 3D 1v1 Arena (or if no GridManager is active)
        GridManager grid = FindFirstObjectByType<GridManager>();
        if (grid != null && !isFirstPersonArenaActive)
        {
            return;
        }

        // Frozen during the "ZOMBIE WON / PLANT WON" pause
        if (inputFrozen)
        {
            return;
        }

        if (immobilizedTimer > 0f)
        {
            immobilizedTimer = Mathf.Max(0f, immobilizedTimer - Time.deltaTime);
        }

        // Toggle cursor lock with Escape in Arena mode
        if (isFirstPersonArenaActive)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                bool locked = Cursor.lockState == CursorLockMode.Locked;
                Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = locked;
            }
            else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        groundedPlayer = controller.isGrounded;

        // Detect Landing Impact: Player was airborne and just touched the ground
        if (!wasGroundedLastFrame && groundedPlayer && airborneTime > 0.12f)
        {
            landingSlowTimer = landingSlowDuration; // 0.5 second slowdown timer
            landingCameraDip = -0.18f;              // Subtle physical camera dip on impact
        }

        if (groundedPlayer)
        {
            airborneTime = 0f;
            if (playerVelocity.y < 0f)
            {
                playerVelocity.y = -2f;
            }
        }
        else
        {
            airborneTime += Time.deltaTime;
        }
        wasGroundedLastFrame = groundedPlayer;

        // Tick down the 0.5s landing slowdown timer
        if (landingSlowTimer > 0f)
        {
            landingSlowTimer = Mathf.Max(0f, landingSlowTimer - Time.deltaTime);
        }
        landingCameraDip = Mathf.Lerp(landingCameraDip, 0f, Time.deltaTime * 10.0f);

        // First-Person Mouse Look (harder to turn while Durian is rolling)
        if (isFirstPersonArenaActive && Cursor.lockState == CursorLockMode.Locked)
        {
            float turnMul = isDurianRolling ? durianRollTurnMultiplier : 1.0f;
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * turnMul;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * (isDurianRolling ? 0.65f : 1.0f);

            transform.Rotate(Vector3.up * mouseX);
            cameraPitch = Mathf.Clamp(cameraPitch - mouseY, -80f, 80f);
        }

        // Get keyboard inputs (WASD / Arrows)
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveZ = Input.GetAxisRaw("Vertical");

        Vector3 moveDir;
        if (IsImmobilized)
        {
            // Immobilized by Durian roll connection: cannot move!
            moveDir = Vector3.zero;
            moveX = 0f;
            moveZ = 0f;
        }
        else if (isDurianRolling)
        {
            // While rolling, propel forward with reduced lateral steering
            float steerX = moveX * 0.35f;
            moveDir = (transform.forward + transform.right * steerX).normalized;
        }
        else if (isFirstPersonArenaActive)
        {
            moveDir = (transform.right * moveX + transform.forward * moveZ).normalized;
        }
        else
        {
            moveDir = new Vector3(moveX, 0f, moveZ).normalized;
        }

        // Handle Sprinting (Shift key) or Durian Roll speed boost
        float currentSpeed = playerSpeed;
        if (isDurianRolling)
        {
            currentSpeed *= durianRollSpeedMultiplier;
        }
        else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            currentSpeed *= sprintMultiplier;
        }

        // Apply 0.5-second Landing Slowdown so player feels the landing impact
        if (landingSlowTimer > 0f && !isDurianRolling)
        {
            currentSpeed *= landingSpeedMultiplier;
        }

        // Handle Jumping (Spacebar) - disabled while immobilized or rolling
        if (!IsImmobilized && !isDurianRolling && (Input.GetKeyDown(KeyCode.Space) || Input.GetButtonDown("Jump")) && groundedPlayer)
        {
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -2.0f * gravityValue);
        }

        // Apply Gravity
        playerVelocity.y += gravityValue * Time.deltaTime;

        // Combine horizontal movement and vertical velocity into a single Move call
        Vector3 finalMove = (moveDir * currentSpeed) + new Vector3(0f, playerVelocity.y, 0f);
        controller.Move(finalMove * Time.deltaTime);

        // Update First-Person Camera Position, FOV (lowered while Durian rolling), & Left/Right Movement Tilt
        if (isFirstPersonArenaActive && playerCamera != null)
        {
            float targetFov = isDurianRolling ? durianRollFov : normalFov;
            playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFov, Time.deltaTime * 10.0f);

            // Slightly tilt the camera left and right based on horizontal movement (moveX)
            float targetTiltZ = -moveX * strafeTiltAngle;
            currentRollTilt = Mathf.Lerp(currentRollTilt, targetTiltZ, Time.deltaTime * strafeTiltSpeed);

            float rollDip = isDurianRolling ? -0.25f : 0f;
            playerCamera.transform.position = transform.position + new Vector3(0f, eyeHeight + landingCameraDip + rollDip, 0f);
            playerCamera.transform.rotation = Quaternion.Euler(cameraPitch, transform.eulerAngles.y, currentRollTilt);
        }
    }
}
