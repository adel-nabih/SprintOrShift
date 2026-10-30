using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// EDITOR-ONLY helper: builds Level1 and Level2 from the text layouts below.
/// Menu: Sprint or Shift > Build Both Levels
///
/// Everything it creates is plain primitives + the three game scripts, so the
/// scenes can also be edited by hand afterwards. Re-running it overwrites both scenes.
///
/// Layout legend (row 0 = north / far end of the maze):
///   #  wall            .  floor
///   S  start point     F  finish portal
///   > < ^ v  one-way gate (DynamicMazeWall, CloseBehindPlayer) - arrow = travel direction
///   T  Toggle wall that starts CLOSED     t  Toggle wall that starts OPEN
///   1-9  wall with a glowing sign (text + facing set in the sign list)
/// </summary>
public static class SprintOrShiftLevelBuilder
{
    const float Cell = 4f;          // metres per grid square
    const float WallHeight = 3.5f;

    // ------------------------------------------------------------- LEVEL DATA

    static readonly string[] Level1Layout =
    {
        "#####3#####",
        "#F....#####",
        "#####^#####",
        "#.....#####",
        "#.#######2#",
        "#.......<.#",
        "#1#######.#",
        "#.........#",
        "#.#########",
        "#S#########",
        "###########",
    };

    static readonly Dictionary<char, (string text, char facing, Color color)> Level1Signs = new()
    {
        { '1', ("DON'T STOP", 'S', Neon.Yellow) },
        { '2', ("DON'T LOOK BACK", 'S', Neon.Red) },
        { '3', ("KEEP MOVING", 'S', Neon.Cyan) },
    };

    static readonly string[] Level2Layout =
    {
        "#######4#########",
        "#...###F#...<...#",
        "#.#.###^#.#####.#",
        "#.#.###.#.#.....#",
        "#.#####.#.#.#.#.#",
        "#.....#.<.t.#.#.#",
        "#####.###5#.###.#",
        "#####.....t.....#",
        "#####.#####.#.###",
        "#####.....3^#...#",
        "#####.2####.#####",
        "#S..>.......1####",
        "#################",
    };

    static readonly Dictionary<char, (string text, char facing, Color color)> Level2Signs = new()
    {
        { '1', ("DON'T LOOK BACK", 'W', Neon.Red) },
        { '2', ("KEEP MOVING", 'S', Neon.Cyan) },
        { '3', ("DEAD END", 'W', Neon.Red) },
        { '4', ("EXIT", 'S', Neon.Cyan) },
        { '5', ("TRUST NOTHING", 'N', Neon.Yellow) },
    };

    static class Neon
    {
        public static readonly Color Yellow = new(1f, 0.85f, 0.15f);
        public static readonly Color Red = new(1f, 0.15f, 0.3f);
        public static readonly Color Cyan = new(0.2f, 0.95f, 1f);
        public static readonly Color Green = new(0.25f, 1f, 0.4f);
    }

    // ----------------------------------------------------------------- ENTRY

    [MenuItem("Sprint or Shift/Build Both Levels")]
    public static void BuildAllLevels()
    {
        EnsureFolder("Assets/Materials");
        EnsureFolder("Assets/Scenes");

        var level1 = BuildLevel("Level1", Level1Layout, Level1Signs,
            "LEVEL 1", "DON'T STOP. DON'T LOOK BACK.", "Level2", toggleCooldown: 2f, fogDensity: 0.022f);
        var level2 = BuildLevel("Level2", Level2Layout, Level2Signs,
            "LEVEL 2", "HIGH CHAOS", "", toggleCooldown: 0.6f, fogDensity: 0.032f);

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(level1, true),
            new EditorBuildSettingsScene(level2, true),
        };

