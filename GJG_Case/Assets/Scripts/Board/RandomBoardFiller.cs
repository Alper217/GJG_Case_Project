using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Fills a world-space BoxCollider2D play area randomly with gapless 2.5D block sprites
/// for layout and grid-size stress testing (e.g. 6x6, 12x15) without game logic.
/// </summary>
public class RandomBoardFiller : MonoBehaviour
{
    public enum BoardSizingMode
    {
        FixedAreaFitBlocks,
        PreserveBlocksExpandFrame
    }

    public enum GridFillMode
    {
        StretchToFillBox,
        UniformNativeAspect,
        UniformSquare
    }

    public enum RowStackOrder
    {
        UpperRowsOnTop,
        LowerRowsOnTop
    }

    private const float ReferenceScreenHeightPx = 1920f;
    private const float MaxAcceptableDistortion = 1.33f;
    private const float MinGridMaxTilePx = 210f;
    private const float ComfortableMinTilePx = 90f;
    private const float ExtremeMinTilePx = 70f;
    private const float PortraitFallbackAspect = 9f / 16f;
    private const string ContainerName = "_GeneratedGrid";

#if UNITY_EDITOR
    private const string CleanNineSliceFramePath = "Assets/Sprites/BoardFrame_9Slice.png";

    private static readonly string[] DefaultSpritePaths =
    {
        "Assets/Match_Items_internship/Blue_Default.png",
        "Assets/Match_Items_internship/Green_Default.png",
        "Assets/Match_Items_internship/Pink_Default.png",
        "Assets/Match_Items_internship/Purple_Default.png",
        "Assets/Match_Items_internship/Red_Default.png",
        "Assets/Match_Items_internship/Yellow_Default.png"
    };
#endif

    [Header("Sizing Strategy")]
    [Tooltip("FixedAreaFitBlocks stretches/fits blocks into BoxCollider2D. PreserveBlocksExpandFrame keeps block size & 0.871 aspect intact and resizes 9-slice tray around them.")]
    [SerializeField] private BoardSizingMode _sizingMode = BoardSizingMode.PreserveBlocksExpandFrame;

    [Header("Target Frame Collider")]
    [Tooltip("Empty GameObject's BoxCollider2D defining the inner bounds of the tray.")]
    [SerializeField] private BoxCollider2D _playArea;
    [SerializeField] private bool _disableColliderInPlayMode = true;

    [Header("Preserve Blocks / Expand Frame Mode")]
    [Tooltip("9-sliced wood tray SpriteRenderer (Assets/Sprites/BoardFrame_9Slice.png) resized dynamically in PreserveBlocksExpandFrame mode.")]
    [SerializeField] private SpriteRenderer _frameRenderer;
    [Tooltip("Fixed block height in world units before screen clamping (width follows exact native block aspect).")]
    [Range(0.25f, 4f)]
    [SerializeField] private float _targetBlockHeight = 1.55f;
    [Tooltip("Uniformly scales blocks down without distortion if a large grid (e.g. 8x6 or 12x15) would overflow screen bounds.")]
    [SerializeField] private bool _clampToCameraViewport = true;
    [Tooltip("Maximum portion of ortho camera width & height the expanded wood frame may occupy.")]
    [SerializeField] private Vector2 _maxViewportFill = new Vector2(0.92f, 0.68f);
    [Tooltip("Border thickness scale (0 = locks Frame Transform Scale at (1,1,1) and resizes ONLY SpriteRenderer Width/Height).")]
    [Range(0f, 3f)]
    [SerializeField] private float _frameRimScale = 0f;

    [Header("Manual Frame Edge Padding (World Units)")]
    [Tooltip("Left inner margin (negative pulls left wood wall closer to blocks).")]
    [Range(-0.5f, 0.5f)]
    [SerializeField] private float _paddingLeft = -0.08f;
    [Tooltip("Right inner margin (negative pulls right wood wall closer to blocks).")]
    [Range(-0.5f, 0.5f)]
    [SerializeField] private float _paddingRight = -0.08f;
    [Tooltip("Top inner margin (negative pulls top wood bar down closer to blocks).")]
    [Range(-0.5f, 0.5f)]
    [SerializeField] private float _paddingTop = -0.14f;
    [Tooltip("Bottom inner margin (negative pulls bottom wood rail up closer to blocks).")]
    [Range(-0.5f, 0.5f)]
    [SerializeField] private float _paddingBottom = -0.16f;
    [SerializeField] private bool _syncPlayAreaColliderToFrame = true;

    [Header("Grid Dimensions (Test 6x6, 12x15, etc.)")]
    [Range(1, 30)]
    [SerializeField] private int _rows = 8;
    [Range(1, 30)]
    [SerializeField] private int _cols = 6;
    [Range(1, 6)]
    [SerializeField] private int _colorCount = 6;

    [Header("Visual Fit & Sprites")]
    [SerializeField] private GridFillMode _fillMode = GridFillMode.StretchToFillBox;
    [Tooltip("Opaque content ratio of Match_Items 256x256 PNGs (223x256 solid cube inside transparent side margins).")]
    [SerializeField] private Vector2 _opaqueContentRatio = new Vector2(223f / 256f, 1f);
    [SerializeField] private Sprite[] _blockSprites = Array.Empty<Sprite>();

    [Header("2.5D Jenga Stacking")]
    [Tooltip("UpperRowsOnTop draws upper rows in front so bottom rims sit like Jenga shadows onto blocks below.")]
    [SerializeField] private RowStackOrder _rowStackOrder = RowStackOrder.UpperRowsOnTop;
    [Tooltip("Vertical overlap ratio between stacked rows (0.15 closes rounded corner gaps & stacks blocks like Jenga).")]
    [Range(0f, 0.25f)]
    [SerializeField] private float _verticalOverlap = 0.15f;
    [Tooltip("Horizontal overlap ratio between adjacent columns (0.06 closes 4-way rounded corner holes cleanly).")]
    [Range(0f, 0.15f)]
    [SerializeField] private float _horizontalOverlap = 0.06f;
    [SerializeField] private string _sortingLayerName = "Default";
    [SerializeField] private int _baseSortingOrder = 0;

