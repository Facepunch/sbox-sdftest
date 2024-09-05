using System;
using System.IO;
using System.Threading.Tasks;
using Sandbox.Diagnostics;

namespace Sandbox;

public sealed class FileSystemEditFeed : Component, IEditFeed
{
	[Property] public string Directory { get; set; } = "World1";

	private Dictionary<Vector2Int, FileSystemCellEditFeed> Cells { get; } = new();

	public ICellEditFeed Subscribe( Vector2Int cellIndex )
	{
		if ( Cells.TryGetValue( cellIndex, out var cell ) )
		{
			return cell;
		}

		return Cells[cellIndex] = new FileSystemCellEditFeed( this, cellIndex );
	}

	internal void Unsubscribe( Vector2Int cellIndex )
	{
		Cells.Remove( cellIndex );
	}

	protected override void OnDestroy()
	{
		foreach ( var cell in Cells.Values.ToArray() )
		{
			cell.Dispose();
		}
	}
}

internal class FileSystemCellEditFeed : ICellEditFeed
{
	private const uint Magic = 0x6c6c6543U;
	private const uint Version = 1U;

	private readonly FileSystemEditFeed _manager;
	private readonly List<EditData> _edits = new();

	private readonly string _filePath;

	private bool _isLoaded;
	private bool _anyUnsaved;

	public Vector2Int CellIndex { get; }
	public event EditedDelegate Edited;

	public void Submit( EditData data )
	{
		_edits.Add( data );
		_anyUnsaved = true;

		if ( _isLoaded )
		{
			Edited?.Invoke( data );
		}
	}

	public FileSystemCellEditFeed( FileSystemEditFeed manager, Vector2Int cellIndex )
	{
		_manager = manager;
		_filePath = Path.Combine( _manager.Directory, $"{CellIndex.x}_{CellIndex.y}.cell" );

		CellIndex = cellIndex;

		_ = LoadAsync();
	}

	private async Task LoadAsync()
	{
		await GameTask.WorkerThread();

		if ( !FileSystem.Data.FileExists( _filePath ) )
		{
			_isLoaded = true;
			return;
		}

		using var stream = FileSystem.Data.OpenRead( _filePath );
		using var reader = new BinaryReader( stream );

		var magic = reader.ReadUInt32();
		var version = reader.ReadUInt32();

		Assert.AreEqual( Magic, magic );

		var read = new List<EditData>();

		while ( reader.BaseStream.Position < reader.BaseStream.Length )
		{
			read.Add( EditData.Read( reader ) );
		}

		await GameTask.MainThread();

		_edits.InsertRange( 0, read );
		_isLoaded = true;

		var editCount = _edits.Count;

		for ( var i = 0; i < editCount; i++ )
		{
			Edited?.Invoke( _edits[i] );
		}
	}

	private void Flush()
	{
		if ( !_anyUnsaved )
		{
			return;
		}

		if ( !_isLoaded )
		{
			Log.Warning( "Can't flush: not loaded!" );
			return;
		}

		FileSystem.Data.CreateDirectory( Path.GetDirectoryName( _filePath ) );

		using var stream = FileSystem.Data.OpenWrite( _filePath );
		using var writer = new BinaryWriter( stream );

		writer.Write( Magic );
		writer.Write( Version );

		var editCount = _edits.Count;

		for ( var i = 0; i < editCount; i++ )
		{
			_edits[i].Write( writer );
		}
	}

	public void Dispose()
	{
		_manager.Unsubscribe( CellIndex );

		Flush();
	}
}
