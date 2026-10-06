using UnityEngine;

namespace BeefsSEGIPlus
{
    /// <summary>
    /// GI add-layer per-channel amplification, before GIToneMap's gamma/toe/highlight.
    /// Kernel LabChannelGain on SEGIATrousFilterBeefEdit - its own clone of ATrousFilter,
    /// kept separate from LabGIToneMap so its cbuffer never has to touch _SigmaDepth/
    /// _SigmaNormal/_SigmaLuma/_ZBufferParams, which are the real ATrousFilter denoise
    /// filter's own live parameters on this same shared ComputeShader object.
    /// </summary>
    internal static class GIChannelGain
    {
        private static ComputeShader _cs;
        private static int _kernel = -1;
        private static bool _lookedUp;
        private static bool _loggedMissing;
        private static RenderTexture _output;

        public static bool NeedsGain
        {
            get
            {
                return !Mathf.Approximately(ConfigData.GIRedGain, 1f)
                    || !Mathf.Approximately(ConfigData.GIGreenGain, 1f)
                    || !Mathf.Approximately(ConfigData.GIBlueGain, 1f);
            }
        }

        public static void Ensure(ComputeShader atrousFilter)
        {
            if (_lookedUp && _kernel >= 0) return;
            if (atrousFilter == null) return;

            _lookedUp = true;
            _cs = atrousFilter;
            try { _kernel = _cs.FindKernel("LabChannelGain"); }
            catch (System.Exception ex)
            {
                _kernel = -1;
                LogMissing("FindKernel(LabChannelGain) failed: " + ex.Message);
                return;
            }

            if (_kernel < 0)
                LogMissing("LabChannelGain kernel missing");
            else
                SEGIPlugin.Log?.LogInfo("GI channel gain kernel LabChannelGain ready.");
        }

        public static RenderTexture Apply(RenderTexture giSource)
        {
            if (!NeedsGain || giSource == null)
                return giSource;
            if (_cs == null || _kernel < 0)
                return giSource;

            int w = giSource.width;
            int h = giSource.height;
            EnsureOutput(w, h);
            if (_output == null)
                return giSource;

            try
            {
                Graphics.Blit(giSource, _output);
                _cs.SetVector("_ScreenSize", new Vector4(w, h, 1f / w, 1f / h));
                _cs.SetInt("_StepSize", 1);
                _cs.SetFloat("_ChanGainR", ConfigData.GIRedGain);
                _cs.SetFloat("_ChanGainG", ConfigData.GIGreenGain);
                _cs.SetFloat("_ChanGainB", ConfigData.GIBlueGain);
                _cs.SetVector("_ZBufferParams", Shader.GetGlobalVector("_ZBufferParams"));
                _cs.SetTexture(_kernel, "_InputGI", giSource);
                _cs.SetTexture(_kernel, "_OutputGI", _output);

                var depth = Shader.GetGlobalTexture("_CameraDepthTexture");
                var normals = Shader.GetGlobalTexture("_CameraGBufferTexture2");
                _cs.SetTexture(_kernel, "_DepthTexture", depth != null ? depth : giSource);
                _cs.SetTexture(_kernel, "_NormalTexture", normals != null ? normals : giSource);

                _cs.Dispatch(_kernel, Mathf.CeilToInt(w / 8f), Mathf.CeilToInt(h / 8f), 1);
                return _output;
            }
            catch (System.Exception ex)
            {
                SEGIPlugin.Log?.LogWarning("GI channel gain dispatch failed; leaving GI unmodified. " + ex.Message);
                return giSource;
            }
        }

        public static void Cleanup()
        {
            if (_output != null)
            {
                _output.Release();
                Object.DestroyImmediate(_output);
                _output = null;
            }
            _cs = null;
            _kernel = -1;
            _lookedUp = false;
            _loggedMissing = false;
        }

        private static void EnsureOutput(int w, int h)
        {
            if (_output != null && _output.width == w && _output.height == h)
                return;
            if (_output != null)
            {
                _output.Release();
                Object.DestroyImmediate(_output);
                _output = null;
            }
            var desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGBHalf, 0)
            {
                enableRandomWrite = true,
                msaaSamples = 1,
                sRGB = false,
                useMipMap = false,
                autoGenerateMips = false
            };
            _output = new RenderTexture(desc)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            _output.Create();
        }

        private static void LogMissing(string detail)
        {
            if (_loggedMissing) return;
            _loggedMissing = true;
            SEGIPlugin.Log?.LogWarning("GI channel gain inactive: " + detail);
        }
    }
}
