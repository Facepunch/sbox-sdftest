﻿using System.IO;
using System.Threading.Tasks;
using Sandbox.Diagnostics;

namespace Sandbox;

public sealed class FileSystemEditFeed : Component, ICellEditFeedFactory
{
	[Property] public string Directory { get; set; } = "World1";

	public ICellEditFeed CreateCellEditFeed( Vector2Int cellIndex )
	{
		return new FileSystemCellEditFeed( Directory, cellIndex );
	}
}

internal class FileSystemCellEditFeed : ICellEditFeed
{
	private const uint Magic = 0x6c6c6543U;
	private const uint Version = 1U;

	private readonly List<EditData> _edits = new();

	private readonly string _filePath;

	private bool _isLoaded;
	private bool _anyUnsaved;

	public Vector2Int CellIndex { get; }

	public IReadOnlyList<EditData> Edits => _edits;

	public event CellEditedDelegate Edited;

	public void Submit( EditData data )
	{
		_edits.Add( data );
		_anyUnsaved = true;

		if ( _isLoaded )
		{
			Edited?.Invoke( this, data );
		}
	}

	public FileSystemCellEditFeed( string directory, Vector2Int cellIndex )
	{
		CellIndex = cellIndex;

		_filePath = Path.Combine( directory, $"{cellIndex.x}_{cellIndex.y}.cell" );

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
			Edited?.Invoke( this, _edits[i] );
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
		Flush();
	}
}
