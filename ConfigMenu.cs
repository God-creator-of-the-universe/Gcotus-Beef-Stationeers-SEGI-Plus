using HarmonyLib;
using UnityEngine;

namespace BeefsSEGIPlus;

public class ConfigMenu : MonoBehaviour
{
    // Some game code re-asserts CursorLockMode.Locked on click regardless of our cursor
    // state (e.g. interact/look input isn't gated on lock state), which snaps the OS
    // cursor to screen center the instant it happens - reverting lockState afterward
    // does not undo that snap. IsOpen + the Harmony prefix below stop the lock from ever
    // being (re)engaged while our window is up, instead of trying to fix it after the fact.
    public static bool IsOpen { get; private set; }

    private bool _showConfigField = false;
    private bool _showConfig
    {
        get => _showConfigField;
        set
        {
            _showConfigField = value;
            IsOpen = value;
        }
    }
    private Vector2 _scrollPosition = Vector2.zero;
    private Rect _windowRect;
    private bool _windowRectInitialized = false;
    private int _lastScreenHeight = 0;
    private int _lastScreenWidth = 0;
    private float _guiScale = 1.0f;
    private GUIStyle _orangeBoxStyle;
    private GUIStyle _blueBoxStyle;
    private GUIStyle _greenBoxStyle;
    private GUIStyle _purpleBoxStyle;
    private GUIStyle _darkerBoxStyle;
    private GUIStyle _windowBoxStyle;
    private GUIStyle _enableOnStyle;
    private GUIStyle _enableOffStyle;
    private bool _stylesInitialized = false;
    private bool _showAdvanced = false;
    private bool _showBloomSection = false;
    private bool _showFlashlightFillSection = false;
    private bool _showLampFillSection = false;
    private bool _showEyeAdaptationSection = false;

    #if SEGI_PROFILER
        private GUIStyle _profilerBoxStyle;
        private Vector2 _profilerResultsScroll = Vector2.zero;
    #endif


    private void Update()
    {
        bool inGameWorld = IsInGameWorlExclMainMenu();

        if (_showConfig && !inGameWorld)
        {
            _showConfig = false;
            return;
        }

        if (!inGameWorld) return;

        if (SEGIPlugin.ToggleEnabledHotkey.Value.IsDown())
        {
            SEGIPlugin.Enabled.Value = !SEGIPlugin.Enabled.Value;
        }

        if (_showConfig && Input.GetKeyDown(KeyCode.Escape))
        {
            _showConfig = false;
            return;
        }

        if (Input.GetKeyDown(KeyCode.F11))
        {
            _showConfig = !_showConfig;
            if (_showConfig)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        if (_windowRectInitialized && (Screen.height != _lastScreenHeight || Screen.width != _lastScreenWidth))
        {
            _windowRectInitialized = false;
            _lastScreenHeight = Screen.height;
            _lastScreenWidth = Screen.width;
        }
    }

    private void OnGUI()
    {
        if (_showConfig)
        {
            if (!_windowRectInitialized)
                InitializeWindowRect();

            if (!_stylesInitialized)
                InitializeStyles();

            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(_guiScale, _guiScale, 1.0f));

            Rect scaledWindowRect = new Rect(
                _windowRect.x / _guiScale,
                _windowRect.y / _guiScale,
                _windowRect.width / _guiScale,
                _windowRect.height / _guiScale
            );

            Color oldColor = GUI.color;
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            GUI.Box(new Rect(scaledWindowRect.x - 2, scaledWindowRect.y - 2, scaledWindowRect.width + 4, scaledWindowRect.height + 4), "");
            GUI.color = oldColor;

            float fixedW = scaledWindowRect.width;
            float fixedH = scaledWindowRect.height;
            scaledWindowRect = GUILayout.Window(0, scaledWindowRect, ConfigWindow, PluginInfo.BUILD_STAMP, _windowBoxStyle);
            // Keep size pinned. GUILayout.Window feedback otherwise grows the rect every frame.
            _windowRect = new Rect(
                scaledWindowRect.x * _guiScale,
                scaledWindowRect.y * _guiScale,
                fixedW * _guiScale,
                fixedH * _guiScale
            );

            GUI.matrix = oldMatrix;
        }
    }

    private void LateUpdate()
    {
        // Stationeers UI reads Input.* independently of IMGUI Event.Use().
        // Resetting axes while a mouse button is held over the lab window stops click-through
        // without eating the axes on every hover frame (that continuous reset was warping the
        // cursor back to screen center whenever it merely moved across the window).
        if (!_showConfig) return;
        if (!Input.GetMouseButton(0) && !Input.GetMouseButtonDown(0)) return;
        Vector2 mouseGui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        if (_windowRect.Contains(mouseGui))
            Input.ResetInputAxes();
    }

    private bool IsInGameWorlExclMainMenu()
    {
        if (!(SEGIPlugin.Instance?.IsInGameWorld() ?? false))
            return false;
        try
        {
            Light worldSun = WorldManager.Instance?.WorldSun?.TargetLight;
            return worldSun != null;
        }
        catch
        {
            return false;
        }
    }

    private void InitializeWindowRect()
    {
        if (_windowRectInitialized) return;
        float screenHeight = Screen.height;
        float screenWidth = Screen.width;
        float baseHeight = 1440f;
        // lab11: 70% width / 80% height of the previous lab10 box (845x1330). lab12: width
        // bumped back up 10% (591.5 -> 650.65) since 70% was too cramped.
        float baseWindowHeight = 1064f;
        float baseWindowWidth = 650.65f;
        _guiScale = Mathf.Max(1.0f, screenHeight / baseHeight);
        float scaledWidth = baseWindowWidth * _guiScale;
        float scaledHeight = baseWindowHeight * _guiScale;
        float maxHeight = screenHeight * 0.92f;
        float maxWidth = screenWidth * 0.62f;
        float windowHeight = Mathf.Min(scaledHeight, maxHeight);
        float windowWidth = Mathf.Min(scaledWidth, maxWidth);
        _windowRect = new Rect(20, 20, windowWidth, windowHeight);
        _windowRectInitialized = true;
    }

    private void ConfigWindow(int windowID)
    {
        GUILayout.BeginVertical();

        float scrollViewHeight = (_windowRect.height / _guiScale) - 50f;
        float scrollViewWidth = (_windowRect.width / _guiScale) - 20f;
        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition,
            GUILayout.Width(scrollViewWidth),
            GUILayout.Height(scrollViewHeight));

        GUILayout.Label("=== Gcotu-Tuned SEGI Plus ===", _darkerBoxStyle, GUILayout.ExpandWidth(true));

