
using System;
using System.IO;
using System.Xml;
using Sandbox.Sdf;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Sandbox;

public record struct EditData( long PlayerId, Vector3 Origin, float Radius )
{
	public static EditData Read( BinaryReader reader )
	{
		return new EditData(
			reader.ReadInt64(),
			new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() ),
			reader.ReadSingle() );
	}

	public void Write( BinaryWriter writer )
	{
		writer.Write( PlayerId );
		writer.Write( Origin.x );
		writer.Write( Origin.y );
		writer.Write( Origin.z );
		writer.Write( Radius );
	}
}

public delegate void CellEditedDelegate( ICellEditFeed feed, EditData data );

public interface ICellEditFeedFactory
{
	ICellEditFeed CreateCellEditFeed( Vector2Int cellIndex );
}

public interface ICellEditFeed : IDisposable
{
	Vector2Int CellIndex { get; }
	IReadOnlyList<EditData> Edits { get; }


	event CellEditedDelegate Edited;

	void Submit( EditData data );
}

public sealed class EditManager : Component
{
	private record struct Cell( ICellEditFeed Feed, HashSet<EditFeedSubscription> Subscriptions );

	private Dictionary<Vector2Int, Cell> Cells { get; } = new();

	[Property]
	public Sdf3DVolume Material { get; set; }

	[Property]
	public float CellSize { get; set; } = 8192f;

	public Vector3 CellToWorld( Vector2Int cellIndex )
	{
		return cellIndex * CellSize;
	}

	public void Submit( Vector3 origin, float radius )
	{
		var min = origin.x - radius - Material.MaxDistance;
		var max = origin.x + radius + Material.MaxDistance;

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

			cell.Feed.Submit( new EditData( Game.SteamId, origin - cellOrigin, radius ) );
		}
	}

	private (Vector2Int Min, Vector2Int Max) GetCellRange( Vector2 min, Vector2 max )
	{
		var cellMin = new Vector2Int(
			(int)MathF.Floor( min.x / CellSize ),
			(int)MathF.Floor( min.x / CellSize ) );

		var cellMax = new Vector2Int(
			(int)MathF.Ceiling( max.x / CellSize ),
			(int)MathF.Ceiling( max.x / CellSize ) );

		cellMax.x = Math.Max( cellMin.x, cellMax.x );
		cellMax.y = Math.Max( cellMin.y, cellMax.y );

		return (cellMin, cellMax);
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

				Cells[cellIndex] = cell = new Cell( factory.CreateCellEditFeed( cellIndex ), new HashSet<EditFeedSubscription>() );
			}

			cells.Add( cell );
		}

		var subscription = new EditFeedSubscription( this, cells.Select( x => x.Feed ), min, max );

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
	public Vector3 Min { get; }
	public Vector3 Max { get; }

	private WorldEditedDelegate _edited;


	public event WorldEditedDelegate Edited
	{
		add
		{
			DispatchEditHistory( value );
			_edited += value;
		}
		remove => _edited -= value;
	}

	internal EditFeedSubscription( EditManager manager, IEnumerable<ICellEditFeed> feeds, Vector3 min, Vector3 max )
	{
		_manager = manager;

		Feeds = feeds.ToArray();

		Min = min;
		Max = max;

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

	private void DispatchEditHistory( WorldEditedDelegate edited )
	{
		foreach ( var feed in Feeds )
		{
			var cellOrigin = _manager.CellToWorld( feed.CellIndex );

			foreach ( var data in feed.Edits )
			{
				var worldOrigin = data.Origin + cellOrigin;
				edited( data with { Origin = worldOrigin } );
			}
		}
	}

	private void OnEdit( ICellEditFeed feed, EditData data )
	{
		var cellOrigin = _manager.CellToWorld( feed.CellIndex );
		var worldOrigin = data.Origin + cellOrigin;

		var margin = data.Radius + _manager.Material.MaxDistance;

		var boundsMin = worldOrigin - margin;
		var boundsMax = worldOrigin + margin;

		if ( boundsMax.x < Min.x ) return;
		if ( boundsMax.y < Min.y ) return;
		if ( boundsMax.y < Min.y ) return;

		if ( boundsMin.x > Max.x ) return;
		if ( boundsMin.y > Max.y ) return;
		if ( boundsMin.y > Max.y ) return;

		_edited?.Invoke( data with { Origin = worldOrigin } );
	}
}
