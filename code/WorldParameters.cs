using System;
using Sandbox.Sdf;
using Sandbox.Utility;

namespace Sandbox;

[GameResource( "World Parameters", "world", "Parameters for procedurally generating worlds.", Icon = "public" )]
public sealed class WorldParameters : GameResource
{
	[Property]
	public Sdf3DVolume Ground { get; set; }

	[Property]
	public Curve TerrainBias { get; set; }

	[Property]
	public Curve PlainsCurve { get; set; }

	[Property]
	public Curve MountainsCurve { get; set; }

	public void SampleHeightmap( int seed, int res, float size, Transform transform, Span<float> result, int level = 0 )
	{
		var terrainField = Noise.SimplexField( new Noise.FractalParameters( seed, Frequency: 1f / 16384f, Octaves: 4 ) );
		var heightField = Noise.SimplexField( new Noise.FractalParameters( seed, Frequency: 1f / 4096f, Octaves: 8 ) );

		var scale = 1f / (1 << level);

		for ( var y = 0; y < res; ++y )
		for ( var x = 0; x < res; ++x )
		{
			var worldPos = (Vector2)transform.PointToWorld( new Vector3( x, y, 0f ) * size / (res - 1) );

			var terrain = terrainField.Sample( worldPos );
			var height = heightField.Sample( worldPos );

			terrain = TerrainBias.Evaluate( terrain );

			var plainsHeight = PlainsCurve.Evaluate( height );
			var mountainsHeight = MountainsCurve.Evaluate( height );

			height = plainsHeight + terrain * (mountainsHeight - plainsHeight);

			result[x + y * res] = height * scale;
		}
	}
}
