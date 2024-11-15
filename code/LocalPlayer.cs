using System;
using Sandbox.Worlds;

public sealed class LocalPlayer : Component
{
	[Property] public float SpawnAreaRadius { get; set; } = 8192f * 256f;

	[RequireComponent]
	public PlayerController PlayerController { get; private set; }

	[RequireComponent]
	public EditWorld EditWorld { get; private set; }

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

		WorldPosition = Cookie.Get( $"{_cookieKey}.pos", defaultPos );
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

	private bool IsWorldReady
	{
		get
		{
			var world = Scene.GetAllComponents<StreamingWorld>()?.FirstOrDefault();
			if ( world is null ) return false;

			var cellIndex = world.GetCellIndex( WorldPosition, 0 );
			if ( !world.TryGetCell( cellIndex, out var cell ) ) return false;

			return cell.State == CellState.Ready && cell.Opacity >= 1f;
		}
	}

	protected override void OnUpdate()
	{
		if ( !IsWorldReady ) return;

		if ( _justSpawned )
		{
			_justSpawned = false;

			EditWorld.Enabled = true;
			PlayerController.Enabled = true;
			PlayerController.Body.Gravity = true;
		}

		if ( _cookieKey is not null && PlayerController.IsOnGround )
		{
			Cookie.Set( $"{_cookieKey}.pos", WorldPosition );
			Cookie.Set( $"{_cookieKey}.rot", PlayerController.EyeAngles );
		}
	}
}
