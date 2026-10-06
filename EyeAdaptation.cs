using UnityEngine;
using UnityEngine.Rendering;

namespace BeefsSEGIPlus
{
    // Auto-exposure-style GI Gain modifier. Measures average screen brightness via a
    // cheap iterative downsample + AsyncGPUReadback (non-blocking - no CPU stall, the
    // exact problem this project's tone-mapping already hit once and fixed by moving
    // to GPU compute), then fades a runtime-only modifier toward a target defined by
    // two user sliders, at a rate defined by two more. Both value and timing are
    // linearly interpolated by the same measured-brightness fraction, so neither
    // slider is hardcoded as "the dark one" or "the bright one" - the user's own
    // slider values decide what happens at each end and how fast.
    internal static class EyeAdaptation
    {
        private const int MeasureEveryNFrames = 6;
        private const int MinMipSize = 4;
        // Metering window: center 50% width/height (25% of screen area) rather than
        // the whole frame, so a bright thing you're looking at isn't diluted by a
        // dark surround - a small area you're staring straight at is what actually
        // matters for perceived brightness, not the average of the whole view.
        private const float CenterCropFraction = 0.5f;
        // Calibration only: maps measured log2 luminance (after the Sensitivity
        // multiplier) to the 0..1 brightness fraction used to blend the user's
        // sliders. Not user-exposed. Believe, not verified - will likely need live
        // tuning once seen in-game.
        private const float LogLumaMin = -6f;
        private const float LogLumaMax = 2f;

        private static RenderTexture[] _mips;
        private static int _sourceW, _sourceH;
        private static bool _readbackPending;
        private static int _frameCounter;
        private static float _measuredBrightness01 = 0.5f;
        private static float _currentModifier;
        private static float _currentGammaModifier;

        public static float CurrentModifier =>
            (SEGIPlugin.EyeAdaptationEnabled?.Value ?? false) ? _currentModifier : 0f;

        // Same measured-brightness signal and fade timing as the GI Gain modifier above,
        // just blended toward a second pair of user sliders that target Screen Gamma
        // instead. Defaults to 0 at both ends, so it's inert until he sets one.
        public static float CurrentGammaModifier =>
            (SEGIPlugin.EyeAdaptationEnabled?.Value ?? false) ? _currentGammaModifier : 0f;

        public static void Tick(RenderTexture finalComposite)
        {
            if (finalComposite == null) return;

            _frameCounter++;
            if (!_readbackPending && _frameCounter % MeasureEveryNFrames == 0)
            {
                try { RequestMeasurement(finalComposite); }
                catch (System.Exception ex)
                {
                    SEGIPlugin.Log?.LogWarning("Eye adaptation measurement failed; skipping this round. " + ex.Message);
                    _readbackPending = false;
                }
            }

            float modAtMin = SEGIPlugin.EyeAdaptationModifierAtMinBrightness?.Value ?? 0f;
            float modAtMax = SEGIPlugin.EyeAdaptationModifierAtMaxBrightness?.Value ?? 0f;
            float timeAtMin = SEGIPlugin.EyeAdaptationFadeTimeAtMinBrightness?.Value ?? 4f;
            float timeAtMax = SEGIPlugin.EyeAdaptationFadeTimeAtMaxBrightness?.Value ?? 1.5f;

            float target = Mathf.Lerp(modAtMin, modAtMax, _measuredBrightness01);
            float tau = Mathf.Max(0.05f, Mathf.Lerp(timeAtMin, timeAtMax, _measuredBrightness01));
            float alpha = 1f - Mathf.Exp(-Time.deltaTime / tau);
            _currentModifier = Mathf.Lerp(_currentModifier, target, alpha);

            float gammaModAtMin = SEGIPlugin.EyeAdaptationScreenGammaModifierAtMinBrightness?.Value ?? 0f;
            float gammaModAtMax = SEGIPlugin.EyeAdaptationScreenGammaModifierAtMaxBrightness?.Value ?? 0f;
            float gammaTarget = Mathf.Lerp(gammaModAtMin, gammaModAtMax, _measuredBrightness01);
            _currentGammaModifier = Mathf.Lerp(_currentGammaModifier, gammaTarget, alpha);
        }

        private static readonly Vector2 CenterCropScale = new Vector2(CenterCropFraction, CenterCropFraction);
        private static readonly Vector2 CenterCropOffset = new Vector2((1f - CenterCropFraction) * 0.5f, (1f - CenterCropFraction) * 0.5f);

        private static void RequestMeasurement(RenderTexture src)
        {
            int cropW = Mathf.Max(1, Mathf.RoundToInt(src.width * CenterCropFraction));
            int cropH = Mathf.Max(1, Mathf.RoundToInt(src.height * CenterCropFraction));
            EnsureMipChain(cropW, cropH);
            if (_mips == null || _mips.Length == 0) return;

            // First step also crops to the center window; the rest is a plain halving chain.
            Graphics.Blit(src, _mips[0], CenterCropScale, CenterCropOffset);
            RenderTexture current = _mips[0];
            for (int i = 1; i < _mips.Length; i++)
            {
                Graphics.Blit(current, _mips[i]);
                current = _mips[i];
            }

            _readbackPending = true;
            AsyncGPUReadback.Request(current, 0, TextureFormat.RGBAFloat, OnReadbackComplete);
        }

        private static void OnReadbackComplete(AsyncGPUReadbackRequest request)
        {
            _readbackPending = false;
            if (request.hasError) return;

            var data = request.GetData<Color>();
            if (data.Length == 0) return;

            float sum = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                Color c = data[i];
                sum += 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            }
            float luminance = sum / data.Length;
            float sensitivity = SEGIPlugin.EyeAdaptationSensitivity?.Value ?? 1f;
            float logLuma = Mathf.Log(Mathf.Max(luminance * sensitivity, 0.0001f), 2f);
            _measuredBrightness01 = Mathf.InverseLerp(LogLumaMin, LogLumaMax, logLuma);
        }

        // w/h here is the size of the crop window (already reduced from the source),
        // not the full source resolution - the chain only needs to cover that.
        private static void EnsureMipChain(int w, int h)
        {
            if (_mips != null && _sourceW == w && _sourceH == h) return;
            ReleaseMipChain();
            _sourceW = w;
            _sourceH = h;

            var sizes = new System.Collections.Generic.List<Vector2Int> { new Vector2Int(w, h) };
            int mw = w;
            int mh = h;
            for (int i = 0; i < 12 && (mw > MinMipSize || mh > MinMipSize); i++)
            {
                mw = Mathf.Max(1, mw / 2);
                mh = Mathf.Max(1, mh / 2);
                sizes.Add(new Vector2Int(mw, mh));
            }

            _mips = new RenderTexture[sizes.Count];
            for (int i = 0; i < sizes.Count; i++)
            {
                var rt = new RenderTexture(sizes[i].x, sizes[i].y, 0, RenderTextureFormat.ARGBHalf)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                rt.Create();
                _mips[i] = rt;
            }
        }

        private static void ReleaseMipChain()
        {
            if (_mips == null) return;
            foreach (RenderTexture rt in _mips)
            {
                if (rt == null) continue;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            _mips = null;
        }

        public static void Cleanup()
        {
            ReleaseMipChain();
            _readbackPending = false;
            _measuredBrightness01 = 0.5f;
            _currentModifier = 0f;
            _currentGammaModifier = 0f;
        }
    }
}
