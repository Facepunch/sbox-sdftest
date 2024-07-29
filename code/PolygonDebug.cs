using System.Diagnostics;
using System.Text.RegularExpressions;
using Sandbox.Polygons;
using Sandbox.Sdf;

namespace Sandbox;

public sealed class PolygonDebug : Component
{
	[Property, TextArea]
	public string Source { get; set; }


	[Property, Range( 0f, 1f, 0.01f )]
	public float Distance { get; set; }

	protected override void DrawGizmos()
	{
		using var polyMeshBuilder = PolygonMeshBuilder.Rent();

		Gizmo.Transform = global::Transform.Zero with { Scale = new Vector3( 1024f, 1024f, 1024f ), Position = new Vector3( -8f * 1024f, -13f * 1024f )};

		Gizmo.Draw.Color = Color.White;
		polyMeshBuilder.FromDebugDump( Source );

		//Gizmo.Draw.Color = Color.Blue;
		//polyMeshBuilder.DrawGizmos( 0f, bevelScale );
	}
}

