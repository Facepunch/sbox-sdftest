using Sandbox.Citizen;

public sealed class RemotePlayer : Component, IWorldOriginEvents
{
	[Property] public SkinnedModelRenderer Renderer { get; set; }
	[Property] public NamePlate NamePlate { get; set; }
	[RequireComponent] public CitizenAnimationHelper AnimationHelper { get; private set; }

	private Transform _startTransform;
	private Transform _endTransform;
	private TimeUntil _moveTime;
	private TimeUntil _sitTime;
	private TimeUntil _despawnTime;

	protected override void OnStart()
	{
		_sitTime = 60f;
		_despawnTime = 600f;
	}

	public void SetFlags( PlayerStateFlags flags )
	{
		AnimationHelper.IsGrounded = (flags & PlayerStateFlags.IsOnGround) != 0;
		AnimationHelper.DuckLevel = (flags & PlayerStateFlags.IsDucking) != 0 ? 1f : 0f;
		AnimationHelper.IsSwimming = (flags & PlayerStateFlags.IsSwimming) != 0;
	}

	public void MoveTo( Vector3 position, Rotation rotation, float duration )
	{
		_startTransform = new Transform( WorldPosition, Renderer.LocalRotation );
		_endTransform = new Transform( position, rotation );

		_moveTime = duration;
		_sitTime = duration * 2f + 5f;
		_despawnTime = duration * 2f + 600f;

		var velocity = (_endTransform.Position - _startTransform.Position) / duration;

		AnimationHelper.WithVelocity( velocity );
		AnimationHelper.WithWishVelocity( velocity );
	}

	protected override void OnUpdate()
	{
		var t = _moveTime.Fraction;

		WorldPosition = Vector3.Lerp( _startTransform.Position, _endTransform.Position, t );
		Renderer.LocalRotation = Rotation.Slerp( _startTransform.Rotation, _endTransform.Rotation, t );

		if ( _sitTime )
		{
			AnimationHelper.Sitting = CitizenAnimationHelper.SittingStyle.Floor;
		}

		if ( _despawnTime )
		{
			DestroyGameObject();
		}
	}

	public void SetInfo( long steamId, string name, string clothing )
	{
		NamePlate.SteamId = steamId;
		NamePlate.PersonaName = name;

		GameObject.Name = $"{steamId} - {name}";

		var clothingContainer = ClothingContainer.CreateFromJson( clothing );

		clothingContainer.Apply( Renderer );

		NamePlate.LocalPosition = Vector3.Up * (clothingContainer.Height * 64f + 16f);
	}

	void IWorldOriginEvents.OnWorldOriginMoved( Vector3 offset )
	{
		_startTransform.Position += offset;
		_endTransform.Position += offset;
	}
}
