using System.Collections.Generic;
using UnityEngine;
using Internal.Core;
using TMPro;

public class BoardController : MonoBehaviour
{
    [Header("Score")]
    [SerializeField] private int _currentScore = 0;
    [SerializeField] private TextMeshProUGUI _scoreText;
    [Header("Board Config")]
    [SerializeField] private int _rows = 10; // M
    [SerializeField] private int _cols = 8;  // N
    [SerializeField] private int _colorCount = 6; // K
    [SerializeField] private float _cellSize = 1.0f;
    [Header("Visuals")]
    [SerializeField] private Block _blockPrefab;
    [SerializeField] private Transform _boardContainer;
    [SerializeField] private SpriteRenderer _boardBackground; // The yellow frame
    [SerializeField] private float _backgroundPadding = 0.5f; // Extra space around blocks
    
    // Config for icons
    // We will load these from Resources or assign in inspector
    // For now simple list
    [System.Serializable]
    public struct ColorSprites
    {
        public Sprite Default;
        public Sprite IconA;
        public Sprite IconB;
        public Sprite IconC;
    }
    
    [SerializeField] private List<ColorSprites> _blockSprites; 

    [Header("Icon Thresholds")]
    [SerializeField] private int _thresholdA = 4;
    [SerializeField] private int _thresholdB = 7;
    [SerializeField] private int _thresholdC = 9; 

    // Internal State
    private ObjectPool<Block> _blockPool;
    private Block[,] _grid; // Visual grid
    private List<Block> _searchBuffer = new List<Block>(100); // Reusable buffer
    private List<Block> _shuffleBuffer = new List<Block>(100); // Reusable buffer for shuffle
    // We might need a separate logical grid if we want pure data separation, 
    // but for this scale, Block component holding data is acceptable if optimized.
    // Optimization: 'Block' is a MonoBehaviour.
   
    private void Start()
    {
        InitializeBoard();
    }

    private void InitializeBoard()
    {
        InitializeBoard(_rows, _cols, _colorCount, _thresholdA, _thresholdB, _thresholdC);
    }

    public void InitializeBoard(int rows, int cols, int colorCount, int thresholdA, int thresholdB, int thresholdC)
    {
        _rows = rows;
        _cols = cols;
        _colorCount = colorCount;
        _thresholdA = thresholdA;
        _thresholdB = thresholdB;
        _thresholdC = thresholdC;

        if (_blockSprites == null || _blockSprites.Count < _colorCount)
        {
            Debug.LogError("Not enough sprites configured!");
            return;
        }
        
        // Initial pool size guess
        if (_blockPool == null)
        {
            _blockPool = new ObjectPool<Block>(_blockPrefab, _rows * _cols, _boardContainer);
        }

        _grid = new Block[_rows, _cols];
        GenerateGrid();
        UpdateAllIcons();

        if (_genericParticle != null && _particlePool == null)
        {
            _particlePool = new ObjectPool<ParticleSystem>(_genericParticle, 20, _boardContainer);
        }
    }
    
    public void RestartBoard(int rows, int cols, int colorCount, int thresholdA, int thresholdB, int thresholdC)
    {
        // Clear existing board
        if (_grid != null)
        {
            for (int y = 0; y < _grid.GetLength(0); y++)
            {
                for (int x = 0; x < _grid.GetLength(1); x++)
                {
                    if (_grid[y, x] != null)
                    {
                        _blockPool.Return(_grid[y, x]);
                        _grid[y, x] = null;
                    }
                }
            }
        }
        
        InitializeBoard(rows, cols, colorCount, thresholdA, thresholdB, thresholdC);
    }

    private void GenerateGrid()
    {
        // 1. Resize and Position Background Frame
        if (_boardBackground != null)
        {
            float totalWidth = (_cols * _cellSize) + _backgroundPadding;
            float totalHeight = (_rows * _cellSize) + _backgroundPadding;
            
            // Should be Sliced or Simple. If Sliced, we use size. 
            // If Simple, we might need localScale (assuming sprite is 1x1 unit).
            // Let's try attempting to set size if Sliced, else Scale.
            
            if (_boardBackground.drawMode == SpriteDrawMode.Sliced || _boardBackground.drawMode == SpriteDrawMode.Tiled)
            {
                _boardBackground.size = new Vector2(totalWidth, totalHeight);
            }
            else
            {
                // Fallback for simple sprites (assuming 100 PPU and 100x100px original size = 1 unit)
                // Better approach: Set scale directly
                 _boardBackground.transform.localScale = new Vector3(totalWidth, totalHeight, 1);
            }
            
            _boardBackground.transform.position = Vector3.forward * 1; // Put it behind blocks (Z=1) if blocks are Z=0
        }

        // 2. Calculate board offset to center it (Blocks)
        float width = _cols * _cellSize;
        float height = _rows * _cellSize;
        Vector3 origin = new Vector3(-width / 2 + _cellSize / 2, -height / 2 + _cellSize / 2, 0);

        for (int x = 0; x < _cols; x++)
        {
            for (int y = 0; y < _rows; y++)
            {
                CreateBlockAt(x, y, origin);
            }
        }
        
        AdjustCamera();
    }