    [Header("Testing Options")]
    [SerializeField] private bool _autoRebuildInPlayMode = true;
    [SerializeField] private bool _drawAreaGizmos = true;
    [SerializeField] private int _randomSeed = 0;

    private readonly List<SpriteRenderer> _spawnedTiles = new List<SpriteRenderer>();
    private Transform _container;
    private LayoutSignature _lastSignature;
    private bool _hasBuiltOnce;
    private int _sessionSeed;
    private bool _drivenExternally;

    public BoardSizingMode SizingMode
    {
        get => _sizingMode;
        set => _sizingMode = value;
    }

    public SpriteRenderer FrameRenderer => _frameRenderer;
    public Vector2 OpaqueContentRatio => _opaqueContentRatio;
    public Sprite[] BlockSprites => _blockSprites;
    public bool HasSpawnedTiles => _spawnedTiles.Count > 0 || transform.Find(ContainerName) != null;

    public struct ResolvedBoardGeometry
    {
        public Vector3 Center;
        public Vector3 Right;
        public Vector3 Up;
        public Quaternion Rotation;
        public float BlockW;
        public float BlockH;
        public float StepX;
        public float StepY;
        public float TotalW;
        public float TotalH;
        public RowStackOrder RowStackOrder;
        public string SortingLayerName;
        public int BaseSortingOrder;
    }

    public struct BoardMetrics
    {
        public bool HasValidArea;
        public float AreaWidthWorld;
        public float AreaHeightWorld;
        public float MaxAvailableWidthWorld;
        public float MaxAvailableHeightWorld;
        public float CellWidthWorld;
        public float CellHeightWorld;
        public float CellAspect;
        public float NativeBlockAspect;
        public float AspectDistortion;
        public bool HasOrthoCamera;
        public float CellWidthPx;
        public float CellHeightPx;
        public int MinRecommendedCols;
        public int MinRecommendedRows;
        public int MaxComfortableCols;
        public int MaxComfortableRows;
        public int MaxExtremeCols;
        public int MaxExtremeRows;
    }

    private struct LayoutSignature : IEquatable<LayoutSignature>
    {
        public BoardSizingMode SizingMode;
        public int Rows;
        public int Cols;
        public int ColorCount;
        public GridFillMode FillMode;
        public RowStackOrder RowStackOrder;
        public float VerticalOverlap;
        public float HorizontalOverlap;
        public float TargetBlockHeight;
        public bool ClampToViewport;
        public Vector2 MaxViewportFill;
        public float FrameRimScale;
        public float PaddingLeft;
        public float PaddingRight;
        public float PaddingTop;
        public float PaddingBottom;
        public Vector2 OpaqueRatio;
        public float AreaWidth;
        public float AreaHeight;
        public Vector3 AreaCenter;
        public int SortingOrder;
        public string SortingLayer;

        public bool Equals(LayoutSignature other)
        {
            return SizingMode == other.SizingMode
                && Rows == other.Rows
                && Cols == other.Cols
                && ColorCount == other.ColorCount
                && FillMode == other.FillMode
                && RowStackOrder == other.RowStackOrder
                && Mathf.Approximately(VerticalOverlap, other.VerticalOverlap)
                && Mathf.Approximately(HorizontalOverlap, other.HorizontalOverlap)
                && Mathf.Approximately(TargetBlockHeight, other.TargetBlockHeight)
                && ClampToViewport == other.ClampToViewport
                && MaxViewportFill == other.MaxViewportFill
                && Mathf.Approximately(FrameRimScale, other.FrameRimScale)
                && Mathf.Approximately(PaddingLeft, other.PaddingLeft)
                && Mathf.Approximately(PaddingRight, other.PaddingRight)
                && Mathf.Approximately(PaddingTop, other.PaddingTop)
                && Mathf.Approximately(PaddingBottom, other.PaddingBottom)
                && OpaqueRatio == other.OpaqueRatio
                && Mathf.Approximately(AreaWidth, other.AreaWidth)
                && Mathf.Approximately(AreaHeight, other.AreaHeight)
                && AreaCenter == other.AreaCenter
                && SortingOrder == other.SortingOrder
                && SortingLayer == other.SortingLayer;
        }
    }

    private void Awake()
    {
        ResolveAutoReferences();

        if (Application.isPlaying && _disableColliderInPlayMode && _playArea != null)
        {
            _playArea.enabled = false;
        }
    }

    private void Start()
    {
        if (Application.isPlaying && !_drivenExternally)
        {
            RebuildGrid(logSummary: true);
        }
    }

    private void Update()
    {
        if (!Application.isPlaying || !_autoRebuildInPlayMode || _drivenExternally)
        {
            return;
        }

        LayoutSignature current = CaptureSignature();
        if (!_hasBuiltOnce || !current.Equals(_lastSignature))
        {
            RebuildGrid(logSummary: false, rerollColors: false);
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        ResolveAutoReferences();
        LoadDefaultBlockSprites();
    }

    [ContextMenu("Load Default Block Sprites")]
    public void LoadDefaultBlockSprites()
    {
        var loaded = new List<Sprite>(DefaultSpritePaths.Length);
        foreach (string path in DefaultSpritePaths)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null)
            {
                loaded.Add(sprite);
            }
        }

        if (loaded.Count > 0)
        {
            _blockSprites = loaded.ToArray();
            EditorUtility.SetDirty(this);
        }
        else
        {
            Debug.LogWarning("[RandomBoardFiller] Could not find default sprites under Assets/Match_Items_internship/.", this);
        }
    }
