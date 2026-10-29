using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Owns phase order and deadlines. Scoring/collapse rules are the next milestone.
[RequireComponent(typeof(BlockPlacer), typeof(TestShooter), typeof(PrototypeCamera))]
public class PrototypeGameManager : MonoBehaviour
{
    public enum GamePhase { MonumentBuilding, ObeliskPlacement, Shooting, Finished }

    [Header("Scene references")]
    [SerializeField] private Camera gameCamera;
    [SerializeField] private Collider groundCollider;
    [Header("Prefab assets")]
    [SerializeField] private GameObject monumentPrefab;
    [SerializeField] private GameObject obeliskPrefab;
    [SerializeField] private Rigidbody projectilePrefab;
    [Header("Match")]
    [SerializeField, Range(2, 4)] private int playerCount = 2;
    [SerializeField, Min(1)] private int maxRounds = 10;
    [SerializeField, Min(1f)] private float turnSeconds = 15f;

    public GamePhase Phase { get; private set; }
    public int CurrentPlayer { get; private set; }
    public int Round { get; private set; }
    public int BlocksRemaining { get; private set; }
    public int StandingAtTurnStart { get; private set; }
    public bool TurnActive { get; private set; }
    public bool Settling { get; private set; }
    public bool MatchStarted { get; private set; }
    public int PlayerCount => playerCount;
    public float SecondsLeft => TurnActive ? Mathf.Max(0f, deadline - Time.time) : 0f;
    public bool CanPlace => TurnActive && SecondsLeft > 0f && BlocksRemaining > 0
        && (Phase == GamePhase.MonumentBuilding || Phase == GamePhase.ObeliskPlacement);
    public bool CanShoot => TurnActive && SecondsLeft > 0f && !shotTaken
        && Phase == GamePhase.Shooting;

    // Future scoring can go here. Player index is zero based.
    public event System.Action<PrototypeBlock, int, GamePhase> BlockFell;

    private readonly List<PrototypeBlock> blocks = new List<PrototypeBlock>();
    private BlockPlacer placer;
    private TestShooter shooter;
    private PrototypeCamera orbitCamera;
    private float deadline;
    private bool shotTaken;
    private int shootingRoundObelisks;
    private string status = "Starting...";
    private GUIStyle titleStyle;
    private GUIStyle detailStyle;

    private void Start()
    {
        playerCount = Mathf.Clamp(playerCount, 2, 4);
        maxRounds = Mathf.Max(1, maxRounds);
        turnSeconds = Mathf.Max(1f, turnSeconds);
        if (gameCamera == null || groundCollider == null || projectilePrefab == null
            || !ValidBlockPrefab(monumentPrefab) || !ValidBlockPrefab(obeliskPrefab)
            || projectilePrefab.GetComponent<Collider>() == null)
        {
            status = "Setup incomplete: assign all five references on Prototype Game Manager.";
            Debug.LogError(status, this);
            return;
        }

        placer = GetComponent<BlockPlacer>();
        shooter = GetComponent<TestShooter>();
        orbitCamera = GetComponent<PrototypeCamera>();
        placer.Initialize(this, gameCamera, groundCollider);
        shooter.Initialize(this, gameCamera, projectilePrefab);
        orbitCamera.Initialize(this, gameCamera);
        MatchStarted = true;
        StartCoroutine(RunMatch());
    }

    private bool ValidBlockPrefab(GameObject prefab)
    {
        if (prefab == null) return false;
        BoxCollider box = prefab.GetComponent<BoxCollider>();
        Rigidbody body = prefab.GetComponent<Rigidbody>();
        Vector3 scale = prefab.transform.localScale;
        return prefab.activeSelf && box != null && box.enabled && !box.isTrigger
            && body != null && scale.x > 0f && scale.y > 0f && scale.z > 0f;
    }

    private IEnumerator RunMatch()
    {
        int monumentAllowance = 5;
        for (int round = 1; round <= maxRounds; round++)
        {
            Round = round;
            if (monumentAllowance > 0)
                for (int player = 0; player < playerCount; player++)
                    yield return RunTurn(GamePhase.MonumentBuilding, player, monumentAllowance);

            for (int player = 0; player < playerCount; player++)
                yield return RunTurn(GamePhase.ObeliskPlacement, player, 1);

            shootingRoundObelisks = 0;
            for (int player = 0; player < playerCount; player++)
                yield return RunTurn(GamePhase.Shooting, player, 0);

            monumentAllowance = shootingRoundObelisks;
            // Double score for surviving obelisk values here once scoring exists.
        }

        TurnActive = false;
        Settling = false;
        Phase = GamePhase.Finished;
        status = "Turn-loop test complete. Stop Play mode to reset.";
    }

