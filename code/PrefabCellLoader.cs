using Sandbox.Worlds;

namespace Sandbox;

public sealed class PrefabCellLoader : Component, ICellLoader
{
	[Property]
	public GameObject Prefab { get; set; }

	public void LoadCell( WorldCell cell )
	{
		Prefab.Clone( global::Transform.Zero, cell.GameObject );
	}

	public void UnloadCell( WorldCell cell )
	{

	}
}
