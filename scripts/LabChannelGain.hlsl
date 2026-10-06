cbuffer Globals : register(b0)
{
    float4 _ScreenSize;
    int _StepSize;
    float _SigmaDepth;
    float _SigmaNormal;
    float _SigmaLuma;
    float4 _ZBufferParams;
    float _ChanGainR;    // red channel amplification, 1 = inert
    float _ChanGainG;    // green channel amplification, 1 = inert
    float _ChanGainB;    // blue channel amplification, 1 = inert
};

Texture2D<float4> _InputGI : register(t0);
Texture2D<float4> _DepthTexture : register(t1);
Texture2D<float4> _NormalTexture : register(t2);
RWTexture2D<float4> _OutputGI : register(u0);

[numthreads(8, 8, 1)]
void LabChannelGain(uint3 id : SV_DispatchThreadID)
{
    uint w = (uint)_ScreenSize.x;
    uint h = (uint)_ScreenSize.y;
    if (id.x >= w || id.y >= h)
        return;

    float4 c = _InputGI.Load(int3(id.xy, 0));

    // Keep t1/t2 and the original ATrous-shared fields live so D3DCompiler does not drop
    // them (ATrous bind layout) - only the three new fields at the end are real here.
    float keepRegs = _DepthTexture.Load(int3(id.xy, 0)).a
                   + _NormalTexture.Load(int3(id.xy, 0)).a
                   + _ZBufferParams.x
                   + _SigmaDepth + _SigmaNormal + _SigmaLuma;
    keepRegs *= 0.0;
    keepRegs += _StepSize * 0.0;

    float3 x = c.rgb;
    x.r *= max(_ChanGainR, 0.0);
    x.g *= max(_ChanGainG, 0.0);
    x.b *= max(_ChanGainB, 0.0);

    _OutputGI[id.xy] = float4(x + keepRegs, c.a);
}
