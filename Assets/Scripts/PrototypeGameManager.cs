using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Owns phase order, deadlines, scoring, the collapse rule and all HUD drawing.
[RequireComponent(typeof(BlockPlacer), typeof(TestShooter), typeof(PrototypeCamera))]
public class PrototypeGameManager : MonoBehaviour
{
    public enum GamePhase { MonumentBuilding, ObeliskPlacement, Shooting, Finished }

    [Header("Scene references")]
    [SerializeField] private Camera gameCamera;
    [Tooltip("Optional. Blocks touching it count as fallen. Leave empty to have no floor.")]
    [SerializeField] private Collider groundCollider;
    [Tooltip("Blocks whose center drops below this height count as fallen.")]
    [SerializeField] private float fallHeight = -2f;
    [Header("Prefab assets")]
    [SerializeField] private GameObject monumentPrefab;
    [SerializeField] private GameObject obeliskPrefab;
    [SerializeField] private Rigidbody projectilePrefab;
    [Header("Match")]
    [SerializeField, Range(2, 4)] private int playerCount = 2;
    [SerializeField, Min(1)] private int maxRounds = 3;
    [SerializeField, Min(1f)] private float turnSeconds = 15f;
    [SerializeField, Min(0)] private int firstRoundMonuments = 3;
    [Header("Scoring")]
    [SerializeField] private int shootObeliskPoints = 10;
    [SerializeField] private int shootMonumentPoints = -5;
    [SerializeField] private int buildObeliskPoints = -10;
    [SerializeField] private int buildMonumentPoints = -5;
    [Tooltip("Knocking over more than half the tower in one placement or shot ends the match " +
        "and zeroes that player's points, once the tower has at least this many blocks.")]
    [SerializeField, Min(1)] private int minBlocksForCollapse = 4;

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
    public int Score(int player) => scores[player];

    // Player index is zero based.
    public event System.Action<PrototypeBlock, int, GamePhase> BlockFell;

    private readonly List<PrototypeBlock> blocks = new List<PrototypeBlock>();
    private BlockPlacer placer;
    private TestShooter shooter;
    private PrototypeCamera orbitCamera;
    private float deadline;
    private bool shotTaken;
    private int shootingRoundObelisks;
    private int roundMonumentAllowance;
    private int[] scores = new int[4];
    private string status = "Starting...";

    // The current "event" is the latest placement or shot; falls after it count toward it.
    private int eventStanding;
    private int eventFallen;
    private PrototypeBlock eventBlock;
    private int collapsedBy = -1;

    private struct ScorePopup
    {
        public string text;
        public int points;
        public float time;
    }
    private readonly List<ScorePopup> popups = new List<ScorePopup>();
    private const float PopupSeconds = 3f;

    private GUIStyle headingStyle;
    private GUIStyle detailStyle;
    private GUIStyle hintStyle;
    private GUIStyle rowStyle;
    private GUIStyle rowRightStyle;
    private GUIStyle popupStyle;
    private GUIStyle bigStyle;

    // Player 1 blue, player 2 red; extras for 3-4 player matches.
    private static readonly Color[] PlayerColors =
    {
        new Color(0.25f, 0.55f, 1f), new Color(1f, 0.3f, 0.3f),
        new Color(0.3f, 0.9f, 0.4f), new Color(1f, 0.85f, 0.25f)
    };
    public static readonly Color ObeliskColor = new Color(0.04f, 0.04f, 0.04f);
    // How strongly placed monuments lean toward their owner's color (0 = grey, 1 = full color).
    public const float OwnerTint = 0.35f;
    private static readonly Color MonumentIconColor = new Color(0.62f, 0.62f, 0.62f);
    private static readonly Color ObeliskIconColor = ObeliskColor;
    private static readonly Color IconOutlineColor = new Color(0.8f, 0.8f, 0.8f);
    private static readonly Color ShotIconColor = new Color(0.85f, 0.85f, 0.85f);
    private static readonly Color MutedText = new Color(0.7f, 0.7f, 0.7f);
    private static readonly Color GainColor = new Color(0.45f, 1f, 0.5f);
    private static readonly Color LossColor = new Color(1f, 0.5f, 0.35f);
    private const float OutlineThickness = 6f;
    private const float Margin = 10f;
    private const float TopBarHeight = 92f;
    private const int MaxIcons = 5;

