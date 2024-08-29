using Sandbox;

public sealed class Player : Component, Component.INetworkSpawn
{
	[Property]
	public CameraComponent Camera { get; set; }

	public void OnNetworkSpawn( Connection owner )
	{
		Camera.GameObject.Enabled = owner == Connection.Local;
	}
}
