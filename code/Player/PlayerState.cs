using System;

[Flags]
public enum PlayerStateFlags : byte
{
	IsOnGround = 1,
	IsDucking = 2,
	IsSwimming = 4
}

public record struct PlayerState( Vector3 Pos, float Yaw, PlayerStateFlags Flags )
{
	public CompressedPlayerState Compress( float cellSize )
	{
		var relPos = Pos / cellSize;
		var relYaw = Yaw / 360f;

		relYaw -= MathF.Floor( relYaw );

		return new CompressedPlayerState(
			(ushort)Math.Clamp( MathF.Round( relPos.x * 65536f ), 0, ushort.MaxValue ),
			(ushort)Math.Clamp( MathF.Round( relPos.y * 65536f ), 0, ushort.MaxValue ),
			(ushort)Math.Clamp( MathF.Round( relPos.z * 65536f ), 0, ushort.MaxValue ),
			(byte)Math.Clamp( MathF.Round( relYaw * 256f ), 0, byte.MaxValue ),
			Flags );
	}
}

public record struct CompressedPlayerState(
	ushort PosX,
	ushort PosY,
	ushort PosZ,
	byte Yaw,
	PlayerStateFlags Flags )
{
	public const int SizeBytes = 8;

	public void Write( Span<byte> span )
	{
		BitConverter.TryWriteBytes( span[..2], PosX );
		BitConverter.TryWriteBytes( span[2..4], PosY );
		BitConverter.TryWriteBytes( span[4..6], PosZ );

		span[6] = Yaw;
		span[7] = (byte)Flags;
	}

	public static CompressedPlayerState Read( ReadOnlySpan<byte> span )
	{
		return new CompressedPlayerState(
			BitConverter.ToUInt16( span[..2] ),
			BitConverter.ToUInt16( span[2..4] ),
			BitConverter.ToUInt16( span[4..6] ),
			span[6], (PlayerStateFlags)span[7] );
	}

	public PlayerState Decompress( float cellSize )
	{
		var relPos = new Vector3( PosX, PosY, PosZ ) / 65536f;
		var relYaw = Yaw / 256f;

		return new PlayerState( relPos * cellSize, relYaw * 360f, Flags );
	}
}
