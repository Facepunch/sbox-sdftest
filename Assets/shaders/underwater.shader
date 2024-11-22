MODES
{
    VrForward();
}

FEATURES
{
}

COMMON
{
    #include "postprocess/shared.hlsl"
	#include "common/classes/Depth.hlsl"
}

struct VertexInput
{
    float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
    float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct PixelInput
{
    float2 vTexCoord : TEXCOORD0;

    #if ( PROGRAM == VFX_PROGRAM_VS )
        float4 vPositionPs : SV_Position;
    #endif

    #if ( ( PROGRAM == VFX_PROGRAM_PS ) )
        float4 vPositionSs : SV_Position;
    #endif
};

VS
{
    PixelInput MainVs( VertexInput i )
    {
        PixelInput o;
        
        o.vPositionPs = float4( i.vPositionOs.xy, 0.0f, 1.0f );
        o.vTexCoord = i.vTexCoord;
        return o;
    }
}

PS
{
    #include "shared/water.hlsl"

    RenderState( DepthWriteEnable, false );
    RenderState( DepthEnable, false );

    CreateTexture2D( g_tColorBuffer ) < Attribute( "ColorBuffer" ); SrgbRead( true ); >;

    float3 GetWorldPositionForDepth( in float2 screenPosition, in float flDepth )
    { 
		float3 vRay = float3( screenPosition * g_vInvViewportSize, flDepth );
		vRay.y = 1.0 - vRay.y;
		vRay.xy = 2.0f * vRay.xy - 1.0f;

		float4 vWorldPos = mul( g_matProjectionToWorld, float4( vRay, 1.0f ) );
		vWorldPos.xyz /= vWorldPos.w;
		
		return vWorldPos.xyz + g_vHighPrecisionLightingOffsetWs;
    }

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        float2 vScreenUv = i.vPositionSs.xy / g_vRenderTargetSize;
        float3 startPos = GetWorldPositionForDepth( i.vPositionSs.xy, 1.0 ).xyz;
        float3 endPos = Depth::GetWorldPosition( i.vPositionSs.xy ).xyz;
        float4 color = Tex2D( g_tColorBuffer, vScreenUv );

        color.xyz = ApplyWaterFog( color.xyz, GetUnderwaterDistance( startPos, endPos, float4( 0.0, 0.0, 1.0, 640.0 ) ) );

        return color;
    }
}