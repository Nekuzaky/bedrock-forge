// Alpha-blended chunk geometry: water, stained glass, ice. Double-sided so water is visible from below.
Shader "BedrockForge/VertexColorTransparent"
{
    Properties { }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex ChunkVert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "ChunkCommon.hlsl"

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SampleBlock(i);
                return half4(MixFog(c.rgb, i.fog), c.a);
            }
            ENDHLSL
        }
    }
}
