// Opaque chunk geometry: block texture × vertex color (baked light).
Shader "BedrockForge/VertexColor"
{
    Properties { }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex ChunkVert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "ChunkCommon.hlsl"

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SampleBlock(i);
                return half4(MixFog(c.rgb, i.fog), 1);
            }
            ENDHLSL
        }

        // Needed when URP renders a depth prepass (depth priming, SSAO, depth texture).
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Back
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