    [Header("Camera Settings")]
    [SerializeField] private float _topPadding = 2.0f; // Space for UI at top
    [SerializeField] private float _bottomPadding = 2.0f; // Space for UI at bottom
    [SerializeField] private float _sidePadding = 0.5f; // Margin on sides

    private void AdjustCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float boardWidth = _cols * _cellSize;
        float boardHeight = _rows * _cellSize;
        
        // Calculate total target area including UI padding
        // logical height = boardHeight + topPadding + bottomPadding
        float totalHeight = boardHeight + _topPadding + _bottomPadding;
        float totalWidth = boardWidth + (_sidePadding * 2);
        
        float screenRatio = (float)Screen.width / Screen.height;
        float targetRatio = totalWidth / totalHeight;
        
        float orthoSize = 0f;

        if (screenRatio >= targetRatio)
        {
            // Fit Height
            orthoSize = totalHeight / 2f;
        }
        else
        {
            // Fit Width
            float differenceInSize = targetRatio / screenRatio;
            orthoSize = totalHeight / 2f * differenceInSize;
        }
        
        cam.orthographicSize = orthoSize;

        // Center camera not on 0,0, but on the visual center of the "Playable Area" relative to the padded full area
        // Board center is at 0,0.
        // We want the whole (Board + TopPad + BotPad) to be centered on screen.
        // The midpoint of that area is: (TopPad - BotPad) / 2
        // Example: If TopPad=5, BotPad=0. Center should be +2.5 so board (0) is lower.
        
