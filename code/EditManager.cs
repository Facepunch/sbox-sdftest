
using System;
using System.IO;
using Sandbox.Sdf;

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

public delegate void EditedDelegate( EditData data );

public interface IEditFeed
{
	ICellEditFeed Subscribe( Vector2Int cellIndex );
}

public interface ICellEditFeed : IDisposable
{
	Vector2Int CellIndex { get; }

	event EditedDelegate Edited;

	void Submit( EditData data );
}

public sealed class EditManager : Component
{
	[Property]
	public Sdf3DVolume Material { get; set; }

	public void Submit( Vector3 origin, float radius )
	{
		throw new NotImplementedException();
	}
}