        var currentEnabled = SEGIPlugin.Enabled.Value;
        var enableStyle = currentEnabled ? _enableOnStyle : _enableOffStyle;
        if (GUILayout.Button($"SEGI PLUS LAB: {(currentEnabled ? "ENABLED" : "DISABLED")}", enableStyle,
                GUILayout.Height(66), GUILayout.ExpandWidth(true)))
        {
            SEGIPlugin.Enabled.Value = !currentEnabled;
            // Toggling collapses/expands almost the whole window's content; a stale scroll
            // offset from before would otherwise leave this button scrolled out of view.
            _scrollPosition = Vector2.zero;
        }

            GUILayout.BeginVertical(_orangeBoxStyle);
            GUILayout.Label("=== Quality Level ===", _darkerBoxStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label($"Quality Level: {SEGIPlugin.QualityLevel.Value} ({ConfigData.GetQualityName()})");
            var newQualityLevel = Mathf.RoundToInt(GUILayout.HorizontalSlider(SEGIPlugin.QualityLevel.Value, 0, 4));
            if (newQualityLevel != SEGIPlugin.QualityLevel.Value) SEGIPlugin.QualityLevel.Value = newQualityLevel;

            if (SEGIPlugin.QualityLevel.Value == 4)
            {
                GUILayout.TextArea("Consumes additional 6.1gb of VRAM. Really better for screenshots than gameplay", _darkerBoxStyle);
            }

            if (SEGIPlugin.QualityLevel.Value >= 2)
            {
                GUILayout.Space(5);
                var currentDense = SEGIPlugin.DenseVoxelMode.Value;
                var newDense = GUILayout.Toggle(currentDense,
                    $"High Density Mode ({(currentDense ? "ON" : "OFF")})");
                if (newDense != currentDense) SEGIPlugin.DenseVoxelMode.Value = newDense;
                GUILayout.TextArea("Twice the GI detail but half the range.", _darkerBoxStyle);
                if (SEGIPlugin.DenseVoxelMode.Value)
                {
                    int maxStep = ConfigData.HighDensityRangeMaxStep;
                    var currentStep = SEGIPlugin.HighDensityRangeStep.Value;
                    var newStep = Mathf.RoundToInt(GUILayout.HorizontalSlider(currentStep, 0, maxStep));
                    float previewSize = ConfigData.PreviewVoxelSpaceSizeAtStep(newStep);
                    int previewRes = ConfigData.PreviewVoxelResolutionAtStep(newStep);
                    GUILayout.Label($"Range Step: {newStep}/{maxStep}  (space {previewSize:F0}, voxels {previewRes} - density stays 2x throughout)");
                    if (newStep != currentStep) SEGIPlugin.HighDensityRangeStep.Value = newStep;
                    GUILayout.TextArea("0 = off / max = full range at double density", _darkerBoxStyle);
                }
            }
            else if (SEGIPlugin.DenseVoxelMode.Value)
            {
                SEGIPlugin.DenseVoxelMode.Value = false;
            }

            GUILayout.EndVertical();

            GUILayout.Space(3);

            GUILayout.BeginVertical(_greenBoxStyle);
            GUILayout.Label("=== Gain Controls ===", _darkerBoxStyle, GUILayout.ExpandWidth(true));
            float giMultiplier = SEGIPlugin.UseGainMultiplier.Value ? 10f : 1f;
            GUILayout.Label($"Global Illumination Gain: {SEGIPlugin.GIGain.Value:F2}" +
                            (SEGIPlugin.UseGainMultiplier.Value ? $" (Applied: {SEGIPlugin.GIGain.Value * giMultiplier:F2})" : ""));
            var newGiGain = GUILayout.HorizontalSlider(SEGIPlugin.GIGain.Value, 0.0f, 20.0f);
            if (!Mathf.Approximately(newGiGain, SEGIPlugin.GIGain.Value)) SEGIPlugin.GIGain.Value = newGiGain;
            GUILayout.TextArea("Master brightness control for all global illumination effects.\nIncrease if lighting seems too dim.", _darkerBoxStyle);
            GUILayout.Label($"Emissive Light Gain: {SEGIPlugin.EmissiveLightGain.Value:F3}" +
                            (SEGIPlugin.UseGainMultiplier.Value ? $" (Applied: {SEGIPlugin.EmissiveLightGain.Value * giMultiplier:F2})" : ""));
            var newEmissiveLightGain = GUILayout.HorizontalSlider(SEGIPlugin.EmissiveLightGain.Value, 0.0f, 10.0f);
            if (!Mathf.Approximately(newEmissiveLightGain, SEGIPlugin.EmissiveLightGain.Value))
                SEGIPlugin.EmissiveLightGain.Value = newEmissiveLightGain;
            GUILayout.TextArea("Multiplier for emissive light contribution during voxelization.\nHigher values = brighter emissive lights in the scene.", _darkerBoxStyle);

            GUILayout.Label($"GI Red Gain: {SEGIPlugin.GIRedGain.Value:F2}");
            GUILayout.BeginHorizontal();
            var newGiRedGain = GUILayout.HorizontalSlider(SEGIPlugin.GIRedGain.Value, 0.0f, 4f);
            if (GUILayout.Button("Reset", GUILayout.Width(50)))
                newGiRedGain = 1.0f;
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(newGiRedGain, SEGIPlugin.GIRedGain.Value))
                SEGIPlugin.GIRedGain.Value = newGiRedGain;

