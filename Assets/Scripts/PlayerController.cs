using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    private CharacterController controller;
    private Vector3 playerVelocity;
    private Vector3 externalKnockbackVelocity = Vector3.zero;
    private bool suppressNextLandingSlow = false;
    private bool groundedPlayer;
    private bool wasGroundedLastFrame = true;
    private float airborneTime = 0f;

    [Header("Movement Settings")]
    [SerializeField] private float playerSpeed = 5.5f;
    [SerializeField] private float sprintMultiplier = 1.5f;
    [SerializeField] private float jumpHeight = 1.25f;
    [SerializeField] private float gravityValue = -9.81f;

    [Header("Character & Ability Modifiers")]
    public float characterSpeedMultiplier = 1.0f;
    [HideInInspector] public float characterBaseFovBonus = 0f;
    [HideInInspector] public float parrySpeedBonusMultiplier = 1.0f;
    [HideInInspector] public float parrySpeedBonusTimer = 0f;
    [HideInInspector] public bool isBunnySliding = false;
    [HideInInspector] public bool isFeralLunging = false;
    [SerializeField] private float bunnySlideSpeedMultiplier = 1.48f;
    [SerializeField] private float bunnySlideFov = 92f;
    [SerializeField] private float feralLungeSpeedMultiplier = 2.45f;
    [SerializeField] private float feralLungeFov = 104f;

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
    [SerializeField] private float durianRollFov = 64f;
    [SerializeField] private float durianRollTurnMultiplier = 0.45f;
    [SerializeField] private float durianRollSpeedMultiplier = 1.65f;
    [SerializeField] private float durianRollSteerYawSpeed = 62f;
    [SerializeField] private float durianRollSpinSpeed = 540f;
    [SerializeField] private float durianRollBaseDip = -0.32f;
    [SerializeField] private float durianRollBobVerticalAmp = 0.12f;
    [SerializeField] private float durianRollBobLateralAmp = 0.045f;
    [SerializeField] private float durianRollBobForwardAmp = 0.03f;
    [SerializeField] private float durianRollPitchBobAmp = 7.0f;
    [SerializeField] private float durianRollWobbleTiltAmp = 4.5f;
    [SerializeField] private float durianRollTurnBankMax = 12.0f;

    public bool IsImmobilized => immobilizedTimer > 0f;
    public float DurianRollAngleDeg => durianRollAngleDeg;
    public float DurianRollVisualBounceY => currentDurianVisualBounceY;
    public float DurianRollSteerTilt => currentDurianTurnBank;

    private float cameraPitch = 0f;
    private float currentRollTilt = 0f;
    private float currentSlideCameraDip = 0f;
    private float durianRollAngleDeg = 0f;
    private float durianRollBlend = 0f;
    private float currentDurianRollDip = 0f;
    private float currentDurianTurnBank = 0f;
    private float currentDurianVisualBounceY = 0f;
    private bool wasDurianRollingLastFrame = false;
    private float lastFrameMouseTurnRate = 0f;
    private GridManager cachedGridManager;

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
        controller.height = 2.0f;
        controller.center = Vector3.zero;
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        controller.enabled = true;

        playerVelocity = Vector3.zero;
        externalKnockbackVelocity = Vector3.zero;
        suppressNextLandingSlow = false;
        wasGroundedLastFrame = true;
        airborneTime = 0f;
        landingSlowTimer = 0f;
        cameraPitch = 0f;
        currentRollTilt = 0f;
        currentSlideCameraDip = 0f;
        immobilizedTimer = 0f;
        isDurianRolling = false;
        isBunnySliding = false;
        isFeralLunging = false;
        ResetDurianRollState();
        parrySpeedBonusMultiplier = 1.0f;
        parrySpeedBonusTimer = 0f;

        // Hide player's own capsule mesh in first-person so it doesn't block camera view
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.enabled = true;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }

        if (playerCamera != null)
        {
            playerCamera.orthographic = false;
            playerCamera.fieldOfView = normalFov + characterBaseFovBonus;
            playerCamera.rect = new Rect(0f, 0f, 1f, 1f);
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void ExitFirstPersonArena()
    {
        isFirstPersonArenaActive = false;
        externalKnockbackVelocity = Vector3.zero;
        suppressNextLandingSlow = false;
        isDurianRolling = false;
        isBunnySliding = false;
        isFeralLunging = false;
        ResetDurianRollState();
        immobilizedTimer = 0f;
        parrySpeedBonusMultiplier = 1.0f;
        parrySpeedBonusTimer = 0f;
        characterSpeedMultiplier = 1.0f;
        characterBaseFovBonus = 0f;

        if (controller != null)
        {
            controller.height = 2.0f;
            controller.center = Vector3.zero;
        }

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
        isBunnySliding = false;
        isFeralLunging = false;
        ResetDurianRollState();
    }

    /// <summary>
    /// Cancels active knockback/airborne escape momentum (e.g., Moonflower Outburst) and movement abilities
    /// like Durian Roll without freezing normal WASD movement.
    /// </summary>
    public void CancelMobilityMomentum()
    {
        externalKnockbackVelocity = Vector3.zero;
        if (playerVelocity.y > 0f)
        {
            playerVelocity.y = 0f;
        }
        isDurianRolling = false;
        isBunnySliding = false;
        isFeralLunging = false;
        ResetDurianRollState();
    }

    private void ResetDurianRollState()
    {
        durianRollAngleDeg = 0f;
        durianRollBlend = 0f;
        currentDurianRollDip = 0f;
        currentDurianTurnBank = 0f;
        currentDurianVisualBounceY = 0f;
        wasDurianRollingLastFrame = false;
        lastFrameMouseTurnRate = 0f;
    }

    public void ApplyParrySpeedBoost(float bonusMultiplier, float duration = 3.0f)
    {
        parrySpeedBonusMultiplier = Mathf.Max(parrySpeedBonusMultiplier, bonusMultiplier);
        parrySpeedBonusTimer = duration;
    }

    public void ApplyKnockback(Vector3 horizontalVelocity, float upwardVelocity, bool suppressLandingSlow = false)
    {
        externalKnockbackVelocity = new Vector3(horizontalVelocity.x, 0f, horizontalVelocity.z);
        playerVelocity.y = Mathf.Max(playerVelocity.y, upwardVelocity);
        groundedPlayer = false;
        wasGroundedLastFrame = false;
        if (suppressLandingSlow)
        {
            suppressNextLandingSlow = true;
        }
    }

    private void Update()
    {
        // Only process 3D player movement when in the 3D 1v1 Arena (or if no GridManager is active)
        if (cachedGridManager == null)
        {
            cachedGridManager = FindAnyObjectByType<GridManager>();
        }
        if (cachedGridManager != null && !isFirstPersonArenaActive)
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

        // Tick down Buckethead 3.0s parry speed boost
        if (parrySpeedBonusTimer > 0f)
        {
            parrySpeedBonusTimer = Mathf.Max(0f, parrySpeedBonusTimer - Time.deltaTime);
            if (parrySpeedBonusTimer <= 0f)
            {
                parrySpeedBonusMultiplier = 1.0f;
            }
        }

        // Update CharacterController hitbox height when Bunny Zombie is sliding
        if (controller != null)
        {
            float targetHeight = isBunnySliding ? 1.0f : 2.0f;
            Vector3 targetCenter = isBunnySliding ? new Vector3(0f, -0.50f, 0f) : Vector3.zero;
            if (Mathf.Abs(controller.height - targetHeight) > 0.01f)
            {
                controller.height = targetHeight;
                controller.center = targetCenter;
            }
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
            if (suppressNextLandingSlow)
            {
                suppressNextLandingSlow = false;
            }
            else
            {
                landingSlowTimer = landingSlowDuration; // 0.5 second slowdown timer
            }
            landingCameraDip = -0.18f;              // Subtle physical camera dip on impact
        }
        else if (groundedPlayer)
        {
            suppressNextLandingSlow = false;
        }

        if (groundedPlayer)
        {
            airborneTime = 0f;
            if (playerVelocity.y < 0f)
            {
                playerVelocity.y = -2f;
            }
            if (externalKnockbackVelocity.sqrMagnitude > 0.0025f && playerVelocity.y <= 0f)
            {
                externalKnockbackVelocity = Vector3.Lerp(externalKnockbackVelocity, Vector3.zero, Time.deltaTime * 10.0f);
                if (externalKnockbackVelocity.sqrMagnitude <= 0.0025f) externalKnockbackVelocity = Vector3.zero;
            }
        }
        else
        {
            airborneTime += Time.deltaTime;
            if (externalKnockbackVelocity.sqrMagnitude > 0.0025f)
            {
                externalKnockbackVelocity = Vector3.Lerp(externalKnockbackVelocity, Vector3.zero, Time.deltaTime * 2.4f);
            }
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
            float turnMul = isDurianRolling ? durianRollTurnMultiplier : (isBunnySliding ? 0.78f : (isFeralLunging ? 0.72f : 1.0f));
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * turnMul;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * (isDurianRolling ? 0.65f : 1.0f);

            transform.Rotate(Vector3.up * mouseX);
            cameraPitch = Mathf.Clamp(cameraPitch - mouseY, -80f, 80f);
            lastFrameMouseTurnRate = (Time.deltaTime > 0.0001f) ? (mouseX / Time.deltaTime) : 0f;
        }
        else
        {
            lastFrameMouseTurnRate = 0f;
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
        else if (isFeralLunging)
        {
            // Feral Lunge Dive: high-speed pounce forward toward the Plant player with subtle steering!
            float steerX = moveX * 0.42f;
            moveDir = (transform.forward + transform.right * steerX).normalized;
        }
        else if (isDurianRolling)
        {
            // While rolling, A/D turns the whole Durian smoothly as well as giving subtle lateral steering
            if (Mathf.Abs(moveX) > 0.01f)
            {
                transform.Rotate(Vector3.up * (moveX * durianRollSteerYawSpeed * Time.deltaTime));
            }
            float steerX = moveX * 0.22f;
            moveDir = (transform.forward + transform.right * steerX).normalized;
        }
        else if (isBunnySliding)
        {
            // While Bunny Zombie is sliding, propel forward low across the floor with responsive steering
            float steerX = moveX * 0.55f;
            float forwardDrive = (Mathf.Abs(moveZ) > 0.1f && moveZ < 0f) ? 0.6f : 1.0f;
            moveDir = (transform.forward * forwardDrive + transform.right * steerX).normalized;
        }
        else if (isFirstPersonArenaActive)
        {
            moveDir = (transform.right * moveX + transform.forward * moveZ).normalized;
        }
        else
        {
            moveDir = new Vector3(moveX, 0f, moveZ).normalized;
        }

        // Calculate speed with character multiplier, sprint, slide, lunge, or Buckethead parry speed buff
        float currentSpeed = playerSpeed * characterSpeedMultiplier;
        if (parrySpeedBonusTimer > 0f)
        {
            currentSpeed *= parrySpeedBonusMultiplier;
        }

        if (isFeralLunging)
        {
            currentSpeed *= feralLungeSpeedMultiplier;
        }
        else if (isDurianRolling)
        {
            currentSpeed *= durianRollSpeedMultiplier;
        }
        else if (isBunnySliding)
        {
            currentSpeed *= bunnySlideSpeedMultiplier;
        }
        else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            currentSpeed *= sprintMultiplier;
        }

        // Apply 0.5-second Landing Slowdown so player feels the landing impact
        if (landingSlowTimer > 0f && !isDurianRolling && !isBunnySliding && !isFeralLunging)
        {
            currentSpeed *= landingSpeedMultiplier;
        }

        // Handle Jumping (Spacebar) - disabled while immobilized, rolling, sliding, or lunging
        if (!IsImmobilized && !isDurianRolling && !isBunnySliding && !isFeralLunging && (Input.GetKeyDown(KeyCode.Space) || Input.GetButtonDown("Jump")) && groundedPlayer)
        {
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -2.0f * gravityValue);
        }

        // Apply Gravity
        playerVelocity.y += gravityValue * Time.deltaTime;

        // Combine horizontal movement, knockback impulse, and vertical velocity into a single Move call
        Vector3 finalMove = (moveDir * currentSpeed) + new Vector3(externalKnockbackVelocity.x, playerVelocity.y, externalKnockbackVelocity.z);
        controller.Move(finalMove * Time.deltaTime);

        // Advance Durian Roll phase & blend so visual spin and first-person POV bob stay phase-locked
        if (isDurianRolling && !wasDurianRollingLastFrame)
        {
            durianRollAngleDeg = 0f;
        }
        wasDurianRollingLastFrame = isDurianRolling;

        durianRollBlend = Mathf.MoveTowards(durianRollBlend, isDurianRolling ? 1f : 0f, Time.deltaTime * (isDurianRolling ? 10f : 8f));
        if (isDurianRolling)
        {
            float horizSpeed = new Vector2(controller.velocity.x, controller.velocity.z).magnitude;
            float expectedRollSpeed = Mathf.Max(0.1f, playerSpeed * characterSpeedMultiplier * durianRollSpeedMultiplier);
            float speedScale = Mathf.Clamp(horizSpeed / expectedRollSpeed, 0.45f, 1.25f);
            durianRollAngleDeg = Mathf.Repeat(durianRollAngleDeg + durianRollSpinSpeed * speedScale * Time.deltaTime, 360f);
        }
        else if (durianRollBlend <= 0.001f)
        {
            durianRollAngleDeg = 0f;
        }

        float rollRad = durianRollAngleDeg * Mathf.Deg2Rad;
        float halfBounce = Mathf.Abs(Mathf.Sin(rollRad)); // 0 at ground strike, 1 at crest
        float spikeRipple = Mathf.Sin(rollRad * 6f) * 0.014f;
        float rollBobY = ((halfBounce - 0.45f) * (durianRollBobVerticalAmp * 2f)) + spikeRipple;
        float rollBobX = Mathf.Sin(rollRad) * durianRollBobLateralAmp;
        float rollBobZ = Mathf.Cos(rollRad * 2f) * durianRollBobForwardAmp;
        float rollPitchBob = 3.0f + Mathf.Sin(rollRad * 2f) * durianRollPitchBobAmp;
        float rollWobbleZ = Mathf.Cos(rollRad) * durianRollWobbleTiltAmp;

        currentDurianVisualBounceY = isDurianRolling ? (halfBounce * 0.10f) : 0f;

        float targetDurianDip = isDurianRolling ? durianRollBaseDip : 0f;
        currentDurianRollDip = Mathf.Lerp(currentDurianRollDip, targetDurianDip, Time.deltaTime * 12.0f);

        float targetTurnBank = isDurianRolling
            ? Mathf.Clamp((-lastFrameMouseTurnRate * 0.09f) - (moveX * 7.5f), -durianRollTurnBankMax, durianRollTurnBankMax)
            : 0f;
        currentDurianTurnBank = Mathf.Lerp(currentDurianTurnBank, targetTurnBank, Time.deltaTime * 10.0f);

        // Update First-Person Camera Position, FOV & Tilt
        if (isFirstPersonArenaActive && playerCamera != null)
        {
            float baseCharFov = normalFov + characterBaseFovBonus;
            float targetFov = isFeralLunging
                ? feralLungeFov
                : (isDurianRolling ? durianRollFov : (isBunnySliding ? bunnySlideFov : baseCharFov));
            if (parrySpeedBonusTimer > 0f)
            {
                targetFov += 8f;
            }
            playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFov, Time.deltaTime * 12.0f);

            // Slightly tilt the camera left and right based on horizontal movement (or slide lean)
            float slideTilt = isBunnySliding ? -7.5f : 0f;
            float targetTiltZ = isDurianRolling ? slideTilt : ((-moveX * strafeTiltAngle) + slideTilt);
            currentRollTilt = Mathf.Lerp(currentRollTilt, targetTiltZ, Time.deltaTime * strafeTiltSpeed);

            float targetSlideDip = isBunnySliding ? -0.46f : (isFeralLunging ? -0.18f : 0f);
            currentSlideCameraDip = Mathf.Lerp(currentSlideCameraDip, targetSlideDip, Time.deltaTime * 14.0f);

            float lungePitchOffset = isFeralLunging ? 7.5f : 0f;
            Vector3 rollBobOffset = (transform.up * rollBobY + transform.right * rollBobX + transform.forward * rollBobZ) * durianRollBlend;
            playerCamera.transform.position = transform.position
                + new Vector3(0f, eyeHeight + landingCameraDip + currentDurianRollDip + currentSlideCameraDip, 0f)
                + rollBobOffset;
            playerCamera.transform.rotation = Quaternion.Euler(
                cameraPitch + lungePitchOffset + (rollPitchBob * durianRollBlend),
                transform.eulerAngles.y,
                currentRollTilt + ((rollWobbleZ + currentDurianTurnBank) * durianRollBlend));
        }
    }
}
