// 工程自带的纯色不透明着色器（Built-in 管线）。
//
// 存在原因：原先运行时代码用 Shader.Find("Unlit/Color") 拿内置纯色着色器，但内置着色器
// 若没有被任何 Material 资产引用、也没进 Always Included Shaders，打包时会被剥离，
// Shader.Find 在 exe 里返回 null；代码随后回退到 Sprites/Default（Cull Off + ZWrite Off +
// 透明混合），于是方块所有面（含白色行进面）都不写深度、双面绘制，白面从任何方向都能"透视"看见。
//
// 本着色器放在 Assets/Resources/Shaders 下，Resources.Load 一定随包打进去，且：
//   · 不透明队列 Geometry，ZWrite On，ZTest LEqual —— 面与面之间正常深度遮挡，白色行进面被方块自身挡住；
//   · Cull Off —— 与内置 Unlit/Color 行为一致，不依赖自建立方体网格的三角形绕序；
//     双面绘制不影响结果：不透明+写深度时，无论绘制顺序如何，最近的片元最终胜出。
Shader "Custom/OpaqueColor"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" "IgnoreProjector"="True" }

        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "OPAQUE_COLOR"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }

    Fallback Off
}
