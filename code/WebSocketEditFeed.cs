using System;
using System.Text.Json;
using System.Threading.Tasks;
using Sandbox.Worlds;

namespace Sandbox;

public sealed class WebSocketEditFeed : Component, ICellEditFeedFactory
{
	[Property] public string Uri { get; set; }
	[Property] public string ServiceName { get; set; } = "SdfWorldServer";

	private bool _receivedWorldParams;
	private WebSocket _socket;
	private readonly Dictionary<Vector2Int, WebSocketCellEditFeed> _cellFeeds = new();

	private RealTimeSince _lastMoveMessage;

	public const float MoveMessagePeriod = 0.5f;

	protected override void OnStart()
	{
		if ( Uri is null )
		{
			Log.Warning( $"No {nameof(Uri)} provided!" );
			return;
		}

		_socket = new WebSocket();
		_socket.OnMessageReceived += OnMessageReceived;
		_socket.OnDataReceived += OnDataReceived;

		_ = ConnectAsync();
	}

	private enum MessageKind : ushort
	{
		Subscribe,
		Unsubscribe,
		Edit,
		PlayerMove
	}

	private record WorldParameterMessage( string Seed, string Parameters );

	private record PlayerInfoMessage( string Name, string Clothing );

	private void OnMessageReceived( string message )
	{
		if ( _receivedWorldParams ) return;

		try
		{
			var contents = JsonSerializer.Deserialize<WorldParameterMessage>( message );
			var cellLoader = Scene.GetComponentInChildren<SdfCellLoader>();

			if ( contents.Seed is { } seed )
			{
				cellLoader.Seed = seed;
			}

			if ( contents.Parameters is { } parameters )
			{
				var resource = new WorldParameters();

				resource.LoadFromJson( parameters );

				cellLoader.Parameters = resource;
			}

			_receivedWorldParams = true;

			var world = Scene.GetComponentInChildren<StreamingWorld>( true );

			world.GameObject.Enabled = true;
		}
		catch ( Exception ex )
		{
			Log.Warning( ex );
		}
	}

	private void OnDataReceived( Span<byte> data )
	{
		if ( data.Length < 4 ) return;

		var kind = (MessageKind)BitConverter.ToUInt16( data[..2] );

		data = data[4..];

		switch ( kind )
		{
			case MessageKind.Edit:
				OnEditReceived( data );
				return;
		}
	}

	private void OnEditReceived( Span<byte> data )
	{
		if ( data.Length < 16 ) return;

		var cellX = BitConverter.ToInt32( data[..4] );
		var cellY = BitConverter.ToInt32( data[4..8] );

		var cellIndex = new Vector2Int( cellX, cellY );

		if ( !_cellFeeds.TryGetValue( cellIndex, out var feed ) ) return;

		feed.OnEditReceived( data[8..] );
	}

	private void Submit( MessageKind messageKind, Vector2Int cellIndex, ReadOnlySpan<byte> payload )
	{
		Span<byte> data = new byte[16 + payload.Length];

		BitConverter.TryWriteBytes( data[..2], (ushort)messageKind );
		BitConverter.TryWriteBytes( data[4..8], cellIndex.x );
		BitConverter.TryWriteBytes( data[8..12], cellIndex.y );
		BitConverter.TryWriteBytes( data[12..16], payload.Length );

		payload.CopyTo( data[16..] );

		_ = _socket?.Send( data );
	}

	private async Task ConnectAsync()
	{
		var token = await Sandbox.Services.Auth.GetToken( ServiceName );

		if ( string.IsNullOrEmpty( token ) )
		{
			Log.Error( "Unable to get a valid session token!" );
			return;
		}

		var headers = new Dictionary<string, string>
		{
			{ "X-SteamId", Game.SteamId.ToString() },
			{ "Authorization", token }
		};

		await _socket.Connect( Uri, headers );

		var playerInfo = new PlayerInfoMessage(
			Utility.Steam.PersonaName,
			ClothingContainer.CreateFromLocalUser().Serialize() );

		await _socket.Send( JsonSerializer.Serialize( playerInfo ) );
	}

	protected override void OnDestroy()
	{
		_socket?.Dispose();
		_socket = null;
	}

	[field: ThreadStatic]
	private static byte[] MoveUpdateBuffer { get; set; }

	[Flags]
	public enum PlayerStateFlags : byte
	{
		IsOnGround,
		IsDucking,
		IsSwimming
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
	}

	protected override void OnFixedUpdate()
	{
		if ( _lastMoveMessage < MoveMessagePeriod ) return;

		var editManager = Scene.GetAllComponents<EditManager>().FirstOrDefault();
		var player = Scene.GetAllComponents<LocalPlayer>().FirstOrDefault();

		if ( editManager is null || _socket?.IsConnected is not true || player is null )
		{
			return;
		}

		var controller = player.PlayerController;
		var cellIndex = editManager.WorldToCell( player.WorldPosition );
		var localPos = player.WorldPosition - editManager.CellToWorld( cellIndex );

		_lastMoveMessage = 0f;

		Span<byte> buffer = MoveUpdateBuffer ??= new byte[8];

		var state = new PlayerState( localPos, controller.EyeAngles.yaw,
			(controller.IsOnGround ? PlayerStateFlags.IsOnGround : 0) |
			(controller.IsDucking ? PlayerStateFlags.IsDucking : 0) |
			(controller.IsSwimming ? PlayerStateFlags.IsSwimming : 0) );

		state
			.Compress( editManager.CellSize )
			.Write( buffer );

		Submit( MessageKind.PlayerMove, cellIndex, buffer );
	}

	public ICellEditFeed CreateCellEditFeed( Vector2Int cellIndex )
	{
		var feed = _cellFeeds[cellIndex] = new WebSocketCellEditFeed( this, cellIndex );

		Submit( MessageKind.Subscribe, cellIndex, ReadOnlySpan<byte>.Empty );

		return feed;
	}

	private class WebSocketCellEditFeed : ICellEditFeed
	{
		private readonly WebSocketEditFeed _parent;
		private readonly List<CompressedEditData> _edits = new();

		private bool _isLoaded;

		public Vector2Int CellIndex { get; }

		public IReadOnlyList<CompressedEditData> Edits => _edits;

		public event CellEditedDelegate Edited;

		[field: ThreadStatic]
		private static byte[] SubmitBuffer { get; set; }

		public void Submit( CompressedEditData data )
		{
			_edits.Add( data );

			Span<byte> buffer = SubmitBuffer ??= new byte[8];

			data.Write( buffer );

			_parent.Submit( MessageKind.Edit, CellIndex, buffer );

			Edited?.Invoke( this, data );
		}

		public WebSocketCellEditFeed( WebSocketEditFeed parent, Vector2Int cellIndex )
		{
			_parent = parent;

			CellIndex = cellIndex;
		}

		public void OnEditReceived( Span<byte> data )
		{
			if ( data.Length < CompressedEditData.SizeBytes ) return;

			var edit = CompressedEditData.Read( data );

			_edits.Add( edit );

			Edited?.Invoke( this, edit );
		}

		public void Dispose()
		{
			_parent.Submit( MessageKind.Unsubscribe, CellIndex, ReadOnlySpan<byte>.Empty );
		}
	}
}
