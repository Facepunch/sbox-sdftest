using Sandbox.Worlds;

public sealed class LocalPlayer : Component
{
	[RequireComponent]
	public PlayerController PlayerController { get; private set; }

	[RequireComponent]
	public EditWorld EditWorld { get; private set; }

	private bool _justSpawned;

	protected override void OnStart()
	{
		var clothing = ClothingContainer.CreateFromLocalUser();

		clothing.Apply( PlayerController.Renderer );

		WorldPosition = Cookie.Get( "player.pos", WorldPosition );
		PlayerController.EyeAngles = Cookie.Get( "player.rot", PlayerController.EyeAngles );
		PlayerController.Renderer.LocalRotation = PlayerController.EyeAngles.WithPitch( 0f );

		// TODO: rotation snaps back to 0 after enabling player controller

		if ( Scene.Camera is { } camera )
		{
			camera.WorldPosition = WorldPosition + Vector3.Up * 64 - PlayerController.EyeAngles.ToRotation().Forward * 128f;
			camera.WorldRotation = PlayerController.EyeAngles;
		}

		EditWorld.Enabled = false;
		PlayerController.Enabled = false;
		PlayerController.Body.Gravity = false;

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

		if ( PlayerController.IsOnGround )
		{
			Cookie.Set( "player.pos", WorldPosition );
			Cookie.Set( "player.rot", PlayerController.EyeAngles );
		}
	}
}
