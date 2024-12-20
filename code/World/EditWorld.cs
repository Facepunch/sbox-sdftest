﻿using System;

namespace Sandbox;

#nullable enable

public sealed class EditWorld : Component
{
	[Property] public GameObject CursorPrefab { get; set; } = null!;

	[Property] public float Radius { get; set; } = 64f;
	[Property] public float MaxRange { get; set; } = 4096f;
	[Property] public float CooldownTime { get; set; } = 0.5f;

	[Property] public Color Color { get; set; } = Color.White;

	private Vector3 _editPos;
	private TimeSince _lastEdit;

	private GameObject? _cursor;

	private void DestroyCursor()
	{
		_cursor?.Destroy();
		_cursor = null;

		Scene.RenderAttributes.Set( "_SdfCursorPosition", Vector3.Zero );
		Scene.RenderAttributes.Set( "_SdfCursorRadius", 0f );
		Scene.RenderAttributes.Set( "_SdfCursorColor", Color.Transparent );
	}

	protected override void OnDisabled()
	{
		DestroyCursor();
	}

	protected override void OnDestroy()
	{
		DestroyCursor();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		var ray = new Ray( Scene.Camera.WorldPosition, Scene.Camera.WorldRotation.Forward );

		var result = Scene.Trace
			.Ray( ray, MaxRange )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		_editPos = ray.Project( result.Hit ? Math.Max( result.Distance, Radius + 32f ) : MaxRange );
		_editPos.z = Math.Clamp( _editPos.z, 192f, 8000f );

		Radius = Math.Clamp( Radius * MathF.Pow( 2f, Input.MouseWheel.y / 4f ), 64f, 512f );

		if ( !_cursor.IsValid() )
		{
			_cursor = CursorPrefab.Clone( _editPos );
		}

		var opacity = MathF.Pow( Math.Clamp( _lastEdit / CooldownTime, 0f, 1f ), 8f );
		var color = Color.WithAlpha( Color.a * opacity );

		_cursor.WorldPosition = _editPos;
		_cursor.WorldScale = Radius / 32f;
		_cursor.Components.Get<ModelRenderer>().Tint = color.WithAlpha( color.a * 0.05f );

		Scene.RenderAttributes.Set( "_SdfCursorPosition", _editPos );
		Scene.RenderAttributes.Set( "_SdfCursorRadius", Radius );
		Scene.RenderAttributes.Set( "_SdfCursorColor", color );

		if ( _lastEdit < CooldownTime || !Input.Pressed( "attack1" ) && !Input.Pressed( "attack2" ) ) return;

		Scene.GetAllComponents<EditManager>().FirstOrDefault()?
			.Submit( Input.Down( "attack1" ) ? EditKind.Add : EditKind.Subtract, Radius, _editPos );

		_lastEdit = 0f;
	}
}
