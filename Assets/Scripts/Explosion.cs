using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using FishNet.Object;

public class Explosion : NetworkBehaviour
{
    public Orb orb; //assigned in inspector

    //set by player every time explosion occurs
    [NonSerialized] public Player player;

    private void OnTriggerEnter2D(Collider2D col)
    {
        if (IsServer && col.CompareTag("Player"))
        {
            Player newPlayer = col.GetComponent<Player>();
            if (newPlayer != player)
                newPlayer.Exploded(orb);
        }
        else if (orb.color == 0 && col.CompareTag("Orb"))
            player.CollectOrb(col.GetComponent<Orb>());
    }
}