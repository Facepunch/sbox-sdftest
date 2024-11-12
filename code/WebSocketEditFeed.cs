using System;
using System.Threading.Tasks;

namespace Sandbox;

public sealed class WebSocketEditFeed : Component, ICellEditFeedFactory
{
	[Property] public string Uri { get; set; }
	[Property] public string ServiceName { get; set; } = "SdfWorldServer";

	private WebSocket _socket;
	private readonly Dictionary<Vector2Int, WebSocketCellEditFeed> _cellFeeds = new();

	protected override void OnStart()
	{
		if ( Uri is null )
		{
			Log.Warning( $"No {nameof(Uri)} provided!" );
			return;
		}

		_socket = new WebSocket();
		_socket.OnDataReceived += OnDataReceived;

		_ = ConnectAsync();
	}

	private enum MessageKind : ushort
	{
		Subscribe,
		Unsubscribe,
		Edit
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
		if ( data.Length < 32 ) return;

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
	}

	protected override void OnDestroy()
	{
		_socket?.Dispose();
		_socket = null;
	}

	public ICellEditFeed CreateCellEditFeed( Vector2Int cellIndex )
	{
		return _cellFeeds[cellIndex] = new WebSocketCellEditFeed( this, cellIndex );
	}

	private class WebSocketCellEditFeed : ICellEditFeed
	{
		private readonly WebSocketEditFeed _parent;
		private readonly List<CompressedEditData> _edits = new();

		private bool _isLoaded;

		public Vector2Int CellIndex { get; }

		public IReadOnlyList<CompressedEditData> Edits => _edits;

		public event CellEditedDelegate Edited;

		public void Submit( CompressedEditData data )
		{
			_edits.Add( data );

			Span<byte> payload = new byte[8];

			data.Write( payload );

			_parent.Submit( MessageKind.Edit, CellIndex, payload );

			Edited?.Invoke( this, data );
		}

		public WebSocketCellEditFeed( WebSocketEditFeed parent, Vector2Int cellIndex )
		{
			_parent = parent;

			CellIndex = cellIndex;

			_parent.Submit( MessageKind.Subscribe, CellIndex, ReadOnlySpan<byte>.Empty );
		}

		public void OnEditReceived( Span<byte> data )
		{
			if ( data.Length < 8 ) return;

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
