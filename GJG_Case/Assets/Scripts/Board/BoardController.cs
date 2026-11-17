using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Core playable Match-2 Blast controller managing board generation, tap input,
/// booster spawn & instant detonation (Rocket/Bomb/Disco), obstacles (Crate/Lock),
/// smooth gravity/refill, deadlock shuffling, undo snapshots, and power-ups.
/// </summary>
public class BoardController : MonoBehaviour, IBoardPowerUpHandler
{
    private const string GameplayContainerName = "_GameplayGrid";
    private const int MaxUndoDepth = 12;

    private struct BoardSnapshot
    {
        public BoardCellData[,] GridCopy;
        public int[] GoalRemaining;
        public int MovesRemaining;
    }

    [Header("Dependencies")]
    [SerializeField] private RandomBoardFiller _boardLayoutSource;
    [SerializeField] private GoalsController _goalsController;
    [SerializeField] private LevelTimer _levelLimit;
    [SerializeField] private GameAudioPlayer _audioPlayer;
    [SerializeField] private BlastVfxController _vfxController;
    [SerializeField] private Camera _inputCamera;

    [Header("Sprite Catalog (2.5D Blocks, Boosters, Obstacles)")]
    [SerializeField] private BoardSpriteCatalog _spriteCatalog = new BoardSpriteCatalog();

    [Header("Gameplay Tuning")]
    [Tooltip("When true, spawned Rocket/Bomb/Disco boosters stay on the board and fall normally until the player clicks them (or another explosion chains them).")]
    [SerializeField] private bool _requireTapToActivateBoosters = true;
    [Min(0.15f)]
    [SerializeField] private float _boosterArmDelay = 0.50f;
    [Min(1)]
    [SerializeField] private int _breakLockTargetsPerUse = 3;

    private BoardCellData[,] _grid;
    private int[,] _groupSizes;
    private bool[,] _visited;

    private int _rows = 8;
    private int _cols = 6;
    private int _colorCount = 6;
    private int _rocketThreshold = 5;
    private int _bombThreshold = 7;
    private int _discoThreshold = 9;
    private int _crateHitPoints = 2;
    private int _lockHitPoints = 3;

    private LevelConfigSO _activeConfig;
    private RandomBoardFiller.ResolvedBoardGeometry _geometry;
    private Transform _tileContainer;
    private SpriteMask _boardMask;
    private static Sprite _maskUnitSprite;

    private readonly List<TileView> _tilePool = new List<TileView>(96);
    private readonly List<BlockColor> _activeColors = new List<BlockColor>(6);
    private readonly List<Vector2Int> _scratchGroup = new List<Vector2Int>(64);
    private readonly List<Vector2Int> _solveBestGroup = new List<Vector2Int>(64);
    private readonly List<RaycastResult> _uiRaycastBuffer = new List<RaycastResult>(16);
    private readonly Stack<BoardSnapshot> _undoStack = new Stack<BoardSnapshot>(MaxUndoDepth);

    private bool _isInputEnabled;
    private bool _isResolving;
    private bool _isBusyWithGravity;
    private System.Random _rng;

    public bool IsResolving => _isResolving || _isBusyWithGravity;

    public event Action OnPowerUpStateInvalidated;
    public event Action OnMoveResolutionCompleted;

    private void Awake()
    {
        ResolveReferences();

        if (_boardLayoutSource != null)
        {
            _boardLayoutSource.SetDrivenExternally(true);
        }

        EnsureContainer();
    }

    private void Update()
    {
        if (!_isInputEnabled || _isResolving || _isBusyWithGravity || _grid == null)
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (IsPointerOverBlockingUi(Input.mousePosition))
            {
                return;
            }

            if (TryScreenPointToCell(Input.mousePosition, out int row, out int col))
            {
                HandleCellTapped(row, col);
            }
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        ResolveReferences();
        _spriteCatalog.LoadDefaultsInEditor();
    }

