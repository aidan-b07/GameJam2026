// Unlit sprite shader with a selectable blend, for the ending's light layers
// (DawnSky): the blend factors come from the material, and _Mode picks what
// the fragment outputs for them.
//  _Mode 0: premultiplied colour (rgb * a); use with Blend OneMinusDstColor One
//           (screen) or One One (add).
//  _Mode 1: 2x multiply: lerp(0.5, rgb, a); use with Blend DstColor SrcColor,
//           a soft-light stand-in (grey leaves the scene as it is, light
//           colours brighten it, dark ones darken it).
// The sprite renderer's colour alpha scales the strength in both modes.
Shader "TickTock/EndingBlend"
{
    Properties
    {
        [MainTexture] _MainTex ("Texture", 2D) = "white" {}
        _Mode ("Mode", Float) = 0
        _SrcBlend ("Src Blend", Float) = 1
        _DstBlend ("Dst Blend", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float _Mode;

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half a = c.a * i.color.a;
                if (_Mode > 0.5)
                    return half4(lerp(half3(0.5, 0.5, 0.5), c.rgb * i.color.rgb, a), 1);
                return half4(c.rgb * i.color.rgb * a, a);
            }
            ENDHLSL
        }
    }
}
