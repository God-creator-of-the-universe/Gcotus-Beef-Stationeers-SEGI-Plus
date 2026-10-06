using System.IO;
using System.Reflection;
using UnityEngine;

namespace BeefsSEGIPlus
{
    /// <summary>
    /// Final-screen post after BlendWithScene:
    /// optional bloom (blur full composite -> gain/gamma -> add with mix),
    /// then optional full-frame gamma, then optional blue-noise dither.
    /// Mix 0 / gamma 1 / dither 0 = inert.
    /// </summary>
    internal static class ScreenPost
    {
        private static readonly string ModDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        private static ComputeShader _cs;
        private static int _blurKernel = -1;
        private static int _mixKernel = -1;
        private static int _gammaKernel = -1;
        private static int _ditherKernel = -1;
        private static bool _lookedUp;
        private static RenderTexture _a;
        private static RenderTexture _b;
        private static RenderTexture _c;

        private static Texture2D _blueNoise;
        private static bool _blueNoiseLoadAttempted;

        public static bool NeedsBloom => ConfigData.BloomMix > 0.0005f;
        public static bool NeedsScreenGamma => !Mathf.Approximately(ConfigData.ScreenGamma, 1f);
        public static bool NeedsDither => ConfigData.DitherStrength > 0.0005f;
        public static bool NeedsAny => NeedsBloom || NeedsScreenGamma || NeedsDither;

        public static void Ensure(ComputeShader atrousFilter)
        {
            if (_lookedUp && _blurKernel >= 0) return;
            if (atrousFilter == null) return;
            _lookedUp = true;
            _cs = atrousFilter;
            try { _blurKernel = _cs.FindKernel("LabGIBlur"); } catch { _blurKernel = -1; }
            try { _mixKernel = _cs.FindKernel("LabScreenMix"); } catch { _mixKernel = -1; }
            try { _gammaKernel = _cs.FindKernel("LabScreenGamma"); } catch { _gammaKernel = -1; }
            try { _ditherKernel = _cs.FindKernel("LabDither"); } catch { _ditherKernel = -1; }
            SEGIPlugin.Log?.LogInfo(
                $"Screen post kernels blur={_blurKernel >= 0} mix={_mixKernel >= 0} gamma={_gammaKernel >= 0} dither={_ditherKernel >= 0}");
        }

        // Lazy - only touches disk the first time dithering is actually turned on.
        private static Texture2D EnsureBlueNoise()
        {
            if (_blueNoise != null || _blueNoiseLoadAttempted)
                return _blueNoise;
            _blueNoiseLoadAttempted = true;
            try
            {
                string path = Path.Combine(ModDirectory, "Content", "bluenoise.png");
                byte[] bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat
                };
                if (!tex.LoadImage(bytes))
                    throw new System.Exception("LoadImage returned false");
                _blueNoise = tex;
                SEGIPlugin.Log?.LogInfo($"Dither blue-noise texture loaded ({tex.width}x{tex.height}).");
            }
            catch (System.Exception ex)
            {
                SEGIPlugin.Log?.LogWarning("Dither blue-noise texture failed to load; dithering will be skipped. " + ex.Message);
                _blueNoise = null;
            }
            return _blueNoise;
        }

