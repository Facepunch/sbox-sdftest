using Sandbox.Sdf;
using Sandbox.Worlds;

namespace Sandbox;

public sealed class EditWorld : Component
{
	protected override void OnUpdate()
	{
		if ( !Input.Pressed( "attack1" ) ) return;

		var ray = new Ray( Scene.Camera.Transform.Position, Scene.Camera.Transform.Rotation.Forward );

		var result = Scene.Trace
			.Ray( ray, 8192f )
			.Radius( 16f )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		if ( !result.Hit ) return;

		foreach ( var sdfWorld in Scene.Components.GetAll<Sdf3DWorld>( FindMode.EverythingInSelfAndDescendants ) )
		{
			var origin = sdfWorld.Transform.World.PointToLocal( result.HitPosition );
			var radius = 128f / sdfWorld.Transform.Scale.x;

			if ( origin.x < -radius * 2f ) continue;
			if ( origin.y < -radius * 2f ) continue;
			if ( origin.x > sdfWorld.Size.x + radius * 2f ) continue;
			if ( origin.y > sdfWorld.Size.y + radius * 2f ) continue;

			_ = sdfWorld.SubtractAsync( new SphereSdf3D( origin, radius ) );
		}
	}
}
