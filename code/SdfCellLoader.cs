using System;
using System.Threading.Tasks;
using Sandbox.Sdf;
using Sandbox.Utility;
using Sandbox.Worlds;

namespace Sandbox;

public sealed class SdfCellLoader : Component, ICellLoader, Component.ExecuteInEditor
{
	[Property]
	public WorldParameters Parameters { get; set; }

	[Property]
	public string Seed { get; set; }

	private readonly List<WorldCell> _fadingCells = new();

	void ICellLoader.LoadCell( WorldCell cell )
	{
		var level = cell.World.Level;
		var sdfObj = new GameObject( false )
		{
			Parent = cell.GameObject,
			Transform = { Local = new Transform( 0f, Rotation.Identity, 1 << level ) }
		};

		var sdfWorld = sdfObj.Components.Create<Sdf3DWorld>();
		var sdfSize = (int)cell.World.CellSize >> level;

		sdfWorld.IsFinite = true;
		sdfWorld.Size = new Vector3Int( sdfSize, sdfSize, (int)cell.World.CellHeight >> level );
		sdfWorld.HasPhysics = level == 0;
		sdfWorld.Opacity = 0f;

		sdfObj.Enabled = true;

		cell.HideStateChanged += Cell_HideStateChanged;

		cell.MarkLoading();

		_ = GenerateAsync( cell, sdfWorld );
	}

	private void Cell_HideStateChanged( WorldCell cell, bool hidden )
	{
		if ( !_fadingCells.Contains( cell ) )
		{
			_fadingCells.Add( cell );
		}
	}

	protected override void OnUpdate()
	{
		for ( var i = _fadingCells.Count - 1; i >= 0; --i )
		{
			var cell = _fadingCells[i];

			if ( !cell.IsValid )
			{
				_fadingCells.RemoveAt( i );
				continue;
			}

			if ( cell.Components.GetInDescendantsOrSelf<Sdf3DWorld>() is not { } sdfWorld )
			{
				_fadingCells.RemoveAt( i );
				continue;
			}

			var targetOpacity = cell.IsHidden ? 0f : 1f;
			var currentOpacity = sdfWorld.Opacity;
			var nextOpacity = Math.Clamp( currentOpacity + Math.Sign( targetOpacity - currentOpacity ) * Time.Delta, 0f, 1f );

			sdfWorld.Opacity = nextOpacity;

			if ( nextOpacity == targetOpacity )
			{
				_fadingCells.RemoveAt( i );
			}
		}
	}

	private async Task GenerateAsync( WorldCell cell, Sdf3DWorld sdfWorld )
	{
		if ( Parameters is null ) return;

		var cellSize = cell.World.CellSize;
		var level = cell.World.Level;

		await Task.WorkerThread();

		var voxelRes = (int)(sdfWorld.Size.x * Parameters.Ground.ChunkResolution / Parameters.Ground.ChunkSize);
		var res = voxelRes;

		var heightmap = new float[res * res];

		Parameters.SampleHeightmap( Seed.FastHash(), res, cellSize, cell.Transform.World, heightmap, level );

		var caveNoise = new CaveNoiseField( Noise.SimplexField( new Noise.FractalParameters( Octaves: 8, Frequency: 1f / 4096f ) ) );
		var caveSdf = new NoiseSdf3D( caveNoise, 0.65f, 256f / sdfWorld.Transform.Scale.x )
			.Transform( new Transform( -cell.Transform.Position / sdfWorld.Transform.Scale.x, Rotation.Identity,
				1f / sdfWorld.Transform.Scale.x ) );

		await Task.MainThread();
		await sdfWorld.AddAsync( new HeightmapSdf3D( heightmap, res, sdfWorld.Size.x ).Intersection( caveSdf ), Parameters.Ground );

		while ( sdfWorld.NeedsMeshUpdate )
		{
			await Task.DelayRealtime( 1 );
		}

		cell.MarkReady();
	}

	void ICellLoader.UnloadCell( WorldCell cell )
	{

	}
}

file record CaveNoiseField( INoiseField BaseNoise ) : INoiseField
{
	public float Sample( float x )
	{
		throw new NotImplementedException();
	}

	public float Sample( float x, float y )
	{
		throw new NotImplementedException();
	}

	public float Sample( float x, float y, float z )
	{
		return BaseNoise.Sample( x, y, z * 2f ) * Math.Clamp( (z - 128f) / 512f, 0f, 1f );
	}
}
