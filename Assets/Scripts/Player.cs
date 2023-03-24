using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using FishNet.Connection;

public class Player : NetworkBehaviour
{
    public Rigidbody2D rb; //assigned in inspector
    public GameObject orbPref; //^
    public SpriteRenderer playerRenderer; //^
    public SpriteRenderer[] arsenalRenderers; //^
    public SpriteRenderer purpleOverlay; //^

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
    private bool stopOccurring; //prevents purple from occurring when red or player is moving

    public delegate void OnBothPlayersReadyAction();
    public static event OnBothPlayersReadyAction OnBothPlayersReady;

    private void OnEnable()
    {
        GameManager.OnClientConnectOrLoad += DeclareReady;
        OnBothPlayersReady += OnSpawn;
    }
    private void OnDisable()
    {
        GameManager.OnClientConnectOrLoad -= DeclareReady;
        OnBothPlayersReady -= OnSpawn;
    }

    private void DeclareReady(GameManager gm)
    {
        if (IsOwner)
            RpcDeclareReady(gm);
    }
    [ServerRpc]
    private void RpcDeclareReady(GameManager gm)
    {
        gm.readyPlayers += 1;
        if (gm.readyPlayers == 2)
            OnBothPlayersReady?.Invoke();
    }

    [ObserversRpc]
    private void OnSpawn()
    {
        if (IsOwner)
        {
            playerRenderer.color = Color.white;

            RpcSpawnOrbs(ClientManager.Connection);
        }
        else
            playerRenderer.color = Color.black;

        int yBoardPosition;
        if (IsOwner && GameManager.playerNumber == 1)
            yBoardPosition = -1;
        else if (IsOwner || GameManager.playerNumber == 1)
            yBoardPosition = 1;
        else
            yBoardPosition = -1;

        if (GameManager.playerNumber == 2)
            Camera.main.transform.rotation = Quaternion.Euler(0, 0, 180);

        transform.SetPositionAndRotation(new Vector2(0, 4 * yBoardPosition), Camera.main.transform.rotation);
    }

    [ServerRpc]
    private void RpcSpawnOrbs(NetworkConnection conn)
    {
        for (int i = 0; i < 4; i++)
        {
            GameObject newOrbObject = Instantiate(orbPref, orbSpawnPosition, Quaternion.identity);
            ServerManager.Spawn(newOrbObject, conn);
            Orb newOrb = newOrbObject.GetComponent<Orb>();
            RpcOrbSetup(newOrb, i);
        }
    }

    [ObserversRpc]
    private void RpcOrbSetup(Orb newOrb, int color)
    {
        orbsInArsenal.Add(newOrb);

        newOrb.transform.SetParent(GameObject.FindGameObjectWithTag("OrbParent").transform);

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
        ArsenalDisplay();
        AttemptToCollect();
        Suction();

        if (!IsOwner)
            return;

        bool redInput = Input.GetButtonDown("Red");
        bool blueInput = Input.GetButtonDown("Blue");
        bool yellowInput = Input.GetButtonDown("Yellow");
        bool greenInput = Input.GetButtonDown("Green");

        //4 = purple. Must trigger purple before other abilities, since other abilities
        //might empty orbsInArsenal, causing purple's ability to occur at the same time
        if (redInput || blueInput || yellowInput || greenInput)
            PrepareTrigger(4);

        if (redInput) PrepareTrigger(0);
        if (blueInput) PrepareTrigger(1);
        if (yellowInput) PrepareTrigger(2);
        if (greenInput) PrepareTrigger(3);
    }

    private void PrepareTrigger(int color)
    {
        Vector2 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        //check first on client
        if (CheckCanTrigger(color, mousePosition))
            RpcSendTrigger(color, mousePosition);
    }

    [ServerRpc]
    private void RpcSendTrigger(int color, Vector2 mousePosition)
    {
        //check again on server
        if (CheckCanTrigger(color, mousePosition))
            RpcReceiveTrigger(color, mousePosition);
    }
    [ObserversRpc]
    private void RpcReceiveTrigger(int color, Vector2 mousePosition)
    {
        Orb newOrb = GetOrbByColor(color); //null if purple
        Orb selectedOrb = GetSelectedOrb(mousePosition);

        if (color == 0) TriggerRed(newOrb, mousePosition);
        if (color == 1) TriggerBlue(newOrb, mousePosition);
        if (color == 2) TriggerYellow(newOrb, selectedOrb);
        if (color == 3) TriggerGreen(newOrb, selectedOrb);
        if (color == 4) TriggerPurple(selectedOrb);
    }

