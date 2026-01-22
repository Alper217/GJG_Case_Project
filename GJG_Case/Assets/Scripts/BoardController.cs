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
    
    [SerializeField] private Vector3 _boardOffset;
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
        public Color ParticleColor;
    }
    
    [SerializeField] private List<ColorSprites> _blockSprites; 

    [Header("Icon Thresholds")]
    [SerializeField] private int _thresholdA = 4;
    [SerializeField] private int _thresholdB = 7;
    [SerializeField] private int _thresholdC = 9; 

    [Header("Score Settings")]
    [SerializeField] private int _scorePerBlockBase = 10; // < A
    [SerializeField] private int _scorePerBlockA = 20;    // >= A
    [SerializeField] private int _scorePerBlockB = 30;    // >= B
    [SerializeField] private int _scorePerBlockC = 50;    // >= C 

    [Header("Combo System")]
    [SerializeField] private UnityEngine.UI.Slider _comboSlider;
    [SerializeField] private UnityEngine.UI.Image _comboFillImage; // Assign the "Fill" image of the slider
    [SerializeField] private CanvasGroup _comboCanvasGroup; // Assign a CanvasGroup to the Combo UI Panel
    [SerializeField] private TextMeshProUGUI _comboText;
    
    [Header("Combo Settings")]
    [SerializeField] private float _maxComboTime = 3.0f;
    [SerializeField] private int _comboActivationThreshold = 3; // How many blasts before combo starts
    [SerializeField] private Color _colorStage1 = Color.green;  // 1x - 1.5x
    [SerializeField] private Color _colorStage2 = Color.yellow; // 1.5x - 2.5x
    [SerializeField] private Color _colorStage3 = Color.red;    // 2.5x - 3.0x

    [Header("Juice")]
    [SerializeField] private Internal.Visuals.FloatingText _floatingTextPrefab;
    [SerializeField] private int _shakeThreshold = 5;
    [SerializeField] private float _shakeDuration = 0.2f;
    [SerializeField] private float _shakeMagnitude = 0.2f;
    private Internal.Visuals.CameraShake _cameraShake;

    [Header("Goals System")]
    [SerializeField] private Internal.Visuals.GoalItemUI _goalItemPrefab;
    [SerializeField] private Transform _goalsContainer; // Parent object for UI items (Horizontal Layout Group)
    [SerializeField] private int _goalsToGenerate = 2; // How many different color goals?
    [SerializeField] private int _minGoalTarget = 10;
    [SerializeField] private int _maxGoalTarget = 25;
    
    private List<Internal.Visuals.GoalItemUI> _activeGoals = new List<Internal.Visuals.GoalItemUI>();

    private float _currentComboTime;
    private int _comboCount; // Raw count of blasts in sequence
    private bool _isComboActive = false;

    [Header("Game Loop")]
    [SerializeField] private float _levelDuration = 60.0f;
    [SerializeField] private TextMeshProUGUI _timerText;
    [SerializeField] private GameObject _endLevelPanel; // Reuse for Win/Lose for now
    [SerializeField] private TextMeshProUGUI _endLevelText;

    private float _currentTime;
    private bool _isGameActive = false;

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
        
        // Auto-add text prefab if missing for testing (optional, but good for safety)
        if (Camera.main != null)
        {
             _cameraShake = Camera.main.GetComponent<Internal.Visuals.CameraShake>();
             if (_cameraShake == null) _cameraShake = Camera.main.gameObject.AddComponent<Internal.Visuals.CameraShake>();
        }
    }

    private void Update()
    {
        // Input
        if (_isGameActive && Input.GetMouseButtonDown(0))
        {
            HandleClick();
        }

        if (_isGameActive)
        {
            _currentTime -= Time.deltaTime;
            UpdateTimerUI();

            if (_currentTime <= 0)
            {
                GameOver(false);
            }
        }

        // Combo Logic
        if (_comboCount > 0)
        {
            _currentComboTime -= Time.deltaTime;
            
            if (_comboSlider != null)
            {
                _comboSlider.value = _currentComboTime / _maxComboTime;
            }

            if (_currentComboTime <= 0)
            {
                ResetCombo();
            }
        }

        // Combo UI Animation (Fade In/Out)
        if (_comboCanvasGroup != null)
        {
            float targetAlpha = _isComboActive ? 1.0f : 0.0f;
            // Smooth fade
            _comboCanvasGroup.alpha = Mathf.Lerp(_comboCanvasGroup.alpha, targetAlpha, Time.deltaTime * 5f);
        }
    }

    private void ResetCombo()
    {
        _comboCount = 0;
        _currentComboTime = 0;
        _isComboActive = false;
        // Don't maintain multiplier here, it resets.
    }

    private void IncrementCombo()
    {
        _comboCount++;
        _currentComboTime = _maxComboTime;

        // Check Activation
        if (_comboCount >= _comboActivationThreshold)
        {
            _isComboActive = true;
        }
        
        // Update Visuals if Active
        if (_isComboActive)
        {
            UpdateComboVisuals();
        }
    }

    private void UpdateComboVisuals()
    {
        float multiplier = GetComboMultiplier();

        if (_comboText != null) 
        {
            _comboText.text = $"x{multiplier:F1}"; // Format 1.2
            _comboText.transform.localScale = Vector3.one * 1.5f;
        }

        if (_comboFillImage != null)
        {
            // Smooth Gradient
            // Range 1.0 -> 2.0 : Stage1 -> Stage2
            // Range 2.0 -> 3.0 : Stage2 -> Stage3
            
            Color finalColor;
            if (multiplier <= 2.0f)
            {
                 // returns 0.0 at 1.0, 1.0 at 2.0
                 float t = Mathf.InverseLerp(1.0f, 2.0f, multiplier);
                 finalColor = Color.Lerp(_colorStage1, _colorStage2, t);
            }
            else
            {
                 // returns 0.0 at 2.0, 1.0 at 3.0
                 float t = Mathf.InverseLerp(2.0f, 3.0f, multiplier);
                 finalColor = Color.Lerp(_colorStage2, _colorStage3, t);
            }
            
            _comboFillImage.color = finalColor;
        }
    }

    private float GetComboMultiplier()
    {
        if (!_isComboActive) return 1.0f;

        // Count how many blasts since activation
        int effectiveCount = _comboCount - _comboActivationThreshold + 1;
        
        // Formula: 1.0 + (count * 0.1) clamped to 3.0
        float val = 1.0f + (effectiveCount * 0.1f);
        return Mathf.Min(val, 3.0f);
    }
    private void InitializeBoard()
    {
        if (_comboCanvasGroup != null) _comboCanvasGroup.alpha = 0f;
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
        
        GenerateGoals();
        
        // Start Game
        _currentTime = _levelDuration;
        _isGameActive = true;
        if (_endLevelPanel != null) _endLevelPanel.SetActive(false);
    }
    
    private void GenerateGoals()
    {
        // Clear existing UI
        if (_activeGoals != null)
        {
            foreach (var goal in _activeGoals)
            {
                 if (goal != null) Destroy(goal.gameObject);
            }
            _activeGoals.Clear();
        }
        else
        {
            _activeGoals = new List<Internal.Visuals.GoalItemUI>();
        }

        if (_goalItemPrefab == null || _goalsContainer == null) return;

        // Pick random unique colors
        List<int> availableColors = new List<int>();
        for (int i = 0; i < _colorCount; i++) availableColors.Add(i);
        
        int countToSpawn = Mathf.Min(_goalsToGenerate, _colorCount);

        for (int i = 0; i < countToSpawn; i++)
        {
            if (availableColors.Count == 0) break;
            
            int rndIndex = Random.Range(0, availableColors.Count);
            int colorId = availableColors[rndIndex];
            availableColors.RemoveAt(rndIndex);

            int targetAmount = Random.Range(_minGoalTarget, _maxGoalTarget + 1);

            var goalUI = Instantiate(_goalItemPrefab, _goalsContainer);
            // Use the "IconC" or "Default" sprite for the goal icon
            Sprite icon = _blockSprites[colorId].Default; 
            goalUI.Init(colorId, targetAmount, icon);
            
            _activeGoals.Add(goalUI);
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
            
            _boardBackground.transform.position = (Vector3.forward * 1) + _boardOffset; // Put it behind blocks (Z=1) if blocks are Z=0
        }

        // 2. Calculate board offset to center it (Blocks)
        float width = _cols * _cellSize;
        float height = _rows * _cellSize;
        Vector3 origin = new Vector3(-width / 2 + _cellSize / 2, -height / 2 + _cellSize / 2, 0) + _boardOffset;

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
        
        if (_cameraShake != null) _cameraShake.SetOriginalPos(cam.transform.localPosition);
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
    // Input is now handled in the main Update loop above with Combo logic
    // private void Update() { ... } 

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
        Vector3 origin = new Vector3(-width / 2 + _cellSize / 2, -height / 2 + _cellSize / 2, 0) + _boardOffset;

        // Inverse of pos = origin + new Vector3(x * _cellSize, y * _cellSize, 0);
        // pos - origin = (x * cell, y * cell)
        // (pos - origin).x / cell = x
        
        float localX = mouseWorldPos.x - (origin.x - _cellSize / 2); // Adjust for center pivot of cell
        float localY = mouseWorldPos.y - (origin.y - _cellSize / 2);

        int gridX = Mathf.FloorToInt(localX / _cellSize);
        int gridY = Mathf.FloorToInt(localY / _cellSize);

        if (gridX >= 0 && gridX < _cols && gridY >= 0 && gridY < _rows)
        {
            // Update Face Tracker
            if (_faceTracker != null)
            {
                _faceTracker.SetLookTarget(mouseWorldPos);
            }

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
    [SerializeField] private Internal.Visuals.FaceTracker _faceTracker;
    [SerializeField] private ParticleSystem _genericParticle; // Simple version: one particle system with changing color or multiple prefabs
    // For optimization: Use one particle system and emit with color? Or pool of particle systems?
    // Let's assume a simple pool of ParticleSystems or just one One-Shot particle prefab.
    
    private ObjectPool<ParticleSystem> _particlePool;

    private void RemoveBlocks(List<Block> blocks)
    {
         int count = blocks.Count;
         int scoreToAdd;

        // Score Calculation
        // Using configured values per block count
        if (count >= _thresholdC)      scoreToAdd = count * _scorePerBlockC; 
        else if (count >= _thresholdB) scoreToAdd = count * _scorePerBlockB;
        else if (count >= _thresholdA) scoreToAdd = count * _scorePerBlockA;
        else                           scoreToAdd = count * _scorePerBlockBase;

        // Apply Combo Multiplier
        float multiplier = GetComboMultiplier();
        int finalScore = Mathf.RoundToInt(scoreToAdd * multiplier);

        _currentScore += finalScore;
        _scoreText.text = $"Score: {_currentScore}";
        Debug.Log($"Base: {scoreToAdd}, Mult: {multiplier:F1}, Added: {finalScore}, Total: {_currentScore}");

        // Juice: Screen Shake
        if (count >= _shakeThreshold && _cameraShake != null)
        {
            _cameraShake.Shake(_shakeDuration, _shakeMagnitude);
        }
        
        // Goals Upgrade
        if (_activeGoals != null && blocks.Count > 0)
        {
            int colorId = blocks[0].ColorId;
            bool allComplete = true;
            
            foreach (var goal in _activeGoals)
            {
                if (goal.ColorId == colorId)
                {
                    goal.DecreaseCount(blocks.Count);
                }
                
                if (!goal.IsCompleted()) allComplete = false;
            }
            
            if (allComplete)
            {
                GameOver(true);
            }
        }

        // Juice: Floating Text
        if (_floatingTextPrefab != null && blocks.Count > 0)
        {
            // Spawn at the center of the first block (or average position)
            Vector3 spawnPos = blocks[0].transform.position + Vector3.up * 0.5f;
            var ft = Instantiate(_floatingTextPrefab, spawnPos, Quaternion.identity);
            ft.Init($"+{finalScore}", _blockSprites[blocks[0].ColorId].ParticleColor);
        }

        IncrementCombo();

        foreach (var block in blocks)
        {
            // Spawn Particle
            if (_particlePool != null)
            {
                ParticleSystem p = _particlePool.Get();
                p.transform.position = block.transform.position;
                
                // Set color
                var main = p.main;
                main.startColor = _blockSprites[block.ColorId].ParticleColor;

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
        
        yield return new WaitForSeconds(0.4f); // Animation delay placeholder

        // 2. Refill
        float width = _cols * _cellSize;
        float height = _rows * _cellSize;
        Vector3 boardOrigin = new Vector3(-width / 2 + _cellSize / 2, -height / 2 + _cellSize / 2, 0) + _boardOffset;

        for (int x = 0; x < _cols; x++)
        {
            for (int y = 0; y < _rows; y++)
            {
                if (_grid[y, x] == null)
                {
                    // Create new block directly at target position (Pop-up animation from Init)
                    CreateBlockAt(x, y, boardOrigin);
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
        Vector3 origin = new Vector3(-width / 2 + _cellSize / 2, -height / 2 + _cellSize / 2, 0) + _boardOffset;
        return origin + new Vector3(x * _cellSize, y * _cellSize, 0);
    }

    private void UpdateTimerUI()
    {
        if (_timerText != null)
        {
            // Fix: Clamp time to 0 to avoid negative numbers
            float displayTime = Mathf.Max(0, _currentTime);

            // Format 00:00
            int minutes = Mathf.FloorToInt(displayTime / 60F);
            int seconds = Mathf.FloorToInt(displayTime % 60F);
            _timerText.text = $"{minutes:00}:{seconds:00}";
            
            // Last 10 seconds warning
            if (displayTime <= 10f && displayTime > 0) 
            {
                _timerText.color = Color.red;
                
                // Pulsate Effect: Sine wave between 1.0 and 1.2 scale
                float scale = 1.0f + Mathf.PingPong(Time.time * 1f, 0.3f);
                _timerText.transform.localScale = Vector3.one * scale;
            }
            else 
            {
                _timerText.color = Color.white;
                _timerText.transform.localScale = Vector3.one;
            }
        }
    }

    private void GameOver(bool isWin)
    {
        _isGameActive = false;
        Debug.Log($"Game Over: {(isWin ? "WIN" : "LOSE")}");

        if (_endLevelPanel != null)
        {
            _endLevelPanel.SetActive(true);
            if (_endLevelText != null)
            {
                _endLevelText.text = isWin ? "LEVEL COMPLETED!" : "TIME'S UP!";
            }
        }
        
        // Disable input or show particles, etc.
    }
}
