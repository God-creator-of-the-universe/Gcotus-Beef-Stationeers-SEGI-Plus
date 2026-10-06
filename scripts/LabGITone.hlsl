cbuffer Globals : register(b0)
{
    float4 _ScreenSize;
    int _StepSize;
    float _SigmaDepth;   // gamma
    float _SigmaNormal;  // toe (black-level crush)
    float _SigmaLuma;    // highlight gain (bright-only lift)
    float4 _ZBufferParams;
};

Texture2D<float4> _InputGI : register(t0);
Texture2D<float4> _DepthTexture : register(t1);
Texture2D<float4> _NormalTexture : register(t2);
RWTexture2D<float4> _OutputGI : register(u0);

[numthreads(8, 8, 1)]
void LabGIToneMap(uint3 id : SV_DispatchThreadID)
{
    uint w = (uint)_ScreenSize.x;
    uint h = (uint)_ScreenSize.y;
    if (id.x >= w || id.y >= h)
        return;

    float4 c = _InputGI.Load(int3(id.xy, 0));

    // Keep t1/t2 live so D3DCompiler does not drop them (ATrous bind layout).
    float keepRegs = _DepthTexture.Load(int3(id.xy, 0)).a
                   + _NormalTexture.Load(int3(id.xy, 0)).a
                   + _ZBufferParams.x;
    keepRegs *= 0.0; // force-use without changing color; luma slot is highlight now
    keepRegs += _StepSize * 0.0;

    float3 x = c.rgb;

    // 1) Gamma on the GI add-layer (1 = inert).
    float gamma = max(_SigmaDepth, 0.05);
    x.r = (x.r <= 1.0) ? pow(max(x.r, 0.0), gamma) : x.r;
    x.g = (x.g <= 1.0) ? pow(max(x.g, 0.0), gamma) : x.g;
    x.b = (x.b <= 1.0) ? pow(max(x.b, 0.0), gamma) : x.b;

    // 2) Highlight gain - counterpart to toe: lifts brights, leaves darks nearly alone.
    //    x + g*x*x ; g=0 inert. Absolute add is tiny when x is small.
    float hg = max(_SigmaLuma, 0.0);
    x = x + hg * x * x;

    // 3) Toe / black-level crush (no 1/(1-toe) stretch - that boosted HDR highs).
    float toe = saturate(_SigmaNormal);
    x = max(x - toe, 0.0);

    _OutputGI[id.xy] = float4(x + keepRegs, c.a);
}