    private IEnumerator RunTurn(GamePhase phase, int player, int allowance)
    {
        Phase = phase;
        CurrentPlayer = player;
        BlocksRemaining = allowance;
        shotTaken = false;
        StandingAtTurnStart = CountStandingBlocks();
        orbitCamera.BeginTurn(player, phase == GamePhase.Shooting);

        if (phase != GamePhase.Shooting)
            placer.BeginPlacement(phase == GamePhase.MonumentBuilding ? monumentPrefab : obeliskPrefab);

        deadline = Time.time + turnSeconds;
        status = "";
        TurnActive = true;
        while (SecondsLeft > 0f && !TurnFinished())
            yield return null;

        bool timedOut = !TurnFinished();
        TurnActive = false;
        placer.EndPlacement();
        Settling = true;
        status = timedOut ? "Time up — unused placements/shot forfeited. Settling..." : "Settling...";
        yield return WaitForPhysics();
        CleanupFallenBlocks();
        Settling = false;
    }

    private bool TurnFinished()
    {
        return Phase == GamePhase.Shooting ? shotTaken : BlocksRemaining <= 0;
    }

    public bool TryRegisterPlacement(GameObject placed)
    {
        if (!CanPlace) return false;
        PrototypeBlock block = placed.GetComponent<PrototypeBlock>();
        if (block == null) block = placed.AddComponent<PrototypeBlock>();
        block.Initialize(this, groundCollider, Phase == GamePhase.ObeliskPlacement, CurrentPlayer);
        blocks.Add(block);
        BlocksRemaining--;
        return true;
    }

    public bool TryUseShot()
    {
        if (!CanShoot) return false;
        shotTaken = true;
        return true;
    }

    public void NotifyBlockFell(PrototypeBlock block)
    {
        if (!MatchStarted || Phase == GamePhase.Finished || !blocks.Contains(block)) return;
        if (Phase == GamePhase.Shooting && block.IsObelisk)
            shootingRoundObelisks++;

        Debug.Log($"Player {CurrentPlayer + 1}: {(block.IsObelisk ? "obelisk" : "monument")} fell during {Phase}.");
        // apply adding or loosing points and collapse detection here or with BlockFell.
        BlockFell?.Invoke(block, CurrentPlayer, Phase);
    }

    private IEnumerator WaitForPhysics()
    {
        yield return new WaitForSeconds(0.5f);
        while (shooter.HasLiveProjectile)
            yield return null;

        float quietTime = 0f;
        while (quietTime < 0.4f)
        {
            bool moving = false;
            foreach (PrototypeBlock block in blocks)
            {
                if (block == null || block.HasFallen || block.Body == null) continue;
                Rigidbody body = block.Body;
                if (!body.IsSleeping() && (body.linearVelocity.sqrMagnitude > 0.01f
                    || body.angularVelocity.sqrMagnitude > 0.04f))
                {
                    moving = true;
                    break;
                }
            }
            quietTime = moving ? 0f : quietTime + Time.deltaTime;
            yield return null;
        }
    }

    private int CountStandingBlocks()
    {
        int count = 0;
        foreach (PrototypeBlock block in blocks)
            if (block != null && !block.HasFallen) count++;
        return count;
    }

    private void CleanupFallenBlocks()
    {
        for (int i = blocks.Count - 1; i >= 0; i--)
        {
            if (blocks[i] == null || blocks[i].HasFallen)
            {
                if (blocks[i] != null) Destroy(blocks[i].gameObject);
                blocks.RemoveAt(i);
            }
        }
    }

    public bool PointerOverHUD(Vector2 pointer)
    {
        return pointer.y >= Screen.height - 112f;
    }

    private string PhaseLabel()
    {
        switch (Phase)
        {
            case GamePhase.MonumentBuilding: return "Build monuments";
            case GamePhase.ObeliskPlacement: return "Place obelisk";
            case GamePhase.Shooting: return "Shoot";
            default: return "Finished";
        }
    }

    private void OnGUI()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            detailStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = Color.white;
            detailStyle.normal.textColor = Color.white;
        }
        GUI.Box(new Rect(8, 8, Screen.width - 16, 100), "");
        string heading = !MatchStarted ? "Setup needed" : Phase == GamePhase.Finished
            ? "Match complete" : $"Player {CurrentPlayer + 1} — {PhaseLabel()} — Round {Round}/{maxRounds}";
        GUI.Label(new Rect(12, 12, Screen.width - 24, 30), heading, titleStyle);
        string details = TurnActive ? $"{Mathf.CeilToInt(SecondsLeft)} seconds"
            + (Phase == GamePhase.Shooting ? " | One shot" : $" | {BlocksRemaining} block(s) left") : status;
        GUI.Label(new Rect(12, 43, Screen.width - 24, 24), details, detailStyle);
        string controls = Phase == GamePhase.Shooting
            ? "A/D : move within your shooting section -- Click: fire"
            : "A/D: move -- R: rotate -- T: flip -- Click: place";
        if (MatchStarted && Phase != GamePhase.Finished)
            GUI.Label(new Rect(12, 70, Screen.width - 24, 24), controls, detailStyle);
    }
}
