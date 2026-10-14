// Alpha-tested chunk geometry (leaves, plants, glass, panes). Double-sided so plant quads show from both sides.
Shader "BedrockForge/VertexColorCutout"
{
    Properties { }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex ChunkVert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "ChunkCommon.hlsl"

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SampleBlock(i);
                clip(c.a - 0.5);
                return half4(MixFog(c.rgb, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Off
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex ChunkVert
            #pragma fragment frag
            #include "ChunkCommon.hlsl"

            half frag(Varyings i) : SV_Target
            {
                clip(SampleBlock(i).a - 0.5);
                return 0;
            }
            ENDHLSL
        }
    }
}