    [ContextMenu("Load Default Board Sprites")]
    public void LoadDefaultBoardSpritesContext()
    {
        _spriteCatalog.LoadDefaultsInEditor();
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    public BoardSpriteCatalog SpriteCatalog => _spriteCatalog;

    public void SetInputEnabled(bool enabled)
    {
        _isInputEnabled = enabled;
    }

    /// <summary>
    /// Initializes a brand new playable board from the supplied LevelConfigSO.
    /// </summary>
    public void LoadLevel(LevelConfigSO config)
    {
        StopAllCoroutines();
        _isResolving = false;
        _isBusyWithGravity = false;
        _undoStack.Clear();
        ReleaseAllPooledViews();

        ResolveReferences();
        _activeConfig = config;

        _rows = config != null ? config.Rows : 8;
        _cols = config != null ? config.Cols : 6;
        _colorCount = config != null ? config.ColorCount : 6;
        _rocketThreshold = config != null ? config.RocketThreshold : 5;
        _bombThreshold = config != null ? config.BombThreshold : 7;
        _discoThreshold = config != null ? config.DiscoThreshold : 9;
        _crateHitPoints = config != null ? Mathf.Max(1, config.WoodCrateHitPoints) : 2;
        _lockHitPoints = config != null ? Mathf.Max(1, config.LockedBlockHitPoints) : 3;

        int seed = config != null && config.RandomSeed != 0
            ? config.RandomSeed
            : Environment.TickCount ^ GetInstanceID();
        _rng = new System.Random(seed);

        BuildActiveLevelPalette(config);

        _grid = new BoardCellData[_rows, _cols];
        _groupSizes = new int[_rows, _cols];
        _visited = new bool[_rows, _cols];

        Sprite sample = _spriteCatalog.GetFirstValidSampleSprite();
        if (_boardLayoutSource != null)
        {
            _boardLayoutSource.ResolveGeometryForBoard(_rows, _cols, sample, applyFrameResize: true, out _geometry);
        }

        UpdateBoardSpriteMask();
        PopulateInitialGrid(config);
        EnsureDeadlockFreeImmediate();
        RecomputeGroupsAndRefreshAllViews(snapPositions: true, playSpawnPop: true);

        _isInputEnabled = true;
        OnPowerUpStateInvalidated?.Invoke();
    }

    #region IBoardPowerUpHandler

    public bool CanUsePowerUp(PowerUpType type)
    {
        if (!_isInputEnabled || _isResolving || _grid == null)
        {
            return false;
        }

        return type switch
        {
            PowerUpType.SolvePartial => true,
            PowerUpType.Undo => _undoStack.Count > 0,
            PowerUpType.BreakLock => HasAnyObstacleOnBoard(),
            _ => false
        };
    }

    public bool TryExecutePowerUp(PowerUpType type)
    {
        if (!CanUsePowerUp(type))
        {
            return false;
        }

        switch (type)
        {
            case PowerUpType.SolvePartial:
                return ExecuteSolvePartial();
            case PowerUpType.Undo:
                return ExecuteUndo();
            case PowerUpType.BreakLock:
                return ExecuteBreakLock();
            default:
                return false;
        }
    }

    #endregion

    private void HandleCellTapped(int row, int col)
    {
        BoardCellData cell = _grid[row, col];
        if (cell.IsEmpty || cell.IsLocked || cell.Kind == TileKind.WoodCrate)
        {
            if (!cell.IsEmpty && (cell.IsLocked || cell.Kind == TileKind.WoodCrate) && cell.View != null)
            {
                cell.View.PlayHitShake();
                _audioPlayer?.PlayUiClick();
            }

            return;
        }

        if (cell.Kind == TileKind.Rocket || cell.Kind == TileKind.Bomb || cell.Kind == TileKind.Disco)
        {
            PushUndoSnapshot();
            _levelLimit?.ConsumeMove();
            StartCoroutine(ActivateStandaloneBoosterRoutine(new Vector2Int(row, col)));
            return;
        }

        Array.Clear(_visited, 0, _visited.Length);
        MatchFinder.FindConnectedColorGroup(_grid, _rows, _cols, row, col, _scratchGroup, _visited);
        if (_scratchGroup.Count < 2)
        {
            cell.View?.PlayHitShake();
            return;
        }

        PushUndoSnapshot();
        _levelLimit?.ConsumeMove();
        StartCoroutine(ExecuteGroupMatchRoutine(new Vector2Int(row, col), new List<Vector2Int>(_scratchGroup)));
    }

    private IEnumerator ExecuteGroupMatchRoutine(Vector2Int tappedCell, List<Vector2Int> group)
    {
        _isResolving = true;
        OnPowerUpStateInvalidated?.Invoke();

        BlockColor groupColor = _grid[tappedCell.x, tappedCell.y].Color;
        BoosterType earnedBooster = DetermineBoosterTier(group.Count);

        var hitObstaclesThisWave = new HashSet<Vector2Int>();

        if (earnedBooster != BoosterType.None || group.Count >= 4)
        {
            bool inwardConverge = earnedBooster != BoosterType.None;
            yield return PopGroupInWavesRoutine(tappedCell, group, inwardConverge, hitObstaclesThisWave);
        }
        else
        {
            _audioPlayer?.PlayBlockPop(group.Count);
            for (int i = 0; i < group.Count; i++)
            {
                Vector2Int p = group[i];
                ClearColorCellAndRecord(p.x, p.y);
                DamageAdjacentObstacles(p.x, p.y, hitObstaclesThisWave);
            }
        }

        if (earnedBooster != BoosterType.None)
        {
            // Count the consumed origin block toward color goal before replacing with booster object
            _goalsController?.RecordBlockCleared(groupColor, 1);

            TransformCellIntoBooster(tappedCell.x, tappedCell.y, earnedBooster, groupColor);
            TileView boosterView = _grid[tappedCell.x, tappedCell.y].View;
            if (boosterView != null)
            {
                boosterView.PlaySpawnPop(1.35f);
            }
            _vfxController?.PlayBlockPop(CellToWorldPosition(tappedCell.x, tappedCell.y), Color.white, 1.25f);
            _audioPlayer?.PlayBoosterBlast();

            if (!_requireTapToActivateBoosters)
            {
                boosterView?.BoostSortingOrder(90);
                float armDelay = Mathf.Max(_boosterArmDelay, 0.50f);
                yield return new WaitForSeconds(armDelay);
                yield return DetonateBoostersChainRoutine(tappedCell);
            }
        }

        yield return CollapseRefillAndRefreshRoutine();
    }

    private IEnumerator PopGroupInWavesRoutine(
        Vector2Int origin,
        List<Vector2Int> group,
        bool skipOriginAndConvergeInward,
        HashSet<Vector2Int> hitObstaclesThisWave)
    {
        int maxDist = 0;
        for (int i = 0; i < group.Count; i++)
        {
            Vector2Int p = group[i];
            if (skipOriginAndConvergeInward && p == origin)
            {
                continue;
            }
            int d = Mathf.Abs(p.x - origin.x) + Mathf.Abs(p.y - origin.y);
            if (d > maxDist) maxDist = d;
        }

        if (skipOriginAndConvergeInward)
        {
            // Converge outer tiles inward toward tappedCell before forming the booster
            int ringSteps = Mathf.Max(1, maxDist);
            float stepDelay = Mathf.Min(0.018f, 0.085f / ringSteps);
            for (int d = maxDist; d >= 1; d--)
            {
                bool anyPopped = false;
                for (int i = 0; i < group.Count; i++)
                {
                    Vector2Int p = group[i];
                    if (p == origin) continue;
                    if (Mathf.Abs(p.x - origin.x) + Mathf.Abs(p.y - origin.y) == d)
                    {
                        ClearColorCellAndRecord(p.x, p.y);
                        DamageAdjacentObstacles(p.x, p.y, hitObstaclesThisWave);
                        anyPopped = true;
                    }
                }
                if (anyPopped)
                {
                    _audioPlayer?.PlayBlockPop(2 + (maxDist - d));
                    yield return new WaitForSeconds(stepDelay);
                }
            }
        }
        else
        {
            // Ripple outward from tappedCell for large plain matches (4+)
            int ringSteps = Mathf.Max(1, maxDist + 1);
            float stepDelay = Mathf.Min(0.016f, 0.075f / ringSteps);
            for (int d = 0; d <= maxDist; d++)
            {
                bool anyPopped = false;
                for (int i = 0; i < group.Count; i++)
                {
                    Vector2Int p = group[i];
                    if (Mathf.Abs(p.x - origin.x) + Mathf.Abs(p.y - origin.y) == d)
                    {
                        ClearColorCellAndRecord(p.x, p.y);
                        DamageAdjacentObstacles(p.x, p.y, hitObstaclesThisWave);
                        anyPopped = true;
                    }
                }
                if (anyPopped)
                {
                    _audioPlayer?.PlayBlockPop(2 + d);
                    if (d < maxDist)
                    {
                        yield return new WaitForSeconds(stepDelay);
                    }
                }
            }
        }
    }

    private IEnumerator ActivateStandaloneBoosterRoutine(Vector2Int origin)
    {
        _isResolving = true;
        OnPowerUpStateInvalidated?.Invoke();

        bool forceCrossAtOrigin = false;
        if (_grid[origin.x, origin.y].Kind == TileKind.Rocket)
        {
            var partnerRockets = CollectAdjacentRockets(origin);
            if (partnerRockets.Count > 0)
            {
                forceCrossAtOrigin = true;
                yield return MergeBoostersIntoOriginRoutine(origin, partnerRockets);
            }
        }

        yield return DetonateBoostersChainRoutine(origin, forceCrossAtOrigin);
        yield return CollapseRefillAndRefreshRoutine();
    }

    private List<Vector2Int> CollectAdjacentRockets(Vector2Int origin)
    {
        var result = new List<Vector2Int>(4);
        int[] dr = { -1, 1, 0, 0 };
        int[] dc = { 0, 0, -1, 1 };

        for (int d = 0; d < 4; d++)
        {
            int nr = origin.x + dr[d];
            int nc = origin.y + dc[d];
            if (nr < 0 || nr >= _rows || nc < 0 || nc >= _cols)
            {
                continue;
            }

            BoardCellData neighbor = _grid[nr, nc];
            if (!neighbor.IsEmpty && !neighbor.IsLocked && neighbor.Kind == TileKind.Rocket)
            {
                result.Add(new Vector2Int(nr, nc));
            }
        }

        return result;
    }

    private IEnumerator MergeBoostersIntoOriginRoutine(Vector2Int origin, List<Vector2Int> partners)
    {
        Vector3 targetWorld = CellToWorldPosition(origin.x, origin.y);
        var mergingViews = new List<TileView>(partners.Count);
        var startPositions = new List<Vector3>(partners.Count);

        for (int i = 0; i < partners.Count; i++)
        {
            Vector2Int p = partners[i];
            TileView view = _grid[p.x, p.y].View;
            if (view != null)
            {
                view.BoostSortingOrder(60);
                mergingViews.Add(view);
                startPositions.Add(view.transform.position);
            }

            // Detach logical cell so the merged partner is consumed cleanly
            _grid[p.x, p.y] = new BoardCellData { IsEmpty = true };
        }

        const float mergeDuration = 0.13f;
        for (float t = 0f; t < mergeDuration; t += Time.deltaTime)
        {
            float alpha = Mathf.Clamp01(t / mergeDuration);
            float ease = alpha * alpha;
            for (int i = 0; i < mergingViews.Count; i++)
            {
                if (mergingViews[i] != null)
                {
                    mergingViews[i].transform.position = Vector3.Lerp(startPositions[i], targetWorld, ease);
                }
            }

            yield return null;
        }

        for (int i = 0; i < mergingViews.Count; i++)
        {
            if (mergingViews[i] != null)
            {
                mergingViews[i].ResetState();
                mergingViews[i].gameObject.SetActive(false);
            }
        }

        _grid[origin.x, origin.y].View?.PlaySpawnPop(1.45f);
        _audioPlayer?.PlayBoosterBlast();
        yield return new WaitForSeconds(0.05f);
    }

    private IEnumerator DetonateBoostersChainRoutine(Vector2Int initialBoosterPos, bool forceCrossAtInitial = false)
    {
        var pending = new Queue<Vector2Int>();
        var detonated = new HashSet<Vector2Int>();

        pending.Enqueue(initialBoosterPos);
        detonated.Add(initialBoosterPos);

        while (pending.Count > 0)
        {
            Vector2Int pos = pending.Dequeue();
            BoardCellData boosterCell = _grid[pos.x, pos.y];
            if (boosterCell.IsEmpty)
            {
                continue;
            }

            BoosterType type = boosterCell.Booster != BoosterType.None
                ? boosterCell.Booster
                : KindToBoosterType(boosterCell.Kind);

            if (type == BoosterType.None)
            {
                continue;
            }

            bool isCrossRocket = forceCrossAtInitial && pos == initialBoosterPos && type == BoosterType.Rocket;
            var damagedThisBooster = new HashSet<Vector2Int>();

            // Clear the booster cell first so it cannot loop
            ReleaseCellView(pos.x, pos.y);
            _grid[pos.x, pos.y] = new BoardCellData { IsEmpty = true };
            _audioPlayer?.PlayBoosterBlast();

            Vector3 originWorld = CellToWorldPosition(pos.x, pos.y);

            switch (type)
            {
                case BoosterType.Rocket:
                    yield return DetonateRocketSequentialRoutine(
                        pos,
                        boosterCell.HorizontalRocket,
                        isCrossRocket,
                        originWorld,
                        pending,
                        detonated,
                        damagedThisBooster);
                    break;

                case BoosterType.Bomb:
                    yield return DetonateBombSequentialRoutine(
                        pos,
                        originWorld,
                        pending,
                        detonated,
                        damagedThisBooster);
                    break;

                case BoosterType.Disco:
                    yield return DetonateDiscoSequentialRoutine(
                        pos,
                        boosterCell.Color,
                        originWorld,
                        pending,
                        detonated,
                        damagedThisBooster);
                    break;
            }

            if (pending.Count > 0)
            {
                yield return new WaitForSeconds(0.06f);
            }
        }
    }

    private static readonly Vector2Int[] VerticalRocketDirs =
    {
        new Vector2Int(-1, 0),
        new Vector2Int(1, 0)
    };

    private static readonly Vector2Int[] HorizontalRocketDirs =
    {
        new Vector2Int(0, -1),
        new Vector2Int(0, 1)
    };

    private static readonly Vector2Int[] CrossRocketDirs =
    {
        new Vector2Int(-1, 0),
        new Vector2Int(1, 0),
        new Vector2Int(0, -1),
        new Vector2Int(0, 1)
    };

    private static readonly Vector2Int[] BombDiagonalDirs =
    {
        new Vector2Int(-1, -1),
        new Vector2Int(-1, 1),
        new Vector2Int(1, -1),
        new Vector2Int(1, 1)
    };

    private IEnumerator DetonateRocketSequentialRoutine(
        Vector2Int pos,
        bool isHorizontal,
        bool isCross,
        Vector3 originWorld,
        Queue<Vector2Int> pending,
        HashSet<Vector2Int> detonated,
        HashSet<Vector2Int> damagedThisBooster)
    {
        Vector2Int[] dirs = isCross
            ? CrossRocketDirs
            : (isHorizontal ? HorizontalRocketDirs : VerticalRocketDirs);

        int maxReach = 0;
        for (int d = 0; d < dirs.Length; d++)
        {
            Vector2Int dir = dirs[d];
            int reach = 0;
            if (dir.x < 0) reach = pos.x;
            else if (dir.x > 0) reach = (_rows - 1) - pos.x;
            else if (dir.y < 0) reach = pos.y;
            else if (dir.y > 0) reach = (_cols - 1) - pos.y;
            if (reach > maxReach) maxReach = reach;
        }

        _vfxController?.PlayRocketLaunch(originWorld, isHorizontal, isCross, _geometry.BlockW);
        if (maxReach <= 0)
        {
            yield break;
        }

        float stepDelay = Mathf.Clamp(0.28f / Mathf.Max(4, maxReach), 0.025f, 0.038f);
        Color rocketFlashTint = new Color(1f, 0.86f, 0.28f, 1f);

        for (int step = 1; step <= maxReach; step++)
        {
            // Fire moving thruster streaks along every active arm before waiting for arrival
            for (int d = 0; d < dirs.Length; d++)
            {
                Vector2Int dir = dirs[d];
                int nr = pos.x + dir.x * step;
                int nc = pos.y + dir.y * step;
                if (nr < 0 || nr >= _rows || nc < 0 || nc >= _cols)
                {
                    continue;
                }

                Vector3 prevWorld = CellToWorldPosition(pos.x + dir.x * (step - 1), pos.y + dir.y * (step - 1));
                Vector3 nextWorld = CellToWorldPosition(nr, nc);
                _vfxController?.PlayRocketStepProjectile(prevWorld, nextWorld, stepDelay, _geometry.BlockW);
            }

            yield return new WaitForSeconds(stepDelay);

            bool anyHitThisStep = false;
            for (int d = 0; d < dirs.Length; d++)
            {
                Vector2Int dir = dirs[d];
                int nr = pos.x + dir.x * step;
                int nc = pos.y + dir.y * step;
                if (nr < 0 || nr >= _rows || nc < 0 || nc >= _cols)
                {
                    continue;
                }

                if (ProcessBoosterHitCell(new Vector2Int(nr, nc), pending, detonated, damagedThisBooster, rocketFlashTint))
                {
                    anyHitThisStep = true;
                }
            }

            if (anyHitThisStep)
            {
                _audioPlayer?.PlayBlockPop(2 + step);
            }
        }
    }

    private IEnumerator DetonateBombSequentialRoutine(
        Vector2Int pos,
        Vector3 originWorld,
        Queue<Vector2Int> pending,
        HashSet<Vector2Int> detonated,
        HashSet<Vector2Int> damagedThisBooster)
    {
        Color bombTint = new Color(1f, 0.56f, 0.14f, 1f);

        // Stage 0: Inner fireball + orthogonal cross neighbors
        _vfxController?.PlayBombDetonationStep(originWorld, _geometry.BlockW, stage: 0);
        bool anyStage0 = false;
        for (int d = 0; d < CrossRocketDirs.Length; d++)
        {
            int nr = pos.x + CrossRocketDirs[d].x;
            int nc = pos.y + CrossRocketDirs[d].y;
            if (nr >= 0 && nr < _rows && nc >= 0 && nc < _cols)
            {
                if (ProcessBoosterHitCell(new Vector2Int(nr, nc), pending, detonated, damagedThisBooster, bombTint))
                {
                    anyStage0 = true;
                }
            }
        }
        if (anyStage0)
        {
            _audioPlayer?.PlayBlockPop(5);
        }

        yield return new WaitForSeconds(0.050f);

        // Stage 1: Outer rolling ring + diagonal corner cells
        _vfxController?.PlayBombDetonationStep(originWorld, _geometry.BlockW, stage: 1);
        bool anyStage1 = false;
        for (int d = 0; d < BombDiagonalDirs.Length; d++)
        {
            int nr = pos.x + BombDiagonalDirs[d].x;
            int nc = pos.y + BombDiagonalDirs[d].y;
            if (nr >= 0 && nr < _rows && nc >= 0 && nc < _cols)
            {
                if (ProcessBoosterHitCell(new Vector2Int(nr, nc), pending, detonated, damagedThisBooster, bombTint))
                {
                    anyStage1 = true;
                }
            }
        }
        if (anyStage1)
        {
            _audioPlayer?.PlayBlockPop(8);
        }
    }

    private IEnumerator DetonateDiscoSequentialRoutine(
        Vector2Int pos,
        BlockColor sourceColor,
        Vector3 originWorld,
        Queue<Vector2Int> pending,
        HashSet<Vector2Int> detonated,
        HashSet<Vector2Int> damagedThisBooster)
    {
        BlockColor effectiveColor = ResolveDiscoTargetColor(sourceColor);
        Color beamColor = _spriteCatalog.GetBlastColor(effectiveColor);

        var targets = new List<Vector2Int>(24);
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                BoardCellData candidate = _grid[r, c];
                if (!candidate.IsEmpty && candidate.Kind == TileKind.ColorBlock && candidate.Color == effectiveColor)
                {
                    targets.Add(new Vector2Int(r, c));
                }
            }
        }

