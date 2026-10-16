using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
#endif

/// <summary>
/// A complete first-person controller supporting WASD movement, jumping, mouse look,
/// and smooth camera control. Automatically compatible with both Unity's New Input System
/// and Legacy Input Manager, works with Rigidbody or CharacterController, and seamlessly
/// integrates with Photon PUN multiplayer networking when present.
/// </summary>
[DisallowMultipleComponent]
public class FirstPersonController : MonoBehaviour
{
#if PHOTON_UNITY_NETWORKING
    private PhotonView photonView;
#endif

    [Header("Movement")]
    [Tooltip("Normal walking speed in m/s.")]
    [SerializeField] private float walkSpeed = 5.0f;

    [Tooltip("Sprint speed in m/s (hold Left Shift).")]
    [SerializeField] private float sprintSpeed = 8.5f;

    [Tooltip("Rate of acceleration/deceleration.")]
    [SerializeField] private float acceleration = 20.0f;

    [Header("Jumping & Gravity")]
    [Tooltip("Maximum jump height in meters.")]
    [SerializeField] private float jumpHeight = 1.5f;

    [Tooltip("Downward gravity force applied when using CharacterController or custom gravity.")]
    [SerializeField] private float customGravity = -19.62f;

    [Tooltip("Layer mask specifying what counts as ground.")]
    [SerializeField] private LayerMask groundMask = ~0;

    [Tooltip("Radius of the ground check sphere.")]
    [SerializeField] private float groundCheckRadius = 0.35f;

    [Header("Look / Camera")]
    [Tooltip("First person camera transform. If left empty, will auto-detect from child objects.")]
    [SerializeField] private Transform playerCamera;

    [Tooltip("Mouse sensitivity multiplier.")]
    [SerializeField] private float mouseSensitivity = 2.0f;

    [Tooltip("Minimum vertical camera angle (looking down).")]
    [SerializeField] private float minPitch = -85.0f;

    [Tooltip("Maximum vertical camera angle (looking up).")]
    [SerializeField] private float maxPitch = 85.0f;

    [Tooltip("Automatically lock the cursor when clicking into the game.")]
    [SerializeField] private bool autoLockCursor = true;

    [Tooltip("Center the camera horizontally at (0, eyeHeight, 0) relative to player.")]
    [SerializeField] private bool autoCenterCamera = true;

    [Tooltip("Eye height of the camera if auto-centering.")]
    [SerializeField] private float eyeHeight = 0.8f;

    [Tooltip("If true, your own body casts a shadow on the floor. If false, no shadow is cast by your own body.")]
    [SerializeField] private bool castLocalShadow = false;

    public enum PerspectiveMode
    {
        FirstPerson,
        ThirdPerson
    }

    [Header("Perspective (1st / 3rd Person)")]
    [Tooltip("Current camera perspective.")]
    [SerializeField] private PerspectiveMode currentPerspective = PerspectiveMode.FirstPerson;

    [Tooltip("Camera offset in third-person mode relative to eye pivot.")]
    [SerializeField] private Vector3 thirdPersonOffset = new Vector3(0.35f, 0.15f, -2.8f);

    [Tooltip("Speed of camera transition between perspectives.")]
    [SerializeField] private float perspectiveTransitionSpeed = 14f;

    [Tooltip("Layers that obstruct third-person camera (e.g. walls).")]
    [SerializeField] private LayerMask cameraCollisionMask = ~0;

    public PerspectiveMode CurrentPerspective => currentPerspective;

    // Internal components
    private CharacterController characterController;
    private Rigidbody rb;
    private CapsuleCollider capsuleCollider;

    // Movement state
    private Vector2 currentInput;
    private bool isSprinting;
    private bool isGrounded;
    private float currentSpeed;
    private Vector3 currentVelocity;
    private float verticalVelocity; // Used for CharacterController

    // Jump timing
    private float jumpBufferTimer;
    private float coyoteTimer;
    private const float JumpBufferDuration = 0.15f;
    private const float CoyoteDuration = 0.15f;

    // Camera state
    private float cameraPitch = 0.0f;
    private bool isCursorLocked = false;

    private void Awake()
    {
#if PHOTON_UNITY_NETWORKING
        photonView = GetComponent<PhotonView>();
#endif
        characterController = GetComponent<CharacterController>();
        rb = GetComponent<Rigidbody>();
        capsuleCollider = GetComponent<CapsuleCollider>();

        SetupCamera();
        SetupPhysics();
        SetupMeshVisibility();
    }