    public static Color PlayerColor(int player) => PlayerColors[player % PlayerColors.Length];

    private void Start()
    {
        playerCount = Mathf.Clamp(playerCount, 2, 4);
        maxRounds = Mathf.Max(1, maxRounds);
        turnSeconds = Mathf.Max(1f, turnSeconds);
        scores = new int[playerCount];
        if (gameCamera == null || projectilePrefab == null
            || !ValidBlockPrefab(monumentPrefab) || !ValidBlockPrefab(obeliskPrefab)
            || projectilePrefab.GetComponent<Collider>() == null)
        {
            status = "Setup incomplete: assign the camera and prefabs on Prototype Game Manager.";
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

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (Phase == GamePhase.Finished && keyboard != null && keyboard.rKey.wasPressedThisFrame)
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
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
        int monumentAllowance = firstRoundMonuments;
        for (int round = 1; round <= maxRounds; round++)
        {
            Round = round;
            roundMonumentAllowance = monumentAllowance;
            if (monumentAllowance > 0)
                for (int player = 0; player < playerCount; player++)
                    yield return RunTurn(GamePhase.MonumentBuilding, player, monumentAllowance);

            for (int player = 0; player < playerCount; player++)
                yield return RunTurn(GamePhase.ObeliskPlacement, player, 1);

            shootingRoundObelisks = 0;
            for (int player = 0; player < playerCount; player++)
                yield return RunTurn(GamePhase.Shooting, player, 0);

            monumentAllowance = shootingRoundObelisks;
        }
        FinishMatch();
    }

    private IEnumerator RunTurn(GamePhase phase, int player, int allowance)
    {
        Phase = phase;
        CurrentPlayer = player;
        BlocksRemaining = allowance;
        shotTaken = false;
        StandingAtTurnStart = CountStandingBlocks();
        BeginEvent(null);
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
        status = timedOut ? "Time's up! Unused pieces are forfeited. Settling..." : "Settling...";
        yield return WaitForPhysics();
        // Clearing fallen blocks can drop blocks that were resting on them, so repeat until stable.
        while (CleanupFallenBlocks() > 0)
        {
            yield return null;
            WakeBlocks();
            yield return WaitForPhysics();
        }
        Settling = false;
    }

    private void FinishMatch()
    {
        StopAllCoroutines();
        if (placer != null) placer.EndPlacement();
        TurnActive = false;
        Settling = false;
        Phase = GamePhase.Finished;
    }

    private bool TurnFinished()
    {
        return Phase == GamePhase.Shooting ? shotTaken : BlocksRemaining <= 0;
    }

    private void BeginEvent(PrototypeBlock placed)
    {
        eventStanding = CountStandingBlocks();
        eventFallen = 0;
        eventBlock = placed;
    }

    public bool TryRegisterPlacement(GameObject placed)
    {
        if (!CanPlace) return false;
        PrototypeBlock block = placed.GetComponent<PrototypeBlock>();
        if (block == null) block = placed.AddComponent<PrototypeBlock>();
        BeginEvent(block);
        block.Initialize(this, groundCollider, fallHeight, Phase == GamePhase.ObeliskPlacement, CurrentPlayer);
        blocks.Add(block);
        BlocksRemaining--;
        return true;
    }

    public bool TryUseShot()
    {
        if (!CanShoot) return false;
        BeginEvent(null);
        shotTaken = true;
        return true;
    }

    public void NotifyBlockFell(PrototypeBlock block)
    {
        if (!MatchStarted || Phase == GamePhase.Finished || !blocks.Contains(block)) return;
        bool shooting = Phase == GamePhase.Shooting;
        if (shooting && block.IsObelisk)
            shootingRoundObelisks++;

        int points = shooting
            ? (block.IsObelisk ? shootObeliskPoints : shootMonumentPoints)
            : (block.IsObelisk ? buildObeliskPoints : buildMonumentPoints);
        AddPoints(CurrentPlayer, points,
            $"Player {CurrentPlayer + 1} knocked over {(block.IsObelisk ? "an obelisk" : "a monument")}");
        BlockFell?.Invoke(block, CurrentPlayer, Phase);

        // The block just placed slipping off is not the tower collapsing.
        if (block == eventBlock) return;
        eventFallen++;
        if (eventStanding >= minBlocksForCollapse && eventFallen * 2 > eventStanding)
        {
            collapsedBy = CurrentPlayer;
            scores[CurrentPlayer] = 0;
            Debug.Log($"Player {CurrentPlayer + 1} knocked over {eventFallen} of {eventStanding} blocks. Match over.");
            FinishMatch();
        }
    }

    private void AddPoints(int player, int points, string reason)
    {
        scores[player] += points;
        popups.Add(new ScorePopup { text = reason, points = points, time = Time.time });
        Debug.Log($"{reason}: {points:+#;-#;0} (total {scores[player]})");
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

    // Returns how many fallen blocks were destroyed.
    private int CleanupFallenBlocks()
    {
        int destroyed = 0;
        for (int i = blocks.Count - 1; i >= 0; i--)
        {
            if (blocks[i] == null || blocks[i].HasFallen)
            {
                if (blocks[i] != null)
                {
                    Destroy(blocks[i].gameObject);
                    destroyed++;
                }
                blocks.RemoveAt(i);
            }
        }
        return destroyed;
    }

    private void WakeBlocks()
    {
        foreach (PrototypeBlock block in blocks)
            if (block != null && block.Body != null) block.Body.WakeUp();
    }

    public bool PointerOverHUD(Vector2 pointer)
    {
        if (Phase == GamePhase.Finished) return true;
        // Pointer is bottom-left origin; GUI rects are top-left origin.
        Vector2 guiPoint = new Vector2(pointer.x, Screen.height - pointer.y);
        return TopBarRect.Contains(guiPoint)
            || (ShowMatchHUD && (InventoryRect.Contains(guiPoint) || ScoresRect.Contains(guiPoint)));
    }

    private bool ShowMatchHUD => MatchStarted && Phase != GamePhase.Finished;
    private Rect TopBarRect => new Rect(OutlineThickness + Margin, OutlineThickness + Margin,
        Screen.width - 2f * (OutlineThickness + Margin), TopBarHeight);
    private Rect InventoryRect
    {
        get
        {
            float height = Phase == GamePhase.Shooting ? 48f : 86f;
            return new Rect(OutlineThickness + Margin, Screen.height - OutlineThickness - Margin - height,
                300f, height);
        }
    }
    private Rect ScoresRect
    {
        get
        {
            float width = 280f;
            float height = 44f + playerCount * 28f + 50f;
            return new Rect(Screen.width - OutlineThickness - Margin - width,
                Screen.height - OutlineThickness - Margin - height, width, height);
        }
    }

    private string PhaseLabel()
    {
        switch (Phase)
        {
            case GamePhase.MonumentBuilding: return "Build monuments";
            case GamePhase.ObeliskPlacement: return "Place your obelisk";
            case GamePhase.Shooting: return "Shoot";
            default: return "Finished";
        }
    }

    private void OnGUI()
    {
        if (headingStyle == null) CreateStyles();

        if (!MatchStarted)
        {
            DrawTopBar("Setup needed", Color.white, status, "");
            return;
        }
        if (Phase == GamePhase.Finished)
        {
            DrawEndScreen();
            return;
        }

        Color accent = PlayerColor(CurrentPlayer);
        DrawTurnOutline(accent);

        string details;
        if (TurnActive)
        {
            int seconds = Mathf.CeilToInt(SecondsLeft);
            details = $"Round {Round} of {maxRounds}   |   {seconds}s left";
            detailStyle.normal.textColor = seconds <= 5 ? LossColor : Color.white;
        }
        else
        {
            details = $"Round {Round} of {maxRounds}   |   {status}";
            detailStyle.normal.textColor = MutedText;
        }
        string controls = Phase == GamePhase.Shooting
            ? "A / D  move within your section     Click  fire"
            : "A / D  orbit     R  rotate     T  stand upright     Click or E  place";
        DrawTopBar($"Player {CurrentPlayer + 1}  —  {PhaseLabel()}", accent, details, controls);

        DrawPopups();
        DrawInventory(accent);
        DrawScores();
    }

    private void CreateStyles()
    {
        headingStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        detailStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
        hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
        hintStyle.normal.textColor = MutedText;
        rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
        rowRightStyle = new GUIStyle(rowStyle) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
        popupStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        bigStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    }

    private void DrawTopBar(string heading, Color headingColor, string details, string controls)
    {
        Rect bar = TopBarRect;
        GUI.Box(bar, "");
        headingStyle.normal.textColor = headingColor;
        GUI.Label(new Rect(bar.x, bar.y + 6f, bar.width, 30f), heading, headingStyle);
        GUI.Label(new Rect(bar.x, bar.y + 36f, bar.width, 24f), details, detailStyle);
        if (controls.Length > 0)
            GUI.Label(new Rect(bar.x, bar.y + 62f, bar.width, 22f), controls, hintStyle);
    }

    private void DrawTurnOutline(Color color)
    {
        Texture tex = Texture2D.whiteTexture;
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, OutlineThickness), tex);
        GUI.DrawTexture(new Rect(0, Screen.height - OutlineThickness, Screen.width, OutlineThickness), tex);
        GUI.DrawTexture(new Rect(0, 0, OutlineThickness, Screen.height), tex);
        GUI.DrawTexture(new Rect(Screen.width - OutlineThickness, 0, OutlineThickness, Screen.height), tex);
        GUI.color = previous;
    }