        _vfxController?.PlayDiscoChargePulse(originWorld, beamColor, _geometry.BlockW);
        if (targets.Count == 0)
        {
            yield break;
        }

        // Sort nearest-first so energy whips radiate cleanly outward from the disco ball
        targets.Sort((a, b) =>
        {
            int da = (a.x - pos.x) * (a.x - pos.x) + (a.y - pos.y) * (a.y - pos.y);
            int db = (b.x - pos.x) * (b.x - pos.x) + (b.y - pos.y) * (b.y - pos.y);
            return da.CompareTo(db);
        });

        yield return new WaitForSeconds(0.065f);

        int batchSize = targets.Count > 8 ? 2 : 1;
        int totalWaves = (targets.Count + batchSize - 1) / batchSize;
        float stepDelay = Mathf.Clamp(0.28f / Mathf.Max(3, totalWaves), 0.024f, 0.036f);

        for (int idx = 0, wave = 0; idx < targets.Count; idx += batchSize, wave++)
        {
            int end = Mathf.Min(targets.Count, idx + batchSize);
            bool anyHit = false;
            for (int k = idx; k < end; k++)
            {
                Vector2Int t = targets[k];
                Vector3 targetWorld = CellToWorldPosition(t.x, t.y);
                _vfxController?.PlayDiscoRayHit(originWorld, targetWorld, beamColor, _geometry.BlockW);
                if (ProcessBoosterHitCell(t, pending, detonated, damagedThisBooster, beamColor))
                {
                    anyHit = true;
                }
            }

            if (anyHit)
            {
                _audioPlayer?.PlayBlockPop(2 + wave);
            }

            if (end < targets.Count)
            {
                yield return new WaitForSeconds(stepDelay);
            }
        }
    }

    private bool ProcessBoosterHitCell(
        Vector2Int target,
        Queue<Vector2Int> pending,
        HashSet<Vector2Int> detonated,
        HashSet<Vector2Int> damagedThisBooster,
        Color impactTint)
    {
        BoardCellData targetCell = _grid[target.x, target.y];
        Vector3 worldPos = CellToWorldPosition(target.x, target.y);

        if (targetCell.IsEmpty)
        {
            _vfxController?.PlayCellImpactFlash(worldPos, impactTint, _geometry.BlockW, 0.72f);
            return false;
        }

        BoosterType hitBooster = targetCell.Booster != BoosterType.None
            ? targetCell.Booster
            : KindToBoosterType(targetCell.Kind);

        if (hitBooster != BoosterType.None && detonated.Add(target))
        {
            pending.Enqueue(target);
            targetCell.View?.PlaySpawnPop(1.22f);
            _vfxController?.PlayCellImpactFlash(worldPos, Color.white, _geometry.BlockW, 1.05f);
            return true;
        }

        _vfxController?.PlayCellImpactFlash(worldPos, impactTint, _geometry.BlockW, 0.95f);
        ApplyBlastHitToCell(target.x, target.y, damagedThisBooster);
        return true;
    }

    private IEnumerator CollapseRefillAndRefreshRoutine()
    {
        _isBusyWithGravity = true;
        ApplyGravityAndRefill();

        float maxWait = 1.6f;
        float elapsed = 0f;
        while (AnyViewMoving() && elapsed < maxWait)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Ensure zero floating tiles if safety cap is reached
        SnapAllViewsToGrid();

        _isBusyWithGravity = false;

        if (!MatchFinder.HasAnyValidMove(_grid, _rows, _cols))
        {
            MatchFinder.ShufflePlayableColorBlocks(_grid, _rows, _cols, _rng);
            RecomputeGroupsAndRefreshAllViews(snapPositions: false, playSpawnPop: true);
            yield return new WaitForSeconds(0.14f);
        }
        else
        {
            RecomputeGroupsAndRefreshAllViews(snapPositions: false, playSpawnPop: false);
        }

        _isResolving = false;
        OnPowerUpStateInvalidated?.Invoke();
        OnMoveResolutionCompleted?.Invoke();
    }

    private void ApplyGravityAndRefill()
    {
        for (int c = 0; c < _cols; c++)
        {
            // Pass 1: Compact unpinned tiles downward within each contiguous segment bounded by pinned obstacles
            int writeRow = 0;
            for (int r = 0; r < _rows; r++)
            {
                BoardCellData cell = _grid[r, c];
                if (cell.IsEmpty)
                {
                    continue;
                }

                if (cell.IsPinnedObstacle)
                {
                    writeRow = r + 1;
                    continue;
                }

                if (r != writeRow)
                {
                    _grid[writeRow, c] = cell;
                    _grid[r, c] = new BoardCellData { IsEmpty = true };
                    cell.View?.FallToPosition(CellToWorldPosition(writeRow, c), initialSpeed: 8.5f);
                }

                writeRow++;
            }

            // Pass 2: Refill remaining empty slots from just above the SpriteMask edge or cleanly into pockets below obstacles
            int highestPinned = -1;
            for (int r = _rows - 1; r >= 0; r--)
            {
                if (_grid[r, c].IsPinnedObstacle)
                {
                    highestPinned = r;
                    break;
                }
            }

            int aboveBoardSpawnIndex = 0;
            for (int r = 0; r < _rows; r++)
            {
                if (!_grid[r, c].IsEmpty)
                {
                    continue;
                }

                BlockColor newColor = PickRandomActiveColor();
                TileView view = AcquireTileView();

                Vector3 targetPos = CellToWorldPosition(r, c);
                if (r > highestPinned)
                {
                    Vector3 spawnPos = CellToWorldPosition(_rows + 0.35f + aboveBoardSpawnIndex * 0.90f, (float)c);
                    aboveBoardSpawnIndex++;
                    view.SnapToPosition(spawnPos);
                    view.FallToPosition(targetPos, initialSpeed: 9.5f);
                }
                else
                {
                    view.SnapToPosition(targetPos);
                    view.PlaySpawnPop(1.0f);
                }

                _grid[r, c] = new BoardCellData
                {
                    IsEmpty = false,
                    Kind = TileKind.ColorBlock,
                    Color = newColor,
                    HitPoints = 1,
                    IsLocked = false,
                    Booster = BoosterType.None,
                    View = view
                };
            }
        }

        RecomputeGroupsAndRefreshAllViews(snapPositions: false, playSpawnPop: false);
    }

    private void ClearColorCellAndRecord(int r, int c)
    {
        BoardCellData cell = _grid[r, c];
        if (cell.IsEmpty)
        {
            return;
        }

        if (cell.Kind == TileKind.ColorBlock)
        {
            _goalsController?.RecordBlockCleared(cell.Color, 1);
            _vfxController?.PlayBlockPop(CellToWorldPosition(r, c), _spriteCatalog.GetBlastColor(cell.Color));
        }

        ReleaseCellView(r, c);
        _grid[r, c] = new BoardCellData { IsEmpty = true };
    }

    private void ApplyBlastHitToCell(int r, int c, HashSet<Vector2Int> damagedObstaclesThisWave)
    {
        BoardCellData cell = _grid[r, c];
        if (cell.IsEmpty)
        {
            return;
        }

        if (cell.IsLocked)
        {
            if (damagedObstaclesThisWave.Add(new Vector2Int(r, c)))
            {
                UnlockCell(r, c);
            }
            return;
        }

        if (cell.Kind == TileKind.WoodCrate)
        {
            if (damagedObstaclesThisWave.Add(new Vector2Int(r, c)))
            {
                DamageWoodCrate(r, c);
            }
            return;
        }

        ClearColorCellAndRecord(r, c);
    }

    private void DamageAdjacentObstacles(int r, int c, HashSet<Vector2Int> hitObstaclesThisWave)
    {
        int[] dr = { -1, 1, 0, 0 };
        int[] dc = { 0, 0, -1, 1 };

        for (int d = 0; d < 4; d++)
        {
            int nr = r + dr[d];
            int nc = c + dc[d];
            if (nr < 0 || nr >= _rows || nc < 0 || nc >= _cols)
            {
                continue;
            }

            BoardCellData neighbor = _grid[nr, nc];
            if (neighbor.IsEmpty || !neighbor.IsPinnedObstacle)
            {
                continue;
            }

            var key = new Vector2Int(nr, nc);
            if (!hitObstaclesThisWave.Add(key))
            {
                continue;
            }

            if (neighbor.IsLocked)
            {
                UnlockCell(nr, nc);
            }
            else if (neighbor.Kind == TileKind.WoodCrate)
            {
                DamageWoodCrate(nr, nc);
            }
        }
    }

    private void UnlockCell(int r, int c)
    {
        BoardCellData cell = _grid[r, c];
        if (cell.IsEmpty || !cell.IsLocked)
        {
            return;
        }

        cell.LockHitPoints = cell.LockHitPoints > 0 ? cell.LockHitPoints - 1 : 0;
        if (cell.LockHitPoints > 0)
        {
            _grid[r, c] = cell;
            RefreshSingleCellVisual(r, c);
            cell.View?.PlayHitShake();
            _vfxController?.PlayBlockPop(CellToWorldPosition(r, c), new Color(0.86f, 0.88f, 0.94f, 1f), 0.72f);
            return;
        }

        cell.IsLocked = false;
        cell.LockHitPoints = 0;
        _grid[r, c] = cell;
        RefreshSingleCellVisual(r, c);
        cell.View?.PlaySpawnPop(1.1f);
        _vfxController?.PlayBlockPop(CellToWorldPosition(r, c), new Color(1f, 0.86f, 0.3f, 1f), 0.95f);
        _goalsController?.RecordLockBroken(1);
    }

    private void DamageWoodCrate(int r, int c)
    {
        BoardCellData cell = _grid[r, c];
        if (cell.IsEmpty || cell.Kind != TileKind.WoodCrate)
        {
            return;
        }

        cell.HitPoints--;
        if (cell.HitPoints > 0)
        {
            _grid[r, c] = cell;
            cell.View?.PlayHitShake();
            _vfxController?.PlayBlockPop(CellToWorldPosition(r, c), new Color(0.68f, 0.44f, 0.22f, 1f), 0.65f);
            RefreshSingleCellVisual(r, c);
            return;
        }

        _vfxController?.PlayBlockPop(CellToWorldPosition(r, c), new Color(0.76f, 0.48f, 0.22f, 1f), 1.05f);

        // Uncover an active colored cube directly underneath the broken crate
        BlockColor revealedColor = (_activeColors.Count == 0 || _activeColors.Contains(cell.Color))
            ? cell.Color
            : PickRandomActiveColor();

        if (cell.View == null)
        {
            cell.View = AcquireTileView();
            cell.View.SnapToPosition(CellToWorldPosition(r, c));
        }

        cell.IsEmpty = false;
        cell.IsLocked = false;
        cell.Kind = TileKind.ColorBlock;
        cell.Booster = BoosterType.None;
        cell.HorizontalRocket = false;
        cell.HitPoints = 1;
        cell.Color = revealedColor;
        _grid[r, c] = cell;

        RefreshSingleCellVisual(r, c);
        cell.View?.PlaySpawnPop(1.25f);
        _goalsController?.RecordWoodCrateBroken(1);
    }

    private void TransformCellIntoBooster(int r, int c, BoosterType booster, BlockColor originColor)
    {
        BoardCellData cell = _grid[r, c];
        if (cell.View == null)
        {
            cell.View = AcquireTileView();
            cell.View.SnapToPosition(CellToWorldPosition(r, c));
        }

        cell.IsEmpty = false;
        cell.IsLocked = false;
        cell.Booster = booster;
        cell.Color = originColor;
        cell.HorizontalRocket = false;
        cell.Kind = booster switch
        {
            BoosterType.Rocket => TileKind.Rocket,
            BoosterType.Bomb => TileKind.Bomb,
            BoosterType.Disco => TileKind.Disco,
            _ => TileKind.ColorBlock
        };

        _grid[r, c] = cell;
        RefreshSingleCellVisual(r, c);
        cell.View.PlaySpawnPop(1.35f);
    }

    private bool ExecuteSolvePartial()
    {
        Func<BlockColor, bool> goalFilter = _goalsController != null
            ? color => _goalsController.IsActiveGoalColor(color)
            : null;

        if (!MatchFinder.FindBestSolvePartialGroup(_grid, _rows, _cols, _visited, _scratchGroup, _solveBestGroup, goalFilter))
        {
            return false;
        }

        PushUndoSnapshot();
        Vector2Int origin = _solveBestGroup[0];
        StartCoroutine(ExecuteGroupMatchRoutine(origin, new List<Vector2Int>(_solveBestGroup)));
        return true;
    }

    private bool ExecuteUndo()
    {
        if (_undoStack.Count == 0 || _isResolving)
        {
            return false;
        }

        BoardSnapshot snap = _undoStack.Pop();

        // Return current active views cleanly to pool, then rebuild views to match snapshot
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                ReleaseCellView(r, c);
                BoardCellData restored = snap.GridCopy[r, c];
                if (!restored.IsEmpty)
                {
                    TileView view = AcquireTileView();
                    view.SnapToPosition(CellToWorldPosition(r, c));
                    restored.View = view;
                }

                _grid[r, c] = restored;
            }
        }

        _goalsController?.RestoreRemainingCounts(snap.GoalRemaining);
        if (_levelLimit != null && _levelLimit.LimitMode == LevelLimitType.Moves && snap.MovesRemaining > 0)
        {
            _levelLimit.RestoreMoves(snap.MovesRemaining);
        }

        RecomputeGroupsAndRefreshAllViews(snapPositions: true, playSpawnPop: false);
        _audioPlayer?.PlayUiClick();
        OnPowerUpStateInvalidated?.Invoke();
        return true;
    }

    private bool ExecuteBreakLock()
    {
        var lockedTargets = new List<Vector2Int>(16);
        var crateTargets = new List<Vector2Int>(16);

        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                BoardCellData cell = _grid[r, c];
                if (cell.IsEmpty) continue;
                if (cell.IsLocked) lockedTargets.Add(new Vector2Int(r, c));
                else if (cell.Kind == TileKind.WoodCrate) crateTargets.Add(new Vector2Int(r, c));
            }
        }

        if (lockedTargets.Count == 0 && crateTargets.Count == 0)
        {
            return false;
        }

        PushUndoSnapshot();

        int remainingQuota = _breakLockTargetsPerUse;
        for (int i = 0; i < lockedTargets.Count && remainingQuota > 0; i++, remainingQuota--)
        {
            // BreakLock breaks locks strongly (destroys immediately even if multi-hit)
            Vector2Int lp = lockedTargets[i];
            if (!_grid[lp.x, lp.y].IsEmpty)
            {
                BoardCellData locked = _grid[lp.x, lp.y];
                locked.LockHitPoints = 1;
                _grid[lp.x, lp.y] = locked;
                UnlockCell(lp.x, lp.y);
            }
        }

        for (int i = 0; i < crateTargets.Count && remainingQuota > 0; i++, remainingQuota--)
        {
            // BreakLock breaks crates strongly (destroys immediately even if 2 HP)
            Vector2Int cp = crateTargets[i];
            if (!_grid[cp.x, cp.y].IsEmpty)
            {
                BoardCellData crate = _grid[cp.x, cp.y];
                crate.HitPoints = 1;
                _grid[cp.x, cp.y] = crate;
                DamageWoodCrate(cp.x, cp.y);
            }
        }

        _audioPlayer?.PlayBoosterBlast();
        StartCoroutine(CollapseRefillAndRefreshRoutine());
        return true;
    }

    private void PushUndoSnapshot()
    {
        var copy = new BoardCellData[_rows, _cols];
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                BoardCellData cell = _grid[r, c];
                cell.View = null;
                copy[r, c] = cell;
            }
        }

        var snapshot = new BoardSnapshot
        {
            GridCopy = copy,
            GoalRemaining = _goalsController != null ? _goalsController.CaptureRemainingCounts() : null,
            MovesRemaining = _levelLimit != null ? _levelLimit.RemainingMoves : 0
        };

        if (_undoStack.Count >= MaxUndoDepth)
        {
            // Keep most recent MaxUndoDepth entries
            var list = new List<BoardSnapshot>(_undoStack);
            list.RemoveAt(list.Count - 1);
            list.Reverse();
            _undoStack.Clear();
            for (int i = 0; i < list.Count; i++) _undoStack.Push(list[i]);
        }

        _undoStack.Push(snapshot);
        OnPowerUpStateInvalidated?.Invoke();
    }

    private bool HasAnyObstacleOnBoard()
    {
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                if (!_grid[r, c].IsEmpty && _grid[r, c].IsPinnedObstacle)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void PopulateInitialGrid(LevelConfigSO config)
    {
        // First fill all cells with playable colors
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                _grid[r, c] = new BoardCellData
                {
                    IsEmpty = false,
                    Kind = TileKind.ColorBlock,
                    Color = PickRandomActiveColor(),
                    HitPoints = 1,
                    IsLocked = false,
                    Booster = BoosterType.None
                };
            }
        }

        // Gather candidate coordinates cleanly (keep slight margin when large enough, fallback to all cells)
        var candidates = new List<Vector2Int>(_rows * _cols);
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                candidates.Add(new Vector2Int(r, c));
            }
        }

        ShuffleCoordinates(candidates);

        int cratesLeft = config != null ? Mathf.Min(config.WoodCrateCount, candidates.Count / 3) : 0;
        int locksLeft = config != null ? Mathf.Min(config.LockedBlockCount, Mathf.Max(0, candidates.Count / 3 - cratesLeft)) : 0;

        int cursor = 0;
        for (int i = 0; i < cratesLeft && cursor < candidates.Count; i++, cursor++)
        {
            Vector2Int p = candidates[cursor];
            _grid[p.x, p.y].Kind = TileKind.WoodCrate;
            _grid[p.x, p.y].HitPoints = _crateHitPoints;
        }

        for (int i = 0; i < locksLeft && cursor < candidates.Count; i++, cursor++)
        {
            Vector2Int p = candidates[cursor];
            _grid[p.x, p.y].IsLocked = true;
            _grid[p.x, p.y].LockHitPoints = _lockHitPoints;
        }

        // Create Views for all cells
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                TileView view = AcquireTileView();
                view.name = $"Tile_R{r}_C{c}";
                view.SnapToPosition(CellToWorldPosition(r, c));
                _grid[r, c].View = view;
            }
        }
    }

    private void EnsureDeadlockFreeImmediate()
    {
        if (!MatchFinder.HasAnyValidMove(_grid, _rows, _cols))
        {
            MatchFinder.ShufflePlayableColorBlocks(_grid, _rows, _cols, _rng);
        }
    }

    private void RecomputeGroupsAndRefreshAllViews(bool snapPositions, bool playSpawnPop)
    {
        MatchFinder.ComputeAllGroupSizes(_grid, _rows, _cols, _groupSizes, _visited, _scratchGroup);
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                if (_grid[r, c].IsEmpty || _grid[r, c].View == null)
                {
                    continue;
                }

                if (snapPositions)
                {
                    _grid[r, c].View.SnapToPosition(CellToWorldPosition(r, c));
                }

                if (playSpawnPop)
                {
                    _grid[r, c].View.PlaySpawnPop(0.85f);
                }

                RefreshSingleCellVisual(r, c);
            }
        }
    }

    private void RefreshSingleCellVisual(int r, int c)
    {
        BoardCellData cell = _grid[r, c];
        if (cell.IsEmpty || cell.View == null)
        {
            return;
        }

        Sprite sprite = ResolveCellSprite(r, c, cell);
        Vector3 worldScale = ComputeWorldScaleForSprite(sprite);

        int rowDepthRank = _geometry.RowStackOrder == RandomBoardFiller.RowStackOrder.UpperRowsOnTop
            ? r
            : (_rows - 1 - r);
        // Reserve 4 sorting slots per cell so overlays cleanly stay under the next Jenga upper row
        int order = _geometry.BaseSortingOrder + (rowDepthRank * _cols + c) * 4;

        bool isCrackedCrate = cell.Kind == TileKind.WoodCrate && _crateHitPoints > 1 && cell.HitPoints < _crateHitPoints;
        int lockDamageStage = 0;
        if (cell.IsLocked && _lockHitPoints > 1 && cell.LockHitPoints < _lockHitPoints)
        {
            int hitsTaken = Mathf.Max(0, _lockHitPoints - cell.LockHitPoints);
            // Map damage cleanly across 2 visual stages regardless of max HP (1 hit left always hits stage 2)
            lockDamageStage = (cell.LockHitPoints == 1 && hitsTaken >= 2) || hitsTaken >= 2 ? 2 : 1;
        }

        cell.View.ConfigureVisual(
            sprite,
            worldScale,
            _geometry.Rotation,
            _geometry.SortingLayerName,
            order,
            cell.IsLocked,
            _spriteCatalog.LockOverlaySprite,
            lockDamageStage,
            isCrackedCrate);
    }

    private Sprite ResolveCellSprite(int r, int c, BoardCellData cell)
    {
        if (cell.Kind == TileKind.WoodCrate)
        {
            return _spriteCatalog.WoodCrateSprite;
        }

        if (cell.Kind == TileKind.Rocket || cell.Kind == TileKind.Bomb || cell.Kind == TileKind.Disco)
        {
            return _spriteCatalog.GetBoosterSprite(KindToBoosterType(cell.Kind));
        }

        int groupSize = cell.IsLocked ? 1 : _groupSizes[r, c];
        return _spriteCatalog.GetBlockSprite(cell.Color, groupSize, _rocketThreshold, _bombThreshold, _discoThreshold);
    }

    private Vector3 ComputeWorldScaleForSprite(Sprite sprite)
    {
        if (sprite == null || _boardLayoutSource == null || _tileContainer == null)
        {
            return Vector3.one;
        }

        Vector2 effUnitSize = _boardLayoutSource.ComputeEffectiveSpriteUnitSize(sprite);
        Vector3 lossy = _tileContainer.lossyScale;
        float invX = Mathf.Abs(lossy.x) > 1e-5f ? 1f / lossy.x : 1f;
        float invY = Mathf.Abs(lossy.y) > 1e-5f ? 1f / lossy.y : 1f;

        return new Vector3(
            (_geometry.BlockW / Mathf.Max(0.001f, effUnitSize.x)) * invX,
            (_geometry.BlockH / Mathf.Max(0.001f, effUnitSize.y)) * invY,
            1f);
    }

    private Vector3 CellToWorldPosition(float row, float col)
    {
        float startX = -_geometry.TotalW * 0.5f + _geometry.BlockW * 0.5f;
        float startY = -_geometry.TotalH * 0.5f + _geometry.BlockH * 0.5f;
        float ox = startX + col * _geometry.StepX;
        float oy = startY + row * _geometry.StepY;
        return _geometry.Center + _geometry.Right * ox + _geometry.Up * oy;
    }

    private void SnapAllViewsToGrid()
    {
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                TileView v = _grid[r, c].View;
                if (v != null && v.IsMoving)
                {
                    v.SnapToPosition(CellToWorldPosition(r, c));
                }
            }
        }
    }

    private void UpdateBoardSpriteMask()
    {
        if (_boardMask == null)
        {
            Transform existing = transform.Find("_BoardMask");
            if (existing != null)
            {
                _boardMask = existing.GetComponent<SpriteMask>();
            }

            if (_boardMask == null)
            {
                var go = new GameObject("_BoardMask", typeof(SpriteMask));
                go.transform.SetParent(transform, false);
                _boardMask = go.GetComponent<SpriteMask>();
            }
        }

        if (_maskUnitSprite == null)
        {
            const int s = 16;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var px = new Color32[s * s];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            _maskUnitSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
        }

        _boardMask.sprite = _maskUnitSprite;
        _boardMask.transform.position = _geometry.Center;
        _boardMask.transform.rotation = _geometry.Rotation;

        Vector3 pLossy = transform.lossyScale;
        float invX = Mathf.Abs(pLossy.x) > 1e-5f ? 1f / pLossy.x : 1f;
        float invY = Mathf.Abs(pLossy.y) > 1e-5f ? 1f / pLossy.y : 1f;
        _boardMask.transform.localScale = new Vector3(
            (_geometry.TotalW + 0.04f) * invX,
            (_geometry.TotalH + 0.02f) * invY,
            1f);
    }

    private BlockColor ResolveDiscoTargetColor(BlockColor preferred)
    {
        int preferredCount = 0;
        var counts = new int[6];
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                BoardCellData cell = _grid[r, c];
                if (!cell.IsEmpty && cell.Kind == TileKind.ColorBlock)
                {
                    int idx = (int)cell.Color;
                    if (idx >= 0 && idx < counts.Length) counts[idx]++;
                    if (cell.Color == preferred) preferredCount++;
                }
            }
        }

        if (preferredCount > 0)
        {
            return preferred;
        }

        int bestIdx = (int)preferred;
        int bestCount = -1;
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] > bestCount)
            {
                bestCount = counts[i];
                bestIdx = i;
            }
        }

        return (BlockColor)Mathf.Clamp(bestIdx, 0, counts.Length - 1);
    }

    /// <summary>
    /// Places one Rocket, one Bomb, and one Disco booster separated across the board for visual testing.
    /// </summary>
    public void SpawnDebugTestBoosters()
    {
        if (_grid == null || _isResolving || _isBusyWithGravity)
        {
            return;
        }

        int midRow = Mathf.Clamp(_rows / 2, 0, _rows - 1);
        int altRow = Mathf.Clamp(midRow - 1, 0, _rows - 1);
        Vector2Int[] spots =
        {
            new Vector2Int(midRow, 0),
            new Vector2Int(altRow, Mathf.Clamp(_cols / 2, 0, _cols - 1)),
            new Vector2Int(midRow, _cols - 1)
        };
        BoosterType[] samples = { BoosterType.Rocket, BoosterType.Bomb, BoosterType.Disco };

        for (int i = 0; i < samples.Length; i++)
        {
            int r = spots[i].x;
            int c = spots[i].y;
            BlockColor col = !_grid[r, c].IsEmpty ? _grid[r, c].Color : (BlockColor)(i % _colorCount);
            TransformCellIntoBooster(r, c, samples[i], col);
            _grid[r, c].View?.PlaySpawnPop(1.35f);
        }

        _audioPlayer?.PlayBoosterBlast();
    }

    private bool TryScreenPointToCell(Vector2 screenPos, out int row, out int col)
    {
        row = -1;
        col = -1;

        Camera cam = _inputCamera != null ? _inputCamera : Camera.main;
        if (cam == null)
        {
            return false;
        }

        float zDist = Mathf.Abs(_geometry.Center.z - cam.transform.position.z);
        Vector3 worldPoint = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, zDist));
        Vector3 rel = worldPoint - _geometry.Center;

        float lx = Vector3.Dot(rel, _geometry.Right);
        float ly = Vector3.Dot(rel, _geometry.Up);

        float startX = -_geometry.TotalW * 0.5f + _geometry.BlockW * 0.5f;
        float startY = -_geometry.TotalH * 0.5f + _geometry.BlockH * 0.5f;

        col = Mathf.RoundToInt((lx - startX) / Mathf.Max(0.001f, _geometry.StepX));
        row = Mathf.RoundToInt((ly - startY) / Mathf.Max(0.001f, _geometry.StepY));

        if (row < 0 || row >= _rows || col < 0 || col >= _cols)
        {
            return false;
        }

        float cellCenterX = startX + col * _geometry.StepX;
        float cellCenterY = startY + row * _geometry.StepY;

        return Mathf.Abs(lx - cellCenterX) <= _geometry.BlockW * 0.56f
            && Mathf.Abs(ly - cellCenterY) <= _geometry.BlockH * 0.56f;
    }

    private bool IsPointerOverBlockingUi(Vector2 screenPos)
    {
        if (EventSystem.current == null)
        {
            return false;
        }

        var eventData = new PointerEventData(EventSystem.current) { position = screenPos };
        _uiRaycastBuffer.Clear();
        EventSystem.current.RaycastAll(eventData, _uiRaycastBuffer);

        for (int i = 0; i < _uiRaycastBuffer.Count; i++)
        {
            if (_uiRaycastBuffer[i].gameObject != null && _uiRaycastBuffer[i].gameObject.layer == 5)
            {
                return true;
            }
        }

        return false;
    }

    private BoosterType DetermineBoosterTier(int groupSize)
    {
        if (groupSize >= _discoThreshold) return BoosterType.Disco;
        if (groupSize >= _bombThreshold) return BoosterType.Bomb;
        if (groupSize >= _rocketThreshold) return BoosterType.Rocket;
        return BoosterType.None;
    }

    private static BoosterType KindToBoosterType(TileKind kind)
    {
        return kind switch
        {
            TileKind.Rocket => BoosterType.Rocket,
            TileKind.Bomb => BoosterType.Bomb,
            TileKind.Disco => BoosterType.Disco,
            _ => BoosterType.None
        };
    }

    private void ShuffleCoordinates(List<Vector2Int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private bool AnyViewMoving()
    {
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                TileView v = _grid[r, c].View;
                if (v != null && v.IsMoving)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private TileView AcquireTileView()
    {
        for (int i = 0; i < _tilePool.Count; i++)
        {
            if (_tilePool[i] != null && !_tilePool[i].gameObject.activeSelf)
            {
                _tilePool[i].ResetState();
                _tilePool[i].gameObject.SetActive(true);
                return _tilePool[i];
            }
        }

        var go = new GameObject("Tile", typeof(SpriteRenderer), typeof(TileView));
        go.transform.SetParent(_tileContainer, false);
        TileView tv = go.GetComponent<TileView>();
        _tilePool.Add(tv);
        return tv;
    }

    private void ReleaseCellView(int r, int c)
    {
        TileView view = _grid[r, c].View;
        if (view != null)
        {
            view.ResetState();
            view.gameObject.SetActive(false);
            _grid[r, c].View = null;
        }
    }

    private void ReleaseAllPooledViews()
    {
        for (int i = 0; i < _tilePool.Count; i++)
        {
            if (_tilePool[i] != null)
            {
                _tilePool[i].ResetState();
                _tilePool[i].gameObject.SetActive(false);
            }
        }
    }

    private void BuildActiveLevelPalette(LevelConfigSO config)
    {
        _activeColors.Clear();
        if (config != null && config.Goals != null)
        {
            for (int i = 0; i < config.Goals.Count; i++)
            {
                GoalTargetType gt = config.Goals[i].TargetType;
                if (gt <= GoalTargetType.Yellow)
                {
                    BlockColor goalColor = (BlockColor)(int)gt;
                    if (!_activeColors.Contains(goalColor) && _activeColors.Count < _colorCount)
                    {
                        _activeColors.Add(goalColor);
                    }
                }
            }
        }

        var remaining = new List<BlockColor>(6);
        for (int i = 0; i < 6; i++)
        {
            BlockColor c = (BlockColor)i;
            if (!_activeColors.Contains(c))
            {
                remaining.Add(c);
            }
        }

        for (int i = remaining.Count - 1; i > 0; i--)
        {
            int swap = _rng.Next(i + 1);
            (remaining[i], remaining[swap]) = (remaining[swap], remaining[i]);
        }

        for (int i = 0; i < remaining.Count && _activeColors.Count < _colorCount; i++)
        {
            _activeColors.Add(remaining[i]);
        }
    }

    private BlockColor PickRandomActiveColor()
    {
        if (_activeColors.Count == 0)
        {
            return (BlockColor)_rng.Next(Mathf.Clamp(_colorCount, 1, 6));
        }

        return _activeColors[_rng.Next(_activeColors.Count)];
    }

    private void EnsureContainer()
    {
        if (_tileContainer == null)
        {
            Transform found = transform.Find(GameplayContainerName);
            if (found != null)
            {
                _tileContainer = found;
            }
            else
            {
                var go = new GameObject(GameplayContainerName);
                go.transform.SetParent(transform, false);
                _tileContainer = go.transform;
            }
        }
    }

    private void ResolveReferences()
    {
        if (_boardLayoutSource == null) _boardLayoutSource = GetComponent<RandomBoardFiller>();
        if (_goalsController == null) _goalsController = FindFirstObjectByType<GoalsController>();
        if (_levelLimit == null) _levelLimit = FindFirstObjectByType<LevelTimer>();
        if (_audioPlayer == null) _audioPlayer = FindFirstObjectByType<GameAudioPlayer>();
        if (_vfxController == null) _vfxController = FindFirstObjectByType<BlastVfxController>();
        if (_inputCamera == null) _inputCamera = Camera.main;
    }
}
