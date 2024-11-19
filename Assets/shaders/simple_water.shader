FEATURES
{
    #include "common/features.hlsl"
}

COMMON
{
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		// Add your vertex manipulation functions here
		return FinalizeVertex( o );
	}
}

PS
{
	#include "common/pixel.hlsl"

	BoolAttribute( bWantsFBCopyTexture, true );
	BoolAttribute( translucent, true );
	
	CreateTexture2D( g_tReflectionTexture ) < Attribute( "ReflectionTexture" ); SrgbRead( false ); Filter(MIN_MAG_MIP_LINEAR);    AddressU( MIRROR );     AddressV( MIRROR ); >;
	CreateTexture2D( g_tFrameBufferCopyTexture ) < Attribute("FrameBufferCopyTexture");   SrgbRead( false ); Filter(MIN_MAG_MIP_LINEAR);    AddressU( MIRROR );     AddressV( MIRROR ); > ;    

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 surfaceWorldPos = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 behindWorldPos = Depth::GetWorldPosition( i.vPositionSs.xy ).xyz;

		float behindDist = distance( behindWorldPos, surfaceWorldPos );
		float behindDepth = abs( surfaceWorldPos.z - behindWorldPos.z );

		float2 behindUv = i.vPositionSs.xy * g_vFrameBufferCopyInvSizeAndUvScale.xy;
		float2 reflectionUv = float2( behindUv.x, 1.0 - behindUv.y * 1.035 );

		float3 behindColor = Tex2D( g_tFrameBufferCopyTexture, behindUv ).xyz;
		float3 reflectionColor = Tex2D( g_tReflectionTexture, reflectionUv ).xyz;

		behindColor *= pow( float3( 0.5, 0.55, 0.65 ), behindDepth / 64.0 );

		float flFresnel = pow( 1.0 - saturate( dot( normalize( -i.vPositionWithOffsetWs.xyz ), i.vNormalWs ) ), 5.0f ); 

		float3 finalColor = lerp( behindColor, reflectionColor, flFresnel );

		return float4( finalColor, 1.0 );
	}
}
