cbuffer Globals : register(b0)
{
    float4 _ScreenSize;
    int _StepSize;
    float _SigmaDepth;   // screen gamma (1 = inert)
    float _SigmaNormal;
    float _SigmaLuma;
    float4 _ZBufferParams;
};

Texture2D<float4> _InputGI : register(t0);
Texture2D<float4> _DepthTexture : register(t1);
Texture2D<float4> _NormalTexture : register(t2);
RWTexture2D<float4> _OutputGI : register(u0);

[numthreads(8, 8, 1)]
void LabScreenGamma(uint3 id : SV_DispatchThreadID)
{
    uint w = (uint)_ScreenSize.x;
    uint h = (uint)_ScreenSize.y;
    if (id.x >= w || id.y >= h)
        return;

    float keep = _DepthTexture.Load(int3(id.xy, 0)).a * 0.0
               + _NormalTexture.Load(int3(id.xy, 0)).a * 0.0
               + _ZBufferParams.x * 0.0
               + _SigmaNormal * 0.0
               + _SigmaLuma * 0.0
               + _StepSize * 0.0;

    float4 c = _InputGI.Load(int3(id.xy, 0));
    float gamma = max(_SigmaDepth, 0.05);
    float3 x = c.rgb;
    x.r = (x.r <= 1.0) ? pow(max(x.r, 0.0), gamma) : x.r;
    x.g = (x.g <= 1.0) ? pow(max(x.g, 0.0), gamma) : x.g;
    x.b = (x.b <= 1.0) ? pow(max(x.b, 0.0), gamma) : x.b;
    _OutputGI[id.xy] = float4(x + keep, c.a);
}
