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
	public float IslandNoiseScale { get; set; } = 65536f;

	[Property]
	public float TerrainNoiseScale { get; set; } = 16384f;

	[Property]
	public float HeightNoiseScale { get; set; } = 4096f;

	[Property]
	public Curve IslandBias { get; set; }

	[Property]
	public Curve TerrainBias { get; set; }

	[Property]
	public Curve PlainsCurve { get; set; }

	[Property]
	public Curve MountainsCurve { get; set; }

	public void SampleHeightmap( int seed, int res, float size, Transform transform, Span<float> result, int level = 0 )
	{
		var islandField = Noise.SimplexField( new Noise.FractalParameters( seed, Frequency: 1f / IslandNoiseScale, Octaves: 8 ) );
		var terrainField = Noise.SimplexField( new Noise.FractalParameters( seed, Frequency: 1f / TerrainNoiseScale, Octaves: 4 ) );
		var heightField = Noise.SimplexField( new Noise.FractalParameters( seed, Frequency: 1f / HeightNoiseScale, Octaves: 8 ) );

		var scale = 1f / (1 << level);

		for ( var y = 0; y < res; ++y )
		for ( var x = 0; x < res; ++x )
		{
			var worldPos = (Vector2)transform.PointToWorld( new Vector3( x, y, 0f ) * size / (res - 1) );

			var island = islandField.Sample( worldPos );
			var terrain = terrainField.Sample( worldPos );
			var height = heightField.Sample( worldPos );

			island = IslandBias.Evaluate( island );
			terrain = TerrainBias.Evaluate( terrain );

			var plainsHeight = PlainsCurve.Evaluate( height );
			var mountainsHeight = MountainsCurve.Evaluate( height );
			var oceanHeight = 128f + height * 32f;
			var landHeight = plainsHeight + terrain * (mountainsHeight - plainsHeight);

			height = oceanHeight + island * (landHeight - oceanHeight);

			result[x + y * res] = height * scale;
		}
	}
}