    // Recent point changes stack under the top bar and fade out.
    private void DrawPopups()
    {
        popups.RemoveAll(p => Time.time - p.time > PopupSeconds);
        Color previous = GUI.color;
        float y = TopBarRect.yMax + 8f;
        int first = Mathf.Max(0, popups.Count - 4);
        for (int i = popups.Count - 1; i >= first; i--)
        {
            ScorePopup popup = popups[i];
            float alpha = Mathf.Clamp01((PopupSeconds - (Time.time - popup.time)) / 0.75f);
            string text = $"{popup.points:+#;-#;0}   {popup.text}";
            float width = popupStyle.CalcSize(new GUIContent(text)).x + 28f;
            Rect rect = new Rect((Screen.width - width) * 0.5f, y, width, 28f);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Box(rect, "");
            Color c = popup.points >= 0 ? GainColor : LossColor;
            c.a = alpha;
            popupStyle.normal.textColor = c;
            GUI.Label(rect, text, popupStyle);
            y += 32f;
        }
        GUI.color = previous;
    }

    private void DrawInventory(Color accent)
    {
        Rect panel = InventoryRect;
        GUI.Box(panel, "");
        Rect row = new Rect(panel.x + 12f, panel.y + 9f, panel.width - 24f, 30f);
        if (Phase == GamePhase.Shooting)
        {
            DrawInventoryRow(row, "Shot", shotTaken ? 0 : 1, 1, new Vector2(14f, 14f),
                ShotIconColor, accent, true);
            return;
        }

        bool building = Phase == GamePhase.MonumentBuilding;
        int monumentsLeft = building ? BlocksRemaining : 0;
        int obelisksLeft = building ? 1 : BlocksRemaining;
        DrawInventoryRow(row, "Monuments", monumentsLeft, roundMonumentAllowance,
            new Vector2(24f, 8f), MonumentIconColor, accent, building);
        row.y += 38f;
        DrawInventoryRow(row, "Obelisk", obelisksLeft, 1, new Vector2(14f, 14f),
            ObeliskIconColor, accent, !building);
    }

