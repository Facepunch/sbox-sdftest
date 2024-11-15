using System;
using Sandbox.Sdf;
using Sandbox.Worlds;

public sealed class LocalPlayer : Component
{
	[Property] public float SpawnAreaRadius { get; set; } = 8192f * 8f;

	[RequireComponent]
	public PlayerController PlayerController { get; private set; }

	[RequireComponent]
	public EditWorld EditWorld { get; private set; }

	public Vector3 GlobalPosition => WorldPosition - (Scene.GetAllComponents<StreamingWorld>().FirstOrDefault()?.WorldPosition ?? Vector3.Zero);

	private bool _justSpawned;
	private string _cookieKey;

	protected override void OnStart()
	{
		var clothing = ClothingContainer.CreateFromLocalUser();

		clothing.Apply( PlayerController.Renderer );

		EditWorld.Enabled = false;
		PlayerController.Enabled = false;
		PlayerController.Body.Gravity = false;
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

		_justSpawned = true;
	}

	private bool IsWorldReady( StreamingWorld world )
	{
		if ( world is null ) return false;

		var cellIndex = world.GetCellIndex( WorldPosition, 0 );
		if ( !world.TryGetCell( cellIndex, out var cell ) ) return false;

		return cell.State == CellState.Ready && cell.Opacity >= 1f;
	}

	protected override void OnUpdate()
	{
		var world = Scene.GetAllComponents<StreamingWorld>().FirstOrDefault();

		if ( !IsWorldReady( world ) ) return;

		RecenterWorld();

		if ( _justSpawned )
		{
			_justSpawned = false;

			EditWorld.Enabled = true;
			PlayerController.Enabled = true;
			PlayerController.Body.Gravity = true;
		}

		if ( _cookieKey is not null && PlayerController.IsOnGround )
		{
			Cookie.Set( $"{_cookieKey}.pos", WorldPosition - world!.WorldPosition );
			Cookie.Set( $"{_cookieKey}.rot", PlayerController.EyeAngles );
		}
	}

	public void RecenterWorld()
	{
		var editManager = Scene.GetComponentInChildren<EditManager>();

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

			foreach ( var sdfWorld in child.GetComponentsInChildren<Sdf3DWorld>() )
			{
				sdfWorld.UpdateTransform();
			}
		}
	}
}
