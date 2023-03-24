using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using FishNet;
using FishNet.Connection;

public class Beacon : NetworkBehaviour //spawns a gamemanager in a scene and, once a client has fully loaded into a scene, sends a signal to the GameManager
{
    public bool spawnGameManager; //set in inspector (in scene)
    public GameObject gameManager; //set in inspector (in prefab)

    public delegate void SignalAction();
    public static event SignalAction Signal;

    public override void OnSpawnServer(NetworkConnection conn)
    {
        base.OnSpawnServer(conn);
        if (spawnGameManager && IsServer) //spawn gamemanager on server
            if (GameObject.FindGameObjectWithTag("GameManager") == null)
            {
                GameObject gm = Instantiate(gameManager);
                InstanceFinder.ServerManager.Spawn(gm);
            }

        RpcSendSignal(conn);
    }

    [TargetRpc]
    private void RpcSendSignal(NetworkConnection conn)
    {
        Signal?.Invoke();
    }

    public override void OnStartServer() //ensures that ServerManager.StartConnection will always create a host
    {
        base.OnStartServer();
        if (!IsClient)
            ClientManager.StartConnection();
    }
}