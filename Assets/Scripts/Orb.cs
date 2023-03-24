using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using System;

public class Orb : NetworkBehaviour
{
    public SpriteRenderer spriteRenderer; //assigned in inspector
    public Rigidbody2D rb; //^, read by player
    public Explosion explosion; //^ (is inactive by default)

    [NonSerialized] public bool collectible; //used by Player
    [NonSerialized] public Player suctioningPlayer; //^
    [NonSerialized] public int color; //^

    //0 = red, 1 = blue, 3 = yellow, 4 = green
    private readonly Color32[] colors = new Color32[4];
    private readonly string[] names = new string[4];

    private void Awake()
    {
        colors[0] = Color.red;
        colors[1] = Color.blue ;
        colors[2] = Color.yellow;
        colors[3] = Color.green;

        names[0] = "Red";
        names[1] = "Blue";
        names[2] = "Yellow";
        names[3] = "Green";
    }

    public void OnSpawn(int newColor) //called by player
    {
        color = newColor;
        name = names[color];
    }

    private void Update()
    {
        byte transparency = (byte)(collectible ? 255 : 100);
        Color32 currentColor = colors[color];
        spriteRenderer.color = new Color32(currentColor.r, currentColor.g, currentColor.b, transparency);
    }
}