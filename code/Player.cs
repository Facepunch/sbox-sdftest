using Sandbox;
using Sandbox.Worlds;

public sealed class Player : Component, Component.INetworkSpawn
{
	[Property]
	public CameraComponent Camera { get; set; }

	[RequireComponent]
	public PlayerController PlayerController { get; private set; }

	public void OnNetworkSpawn( Connection owner )
	{
		Camera.GameObject.Enabled = owner == Connection.Local;

		if ( owner == Connection.Local )
		{
			WorldPosition = Cookie.Get( "player.pos", WorldPosition );
			WorldRotation = Cookie.Get( "player.rot", WorldRotation );
		}
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		if ( PlayerController.CharacterController.IsOnGround )
		{
			Cookie.Set( "player.pos", WorldPosition );
			Cookie.Set( "player.rot", WorldRotation );
		}

		var world = Scene.GetComponentInChildren<StreamingWorld>( true );

		if ( world is null )
		{
			PlayerController.Frozen = true;
			return;
		}

		var cellIndex = world.GetCellIndex( WorldPosition, 0 );

		PlayerController.Frozen = !world.TryGetCell( cellIndex, out var cell ) || cell.State != CellState.Ready || cell.Opacity < 1f;
	}
}