    private void Start()
    {
#if PHOTON_UNITY_NETWORKING
        if (photonView != null && !photonView.IsMine)
        {
            // If this is a remote network player, disable their camera & listener locally
            if (playerCamera != null)
            {
                playerCamera.gameObject.SetActive(false);
                AudioListener listener = playerCamera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
            }
            return;
        }

        // If connected to Photon but not in a room yet (e.g. while in Lobby), keep cursor completely free for UI
        if (PhotonNetwork.IsConnected && !PhotonNetwork.InRoom)
        {
            SetCursorLocked(false);
            return;
        }
#endif

        if (autoLockCursor)
        {
            SetCursorLocked(true);
        }

        DisableConflictingSceneCameras();
        SetupMeshVisibility();
    }

    private void OnDisable()
    {
#if PHOTON_UNITY_NETWORKING
        if (photonView == null || photonView.IsMine)
        {
            SetCursorLocked(false);
        }
#else
        SetCursorLocked(false);
#endif
    }

    private void Update()
    {
#if PHOTON_UNITY_NETWORKING
        // While in lobby, keep cursor free and do not process character input
        if (PhotonNetwork.IsConnected && !PhotonNetwork.InRoom) return;
        if (photonView != null && !photonView.IsMine) return;
#endif

        // When Game Over is active, freeze look/movement and keep cursor unlocked
        if (PlayerHealth.IsGameOver)
        {
            if (isCursorLocked)
            {
                SetCursorLocked(false);
            }
            return;
        }

        // When in-game pause/exit menu is open, freeze look/movement and keep cursor unlocked
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsInGameMenuOpen)
        {
            if (isCursorLocked)
            {
                SetCursorLocked(false);
            }
            return;
        }

        // Fall-through protection: if player falls off the world, respawn onto ground
        if (transform.position.y < -20f)
        {
            transform.position = new Vector3(0f, 2f, 0f);
            if (rb != null)
            {
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = Vector3.zero;
#else
                rb.velocity = Vector3.zero;
#endif
            }
        }

        HandleCursorLockInput();
        HandlePerspectiveInput();
        HandleLook();
        ReadMovementInput();
        UpdateTimers();

        if (characterController != null && characterController.enabled)
        {
            MoveWithCharacterController();
        }
    }

    private void FixedUpdate()
    {
#if PHOTON_UNITY_NETWORKING
        if (photonView != null && !photonView.IsMine) return;
        if (PhotonNetwork.IsConnected && !PhotonNetwork.InRoom) return;
#endif
        if (PlayerHealth.IsGameOver) return;

        if (rb != null && !rb.isKinematic && (characterController == null || !characterController.enabled))
        {
            MoveWithRigidbody();
        }
    }

    #region Setup

    private void SetupCamera()
    {
        if (playerCamera == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null)
            {
                playerCamera = cam.transform;
            }
            else if (Camera.main != null && Camera.main.transform.IsChildOf(transform))
            {
                playerCamera = Camera.main.transform;
            }
        }

        if (playerCamera != null)
        {
            if (autoCenterCamera)
            {
                playerCamera.localPosition = new Vector3(0f, eyeHeight, 0.15f);
            }
            // Start looking slightly down towards the ground
            cameraPitch = 10.0f;
            playerCamera.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
        }
        else
        {
            Debug.LogWarning("[FirstPersonController] No Camera found on Player or as child! Please assign a Camera to PlayerCamera.");
        }
    }

    public void SetupMeshVisibility()
    {
#if PHOTON_UNITY_NETWORKING
        bool isLocal = (photonView == null || photonView.IsMine);
#else
        bool isLocal = true;
#endif
        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
        foreach (MeshRenderer r in renderers)
        {
            if (playerCamera != null && r.transform.IsChildOf(playerCamera)) continue;
            if (r.gameObject.name.Contains("Beacon") || r.gameObject.name.Contains("Tracer")) continue;

            if (isLocal)
            {
                if (currentPerspective == PerspectiveMode.ThirdPerson)
                {
                    r.enabled = true;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
                else
                {
                    if (castLocalShadow)
                    {
                        r.enabled = true;
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                    }
                    else
                    {
                        r.enabled = false;
                    }
                }
            }
            else
            {
                r.enabled = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }
    }

    private void SetupPhysics()
    {
        if (characterController != null && rb != null)
        {
            // Avoid physics conflicts between CharacterController and Rigidbody
            rb.isKinematic = true;
        }
        else if (rb != null)
        {
            // Freeze rotation so player capsule doesn't tip over
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }
    }

    private void DisableConflictingSceneCameras()
    {
        if (playerCamera == null) return;

        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (Camera cam in cameras)
        {
            if (cam != null && cam.gameObject != playerCamera.gameObject && !cam.transform.IsChildOf(transform))
            {
                cam.gameObject.SetActive(false);
                Debug.Log($"[FirstPersonController] Disabled external scene camera '{cam.name}' to prioritize player camera.");
            }
        }
    }

    #endregion

    #region Input Handling

    private void ReadMovementInput()
    {
        Vector2 input = Vector2.zero;
        bool jumpPressed = false;
        bool sprintHeld = false;

        // 1. Try New Input System
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) input.y += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) input.y -= 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1f;

            if (Keyboard.current.spaceKey.wasPressedThisFrame) jumpPressed = true;
            if (Keyboard.current.leftShiftKey.isPressed) sprintHeld = true;
        }
