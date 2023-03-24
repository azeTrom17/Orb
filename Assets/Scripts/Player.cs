using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using FishNet.Connection;
using System;
using System.Runtime.CompilerServices;

public class Player : NetworkBehaviour
{
    public Rigidbody2D rb; //assigned in inspector
    public GameObject orbPref; //^
    
    private Transform orbParent;
    private Vector2 orbSpawnPosition = new(-15, 0);

    private readonly List<Orb> orbsInArsenal = new();
    private readonly List<Orb> firedOrbs = new();
    private readonly List<SuctionInfo> suctionInfos = new();
    private readonly List<Orb> overlappingOrbs = new();

    private readonly float moveSpeed = 12f;
    private readonly float suctionSpeed = 12f;
    private readonly float moveDuration = .31f;
    private readonly float explosionDuration = .5f;

    private bool greenSuctionOccurring; //prevents blue from occuring when green suction is occurring

    private void OnEnable()
    {
        GameManager.OnClientConnectOrLoad += OnSpawn;
    }
    private void OnDisable()
    {
        GameManager.OnClientConnectOrLoad -= OnSpawn;
    }

    private void OnSpawn(GameManager gm)
    {

        if (IsOwner)
        {
            orbParent = GameObject.FindGameObjectWithTag("OrbParent").transform;
            RpcSpawnOrbs(ClientManager.Connection);
        }
    }

    [ServerRpc]
    private void RpcSpawnOrbs(NetworkConnection conn)
    {
        for (int i = 0; i < 4; i++)
        {
            GameObject newOrbObject = Instantiate(orbPref, orbSpawnPosition, Quaternion.identity);
            ServerManager.Spawn(newOrbObject, conn);
            Orb newOrb = newOrbObject.GetComponent<Orb>();
            RpcOrbSetup(newOrb, conn, i);
        }
    }

    [ObserversRpc]
    private void RpcOrbSetup(Orb newOrb, NetworkConnection conn, int color)
    {
        if (conn == ClientManager.Connection)
            orbsInArsenal.Add(newOrb);

        newOrb.transform.SetParent(orbParent);

        //color 0 = red, 1 = blue, 2 = yellow, 3 = green
        newOrb.OnSpawn(color);
    }

    private void OnTriggerEnter2D(Collider2D col)
    {
        if (col.CompareTag("Orb"))
            overlappingOrbs.Add(col.GetComponent<Orb>());
    }
    private void OnTriggerExit2D(Collider2D col)
    {
        if (col.CompareTag("Orb"))
            overlappingOrbs.Remove(col.GetComponent<Orb>());
    }
    private void AttemptToCollect() //run in update
    {
        foreach (Orb newOrb in overlappingOrbs)
            CollectOrb(newOrb);
    }

    public void CollectOrb(Orb newOrb) //called by explosion
    {
        //if newOrb is already in arsenal
        foreach (Orb arsenalOrb in orbsInArsenal)
            if (arsenalOrb == newOrb)
                return;

        if (!newOrb.collectible && newOrb.suctioningPlayer != this)
            return;

        //if suction occurred between this player and newOrb, remove newOrb from suctionInfos
        foreach (SuctionInfo suctionInfo in suctionInfos)
            if (suctionInfo.destination == transform || suctionInfo.projectile == rb)
                if (suctionInfo.destination == newOrb.transform ||  suctionInfo.projectile == newOrb.rb)
                {
                    suctionInfos.Remove(suctionInfo);
                    newOrb.suctioningPlayer = null;
                    greenSuctionOccurring = false;
                    rb.velocity = Vector2.zero;
                    break;
                }

        newOrb.rb.velocity = Vector2.zero;
        newOrb.collectible = false;
        newOrb.transform.position = orbSpawnPosition;
        orbsInArsenal.Add(newOrb);
    }

    public IEnumerator TriggerExplosion(Orb newOrb)
    {
        Explosion explosion = newOrb.explosion;
        yield return new WaitForSeconds(moveDuration);

        explosion.player = this;
        explosion.gameObject.SetActive(true);

        yield return new WaitForSeconds(explosionDuration);

        explosion.gameObject.SetActive(false);
        explosion.player = null;
        firedOrbs.Remove(newOrb);
        newOrb.collectible = true;
    }

    public void Exploded(Orb newOrb) //called by explosion
    {
        for (int i = 0; i < firedOrbs.Count; i++)
            if (firedOrbs[i] == newOrb)
                return;

        Eliminate();
    }

    private void Eliminate()
    {
        Debug.Log("You have been eliminated");
        transform.position = new(15, 0);
    }