        float verticalOffset = (_bottomPadding - _topPadding) / 2f;
        cam.transform.position = new Vector3(0, verticalOffset, -10);
    }

    private void UpdateAllIcons()
    {
        // We need to avoid re-calculating for same group multiple times.
        // Use a visited set.
        bool[,] visited = new bool[_rows, _cols];
        
        for (int y = 0; y < _rows; y++)
        {
            for (int x = 0; x < _cols; x++)
            {
                if (!visited[y, x] && _grid[y, x] != null)
                {
                    Internal.Logic.MatchFinder.FindConnectedGroup(_grid[y, x], _grid, _rows, _cols, _searchBuffer);
                    int size = _searchBuffer.Count;
                    
                    foreach (var block in _searchBuffer)
                    {
                        visited[block.Row, block.Col] = true;
                        UpdateBlockIcon(block, size);
                    }
                }
            }
        }
    }

    private void UpdateBlockIcon(Block block, int groupSize)
    {
        ColorSprites sprites = _blockSprites[block.ColorId];
        Sprite newSprite = sprites.Default;

        if (groupSize >= _thresholdC) newSprite = sprites.IconC;
        else if (groupSize >= _thresholdB) newSprite = sprites.IconB;
        else if (groupSize >= _thresholdA) newSprite = sprites.IconA;

        block.UpdateVisual(newSprite);
    }

    private void CreateBlockAt(int x, int y, Vector3 origin)
    {
        int colorId = Random.Range(0, _colorCount);
        Block block = _blockPool.Get();
        
        Vector3 pos = origin + new Vector3(x * _cellSize, y * _cellSize, 0);
        block.transform.position = pos;
        
        block.Init(y, x, colorId, _blockSprites[colorId].Default);
        _grid[y, x] = block;
    }
    
    // Stub for input handling
    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            HandleClick();
        }
    }

    private void HandleClick()
    {
        // Robust input for both Perspective (3D) and Orthographic (2D) cameras
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Plane boardPlane = new Plane(Vector3.forward, Vector3.zero); // Plane at Z=0 facing -Z (back) -> Unity 2D default is XY plane
        
        float distance;
        Vector3 mouseWorldPos;

        if (boardPlane.Raycast(ray, out distance))
        {
            mouseWorldPos = ray.GetPoint(distance);
        }
        else
        {
            // Fallback (mostly for pure 2D setups where ray might be parallel if malformed)
             mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        }
        
        // Convert world pos to grid index
        // Grid origin is calculated in GenerateGrid. 
        // We need to store origin or recalculate it.
        // Let's store board parameters properly.
        float width = _cols * _cellSize;
        float height = _rows * _cellSize;
        Vector3 origin = new Vector3(-width / 2 + _cellSize / 2, -height / 2 + _cellSize / 2, 0);

        // Inverse of pos = origin + new Vector3(x * _cellSize, y * _cellSize, 0);
        // pos - origin = (x * cell, y * cell)
        // (pos - origin).x / cell = x
        
        float localX = mouseWorldPos.x - (origin.x - _cellSize / 2); // Adjust for center pivot of cell
        float localY = mouseWorldPos.y - (origin.y - _cellSize / 2);

        int gridX = Mathf.FloorToInt(localX / _cellSize);
        int gridY = Mathf.FloorToInt(localY / _cellSize);

        if (gridX >= 0 && gridX < _cols && gridY >= 0 && gridY < _rows)
        {
            Block clickedBlock = _grid[gridY, gridX];
            if (clickedBlock != null)
            {
                Internal.Logic.MatchFinder.FindConnectedGroup(clickedBlock, _grid, _rows, _cols, _searchBuffer);
                if (_searchBuffer.Count >= 2)
                {
                    Debug.Log($"Found match of size {_searchBuffer.Count}");
                    RemoveBlocks(_searchBuffer);
                }
            }
        }
    }

    [Header("Effects")]
    [SerializeField] private ParticleSystem _genericParticle; // Simple version: one particle system with changing color or multiple prefabs
    // For optimization: Use one particle system and emit with color? Or pool of particle systems?
    // Let's assume a simple pool of ParticleSystems or just one One-Shot particle prefab.
    
    private ObjectPool<ParticleSystem> _particlePool;

    private void RemoveBlocks(List<Block> blocks)
    {
         int count = blocks.Count;
         int scoreToAdd;

        if (count >= _thresholdC)      scoreToAdd = count * 50; 
        else if (count >= _thresholdB) scoreToAdd = count * 30;
        else if (count >= _thresholdA) scoreToAdd = count * 20;
        else                           scoreToAdd = count * 10;

        _currentScore += scoreToAdd;
        _scoreText.text = $"Score: {_currentScore}";
         Debug.Log($"Patlayan Blok: {count}, Kazanılan Puan: {scoreToAdd}, Toplam: {_currentScore}");

        foreach (var block in blocks)
        {
            // Spawn Particle
            if (_particlePool != null)
            {
                ParticleSystem p = _particlePool.Get();
                p.transform.position = block.transform.position;
                
                // Set color if possible
                var main = p.main;
                // Assuming we can get color from sprite or config. 
                // For now, let's just emit.
                p.Play();
                StartCoroutine(ReturnParticle(p, 1.0f));
            }
            
            _grid[block.Row, block.Col] = null;
            _blockPool.Return(block);
        }
        
        // Trigger collapse and refill
        // TODO: Coroutine for animation
        StartCoroutine(CollapseAndRefillRoutine());
    }
    
    private System.Collections.IEnumerator ReturnParticle(ParticleSystem p, float delay)
    {
        yield return new WaitForSeconds(delay);
        _particlePool.Return(p);
    }

    private System.Collections.IEnumerator CollapseAndRefillRoutine()
    {
        // 1. Collapse (Gravity)
        for (int x = 0; x < _cols; x++)
        {
            int writeY = 0;
            for (int y = 0; y < _rows; y++)
            {
                if (_grid[y, x] != null)
                {
                    if (y != writeY)
                    {
                        // Move block down
                        _grid[writeY, x] = _grid[y, x];
                        _grid[y, x] = null;
                        
                        // Update block internal state
                        _grid[writeY, x].UpdatePosition(writeY, x, GetWorldPosition(x, writeY));
                    }
                    writeY++;
                }
            }
        }
        
        yield return new WaitForSeconds(0.2f); // Animation delay placeholder

        // 2. Refill
        for (int x = 0; x < _cols; x++)
        {
            for (int y = 0; y < _rows; y++)
            {
                if (_grid[y, x] == null)
                {
                    // Create new block
                     CreateBlockAt(x, y, GetWorldPosition(x, y) + Vector3.up * _rows * _cellSize); // Spawn above
                     // Animate falling to GetWorldPosition(x,y)
                     // For now instant snap in CreateBlockAt (need to refactor CreateBlockAt to separate pos)
                     // Re-setting target pos:
                     Block b = _grid[y, x];
                     b.UpdatePosition(y, x, GetWorldPosition(x, y));
                }
            }
        }
        
        // Update Icons
        UpdateAllIcons();
        
        // Check for deadlock after refill
        if (Internal.Logic.ShuffleLogic.IsDeadlocked(_grid, _rows, _cols))
        {
             Debug.Log("Deadlock detected! Shuffling...");
             yield return new WaitForSeconds(0.5f);
             
             // Collect all blocks
             _shuffleBuffer.Clear();
             foreach (var b in _grid) if(b!=null) _shuffleBuffer.Add(b);
             
             Internal.Logic.ShuffleLogic.ShuffleBoard(_grid, _rows, _cols, _shuffleBuffer);
             
             // Update positions visually
             foreach (var b in _shuffleBuffer)
             {
                 b.UpdatePosition(b.Row, b.Col, GetWorldPosition(b.Col, b.Row));
             }
             UpdateAllIcons();
        }
    }

    private Vector3 GetWorldPosition(int x, int y)
    {
        float width = _cols * _cellSize;
        float height = _rows * _cellSize;
        Vector3 origin = new Vector3(-width / 2 + _cellSize / 2, -height / 2 + _cellSize / 2, 0);
        return origin + new Vector3(x * _cellSize, y * _cellSize, 0);
    }
}
