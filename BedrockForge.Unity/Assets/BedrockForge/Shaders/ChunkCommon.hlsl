// Shared by the Bedrock Forge chunk shaders. Lighting (face shade + AO) and fallback colors come in the vertex
// color; uv.z is the slice of the global block texture array built by TextureCatalog.
#ifndef BEDROCKFORGE_CHUNK_COMMON
#define BEDROCKFORGE_CHUNK_COMMON

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D_ARRAY(_BedrockForgeBlocks);
SAMPLER(sampler_BedrockForgeBlocks);

struct Attributes
{
    float4 positionOS : POSITION;
    half4 color : COLOR;
    float3 uv : TEXCOORD0;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    half4 color : COLOR;
    float3 uv : TEXCOORD0;
    half fog : TEXCOORD1;
};

Varyings ChunkVert(Attributes i)
{
    Varyings o;
    o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
    o.color = i.color;
    o.uv = i.uv;
    o.fog = ComputeFogFactor(o.positionCS.z);
    return o;
}

half4 SampleBlock(Varyings i)
{
    return SAMPLE_TEXTURE2D_ARRAY(_BedrockForgeBlocks, sampler_BedrockForgeBlocks, i.uv.xy, i.uv.z) * i.color;
}

#endif
