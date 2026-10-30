using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// SPRINT OR SHIFT - First-person player.
///
/// Responsibilities (and nothing else):
///   1. WASD movement on a CharacterController, with smooth acceleration.
///   2. Smooth mouse look, with the up/down angle clamped.
///   3. RULE 1 - Hesitation Penalty: standing still (speed < 0.1) for 2 seconds
///      teleports the player back to the level's Start Point.
///   4. Telling the GameManager when the Finish Portal trigger is touched.
///
/// Setup: put this on the "Player" object (it adds a CharacterController).
/// Drag the child camera into "Camera Pivot". The Start Point is read from
/// the GameManager, so there is only one place to set it.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The child camera. It pitches up/down; the Player body turns left/right.")]
    public Transform cameraPivot;

    [Header("Movement")]
    [Tooltip("Top running speed in metres per second.")]
    public float moveSpeed = 6.5f;
    [Tooltip("How quickly we reach top speed / stop. Higher = snappier.")]
    public float acceleration = 12f;
    public float gravity = -20f;

    [Header("Mouse Look")]
    [Tooltip("Degrees turned per pixel of mouse movement.")]
    public float mouseSensitivity = 0.45f;
    [Tooltip("Higher = more responsive, lower = floatier camera.")]
    public float lookSmoothing = 30f;
    [Tooltip("How far up/down the player can look.")]
    public float maxLookAngle = 85f;

    [Header("Rule 1 - Hesitation Penalty")]
    [Tooltip("Seconds of standing still before being sent back to Start.")]
    public float stillnessLimit = 2f;
    [Tooltip("Below this horizontal speed the player counts as 'still'.")]
    public float stillSpeedThreshold = 0.1f;

    /// <summary>0 = moving, 1 = about to be teleported. Drives the UI warning.</summary>
    public float StillnessProgress => Mathf.Clamp01(stillTimer / stillnessLimit);

    /// <summary>The GameManager switches this off during level transitions.</summary>
    public bool InputEnabled { get; set; } = true;

    CharacterController controller;
    Vector3 horizontalVelocity;   // smoothed WASD velocity
    float verticalVelocity;       // gravity
    float yaw, pitch;             // current look angles
    Vector2 smoothedLookDelta;    // smoothed mouse input
    float stillTimer;
    bool hasMovedSinceSpawn;      // no penalty while waiting at Start before the first step

    void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    void Start()
    {
        // Begin the level standing on the Start Point (no warning message).
        MoveToStart();
        LockCursor(true);
    }

    void Update()
    {
        if (!InputEnabled)
        {
            stillTimer = 0f;
            GameManager.Instance?.UpdateStillnessWarning(0f);
            return;
        }

        HandleCursor();
        HandleLook();
        HandleMovement();
        HandleStillness();
    }

    // ------------------------------------------------------------------ Look

    void HandleLook()
    {
        if (Mouse.current == null || Cursor.lockState != CursorLockMode.Locked) return;

        // Mouse delta is already "per frame", so it is NOT multiplied by deltaTime.
        Vector2 rawDelta = Mouse.current.delta.ReadValue() * mouseSensitivity;
        smoothedLookDelta = Vector2.Lerp(smoothedLookDelta, rawDelta, 1f - Mathf.Exp(-lookSmoothing * Time.deltaTime));

        yaw += smoothedLookDelta.x;
        pitch = Mathf.Clamp(pitch - smoothedLookDelta.y, -maxLookAngle, maxLookAngle);

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);           // body turns left/right
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);  // camera tilts up/down
    }

    // -------------------------------------------------------------- Movement

    void HandleMovement()
    {
        Vector2 input = ReadMoveInput();
        Vector3 targetVelocity = (transform.right * input.x + transform.forward * input.y) * moveSpeed;

        // Ease towards the target speed so starting/stopping feels smooth, not robotic.
        horizontalVelocity = Vector3.Lerp(horizontalVelocity, targetVelocity, 1f - Mathf.Exp(-acceleration * Time.deltaTime));

        // Keep the player glued to the floor, otherwise apply gravity.
        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 motion = horizontalVelocity + Vector3.up * verticalVelocity;
        controller.Move(motion * Time.deltaTime);
    }

    static Vector2 ReadMoveInput()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return Vector2.zero;

        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1f;
        return Vector2.ClampMagnitude(input, 1f); // diagonals are not faster
    }

    // ----------------------------------------------- Rule 1: Hesitation Penalty

    void HandleStillness()
    {
        // Use the speed the controller ACTUALLY moved at. Pushing into a wall
        // (e.g. boxed in at a dead end) counts as standing still - that is Rule 3.
        Vector3 v = controller.velocity;
        float horizontalSpeed = new Vector2(v.x, v.z).magnitude;

        if (horizontalSpeed >= stillSpeedThreshold)
        {
            hasMovedSinceSpawn = true;
            stillTimer = 0f;
        }
        else if (hasMovedSinceSpawn)
        {
            stillTimer += Time.deltaTime;
        }

        GameManager.Instance?.UpdateStillnessWarning(StillnessProgress);

        if (stillTimer >= stillnessLimit)
            TeleportToStart();
    }

    /// <summary>
    /// Sends the player back to the Start Point and shows the UI warning.
    /// Called by the stillness rule, but public so anything else can use it too.
    /// </summary>
    public void TeleportToStart()
    {
        MoveToStart();
        GameManager.Instance?.OnPlayerTeleported();
    }

    void MoveToStart()
    {
        Transform start = GameManager.Instance != null ? GameManager.Instance.startPoint : null;
        if (start == null) return;

        // A CharacterController overrides transform changes while enabled,
        // so switch it off for the instant we move the player.
        controller.enabled = false;
        transform.SetPositionAndRotation(start.position, Quaternion.Euler(0f, start.eulerAngles.y, 0f));
        controller.enabled = true;

        yaw = start.eulerAngles.y;
        pitch = 0f;
        cameraPivot.localRotation = Quaternion.identity;
        horizontalVelocity = Vector3.zero;
        verticalVelocity = 0f;
        smoothedLookDelta = Vector2.zero;
        stillTimer = 0f;
        hasMovedSinceSpawn = false;
    }

    // ------------------------------------------------------------ Win trigger

    void OnTriggerEnter(Collider other)
    {
        // The Finish Portal uses Unity's built-in "Finish" tag.
        if (other.CompareTag("Finish"))
            GameManager.Instance?.OnPortalReached();
    }

    // ---------------------------------------------------------------- Cursor

    void HandleCursor()
    {
        // Esc frees the mouse (handy in the Editor); clicking the game grabs it again.
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) LockCursor(false);
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) LockCursor(true);
    }

    public static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
