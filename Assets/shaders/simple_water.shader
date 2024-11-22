FEATURES
{
    #include "common/features.hlsl"
}

COMMON
{
	#include "common/shared.hlsl"
    #include "shared/water.hlsl"
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
		float3x4 matObjectToWorld = CalculateInstancingObjectToWorldMatrix( i );
		float3 surfaceWorldPos = mul( matObjectToWorld, float4( i.vPositionOs.xyz, 1.0 ) ).xyz;
		
		// i.vPositionOs.z += SampleSurface( surfaceWorldPos.xy ) * 16.0;

		PixelInput o = ProcessVertex( i );

		return FinalizeVertex( o );
	}
}

PS
{
	#include "common/pixel.hlsl"

	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, true );
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );

	BoolAttribute( bWantsFBCopyTexture, true );
	BoolAttribute( translucent, true );
	
	CreateTexture2D( g_tReflectionTexture ) < Attribute( "ReflectionTexture" ); SrgbRead( true ); Filter(MIN_MAG_MIP_LINEAR);    AddressU( MIRROR );     AddressV( MIRROR ); >;
	CreateTexture2D( g_tFrameBufferCopyTexture ) < Attribute("FrameBufferCopyTexture");   SrgbRead( false ); Filter(MIN_MAG_MIP_LINEAR);    AddressU( MIRROR );     AddressV( MIRROR ); > ;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float surfaceDist = length( i.vPositionWithOffsetWs );
		float3 surfaceWorldPos = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 surfaceNormal = SampleSurfaceNormal( surfaceWorldPos.xy );

		float2 behindRefraction = surfaceNormal.xy / 16.0;
		float3 behindWorldPos = Depth::GetWorldPosition( i.vPositionSs.xy + behindRefraction / g_vFrameBufferCopyInvSizeAndUvScale.xy ).xyz;

		float behindDist = distance( behindWorldPos, surfaceWorldPos );
		float behindDepth = i.vPositionWithOffsetWs.z > 0
			? surfaceDist
			: abs( surfaceWorldPos.z - behindWorldPos.z ) + behindDist;

		float2 behindUv = i.vPositionSs.xy * g_vFrameBufferCopyInvSizeAndUvScale.xy;
		float2 reflectionUv = float2( 1.0 - behindUv.x, behindUv.y * 1.035 );

		behindUv += behindRefraction;
		reflectionUv += surfaceNormal.xy / 16.0;

		float3 behindColor = Tex2D( g_tFrameBufferCopyTexture, behindUv ).xyz;
		float3 reflectionColor = Tex2D( g_tReflectionTexture, reflectionUv ).xyz;

		behindColor = ApplyWaterFog( behindColor, behindDepth );
		behindColor = DoAtmospherics( surfaceWorldPos, i.vPositionSs.xy, float4( behindColor, 1.0 ) ).xyz;

		float fresnel = pow( 1.0 - saturate( abs( dot( normalize( -i.vPositionWithOffsetWs.xyz ), normalize(i.vNormalWs + surfaceNormal) ) ) ), 5.0f ); 

		return float4( lerp( behindColor, reflectionColor, fresnel ), 1.0 );
	}
}