            GUILayout.Label($"GI Green Gain: {SEGIPlugin.GIGreenGain.Value:F2}");
            GUILayout.BeginHorizontal();
            var newGiGreenGain = GUILayout.HorizontalSlider(SEGIPlugin.GIGreenGain.Value, 0.0f, 4f);
            if (GUILayout.Button("Reset", GUILayout.Width(50)))
                newGiGreenGain = 1.0f;
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(newGiGreenGain, SEGIPlugin.GIGreenGain.Value))
                SEGIPlugin.GIGreenGain.Value = newGiGreenGain;

            GUILayout.Label($"GI Blue Gain: {SEGIPlugin.GIBlueGain.Value:F2}");
            GUILayout.BeginHorizontal();
            var newGiBlueGain = GUILayout.HorizontalSlider(SEGIPlugin.GIBlueGain.Value, 0.0f, 4f);
            if (GUILayout.Button("Reset", GUILayout.Width(50)))
                newGiBlueGain = 1.0f;
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(newGiBlueGain, SEGIPlugin.GIBlueGain.Value))
                SEGIPlugin.GIBlueGain.Value = newGiBlueGain;
            GUILayout.TextArea("Extra per-channel amplification of the GI add-layer.\n1 = off. Use to balance color strength.", _darkerBoxStyle);

            GUILayout.Space(5);
            var currentMultiplier = SEGIPlugin.UseGainMultiplier.Value;
            var newMultiplier = GUILayout.Toggle(currentMultiplier, $"x10 Gain Multiplier ({currentMultiplier})");
            if (newMultiplier != currentMultiplier) SEGIPlugin.UseGainMultiplier.Value = newMultiplier;
            GUILayout.Space(5);
            GUILayout.Label($"Secondary Bounce Gain: {SEGIPlugin.SecondaryBounceGain.Value:F2}");
            var newSecondaryBounce = GUILayout.HorizontalSlider(SEGIPlugin.SecondaryBounceGain.Value, 0.0f, 1.0f);
            if (!Mathf.Approximately(newSecondaryBounce, SEGIPlugin.SecondaryBounceGain.Value))
                SEGIPlugin.SecondaryBounceGain.Value = newSecondaryBounce;
            GUILayout.TextArea("Controls secondary light bounces. Higher values = more light bouncing into shadowed areas.", _darkerBoxStyle);
            GUILayout.Space(5);

            GUILayout.Label($"GI Gamma (contribution): {SEGIPlugin.GIGamma.Value:F2}");
            GUILayout.BeginHorizontal();
            var newGiGamma = GUILayout.HorizontalSlider(SEGIPlugin.GIGamma.Value, 0.4f, 10f);
            if (GUILayout.Button("Reset", GUILayout.Width(50)))
                newGiGamma = 1.0f;
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(newGiGamma, SEGIPlugin.GIGamma.Value))
                SEGIPlugin.GIGamma.Value = newGiGamma;

            GUILayout.Label($"GI Highlight Gain: {SEGIPlugin.GIHighlightGain.Value:F2}");
            GUILayout.BeginHorizontal();
            var newGiHighlight = GUILayout.HorizontalSlider(SEGIPlugin.GIHighlightGain.Value, 0.0f, 20f);
            if (GUILayout.Button("Reset", GUILayout.Width(50)))
                newGiHighlight = 0.0f;
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(newGiHighlight, SEGIPlugin.GIHighlightGain.Value))
                SEGIPlugin.GIHighlightGain.Value = newGiHighlight;

            GUILayout.Label($"GI Toe / Shadow Crush: {SEGIPlugin.GIToe.Value:F3}");
            GUILayout.BeginHorizontal();
            var newGiToe = GUILayout.HorizontalSlider(SEGIPlugin.GIToe.Value, 0.0f, 0.2f);
            if (GUILayout.Button("Reset", GUILayout.Width(50)))
                newGiToe = 0.0f;
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(newGiToe, SEGIPlugin.GIToe.Value))
                SEGIPlugin.GIToe.Value = newGiToe;
            GUILayout.Space(5);
            var currentBubble = SEGIPlugin.EmissiveBubbleEnabled.Value;
            var newBubble = GUILayout.Toggle(currentBubble,
                $"Emissive Exclusion Bubble ({(currentBubble ? "ON" : "OFF")})");
            if (newBubble != currentBubble) SEGIPlugin.EmissiveBubbleEnabled.Value = newBubble;
            GUILayout.TextArea("Prevent held items and suit from contributing to GI.", _darkerBoxStyle);
            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.BeginVertical(_blueBoxStyle);
            if (GUILayout.Button($"=== Bloom / Screen === {(_showBloomSection ? "[-]" : "[+]")}", _darkerBoxStyle, GUILayout.ExpandWidth(true)))
                _showBloomSection = !_showBloomSection;
            if (_showBloomSection)
            {
                GUILayout.Label($"Bloom Mix: {SEGIPlugin.BloomMix.Value:F2}");
                GUILayout.BeginHorizontal();
                var newBloomMix = GUILayout.HorizontalSlider(SEGIPlugin.BloomMix.Value, 0.0f, 1.0f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newBloomMix = 0.0f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newBloomMix, SEGIPlugin.BloomMix.Value))
                    SEGIPlugin.BloomMix.Value = newBloomMix;

                GUILayout.Label($"Bloom Blur: {SEGIPlugin.BloomBlur.Value:F1}");
                GUILayout.BeginHorizontal();
                var newBloomBlur = GUILayout.HorizontalSlider(SEGIPlugin.BloomBlur.Value, 0.0f, 48f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newBloomBlur = 8.0f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newBloomBlur, SEGIPlugin.BloomBlur.Value))
                    SEGIPlugin.BloomBlur.Value = newBloomBlur;

                GUILayout.Label($"Bloom Gain: {SEGIPlugin.BloomGain.Value:F2}");
                GUILayout.BeginHorizontal();
                var newBloomGain = GUILayout.HorizontalSlider(SEGIPlugin.BloomGain.Value, 0.0f, 8f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newBloomGain = 1.0f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newBloomGain, SEGIPlugin.BloomGain.Value))
                    SEGIPlugin.BloomGain.Value = newBloomGain;

                GUILayout.Label($"Bloom Gamma: {SEGIPlugin.BloomGamma.Value:F2}");
                GUILayout.BeginHorizontal();
                var newBloomGamma = GUILayout.HorizontalSlider(SEGIPlugin.BloomGamma.Value, 0.2f, 5f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newBloomGamma = 1.0f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newBloomGamma, SEGIPlugin.BloomGamma.Value))
                    SEGIPlugin.BloomGamma.Value = newBloomGamma;

                GUILayout.Label($"Screen Gamma: {SEGIPlugin.ScreenGamma.Value:F2}");
                GUILayout.BeginHorizontal();
                var newScreenGamma = GUILayout.HorizontalSlider(SEGIPlugin.ScreenGamma.Value, 0.2f, 5f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newScreenGamma = 1.0f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newScreenGamma, SEGIPlugin.ScreenGamma.Value))
                    SEGIPlugin.ScreenGamma.Value = newScreenGamma;

                GUILayout.Space(5);
                GUILayout.Label($"Dither Strength: {SEGIPlugin.DitherStrength.Value:F2}");
                GUILayout.BeginHorizontal();
                var newDitherStrength = GUILayout.HorizontalSlider(SEGIPlugin.DitherStrength.Value, 0.0f, 2.0f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newDitherStrength = 0.0f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newDitherStrength, SEGIPlugin.DitherStrength.Value))
                    SEGIPlugin.DitherStrength.Value = newDitherStrength;
                GUILayout.TextArea("Blue-noise dither on the final frame to break up color\nbanding rings. 0 = off, 1 = one full 8-bit step.", _darkerBoxStyle);
            }
            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.BeginVertical(_purpleBoxStyle);
            if (GUILayout.Button($"=== Eye Adaptation === {(_showEyeAdaptationSection ? "[-]" : "[+]")}", _darkerBoxStyle, GUILayout.ExpandWidth(true)))
                _showEyeAdaptationSection = !_showEyeAdaptationSection;
            if (_showEyeAdaptationSection)
            {
                var currentEyeAdapt = SEGIPlugin.EyeAdaptationEnabled.Value;
                var newEyeAdapt = GUILayout.Toggle(currentEyeAdapt, $"Enable Eye Adaptation ({(currentEyeAdapt ? "ON" : "OFF")})");
                if (newEyeAdapt != currentEyeAdapt) SEGIPlugin.EyeAdaptationEnabled.Value = newEyeAdapt;
                GUILayout.TextArea("Fades a hidden GI Gain modifier based on measured screen\nbrightness. Your GI Gain slider itself is never changed.", _darkerBoxStyle);

                GUILayout.Space(5);
                GUILayout.Label($"Modifier at Min Brightness: {SEGIPlugin.EyeAdaptationModifierAtMinBrightness.Value:+0.00;-0.00;0.00}");
                GUILayout.BeginHorizontal();
                var newModMin = GUILayout.HorizontalSlider(SEGIPlugin.EyeAdaptationModifierAtMinBrightness.Value, -30f, 30f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newModMin = 2.0f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newModMin, SEGIPlugin.EyeAdaptationModifierAtMinBrightness.Value))
                    SEGIPlugin.EyeAdaptationModifierAtMinBrightness.Value = newModMin;

                GUILayout.Label($"Modifier at Max Brightness: {SEGIPlugin.EyeAdaptationModifierAtMaxBrightness.Value:+0.00;-0.00;0.00}");
                GUILayout.BeginHorizontal();
                var newModMax = GUILayout.HorizontalSlider(SEGIPlugin.EyeAdaptationModifierAtMaxBrightness.Value, -30f, 30f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newModMax = -2.0f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newModMax, SEGIPlugin.EyeAdaptationModifierAtMaxBrightness.Value))
                    SEGIPlugin.EyeAdaptationModifierAtMaxBrightness.Value = newModMax;

                GUILayout.Space(5);
                GUILayout.Label($"Fade Time at Min Brightness: {SEGIPlugin.EyeAdaptationFadeTimeAtMinBrightness.Value:F2}s");
                GUILayout.BeginHorizontal();
                var newTimeMin = GUILayout.HorizontalSlider(SEGIPlugin.EyeAdaptationFadeTimeAtMinBrightness.Value, 0.1f, 15f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newTimeMin = 4.0f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newTimeMin, SEGIPlugin.EyeAdaptationFadeTimeAtMinBrightness.Value))
                    SEGIPlugin.EyeAdaptationFadeTimeAtMinBrightness.Value = newTimeMin;

                GUILayout.Label($"Fade Time at Max Brightness: {SEGIPlugin.EyeAdaptationFadeTimeAtMaxBrightness.Value:F2}s");
                GUILayout.BeginHorizontal();
                var newTimeMax = GUILayout.HorizontalSlider(SEGIPlugin.EyeAdaptationFadeTimeAtMaxBrightness.Value, 0.1f, 15f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newTimeMax = 1.5f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newTimeMax, SEGIPlugin.EyeAdaptationFadeTimeAtMaxBrightness.Value))
                    SEGIPlugin.EyeAdaptationFadeTimeAtMaxBrightness.Value = newTimeMax;

                GUILayout.Space(5);
                GUILayout.Label($"Sensitivity: {SEGIPlugin.EyeAdaptationSensitivity.Value:F2}");
                GUILayout.BeginHorizontal();
                var newSensitivity = GUILayout.HorizontalSlider(SEGIPlugin.EyeAdaptationSensitivity.Value, 0.1f, 10f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newSensitivity = 1.0f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newSensitivity, SEGIPlugin.EyeAdaptationSensitivity.Value))
                    SEGIPlugin.EyeAdaptationSensitivity.Value = newSensitivity;
                GUILayout.TextArea("Multiplies measured brightness before Min/Max. Turn up if\nit under-reacts to what you see; down if it overreacts.", _darkerBoxStyle);

                GUILayout.Space(5);
                GUILayout.Label($"Screen Gamma Modifier at Min Brightness: {SEGIPlugin.EyeAdaptationScreenGammaModifierAtMinBrightness.Value:+0.00;-0.00;0.00}");
                GUILayout.BeginHorizontal();
                var newGammaModMin = GUILayout.HorizontalSlider(SEGIPlugin.EyeAdaptationScreenGammaModifierAtMinBrightness.Value, -0.8f, 4f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newGammaModMin = 0f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newGammaModMin, SEGIPlugin.EyeAdaptationScreenGammaModifierAtMinBrightness.Value))
                    SEGIPlugin.EyeAdaptationScreenGammaModifierAtMinBrightness.Value = newGammaModMin;

                GUILayout.Label($"Screen Gamma Modifier at Max Brightness: {SEGIPlugin.EyeAdaptationScreenGammaModifierAtMaxBrightness.Value:+0.00;-0.00;0.00}");
                GUILayout.BeginHorizontal();
                var newGammaModMax = GUILayout.HorizontalSlider(SEGIPlugin.EyeAdaptationScreenGammaModifierAtMaxBrightness.Value, -0.8f, 4f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newGammaModMax = 0f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newGammaModMax, SEGIPlugin.EyeAdaptationScreenGammaModifierAtMaxBrightness.Value))
                    SEGIPlugin.EyeAdaptationScreenGammaModifierAtMaxBrightness.Value = newGammaModMax;
                GUILayout.TextArea("Added to Screen Gamma (which you should leave at 1) by the\nsame brightness fade above. 0/0 = inactive, the default.", _darkerBoxStyle);

                GUILayout.TextArea("Both value and timing blend by measured brightness alone -\nneither slider is fixed as \"the dark one\" or \"the bright one\".\nMetering favors the center of the screen.", _darkerBoxStyle);
            }
            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.BeginVertical(_orangeBoxStyle);
            if (GUILayout.Button($"=== Flashlight Fill Light === {(_showFlashlightFillSection ? "[-]" : "[+]")}", _darkerBoxStyle, GUILayout.ExpandWidth(true)))
                _showFlashlightFillSection = !_showFlashlightFillSection;
            if (_showFlashlightFillSection)
            {
                GUILayout.Label($"Strength: {SEGIPlugin.FlashlightFillPower.Value:F2}");
                GUILayout.BeginHorizontal();
                var newFillPower = GUILayout.HorizontalSlider(SEGIPlugin.FlashlightFillPower.Value, 0f, 5f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newFillPower = 0f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newFillPower, SEGIPlugin.FlashlightFillPower.Value))
                    SEGIPlugin.FlashlightFillPower.Value = newFillPower;

                GUILayout.Label($"Falloff Range: {SEGIPlugin.FlashlightFillRange.Value:F2}");
                GUILayout.BeginHorizontal();
                var newFillRange = GUILayout.HorizontalSlider(SEGIPlugin.FlashlightFillRange.Value, 0.5f, 10f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newFillRange = 3f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newFillRange, SEGIPlugin.FlashlightFillRange.Value))
                    SEGIPlugin.FlashlightFillRange.Value = newFillRange;

                GUILayout.TextArea("Fake white point light at your active flashlight/headlamp.\nNot real GI. Strength 0 disables it entirely.", _darkerBoxStyle);
            }
            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.BeginVertical(_orangeBoxStyle);
            if (GUILayout.Button($"=== Lamp Fill Light === {(_showLampFillSection ? "[-]" : "[+]")}", _darkerBoxStyle, GUILayout.ExpandWidth(true)))
                _showLampFillSection = !_showLampFillSection;
            if (_showLampFillSection)
            {
                GUILayout.Label($"Strength: {SEGIPlugin.LampFillPower.Value:F2}");
                GUILayout.BeginHorizontal();
                var newLampFillPower = GUILayout.HorizontalSlider(SEGIPlugin.LampFillPower.Value, 0f, 5f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newLampFillPower = 0f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newLampFillPower, SEGIPlugin.LampFillPower.Value))
                    SEGIPlugin.LampFillPower.Value = newLampFillPower;

                GUILayout.Label($"Falloff Range: {SEGIPlugin.LampFillRange.Value:F2}");
                GUILayout.BeginHorizontal();
                var newLampFillRange = GUILayout.HorizontalSlider(SEGIPlugin.LampFillRange.Value, 0.5f, 10f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newLampFillRange = 3f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newLampFillRange, SEGIPlugin.LampFillRange.Value))
                    SEGIPlugin.LampFillRange.Value = newLampFillRange;

                GUILayout.Space(5);
                var currentOrtho = SEGIPlugin.LampFillOrthogonal.Value;
                var newOrtho = GUILayout.Toggle(currentOrtho, $"Orthogonal Ring Lights ({(currentOrtho ? "ON" : "OFF")})");
                if (newOrtho != currentOrtho) SEGIPlugin.LampFillOrthogonal.Value = newOrtho;
                var currentDiag = SEGIPlugin.LampFillDiagonal.Value;
                var newDiag = GUILayout.Toggle(currentDiag, $"Diagonal Ring Lights ({(currentDiag ? "ON" : "OFF")})");
                if (newDiag != currentDiag) SEGIPlugin.LampFillDiagonal.Value = newDiag;

                GUILayout.TextArea("Rings every active stationary lamp with up to 8 fake white\npoint lights to fake bounce light SEGI fails to route around\nnearby obstacles. Not real GI. Strength 0, or both ring\ncheckboxes off, disables it entirely.", _darkerBoxStyle);
            }
            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.BeginVertical(_purpleBoxStyle);
            if (GUILayout.Button($"=== Advanced === {(_showAdvanced ? "[-]" : "[+]")}", _darkerBoxStyle, GUILayout.ExpandWidth(true)))
                _showAdvanced = !_showAdvanced;
            if (_showAdvanced)
            {
                var currentLightweight = SEGIPlugin.LightweightMode.Value;
                var newLightweight = GUILayout.Toggle(currentLightweight, $"**Lightweight Mode** ({currentLightweight})");
                if (newLightweight != currentLightweight) SEGIPlugin.LightweightMode.Value = newLightweight;
                GUILayout.TextArea("Only renders emissive objects during voxelization. Faster but causes light leakage.\nYou probably will want to lower the main GI Gain with this enabled", _darkerBoxStyle);

                GUILayout.Space(10);
                var currentForwardBias = SEGIPlugin.ForwardOriginBias.Value;
                var newForwardBias = GUILayout.Toggle(currentForwardBias, $"Forward Origin Bias ({(currentForwardBias ? "ON" : "OFF")})");
                if (newForwardBias != currentForwardBias) SEGIPlugin.ForwardOriginBias.Value = newForwardBias;
                GUILayout.TextArea("Shifts the voxel volume 25% forward in the\ndirection you're looking.", _darkerBoxStyle);

                GUILayout.Space(10);
                var currentAdaptive = SEGIPlugin.AdaptivePerformance.Value;
                var newAdaptive = GUILayout.Toggle(currentAdaptive, $"Enable Adaptive Performance ({currentAdaptive})");
                if (newAdaptive != currentAdaptive) SEGIPlugin.AdaptivePerformance.Value = newAdaptive;
                if (SEGIPlugin.AdaptivePerformance.Value)
                {
                    GUILayout.Space(5);
                    GUILayout.Label($"Target Framerate: {SEGIPlugin.TargetFramerate.Value} FPS");
                    var newTargetFPS = Mathf.RoundToInt(GUILayout.HorizontalSlider(SEGIPlugin.TargetFramerate.Value, 15, 240));
                    if (newTargetFPS != SEGIPlugin.TargetFramerate.Value) SEGIPlugin.TargetFramerate.Value = newTargetFPS;
                    GUILayout.TextArea("The system will try to adjust SEGI Plus to stay around this framerate", _darkerBoxStyle);

                    GUILayout.Space(5);
                    GUILayout.Label($"Adaptive Strategy: {SEGIPlugin.AdaptiveStrategy.Value} ({GetStratName(SEGIPlugin.AdaptiveStrategy.Value)})");
                    var newStrategy = Mathf.RoundToInt(GUILayout.HorizontalSlider(SEGIPlugin.AdaptiveStrategy.Value, 0, 1));
                    if (newStrategy != SEGIPlugin.AdaptiveStrategy.Value) SEGIPlugin.AdaptiveStrategy.Value = newStrategy;
                    GUILayout.TextArea(GetStratDescription(SEGIPlugin.AdaptiveStrategy.Value), _darkerBoxStyle);

                    if (SEGIPlugin.AdaptiveStrategy.Value == 1)
                    {
                        GUILayout.Space(5);
                        GUILayout.Label($"Min Distance: {SEGIPlugin.AdaptiveMinDistancePercent.Value:F0}% of max");
                        var newMinDistPercent = GUILayout.HorizontalSlider(SEGIPlugin.AdaptiveMinDistancePercent.Value, 10f, 100f);
                        if (!Mathf.Approximately(newMinDistPercent, SEGIPlugin.AdaptiveMinDistancePercent.Value))
                            SEGIPlugin.AdaptiveMinDistancePercent.Value = newMinDistPercent;
                        GUILayout.TextArea("What's the minimum distance we can use? Lower % = closer but also faster", _darkerBoxStyle);
                    }
                }
                GUILayout.Space(10);

                GUILayout.Label($"Occlusion Strength: {SEGIPlugin.OcclusionStrengthOffset.Value:+0.00;-0.00;0.00}");
                GUILayout.BeginHorizontal();
                var newOccStrOffset = GUILayout.HorizontalSlider(SEGIPlugin.OcclusionStrengthOffset.Value, -0.35f, 0.75f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newOccStrOffset = 0f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newOccStrOffset, SEGIPlugin.OcclusionStrengthOffset.Value))
                    SEGIPlugin.OcclusionStrengthOffset.Value = newOccStrOffset;
                GUILayout.TextArea("How strongly geometry stops GI. Higher reduces light\nleaking through walls but also darkens scene.", _darkerBoxStyle);
                GUILayout.Space(5);
                GUILayout.Label($"Cone Trace Bias: {SEGIPlugin.ConeTraceBiasOffset.Value:+0.00;-0.00;0.00}");
                GUILayout.BeginHorizontal();
                var newBiasOffset = GUILayout.HorizontalSlider(SEGIPlugin.ConeTraceBiasOffset.Value, -0.3f, 0.6f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newBiasOffset = 0f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newBiasOffset, SEGIPlugin.ConeTraceBiasOffset.Value))
                    SEGIPlugin.ConeTraceBiasOffset.Value = newBiasOffset;
                GUILayout.TextArea("How far from surfaces GI probes sample, Lower = more\nself-occlusion. Higher = more light leakage.", _darkerBoxStyle);
                GUILayout.Space(5);
                GUILayout.Label($"Inner Occlusion Layers: {SEGIPlugin.InnerOcclusionLayers.Value}");
                GUILayout.BeginHorizontal();
                var newInnerOcclusionLayers = Mathf.RoundToInt(GUILayout.HorizontalSlider(SEGIPlugin.InnerOcclusionLayers.Value, 0, 4));
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newInnerOcclusionLayers = 1;
                GUILayout.EndHorizontal();
                if (newInnerOcclusionLayers != SEGIPlugin.InnerOcclusionLayers.Value)
                    SEGIPlugin.InnerOcclusionLayers.Value = newInnerOcclusionLayers;
                GUILayout.TextArea("Thickens near occluders in the voxel grid by this many\nlayers so thin/small objects block GI instead of leaking\nlight around them. 1 = default.", _darkerBoxStyle);
            }
            GUILayout.EndVertical();

            #if SEGI_PROFILER
                GUILayout.Space(10);
                DrawDebugOverridesSection();
                GUILayout.Space(10);
                DrawProfilerSection();
                GUILayout.Space(10);
            #endif

            GUILayout.Space(8);
            if (GUILayout.Button("apply Gcotu defaults", GUILayout.Height(28)))
                ApplyPersonalDefaults();

            GUILayout.Label("Press F11 to toggle this menu", GUILayout.ExpandWidth(true));
            GUILayout.Label("All changes are saved automatically", GUILayout.ExpandWidth(true));

        GUILayout.EndScrollView();
        GUILayout.EndVertical();

        GUI.DragWindow();
    }

    #if SEGI_PROFILER
        private GUIStyle _debugBoxStyle;

        private void DrawDebugOverridesSection()
        {
            if (_debugBoxStyle == null)
                _debugBoxStyle = MakeStyle(new Color(0.1f, 0.35f, 0.4f, 0.8f), Color.white);

            GUILayout.BeginVertical(_debugBoxStyle);
            GUILayout.Label("=== Debug Overrides (Dev Only) ===", _darkerBoxStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label("Null = use hardcoded default. Drag slider to override.", _darkerBoxStyle);

            GUILayout.Space(5);
            GUILayout.Label("— Temporal —");
            DebugOverrides.TemporalBlendWeight = DrawNullableFloat("Blend Weight", DebugOverrides.TemporalBlendWeight, 0.01f, 0.005f, 0.25f);
            DebugOverrides.DisocclusionSensitivity = DrawNullableFloat("Disocclusion Sens.", DebugOverrides.DisocclusionSensitivity, 5.0f, 1f, 20f);
            DebugOverrides.MotionBlendMax = DrawNullableFloat("Motion Blend Max", DebugOverrides.MotionBlendMax, 0.25f, 0.1f, 0.5f);

            GUILayout.Space(5);
            GUILayout.Label("— Cone Tracing —");
            DebugOverrides.ConeLength = DrawNullableFloat("Length", DebugOverrides.ConeLength, ConfigData.ConeLength, 0.5f, 3.0f);
            DebugOverrides.ConeWidth = DrawNullableFloat("Width", DebugOverrides.ConeWidth, ConfigData.ConeWidth, 1.0f, 12.0f);
            DebugOverrides.ConeTraceBias = DrawNullableFloat("Cone Trace Bias", DebugOverrides.ConeTraceBias, ConfigData.ConeTraceBias, 0.0f, 2.0f);

            GUILayout.Space(5);
            GUILayout.Label("— Visual Tuning —");
            DebugOverrides.OcclusionStrength = DrawNullableFloat("Occlusion Strength", DebugOverrides.OcclusionStrength, 0.86f, 0f, 2f);
            DebugOverrides.NearOcclusionStrength = DrawNullableFloat("Near Occlusion", DebugOverrides.NearOcclusionStrength, ConfigData.NearOcclusionStrength, 0f, 2f);

            GUILayout.Space(5);
            GUILayout.Label("— Sun Shadows —");
            DebugOverrides.SunShadowSoftness = DrawNullableFloat("Shadow Softness", DebugOverrides.SunShadowSoftness, 150.0f, 50f, 1000f);
            DebugOverrides.SunShadowResolution = DrawNullableInt("Shadow Resolution", DebugOverrides.SunShadowResolution, 256, 64, 1024);

            GUILayout.Space(5);
            GUILayout.Label("— Voxelization —");
            DebugOverrides.EmissiveTemporalBlend = DrawNullableFloat("Emissive Temporal Blend", DebugOverrides.EmissiveTemporalBlend, 0.6f, 0.05f, 1.0f);
            DebugOverrides.ForwardOriginBias = DrawNullableBool("Forward Origin Bias (25%)", DebugOverrides.ForwardOriginBias);

            GUILayout.Space(5);
            if (GUILayout.Button("Reset All to Defaults", GUILayout.Height(25)))
            {
                DebugOverrides.TemporalBlendWeight = null;
                DebugOverrides.DisocclusionSensitivity = null;
                DebugOverrides.MotionBlendMax = null;
                DebugOverrides.ConeLength = null;
                DebugOverrides.ConeWidth = null;
                DebugOverrides.ConeTraceBias = null;
                DebugOverrides.OcclusionStrength = null;
                DebugOverrides.NearOcclusionStrength = null;
                DebugOverrides.SunShadowSoftness = null;
                DebugOverrides.SunShadowResolution = null;
                DebugOverrides.EmissiveTemporalBlend = null;
                DebugOverrides.ForwardOriginBias = null;
            }

            GUILayout.EndVertical();
        }

        private float? DrawNullableFloat(string label, float? current, float defaultVal, float min, float max)
        {
            GUILayout.BeginHorizontal();
            bool active = current.HasValue;
            float displayVal = current ?? defaultVal;
            GUILayout.Label($"{label}: {displayVal:F3}{(active ? "" : " (default)")}", GUILayout.Width(260));
            float newVal = GUILayout.HorizontalSlider(displayVal, min, max);
            bool reset = active && GUILayout.Button("×", GUILayout.Width(22));
            GUILayout.EndHorizontal();
            if (reset) return null;
            if (!Mathf.Approximately(newVal, displayVal)) return newVal;
            return current;
        }

        private int? DrawNullableInt(string label, int? current, int defaultVal, int min, int max)
        {
            GUILayout.BeginHorizontal();
            bool active = current.HasValue;
            int displayVal = current ?? defaultVal;
            GUILayout.Label($"{label}: {displayVal}{(active ? "" : " (default)")}", GUILayout.Width(260));
            int newVal = Mathf.RoundToInt(GUILayout.HorizontalSlider(displayVal, min, max));
            bool reset = active && GUILayout.Button("×", GUILayout.Width(22));
            GUILayout.EndHorizontal();
            if (reset) return null;
            if (newVal != displayVal) return newVal;
            return current;
        }

        private bool? DrawNullableBool(string label, bool? current)
        {
            GUILayout.BeginHorizontal();
            bool active = current.HasValue;
            string state = active ? (current.Value ? "ON" : "OFF") : "default";
            GUILayout.Label($"{label}: {state}", GUILayout.Width(260));
            bool clicked = GUILayout.Button(active ? (current.Value ? "ON" : "OFF") : "—", GUILayout.Width(50));
            GUILayout.EndHorizontal();
            if (clicked)
            {
                if (!active) return false;
                if (!current.Value) return true;
                return null; // cycle: null → false → true → null
            }
            return current;
        }

        private void DrawProfilerSection()
        {
            if (_profilerBoxStyle == null)
                _profilerBoxStyle = MakeStyle(new Color(0.4f, 0.1f, 0.5f, 0.8f), Color.white);

            GUILayout.BeginVertical(_profilerBoxStyle);
            GUILayout.Label("=== Profiler ===", _darkerBoxStyle, GUILayout.ExpandWidth(true));

            var segi = Camera.main != null ? Camera.main.GetComponent<SEGIStationeers>() : null;
            if (segi == null)
            {
                GUILayout.Label("SEGI component not found on camera.");
                GUILayout.EndVertical();
                return;
            }

            var profiler = segi.Profiler;

            switch (profiler.State)
            {
                case SEGIProfiler.ProfileState.Idle:
                    GUILayout.TextArea("Profiler",_darkerBoxStyle);
                    GUILayout.Space(3);
                    if (GUILayout.Button("Start Profiling", GUILayout.Height(35)))
                    {
                        profiler.StartProfiling();
                    }
                    if (profiler.HasResults)
                    {
                        GUILayout.Space(3);
                        GUILayout.Label("Results:");
                        DrawProfilerResults(profiler);
                    }
                    break;

                case SEGIProfiler.ProfileState.Running:
                    GUILayout.Label(profiler.StatusMessage);

                    float progress = profiler.GetProgress();
                    Rect progressRect = GUILayoutUtility.GetRect(GUIContent.none, _darkerBoxStyle,
                        GUILayout.Height(24), GUILayout.ExpandWidth(true));

                    GUI.Box(progressRect, "");

                    Color oldBg = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.2f, 0.7f, 0.3f, 1f);
                    GUI.Box(new Rect(progressRect.x, progressRect.y,
                        progressRect.width * progress, progressRect.height), "");
                    GUI.backgroundColor = oldBg;

                    float stepWidth = progressRect.width / profiler.StepCount;
                    for (int i = 1; i < profiler.StepCount; i++)
                    {
                        float x = progressRect.x + stepWidth * i;
                        GUI.DrawTexture(new Rect(x, progressRect.y, 1, progressRect.height),
                            Texture2D.whiteTexture);
                    }

                    GUI.Label(progressRect,
                        $"Step {profiler.CurrentStepIndex + 1}/{profiler.StepCount}  —  " +
                        $"{progress * 100f:F0}%  ({profiler.TotalFrameCount - (int)(profiler.TotalFrameCount * progress)} frames left)",
                        new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });

                    GUILayout.Space(3);
                    if (GUILayout.Button("Cancel"))
                    {
                        profiler.Cancel();
                    }
                    break;

                case SEGIProfiler.ProfileState.Complete:
                    GUILayout.Label("Profiling complete!");
                    DrawProfilerResults(profiler);
                    GUILayout.Space(3);
                    if (GUILayout.Button("Run Again", GUILayout.Height(30)))
                    {
                        profiler.StartProfiling();
                    }
                    break;
            }

            GUILayout.EndVertical();
        }

        private void DrawProfilerResults(SEGIProfiler profiler)
        {
            string results = profiler.GetResultsTable();

            _profilerResultsScroll = GUILayout.BeginScrollView(_profilerResultsScroll,
                GUILayout.Height(350), GUILayout.ExpandWidth(true));

            GUIStyle monoStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = false,
                richText = false
            };
            try
            {
                var font = Font.CreateDynamicFontFromOSFont("Consolas", 12);
                if (font != null) monoStyle.font = font;
            }
            catch { }

            GUILayout.Label(results, monoStyle);
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy"))
            {
                GUIUtility.systemCopyBuffer = results;
            }
            GUILayout.EndHorizontal();
        }
    #endif

    private void OnDestroy()
    {
        bool wasOpenInGameWorld = _showConfig && (SEGIPlugin.Instance?.IsInGameWorld() ?? false);
        _showConfig = false; // clears IsOpen first so the cursor-lock guard below doesn't block this
        if (wasOpenInGameWorld)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // Some interact/look input isn't gated on cursor lock state, so game code can still
    // try to (re)lock the cursor on a click while our menu is open - that snaps the OS
    // cursor to screen center the instant it happens, and reverting lockState afterward
    // can't undo the snap. This stops the lock from being engaged in the first place
    // instead of reacting after the fact.
    [HarmonyPatch(typeof(Cursor), nameof(Cursor.lockState), MethodType.Setter)]
    private static class CursorLockGuard
    {
        private static void Prefix(ref CursorLockMode value)
        {
            if (IsOpen && value != CursorLockMode.None)
                value = CursorLockMode.None;
        }
    }

    private void ApplyPersonalDefaults()
    {
        // Enable off first so heavy settings cannot bite until he turns GI on.
        SEGIPlugin.Enabled.Value = false;
        SEGIPlugin.QualityLevel.Value = 3; // Extreme
        SEGIPlugin.DenseVoxelMode.Value = true;
        SEGIPlugin.HighDensityRangeStep.Value = 0;
        // Leave Lightweight Mode / Forward Origin Bias / Adaptive Performance alone.

        SEGIPlugin.GIGain.Value = 20f;
        SEGIPlugin.UseGainMultiplier.Value = false;
        SEGIPlugin.EmissiveLightGain.Value = 0.23f;
        SEGIPlugin.SecondaryBounceGain.Value = 0.8f;
        SEGIPlugin.EmissiveBubbleEnabled.Value = false;

        SEGIPlugin.GIRedGain.Value = 1f;
        SEGIPlugin.GIGreenGain.Value = 1f;
        SEGIPlugin.GIBlueGain.Value = 1.5f;

        SEGIPlugin.GIGamma.Value = 0.8f;
        SEGIPlugin.GIHighlightGain.Value = 20.0f;
        SEGIPlugin.GIToe.Value = 0.008f;

        SEGIPlugin.BloomMix.Value = 0f;
        SEGIPlugin.BloomBlur.Value = 23f;
        SEGIPlugin.BloomGain.Value = 1.23f;
        SEGIPlugin.BloomGamma.Value = 2.3f;
        SEGIPlugin.ScreenGamma.Value = 1.0f;
        SEGIPlugin.DitherStrength.Value = 0.5f;

        SEGIPlugin.OcclusionStrengthOffset.Value = 0.13f;
        SEGIPlugin.ConeTraceBiasOffset.Value = 0.13f;

        SEGIPlugin.FlashlightFillPower.Value = 0.5f;
        SEGIPlugin.FlashlightFillRange.Value = 5f;

        SEGIPlugin.LampFillPower.Value = 0.3f;
        SEGIPlugin.LampFillRange.Value = 3.0f;
        SEGIPlugin.LampFillOrthogonal.Value = true;
        SEGIPlugin.LampFillDiagonal.Value = true;

        SEGIPlugin.EyeAdaptationEnabled.Value = true;
        SEGIPlugin.EyeAdaptationModifierAtMinBrightness.Value = -4f;
        SEGIPlugin.EyeAdaptationModifierAtMaxBrightness.Value = -30f;
        SEGIPlugin.EyeAdaptationFadeTimeAtMinBrightness.Value = 3f;
        SEGIPlugin.EyeAdaptationFadeTimeAtMaxBrightness.Value = 0.1f;
        SEGIPlugin.EyeAdaptationSensitivity.Value = 2f;
        SEGIPlugin.EyeAdaptationScreenGammaModifierAtMinBrightness.Value = -0.3f;
        SEGIPlugin.EyeAdaptationScreenGammaModifierAtMaxBrightness.Value = 1.3f;
    }

    private void InitializeStyles()
    {
        if (_stylesInitialized) return;

        // All section panels share one dark gray; nested headers/help boxes get an even
        // darker gray; the window body itself gets a dark gray a bit brighter than the panels.
        Color panelGray = new Color(0.16f, 0.16f, 0.16f, 0.9f);
        Color darkerGray = new Color(0.08f, 0.08f, 0.08f, 0.9f);
        Color windowGray = new Color(0.22f, 0.22f, 0.22f, 1f);

        _orangeBoxStyle = MakeStyle(panelGray, Color.white);
        _blueBoxStyle = MakeStyle(panelGray, Color.white);
        _greenBoxStyle = MakeStyle(panelGray, Color.white);
        _purpleBoxStyle = MakeStyle(panelGray, Color.white);
        _darkerBoxStyle = MakeStyle(darkerGray, Color.white);

        _enableOnStyle = MakeStyle(new Color(0.10f, 0.45f, 0.12f, 1f), Color.white);
        _enableOnStyle.fontSize = 18;
        _enableOffStyle = MakeStyle(new Color(0.3f, 0.3f, 0.3f, 1f), Color.white);
        _enableOffStyle.fontSize = 18;

        Texture2D windowTex = WhyDoIhaveToCreateADamnTextureForThisToWorkGuh(windowGray);
        _windowBoxStyle = new GUIStyle(GUI.skin.window);
        _windowBoxStyle.normal.background = windowTex;
        _windowBoxStyle.onNormal.background = windowTex;
        _windowBoxStyle.normal.textColor = Color.white;
        _windowBoxStyle.onNormal.textColor = Color.white;

        _stylesInitialized = true;
    }

    private GUIStyle MakeStyle(Color backgroundColor, Color textColor)
    {
        var style = new GUIStyle(GUI.skin.box);
        style.normal.background = WhyDoIhaveToCreateADamnTextureForThisToWorkGuh(backgroundColor);
        style.normal.textColor = textColor;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;
        return style;
    }

    private Texture2D WhyDoIhaveToCreateADamnTextureForThisToWorkGuh(Color color)
    {
        Color[] pixels = new Color[4];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = color;

        Texture2D texture = new Texture2D(2, 2);
        // Without this, Unity's scene-load asset cleanup can reclaim this texture out from
        // under the still-alive GUIStyle referencing it (the mod's own GameObject, and so
        // this component's _stylesInitialized flag, survives world-to-world) - the F11 panel
        // backgrounds then render as fully transparent starting with the second world loaded
        // in a session. Every RenderTexture elsewhere in this project already sets this flag.
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private string GetStratName(int strategy)
    {
        return strategy switch
        {
            0 => "Balanced",
            1 => "Reduce Distance First",
            _ => "Unknown"
        };
    }

    private string GetStratDescription(int strategy)
    {
        return strategy switch
        {
            0 => "Balanced: Scales all settings proportionally as in experimental",
            1 => "Reduce Distance First: Keeps quality and reduces distance of global illumination first",
            _ => "Unknown"
        };
    }
}