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

	[Property]
	public float MaxHeight { get; set; } = 8192f;

	[Button]
	public void Randomize()
	{
		var bytes = new byte[8];

		Random.Shared.NextBytes( bytes );

		Seed = string.Join( "", bytes.Select( x => x.ToString( "x2" ) ) );
		Regenerate();
	}

	[Button]
	public void Regenerate()
	{
		Scene.GetAllComponents<StreamingWorld>()
			.FirstOrDefault()
			?.Clear();
	}

	void ICellLoader.LoadCell( WorldCell cell )
	{
		var level = cell.Index.Level;
		var sdfObj = new GameObject( false )
		{
			Parent = cell.GameObject,
			Transform = { Local = new Transform( 0f, Rotation.Identity, 1 << level ) }
		};

		var sdfWorld = sdfObj.Components.Create<Sdf3DWorld>();
		var sdfSize = (int)cell.Size.x >> level;

		sdfWorld.IsFinite = true;
		sdfWorld.Size = new Vector3( sdfSize, sdfSize, (int)MaxHeight >> level );
		sdfWorld.HasPhysics = level == 0;
		sdfWorld.Opacity = 0f;

		sdfObj.Enabled = true;

		if ( level < 3 )
		{
			sdfObj.Components.Create<EditRelay>();
		}

		cell.OpacityChanged += Cell_OpacityChanged;

		cell.MarkLoading();

		_ = GenerateAsync( cell, sdfWorld );
	}

	private void Cell_OpacityChanged( WorldCell cell, float opacity )
	{
		if ( cell.Components.GetInDescendantsOrSelf<Sdf3DWorld>() is { } sdfWorld )
		{
			sdfWorld.Opacity = opacity;
		}
	}

	private async Task GenerateAsync( WorldCell cell, Sdf3DWorld sdfWorld )
	{
		if ( Parameters is null ) return;

		var level = cell.Index.Level;

		await Task.WorkerThread();

		var voxelRes = (int)(sdfWorld.Size.x * Parameters.Ground.ChunkResolution / Parameters.Ground.ChunkSize);

		var heightmapNoise = Parameters.GetHeightmapField( Seed.FastHash(), cell.Transform.World, level );
		var caveNoise = new CaveNoiseField(
			Noise.SimplexField( new Noise.FractalParameters( Octaves: 6, Frequency: 1f / 4096f ) ),
			Noise.SimplexField( new Noise.FractalParameters( Octaves: 2, Frequency: 1f / 16384f ) ) );
		var caveSdf = new NoiseSdf3D( caveNoise, 0.6f, 256f / sdfWorld.WorldScale.x )
			.Transform( new Transform( -cell.WorldPosition / sdfWorld.WorldScale.x, Rotation.Identity, 1f / sdfWorld.WorldScale.x ) );
		var heightmapSdf = new HeightmapSdf3D( heightmapNoise, voxelRes, sdfWorld.Size.x );
		var finalSdf = heightmapSdf.Intersection( caveSdf );

		await Task.MainThread();
		await sdfWorld.AddAsync( finalSdf, Parameters.Ground );

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

public sealed class EditRelay : Component
{
	private EditManager _manager;
	private EditFeedSubscription _subscription;

	[RequireComponent]
	public Sdf3DWorld SdfWorld { get; private set; }

	protected override void OnStart()
	{
		var min = WorldPosition;
		var max = min + SdfWorld.Size * SdfWorld.WorldScale.x;

		_manager = Scene.GetAllComponents<EditManager>().FirstOrDefault();

		if ( _manager is null ) return;

		_subscription = _manager.Subscribe( min, max );
		_subscription.Edited += OnEdited;
	}

	private void OnEdited( EditData data )
	{
		var origin = SdfWorld.Transform.World.PointToLocal( data.Origin );
		var localRadius = data.Size / SdfWorld.WorldScale.x;

		_ = data.Kind switch
		{
			EditKind.Add => SdfWorld.AddAsync( new SphereSdf3D( origin, localRadius ), _manager.Material ),
			EditKind.Subtract => SdfWorld.SubtractAsync( new SphereSdf3D( origin, localRadius ) ),
			_ => Task.CompletedTask
		};
	}

	protected override void OnDestroy()
	{
		_subscription?.Dispose();
		_subscription = null;
	}
}

file record CaveNoiseField( INoiseField BaseNoise, INoiseField ThresholdNoise ) : INoiseField
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
		var threshold = ThresholdNoise.Sample( x, y );

		return BaseNoise.Sample( x, y, z * 2f ) * Math.Clamp( (z - 64f - threshold * 192f) / 256f, 0f, 1f );
	}
}
