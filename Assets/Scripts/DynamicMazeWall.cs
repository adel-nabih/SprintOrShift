using UnityEngine;

/// <summary>
/// SPRINT OR SHIFT - RULE 2: "Don't Look Back".
///
/// A purple maze wall that only ever changes BEHIND the player's back.
/// The rule, in plain words:
///   1. The player must actually SEE the wall: it is on screen AND nothing blocks
///      the line of sight (a wall hidden behind other walls does not count).
///   2. Later, once the wall is BEHIND the player (and off screen), it shifts.
///   3. After a shift it must be seen again before it can shift again.
///
/// Two behaviours, picked in the Inspector:
///   CloseBehindPlayer - a one-way gate. Starts open; once the player has walked
///                       through it (in the direction of the blue Z arrow) and it is
///                       behind them, it slams shut so they cannot retrace their steps.
///   Toggle            - flips open/closed each time it ends up behind the player.
///                       Used for morphing corridors and dead-end traps.
///
/// Why not OnBecameInvisible()? It also counts the Scene-view camera in the Editor,
/// stops firing once the Renderer is hidden, and ignores what is in the way.
/// Checking the player camera directly is the same idea, but predictable.
///
/// Setup: put this on a Cube that sits in a corridor (needs a Renderer + Collider).
/// </summary>
[RequireComponent(typeof(Renderer), typeof(Collider))]
public class DynamicMazeWall : MonoBehaviour
{
    public enum ShiftMode { CloseBehindPlayer, Toggle }

    [Tooltip("CloseBehindPlayer = one-way gate. Toggle = flips every time it ends up behind the player.")]
    public ShiftMode mode = ShiftMode.CloseBehindPlayer;

    [Tooltip("Is the wall solid when the level starts (and after a reset)?")]
    public bool startsClosed = false;

    [Tooltip("Never shift while the player is closer than this to the wall (stops it trapping them inside it).")]
    public float minPlayerDistance = 1.5f;

    [Tooltip("CloseBehindPlayer only: how far past the wall's centre the player must be before it can close.")]
    public float passDistance = 3f;

    [Tooltip("How far round (degrees from where the player looks) the wall must be to count as 'behind'.")]
    public float behindAngle = 110f;

    [Tooltip("Minimum seconds between two shifts. Lower = more chaos.")]
    public float shiftCooldown = 1.5f;

    /// <summary>True while the wall blocks the corridor.</summary>
    public bool IsClosed { get; private set; }

    static readonly Plane[] frustumPlanes = new Plane[6];

    Renderer wallRenderer;
    Collider wallCollider;
    Bounds wallBounds;      // cached once, because a hidden Renderer reports empty bounds
    Camera playerCamera;
    Transform player;
    bool hasBeenSeen;
    float lastShiftTime;

    void Awake()
    {
        wallRenderer = GetComponent<Renderer>();
        wallCollider = GetComponent<Collider>();
        wallBounds = wallRenderer.bounds;
        SetClosed(startsClosed);
    }

    void Start()
    {
        FirstPersonController fpc = FindAnyObjectByType<FirstPersonController>();
        if (fpc != null)
        {
            player = fpc.transform;
            playerCamera = fpc.GetComponentInChildren<Camera>();
        }
    }

    void Update()
    {
        if (playerCamera == null) return;

        // 1) Can the player actually see us right now?
        if (CanPlayerSee())
        {
            hasBeenSeen = true;
            return;
        }

        // 2) Only shift once the player saw us AND we are now behind them,
        //    the cooldown has passed, and they are not standing right next to us.
        if (!hasBeenSeen || !IsBehindPlayer()) return;
        if (Time.time - lastShiftTime < shiftCooldown) return;
        if (wallBounds.SqrDistance(player.position) < minPlayerDistance * minPlayerDistance) return;

        // 3) Shift!
        if (mode == ShiftMode.Toggle)
        {
            Shift(!IsClosed);
        }
        else if (!IsClosed && PlayerHasPassedThrough())
        {
            Shift(true);
        }
    }

    /// <summary>On screen, and no other wall blocks the line from the camera to us.</summary>
    bool CanPlayerSee()
    {
        GeometryUtility.CalculateFrustumPlanes(playerCamera, frustumPlanes);
        if (!GeometryUtility.TestPlanesAABB(frustumPlanes, wallBounds)) return false;

        bool blocked = Physics.Linecast(playerCamera.transform.position, wallBounds.center,
            out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        return !blocked || hit.collider == wallCollider;
    }

    /// <summary>The wall is roughly behind where the player is looking (ignoring up/down).</summary>
    bool IsBehindPlayer()
    {
        Vector3 look = playerCamera.transform.forward;
        Vector3 toWall = wallBounds.center - playerCamera.transform.position;
        look.y = 0f;
        toWall.y = 0f;
        return Vector3.Angle(look, toWall) > behindAngle;
    }

    /// <summary>The player is on the far side of the gate (along its blue Z arrow).</summary>
    bool PlayerHasPassedThrough()
    {
        return Vector3.Dot(player.position - transform.position, transform.forward) > passDistance;
    }

    void Shift(bool closed)
    {
        SetClosed(closed);
        hasBeenSeen = false;
        lastShiftTime = Time.time;
    }

    /// <summary>Show/hide the wall and turn its collision on/off together.</summary>
    void SetClosed(bool closed)
    {
        IsClosed = closed;
        wallRenderer.enabled = closed;
        wallCollider.enabled = closed;
    }

    /// <summary>Put the wall back to its level-start state (called when the player is sent to Start).</summary>
    public void ResetWall()
    {
        SetClosed(startsClosed);
        hasBeenSeen = false;
        lastShiftTime = Time.time;
    }

    // Editor helper (only when the wall is selected, so it never clutters the Game view):
    // draws the wall's box, plus the travel direction for one-way gates.
    void OnDrawGizmosSelected()
    {
        Gizmos.color = mode == ShiftMode.Toggle ? Color.magenta : Color.cyan;
        Gizmos.DrawWireCube(transform.position, transform.lossyScale);
        if (mode == ShiftMode.CloseBehindPlayer)
            Gizmos.DrawRay(transform.position, transform.forward * passDistance);
    }
}