#endif

    [ContextMenu("Rebuild Grid")]
    public void RebuildGridContext()
    {
        RebuildGrid(logSummary: true);
    }

    [ContextMenu("Clear Grid")]
    public void ClearGrid()
    {
        CollectExistingContainer();
        if (_container != null)
        {
            if (Application.isPlaying)
            {
                Destroy(_container.gameObject);
            }
            else
            {
                DestroyImmediate(_container.gameObject);
            }

            _container = null;
        }

        _spawnedTiles.Clear();
        _hasBuiltOnce = false;
    }

    /// <summary>
    /// Allows a gameplay BoardController to take ownership of the play area at runtime
    /// while reusing the exact configured frame-expansion and Jenga geometry settings.
    /// </summary>
    public void SetDrivenExternally(bool external)
    {
        _drivenExternally = external;
        if (external)
        {
            ClearGrid();
        }
    }

    public Vector2 ComputeEffectiveSpriteUnitSize(Sprite sprite)
    {
        return GetEffectiveSpriteUnitSize(sprite);
    }

    public bool ResolveGeometryForBoard(
        int rows,
        int cols,
        Sprite sampleSprite,
        bool applyFrameResize,
        out ResolvedBoardGeometry geometry)
    {
        ResolveAutoReferences();
        _rows = Mathf.Clamp(rows, 1, 30);
        _cols = Mathf.Clamp(cols, 1, 30);

        if (!TryResolveBoardGeometry(
            applyFrameResize,
            sampleSprite,
            out Vector3 center,
            out Vector3 right,
            out Vector3 up,
            out Quaternion rotation,
            out GridLayout layout,
            out _,
            out _))
        {
            geometry = default;
            return false;
        }

        geometry = new ResolvedBoardGeometry
        {
            Center = center,
            Right = right,
            Up = up,
            Rotation = rotation,
            BlockW = layout.BlockW,
            BlockH = layout.BlockH,
            StepX = layout.StepX,
            StepY = layout.StepY,
            TotalW = layout.TotalW,
            TotalH = layout.TotalH,
            RowStackOrder = _rowStackOrder,
            SortingLayerName = _sortingLayerName,
            BaseSortingOrder = _baseSortingOrder
        };
        return true;
    }

    /// <summary>
    /// Sets grid dimensions and rebuilds immediately.
    /// </summary>
    public void SetDimensionsAndRebuild(int rows, int cols, bool logSummary = true)
    {
        _rows = Mathf.Clamp(rows, 1, 30);
        _cols = Mathf.Clamp(cols, 1, 30);
        RebuildGrid(logSummary);
    }

    /// <summary>
    /// Toggles between FixedAreaFitBlocks and PreserveBlocksExpandFrame and rebuilds immediately.
    /// </summary>
    public void ToggleSizingModeAndRebuild()
    {
        _sizingMode = _sizingMode == BoardSizingMode.FixedAreaFitBlocks
            ? BoardSizingMode.PreserveBlocksExpandFrame
            : BoardSizingMode.FixedAreaFitBlocks;
        RebuildGrid(logSummary: true);
    }

    /// <summary>
    /// Fills the board randomly according to the selected SizingMode.
    /// </summary>
    public void RebuildGrid(bool logSummary = false, bool rerollColors = true)
    {
        ResolveAutoReferences();

        List<Sprite> palette = BuildActivePalette();
        if (palette.Count == 0)
        {
            Debug.LogWarning("[RandomBoardFiller] Block Sprites list is empty. Assign sprites or use ContextMenu > Load Default Block Sprites.", this);
            return;
        }

        if (!TryResolveBoardGeometry(
            applyFrameResize: true,
            palette[0],
            out Vector3 center,
            out Vector3 right,
            out Vector3 up,
            out Quaternion rotation,
            out GridLayout layout,
            out _,
            out _))
        {
            Debug.LogWarning("[RandomBoardFiller] Neither PlayArea BoxCollider2D nor FrameRenderer is configured.", this);
            return;
        }

        EnsureContainer();

        int totalCells = _rows * _cols;
        EnsureTilePoolSize(totalCells);

        Vector3 containerLossy = _container.lossyScale;
        float invScaleX = Mathf.Abs(containerLossy.x) > 1e-5f ? 1f / containerLossy.x : 1f;
        float invScaleY = Mathf.Abs(containerLossy.y) > 1e-5f ? 1f / containerLossy.y : 1f;

        float startOffsetX = -layout.TotalW * 0.5f + layout.BlockW * 0.5f;
        float startOffsetY = -layout.TotalH * 0.5f + layout.BlockH * 0.5f;

        if (rerollColors || _sessionSeed == 0)
        {
            _sessionSeed = Environment.TickCount ^ GetInstanceID();
        }

        int seed = _randomSeed != 0 ? _randomSeed : _sessionSeed;
        System.Random rng = new System.Random(seed);

        int idx = 0;
        for (int row = 0; row < _rows; row++)
        {
            float localOffsetY = startOffsetY + row * layout.StepY;
            int rowDepthRank = _rowStackOrder == RowStackOrder.UpperRowsOnTop
                ? row
                : (_rows - 1 - row);

            for (int col = 0; col < _cols; col++)
            {
                float localOffsetX = startOffsetX + col * layout.StepX;
                Sprite sprite = palette[rng.Next(palette.Count)];

                SpriteRenderer sr = _spawnedTiles[idx];
                sr.gameObject.SetActive(true);
                sr.name = $"Tile_R{row}_C{col}";
                sr.sprite = sprite;
                sr.sortingLayerName = _sortingLayerName;
                sr.sortingOrder = _baseSortingOrder + rowDepthRank * _cols + col;

                Vector3 worldPos = center + right * localOffsetX + up * localOffsetY;
                sr.transform.position = worldPos;
                sr.transform.rotation = rotation;

                Vector2 visibleSpriteSize = GetEffectiveSpriteUnitSize(sprite);
                float worldScaleX = layout.BlockW / Mathf.Max(0.001f, visibleSpriteSize.x);
                float worldScaleY = layout.BlockH / Mathf.Max(0.001f, visibleSpriteSize.y);

                sr.transform.localScale = new Vector3(
                    worldScaleX * invScaleX,
                    worldScaleY * invScaleY,
                    1f);

                idx++;
            }
        }

        _lastSignature = CaptureSignature();
        _hasBuiltOnce = true;

        if (logSummary)
        {
            BoardMetrics metrics = CalculateMetrics();
            LogMetricsReport(metrics);
        }
    }

    /// <summary>
    /// Computes real-world cell dimensions, aspect distortion, and recommended min/max grid limits.
    /// </summary>
    public BoardMetrics CalculateMetrics()
    {
        var metrics = new BoardMetrics();
        Sprite sampleSprite = GetFirstValidSprite();

        if (!TryResolveBoardGeometry(
            applyFrameResize: false,
            sampleSprite,
            out _,
            out _,
            out _,
            out _,
            out GridLayout layout,
            out float capacityW,
            out float capacityH))
        {
            return metrics;
        }

        metrics.HasValidArea = true;
        metrics.AreaWidthWorld = layout.TotalW;
        metrics.AreaHeightWorld = layout.TotalH;
        metrics.MaxAvailableWidthWorld = capacityW;
        metrics.MaxAvailableHeightWorld = capacityH;

        float nativeAspect = GetNativeOpaqueAspect(sampleSprite);
        metrics.NativeBlockAspect = nativeAspect;

        metrics.CellWidthWorld = layout.BlockW;
        metrics.CellHeightWorld = layout.BlockH;
        metrics.CellAspect = layout.BlockW / Mathf.Max(0.0001f, layout.BlockH);

        float ratioDiff = metrics.CellAspect / Mathf.Max(0.0001f, nativeAspect);
        metrics.AspectDistortion = ratioDiff >= 1f ? ratioDiff : 1f / ratioDiff;

        Camera cam = GetOrthoCamera();
        if (cam != null)
        {
            metrics.HasOrthoCamera = true;
            float pxPerUnit = ReferenceScreenHeightPx / (cam.orthographicSize * 2f);
            metrics.CellWidthPx = layout.BlockW * pxPerUnit;
            metrics.CellHeightPx = layout.BlockH * pxPerUnit;

            float areaPxW = capacityW * pxPerUnit;
            float areaPxH = capacityH * pxPerUnit;

            metrics.MinRecommendedCols = Mathf.Clamp(CountFitForTilePx(areaPxW, MinGridMaxTilePx, _horizontalOverlap), 4, 8);
            metrics.MinRecommendedRows = Mathf.Clamp(CountFitForTilePx(areaPxH, MinGridMaxTilePx, _verticalOverlap), 4, 8);

            metrics.MaxComfortableCols = Mathf.Max(metrics.MinRecommendedCols, CountFitForTilePx(areaPxW, ComfortableMinTilePx, _horizontalOverlap));
            metrics.MaxComfortableRows = Mathf.Max(metrics.MinRecommendedRows, CountFitForTilePx(areaPxH, ComfortableMinTilePx, _verticalOverlap));

            metrics.MaxExtremeCols = Mathf.Max(metrics.MaxComfortableCols, CountFitForTilePx(areaPxW, ExtremeMinTilePx, _horizontalOverlap));
            metrics.MaxExtremeRows = Mathf.Max(metrics.MaxComfortableRows, CountFitForTilePx(areaPxH, ExtremeMinTilePx, _verticalOverlap));
        }

        return metrics;
    }

    private struct GridLayout
    {
        public float BlockW;
        public float BlockH;
        public float StepX;
        public float StepY;
        public float TotalW;
        public float TotalH;
    }

    private static int CountFitForTilePx(float areaPx, float tilePx, float overlap)
    {
        float stepFactor = Mathf.Max(0.1f, 1f - overlap);
        float ratio = areaPx / Mathf.Max(1f, tilePx);
        return Mathf.FloorToInt(1f + (ratio - 1f) / stepFactor);
    }

    private void ResolveAutoReferences()
    {
        if (_playArea == null)
        {
            _playArea = GetComponent<BoxCollider2D>() ?? GetComponentInChildren<BoxCollider2D>();
        }

        if (_frameRenderer == null)
        {
            _frameRenderer = GetComponent<SpriteRenderer>() ?? GetComponentInParent<SpriteRenderer>();
        }

#if UNITY_EDITOR
        EnsureStandaloneNineSliceFrameSprite();
#endif
    }

