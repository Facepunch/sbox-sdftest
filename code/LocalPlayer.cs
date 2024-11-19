using System;
using Sandbox.Sdf;
using Sandbox.Worlds;

public interface IWorldOriginEvents : ISceneEvent<IWorldOriginEvents>
{
	void OnWorldOriginMoved( Vector3 offset );
}

public sealed class LocalPlayer : Component
{
	[ConCmd( "teleport", Help = "Teleport to the given coordinates." )]
	public static void TeleportCommand( float x, float y )
	{
		if ( Game.ActiveScene is not { } scene ) return;
		if ( scene.GetComponentInChildren<LocalPlayer>() is not { } player ) return;
		if ( scene.GetComponentInChildren<EditManager>() is not { } editManager ) return;

		player.PreSpawn();
		player.GlobalPosition = new Vector3( x * editManager.CellSize, y * editManager.CellSize, 8192f );
		player.PostSpawn();
	}

	[Property] public float SpawnAreaRadius { get; set; } = 8192f * 8f;

	[RequireComponent] public PlayerController PlayerController { get; private set; } = null!;
	[RequireComponent] public EditWorld EditWorld { get; private set; } = null!;

	public Vector3 GlobalPosition
	{
		get => WorldPosition - (Scene.GetAllComponents<StreamingWorld>().FirstOrDefault()?.WorldPosition ?? Vector3.Zero);
		set
		{
			WorldPosition = value + (Scene.GetAllComponents<StreamingWorld>().FirstOrDefault()?.WorldPosition ?? Vector3.Zero);
			RecenterWorld();
		}
	}

	private bool _justSpawned;
	private string? _cookieKey;

	protected override void OnStart()
	{
		var clothing = ClothingContainer.CreateFromLocalUser();

		clothing.Apply( PlayerController.Renderer );

		PreSpawn();
	}

	private void PreSpawn()
	{
		EditWorld.Enabled = false;
		PlayerController.Enabled = false;
		PlayerController.Body.Gravity = false;
	}

	private void PostSpawn()
	{
		_justSpawned = true;
	}

	public void Spawn( string uri, string seed )
	{
		var hash = seed.FastHash();

		_cookieKey = $"world.{hash}.player";

		var playerHash = HashCode.Combine( hash, Game.SteamId );
		var random = new Random( playerHash );
		var defaultPos = new Vector3( random.VectorInCircle( SpawnAreaRadius ), 8192f );
		var spawnPos = Cookie.Get( $"{_cookieKey}.pos", defaultPos );

		WorldPosition = spawnPos;
		RecenterWorld();
		
		PlayerController.EyeAngles = Cookie.Get( $"{_cookieKey}.rot", Rotation.FromYaw( random.NextSingle() * 360f ) );
		PlayerController.Renderer.LocalRotation = PlayerController.EyeAngles.WithPitch( 0f );

		// TODO: rotation snaps back to 0 after enabling player controller

		if ( Scene.Camera is { } camera )
		{
			camera.WorldPosition = WorldPosition + Vector3.Up * 64 - PlayerController.EyeAngles.ToRotation().Forward * 128f;
			camera.WorldRotation = PlayerController.EyeAngles;
		}

		PostSpawn();
	}

	private bool IsWorldReady( StreamingWorld? world )
	{
		if ( world is null ) return false;

		var cellIndex = world.GetCellIndex( WorldPosition, 0 );
		if ( !world.TryGetCell( cellIndex, out var cell ) ) return false;

		return cell is { State: CellState.Ready, Opacity: >= 1f };
	}

	protected override void OnUpdate()
	{
		var world = Scene.GetAllComponents<StreamingWorld>().FirstOrDefault();

		if ( !IsWorldReady( world ) ) return;

		RecenterWorld();

		if ( _justSpawned )
		{
			_justSpawned = false;

			PlayerController.Enabled = true;
			PlayerController.Body.Gravity = true;
		}

		if ( _cookieKey is not null && PlayerController.IsOnGround )
		{
			EditWorld.Enabled = true;
			Cookie.Set( $"{_cookieKey}.pos", WorldPosition - world!.WorldPosition );
			Cookie.Set( $"{_cookieKey}.rot", PlayerController.EyeAngles );
		}
	}

	public void RecenterWorld()
	{
		var editManager = Scene.GetComponentInChildren<EditManager>();
		var world = Scene.GetComponentInChildren<StreamingWorld>( true );

		var cellSize = editManager.CellSize;

		if ( WorldPosition.x >= -cellSize && WorldPosition.x <= cellSize && WorldPosition.y >= -cellSize && WorldPosition.y <= cellSize )
		{
			return;
		}

		var cellIndex = editManager.WorldToCell( WorldPosition );
		var worldOffset = editManager.CellToWorld( cellIndex );

		Log.Info( $"Recentering! {worldOffset}" );

		foreach ( var child in Scene.Children )
		{
			if ( child.Tags.Has( "absolute" ) ) continue;

			child.WorldPosition -= worldOffset;
		}

		foreach ( var sdfWorld in Scene.GetAllComponents<Sdf3DWorld>() )
		{
			sdfWorld.UpdateTransform();
		}

		Scene.RenderAttributes.Set( "_SdfWorldOffset", -world.WorldPosition );

		IWorldOriginEvents.Post( x => x.OnWorldOriginMoved( -worldOffset ) );
	}
}