    // Remaining items draw solid, used ones faded. The active row takes the player's color.
    private void DrawInventoryRow(Rect row, string label, int left, int total, Vector2 iconSize,
        Color iconColor, Color accent, bool active)
    {
        rowStyle.normal.textColor = active ? accent : MutedText;
        GUI.Label(new Rect(row.x, row.y, 125f, row.height), $"{label}  {left}/{total}", rowStyle);

        Color previous = GUI.color;
        float x = row.x + 130f;
        int icons = Mathf.Min(total, MaxIcons);
        bool outlined = iconColor.grayscale < 0.2f; // dark icons vanish against the panel otherwise
        for (int i = 0; i < icons; i++)
        {
            float alpha = i < left ? (active ? 1f : 0.6f) : 0.18f;
            Rect icon = new Rect(x, row.y + (row.height - iconSize.y) * 0.5f, iconSize.x, iconSize.y);
            if (outlined)
            {
                Color edge = IconOutlineColor;
                edge.a = alpha;
                GUI.color = edge;
                GUI.DrawTexture(new Rect(icon.x - 1.5f, icon.y - 1.5f, icon.width + 3f, icon.height + 3f),
                    Texture2D.whiteTexture);
            }
            Color c = iconColor;
            c.a = outlined ? 1f : alpha;
            if (outlined && i >= left) c = new Color(0.25f, 0.25f, 0.25f, alpha);
            GUI.color = c;
            GUI.DrawTexture(icon, Texture2D.whiteTexture);
            x += iconSize.x + 5f;
        }
        GUI.color = previous;
        if (total > MaxIcons)
            GUI.Label(new Rect(x, row.y, 40f, row.height), "+", rowStyle);
        if (total == 0)
            GUI.Label(new Rect(x, row.y, 140f, row.height), "none this round", rowStyle);
    }

