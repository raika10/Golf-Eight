// 自分のボールを「壁などに遮蔽されたときだけ」シルエット表示するためのURP用シェーダ。
// ZTest Greater により、手前に他のものが描かれているピクセル（＝隠れている部分）だけを塗る。
// ボール本体と同じ形・同じ位置の子メッシュに割り当て、GolfBall が自分のボールのときだけ表示する。
Shader "Golf/SeeThroughBall"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 0.82, 0.25, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "SeeThroughSilhouette"
            Tags { "LightMode" = "UniversalForward" }

            ZTest Greater   // 遮蔽されている（手前に何かある）ピクセルだけ描画
            ZWrite Off
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
