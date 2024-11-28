using System;
using System.Diagnostics;
using Sandbox.Diagnostics;
using Sandbox.Movement;
using Sandbox.Utility;

namespace SdfWorld;

[Title( "MoveMode - Climb" ), Group( "Movement" ), Icon( "hiking" )]
public sealed class MoveModeClimb : MoveMode, IClimbingMode, Component.ICollisionListener, IWorldOriginEvents
{
	[Property]
	public int Priority { get; set; } = 5;

	/// <summary>
	/// A list of tags we can climb up.
	/// </summary>
	[Property]
	public TagSet ClimbableTags { get; set; } = new() { "world" };

	/// <summary>
	/// The GameObject we're climbing. This will usually be a ladder trigger.
	/// </summary>
	public GameObject? ClimbingObject { get; private set; }

	/// <summary>
	/// When climbing, this is the rotation of the wall/ladder you're climbing, where
	/// Forward is the direction to look at the ladder, and Up is the direction to climb.
	/// </summary>
	public Rotation ClimbingRotation { get; private set; }

	private class Limb
	{
		public MoveModeClimb MoveMode { get; }
		public string Name { get; }

		private readonly Func<Transform> _getRestTransform;

		public Limb( MoveModeClimb moveMode, string name, Func<Transform> getRestTransform )
		{
			MoveMode = moveMode;
			Name = name;
			_getRestTransform = getRestTransform;

			_from = _to = getRestTransform();
			_moveTime = 0f;
		}

		public bool Moving => _moveTime.Relative > 0.25f;
		public bool HasHold { get; private set; }

		private Transform _from;
		private Transform _to;

		private TimeUntil _moveTime;

		public Transform Transform
		{
			get
			{
				var eased = Transform.Lerp( _from, _to, Easing.SineEaseInOut( _moveTime.Fraction ), true );

				eased.Position += MoveMode.ClimbingRotation.Backward * 12f * MathF.Sin( _moveTime.Fraction * MathF.PI );

				return eased;
			}
		}

		public float MoveScore => Math.Clamp( _moveTime.Passed, 0f, 1f ) + (_to.Position - IdealTransform.Position).Length.Remap( 0f, 64f ) * 2f;

		public bool TryFindHold( Vector3 origin, Rotation rotation, float duration )
		{
			HasHold = false;

			var restTransform = _getRestTransform();

			origin += rotation * restTransform.Position;
			var trace = MoveMode.Trace( origin - rotation.Forward * 16f, rotation.Forward, 48f )
				.Run();

			if ( !trace.Hit ) return false;

			HasHold = true;

			_from = _to;
			_to = new Transform( trace.HitPosition - trace.Direction * 2f, MoveMode.ClimbingRotation * restTransform.Rotation );
			_moveTime = duration;

			return true;
		}

		public Transform IdealTransform
		{
			get
			{
				var position = MoveMode.Controller.WorldPosition + MoveMode.Controller.WishVelocity;
				var rotation = MoveMode.ClimbingRotation;
				var restTransform = _getRestTransform();

				return new Transform( position + rotation * restTransform.Position, rotation * restTransform.Rotation );
			}
		}

		public Transform IdealBodyTransform
		{
			get
			{
				var restTransform = _getRestTransform();
				var rotation = _to.Rotation * restTransform.Rotation.Inverse;
				var position = _to.Position - rotation * restTransform.Position;

				return new Transform( position, rotation );
			}
		}

		public void UpdateRenderer( SkinnedModelRenderer renderer )
		{
			renderer.SetIk( Name, Transform );
		}

		public void CancelAnimation( SkinnedModelRenderer renderer )
		{
			renderer.Set( $"ik.{Name}.enabled", false );
		}

		public void WorldOriginMoved( Vector3 offset )
		{
			_from.Position += offset;
			_to.Position += offset;
		}
	}

	private readonly Limb[] _limbs;

	public override bool AllowFalling => !ClimbingObject.IsValid();

	[Property, Group( "Limbs" )] public Vector3 FootOffset { get; set; } = new ( 4f, 16f, 20f );
	[Property, Group( "Limbs" )] public Vector3 HandOffset { get; set; } = new ( 4f, 16f, 56f );

	[Property, Group( "Limbs" )] public Angles FootAngles { get; set; } = new ( 0f, 0f, 90f );
	[Property, Group( "Limbs" )] public Angles HandAngles { get; set; } = new ( -45f, 90f, 90f );

	public Transform LeftFootRestTransform => new ( FootOffset, FootAngles );
	public Transform RightFootRestTransform => new ( FootOffset with { y = -FootOffset.y }, FootAngles with { yaw = -FootAngles.yaw } );
	public Transform LeftHandRestTransform => new ( HandOffset, HandAngles );
	public Transform RightHandRestTransform => new ( HandOffset with { y = -HandOffset.y }, HandAngles with { yaw = -HandAngles.yaw } );

	public MoveModeClimb()
	{
		_limbs = new[]
		{
			new Limb( this, "foot_left", () => LeftFootRestTransform ),
			new Limb( this, "foot_right", () => RightFootRestTransform ),
			new Limb( this, "hand_left", () => LeftHandRestTransform ),
			new Limb( this, "hand_right", () => RightHandRestTransform )
		};
	}

	public override void UpdateRigidBody( Rigidbody body )
	{
		body.Gravity = false;
		body.LinearDamping = 10.0f;
		body.AngularDamping = 1f;
	}

	public override int Score( PlayerController controller )
	{
		return ClimbingObject.IsValid() ? Priority : -100;
	}

	public override void PostPhysicsStep()
	{

	}

	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( ClimbingObject.IsValid() ) return;