    private void DrawScores()
    {
        Rect panel = ScoresRect;
        GUI.Box(panel, "");
        rowStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(panel.x + 12f, panel.y + 8f, panel.width - 24f, 26f), "Scores", rowStyle);

        float y = panel.y + 40f;
        for (int player = 0; player < playerCount; player++)
        {
            DrawScoreRow(new Rect(panel.x + 8f, y, panel.width - 16f, 26f), player,
                player == CurrentPlayer);
            y += 28f;
        }
        GUI.Label(new Rect(panel.x, y + 4f, panel.width, 20f),
            $"Shooting:  obelisk {shootObeliskPoints:+#;-#;0},  monument {shootMonumentPoints:+#;-#;0}", hintStyle);
        GUI.Label(new Rect(panel.x, y + 22f, panel.width, 20f),
            $"Building:  obelisk {buildObeliskPoints:+#;-#;0},  monument {buildMonumentPoints:+#;-#;0}", hintStyle);
    }

    private void DrawScoreRow(Rect row, int player, bool highlight)
    {
        Color previous = GUI.color;
        if (highlight)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.1f);
            GUI.DrawTexture(row, Texture2D.whiteTexture);
        }
        GUI.color = PlayerColor(player);
        GUI.DrawTexture(new Rect(row.x + 6f, row.y + (row.height - 12f) * 0.5f, 12f, 12f), Texture2D.whiteTexture);
        GUI.color = previous;

        rowStyle.normal.textColor = highlight ? PlayerColor(player) : Color.white;
        GUI.Label(new Rect(row.x + 26f, row.y, row.width - 90f, row.height), $"Player {player + 1}", rowStyle);
        rowRightStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(row.x, row.y, row.width - 8f, row.height), scores[player].ToString(), rowRightStyle);
    }

    private void DrawEndScreen()
    {
        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = previous;

        int best = int.MinValue;
        for (int player = 0; player < playerCount; player++) best = Mathf.Max(best, scores[player]);
        List<int> winners = new List<int>();
        for (int player = 0; player < playerCount; player++)
            if (scores[player] == best) winners.Add(player);
        List<int> order = new List<int>();
        for (int player = 0; player < playerCount; player++) order.Add(player);
        order.Sort((a, b) => scores[b].CompareTo(scores[a]));

        float width = 440f;
        float height = 196f + playerCount * 30f + (collapsedBy >= 0 ? 26f : 0f);
        Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        GUI.Box(panel, "");

        float y = panel.y + 14f;
        headingStyle.normal.textColor = MutedText;
        GUI.Label(new Rect(panel.x, y, width, 28f), "Game over", headingStyle);
        y += 34f;

        bool tie = winners.Count > 1;
        bigStyle.normal.textColor = tie ? Color.white : PlayerColor(winners[0]);
        GUI.Label(new Rect(panel.x, y, width, 44f), tie ? "It's a tie!" : $"Player {winners[0] + 1} wins!", bigStyle);
        y += 50f;

        if (collapsedBy >= 0)
        {
            hintStyle.normal.textColor = LossColor;
            GUI.Label(new Rect(panel.x, y, width, 22f),
                $"Player {collapsedBy + 1} knocked over the tower: points reset to 0", hintStyle);
            hintStyle.normal.textColor = MutedText;
            y += 26f;
        }

        y += 6f;
        foreach (int player in order)
        {
            DrawScoreRow(new Rect(panel.x + 70f, y, width - 140f, 26f), player, winners.Contains(player));
            y += 30f;
        }

        y += 12f;
        detailStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(panel.x, y, width, 24f), "Press R to play again", detailStyle);
    }
}
