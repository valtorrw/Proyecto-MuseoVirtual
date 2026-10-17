using UnityEngine;
using UnityEngine.InputSystem;

/// Control movimiento
/// Incluye: caminar, correr, gravedad, mirar con ratón/stick y bloqueo del cursor.
/// 

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Transform de la cámara (hijo del jugador, a la altura de los ojos).")]
    [SerializeField] private Transform cameraTransform;

    [Header("Movimiento")]
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float sprintSpeed = 7f;
    [SerializeField] private float acceleration = 12f;
    [SerializeField] private float gravity = -20f;

    [Header("Mirada")]
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float stickSensitivity = 150f;
    [SerializeField] private float maxLookAngle = 85f;
    [SerializeField] private bool invertY = false;

    [Header("Depuración")]
    [Tooltip("Muestra en la consola el input de movimiento y la posición.")]
    [SerializeField] private bool debugLog = false;

    private CharacterController controller;

    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private float pitch;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        controller.enabled = true;

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (cameraTransform == null)
            Debug.LogError("[FirstPersonController] No hay cámara asignada.", this);
        else if (!cameraTransform.IsChildOf(transform))
            Debug.LogWarning("[FirstPersonController] La cámara NO es hija del Player, " +
                             "por eso no se movería con él. Arrástrala dentro del Player en la jerarquía.", this);

        // Un Rigidbody no cinemático pelea contra el Character Controller e impide moverse.
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            Debug.LogWarning("[FirstPersonController] Se encontró un Rigidbody en el Player; " +
                             "se marca como cinemático. Lo ideal es quitarlo.", this);
            rb.isKinematic = true;
        }

        // Un collider extra (p. ej. el de la cápsula) en el mismo objeto bloquea al Character Controller.
        foreach (Collider c in GetComponents<Collider>())
        {
            if (c is CharacterController) continue;
            Debug.LogWarning("[FirstPersonController] Se desactivó el collider extra '" + c.GetType().Name +
                             "' del Player porque bloquea el movimiento.", this);
            c.enabled = false;
        }
    }

    private void Start()
    {
        Debug.Log("[FirstPersonController] Script activo en '" + gameObject.name + "'.", this);
        SetCursorLocked(true);
    }

    private void Update()
    {
        HandleCursor();

        // La mirada solo funciona con el cursor bloqueado; el movimiento y la gravedad siempre.
        if (Cursor.lockState == CursorLockMode.Locked)
            HandleLook();

        HandleMovement();
    }

    // ---------- Entrada ----------

    private static Vector2 ReadMoveInput()
    {
        Vector2 input = Vector2.zero;

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1f;
        }

        Gamepad pad = Gamepad.current;
        if (pad != null)
            input += pad.leftStick.ReadValue();

        return Vector2.ClampMagnitude(input, 1f);
    }

    private static bool ReadSprintInput()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.leftShiftKey.isPressed) return true;

        Gamepad pad = Gamepad.current;
        return pad != null && pad.leftStickButton.isPressed;
    }

    private static Vector2 ReadLookInput(float mouseSens, float stickSens)
    {
        Vector2 look = Vector2.zero;

        // Ratón: el delta ya es "por frame", no se multiplica por deltaTime.
        Mouse mouse = Mouse.current;
        if (mouse != null)
            look += mouse.delta.ReadValue() * mouseSens;

        // Stick: valor entre -1 y 1, sí depende del tiempo.
        Gamepad pad = Gamepad.current;
        if (pad != null)
            look += pad.rightStick.ReadValue() * (stickSens * Time.deltaTime);

        return look;
    }

    // ---------- Mirada y movimiento ----------

    private void HandleLook()
    {
        Vector2 look = ReadLookInput(mouseSensitivity, stickSensitivity);

        float yInput = invertY ? look.y : -look.y;

        pitch = Mathf.Clamp(pitch + yInput, -maxLookAngle, maxLookAngle);
        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        transform.Rotate(Vector3.up * look.x);
    }

    private void HandleMovement()
    {
        Vector2 input = ReadMoveInput();
        float targetSpeed = ReadSprintInput() ? sprintSpeed : walkSpeed;

        Vector3 desiredDirection = transform.right * input.x + transform.forward * input.y;
        desiredDirection = Vector3.ClampMagnitude(desiredDirection, 1f);

        // Aceleración suave hacia la velocidad deseada
        horizontalVelocity = Vector3.MoveTowards(
            horizontalVelocity,
            desiredDirection * targetSpeed,
            acceleration * targetSpeed * Time.deltaTime);

        // Gravedad
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f; // pequeña fuerza para mantenerse pegado al suelo

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);

        if (debugLog)
            Debug.Log($"input={input} vel={velocity} grounded={controller.isGrounded} pos={transform.position}");
    }

    // ---------- Cursor ----------

    private void HandleCursor()
    {
        // Escape libera el cursor; clic izquierdo lo vuelve a bloquear.
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            SetCursorLocked(false);
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame
                 && Cursor.lockState != CursorLockMode.Locked)
            SetCursorLocked(true);
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}