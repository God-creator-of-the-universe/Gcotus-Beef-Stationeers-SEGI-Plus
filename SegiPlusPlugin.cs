using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace BeefsSEGIPlus
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class SEGIPlugin : BaseUnityPlugin
    {
        public static SEGIPlugin Instance;
        public static ManualLogSource Log;

        private float popupDelay = 1.5f;
        // public static ConfigEntry<bool> Update1_2_0_Popup;

        private class UpdatePopupItem
        {
            public string Key;
            public string Title;
            public string Changelog;
            public ConfigEntry<bool> Config;
        }

        private readonly List<UpdatePopupItem> allPopups = new List<UpdatePopupItem>();
        private Queue<UpdatePopupItem> popupQueue;
        private UpdatePopupItem currentPopup;

        private Rect popupRect;
        private Vector2 scrollPos;
        private bool showPopup = false;
        private bool pendingShow = false;
        private float guiScale = 1.0f;
        private int lastScreenHeight = 0;
        private int lastScreenWidth = 0;
        private bool popupRectInitialized = false;

        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<KeyboardShortcut> ToggleEnabledHotkey;
        public static ConfigEntry<int> QualityLevel;
        public static ConfigEntry<float> SecondaryBounceGain;
        public static ConfigEntry<float> EmissiveLightGain;
        public static ConfigEntry<float> GIRedGain;
        public static ConfigEntry<float> GIGreenGain;
        public static ConfigEntry<float> GIBlueGain;
        public static ConfigEntry<float> GIGain;
        public static ConfigEntry<float> GIGamma;
        public static ConfigEntry<float> GIHighlightGain;
        public static ConfigEntry<float> GIToe;
        public static ConfigEntry<float> BloomBlur;
        public static ConfigEntry<float> BloomGain;
        public static ConfigEntry<float> BloomGamma;
        public static ConfigEntry<float> BloomMix;
        public static ConfigEntry<float> ScreenGamma;
        public static ConfigEntry<float> DitherStrength;
        public static ConfigEntry<bool> LightweightMode;
        public static ConfigEntry<int> TargetFramerate;
        public static ConfigEntry<bool> AdaptivePerformance;
        public static ConfigEntry<int> AdaptiveStrategy;
        public static ConfigEntry<float> AdaptiveMinDistancePercent;
        public static ConfigEntry<bool> UseGainMultiplier;
        public static ConfigEntry<bool> EmissiveBubbleEnabled;
        public static ConfigEntry<bool> DenseVoxelMode;
        public static ConfigEntry<int> HighDensityRangeStep;
        public static ConfigEntry<bool> ForwardOriginBias;
        public static ConfigEntry<float> OcclusionStrengthOffset;
        public static ConfigEntry<float> ConeTraceBiasOffset;
        public static ConfigEntry<int> InnerOcclusionLayers;
        public static ConfigEntry<float> FlashlightFillPower;
        public static ConfigEntry<float> FlashlightFillRange;
        public static ConfigEntry<float> LampFillPower;
        public static ConfigEntry<float> LampFillRange;
        public static ConfigEntry<bool> LampFillOrthogonal;
        public static ConfigEntry<bool> LampFillDiagonal;
        public static ConfigEntry<bool> EyeAdaptationEnabled;
        public static ConfigEntry<float> EyeAdaptationModifierAtMinBrightness;
        public static ConfigEntry<float> EyeAdaptationModifierAtMaxBrightness;
        public static ConfigEntry<float> EyeAdaptationFadeTimeAtMinBrightness;
        public static ConfigEntry<float> EyeAdaptationFadeTimeAtMaxBrightness;
        public static ConfigEntry<float> EyeAdaptationSensitivity;
        public static ConfigEntry<float> EyeAdaptationScreenGammaModifierAtMinBrightness;
        public static ConfigEntry<float> EyeAdaptationScreenGammaModifierAtMaxBrightness;

        private static SEGIStationeers SegiStationeersInstance { get; set; }

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            BindAllConfigs();
            new Harmony(PluginInfo.PLUGIN_GUID).PatchAll();
            Log.LogInfo($"Plugin {PluginInfo.PLUGIN_NAME} is loaded!");

            // Update1_2_0_Popup = AddUpdatePopup(
            //     "Update1_2_0_Popup",
            //     "SEGI Plus was Updated to v1.2.0!",
            //     "Changelog v1.2.0:\n " +
            //     "- Added first pass of adaptive framerate control that works with the quality setting to try and improve performance\n" +
            //     "- This can be used at any quality setting and with or without lightweight mode\n\n" +
            //     "## IMPORTANT ##\n" +
            //     "- This isn't automatically enabled as it's yet experimental - you can enable this in settings\n\n" +
            //     "Press F11 in-game to access the configuration menu or use the workshop button on the left and click on the mod to adjust settings!\n\n" +
            //     "Changelog v1.2.1:\n" +
            //     "- Widened adaptive framerate slider choices\n" +
            //     "- Automatically remove/mark read the major update popup if go into world\n" +
            //     "- Added an x10 multiplier option if you want to play around with silly gain values\n" +
            //     "- Darkened background of F11 menu slightly",
            //     defaultSeen: false);

            popupQueue = new Queue<UpdatePopupItem>();
            foreach (var p in allPopups)
            {
                if (!p.Config.Value)
                {
                    popupQueue.Enqueue(p);
                }
            }

            pendingShow = popupQueue.Count > 0;
        }



        private void Start()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded; ;
            StartCoroutine(InitializeSEGICoroutine());
            gameObject.AddComponent<ConfigMenu>();
            gameObject.AddComponent<FlashlightFillLight>();
            gameObject.AddComponent<LampFillLightManager>();
        }

        private void Update()
        {
            if (SegiStationeersInstance != null)
            {
                SegiStationeersInstance.bypassRendering = !Enabled.Value;
                if (SegiStationeersInstance.sun == null)
                {
                    try
                    {
                        Light worldSun = WorldManager.Instance?.WorldSun?.TargetLight;
                        if (worldSun != null)
                        {
                            SegiStationeersInstance.sun = worldSun;
                            Log.LogInfo($"SEGI Plus sun light: {worldSun.name}");

                            if (showPopup && currentPopup != null)
                            {
                                currentPopup.Config.Value = true;
                                Config.Save();
                                if (popupQueue.Count > 0)
                                {
                                    currentPopup = popupQueue.Dequeue();
                                    scrollPos = Vector2.zero;
                                    popupRectInitialized = false;
                                }
                                else
                                {
                                    currentPopup = null;
                                    showPopup = false;
                                    pendingShow = false;
                                }
                            }
                        }
                    }
                    catch {}
                }
            }
            if (SegiStationeersInstance == null && Camera.main != null)
            {
                try
                {
                    InitializeSEGI();
                }
                catch (Exception ex)
                {
                    Log.LogError($"Failed to initialize SEGI Plus: {ex.Message}");
                }
            }

            if (pendingShow && currentPopup == null && popupQueue != null && popupQueue.Count > 0 &&
                IsInGameWorld())
            {
                if (popupDelay > 0f)
                {
                    popupDelay -= Time.deltaTime;
                    return;
                }
                currentPopup = popupQueue.Dequeue();
                StartShowingPopup(currentPopup);
            }

            if (showPopup && !IsInGameWorld())
            {
                showPopup = false;
            }

            if (popupRectInitialized && (Screen.height != lastScreenHeight || Screen.width != lastScreenWidth))
            {
                popupRectInitialized = false;
                lastScreenHeight = Screen.height;
                lastScreenWidth = Screen.width;
            }
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void BindAllConfigs()
        {
            Enabled = Config.Bind("General", "Enable (There is also F11 config menu in-game)", false, "Enable SEGI Plus global illumination. There is an F11 config menu in-game too. Default off so you can tune before enabling.");
            ToggleEnabledHotkey = Config.Bind("General", "Toggle Enabled Hotkey", new KeyboardShortcut(KeyCode.F9),
                "Toggles SEGI Plus on/off in-game without opening the F11 menu.");
            UseGainMultiplier = Config.Bind("General", "Use x10 Gain Multiplier (applies to Global/Emissive gains)", false, "Multiply GI and Emissive gain values by 10. Why? I dunno, but you can");
            EmissiveLightGain = Config.Bind("Gain Knobs", "Emissive Light Gain", 2.0f,
                new ConfigDescription("Multiplier for emissive light contribution during voxelization", new AcceptableValueRange<float>(0f, 10f)));
            // Per-channel gain on the GI add-layer (tone-map stage, not voxelization - see GIToneMap.cs).
            // EmissiveLightGain above is a single scalar consumed inside SEGI's stock voxelization
            // shader, which we have no editable source for, so a true per-channel voxelization gain
            // isn't reachable; this applies the extra R/G/B amplification one stage later instead.
            GIRedGain = Config.Bind("Gain Knobs", "GI Red Gain", 1.0f,
                new ConfigDescription("Extra per-channel multiplier on the GI add-layer's red channel. 1 = off.", new AcceptableValueRange<float>(0f, 4f)));
            GIGreenGain = Config.Bind("Gain Knobs", "GI Green Gain", 1.0f,
                new ConfigDescription("Extra per-channel multiplier on the GI add-layer's green channel. 1 = off.", new AcceptableValueRange<float>(0f, 4f)));
            GIBlueGain = Config.Bind("Gain Knobs", "GI Blue Gain", 1.0f,
                new ConfigDescription("Extra per-channel multiplier on the GI add-layer's blue channel. 1 = off.", new AcceptableValueRange<float>(0f, 4f)));
            GIGain = Config.Bind("Gain Knobs", "Global Illumination Gain", 2.0f,
                new ConfigDescription("Global illumination gain", new AcceptableValueRange<float>(0f, 20f)));
            SecondaryBounceGain = Config.Bind("Gain Knobs", "Secondary Bounce Gain", 0.25f,
                new ConfigDescription("Secondary bounce gain", new AcceptableValueRange<float>(0f, 1f)));
            GIGamma = Config.Bind("Gain Knobs", "GI Gamma (contribution only)", 1.0f,
                new ConfigDescription("Gamma on the GI add-layer only. 1 = off/linear.",
                    new AcceptableValueRange<float>(0.4f, 10f)));
            GIHighlightGain = Config.Bind("Gain Knobs", "GI Highlight Gain", 0.0f,
                new ConfigDescription("Bright-only lift after gamma, before toe. 0 = off.",
                    new AcceptableValueRange<float>(0f, 20f)));
            GIToe = Config.Bind("Gain Knobs", "GI Toe / Shadow Crush", 0.0f,
                new ConfigDescription("Black-level crush after gamma/highlight. 0 = off.",
                    new AcceptableValueRange<float>(0f, 0.2f)));
            BloomBlur = Config.Bind("Screen Post", "Bloom Blur Radius", 8.0f,
                new ConfigDescription("Blur radius for screen bloom. Mix 0 disables the stage.",
                    new AcceptableValueRange<float>(0f, 48f)));
            BloomGain = Config.Bind("Screen Post", "Bloom Gain", 1.0f,
                new ConfigDescription("Gain on the blurred bloom buffer.",
                    new AcceptableValueRange<float>(0f, 8f)));
            BloomGamma = Config.Bind("Screen Post", "Bloom Gamma", 1.0f,
                new ConfigDescription("Gamma on the blurred bloom buffer. 1 = linear.",
                    new AcceptableValueRange<float>(0.2f, 5f)));
            BloomMix = Config.Bind("Screen Post", "Bloom Mix", 0.0f,
                new ConfigDescription("0 = bloom stage off. Adds processed blur onto the composite.",
                    new AcceptableValueRange<float>(0f, 1f)));
            ScreenGamma = Config.Bind("Screen Post", "Screen Gamma", 1.0f,
                new ConfigDescription("Gamma on the full composited 3D frame after bloom. 1 = off.",
                    new AcceptableValueRange<float>(0.2f, 5f)));
            DitherStrength = Config.Bind("Screen Post", "Dither Strength", 0f,
                new ConfigDescription("Blue-noise dither on the final frame, to break up color-banding rings in smooth light falloff. 0 = off, 1 = one full 8-bit step.",
                    new AcceptableValueRange<float>(0f, 2f)));
            EmissiveBubbleEnabled = Config.Bind("Gain Knobs", "Emissive Exclusion Bubble", true,
                "Prevents held items and suit from casting emissive light into the scene.");
            QualityLevel = Config.Bind("Performance", "Quality Level - 0 for Low, 4 for Ultra Extreme.", 1,
                new ConfigDescription("Quality (0=Low, 1=Medium, 2=High, 3=Extreme, 4=Ultra Extreme VRAM Eater Pro Max)",
                    new AcceptableValueRange<int>(0, 4)));
            DenseVoxelMode = Config.Bind("Performance", "High Density Mode", false,
                "Finer GI detail at the cost of shorter range.");
            HighDensityRangeStep = Config.Bind("Performance", "High Density Range Step", 0,
                new ConfigDescription(
                    "0 = stock High Density (half range, double density). Each step trades a bit of that density back for range; the top step is full stock range, still at double density (so it costs more voxels). Inert when High Density is off.",
                    new AcceptableValueRange<int>(0, ConfigData.HighDensityRangeMaxStep)));
            ForwardOriginBias = Config.Bind("Performance", "Forward Origin Bias", true,
                "Pushes the voxel volume 25% forward in the camera's facing direction.");
            LightweightMode = Config.Bind("Performance", "**Lightweight Mode**", false,
                "If you enable this it cull most objects except the emissive ones during voxelization and runs *way* faster, at the cost of light leakage. Can be combined with any quality setting. Will be deprecated in a future update.");
            AdaptivePerformance = Config.Bind("Performance", "Adaptive Performance", false,
                "Automatically adjusts settings to maintain framerate. Will be deprecated in a future update.");
            AdaptiveStrategy = Config.Bind("Performance", "Adaptive Strategy", 1,
                new ConfigDescription("Adaptive Strategy (0=Balanced, 1=Reduce distance first)",
                    new AcceptableValueRange<int>(0, 1)));
            AdaptiveMinDistancePercent = Config.Bind("Performance", "Adaptive Min Distance Percent (Strategy 1)", 20f,
                new ConfigDescription("Minimum voxel space size as percentage of max when using 'Reduce Distance First' strategy (lower = more aggressive)",
                    new AcceptableValueRange<float>(10f, 100f)));
            TargetFramerate = Config.Bind("Performance", "Target Framerate", 60,
                new ConfigDescription("The system will try to adjust SEGI Plus to stay around this framerate",
                    new AcceptableValueRange<int>(15, 240)));
            OcclusionStrengthOffset = Config.Bind("Advanced", "Occlusion Strength Offset", 0f,
                new ConfigDescription("Offset for how strongly geometry stops GI. Higher reduces light leaking through walls but also darkens scene. 0 = default.",
                    new AcceptableValueRange<float>(-0.35f, 0.75f)));
            ConeTraceBiasOffset = Config.Bind("Advanced", "Cone Trace Bias Offset", 0f,
                new ConfigDescription("Offset forwHow far from surfaces GI probes sample, Lower = more self-occlusion. Higher = more light leakage. 0 = default.",
                    new AcceptableValueRange<float>(-0.3f, 0.6f)));
            InnerOcclusionLayers = Config.Bind("Advanced", "Inner Occlusion Layers", 1,
                new ConfigDescription("How many voxel layers around near occluders count as solid. Higher blocks more light leaking around thin/small objects but can over-darken nearby surfaces. 1 = default.",
                    new AcceptableValueRange<int>(0, 4)));
            FlashlightFillPower = Config.Bind("Flashlight Fill Light", "Strength", 0f,
                new ConfigDescription("Fake white point light intensity at your active flashlight/headlamp. 0 = off, skips entirely.",
                    new AcceptableValueRange<float>(0f, 5f)));
            FlashlightFillRange = Config.Bind("Flashlight Fill Light", "Falloff Range", 3f,
                new ConfigDescription("How far the fake fill light reaches before fading out.",
                    new AcceptableValueRange<float>(0.5f, 10f)));
            LampFillPower = Config.Bind("Lamp Fill Light", "Strength", 0f,
                new ConfigDescription("Fake point light intensity for each of the 8 lights ringing every active stationary lamp. 0 = off, skips entirely.",
                    new AcceptableValueRange<float>(0f, 5f)));
            LampFillRange = Config.Bind("Lamp Fill Light", "Falloff Range", 3f,
                new ConfigDescription("How far each ring light reaches before fading out.",
                    new AcceptableValueRange<float>(0.5f, 10f)));
            LampFillOrthogonal = Config.Bind("Lamp Fill Light", "Orthogonal Ring Lights", true,
                "Places the 4 ring lights at 0/90/180/270 degrees around each lamp. Both this and Diagonal off = feature fully off regardless of Strength.");
            LampFillDiagonal = Config.Bind("Lamp Fill Light", "Diagonal Ring Lights", true,
                "Places the 4 ring lights at 45/135/225/315 degrees around each lamp. Both this and Orthogonal off = feature fully off regardless of Strength.");
            EyeAdaptationEnabled = Config.Bind("Eye Adaptation", "Enable Eye Adaptation", false,
                "Fades a runtime-only GI Gain modifier based on measured screen brightness. Does not change your GI Gain slider value.");
            EyeAdaptationModifierAtMinBrightness = Config.Bind("Eye Adaptation", "Modifier at Min Brightness", 2.0f,
                new ConfigDescription("GI Gain modifier applied when the measured screen brightness is at its lowest.",
                    new AcceptableValueRange<float>(-30f, 30f)));
            EyeAdaptationModifierAtMaxBrightness = Config.Bind("Eye Adaptation", "Modifier at Max Brightness", -2.0f,
                new ConfigDescription("GI Gain modifier applied when the measured screen brightness is at its highest.",
                    new AcceptableValueRange<float>(-30f, 30f)));
            EyeAdaptationFadeTimeAtMinBrightness = Config.Bind("Eye Adaptation", "Fade Time at Min Brightness", 4.0f,
                new ConfigDescription("Seconds to fade toward the Min Brightness modifier.",
                    new AcceptableValueRange<float>(0.1f, 15f)));
            EyeAdaptationFadeTimeAtMaxBrightness = Config.Bind("Eye Adaptation", "Fade Time at Max Brightness", 1.5f,
                new ConfigDescription("Seconds to fade toward the Max Brightness modifier.",
                    new AcceptableValueRange<float>(0.1f, 15f)));
            EyeAdaptationSensitivity = Config.Bind("Eye Adaptation", "Sensitivity", 1.0f,
                new ConfigDescription("Multiplies measured brightness before it's mapped to Min/Max. Turn up if it isn't reacting enough to what you see; down if it overreacts.",
                    new AcceptableValueRange<float>(0.1f, 10f)));
            EyeAdaptationScreenGammaModifierAtMinBrightness = Config.Bind("Eye Adaptation", "Screen Gamma Modifier at Min Brightness", 0f,
                new ConfigDescription("Added to Screen Gamma when measured screen brightness is at its lowest. 0 = inactive.",
                    new AcceptableValueRange<float>(-0.8f, 4f)));
            EyeAdaptationScreenGammaModifierAtMaxBrightness = Config.Bind("Eye Adaptation", "Screen Gamma Modifier at Max Brightness", 0f,
                new ConfigDescription("Added to Screen Gamma when measured screen brightness is at its highest. 0 = inactive.",
                    new AcceptableValueRange<float>(-0.8f, 4f)));
        }

        private IEnumerator InitializeSEGICoroutine()
        {
            while (Camera.main == null)
            {
                yield return new WaitForSeconds(0.1f);
            }
            yield return new WaitForSeconds(1f);
            try
            {
                InitializeSEGI();
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to initialize SEGI: {ex.Message}");
            }
        }

        private void InitializeSEGI()
        {
            if (Camera.main == null)
            {
                Log.LogWarning("No main camera found for SEGI initialization");
                return;
            }
            if (!IsInGameWorld())
            {
                return;
            }
            if (SegiStationeersInstance != null)
            {
                Log.LogWarning("SEGI Plus running");
                return;
            }
            SegiStationeersInstance = Camera.main.gameObject.AddComponent<SEGIStationeers>();
            DontDestroyOnLoad(SegiStationeersInstance.gameObject);
            Log.LogInfo("SEGI Plus initialized");
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            Log.LogInfo($"Scene loaded: '{scene.name}' (mode: {mode})");
            if (SegiStationeersInstance != null && !IsInGameWorld())
            {
                Log.LogInfo("Cleaning up");
                DestroyImmediate(SegiStationeersInstance.gameObject);
                SegiStationeersInstance = null;
            }
            else if (SegiStationeersInstance == null && IsInGameWorld())
            {
                Log.LogInfo("Entered world");
                Log.LogInfo($": '{scene.name}' (mode: {mode})");
                StartCoroutine(InitializeSEGICoroutine());
            }
        }

        public bool IsInGameWorld()
        {
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            string lowerSceneName = sceneName.ToLower();
            bool isMenu = lowerSceneName.Contains("menu") || lowerSceneName.Contains("splash");
            bool isGameWorld = !isMenu;
            return isGameWorld;
        }

        private ConfigEntry<bool> AddUpdatePopup(string key, string title, string changelog, bool defaultSeen = false)
        {
            var cfg = Config.Bind("X - NO TOUCH - Internal", key, defaultSeen, new ConfigDescription(""));
            var item = new UpdatePopupItem
            {
                Key = key,
                Title = title,
                Changelog = changelog,
                Config = cfg
            };
            allPopups.Add(item);
            return cfg;
        }

        private void StartShowingPopup(UpdatePopupItem item)
        {
            popupRectInitialized = false;
            scrollPos = Vector2.zero;
            showPopup = true;
        }

        private void InitializePopupRect()
        {
            if (popupRectInitialized) return;

            float screenHeight = Screen.height;
            float screenWidth = Screen.width;
            float baseHeight = 1080f;

            guiScale = Mathf.Max(1.0f, screenHeight / baseHeight);

            float baseWidth = 520f;
            float basePopupHeight = 320f;

            float scaledWidth = baseWidth * guiScale;
            float scaledHeight = basePopupHeight * guiScale;

            popupRect = new Rect((screenWidth - scaledWidth) / 2f, (screenHeight - scaledHeight) / 2f, scaledWidth, scaledHeight);
            lastScreenHeight = (int)screenHeight;
            lastScreenWidth = (int)screenWidth;
            popupRectInitialized = true;
        }
        private void OnGUI()
        {
            Color oldColor = GUI.color;
            if (!showPopup || currentPopup == null) return;
            if (!IsInGameWorld()) return;

            if (!popupRectInitialized)
                InitializePopupRect();

            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(guiScale, guiScale, 1.0f));

            Rect scaledRect = new Rect(
                popupRect.x / guiScale,
                popupRect.y / guiScale,
                popupRect.width / guiScale,
                popupRect.height / guiScale
            );

            GUI.color = Color.white;
            GUI.backgroundColor = Color.red;
            scaledRect = GUI.ModalWindow(987987987, scaledRect, DrawPopupWindow, currentPopup.Title);

            popupRect = new Rect(
                scaledRect.x * guiScale,
                scaledRect.y * guiScale,
                scaledRect.width * guiScale,
                scaledRect.height * guiScale
            );

            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        private void DrawPopupWindow(int id)
        {
            GUILayout.BeginVertical();
            var wrapStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };

            float scaledHeight = (popupRect.height / guiScale) - 80;
            scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.Height(scaledHeight));
            GUILayout.Label(currentPopup.Changelog, wrapStyle, GUILayout.ExpandWidth(true));
            GUILayout.EndScrollView();

            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("OK - Don't show this update again", GUILayout.Height(30), GUILayout.Width(250)))
            {
                currentPopup.Config.Value = true;
                Config.Save();
                if (popupQueue.Count > 0)
                {
                    currentPopup = popupQueue.Dequeue();
                    scrollPos = Vector2.zero;
                    popupRectInitialized = false;
                    showPopup = true;
                }
                else
                {
                    currentPopup = null;
                    showPopup = false;
                    pendingShow = false;
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

    }
    internal static class ConfigData
    {
        private static int CurrentQualityLevel => SEGIPlugin.QualityLevel?.Value ?? 1;
        private static readonly SEGIStationeers.VoxelResolution[] VoxelResolutions =
            [SEGIStationeers.VoxelResolution.Medium, SEGIStationeers.VoxelResolution.Medium, SEGIStationeers.VoxelResolution.High, SEGIStationeers.VoxelResolution.High, SEGIStationeers.VoxelResolution.Ultra];
        private static readonly bool[] HalfResolutionLevels = [true, false, false, false, false];
        private static readonly bool[] VoxelAntiAliasingLevels = [false, false, true, true, true];
        private static readonly float[] VoxelSpaceSizes = [16.0f, 16.0f, 32.0f, 32.0f, 48.0f];
        private static readonly float[] ShadowSpaceSizes = [24.0f, 24.0f, 48.0f, 48.0f, 60.0f];
        private static readonly bool[] GaussianMipFilterLevels = [true, true, true, true, true];
        private static readonly int[] ConesLevels = [4, 6, 8, 12, 12];
        private static readonly int[] ConeTraceStepsLevels = [6, 8, 10, 14, 14];
        private static readonly float[] ConeLengths = [1.0f, 1.0f, 1.0f, 1.0f, 1.0f];
        private static readonly float[] ConeWidths = [6.0f, 6.0f, 6.0f, 6.0f, 6.0f];
        private static readonly int[] SunShadowResolutions = [256, 256, 512, 512, 512];
        public static SEGIStationeers.VoxelResolution VoxelResolution => VoxelResolutions[CurrentQualityLevel];
        public static bool HalfResolution => HalfResolutionLevels[CurrentQualityLevel];
        public static bool VoxelAntiAliasing => VoxelAntiAliasingLevels[CurrentQualityLevel];
        public static bool DenseVoxelMode => SEGIPlugin.DenseVoxelMode?.Value ?? false;
        // Inert unless High Density is on. Step 0 = stock HD (half box, base voxels).
        // Each step trades density back for range; top step = full box, double voxels.
        // Density (voxels per world unit) is exactly 2x normal at every step in between.
        public const int HighDensityRangeMaxStep = 8;
        public static int HighDensityRangeStep =>
            DenseVoxelMode ? Mathf.Clamp(SEGIPlugin.HighDensityRangeStep?.Value ?? 0, 0, HighDensityRangeMaxStep) : 0;
        private static float HighDensityRangeT => HighDensityRangeStep / (float)HighDensityRangeMaxStep;
        private static float DenseScale => DenseVoxelMode ? 0.5f * (1f + HighDensityRangeT) : 1.0f;
        public static int EffectiveVoxelResolution
        {
            get
            {
                int baseRes = (int)VoxelResolutions[CurrentQualityLevel];
                return DenseVoxelMode ? Mathf.RoundToInt(baseRes * (1f + HighDensityRangeT)) : baseRes;
            }
        }
        // Preview helpers for the F11 slider label - what step N would produce, without committing it.
        public static float PreviewVoxelSpaceSizeAtStep(int step)
        {
            float t = Mathf.Clamp01(step / (float)HighDensityRangeMaxStep);
            return VoxelSpaceSizes[CurrentQualityLevel] * 0.5f * (1f + t);
        }
        public static int PreviewVoxelResolutionAtStep(int step)
        {
            float t = Mathf.Clamp01(step / (float)HighDensityRangeMaxStep);
            int baseRes = (int)VoxelResolutions[CurrentQualityLevel];
            return Mathf.RoundToInt(baseRes * (1f + t));
        }
        public static float VoxelSpaceSize => VoxelSpaceSizes[CurrentQualityLevel] * DenseScale;
        public static float ShadowSpaceSize => ShadowSpaceSizes[CurrentQualityLevel] * DenseScale;
        public static bool GaussianMipFilter => GaussianMipFilterLevels[CurrentQualityLevel];
        public static int Cones => ConesLevels[CurrentQualityLevel];
        public static int ConeTraceSteps => ConeTraceStepsLevels[CurrentQualityLevel];
        public static float ConeLength => ConeLengths[CurrentQualityLevel] * (DenseVoxelMode ? 1.5f : 1.0f);
        public static float ConeWidth => ConeWidths[CurrentQualityLevel];
        public static int SunShadowResolution => SunShadowResolutions[CurrentQualityLevel];
        public static float ConeTraceBias => Mathf.Max(0.05f, (DenseVoxelMode ? 0.325f : 0.65f) + (SEGIPlugin.ConeTraceBiasOffset?.Value ?? 0f));
        public static float TemporalBlendWeight => 0.01f;
        public static float GIGain
        {
            get
            {
                // Eye adaptation is added here, at read-time, so the persisted slider
                // value in the config file never reflects the runtime modifier.
                float baseValue = (SEGIPlugin.GIGain?.Value ?? 0.6f) + EyeAdaptation.CurrentModifier;
                bool useMultiplier = SEGIPlugin.UseGainMultiplier?.Value ?? false;
                return useMultiplier ? baseValue * 10f : baseValue;
            }
        }

        public static float EmissiveLightGain
        {
            get
            {
                float baseValue = SEGIPlugin.EmissiveLightGain?.Value ?? 3.0f;
                bool useMultiplier = SEGIPlugin.UseGainMultiplier?.Value ?? false;
                return useMultiplier ? baseValue * 10f : baseValue;
            }
        }

        public static bool EmissiveBubbleEnabled => SEGIPlugin.EmissiveBubbleEnabled?.Value ?? false;

        public static float SecondaryBounceGain
        {
            get
            {
                return SEGIPlugin.SecondaryBounceGain?.Value ?? 0.4f;
            }
        }
        public static float GIGamma => Mathf.Clamp(SEGIPlugin.GIGamma?.Value ?? 1f, 0.4f, 10f);
        public static float GIHighlightGain => Mathf.Clamp(SEGIPlugin.GIHighlightGain?.Value ?? 0f, 0f, 20f);
        public static float GIToe => Mathf.Clamp(SEGIPlugin.GIToe?.Value ?? 0f, 0f, 0.2f);
        public static float GIRedGain => Mathf.Clamp(SEGIPlugin.GIRedGain?.Value ?? 1f, 0f, 4f);
        public static float GIGreenGain => Mathf.Clamp(SEGIPlugin.GIGreenGain?.Value ?? 1f, 0f, 4f);
        public static float GIBlueGain => Mathf.Clamp(SEGIPlugin.GIBlueGain?.Value ?? 1f, 0f, 4f);
        public static float BloomBlur => Mathf.Clamp(SEGIPlugin.BloomBlur?.Value ?? 8f, 0f, 48f);
        public static float BloomGain => Mathf.Clamp(SEGIPlugin.BloomGain?.Value ?? 1f, 0f, 8f);
        public static float BloomGamma => Mathf.Clamp(SEGIPlugin.BloomGamma?.Value ?? 1f, 0.2f, 5f);
        public static float BloomMix => Mathf.Clamp01(SEGIPlugin.BloomMix?.Value ?? 0f);
        public static float ScreenGamma
        {
            get
            {
                // Eye adaptation is added here, at read-time, same pattern as GIGain above -
                // the persisted slider value (meant to stay at 1) never reflects the runtime modifier.
                float baseValue = (SEGIPlugin.ScreenGamma?.Value ?? 1f) + EyeAdaptation.CurrentGammaModifier;
                return Mathf.Clamp(baseValue, 0.2f, 5f);
            }
        }
        public static float DitherStrength => Mathf.Clamp(SEGIPlugin.DitherStrength?.Value ?? 0f, 0f, 2f);
        public static float OcclusionStrength => Mathf.Clamp(0.86f + (SEGIPlugin.OcclusionStrengthOffset?.Value ?? 0f), 0.5f, 1.6f);
        private static readonly float[] NearOcclusionStrengths = { 0.42f, 0.42f, 0.86f, 0.86f, 0.86f };
        public static float NearOcclusionStrength => NearOcclusionStrengths[CurrentQualityLevel];
        public static float OcclusionPower => 1.0f;
        public static int InnerOcclusionLayers => Mathf.Clamp(SEGIPlugin.InnerOcclusionLayers?.Value ?? 1, 0, 4);
        public static int SecondaryCones => 4;
        public static float SecondaryOcclusionStrength => 1.25f;
        public static float FarOcclusionStrength => 0.86f;
        public static float FarthestOcclusionStrength => 0.86f;
        public static bool LightweightMode => SEGIPlugin.LightweightMode?.Value ?? false;
        public static bool ForwardOriginBias => SEGIPlugin.ForwardOriginBias?.Value ?? true;
        public static int TargetFramerate => SEGIPlugin.TargetFramerate?.Value ?? 75;

        public static string GetQualityName()
        {
            return CurrentQualityLevel switch
            {
                0 => "Low",
                1 => "Medium",
                2 => "High",
                3 => "Extreme",
                4 => "Ultra Extreme VRAM Eater Pro Max",
                _ => "Unknown"
            };
        }
        public static float AdaptiveMinVoxelSpaceSize
        {
            get
            {
                int quality = SEGIPlugin.QualityLevel?.Value ?? 1;
                return VoxelSpaceSizes[quality] * DenseScale * 0.5f; // Start at 50% of max
            }
        }
        public static int AdaptiveStrategy => SEGIPlugin.AdaptiveStrategy?.Value ?? 0;
        public static bool AdaptivePerformance => SEGIPlugin.AdaptivePerformance?.Value ?? false;
        public static float AdaptiveScaleDownThreshold => 1.15f;  // Scale down if frame time >this
        public static float AdaptiveScaleUpThreshold => 0.85f;    // Scale up only if frame time <this
        public static float AdaptiveRate => 0.05f;
        public static bool AdaptiveMaxHalfResolution => HalfResolutionLevels[CurrentQualityLevel];
        public static bool AdaptiveMaxVoxelAA => VoxelAntiAliasingLevels[CurrentQualityLevel];
        public static int AdaptiveMinVoxelRes => 64;
        public static int AdaptiveMinCones => 4;
        public static int AdaptiveMinConeTraceSteps => 6;

        private static readonly float[] AdaptiveMinVoxelSpaceSizeMultipliers = { 0.5f, 0.25f };
        public static float GetAdaptiveMinVoxelSpaceSize(int strategy)
        {
            float maxMaxDistance = VoxelSpaceSizes[3] * DenseScale;
            float currentMax = VoxelSpaceSizes[CurrentQualityLevel] * DenseScale;

            if (strategy == 1) // reduce dist first
            {
                float percent = SEGIPlugin.AdaptiveMinDistancePercent?.Value ?? 25f;
                float absoluteMinimum = maxMaxDistance * (percent / 100f);
                return Mathf.Min(absoluteMinimum, currentMax);
            }
            else // balanced
            {
                float absoluteMinimum = maxMaxDistance * AdaptiveMinVoxelSpaceSizeMultipliers[0];
                return Mathf.Min(absoluteMinimum, currentMax);
            }
        }

        public static float GetAdaptiveMaxVoxelSpaceSize(int strategy)
        {
            return VoxelSpaceSizes[CurrentQualityLevel] * DenseScale;
        }

        private static readonly float[] AdaptiveVoxelSpaceScaleThresholds = { 0.9f, 0.9f };
        private static readonly float[] AdaptiveVoxelSpaceScaleRanges = { 0.75f, 0.75f };
        public static float GetAdaptiveVoxelSpaceScaleThreshold(int strategy) => AdaptiveVoxelSpaceScaleThresholds[strategy];
        public static float GetAdaptiveVoxelSpaceScaleRange(int strategy) => AdaptiveVoxelSpaceScaleRanges[strategy];


        private static readonly float[] AdaptiveResolutionScaleThresholds = { 0.9f, 0.3f };
        private static readonly float[] AdaptiveResolutionScaleRanges = { 0.75f, 0.75f };
        public static float GetAdaptiveResolutionScaleThreshold(int strategy) => AdaptiveResolutionScaleThresholds[strategy];
        public static float GetAdaptiveResolutionScaleRange(int strategy) => AdaptiveResolutionScaleRanges[strategy];


        private static readonly float[] AdaptiveConesStepsScaleThresholds = { 1.0f, 0.6f };
        private static readonly float[] AdaptiveConesStepsScaleRanges = { 1.0f, 0.3f };
        public static float GetAdaptiveConesStepsScaleThreshold(int strategy) => AdaptiveConesStepsScaleThresholds[strategy];
        public static float GetAdaptiveConesStepsScaleRange(int strategy) => AdaptiveConesStepsScaleRanges[strategy];


        // public static float AdaptiveHalfResOnThreshold => 0.65f;   // Turn ON half res below this
        // public static float AdaptiveHalfResOffThreshold => 0.75f;  // Turn OFF half res above this
        private static readonly float[] AdaptiveHalfResOnThresholds = { 0.65f, 0.25f };
        private static readonly float[] AdaptiveHalfResOffThresholds = { 0.75f, 0.30f };
        public static float GetAdaptiveHalfResOnThreshold(int strategy) => AdaptiveHalfResOnThresholds[strategy];
        public static float GetAdaptiveHalfResOffThreshold(int strategy) => AdaptiveHalfResOffThresholds[strategy];


        // public static float AdaptiveVoxelAAOffThreshold => 0.55f;  // Turn OFF voxel AA below this
        // public static float AdaptiveVoxelAAOnThreshold => 0.65f;   // Turn ON voxel AA above this
        private static readonly float[] AdaptiveVoxelAAOffThresholds = { 0.55f, 0.60f };
        private static readonly float[] AdaptiveVoxelAAOnThresholds = { 0.65f, 0.70f };
        public static float GetAdaptiveVoxelAAOffThreshold(int strategy) => AdaptiveVoxelAAOffThresholds[strategy];
        public static float GetAdaptiveVoxelAAOnThreshold(int strategy) => AdaptiveVoxelAAOnThresholds[strategy];


        // public static float AdaptiveBilateralOffThreshold => 0.65f; // Turn OFF filtering below this
        // public static float AdaptiveBilateralOnThreshold => 0.75f;  // Turn ON filtering above this
        private static readonly float[] AdaptiveBilateralOffThresholds = { 0.65f, 0.60f };
        private static readonly float[] AdaptiveBilateralOnThresholds = { 0.75f, 0.70f };
        public static float GetAdaptiveBilateralOffThreshold(int strategy) => AdaptiveBilateralOffThresholds[strategy];
        public static float GetAdaptiveBilateralOnThreshold(int strategy) => AdaptiveBilateralOnThresholds[strategy];


    }
}