#endif

        // 2. Fallback to Legacy Input Manager
#if ENABLE_LEGACY_INPUT_MANAGER
        if (input == Vector2.zero)
        {
            input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        }
        if (!jumpPressed && (Input.GetButtonDown("Jump") || Input.GetKeyDown(KeyCode.Space)))
        {
            jumpPressed = true;
        }
        if (!sprintHeld && Input.GetKey(KeyCode.LeftShift))
        {
            sprintHeld = true;
        }
#endif

        currentInput = Vector2.ClampMagnitude(input, 1.0f);
        isSprinting = sprintHeld && currentInput.y > 0.1f;

        if (jumpPressed)
        {
            jumpBufferTimer = JumpBufferDuration;
        }
    }

    private void HandleLook()
    {
        if (!isCursorLocked || playerCamera == null) return;

        Vector2 mouseDelta = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            // In the new input system, delta is in pixels. Scale by 0.05 to balance sensitivity.
            mouseDelta = Mouse.current.delta.ReadValue() * 0.05f;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (mouseDelta == Vector2.zero)
        {
            mouseDelta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        }
#endif

        float mouseX = mouseDelta.x * mouseSensitivity;
        float mouseY = mouseDelta.y * mouseSensitivity;

        // Horizontal rotation on the player body (Yaw)
        transform.Rotate(Vector3.up * mouseX);

        // Vertical rotation on the camera (Pitch)
        cameraPitch -= mouseY;
        cameraPitch = Mathf.Clamp(cameraPitch, minPitch, maxPitch);

        if (currentPerspective == PerspectiveMode.FirstPerson)
        {
            Vector3 firstPersonPos = new Vector3(0f, eyeHeight, 0.15f);
            playerCamera.localPosition = Vector3.Lerp(playerCamera.localPosition, firstPersonPos, Time.deltaTime * perspectiveTransitionSpeed);
            playerCamera.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
        }
        else
        {
            // Third Person Orbit & Wall Collision Avoidance
            Vector3 pivotLocal = new Vector3(0f, eyeHeight, 0f);
            Quaternion pitchRot = Quaternion.Euler(cameraPitch, 0f, 0f);

            Vector3 worldPivot = transform.TransformPoint(pivotLocal);
            Vector3 worldLookDir = transform.rotation * pitchRot * Vector3.forward;
            Vector3 worldBackDir = -worldLookDir;

            float targetDistance = Mathf.Abs(thirdPersonOffset.z);
            float actualDistance = targetDistance;

            // SphereCast backwards to prevent camera from clipping into walls
            if (Physics.SphereCast(worldPivot, 0.22f, worldBackDir, out RaycastHit hit, targetDistance, cameraCollisionMask, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(transform))
                {
                    actualDistance = Mathf.Clamp(hit.distance - 0.15f, 0.4f, targetDistance);
                }
            }

            Vector3 computedLocalOffset = new Vector3(thirdPersonOffset.x, thirdPersonOffset.y, -actualDistance);
            Vector3 targetLocalPos = pivotLocal + pitchRot * computedLocalOffset;

            playerCamera.localPosition = Vector3.Lerp(playerCamera.localPosition, targetLocalPos, Time.deltaTime * perspectiveTransitionSpeed);
            playerCamera.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
        }
    }

    private void HandlePerspectiveInput()
    {
        bool togglePressed = false;
        float scroll = 0f;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.vKey.wasPressedThisFrame || Keyboard.current.f5Key.wasPressedThisFrame)
            {
                togglePressed = true;
            }
        }
        if (Mouse.current != null)
        {
            scroll = Mouse.current.scroll.ReadValue().y;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.V) || Input.GetKeyDown(KeyCode.F5))
        {
            togglePressed = true;
        }
        if (Mathf.Abs(scroll) < 0.01f)
        {
            scroll = Input.GetAxis("Mouse ScrollWheel");
        }