		var forward = Controller.EyeAngles.Forward.WithZ( 0f ).Normal;

		if ( collision.Contact.Normal.Dot( forward ) < 0.707f ) return;
		if ( !collision.Other.GameObject.Tags.HasAny( ClimbableTags ) ) return;

		TryStartClimbing( collision.Other.GameObject, collision.Contact.Point, collision.Contact.Normal );
	}

	private SceneTrace Trace( Vector3 from, Vector3 dir, float maxDist )
	{
		return Scene.Trace
			.FromTo( from, from + dir * maxDist )
			.Radius( 2f )
			.WithAnyTags( Tags )
			.IgnoreGameObjectHierarchy( Controller.GameObject );
	}

	private Vector3? Trace( Vector3 origin, Vector2 offset )
	{
		var from = Controller.WorldPosition + ClimbingRotation * new Vector3( -16f, offset.x, offset.y );
		var dir = ClimbingRotation.Forward;

		var result = Trace( from, dir, 64f ).Run();

		// DebugOverlay.Line( from, from + dir * 32f, result.Hit ? Color.Green : Color.Red, 1f );

		return result is { Hit: true, HitPosition: var pos } ? pos : null;
	}

	private bool TryUpdateClimbingRotation( Vector3 origin )
	{
		if ( Trace( origin, new Vector2( -16f, 32f ) ) is not { } left ) return false;
		if ( Trace( origin, new Vector2( +16f, 32f ) ) is not { } right ) return false;

		if ( Trace( origin, new Vector2( 0f, 48f ) ) is not { } up ) return false;
		if ( Trace( origin, new Vector2( 0f, 16f ) ) is not { } down ) return false;

		var vert = up - down;
		var horz = right - left;

		var forward = Vector3.Cross( horz, vert ).Normal;

		ClimbingRotation = Rotation.LookAt( forward, Vector3.Up );

		if ( ClimbingRotation.Up.z < 0.6f )
		{
			StopClimbing();
			return false;
		}

		// DebugOverlay.Box( 0f, new Vector3( 8f, 32f, 64f ), duration: 1f, transform: new Transform( Controller.WorldPosition + ClimbingRotation * new Vector3( 0f, 0f, 32f ), ClimbingRotation ) );

		return true;
	}

	private bool TryStartClimbing( GameObject target, Vector3 origin, Vector3 forward )
	{
		ClimbingRotation = Rotation.LookAt( forward, Vector3.Up );

		if ( !TryUpdateClimbingRotation( origin ) )
		{
			return false;
		}

		ClimbingObject = target;

		foreach ( var limb in _limbs )
		{
			limb.TryFindHold( Controller.WorldPosition, ClimbingRotation, 0f );
		}

		if ( _limbs.Count( x => x.HasHold ) < 3 )
		{
			ClimbingObject = null;
			return false;
		}

		return true;
	}

	private void StopClimbing()
	{
		ClimbingObject = null;

		Controller.Renderer.Set( "special_movement_states", 0 );

		foreach ( var limb in _limbs )
		{
			limb.CancelAnimation( Controller.Renderer );
		}
	}

	protected override void OnUpdate()
	{
		if ( !ClimbingObject.IsValid() ) return;

		Controller.Renderer.Set( "special_movement_states", 1 );

		foreach ( var limb in _limbs )
		{
			limb.UpdateRenderer( Controller.Renderer );
		}
	}

	public override void AddVelocity()
	{
		var idealPos = Vector3.Zero;
		var count = 0;

		foreach ( var limb in _limbs )
		{
			if ( !limb.HasHold ) return;

			idealPos += limb.IdealBodyTransform.Position;
			++count;
		}

		Assert.AreNotEqual( 0, count );

		idealPos /= count;

		var pos = Controller.WorldPosition;
		var delta = idealPos - pos;

		if ( delta.Length > 0.01f )
		{
			Controller.Body.Velocity = Controller.Body.Velocity.AddClamped( delta * 2.0f, delta.Length * 5.0f );
		}
	}

	public override Vector3 UpdateMove( Rotation eyes, Vector3 input )
	{
		// wishVelocity *= 340.0f;

		if ( Input.Down( "jump" ) )
		{
			// Jump away from ladder
			Controller.Jump( ClimbingRotation.Backward * 200 + ClimbingRotation.Up * 200 );
			StopClimbing();
			return default;
		}

		input = input.ClampLength( 1f );

		var wishVelocity = ClimbingRotation * new Vector3( 0, input.y * 32f, input.x * 24f );

		if ( wishVelocity.Length < 8f ) return default;

		foreach ( var limb in _limbs )
		{
			if ( limb.Moving ) return wishVelocity;
		}

		var toMove = _limbs.MaxBy( x => x.MoveScore )!;
		var circle = Random.Shared.VectorInCircle( 4f );

		if ( toMove.TryFindHold( WorldPosition + wishVelocity + ClimbingRotation * new Vector3( 0f, circle.x, circle.y ), ClimbingRotation, 0.5f ) )
		{
			TryUpdateClimbingRotation( Controller.WorldPosition );
		}

		return wishVelocity;
	}

	void IWorldOriginEvents.OnWorldOriginMoved( Vector3 offset )
	{
		foreach ( var limb in _limbs )
		{
			limb.WorldOriginMoved( offset );
		}
	}

	public override void OnModeBegin()
	{
		if ( Controller.GetComponent<EditWorld>( true ) is { } editWorld )
		{
			editWorld.Enabled = false;
		}
	}

	public override void OnModeEnd( MoveMode next )
	{
		if ( Controller.GetComponent<EditWorld>( true ) is { } editWorld )
		{
			editWorld.Enabled = true;
		}

		base.OnModeEnd( next );
	}
}