    private bool CheckCanTrigger(int color, Vector2 mousePosition)
    {
        //if orb is purple, defer to PurpleAvailable
        if (color == 4)
            return PurpleAvailable();
        
        //if orb is not in arsenal, prevent trigger
        if (GetOrbByColor(color) == null)
            return false;

        //if blue and green suction is occurring, prevent trigger
        if (color == 1 && greenSuctionOccurring)
            return false;

        //if yellow or green and no orb is selected, prevent trigger
        if (color > 1 && GetSelectedOrb(mousePosition) == null)
            return false;

        return true;
    }

    private bool PurpleAvailable() //used by CheckCanTrigger and ArsenalDisplay
    {
        //if there are any orbs in arsenal, or currently on the way, or if player or red is moving
        if (orbsInArsenal.Count > 0 || suctionInfos.Count > 0 || stopOccurring)
            return false;

        return true;
    }

    private void ArsenalDisplay() //run in update
    {
        //if orbs are in arsenal or suction is occurring, enable purple overlay
        if (PurpleAvailable())
        {
            purpleOverlay.enabled = true;
            return;
        }
        purpleOverlay.enabled = false;

        //caches the desired changes in an array
        bool[] spriteActiveCache = new bool[8];
        foreach (Orb orb in orbsInArsenal)
        {
            // if a red (for example) orb is found in arsenal, sets the outer cone active. If
            // the outer cone's already active, sets the inner cone active
            if (!spriteActiveCache[orb.color])
                spriteActiveCache[orb.color] = true;
            else
                spriteActiveCache[orb.color + 4] = true;
        }

        //applies the changes
        for (int i = 0;i < 8; i++)
            arsenalRenderers[i].enabled = spriteActiveCache[i];
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

    private Vector2 GetMouseDirection(Vector2 mousePosition)
    {
        return (mousePosition - (Vector2)transform.position).normalized;
    }

    private Orb GetSelectedOrb(Vector2 mousePosition)
    {
        //raycast only checks layer 7 (Orb)
        int layerMask = 1 << 7;
        RaycastHit2D hit = Physics2D.Raycast(mousePosition, Vector2.zero, 0, layerMask);
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
        stopOccurring = true;
        yield return new WaitForSeconds(moveDuration);
        newRb.velocity = Vector2.zero;
        stopOccurring = false;
    }

    //ability methods:
    private void TriggerRed(Orb newOrb, Vector2 mousePosition)
    {
        newOrb.transform.position = transform.position;
        newOrb.rb.velocity = GetMouseDirection(mousePosition) * moveSpeed;
        FireOrb(newOrb);
        StartCoroutine(TriggerExplosion(newOrb));
        StartCoroutine(Stop(newOrb.rb));
    }

    private void TriggerBlue(Orb newOrb, Vector2 mousePosition)
    {
        newOrb.transform.position = transform.position;
        rb.velocity = GetMouseDirection(mousePosition) * moveSpeed;
        FireOrb(newOrb);
        StartCoroutine(TriggerExplosion(newOrb));
        StartCoroutine(Stop(rb));
    }

    private void TriggerYellow(Orb newOrb, Orb selectedOrb)
    {
        newOrb.transform.position = selectedOrb.transform.position;
        FireOrb(newOrb);
        StartCoroutine(TriggerExplosion(newOrb));

        InitilizeSuction(selectedOrb, selectedOrb.rb, transform);
    }

    private void TriggerGreen(Orb newOrb, Orb selectedOrb)
    {
        newOrb.transform.position = transform.position;
        FireOrb(newOrb);
        StartCoroutine(TriggerExplosion(newOrb));

        greenSuctionOccurring = true;
        InitilizeSuction(selectedOrb, rb, selectedOrb.transform);
    }

    private void TriggerPurple(Orb selectedOrb)
    {
        InitilizeSuction(selectedOrb, selectedOrb.rb, transform);
    }
}
public struct SuctionInfo
{
    public Rigidbody2D projectile;
    public Transform destination;
}