using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// EDITOR-ONLY helper for the course submission.
///   Sprint or Shift > Capture Submission Screenshots  - renders in-game shots + top-down maps
///   Sprint or Shift > Build macOS Player               - builds a playable .app
/// Output goes to the "Submission" folder next to the project. Scenes are never saved.
/// </summary>
public static class SubmissionTools
{
    static string SubmissionDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Submission"));

    [MenuItem("Sprint or Shift/Capture Submission Screenshots")]
    public static void CaptureScreenshots()
    {
        string dir = Path.Combine(SubmissionDir, "Screenshots");
        Directory.CreateDirectory(dir);

        // ---- Level 1 (11 x 11 grid, 4 m cells)
        Open("Level1");
        Shot(dir, "01_level1_start", new Vector3(4, 0, 4), 0, ui: "intro");
        Shot(dir, "02_level1_stillness_warning", new Vector3(12, 0, 12), 90, ui: "stillness");
        Shot(dir, "03_level1_dont_look_back_sign", new Vector3(36, 0, 12), 0);
        Shot(dir, "04_level1_gate_closed_behind", new Vector3(16, 0, 20), 90, closeGate: "Gate_5_8");
        Shot(dir, "05_level1_finish_portal", new Vector3(16, 0, 36), -90);
        Map(dir, "10_level1_topdown", 11, 11);

        // ---- Level 2 (17 x 13 grid)
        Open("Level2");
        Shot(dir, "06_level2_start", new Vector3(4, 0, 4), 90, ui: "intro");
        Shot(dir, "07_level2_dead_end_trick", new Vector3(20, 0, 12), 90);
        Shot(dir, "08_level2_trust_nothing", new Vector3(36, 0, 44), 180);
        Shot(dir, "09_victory_screen", new Vector3(16, 0, 48), -90, ui: "victory");
        Map(dir, "11_level2_topdown", 17, 13);

        Debug.Log($"[Submission] Screenshots saved to {dir}");
    }

    [MenuItem("Sprint or Shift/Build macOS Player")]
    public static void BuildMac()
    {
        string path = Path.Combine(SubmissionDir, "Build/SprintOrShift.app");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Level1.unity", "Assets/Scenes/Level2.unity" },
            locationPathName = path,
            target = BuildTarget.StandaloneOSX,
        });
        Debug.Log($"[Submission] Build {report.summary.result} -> {path}");
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }

    // ------------------------------------------------------------------

    static void Open(string scene) => EditorSceneManager.OpenScene($"Assets/Scenes/{scene}.unity");

    /// <summary>Renders the player camera from a pose, optionally with a UI state shown.</summary>
    static void Shot(string dir, string name, Vector3 pos, float yaw, string ui = null, string closeGate = null)
    {
        // Show dynamic walls as they look at level start (edit mode never ran Awake).
        foreach (var w in Object.FindObjectsByType<DynamicMazeWall>())
            w.GetComponent<Renderer>().enabled = w.startsClosed || w.name == closeGate;

        var gm = Object.FindAnyObjectByType<GameManager>();
        var player = gm.player.transform;
        player.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        var cam = player.GetComponentInChildren<Camera>();

        // Overlay canvases are not drawn by Camera.Render, so attach the UI to the camera.
        var canvas = Object.FindAnyObjectByType<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 0.5f;
        gm.stillnessOverlay.alpha = ui == "stillness" ? 0.85f : 0f;
        gm.stillnessText.text = "DON'T STOP!\n<size=50%>0.6</size>";
        gm.messageText.text = ui == "intro" ? $"{gm.levelTitle}\n<size=45%>{gm.levelSubtitle}</size>" : "";
        gm.victoryPanel.SetActive(ui == "victory");

        Render(cam, Path.Combine(dir, name + ".png"), 1600, 900);
    }

    /// <summary>Straight-down orthographic view of the whole maze (all purple walls drawn).</summary>
    static void Map(string dir, string name, int cols, int rows)
    {
        foreach (var w in Object.FindObjectsByType<DynamicMazeWall>())
            w.GetComponent<Renderer>().enabled = true;
        Object.FindAnyObjectByType<Canvas>().gameObject.SetActive(false);
        RenderSettings.fog = false;

        var go = new GameObject("MapCamera");
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = rows * 4f / 2f + 1f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.02f, 0.05f);
        go.transform.SetPositionAndRotation(new Vector3((cols - 1) * 2f, 40f, (rows - 1) * 2f), Quaternion.Euler(90, 0, 0));

        float aspect = (cols * 4f + 2f) / (rows * 4f + 2f);
        Render(cam, Path.Combine(dir, name + ".png"), Mathf.RoundToInt(1000 * aspect), 1000);
        Object.DestroyImmediate(go);
    }

    static void Render(Camera cam, string file, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.aspect = (float)w / h;
        Canvas.ForceUpdateCanvases();
        foreach (var t in Object.FindObjectsByType<TMP_Text>()) t.ForceMeshUpdate();
        cam.Render();
        cam.Render(); // second pass lets TMP / post-processing settle

        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());

        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }
}