#if UNITY_EDITOR
    private void EnsureStandaloneNineSliceFrameSprite()
    {
        if (_frameRenderer == null || _sizingMode != BoardSizingMode.PreserveBlocksExpandFrame)
        {
            return;
        }

        Sprite current = _frameRenderer.sprite;
        bool needsUpgrade = current == null
            || current.name == "popup elements_76"
            || (current.border.x / Mathf.Max(1f, current.pixelsPerUnit)) > 2.5f;

        if (!needsUpgrade)
        {
            return;
        }

        Sprite standalone = AssetDatabase.LoadAssetAtPath<Sprite>(CleanNineSliceFramePath);
        if (standalone != null && standalone != current)
        {
            _frameRenderer.sprite = standalone;
            EditorUtility.SetDirty(_frameRenderer);
        }
    }
#endif

    private Camera GetOrthoCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            cam = FindFirstObjectByType<Camera>();
        }

        return cam != null && cam.orthographic && cam.orthographicSize > 0.0001f ? cam : null;
    }

    private bool TryResolveBoardGeometry(
        bool applyFrameResize,
        Sprite sampleSprite,
        out Vector3 center,
        out Vector3 right,
        out Vector3 up,
        out Quaternion rotation,
        out GridLayout layout,
        out float capacityW,
        out float capacityH)
    {
        if (_sizingMode == BoardSizingMode.FixedAreaFitBlocks)
        {
            if (!TryGetPlayAreaWorldBasis(out center, out right, out up, out capacityW, out capacityH))
            {
                rotation = transform.rotation;
                layout = default;
                return false;
            }

            rotation = _playArea.transform.rotation;
            layout = ComputeFixedAreaGridLayout(capacityW, capacityH, sampleSprite);
            return true;
        }

        return TryResolveExpandedFrameGeometry(
            applyFrameResize,
            sampleSprite,
            out center,
            out right,
            out up,
            out rotation,
            out layout,
            out capacityW,
            out capacityH);
    }

    private bool TryResolveExpandedFrameGeometry(
        bool applyFrameResize,
        Sprite sampleSprite,
        out Vector3 center,
        out Vector3 right,
        out Vector3 up,
        out Quaternion rotation,
        out GridLayout layout,
        out float capacityW,
        out float capacityH)
    {
#if UNITY_EDITOR
        if (applyFrameResize)
        {
            EnsureStandaloneNineSliceFrameSprite();
        }
#endif

        bool hasFrame = _frameRenderer != null && _frameRenderer.sprite != null;
        if (!hasFrame && _playArea == null)
        {
            center = Vector3.zero;
            right = Vector3.right;
            up = Vector3.up;
            rotation = transform.rotation;
            layout = default;
            capacityW = 0f;
            capacityH = 0f;
            return false;
        }

        float rimScale = 1f;
        Vector4 borderUnits = Vector4.zero;
        if (hasFrame)
        {
            rimScale = _frameRimScale > 0.001f ? _frameRimScale : 1f;

            Sprite frameSprite = _frameRenderer.sprite;
            float framePpu = Mathf.Max(1f, frameSprite.pixelsPerUnit);
            borderUnits = frameSprite.border / framePpu;
        }

        float effLocalL = borderUnits.x + _paddingLeft / rimScale;
        float effLocalB = borderUnits.y + _paddingBottom / rimScale;
        float effLocalR = borderUnits.z + _paddingRight / rimScale;
        float effLocalT = borderUnits.w + _paddingTop / rimScale;

        float borderWorldW = (effLocalL + effLocalR) * rimScale;
        float borderWorldH = (effLocalB + effLocalT) * rimScale;

        layout = ComputeExpandedGridLayout(sampleSprite, borderWorldW, borderWorldH, out capacityW, out capacityH);

        if (hasFrame)
        {
            Transform ft = _frameRenderer.transform;
            float sizeX = Mathf.Max(borderUnits.x + borderUnits.z + 0.05f, (layout.TotalW / rimScale) + effLocalL + effLocalR);
            float sizeY = Mathf.Max(borderUnits.y + borderUnits.w + 0.05f, (layout.TotalH / rimScale) + effLocalB + effLocalT);

            if (applyFrameResize)
            {
                ft.localScale = new Vector3(rimScale, rimScale, 1f);
                _frameRenderer.drawMode = SpriteDrawMode.Sliced;
                _frameRenderer.size = new Vector2(sizeX, sizeY);
            }

            Vector2 normPivot = new Vector2(
                _frameRenderer.sprite.pivot.x / Mathf.Max(1f, _frameRenderer.sprite.rect.width),
                _frameRenderer.sprite.pivot.y / Mathf.Max(1f, _frameRenderer.sprite.rect.height));

            float cavityLocalX = 0.5f * ((1f - 2f * normPivot.x) * sizeX + (effLocalL - effLocalR));
            float cavityLocalY = 0.5f * ((1f - 2f * normPivot.y) * sizeY + (effLocalB - effLocalT));

            // Use world-scaled offset directly so Edit Mode preview matches even before Transform.localScale mutates.
            Vector3 scaledLocalOffset = new Vector3(cavityLocalX * rimScale, cavityLocalY * rimScale, 0f);
            center = ft.position + ft.rotation * scaledLocalOffset;
            right = ft.right;
            up = ft.up;
            rotation = ft.rotation;
        }
        else
        {
            Transform pt = _playArea.transform;
            center = pt.TransformPoint(_playArea.offset);
            right = pt.right;
            up = pt.up;
            rotation = pt.rotation;
        }

        if (applyFrameResize && _syncPlayAreaColliderToFrame && _playArea != null)
        {
            Transform pt = _playArea.transform;
            Vector3 localCenter = pt.InverseTransformPoint(center);
            _playArea.offset = new Vector2(localCenter.x, localCenter.y);

            Vector3 pLossy = pt.lossyScale;
            float sx = Mathf.Abs(pLossy.x) > 1e-5f ? Mathf.Abs(pLossy.x) : 1f;
            float sy = Mathf.Abs(pLossy.y) > 1e-5f ? Mathf.Abs(pLossy.y) : 1f;
            _playArea.size = new Vector2(layout.TotalW / sx, layout.TotalH / sy);
        }

        return true;
    }

    private GridLayout ComputeExpandedGridLayout(
        Sprite sampleSprite,
        float borderWorldW,
        float borderWorldH,
        out float maxCapacityW,
        out float maxCapacityH)
    {
        int rows = Mathf.Max(1, _rows);
        int cols = Mathf.Max(1, _cols);
        float ovX = Mathf.Clamp(_horizontalOverlap, 0f, 0.4f);
        float ovY = Mathf.Clamp(_verticalOverlap, 0f, 0.4f);

        float spanFactorX = (cols - 1) * (1f - ovX) + 1f;
        float spanFactorY = (rows - 1) * (1f - ovY) + 1f;

        float nativeAspect = GetNativeOpaqueAspect(sampleSprite);
        float blockH = Mathf.Max(0.1f, _targetBlockHeight);
        float blockW = blockH * nativeAspect;

        float rawTotalW = blockW * spanFactorX;
        float rawTotalH = blockH * spanFactorY;

        maxCapacityW = rawTotalW;
        maxCapacityH = rawTotalH;

        Camera cam = GetOrthoCamera();
        if (cam != null)
        {
            float camH = cam.orthographicSize * 2f;
            float camAspect = cam.aspect > 0.9f ? PortraitFallbackAspect : Mathf.Max(0.25f, cam.aspect);
            float camW = camH * camAspect;
            maxCapacityW = Mathf.Max(1f, camW * Mathf.Clamp(_maxViewportFill.x, 0.2f, 1f) - borderWorldW);
            maxCapacityH = Mathf.Max(1f, camH * Mathf.Clamp(_maxViewportFill.y, 0.2f, 1f) - borderWorldH);

            if (_clampToCameraViewport)
            {
                float clampScale = Mathf.Min(1f, Mathf.Min(
                    maxCapacityW / Mathf.Max(0.01f, rawTotalW),
                    maxCapacityH / Mathf.Max(0.01f, rawTotalH)));

                blockH *= clampScale;
                blockW *= clampScale;
            }
        }

        float stepX = blockW * (1f - ovX);
        float stepY = blockH * (1f - ovY);

        return new GridLayout
        {
            BlockW = blockW,
            BlockH = blockH,
            StepX = stepX,
            StepY = stepY,
            TotalW = (cols - 1) * stepX + blockW,
            TotalH = (rows - 1) * stepY + blockH
        };
    }

    private GridLayout ComputeFixedAreaGridLayout(float areaW, float areaH, Sprite sampleSprite)
    {
        int rows = Mathf.Max(1, _rows);
        int cols = Mathf.Max(1, _cols);
        float ovX = Mathf.Clamp(_horizontalOverlap, 0f, 0.4f);
        float ovY = Mathf.Clamp(_verticalOverlap, 0f, 0.4f);

        float spanFactorX = (cols - 1) * (1f - ovX) + 1f;
        float spanFactorY = (rows - 1) * (1f - ovY) + 1f;

        float blockW;
        float blockH;
        if (_fillMode == GridFillMode.StretchToFillBox)
        {
            blockW = areaW / spanFactorX;
            blockH = areaH / spanFactorY;
        }
        else
        {
            float targetAspect = _fillMode == GridFillMode.UniformSquare
                ? 1f
                : GetNativeOpaqueAspect(sampleSprite);

            float maxBlockHByHeight = areaH / spanFactorY;
            float maxBlockHByWidth = (areaW / spanFactorX) / Mathf.Max(0.01f, targetAspect);
            blockH = Mathf.Min(maxBlockHByHeight, maxBlockHByWidth);
            blockW = blockH * targetAspect;
        }

        float stepX = blockW * (1f - ovX);
        float stepY = blockH * (1f - ovY);

        return new GridLayout
        {
            BlockW = blockW,
            BlockH = blockH,
            StepX = stepX,
            StepY = stepY,
            TotalW = (cols - 1) * stepX + blockW,
            TotalH = (rows - 1) * stepY + blockH
        };
    }

    private float GetNativeOpaqueAspect(Sprite sampleSprite)
    {
        Vector2 unitSize = sampleSprite != null
            ? GetEffectiveSpriteUnitSize(sampleSprite)
            : new Vector2(2.23f, 2.56f);

        return unitSize.x / Mathf.Max(0.0001f, unitSize.y);
    }

    private Vector2 GetEffectiveSpriteUnitSize(Sprite sprite)
    {
        float ppu = Mathf.Max(1f, sprite.pixelsPerUnit);
        Vector2 rectWorldSize = sprite.rect.size / ppu;
        return new Vector2(
            rectWorldSize.x * Mathf.Clamp(_opaqueContentRatio.x, 0.1f, 2f),
            rectWorldSize.y * Mathf.Clamp(_opaqueContentRatio.y, 0.1f, 2f));
    }

    private bool TryGetPlayAreaWorldBasis(out Vector3 center, out Vector3 right, out Vector3 up, out float width, out float height)
    {
        if (_playArea == null)
        {
            _playArea = GetComponent<BoxCollider2D>();
        }

        if (_playArea == null)
        {
            center = Vector3.zero;
            right = Vector3.right;
            up = Vector3.up;
            width = 0f;
            height = 0f;
            return false;
        }

        Transform areaTransform = _playArea.transform;
        Vector3 lossy = areaTransform.lossyScale;
        width = Mathf.Abs(_playArea.size.x * lossy.x);
        height = Mathf.Abs(_playArea.size.y * lossy.y);
        center = areaTransform.TransformPoint(_playArea.offset);
        right = areaTransform.right;
        up = areaTransform.up;

        return width > 0.01f && height > 0.01f;
    }

    private List<Sprite> BuildActivePalette()
    {
        var palette = new List<Sprite>();
        if (_blockSprites == null)
        {
            return palette;
        }

        int limit = Mathf.Clamp(_colorCount, 1, _blockSprites.Length);
        for (int i = 0; i < _blockSprites.Length && palette.Count < limit; i++)
        {
            if (_blockSprites[i] != null)
            {
                palette.Add(_blockSprites[i]);
            }
        }

        return palette;
    }

    private Sprite GetFirstValidSprite()
    {
        if (_blockSprites == null)
        {
            return null;
        }

        for (int i = 0; i < _blockSprites.Length; i++)
        {
            if (_blockSprites[i] != null)
            {
                return _blockSprites[i];
            }
        }

        return null;
    }

    private void CollectExistingContainer()
    {
        if (_container != null)
        {
            return;
        }

        Transform existing = transform.Find(ContainerName);
        if (existing != null)
        {
            _container = existing;
            _spawnedTiles.Clear();
            for (int i = 0; i < _container.childCount; i++)
            {
                SpriteRenderer sr = _container.GetChild(i).GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    _spawnedTiles.Add(sr);
                }
            }
        }
    }

    private void EnsureContainer()
    {
        CollectExistingContainer();
        if (_container == null)
        {
            var go = new GameObject(ContainerName);
            go.transform.SetParent(transform, false);
            _container = go.transform;
        }
    }

    private void EnsureTilePoolSize(int neededCount)
    {
        _spawnedTiles.RemoveAll(t => t == null);

        while (_spawnedTiles.Count < neededCount)
        {
            var tileGo = new GameObject("Tile", typeof(SpriteRenderer));
            tileGo.transform.SetParent(_container, false);
            _spawnedTiles.Add(tileGo.GetComponent<SpriteRenderer>());
        }

        for (int i = neededCount; i < _spawnedTiles.Count; i++)
        {
            if (_spawnedTiles[i] != null)
            {
                _spawnedTiles[i].gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Samples currently computed cell height so PreserveBlocksExpandFrame matches current visual block size.
    /// </summary>
    public void SampleTargetBlockHeightFromPlayArea()
    {
        BoardMetrics current = CalculateMetrics();
        if (!current.HasValidArea || current.CellHeightWorld <= 0.01f)
        {
            return;
        }

        _targetBlockHeight = Mathf.Clamp(current.CellHeightWorld, 0.25f, 4f);
        if (_sizingMode == BoardSizingMode.PreserveBlocksExpandFrame)
        {
            RebuildGrid(logSummary: true);
        }
    }

    private LayoutSignature CaptureSignature()
    {
        Vector3 areaCenter = Vector3.zero;
        float areaW = 0f;
        float areaH = 0f;
        if (_playArea != null)
        {
            TryGetPlayAreaWorldBasis(out areaCenter, out _, out _, out areaW, out areaH);
        }

        return new LayoutSignature
        {
            SizingMode = _sizingMode,
            Rows = _rows,
            Cols = _cols,
            ColorCount = _colorCount,
            FillMode = _fillMode,
            RowStackOrder = _rowStackOrder,
            VerticalOverlap = _verticalOverlap,
            HorizontalOverlap = _horizontalOverlap,
            TargetBlockHeight = _targetBlockHeight,
            ClampToViewport = _clampToCameraViewport,
            MaxViewportFill = _maxViewportFill,
            FrameRimScale = _frameRimScale,
            PaddingLeft = _paddingLeft,
            PaddingRight = _paddingRight,
            PaddingTop = _paddingTop,
            PaddingBottom = _paddingBottom,
            OpaqueRatio = _opaqueContentRatio,
            AreaWidth = areaW,
            AreaHeight = areaH,
            AreaCenter = areaCenter,
            SortingOrder = _baseSortingOrder,
            SortingLayer = _sortingLayerName
        };
    }

    private void LogMetricsReport(BoardMetrics m)
    {
        if (!m.HasValidArea)
        {
            return;
        }

        string pxPart = m.HasOrthoCamera
            ? $" | 1080x1920 Hucre: {m.CellWidthPx:F0}x{m.CellHeightPx:F0} px | " +
              $"Min: ~{m.MinRecommendedRows}x{m.MinRecommendedCols} | " +
              $"Konforlu Max: ~{m.MaxComfortableRows}x{m.MaxComfortableCols} | " +
              $"Uc Max: ~{m.MaxExtremeRows}x{m.MaxExtremeCols}"
            : " | (Ortho Camera.main yok, piksel hesabi atlandi)";

        string distortionWarn = m.AspectDistortion > MaxAcceptableDistortion ? " [UYARI: Sekil Orani Fazla Bozuldu!]" : "";
        Debug.Log(
            $"[RandomBoardFiller:{_sizingMode}] Grid: {_rows}x{_cols} (Satir x Sutun) | Grid Alan: {m.AreaWidthWorld:F2}x{m.AreaHeightWorld:F2} u | " +
            $"Hucre: {m.CellWidthWorld:F2}x{m.CellHeightWorld:F2} u | Oran Bozulmasi: {m.AspectDistortion:F2}x{distortionWarn}{pxPart}",
            this);
    }

    private void OnDrawGizmos()
    {
        if (!_drawAreaGizmos)
        {
            return;
        }

        if (!TryResolveBoardGeometry(
            applyFrameResize: false,
            GetFirstValidSprite(),
            out Vector3 center,
            out Vector3 right,
            out Vector3 up,
            out _,
            out GridLayout layout,
            out _,
            out _))
        {
            return;
        }

        Vector3 halfRight = right * (layout.TotalW * 0.5f);
        Vector3 halfUp = up * (layout.TotalH * 0.5f);
        Vector3 bl = center - halfRight - halfUp;
        Vector3 br = center + halfRight - halfUp;
        Vector3 tr = center + halfRight + halfUp;
        Vector3 tl = center - halfRight + halfUp;

        Gizmos.color = new Color(0.2f, 0.95f, 0.4f, 0.85f);
        Gizmos.DrawLine(bl, br);
        Gizmos.DrawLine(br, tr);
        Gizmos.DrawLine(tr, tl);
        Gizmos.DrawLine(tl, bl);

        Gizmos.color = new Color(0.2f, 0.95f, 0.4f, 0.2f);
        float startX = -layout.TotalW * 0.5f + layout.BlockW * 0.5f;
        float startY = -layout.TotalH * 0.5f + layout.BlockH * 0.5f;
        Vector3 halfCellRight = right * (layout.BlockW * 0.5f);
        Vector3 halfCellUp = up * (layout.BlockH * 0.5f);

        for (int r = 0; r < _rows; r++)
        {
            float cy = startY + r * layout.StepY;
            for (int c = 0; c < _cols; c++)
            {
                float cx = startX + c * layout.StepX;
                Vector3 tileCenter = center + right * cx + up * cy;
                Vector3 p0 = tileCenter - halfCellRight - halfCellUp;
                Vector3 p1 = tileCenter + halfCellRight - halfCellUp;
                Vector3 p2 = tileCenter + halfCellRight + halfCellUp;
                Vector3 p3 = tileCenter - halfCellRight + halfCellUp;
                Gizmos.DrawLine(p0, p1);
                Gizmos.DrawLine(p1, p2);
                Gizmos.DrawLine(p2, p3);
                Gizmos.DrawLine(p3, p0);
            }
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(RandomBoardFiller))]
public class RandomBoardFillerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var filler = target as RandomBoardFiller;
        if (filler == null)
        {
            return;
        }

        EditorGUILayout.LabelField("Quick Controls & Stress Tests", EditorStyles.boldLabel);

        string toggleLabel = filler.SizingMode == RandomBoardFiller.BoardSizingMode.FixedAreaFitBlocks
            ? "Switch To: Expand Frame (Preserve Blocks)"
            : "Switch To: Fixed Frame (Fit Blocks)";

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(toggleLabel))
        {
            Undo.RecordObject(filler, "Toggle Board Sizing Mode");
            if (filler.FrameRenderer != null)
            {
                Undo.RecordObject(filler.FrameRenderer, "Resize Frame");
                Undo.RecordObject(filler.FrameRenderer.transform, "Scale Frame");
            }

            filler.ToggleSizingModeAndRebuild();
            EditorUtility.SetDirty(filler);
        }
        if (GUILayout.Button("Sample Block Size"))
        {
            Undo.RecordObject(filler, "Sample Target Block Height");
            filler.SampleTargetBlockHeightFromPlayArea();
            EditorUtility.SetDirty(filler);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Rebuild Random"))
        {
            filler.RebuildGrid(logSummary: true);
        }
        if (GUILayout.Button("Clear"))
        {
            filler.ClearGrid();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Test 6 x 6"))
        {
            RunPreset(filler, 6, 6, "Set 6x6 Grid");
        }
        if (GUILayout.Button("Test 6 x 8 (6C x 8R)"))
        {
            RunPreset(filler, 8, 6, "Set 6x8 Grid");
        }
        if (GUILayout.Button("Test 8 x 6 (8C x 6R)"))
        {
            RunPreset(filler, 6, 8, "Set 8x6 Grid");
        }
        if (GUILayout.Button("Test 12 x 15"))
        {
            RunPreset(filler, 15, 12, "Set 12x15 Grid");
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Reload Match_Items Default Sprites"))
        {
            Undo.RecordObject(filler, "Load Default Sprites");
            filler.LoadDefaultBlockSprites();
        }

        if (filler.SizingMode == RandomBoardFiller.BoardSizingMode.PreserveBlocksExpandFrame && filler.FrameRenderer == null)
        {
            EditorGUILayout.HelpBox(
                "PreserveBlocksExpandFrame modunda ahsap cercevenin de buyuyup kuculmesi icin Frame Renderer alanina BoardFrame SpriteRenderer'ini atayin.",
                MessageType.Info);
        }

        RandomBoardFiller.BoardMetrics m = filler.CalculateMetrics();
        if (!m.HasValidArea)
        {
            EditorGUILayout.HelpBox("PlayArea BoxCollider2D veya FrameRenderer bulunamadi.", MessageType.Warning);
        }
        else
        {
            string pxInfo = m.HasOrthoCamera
                ? $"1080x1920 Hucre Boyutu: {m.CellWidthPx:F0} x {m.CellHeightPx:F0} px\n" +
                  $"Onerilen Min (Cok Iri Oln): ~{m.MinRecommendedRows} x {m.MinRecommendedCols} (Satir x Sutun)\n" +
                  $"Konforlu Max (>= 90 px): ~{m.MaxComfortableRows} x {m.MaxComfortableCols} (Satir x Sutun)\n" +
                  $"Uc / Siki Max (>= 70 px): ~{m.MaxExtremeRows} x {m.MaxExtremeCols} (Satir x Sutun)"
                : "Main Ortho Camera bulunamadigi icin 1080x1920 piksel hesabi yapilamadi.";

            bool distorted = m.AspectDistortion > 1.33f;
            bool tooTiny = m.HasOrthoCamera && Mathf.Min(m.CellWidthPx, m.CellHeightPx) < 70f;
            bool tooBig = m.HasOrthoCamera && Mathf.Max(m.CellWidthPx, m.CellHeightPx) > 220f;

            MessageType msgType = (distorted || tooTiny || tooBig) ? MessageType.Warning : MessageType.Info;
            string summary =
                $"Mod: {filler.SizingMode}\n" +
                $"Grid Ic Alan: {m.AreaWidthWorld:F2} x {m.AreaHeightWorld:F2} unit (Max Kapasite: {m.MaxAvailableWidthWorld:F2} x {m.MaxAvailableHeightWorld:F2} u)\n" +
                $"Hucre Boyutu: {m.CellWidthWorld:F2} x {m.CellHeightWorld:F2} unit | Oran Bozulmasi: {m.AspectDistortion:F2}x\n" +
                pxInfo;

            EditorGUILayout.HelpBox(summary, msgType);
        }

        EditorGUILayout.Space(8);
        if (DrawDefaultInspector() && !Application.isPlaying && filler.HasSpawnedTiles)
        {
            filler.RebuildGrid(logSummary: false, rerollColors: false);
        }
    }

    private static void RunPreset(RandomBoardFiller filler, int rows, int cols, string undoLabel)
    {
        Undo.RecordObject(filler, undoLabel);
        if (filler.FrameRenderer != null)
        {
            Undo.RecordObject(filler.FrameRenderer, undoLabel);
            Undo.RecordObject(filler.FrameRenderer.transform, undoLabel);
        }

        filler.SetDimensionsAndRebuild(rows, cols, logSummary: true);
        EditorUtility.SetDirty(filler);
    }
}
#endif
