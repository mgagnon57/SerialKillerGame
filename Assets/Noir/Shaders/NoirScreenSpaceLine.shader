// A line that is always the same number of PIXELS wide, however far away it is.
//
// A lot line drawn as world-space geometry has one width in metres, so its width in pixels
// falls off with distance. At 0.9 m and eight hundred metres out it covers less than one pixel,
// lands on some pixel centres and misses others, and the boundary reads as a dashed line that
// means nothing - which is exactly what a survey drawing must never do. Widening the metres
// only moves the distance at which it happens, and makes the near field coarse in exchange.
//
// So the width is applied AFTER projection instead. The mesh carries the line's centre line and
// the direction it runs in; this expands each vertex sideways in screen space, by a fixed number
// of pixels, perpendicular to the line as the camera sees it. Depth still comes from the centre
// position, so the line is still occluded by ground it genuinely runs behind.
Shader "Noir/ScreenSpaceLine"
{
    Properties
    {
        _WidthPixels ("Width (pixels)", Range(0.5, 12)) = 2.5
        _Color ("Tint", Color) = (1,1,1,1)

        // AN OVERLAY LINE IGNORES DEPTH. Default is the ordinary LEqual/On, so a lot line is
        // paint on the ground and is properly hidden by anything standing in front of it. The
        // house outlines set Always/Off instead: the whole point of switching them on is to see
        // where a building is, and the building itself is the thing in the way.
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+50"
        }

        Pass
        {
            Name "NoirLine"
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                // xyz: the direction the line runs, in object space.
                // w:   which side of it this vertex is, -1 or +1.
                float4 tangentSide : TEXCOORD0;
                float4 colour : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 colour : COLOR;
            };

            float _WidthPixels;
            float4 _Color;

            Varyings vert(Attributes v)
            {
                Varyings o;

                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 tanWS = TransformObjectToWorldDir(v.tangentSide.xyz);

                float4 clip  = TransformWorldToHClip(posWS);
                float4 ahead = TransformWorldToHClip(posWS + tanWS);

                // Behind the camera the perspective divide is meaningless and the offset would
                // fly off. Leave those vertices where they are; they are being clipped anyway.
                if (clip.w > 1e-4 && ahead.w > 1e-4)
                {
                    // Work in pixels, not in NDC. NDC is stretched by the aspect ratio, so a
                    // perpendicular taken there is not perpendicular on the screen - the lines
                    // would come out thinner running one way across the map than the other.
                    float2 halfRes = _ScreenParams.xy * 0.5;
                    float2 p0 = (clip.xy  / clip.w)  * halfRes;
                    float2 p1 = (ahead.xy / ahead.w) * halfRes;

                    float2 along = p1 - p0;
                    float len = length(along);

                    // A line pointing straight at the camera has no direction on screen. Any
                    // perpendicular will do; it is one vertex of a segment seen end-on.
                    float2 dir = len > 1e-5 ? along / len : float2(1.0, 0.0);
                    float2 side = float2(-dir.y, dir.x);

                    float2 offsetPixels = side * (_WidthPixels * 0.5) * v.tangentSide.w;
                    clip.xy += (offsetPixels / halfRes) * clip.w;
                }

                o.positionCS = clip;
                o.colour = v.colour * _Color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return i.colour;
            }
            ENDHLSL
        }
    }

    // If this shader is ever missing or fails to compile, the lines still draw - at a fixed
    // width in metres, the way they did before. A plat view with coarse lines beats no plat view.
    Fallback "Sprites/Default"
}
