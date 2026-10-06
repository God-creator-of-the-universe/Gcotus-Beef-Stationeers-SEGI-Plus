using UnityEngine;

namespace BeefsSEGIPlus
{
    /// <summary>
    /// GI add-layer tone before BlendWithScene: gamma, highlight gain, toe.
    /// Kernel LabGIToneMap on SEGIATrousFilterBeefEdit.
    /// </summary>
    internal static class GIToneMap
    {
        private static ComputeShader _cs;
        private static int _toneKernel = -1;
        private static bool _lookedUp;
        private static bool _loggedMissing;
        private static RenderTexture _output;

        public static bool NeedsToneMap
        {
            get
            {
                float g = ConfigData.GIGamma;
                float t = ConfigData.GIToe;
                float h = ConfigData.GIHighlightGain;
                return !Mathf.Approximately(g, 1f) || t > 0.0005f || h > 0.0005f;
            }
        }

        public static void Ensure(ComputeShader atrousFilter)
        {
            if (_lookedUp && _toneKernel >= 0) return;
            if (atrousFilter == null) return;

            _lookedUp = true;
            _cs = atrousFilter;
            try { _toneKernel = _cs.FindKernel("LabGIToneMap"); }
            catch (System.Exception ex)
            {
                _toneKernel = -1;
                LogMissing("FindKernel(LabGIToneMap) failed: " + ex.Message);
                return;
            }

            if (_toneKernel < 0)
                LogMissing("LabGIToneMap kernel missing");
            else
                SEGIPlugin.Log?.LogInfo("GI tone kernel LabGIToneMap ready.");
        }

        public static RenderTexture Apply(RenderTexture giSource)
        {
            if (!NeedsToneMap || giSource == null)
                return giSource;
            if (_cs == null || _toneKernel < 0)
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
                _cs.SetFloat("_SigmaDepth", ConfigData.GIGamma);
                _cs.SetFloat("_SigmaNormal", ConfigData.GIToe);
                _cs.SetFloat("_SigmaLuma", ConfigData.GIHighlightGain);
                _cs.SetVector("_ZBufferParams", Shader.GetGlobalVector("_ZBufferParams"));
                _cs.SetTexture(_toneKernel, "_InputGI", giSource);
                _cs.SetTexture(_toneKernel, "_OutputGI", _output);

                var depth = Shader.GetGlobalTexture("_CameraDepthTexture");
                var normals = Shader.GetGlobalTexture("_CameraGBufferTexture2");
                _cs.SetTexture(_toneKernel, "_DepthTexture", depth != null ? depth : giSource);
                _cs.SetTexture(_toneKernel, "_NormalTexture", normals != null ? normals : giSource);

                _cs.Dispatch(_toneKernel, Mathf.CeilToInt(w / 8f), Mathf.CeilToInt(h / 8f), 1);
                return _output;
            }
            catch (System.Exception ex)
            {
                SEGIPlugin.Log?.LogWarning("GI tone dispatch failed; leaving GI untoned. " + ex.Message);
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
            _toneKernel = -1;
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
            SEGIPlugin.Log?.LogWarning("GI tone inactive: " + detail);
        }
    }
}
