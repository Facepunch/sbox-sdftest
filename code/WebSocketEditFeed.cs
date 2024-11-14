using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Sandbox.Worlds;

namespace Sandbox;

public sealed class WebSocketEditFeed : Component, ICellEditFeedFactory
{
	[Property] public string Uri { get; set; }
	[Property] public string ServiceName { get; set; } = "SdfWorldServer";

	[Property] public GameObject RemotePlayerPrefab { get; set; }

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
		PlayerState
	}

	[JsonDerivedType( typeof(WorldParameterMessage), "WorldParameter" )]
	[JsonDerivedType( typeof( PlayerInfoMessage ), "PlayerInfo" )]
	private record ServerMessage;
	private record WorldParameterMessage( string Seed, string Parameters ) : ServerMessage;
	private record PlayerInfoMessage( long SteamId, string Name, string Clothing ) : ServerMessage;

	private void OnMessageReceived( string message )
	{
		try
		{
			var contents = JsonSerializer.Deserialize<ServerMessage>( message );

			switch ( contents )
			{
				case WorldParameterMessage worldParameterMessage:
				{
					if ( _receivedWorldParams ) return;

					var cellLoader = Scene.GetComponentInChildren<SdfCellLoader>();

					if ( worldParameterMessage.Seed is { } seed )
					{
						cellLoader.Seed = seed;
					}

					if ( worldParameterMessage.Parameters is { } parameters )
					{
						var resource = new WorldParameters();

						resource.LoadFromJson( parameters );

						cellLoader.Parameters = resource;
					}

					_receivedWorldParams = true;

					var world = Scene.GetComponentInChildren<StreamingWorld>( true );

					world.GameObject.Enabled = true;
					break;
				}

				case PlayerInfoMessage playerInfoMessage:
				{
					Log.Info( playerInfoMessage );

					if ( !RemotePlayers.TryGetValue( playerInfoMessage.SteamId, out var player ) )
					{
						Log.Warning( $"Unknown player: {playerInfoMessage.SteamId}" );
						break;
					}

					player.SetInfo( playerInfoMessage.SteamId, playerInfoMessage.Name, playerInfoMessage.Clothing );
					break;
				}
			}
		}
		catch ( Exception ex )
		{
			Log.Warning( ex );
		}
	}

	private void OnDataReceived( Span<byte> data )
	{
		if ( data.Length < 12 ) return;

		var kind = (MessageKind)BitConverter.ToUInt16( data[..2] );
		var cellX = BitConverter.ToInt32( data[4..8] );
		var cellY = BitConverter.ToInt32( data[8..12] );
		var cellIndex = new Vector2Int( cellX, cellY );

		if ( !_cellFeeds.TryGetValue( cellIndex, out var feed ) ) return;

		feed.OnDataReceived( kind, data[12..] );
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

		var playerInfo = new PlayerInfoMessage( Game.SteamId, Utility.Steam.PersonaName, ClothingContainer.CreateFromLocalUser().Serialize() );

		await _socket.Send( JsonSerializer.Serialize( playerInfo ) );
	}

	protected override void OnDestroy()
	{
		_socket?.Dispose();
		_socket = null;
	}

	[field: ThreadStatic]
	private static byte[] MoveUpdateBuffer { get; set; }

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

		Submit( MessageKind.PlayerState, cellIndex, buffer );
	}

	private Dictionary<long, RemotePlayer> RemotePlayers { get; } = new();

	private void UpdatePlayerState( long steamId, PlayerState state, float updatePeriod )
	{
		if ( !RemotePlayers.TryGetValue( steamId, out var player ) )
		{
			player = RemotePlayers[steamId] = RemotePlayerPrefab
				.Clone( state.Pos )
				.GetComponent<RemotePlayer>();
		}

		player.MoveTo( state.Pos, Rotation.FromYaw( state.Yaw ), updatePeriod );
		player.SetFlags( state.Flags );
	}

	public ICellEditFeed CreateCellEditFeed( EditManager editManager, Vector2Int cellIndex )
	{
		var feed = _cellFeeds[cellIndex] = new WebSocketCellEditFeed( editManager, this, cellIndex );

		Submit( MessageKind.Subscribe, cellIndex, ReadOnlySpan<byte>.Empty );

		return feed;
	}

	private class WebSocketCellEditFeed : ICellEditFeed
	{
		private readonly List<CompressedEditData> _edits = new();

		private bool _isLoaded;
		public EditManager EditManager { get; }
		private WebSocketEditFeed Parent { get; }
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

			Parent.Submit( MessageKind.Edit, CellIndex, buffer );

			Edited?.Invoke( this, data );
		}

		public WebSocketCellEditFeed( EditManager editManager, WebSocketEditFeed parent, Vector2Int cellIndex )
		{
			EditManager = editManager;
			Parent = parent;

			CellIndex = cellIndex;
		}

		public void OnDataReceived( MessageKind kind, Span<byte> data )
		{
			switch ( kind )
			{
				case MessageKind.Edit:
				{
					if ( data.Length < CompressedEditData.SizeBytes ) return;

					var edit = CompressedEditData.Read( data );

					_edits.Add( edit );

					Edited?.Invoke( this, edit );
					break;
				}

				case MessageKind.PlayerState:
				{
					if ( data.Length < 8 ) return;

					var count = BitConverter.ToInt32( data[..4] );
					var updatePeriod = BitConverter.ToSingle( data[4..8] );

					var cellPos = EditManager.CellToWorld( CellIndex );

					const int stride = 8 + CompressedPlayerState.SizeBytes;

					data = data[8..];

					if ( data.Length < count * stride ) return;

					for ( int i = 0, offset = 0; i < count; ++i, offset += stride )
					{
						var steamId = BitConverter.ToInt64( data[offset..] );
						var state = CompressedPlayerState
							.Read( data[(offset + 8)..] )
							.Decompress( EditManager.CellSize );

						Parent.UpdatePlayerState( steamId, state with { Pos = state.Pos + cellPos }, updatePeriod );
					}

					break;
				}
			}
		}

		public void Dispose()
		{
			Parent.Submit( MessageKind.Unsubscribe, CellIndex, ReadOnlySpan<byte>.Empty );
		}
	}
}
