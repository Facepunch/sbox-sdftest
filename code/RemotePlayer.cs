using Sandbox.Citizen;

public sealed class RemotePlayer : Component
{
	[Property] public SkinnedModelRenderer Renderer { get; set; }
	[Property] public NamePlate NamePlate { get; set; }
	[RequireComponent] public CitizenAnimationHelper AnimationHelper { get; private set; }

	private Transform _startTransform;
	private Transform _endTransform;
	private TimeUntil _moveTime;

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

		var velocity = (_endTransform.Position - _startTransform.Position) / duration;

		AnimationHelper.WithVelocity( velocity );
		AnimationHelper.WithWishVelocity( velocity );
	}

	protected override void OnUpdate()
	{
		var t = _moveTime.Fraction;

		WorldPosition = Vector3.Lerp( _startTransform.Position, _endTransform.Position, t );
		Renderer.LocalRotation = Rotation.Slerp( _startTransform.Rotation, _endTransform.Rotation, t );
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
}