    private void Update()
    {
        if (Input.GetButtonDown("Red")) TriggerRed();
        if (Input.GetButtonDown("Blue")) TriggerBlue();
        if (Input.GetButtonDown("Yellow")) TriggerYellow();
        if (Input.GetButtonDown("Green")) TriggerGreen();
        if (Input.GetButtonDown("Purple")) TriggerPurple();

        string debugString = "";
        for (int i = 0; i < orbsInArsenal.Count; i++)
            debugString += orbsInArsenal[i].name + " ";
        Debug.Log(debugString);

        AttemptToCollect();
        Suction();
    }

    //helper methods:

    private void FireOrb(Orb newOrb)
    {
        orbsInArsenal.Remove(newOrb);
        firedOrbs.Add(newOrb);
    }

    private Orb GetOrbByColor(int color)
    {
        for (int i = 0; i < orbsInArsenal.Count; i++)
            if (orbsInArsenal[i].color == color)
                return orbsInArsenal[i];
        return null;
    }

    private Vector2 GetMouseDirection()
    {
        Vector2 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        return (mousePosition - (Vector2)transform.position).normalized;
    }

    private Orb GetSelectedOrb()
    {
        //raycast only checks layer 7 (Orb)
        int layerMask = 1 << 7;
        RaycastHit2D hit = Physics2D.Raycast(Camera.main.ScreenToWorldPoint(Input.mousePosition), Vector2.zero, 0, layerMask);
        if (hit)
        {
            Orb newOrb = hit.collider.GetComponent<Orb>();
            if (newOrb.collectible)
                return newOrb;

            return null;
        }
        return null;
    }

    private void InitilizeSuction(Orb newOrb, Rigidbody2D newProjectile, Transform newDestination)
    {
        newOrb.collectible = false;
        newOrb.suctioningPlayer = this;

        SuctionInfo suctionInfo = new()
        {
            projectile = newProjectile,
            destination = newDestination
        };
        suctionInfos.Add(suctionInfo);
    }

    private void Suction() //run in update
    {
        foreach (SuctionInfo suctionInfo in suctionInfos)
        {
            Vector2 direction = (suctionInfo.destination.position - suctionInfo.projectile.transform.position).normalized;
            suctionInfo.projectile.velocity = direction * suctionSpeed;
        }
    }

    private IEnumerator Stop(Rigidbody2D newRb)
    {
        yield return new WaitForSeconds(moveDuration);
        newRb.velocity = Vector2.zero;
    }

    //ability methods:
    private void TriggerRed()
    {
        Orb newOrb = GetOrbByColor(0);
        if (newOrb == null) return;

        newOrb.transform.position = transform.position;
        newOrb.rb.velocity = GetMouseDirection() * moveSpeed;
        FireOrb(newOrb);
        StartCoroutine(TriggerExplosion(newOrb));
        StartCoroutine(Stop(newOrb.rb));
    }

    private void TriggerBlue()
    {
        Orb newOrb = GetOrbByColor(1);
        if (newOrb == null) return;

        if (greenSuctionOccurring)
            return;

        newOrb.transform.position = transform.position;
        rb.velocity = GetMouseDirection() * moveSpeed;
        FireOrb(newOrb);
        StartCoroutine(TriggerExplosion(newOrb));
        StartCoroutine(Stop(rb));
    }

    private void TriggerYellow()
    {
        Orb newOrb = GetOrbByColor(2);
        if (newOrb == null) return;

        Orb selectedOrb = GetSelectedOrb();
        if (selectedOrb == null) return;

        newOrb.transform.position = selectedOrb.transform.position;
        FireOrb(newOrb);
        StartCoroutine(TriggerExplosion(newOrb));

        InitilizeSuction(selectedOrb, selectedOrb.rb, transform);
    }

    private void TriggerGreen()
    {
        Orb newOrb = GetOrbByColor(3);
        if (newOrb == null) return;

        Orb selectedOrb = GetSelectedOrb();
        if (selectedOrb == null) return;

        newOrb.transform.position = transform.position;
        FireOrb(newOrb);
        StartCoroutine(TriggerExplosion(newOrb));

        greenSuctionOccurring = true;
        InitilizeSuction(selectedOrb, rb, selectedOrb.transform);
    }

    private void TriggerPurple()
    {
        //if there are any orbs in arsenal or currently on the way
        if (orbsInArsenal.Count > 0 || suctionInfos.Count > 0)
            return;

        Orb selectedOrb = GetSelectedOrb();
        if (selectedOrb == null) return;

        InitilizeSuction(selectedOrb, selectedOrb.rb, transform);
    }
}
public struct SuctionInfo
{
    public Rigidbody2D projectile;
    public Transform destination;
}