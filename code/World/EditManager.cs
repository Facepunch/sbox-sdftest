using System;
using System.IO;
using Sandbox.Sdf;
using Sandbox.Worlds;

namespace Sandbox;

public enum EditKind : byte
{
	Add,
	Subtract
}

public record struct EditData( EditKind Kind, float Size, Vector3 Origin )
{
	public const float MinSize = 16f;
	public const float MaxSize = 1024f;

	public bool TryCompress( float cellSize, out CompressedEditData compressed )
	{
		var relSize = (int)MathF.Round( MathF.Log2( Size / MinSize ) * 255f / MathF.Log2( MaxSize / MinSize ) );
		var minPos = cellSize * -0.5f;
		var relPos = (Origin - minPos) / (cellSize * 2f);

		compressed = default;

		if ( relPos.x < 0f || relPos.y < 0f || relPos.z < 0f ) return false;
		if ( relPos.x >= 1f || relPos.y >= 1f || relPos.z >= 1f ) return false;

		compressed = new CompressedEditData( Kind, (byte)Math.Clamp( relSize, 0, 255 ),
			(ushort)Math.Clamp( MathF.Round( relPos.x * 65536f ), 0, ushort.MaxValue ),
			(ushort)Math.Clamp( MathF.Round( relPos.y * 65536f ), 0, ushort.MaxValue ),
			(ushort)Math.Clamp( MathF.Round( relPos.z * 65536f ), 0, ushort.MaxValue ) );

		return true;
	}
}

public record struct CompressedEditData( EditKind Kind, byte Size, ushort OriginX, ushort OriginY, ushort OriginZ )
{
	public const int SizeBytes = 8;

	public static CompressedEditData Read( BinaryReader reader )
	{
		return new CompressedEditData(
			(EditKind)reader.ReadByte(),
			reader.ReadByte(),
			reader.ReadUInt16(),
			reader.ReadUInt16(),
			reader.ReadUInt16() );
	}

	public void Write( BinaryWriter writer )
	{
		writer.Write( (byte)Kind );
		writer.Write( Size );
		writer.Write( OriginX );
		writer.Write( OriginY );
		writer.Write( OriginZ );
	}

	public void Write( Span<byte> span )
	{
		span[0] = (byte)Kind;
		span[1] = Size;

		BitConverter.TryWriteBytes( span[2..4], OriginX );
		BitConverter.TryWriteBytes( span[4..6], OriginY );
		BitConverter.TryWriteBytes( span[6..8], OriginZ );
	}

	public static CompressedEditData Read( ReadOnlySpan<byte> span )
	{
		return new CompressedEditData(
			(EditKind)span[0], span[1],
			BitConverter.ToUInt16( span[2..4] ),
			BitConverter.ToUInt16( span[4..6] ),
			BitConverter.ToUInt16( span[6..8] ) );
	}

	public EditData Decompress( float cellSize )
	{
		var minPos = cellSize * -0.5f;
		var relPos = new Vector3( OriginX, OriginY, OriginZ ) / 65536f;

		return new EditData( Kind,
			EditData.MinSize * MathF.Pow( 2f, Size / 255f * MathF.Log2( EditData.MaxSize / EditData.MinSize ) ),
			minPos + relPos * cellSize * 2f );
	}
}

public delegate void CellEditedDelegate( ICellEditFeed feed, CompressedEditData data );

public interface ICellEditFeedFactory
{
	ICellEditFeed CreateCellEditFeed( EditManager editManager, Vector2Int cellIndex );
}

public interface ICellEditFeed : IDisposable
{
	Vector2Int CellIndex { get; }

	event CellEditedDelegate Edited;

	void Submit( CompressedEditData data );
	void CopyEditHistory( List<CompressedEditData> edits );
}

public sealed class EditManager : Component
{
	private record struct Cell( ICellEditFeed Feed, HashSet<EditFeedSubscription> Subscriptions );

	private Dictionary<Vector2Int, Cell> Cells { get; } = new();

	[Property] public Sdf3DVolume Material { get; set; } = null!;

	[Property]
	public float CellSize { get; set; } = 8192f;

	private StreamingWorld? _world;
	public StreamingWorld World => _world ??= Scene.GetComponentInChildren<StreamingWorld>( true );

	public Vector2Int BaseCellIndex => (Vector2Int)(-World.WorldPosition / CellSize);

	public Vector3 CellToWorld( Vector2Int cellIndex )
	{
		return (cellIndex - BaseCellIndex) * CellSize;
	}

	public Vector2Int WorldToCell( Vector3 pos )
	{
		return BaseCellIndex + new Vector2Int(
			(int)MathF.Floor( pos.x / CellSize ),
			(int)MathF.Floor( pos.y / CellSize ) );
	}

	public void Submit( EditKind kind, float size, Vector3 origin )
	{
		var min = origin - size - Material.MaxDistance;
		var max = origin + size + Material.MaxDistance;

		var (cellMin, cellMax) = GetCellRange( min, max );

		for ( var x = cellMin.x; x < cellMax.x; ++x )
		for ( var y = cellMin.y; y < cellMax.y; ++y )
		{
			var cellIndex = new Vector2Int( x, y );

			if ( !Cells.TryGetValue( cellIndex, out var cell ) )
			{
				// Skip editing cells we're not subscribed to

				continue;
			}

			var cellOrigin = CellToWorld( cellIndex );
			var edit = new EditData( kind, size, origin - cellOrigin );

			if ( !edit.TryCompress( CellSize, out var compressed ) ) continue;

			cell.Feed.Submit( compressed );
		}
	}