#endif

        if (togglePressed)
        {
            TogglePerspective();
        }
        else if (scroll < -0.1f && currentPerspective == PerspectiveMode.FirstPerson)
        {
            SetPerspective(PerspectiveMode.ThirdPerson);
        }
        else if (scroll > 0.1f && currentPerspective == PerspectiveMode.ThirdPerson)
        {
            SetPerspective(PerspectiveMode.FirstPerson);
        }
    }

    public void TogglePerspective()
    {
        SetPerspective(currentPerspective == PerspectiveMode.FirstPerson ? PerspectiveMode.ThirdPerson : PerspectiveMode.FirstPerson);
    }

    public void SetPerspective(PerspectiveMode mode)
    {
        currentPerspective = mode;
        SetupMeshVisibility();

        PlayerShooter shooter = GetComponent<PlayerShooter>();
        if (shooter != null)
        {
            shooter.SetWeaponPerspective(currentPerspective == PerspectiveMode.ThirdPerson);
        }
    }

    private void HandleCursorLockInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (NetworkManager.Instance == null)
            {
                SetCursorLocked(false);
            }
        }
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !isCursorLocked)
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsInGameMenuOpen)
            {
                SetCursorLocked(true);
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (NetworkManager.Instance == null)
            {
                SetCursorLocked(false);
            }
        }
        if (Input.GetMouseButtonDown(0) && !isCursorLocked)
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsInGameMenuOpen)
            {
                SetCursorLocked(true);
            }
        }
#endif
    }

    public void SetCursorLocked(bool locked)
    {
        isCursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    #endregion

    #region Movement Mechanics

    private void UpdateTimers()
    {
        if (jumpBufferTimer > 0f) jumpBufferTimer -= Time.deltaTime;

        CheckGrounded();

        if (isGrounded)
        {
            coyoteTimer = CoyoteDuration;
        }
        else
        {
            if (coyoteTimer > 0f) coyoteTimer -= Time.deltaTime;
        }
    }

    private void CheckGrounded()
    {
        if (characterController != null && characterController.enabled)
        {
            isGrounded = characterController.isGrounded;
            return;
        }

        Vector3 checkPos = GetGroundCheckPosition();
        Collider[] colliders = Physics.OverlapSphere(checkPos, groundCheckRadius, groundMask, QueryTriggerInteraction.Ignore);
        isGrounded = false;

        foreach (Collider col in colliders)
        {
            if (col != null && col.gameObject != gameObject && !col.transform.IsChildOf(transform))
            {
                isGrounded = true;
                break;
            }
        }
    }

    private Vector3 GetGroundCheckPosition()
    {
        float bottomOffset = 1.0f; // Default for 2m height capsule
        if (capsuleCollider != null)
        {
            bottomOffset = (capsuleCollider.height * 0.5f) - capsuleCollider.center.y;
        }

        // Slightly above bottom sphere edge to catch ground contacts
        return transform.position + Vector3.down * (bottomOffset - groundCheckRadius * 0.75f);
    }

    private void MoveWithCharacterController()
    {
        float targetSpeed = (isSprinting ? sprintSpeed : walkSpeed) * currentInput.magnitude;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.deltaTime);

        Vector3 moveDirection = (transform.forward * currentInput.y + transform.right * currentInput.x);
        if (moveDirection.sqrMagnitude > 1f) moveDirection.Normalize();

        Vector3 motion = moveDirection * currentSpeed;

        if (isGrounded)
        {
            if (verticalVelocity < 0f)
            {
                verticalVelocity = -2f; // Slight downward push to stick to ground
            }

            if (jumpBufferTimer > 0f && coyoteTimer > 0f)
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * customGravity);
                jumpBufferTimer = 0f;
                coyoteTimer = 0f;
            }
        }
        else
        {
            verticalVelocity += customGravity * Time.deltaTime;
        }

        motion.y = verticalVelocity;
        characterController.Move(motion * Time.deltaTime);
    }

    private void MoveWithRigidbody()
    {
        float targetSpeed = isSprinting ? sprintSpeed : walkSpeed;
        Vector3 moveDirection = (transform.forward * currentInput.y + transform.right * currentInput.x);
        if (moveDirection.sqrMagnitude > 1f) moveDirection.Normalize();

        Vector3 targetVelocity = moveDirection * targetSpeed;

#if UNITY_6000_0_OR_NEWER
        Vector3 currentRbVel = rb.linearVelocity;
#else
        Vector3 currentRbVel = rb.velocity;
#endif

        // Smooth horizontal acceleration
        float targetVx = Mathf.MoveTowards(currentRbVel.x, targetVelocity.x, acceleration * Time.fixedDeltaTime);
        float targetVz = Mathf.MoveTowards(currentRbVel.z, targetVelocity.z, acceleration * Time.fixedDeltaTime);
        float targetVy = currentRbVel.y;

        // Jump execution
        if (jumpBufferTimer > 0f && coyoteTimer > 0f)
        {
            targetVy = Mathf.Sqrt(jumpHeight * -2f * Physics.gravity.y);
            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
        }

        Vector3 newVel = new Vector3(targetVx, targetVy, targetVz);

#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = newVel;
#else
        rb.velocity = newVel;
#endif
    }

    #endregion

    #region Editor Debugging

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(GetGroundCheckPosition(), groundCheckRadius);
    }

    #endregion
}
