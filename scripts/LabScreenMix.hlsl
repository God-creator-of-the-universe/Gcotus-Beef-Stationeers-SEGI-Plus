cbuffer Globals : register(b0)
{
    float4 _ScreenSize;
    int _StepSize;
    float _SigmaDepth;   // bloom gain
    float _SigmaNormal;  // bloom gamma
    float _SigmaLuma;    // mix 0..1
    float4 _ZBufferParams;
};

Texture2D<float4> _InputGI : register(t0);       // sharp composite
Texture2D<float4> _DepthTexture : register(t1);  // blurred composite
Texture2D<float4> _NormalTexture : register(t2);
RWTexture2D<float4> _OutputGI : register(u0);

[numthreads(8, 8, 1)]
void LabScreenMix(uint3 id : SV_DispatchThreadID)
{
    uint w = (uint)_ScreenSize.x;
    uint h = (uint)_ScreenSize.y;
    if (id.x >= w || id.y >= h)
        return;

    float keep = _NormalTexture.Load(int3(id.xy, 0)).a * 0.0 + _ZBufferParams.x * 0.0 + _StepSize * 0.0;

    float4 sharp = _InputGI.Load(int3(id.xy, 0));
    float3 blurred = _DepthTexture.Load(int3(id.xy, 0)).rgb;

    float gain = max(_SigmaDepth, 0.0);
    float gamma = max(_SigmaNormal, 0.05);
    float mixv = saturate(_SigmaLuma);

    float3 b = blurred * gain;
    b.r = (b.r <= 1.0) ? pow(max(b.r, 0.0), gamma) : b.r;
    b.g = (b.g <= 1.0) ? pow(max(b.g, 0.0), gamma) : b.g;
    b.b = (b.b <= 1.0) ? pow(max(b.b, 0.0), gamma) : b.b;

    float3 outc = sharp.rgb + mixv * b + keep;
    _OutputGI[id.xy] = float4(outc, sharp.a);
}
