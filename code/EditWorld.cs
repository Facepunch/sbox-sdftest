using System;
using Sandbox.Sdf;

namespace Sandbox;

#nullable enable

public sealed class EditWorld : Component
{
	[Property] public Sdf3DVolume Material { get; set; } = null!;

	private float _editRange = 512f;
	private Vector3 _lastEditPos;

	protected override void OnUpdate()
	{
		var ray = new Ray( Scene.Camera.Transform.Position, Scene.Camera.Transform.Rotation.Forward );
		var maxRange = Input.Down( "attack2" ) ? 4096f : 1024f;

		if ( Input.Pressed( "attack1" ) || Input.Pressed( "attack2" ) )
		{
			var result = Scene.Trace
				.Ray( ray, maxRange )
				.Radius( 16f )
				.IgnoreGameObjectHierarchy( GameObject )
				.Run();

			_editRange = result.Hit ? result.Distance : maxRange;
		}

		if ( !Input.Down( "attack1" ) && !Input.Down( "attack2" ) ) return;

		var editPos = ray.Project( _editRange );

		editPos.z = Math.Clamp( editPos.z, 192f, 8000f );

		if ( (editPos - _lastEditPos).LengthSquared < 32f * 32f ) return;

		_lastEditPos = editPos;

		BroadcastModify( editPos, 128f, Input.Down( "attack1" ) ? Material : null );
	}

	[Broadcast]
	private void BroadcastModify( Vector3 pos, float radius, Sdf3DVolume? material = null )
	{
		foreach ( var sdfWorld in Scene.Components.GetAll<Sdf3DWorld>( FindMode.EverythingInSelfAndDescendants ) )
		{
			var origin = sdfWorld.Transform.World.PointToLocal( pos );
			var localRadius = radius / sdfWorld.Transform.Scale.x;

			if ( origin.x < -localRadius * 2f ) continue;
			if ( origin.y < -localRadius * 2f ) continue;
			if ( origin.x > sdfWorld.Size.x + localRadius * 2f ) continue;
			if ( origin.y > sdfWorld.Size.y + localRadius * 2f ) continue;

			if ( material is null )
			{
				_ = sdfWorld.SubtractAsync( new SphereSdf3D( origin, localRadius ) );
			}
			else
			{
				_ = sdfWorld.AddAsync( new SphereSdf3D( origin, localRadius ), material );
			}
		}
	}
}
