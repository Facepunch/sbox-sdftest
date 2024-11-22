FEATURES
{
    #include "common/features.hlsl"
}

COMMON
{
	#include "common/shared.hlsl"
	#include "procedural.hlsl"

	float SampleSurface( float2 pos )
	{
		float2 offset1 = g_flTime * float2( 21.5, 14.7 );
		float2 offset2 = g_flTime * float2( -19.0, 37.0 );

		float noise1 = Simplex2D( (pos + offset1) / 256.0 );
		float noise2 = ValueNoise( (pos + offset2) / 64.0 ) * 2.0 - 1.0;
		float noise = lerp( noise1, noise2, 0.125 );

		return noise;
	}
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

	BoolAttribute( bWantsFBCopyTexture, true );
	BoolAttribute( translucent, true );
	
	CreateTexture2D( g_tReflectionTexture ) < Attribute( "ReflectionTexture" ); SrgbRead( false ); Filter(MIN_MAG_MIP_LINEAR);    AddressU( MIRROR );     AddressV( MIRROR ); >;
	CreateTexture2D( g_tFrameBufferCopyTexture ) < Attribute("FrameBufferCopyTexture");   SrgbRead( false ); Filter(MIN_MAG_MIP_LINEAR);    AddressU( MIRROR );     AddressV( MIRROR ); > ;    

	float3 SampleSurfaceNormal( float2 pos )
	{
		float heightL = SampleSurface( pos - float2( 1.0, 0.0 ) );
		float heightR = SampleSurface( pos + float2( 1.0, 0.0 ) );
		float heightU = SampleSurface( pos - float2( 0.0, 1.0 ) );
		float heightD = SampleSurface( pos + float2( 0.0, 1.0 ) );

		return normalize( float3( heightR - heightL, heightD - heightU, 1.0 ) );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 surfaceDist = length( i.vPositionWithOffsetWs );
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

		behindColor *= pow( float3( 0.5, 0.55, 0.65 ), behindDepth / 128.0 );

		float fresnel = pow( 1.0 - saturate( abs( dot( normalize( -i.vPositionWithOffsetWs.xyz ), normalize(i.vNormalWs + surfaceNormal) ) ) ), 5.0f ); 

		float4 finalColor = float4( lerp( behindColor, reflectionColor, fresnel ), 1.0 );

		finalColor = DoAtmospherics( surfaceWorldPos, i.vPositionSs, finalColor );

		return finalColor;
	}
}
