using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// SPRINT OR SHIFT - one GameManager per level scene.
///
/// Responsibilities:
///   - Holds the level's Start Point (the one place the spawn is set).
///   - Shows the UI: stillness warning overlay, short centre messages, victory panel.
///   - Reacts to game events: player teleported, Finish Portal reached.
///   - Loads the next level (Level1 -> Level2 -> Victory screen).
///
/// Other scripts talk to it through GameManager.Instance.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Level")]
    public string levelTitle = "LEVEL 1";
    public string levelSubtitle = "DON'T STOP. DON'T LOOK BACK.";
    [Tooltip("Scene to load after the portal. Leave EMPTY on the last level to show the Victory screen.")]
    public string nextSceneName = "Level2";
    [Tooltip("Seconds between touching the portal and loading the next level.")]
    public float transitionDelay = 2.5f;

    [Header("Scene References")]
    public FirstPersonController player;
    [Tooltip("Where the player spawns and is teleported back to.")]
    public Transform startPoint;

    [Header("UI")]
    [Tooltip("Full-screen red overlay that fades in while standing still.")]
    public CanvasGroup stillnessOverlay;
    public TMP_Text stillnessText;
    [Tooltip("Centre-screen text for level intros, 'Back to Start', 'Level Complete'.")]
    public TMP_Text messageText;
    [Tooltip("Shown after the final level. Must start disabled.")]
    public GameObject victoryPanel;

    DynamicMazeWall[] dynamicWalls;
    Coroutine messageRoutine;
    bool levelFinished;
    bool victory;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        dynamicWalls = FindObjectsByType<DynamicMazeWall>();

        if (victoryPanel != null) victoryPanel.SetActive(false);
        UpdateStillnessWarning(0f);
        ShowMessage($"{levelTitle}\n<size=45%>{levelSubtitle}</size>", 3f);
    }

    void Update()
    {
        // On the Victory screen, R restarts from Level 1 (first scene in Build Profiles).
        if (victory && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            SceneManager.LoadScene(0);
    }

    // ------------------------------------------------------------ Events

    /// <summary>Called by the player after the stillness rule sent them back to Start.</summary>
    public void OnPlayerTeleported()
    {
        // Every shifting wall goes back to how it began, so the level is fair again.
        foreach (DynamicMazeWall wall in dynamicWalls)
            wall.ResetWall();

        ShowMessage("TOO SLOW!\n<size=45%>BACK TO START</size>", 1.5f);
    }

    /// <summary>Called by the player when they touch the Finish Portal.</summary>
    public void OnPortalReached()
    {
        if (levelFinished) return;
        levelFinished = true;
        player.InputEnabled = false;
        StartCoroutine(FinishLevel());
    }

    IEnumerator FinishLevel()
    {
        bool isLastLevel = string.IsNullOrEmpty(nextSceneName);

        if (isLastLevel)
        {
            // Victory screen: free the mouse and wait for R.
            ShowMessage("", 0f);
            victoryPanel.SetActive(true);
            FirstPersonController.LockCursor(false);
            victory = true;
            yield break;
        }

        ShowMessage($"{levelTitle} COMPLETE\n<size=45%>GET READY...</size>", transitionDelay);
        yield return new WaitForSeconds(transitionDelay);
        SceneManager.LoadScene(nextSceneName);
    }

    // ------------------------------------------------------------------ UI

    /// <summary>progress: 0 = moving, 1 = about to teleport. Fades the red overlay in.</summary>
    public void UpdateStillnessWarning(float progress)
    {
        if (stillnessOverlay == null) return;

        // Ignore tiny pauses, then fade in fast so the danger is obvious.
        float alpha = Mathf.InverseLerp(0.15f, 0.6f, progress);
        stillnessOverlay.alpha = alpha;

        if (alpha > 0f && stillnessText != null)
        {
            float secondsLeft = player.stillnessLimit * (1f - progress);
            stillnessText.text = $"DON'T STOP!\n<size=50%>{secondsLeft:0.0}</size>";
        }
    }

    /// <summary>Shows centre-screen text for a few seconds (0 = clear it now).</summary>
    public void ShowMessage(string text, float duration)
    {
        if (messageText == null) return;
        if (messageRoutine != null) StopCoroutine(messageRoutine);
        messageRoutine = StartCoroutine(MessageRoutine(text, duration));
    }

    IEnumerator MessageRoutine(string text, float duration)
    {
        messageText.text = text;
        messageText.alpha = 1f;
        if (duration <= 0f) yield break;

        yield return new WaitForSeconds(duration);

        // Quick fade-out.
        for (float t = 0f; t < 0.4f; t += Time.deltaTime)
        {
            messageText.alpha = 1f - t / 0.4f;
            yield return null;
        }
        messageText.text = "";
    }
}
