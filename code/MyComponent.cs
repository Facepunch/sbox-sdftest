
using System;
using System.Threading.Tasks;
using Sandbox.Sdf;

public sealed class MyComponent : Component
{
	[Property]
	public Sdf2DWorld SdfWorld { get; set; }

	[Property]
	public GameObject Cursor { get; set; }

	[Property]
	public Sdf2DLayer Layer { get; set; }

	private Vector3 _lastHitPos;

	protected override void OnStart()
	{
		base.OnStart();

		if ( Connection.Local == Connection.Host )
		{
			SdfWorld.Network.TakeOwnership();
		}

		if ( SdfWorld.Network.IsOwner )
		{
			_ = CreateWorld();
		}
	}

	private async Task CreateWorld()
	{
		await SdfWorld.AddAsync( new CircleSdf( 0f, 160f ), Layer );
	}

	protected override void OnUpdate()
	{
		if ( !SdfWorld.Network.IsOwner )
		{
			return;
		}

		var plane = new Plane( Transform.Position + Transform.Rotation.Up * 20f, Transform.Rotation.Up );
		var ray = Scene.Camera.ScreenPixelToRay( Mouse.Position );

		if ( !plane.TryTrace( ray, out var hit ) )
		{
			return;
		}

		Cursor.Transform.Position = hit;

		var hitLocal = Transform.World.PointToLocal( hit );

		if ( Input.Down( "attack1" ) && _lastHitPos != hit )
		{
			_lastHitPos = hit;
			_ = SdfWorld.SubtractAsync( new CircleSdf( hitLocal, 16f ), Layer );
		}
	}
}