        public static RenderTexture Apply(RenderTexture composite)
        {
            if (!NeedsAny || composite == null || _cs == null)
                return composite;

            int w = composite.width;
            int h = composite.height;
            EnsureRTs(w, h);

            RenderTexture current = composite;
            try
            {
                if (NeedsBloom)
                {
                    if (_blurKernel < 0 || _mixKernel < 0)
                    {
                        SEGIPlugin.Log?.LogWarning("Bloom requested but kernels missing; skipping bloom.");
                    }
                    else
                    {
                        DispatchBlur(current, _a, w, h, horizontal: true);
                        DispatchBlur(_a, _b, w, h, horizontal: false);

                        Graphics.Blit(current, _c);
                        SetCommon(_mixKernel, w, h, current);
                        _cs.SetFloat("_SigmaDepth", ConfigData.BloomGain);
                        _cs.SetFloat("_SigmaNormal", ConfigData.BloomGamma);
                        _cs.SetFloat("_SigmaLuma", ConfigData.BloomMix);
                        _cs.SetTexture(_mixKernel, "_InputGI", current);
                        _cs.SetTexture(_mixKernel, "_DepthTexture", _b);
                        _cs.SetTexture(_mixKernel, "_NormalTexture", current);
                        _cs.SetTexture(_mixKernel, "_OutputGI", _c);
                        _cs.Dispatch(_mixKernel, Mathf.CeilToInt(w / 8f), Mathf.CeilToInt(h / 8f), 1);
                        current = _c;
                    }
                }

                if (NeedsScreenGamma)
                {
                    if (_gammaKernel < 0)
                    {
                        SEGIPlugin.Log?.LogWarning("Screen gamma requested but kernel missing; skipping.");
                    }
                    else
                    {
                        RenderTexture dest = (current == _a) ? _b : _a;
                        Graphics.Blit(current, dest);
                        SetCommon(_gammaKernel, w, h, current);
                        _cs.SetFloat("_SigmaDepth", ConfigData.ScreenGamma);
                        _cs.SetFloat("_SigmaNormal", 0f);
                        _cs.SetFloat("_SigmaLuma", 0f);
                        _cs.SetTexture(_gammaKernel, "_InputGI", current);
                        _cs.SetTexture(_gammaKernel, "_DepthTexture", current);
                        _cs.SetTexture(_gammaKernel, "_NormalTexture", current);
                        _cs.SetTexture(_gammaKernel, "_OutputGI", dest);
                        _cs.Dispatch(_gammaKernel, Mathf.CeilToInt(w / 8f), Mathf.CeilToInt(h / 8f), 1);
                        current = dest;
                    }
                }

                if (NeedsDither)
                {
                    Texture2D blueNoise = EnsureBlueNoise();
                    if (_ditherKernel < 0 || blueNoise == null)
                    {
                        SEGIPlugin.Log?.LogWarning("Dither requested but kernel or blue-noise texture missing; skipping.");
                    }
                    else
                    {
                        RenderTexture dest = (current == _a) ? _b : _a;
                        Graphics.Blit(current, dest);
                        SetCommon(_ditherKernel, w, h, current);
                        _cs.SetFloat("_SigmaDepth", ConfigData.DitherStrength);
                        _cs.SetFloat("_SigmaNormal", 0f);
                        _cs.SetFloat("_SigmaLuma", 0f);
                        _cs.SetTexture(_ditherKernel, "_InputGI", current);
                        _cs.SetTexture(_ditherKernel, "_DepthTexture", current);
                        _cs.SetTexture(_ditherKernel, "_NormalTexture", blueNoise);
                        _cs.SetTexture(_ditherKernel, "_OutputGI", dest);
                        _cs.Dispatch(_ditherKernel, Mathf.CeilToInt(w / 8f), Mathf.CeilToInt(h / 8f), 1);
                        current = dest;
                    }
                }

                return current;
            }
            catch (System.Exception ex)
            {
                SEGIPlugin.Log?.LogWarning("Screen post failed; leaving composite as-is. " + ex.Message);
                return composite;
            }
        }

        public static void Cleanup()
        {
            Release(ref _a);
            Release(ref _b);
            Release(ref _c);
            _cs = null;
            _blurKernel = _mixKernel = _gammaKernel = _ditherKernel = -1;
            _lookedUp = false;
            if (_blueNoise != null)
            {
                UnityEngine.Object.DestroyImmediate(_blueNoise);
                _blueNoise = null;
            }
            _blueNoiseLoadAttempted = false;
        }

        private static void DispatchBlur(RenderTexture src, RenderTexture dst, int w, int h, bool horizontal)
        {
            Graphics.Blit(src, dst);
            SetCommon(_blurKernel, w, h, src);
            _cs.SetInt("_StepSize", horizontal ? 0 : 1);
            _cs.SetFloat("_SigmaDepth", ConfigData.BloomBlur);
            _cs.SetFloat("_SigmaNormal", 0f);
            _cs.SetFloat("_SigmaLuma", 0f);
            _cs.SetTexture(_blurKernel, "_InputGI", src);
            _cs.SetTexture(_blurKernel, "_DepthTexture", src);
            _cs.SetTexture(_blurKernel, "_NormalTexture", src);
            _cs.SetTexture(_blurKernel, "_OutputGI", dst);
            _cs.Dispatch(_blurKernel, Mathf.CeilToInt(w / 8f), Mathf.CeilToInt(h / 8f), 1);
        }

        private static void SetCommon(int kernel, int w, int h, RenderTexture unusedBind)
        {
            _cs.SetVector("_ScreenSize", new Vector4(w, h, 1f / w, 1f / h));
            _cs.SetInt("_StepSize", 0);
            _cs.SetVector("_ZBufferParams", Shader.GetGlobalVector("_ZBufferParams"));
        }

        private static void EnsureRTs(int w, int h)
        {
            Ensure(ref _a, w, h);
            Ensure(ref _b, w, h);
            Ensure(ref _c, w, h);
        }

        private static void Ensure(ref RenderTexture rt, int w, int h)
        {
            if (rt != null && rt.width == w && rt.height == h) return;
            Release(ref rt);
            var desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGBHalf, 0)
            {
                enableRandomWrite = true,
                msaaSamples = 1,
                sRGB = false,
                useMipMap = false,
                autoGenerateMips = false
            };
            rt = new RenderTexture(desc)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            rt.Create();
        }

        private static void Release(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Object.DestroyImmediate(rt);
            rt = null;
        }
    }
}
