cbuffer Globals : register(b0)
{
    float4 _ScreenSize;
    int _StepSize;       // 0 = horizontal pass, 1 = vertical pass
    float _SigmaDepth;   // blur radius in pixels (0 = caller should skip)
    float _SigmaNormal;
    float _SigmaLuma;
    float4 _ZBufferParams;
};

Texture2D<float4> _InputGI : register(t0);
Texture2D<float4> _DepthTexture : register(t1);
Texture2D<float4> _NormalTexture : register(t2);
RWTexture2D<float4> _OutputGI : register(u0);

[numthreads(8, 8, 1)]
void LabGIBlur(uint3 id : SV_DispatchThreadID)
{
    uint w = (uint)_ScreenSize.x;
    uint h = (uint)_ScreenSize.y;
    if (id.x >= w || id.y >= h)
        return;

    // Keep t1/t2 live for ATrous bind layout.
    float keep = _DepthTexture.Load(int3(id.xy, 0)).a
               + _NormalTexture.Load(int3(id.xy, 0)).a
               + _ZBufferParams.x * 0.0
               + _SigmaNormal * 0.0
               + _SigmaLuma * 0.0;

    float radiusF = max(_SigmaDepth, 0.0);
    int radius = (int)min(radiusF + 0.5, 48.0);
    if (radius < 1)
    {
        _OutputGI[id.xy] = _InputGI.Load(int3(id.xy, 0)) + float4(keep * 0, keep * 0, keep * 0, 0);
        return;
    }

    float sigma = max(radiusF * 0.5, 0.5);
    float2 dir = (_StepSize == 0) ? float2(1, 0) : float2(0, 1);

    float4 sum = 0.0.xxxx;
    float wsum = 0.0;
    [loop]
    for (int i = -radius; i <= radius; i++)
    {
        float2 uv = float2(id.xy) + dir * i;
        int2 p = int2(clamp(uv.x, 0, w - 1), clamp(uv.y, 0, h - 1));
        float wt = exp(-0.5 * (i * i) / (sigma * sigma));
        sum += _InputGI.Load(int3(p, 0)) * wt;
        wsum += wt;
    }
    _OutputGI[id.xy] = sum / max(wsum, 1e-4) + float4(0, 0, 0, 0) * keep;
}