	private (Vector2Int Min, Vector2Int Max) GetCellRange( Vector2 min, Vector2 max )
	{
		var cellMin = new Vector2Int(
			(int)MathF.Floor( min.x / CellSize ),
			(int)MathF.Floor( min.y / CellSize ) );

		var cellMax = new Vector2Int(
			(int)MathF.Ceiling( max.x / CellSize ),
			(int)MathF.Ceiling( max.y / CellSize ) );

		cellMax.x = Math.Max( cellMin.x, cellMax.x );
		cellMax.y = Math.Max( cellMin.y, cellMax.y );

		return (cellMin + BaseCellIndex, cellMax + BaseCellIndex);
	}

	public EditFeedSubscription Subscribe( Vector3 min, Vector3 max )
	{
		var (cellMin, cellMax) = GetCellRange( min, max );
		var cells = new List<Cell>();

		for ( var x = cellMin.x; x < cellMax.x; ++x )
		for ( var y = cellMin.y; y < cellMax.y; ++y )
		{
			var cellIndex = new Vector2Int( x, y );

			if ( !Cells.TryGetValue( cellIndex, out var cell ) )
			{
				var factory = Scene.GetAllComponents<ICellEditFeedFactory>().First();

				Cells[cellIndex] = cell = new Cell( factory.CreateCellEditFeed( this, cellIndex ), new HashSet<EditFeedSubscription>() );
			}

			cells.Add( cell );
		}

		var localOrigin = CellToWorld( cellMin );
		var subscription = new EditFeedSubscription( this, cells.Select( x => x.Feed ),
			cellMin, cellMax, min - localOrigin, max - localOrigin );

		foreach ( var cell in cells )
		{
			cell.Subscriptions.Add( subscription );
		}

		return subscription;
	}

	internal void RemoveSubscription( EditFeedSubscription subscription )
	{
		foreach ( var feed in subscription.Feeds )
		{
			if ( !Cells.TryGetValue( feed.CellIndex, out var cell ) ) continue;
			if ( cell.Feed != feed ) continue;
			if ( !cell.Subscriptions.Remove( subscription ) ) continue;
			if ( cell.Subscriptions.Count > 0 ) continue;

			Cells.Remove( feed.CellIndex );

			cell.Feed.Dispose();
		}
	}

	protected override void OnDestroy()
	{
		foreach ( var cell in Cells.Values.ToArray() )
		{
			cell.Feed.Dispose();
		}

		Cells.Clear();
	}
}

public delegate void WorldEditedDelegate( EditData data );

public sealed class EditFeedSubscription : IDisposable
{
	private readonly EditManager _manager;

	public IReadOnlyList<ICellEditFeed> Feeds { get; }

	public Vector2Int MinCellIndex { get; }
	public Vector2Int MaxCellIndex { get; }

	public Vector3 LocalMin { get; }
	public Vector3 LocalMax { get; }

	private WorldEditedDelegate? _edited;


	public event WorldEditedDelegate Edited
	{
		add
		{
			DispatchEditHistory( value );
			_edited += value;
		}
		remove => _edited -= value;
	}

	internal EditFeedSubscription( EditManager manager, IEnumerable<ICellEditFeed> feeds,
		Vector2Int minCellIndex, Vector2Int maxCellIndex,
		Vector3 localMin, Vector3 localMax )
	{
		_manager = manager;

		Feeds = feeds.ToArray();

		MinCellIndex = minCellIndex;
		MaxCellIndex = maxCellIndex;

		LocalMin = localMin;
		LocalMax = localMax;

		foreach ( var feed in Feeds )
		{
			feed.Edited += OnEdit;
		}
	}

	public void Dispose()
	{
		foreach ( var feed in Feeds )
		{
			feed.Edited -= OnEdit;
		}

		_manager.RemoveSubscription( this );
	}

	[field: ThreadStatic]
	private static List<CompressedEditData>? EditHistory { get; set; }

	private void DispatchEditHistory( WorldEditedDelegate edited )
	{
		EditHistory ??= new List<CompressedEditData>();

		foreach ( var feed in Feeds )
		{
			var cellOrigin = _manager.CellToWorld( feed.CellIndex );

			EditHistory.Clear();
			feed.CopyEditHistory( EditHistory );

			foreach ( var edit in EditHistory )
			{
				var data = edit.Decompress( _manager.CellSize );
				var worldOrigin = data.Origin + cellOrigin;
				edited( data with { Origin = worldOrigin } );
			}
		}
	}

	private void OnEdit( ICellEditFeed feed, CompressedEditData edit )
	{
		var data = edit.Decompress( _manager.CellSize );

		Vector3 cellOffset = (feed.CellIndex - MinCellIndex) * _manager.CellSize;
		var cellOrigin = _manager.CellToWorld( feed.CellIndex );
		var worldOrigin = data.Origin + cellOrigin;

		var margin = data.Size + _manager.Material.MaxDistance;

		var boundsMin = data.Origin - margin + cellOffset;
		var boundsMax = data.Origin + margin + cellOffset;

		if ( boundsMax.x < LocalMin.x ) return;
		if ( boundsMax.y < LocalMin.y ) return;
		if ( boundsMax.z < LocalMin.z ) return;

		if ( boundsMin.x > LocalMax.x ) return;
		if ( boundsMin.y > LocalMax.y ) return;
		if ( boundsMin.z > LocalMax.z ) return;

		_edited?.Invoke( data with { Origin = worldOrigin } );
	}
}
