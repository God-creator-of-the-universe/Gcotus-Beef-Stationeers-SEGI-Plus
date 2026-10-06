cbuffer Globals : register(b0)
{
    float4 _ScreenSize;
    int _StepSize;
    float _SigmaDepth;   // dither strength in this kernel: 0 = off, 1 = full +-0.5/255 step
    float _SigmaNormal;
    float _SigmaLuma;
    float4 _ZBufferParams;
};

Texture2D<float4> _InputGI : register(t0);
Texture2D<float4> _DepthTexture : register(t1);
Texture2D<float4> _NormalTexture : register(t2); // repurposed: tileable blue noise, R channel
RWTexture2D<float4> _OutputGI : register(u0);

// bluenoise.png is a 470x470 seamlessly-looping tile (Christoph Peters, CC0).
#define BLUE_NOISE_SIZE 470

[numthreads(8, 8, 1)]
void LabDither(uint3 id : SV_DispatchThreadID)
{
    uint w = (uint)_ScreenSize.x;
    uint h = (uint)_ScreenSize.y;
    if (id.x >= w || id.y >= h)
        return;

    // Keep t1 and the unused Globals fields live so D3DCompiler does not drop them
    // (ATrous shared bind layout) - only _SigmaDepth and _NormalTexture are real here.
    float keep = _DepthTexture.Load(int3(id.xy, 0)).a * 0.0
               + _SigmaNormal * 0.0 + _SigmaLuma * 0.0
               + _ZBufferParams.x * 0.0 + _StepSize * 0.0;

    float4 c = _InputGI.Load(int3(id.xy, 0));
    float strength = max(_SigmaDepth, 0.0) * (1.0 / 255.0);

    // Three decorrelated taps from one grayscale tile so the added noise isn't a single
    // repeated dot pattern across channels (which would read as colored speckle).
    uint2 rCoord = id.xy % BLUE_NOISE_SIZE;
    uint2 gCoord = (id.xy + uint2(157u, 47u)) % BLUE_NOISE_SIZE;
    uint2 bCoord = (id.xy + uint2(311u, 199u)) % BLUE_NOISE_SIZE;

    float nr = _NormalTexture.Load(int3(rCoord, 0)).r - 0.5;
    float ng = _NormalTexture.Load(int3(gCoord, 0)).r - 0.5;
    float nb = _NormalTexture.Load(int3(bCoord, 0)).r - 0.5;

    float3 x = c.rgb + float3(nr, ng, nb) * strength + keep;
    _OutputGI[id.xy] = float4(x, c.a);
}