        EditorSceneManager.OpenScene(level1);
        Debug.Log("[Sprint or Shift] Built Level1 + Level2 and added them to the build scene list.");
    }

    // ------------------------------------------------------------ SCENE BUILD

    static string BuildLevel(string sceneName, string[] layout,
        Dictionary<char, (string text, char facing, Color color)> signs,
        string title, string subtitle, string nextScene, float toggleCooldown, float fogDensity)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        int rows = layout.Length, cols = layout[0].Length;

        // --- Lighting & atmosphere: dark, foggy, flat cartoon ambient.
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.34f, 0.3f, 0.5f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.02f, 0.02f, 0.05f);
        RenderSettings.fogDensity = fogDensity;

        var sun = new GameObject("Directional Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 0.7f;
        sun.color = new Color(0.65f, 0.7f, 1f);
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

        var volume = new GameObject("Global Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = GetOrCreateVolumeProfile();

        // --- Root groups keep the Hierarchy tidy.
        var gameManagerGO = new GameObject("GameManager");
        var environment = new GameObject("Environment").transform;
        var maze = new GameObject("Maze").transform;
        var staticWalls = new GameObject("Walls").transform; staticWalls.SetParent(maze);
        var dynamicWalls = new GameObject("DynamicWalls").transform; dynamicWalls.SetParent(maze);
        var signRoot = new GameObject("Signs").transform; signRoot.SetParent(maze);

        // --- Floor covering the whole grid (top surface at y = 0).
        var floor = CreateCube("Floor", environment,
            new Vector3((cols - 1) * Cell / 2f, -0.25f, (rows - 1) * Cell / 2f),
            new Vector3(cols * Cell, 0.5f, rows * Cell), Mat("Floor", new Color(0.035f, 0.035f, 0.06f)));
        floor.isStatic = true;

        Transform startPoint = null;
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            char ch = layout[r][c];
            Vector3 pos = CellToWorld(r, c, rows);

            if (ch == '#' || char.IsDigit(ch))
            {
                var wall = CreateCube($"Wall_{r}_{c}", staticWalls, pos + Vector3.up * WallHeight / 2f,
                    new Vector3(Cell, WallHeight, Cell), Mat("Wall", new Color(0.26f, 0.22f, 0.52f)));
                wall.isStatic = true;

                if (char.IsDigit(ch) && signs.TryGetValue(ch, out var sign))
                    CreateSign(signRoot, pos, FacingToDir(sign.facing), sign.text, sign.color);
            }
            else if (ch == 'S')
            {
                startPoint = CreateStart(environment, layout, r, c, rows, pos);
            }
            else if (ch == 'F')
            {
                CreatePortal(layout, r, c, rows, pos);
            }
            else if ("><^v".IndexOf(ch) >= 0)
            {
                CreateDynamicWall($"Gate_{r}_{c}", dynamicWalls, pos, ArrowToDir(ch),
                    DynamicMazeWall.ShiftMode.CloseBehindPlayer, false, 0f);
            }
            else if (ch == 'T' || ch == 't')
            {
                CreateDynamicWall($"ShiftWall_{r}_{c}", dynamicWalls, pos, Vector3.forward,
                    DynamicMazeWall.ShiftMode.Toggle, ch == 'T', toggleCooldown);
            }
        }

        // --- Player: CharacterController body + camera child + flashlight.
        var player = new GameObject("Player");
        player.transform.SetPositionAndRotation(startPoint.position, startPoint.rotation);
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = 0.4f; cc.center = new Vector3(0f, 0.9f, 0f);
        var fpc = player.AddComponent<FirstPersonController>();

        var camGO = new GameObject("PlayerCamera");
        camGO.tag = "MainCamera";
        camGO.transform.SetParent(player.transform, false);
        camGO.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = RenderSettings.fogColor;
        cam.fieldOfView = 75f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 120f;
        camGO.AddComponent<AudioListener>();
        camGO.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        fpc.cameraPivot = camGO.transform;

        var flashlight = new GameObject("Flashlight").AddComponent<Light>();
        flashlight.transform.SetParent(camGO.transform, false);
        flashlight.type = LightType.Spot;
        flashlight.range = 22f; flashlight.spotAngle = 75f; flashlight.intensity = 12f;
        flashlight.color = new Color(1f, 0.95f, 0.85f);

        // --- UI + GameManager wiring.
        var gm = gameManagerGO.AddComponent<GameManager>();
        gm.levelTitle = title;
        gm.levelSubtitle = subtitle;
        gm.nextSceneName = nextScene;
        gm.player = fpc;
        gm.startPoint = startPoint;
        BuildUI(gm);

        string path = $"Assets/Scenes/{sceneName}.unity";
        EditorSceneManager.SaveScene(scene, path);
        return path;
    }

    // ---------------------------------------------------------- MAZE PIECES

    static Transform CreateStart(Transform parent, string[] layout, int r, int c, int rows, Vector3 pos)
    {
        Vector3 forward = FirstOpenDirection(layout, r, c);
        var start = new GameObject("StartPoint").transform;
        start.SetParent(parent);
        start.SetPositionAndRotation(pos, Quaternion.LookRotation(forward));

        CreateCube("StartPad", start, pos + Vector3.up * 0.02f, new Vector3(Cell * 0.8f, 0.05f, Cell * 0.8f),
            Mat("StartPad", Neon.Green * 0.4f, Neon.Green * 2.5f));

        // "START" sign on the wall behind the spawn, facing the way the player runs.
        CreateSign(start, pos - forward * Cell, forward, "START", Neon.Green);
        return start;
    }

    static void CreatePortal(string[] layout, int r, int c, int rows, Vector3 pos)
    {
        Vector3 approach = FirstOpenDirection(layout, r, c);
        var portal = CreateCube("FinishPortal", null, pos + Vector3.up * 1.7f, new Vector3(Cell * 0.7f, 3.2f, 0.4f),
            Mat("Portal", new Color(0.1f, 0.8f, 1f), new Color(0.2f, 1.6f, 3f) * 2f));
        portal.transform.rotation = Quaternion.LookRotation(approach);
        portal.tag = "Finish";

        // A deep trigger box so running into the portal always counts.
        var box = portal.GetComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(1f, 1f, Cell / 0.4f);

        var glow = new GameObject("PortalLight").AddComponent<Light>();
        glow.transform.SetParent(portal.transform, false);
        glow.type = LightType.Point;
        glow.color = Neon.Cyan;
        glow.range = 10f;
        glow.intensity = 6f;
    }

    static void CreateDynamicWall(string name, Transform parent, Vector3 pos, Vector3 forward,
        DynamicMazeWall.ShiftMode mode, bool startsClosed, float cooldown)
    {
        var wall = CreateCube(name, parent, pos + Vector3.up * WallHeight / 2f,
            new Vector3(Cell, WallHeight, Cell),
            Mat("DynamicWall", new Color(0.3f, 0.12f, 0.32f), new Color(0.25f, 0.02f, 0.15f)));
        wall.transform.rotation = Quaternion.LookRotation(forward);

        var shifter = wall.AddComponent<DynamicMazeWall>();
        shifter.mode = mode;
        shifter.startsClosed = startsClosed;
        shifter.shiftCooldown = cooldown;
    }

    /// <summary>A glowing neon frame + dark plate + TextMeshPro text, stuck to a wall face.</summary>
    static void CreateSign(Transform parent, Vector3 wallCellPos, Vector3 facing, string text, Color color)
    {
        var sign = new GameObject($"Sign_{text}").transform;
        sign.SetParent(parent);
        sign.position = wallCellPos + facing * (Cell / 2f) + Vector3.up * 2.1f;
        sign.rotation = Quaternion.LookRotation(-facing); // TMP text reads from its -Z side

        var frame = CreateCube("Frame", sign, Vector3.zero, Vector3.one, Mat($"Neon_{ColorUtility.ToHtmlStringRGB(color)}", color * 0.5f, color * 3f));
        frame.transform.localPosition = new Vector3(0f, 0f, -0.02f);
        frame.transform.localScale = new Vector3(3.5f, 1.25f, 0.04f);
        Object.DestroyImmediate(frame.GetComponent<Collider>());

        var plate = CreateCube("Plate", sign, Vector3.zero, Vector3.one, Mat("SignPlate", new Color(0.01f, 0.01f, 0.02f)));
        plate.transform.localPosition = new Vector3(0f, 0f, -0.05f);
        plate.transform.localScale = new Vector3(3.2f, 0.9f, 0.04f);
        Object.DestroyImmediate(plate.GetComponent<Collider>());

        var tmp = new GameObject("Text").AddComponent<TextMeshPro>();
        tmp.transform.SetParent(sign, false);
        tmp.transform.localPosition = new Vector3(0f, 0f, -0.08f);
        tmp.rectTransform.sizeDelta = new Vector2(3f, 0.8f);
        tmp.text = text;
        tmp.color = Color.Lerp(color, Color.white, 0.35f);
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 1f;
        tmp.fontSizeMax = 7f;
    }

    // -------------------------------------------------------------------- UI

    static void BuildUI(GameManager gm)
    {
        var canvasGO = new GameObject("UI");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Crosshair dot.
        var crosshair = CreateUIImage("Crosshair", canvasGO.transform, new Color(1f, 1f, 1f, 0.7f));
        var chRect = crosshair.rectTransform;
        chRect.anchorMin = chRect.anchorMax = new Vector2(0.5f, 0.5f);
        chRect.sizeDelta = new Vector2(6f, 6f);

        // Stillness warning: red full-screen overlay + countdown text.
        var overlay = CreateUIImage("StillnessOverlay", canvasGO.transform, new Color(0.7f, 0f, 0.08f, 0.45f));
        Stretch(overlay.rectTransform);
        overlay.raycastTarget = false;
        var group = overlay.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        var warning = CreateUIText("WarningText", overlay.transform, "DON'T STOP!", 150f, Color.white);
        Stretch(warning.rectTransform);

        // Centre messages (level intro, back to start, level complete).
        var message = CreateUIText("MessageText", canvasGO.transform, "", 110f, Neon.Yellow);
        Stretch(message.rectTransform);
        message.rectTransform.anchorMin = new Vector2(0f, 0.55f);

        // Victory panel (hidden until the last portal).
        var victory = CreateUIImage("VictoryPanel", canvasGO.transform, new Color(0.01f, 0.01f, 0.03f, 0.92f));
        Stretch(victory.rectTransform);
        var victoryText = CreateUIText("VictoryText", victory.transform,
            "YOU ESCAPED!\n<size=40%>You never stopped. You never looked back.\n\nPress R to play again</size>", 140f, Neon.Cyan);
        Stretch(victoryText.rectTransform);
        victory.gameObject.SetActive(false);

        gm.stillnessOverlay = group;
        gm.stillnessText = warning;
        gm.messageText = message;
        gm.victoryPanel = victory.gameObject;
    }

    static Image CreateUIImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    static TextMeshProUGUI CreateUIText(string name, Transform parent, string text, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // --------------------------------------------------------------- HELPERS

    static Vector3 CellToWorld(int r, int c, int rows) => new(c * Cell, 0f, (rows - 1 - r) * Cell);

    static Vector3 FacingToDir(char f) => f switch
    {
        'N' => Vector3.forward, 'S' => Vector3.back, 'E' => Vector3.right, _ => Vector3.left,
    };

    static Vector3 ArrowToDir(char a) => a switch
    {
        '^' => Vector3.forward, 'v' => Vector3.back, '>' => Vector3.right, _ => Vector3.left,
    };

    /// <summary>Direction (N, E, S, W order) of the first walkable neighbour cell.</summary>
    static Vector3 FirstOpenDirection(string[] layout, int r, int c)
    {
        (int dr, int dc, Vector3 dir)[] options =
        {
            (-1, 0, Vector3.forward), (0, 1, Vector3.right), (1, 0, Vector3.back), (0, -1, Vector3.left),
        };
        foreach (var o in options)
        {
            char n = layout[r + o.dr][c + o.dc];
            if (n != '#' && !char.IsDigit(n)) return o.dir;
        }
        return Vector3.forward;
    }

    static GameObject CreateCube(string name, Transform parent, Vector3 position, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    /// <summary>Loads (or creates) a URP Lit material in Assets/Materials. Emission is optional.</summary>
    static Material Mat(string name, Color baseColor, Color? emission = null)
    {
        string path = $"Assets/Materials/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", baseColor);
        mat.SetFloat("_Smoothness", 0.05f);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            // URP keeps the _EMISSION keyword only if the material is flagged as emissive.
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", emission.Value);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>Bloom makes emissive signs / portal glow; vignette darkens the edges.</summary>
    static VolumeProfile GetOrCreateVolumeProfile()
    {
        const string path = "Assets/Settings/SprintOrShiftVolume.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile != null) return profile;

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);

        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(0.9f);
        bloom.intensity.Override(1.4f);
        bloom.scatter.Override(0.7f);

        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.38f);

        var tonemap = profile.Add<Tonemapping>(true);
        tonemap.mode.Override(TonemappingMode.Neutral);

        foreach (var component in profile.components)
            AssetDatabase.AddObjectToAsset(component, profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
