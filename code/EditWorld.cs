using System;
using Sandbox.Sdf;

namespace Sandbox;

#nullable enable

public sealed class EditWorld : Component
{
	[Property] public Sdf3DVolume Material { get; set; } = null!;
	[Property] public GameObject CursorPrefab { get; set; } = null!;

	[Property] public float Radius { get; set; } = 64f;
	[Property] public float MaxRange { get; set; } = 4096f;
	[Property] public float CooldownTime { get; set; } = 0.5f;

	[Property] public Color Color { get; set; } = Color.White;

	private Vector3 _editPos;
	private TimeSince _lastEdit;

	private GameObject? _cursor;

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		var ray = new Ray( Scene.Camera.Transform.Position, Scene.Camera.Transform.Rotation.Forward );

		var result = Scene.Trace
			.Ray( ray, MaxRange )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		_editPos = ray.Project( result.Hit ? Math.Max( result.Distance, Radius + 32f ) : MaxRange );
		_editPos.z = Math.Clamp( _editPos.z, 192f, 8000f );

		Radius = Math.Clamp( Radius * MathF.Pow( 2f, Input.MouseWheel.y / 4f ), 64f, 128f );

		if ( !_cursor.IsValid() )
		{
			_cursor = CursorPrefab.Clone( _editPos );
		}

		var opacity = MathF.Pow( Math.Clamp( _lastEdit / CooldownTime, 0f, 1f ), 8f );
		var color = Color.WithAlpha( Color.a * opacity );

		_cursor.Transform.Position = _editPos;
		_cursor.Transform.LocalScale = Radius / 32f;
		_cursor.Components.Get<ModelRenderer>().Tint = color.WithAlpha( color.a * 0.125f );

		Scene.RenderAttributes.Set( "_SdfCursorPosition", _editPos );
		Scene.RenderAttributes.Set( "_SdfCursorRadius", Radius );
		Scene.RenderAttributes.Set( "_SdfCursorColor", color );

		if ( _lastEdit < CooldownTime || !Input.Pressed( "attack1" ) && !Input.Pressed( "attack2" ) ) return;

		BroadcastModify( _editPos, Radius, Input.Down( "attack1" ) ? Material : null );

		_lastEdit = 0f;
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
