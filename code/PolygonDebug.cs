using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sandbox.Polygons;
using Sandbox.Sdf;

namespace Sandbox;

public sealed class PolygonDebug : Component
{
	[Property, TextArea, Group( "Debug Dump" )]
	public string? Source { get; set; }

	[Button( "Apply" ), Group( "Debug Dump" )]
	public void Reduce()
	{
		var parsed = Json.Deserialize<DebugDump>( Source );

		parsed = parsed.Reduce() with { Exception = null };

		Source = Json.Serialize( parsed );
	}

	[Property, Group( "Parameters" )]
	public float Min { get; set; } = 0f;

	[Property, Group( "Parameters" )]
	public float Max { get; set; } = 1f;


	[Property, Range( 0f, 1f, 0.002f ), Group( "Parameters" )]
	public float Fraction { get; set; }

	[Property, Group( "Parameters" )]
	public bool FromSdf { get; set; }

	[Property, Group( "Parameters" )]
	public bool CustomFraction { get; set; }

	[Property, Group( "Parameters" )]
	public int MaxIterations { get; set; }

	protected override void DrawGizmos()
	{
		Gizmo.Transform = global::Transform.Zero with { Scale = new Vector3( 1024f, 1024f, 1024f ), Position = new Vector3( -8f * 1024f, -8f * 1024f )};

		PolygonMeshBuilder.RunDebugDump( Source, CustomFraction ? Min + (Max - Min) * Fraction : null, FromSdf, MaxIterations );

		//Gizmo.Draw.Color = Color.Blue;
		//polyMeshBuilder.DrawGizmos( 0f, bevelScale );
	}
}

