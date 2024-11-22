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

float3 SampleSurfaceNormal( float2 pos )
{
    float heightL = SampleSurface( pos - float2( 1.0, 0.0 ) );
    float heightR = SampleSurface( pos + float2( 1.0, 0.0 ) );
    float heightU = SampleSurface( pos - float2( 0.0, 1.0 ) );
    float heightD = SampleSurface( pos + float2( 0.0, 1.0 ) );

    return normalize( float3( heightR - heightL, heightD - heightU, 1.0 ) );
}

float GetUnderwaterDistance( float3 startPos, float3 endPos, float4 waterPlane )
{
    float3 viewDir = normalize( endPos - startPos );

    float startDepth = dot( float4( startPos.xyz, -1.0 ), waterPlane );

    if ( startDepth > 0.0 )
    {
        return 0.0;
    }

    float endDepth = min( dot( float4( endPos, -1.0 ), waterPlane ), 0.0 );

    return abs( startDepth - endDepth ) / abs( dot( viewDir, waterPlane.xyz ) ) - endDepth;
}

float3 ApplyWaterFog( float3 color, float underwaterDistance )
{
    return color * pow( float3( 0.5, 0.55, 0.65 ), underwaterDistance / 128.0 );
}
