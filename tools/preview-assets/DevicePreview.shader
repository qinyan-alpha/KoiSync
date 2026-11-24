Shader "KoiSync/DevicePreview"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4
    }
    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        ZTest [_ZTest]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct Input { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct Output { float4 position : SV_POSITION; fixed4 color : COLOR; };
            Output vert(Input value)
            {
                Output result;
                result.position = UnityObjectToClipPos(value.vertex);
                result.color = value.color * _Color;
                return result;
            }
            fixed4 frag(Output value) : SV_Target { return value.color; }
            ENDCG
        }
    }
}
