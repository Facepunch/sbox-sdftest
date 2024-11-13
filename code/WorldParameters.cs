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

	public INoiseField GetHeightmapField( int seed, Transform transform, int level )
	{
		return new HeightmapField(
			Noise.SimplexField( new Noise.FractalParameters( seed, Frequency: 1f / IslandNoiseScale, Octaves: 8 ) ),
			Noise.SimplexField( new Noise.FractalParameters( seed, Frequency: 1f / TerrainNoiseScale, Octaves: 4 ) ),
			Noise.SimplexField( new Noise.FractalParameters( seed, Frequency: 1f / HeightNoiseScale, Octaves: 8 ) ),
			IslandBias,
			TerrainBias,
			PlainsCurve,
			MountainsCurve,
			transform,
			1f / (1 << level) );
	}

	private record HeightmapField(
		INoiseField IslandField,
		INoiseField TerrainField,
		INoiseField HeightField,
		Curve IslandBias,
		Curve TerrainBias,
		Curve PlainsCurve,
		Curve MountainsCurve,
		Transform Transform,
		float Scale ) : INoiseField
	{
		public float Sample( float x )
		{
			throw new NotImplementedException();
		}

		public float Sample( float x, float y )
		{
			var worldPos = (Vector2)Transform.PointToWorld( new Vector3( x, y, 0f ) / Scale );

			var island = IslandField.Sample( worldPos );
			var terrain = TerrainField.Sample( worldPos );
			var height = HeightField.Sample( worldPos );

			island = IslandBias.Evaluate( island );
			terrain = TerrainBias.Evaluate( terrain );

			var plainsHeight = PlainsCurve.Evaluate( height );
			var mountainsHeight = MountainsCurve.Evaluate( height );
			var oceanHeight = 512f + height * 128f;
			var landHeight = plainsHeight + terrain * (mountainsHeight - plainsHeight);

			height = oceanHeight + island * (landHeight - oceanHeight);

			return height * Scale - 2f;
		}

		public float Sample( float x, float y, float z )
		{
			throw new NotImplementedException();
		}
	}
}
