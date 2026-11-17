using UnityEngine;

/// <summary>
/// Pooled 2.5D visual representation for a single board occupant (block, booster, or obstacle).
/// Handles smooth gravity falls, lock chain overlays, crack darkening, and pop/hit impulses.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class TileView : MonoBehaviour
{
    private SpriteRenderer _mainRenderer;
    private SpriteRenderer _lockOverlayRenderer;
    private SpriteRenderer _crackTintOverlay;

    private Vector3 _baseLocalScale = Vector3.one;
    private Vector3 _targetWorldPos;
    private float _fallVelocity;
    private bool _isFalling;
    private float _popScaleTimer;
    private float _popScaleDuration = 0.18f;
    private float _popAmplitude = 0.16f;
    private float _shakeTimer;
    private Color _baseTint = Color.white;

    private const float GravityAccel = 74f;
    private const float MaxFallSpeed = 34f;

    private void Awake()
    {
        EnsureRenderers();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
        {
            return;
        }

        if (_isFalling)
        {
            _fallVelocity = Mathf.Min(MaxFallSpeed, _fallVelocity + GravityAccel * dt);
            Vector3 pos = transform.position;
            pos = Vector3.MoveTowards(pos, _targetWorldPos, _fallVelocity * dt);
            transform.position = pos;

            if ((pos - _targetWorldPos).sqrMagnitude <= 0.00001f)
            {
                transform.position = _targetWorldPos;
                _isFalling = false;
                _fallVelocity = 0f;
                _popScaleTimer = 0.10f; // subtle landing squash
            }
        }

        UpdatePunchScaleAndShake(dt);
    }

    public void ConfigureVisual(
        Sprite mainSprite,
        Vector3 worldScale,
        Quaternion rotation,
        string sortingLayerName,
        int baseOrder,
        bool isLocked,
        Sprite lockSprite,
        int lockDamageStage,
        bool isCrackedCrate)
    {
        EnsureRenderers();

        _mainRenderer.sprite = mainSprite;
        _mainRenderer.sortingLayerName = sortingLayerName;
        _mainRenderer.sortingOrder = baseOrder;

        _baseTint = isCrackedCrate ? new Color(0.84f, 0.72f, 0.62f, 1f) : Color.white;
        _mainRenderer.color = _baseTint;

        _baseLocalScale = worldScale;
        transform.rotation = rotation;
        if (_popScaleTimer <= 0f)
        {
            transform.localScale = worldScale;
        }

        if (_lockOverlayRenderer != null)
        {
            _lockOverlayRenderer.enabled = isLocked && lockSprite != null;
            if (_lockOverlayRenderer.enabled)
            {
                _lockOverlayRenderer.sprite = lockSprite;
                _lockOverlayRenderer.sortingLayerName = sortingLayerName;
                _lockOverlayRenderer.sortingOrder = baseOrder + 1;

                // Visual progressive wear for 3-hit chains (0: intact, 1: dented/cracked, 2: heavily cracked amber)
                int clampedLockStage = Mathf.Clamp(lockDamageStage, 0, 2);
                if (clampedLockStage == 0)
                {
                    _lockOverlayRenderer.color = Color.white;
                    _lockOverlayRenderer.transform.localRotation = Quaternion.identity;
                    _lockOverlayRenderer.transform.localScale = Vector3.one;
                }
                else if (clampedLockStage == 1)
                {
                    _lockOverlayRenderer.color = new Color(0.92f, 0.84f, 0.78f, 0.94f);
                    _lockOverlayRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, 3.5f);
                    _lockOverlayRenderer.transform.localScale = Vector3.one * 0.98f;
                }
                else
                {
                    _lockOverlayRenderer.color = new Color(1.00f, 0.64f, 0.46f, 0.84f);
                    _lockOverlayRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, -6.5f);
                    _lockOverlayRenderer.transform.localScale = Vector3.one * 0.93f;
                }
            }
            else
            {
                _lockOverlayRenderer.color = Color.white;
                _lockOverlayRenderer.transform.localRotation = Quaternion.identity;
                _lockOverlayRenderer.transform.localScale = Vector3.one;
            }
        }

        if (_crackTintOverlay != null)
        {
            bool showLockCrack = isLocked && lockDamageStage > 0;
            _crackTintOverlay.enabled = isCrackedCrate || showLockCrack;
            if (_crackTintOverlay.enabled)
            {
                _crackTintOverlay.sprite = (showLockCrack && lockDamageStage >= 2)
                    ? GetOrCreateHeavyCrackSprite()
                    : GetOrCreateCrackSprite();
                _crackTintOverlay.color = showLockCrack && lockDamageStage >= 2
                    ? new Color(1f, 0.90f, 0.82f, 1f)
                    : Color.white;
                _crackTintOverlay.sortingLayerName = sortingLayerName;
                _crackTintOverlay.sortingOrder = baseOrder + 2;
            }
        }
    }

    public void SnapToPosition(Vector3 worldPos)
    {
        _targetWorldPos = worldPos;
        _isFalling = false;
        _fallVelocity = 0f;
        transform.position = worldPos;
    }

    public void FallToPosition(Vector3 worldPos, float initialSpeed = 2f)
    {
        _targetWorldPos = worldPos;
        _isFalling = (transform.position - worldPos).sqrMagnitude > 0.0001f;
        if (_isFalling)
        {
            _fallVelocity = Mathf.Max(_fallVelocity, initialSpeed);
        }
    }

    public bool IsMoving => _isFalling;

    public void PlaySpawnPop(float strength = 1f)
    {
        _popScaleDuration = 0.18f * Mathf.Clamp(strength, 0.5f, 2.2f);
        _popAmplitude = 0.16f * Mathf.Clamp(strength, 0.75f, 1.8f);
        _popScaleTimer = _popScaleDuration;
    }

    public void BoostSortingOrder(int extraOrder)
    {
        EnsureRenderers();
        _mainRenderer.sortingOrder += extraOrder;
        if (_lockOverlayRenderer != null) _lockOverlayRenderer.sortingOrder += extraOrder;
        if (_crackTintOverlay != null) _crackTintOverlay.sortingOrder += extraOrder;
    }

    public void PlayHitShake()
    {
        _shakeTimer = 0.18f;
    }

    public void ResetState()
    {
        _isFalling = false;
        _fallVelocity = 0f;
        _popScaleTimer = 0f;
        _shakeTimer = 0f;
        transform.localScale = _baseLocalScale;
    }

    private void UpdatePunchScaleAndShake(float dt)
    {
        Vector3 scale = _baseLocalScale;
        if (_popScaleTimer > 0f && _popScaleDuration > 0.001f)
        {
            _popScaleTimer = Mathf.Max(0f, _popScaleTimer - dt);
            float n = _popScaleTimer / _popScaleDuration;
            float pulse = Mathf.Sin(n * Mathf.PI) * _popAmplitude;
            scale = new Vector3(_baseLocalScale.x * (1f + pulse), _baseLocalScale.y * (1f - pulse * 0.35f), _baseLocalScale.z);
        }

        transform.localScale = scale;

        if (_shakeTimer > 0f)
        {
            _shakeTimer = Mathf.Max(0f, _shakeTimer - dt);
            float mag = (_shakeTimer / 0.18f) * 0.07f;
            float ox = Mathf.Sin(Time.time * 55f) * mag;
            if (!_isFalling)
            {
                transform.position = _targetWorldPos + new Vector3(ox, 0f, 0f);
                if (_shakeTimer <= 0f)
                {
                    transform.position = _targetWorldPos;
                }
            }
        }
    }

    private void EnsureRenderers()
    {
        if (_mainRenderer == null)
        {
            _mainRenderer = GetComponent<SpriteRenderer>();
        }

        _mainRenderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

        if (_lockOverlayRenderer == null)
        {
            Transform lockChild = transform.Find("LockOverlay");
            if (lockChild == null)
            {
                var go = new GameObject("LockOverlay", typeof(SpriteRenderer));
                go.transform.SetParent(transform, false);
                lockChild = go.transform;
            }

            _lockOverlayRenderer = lockChild.GetComponent<SpriteRenderer>();
            _lockOverlayRenderer.enabled = false;
        }

        _lockOverlayRenderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

        if (_crackTintOverlay == null)
        {
            Transform crackChild = transform.Find("CrackOverlay");
            if (crackChild == null)
            {
                var go = new GameObject("CrackOverlay", typeof(SpriteRenderer));
                go.transform.SetParent(transform, false);
                crackChild = go.transform;
            }

            _crackTintOverlay = crackChild.GetComponent<SpriteRenderer>();
            _crackTintOverlay.sprite = GetOrCreateCrackSprite();
            _crackTintOverlay.enabled = false;
        }

        _crackTintOverlay.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
    }

    private static Sprite _cachedCrackSprite;
    private static Sprite _cachedHeavyCrackSprite;

    private static Sprite GetOrCreateCrackSprite()
    {
        if (_cachedCrackSprite != null)
        {
            return _cachedCrackSprite;
        }

        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color32[size * size];
        Color32 crackDark = new Color32(45, 22, 10, 235);
        DrawLine(pixels, size, 16, 48, 30, 34, crackDark, 2);
        DrawLine(pixels, size, 30, 34, 24, 22, crackDark, 2);
        DrawLine(pixels, size, 24, 22, 42, 12, crackDark, 2);
        DrawLine(pixels, size, 30, 34, 46, 38, crackDark, 1);

        tex.SetPixels32(pixels);
        tex.Apply();
        _cachedCrackSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 25f);
        return _cachedCrackSprite;
    }

    private static Sprite GetOrCreateHeavyCrackSprite()
    {
        if (_cachedHeavyCrackSprite != null)
        {
            return _cachedHeavyCrackSprite;
        }

        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color32[size * size];
        Color32 crackDark = new Color32(36, 16, 8, 248);
        Color32 emberEdge = new Color32(255, 168, 72, 175);
        // Primary jagged diagonal crack + opposing heavy fracture lines
        DrawLine(pixels, size, 12, 52, 32, 32, emberEdge, 3);
        DrawLine(pixels, size, 32, 32, 50, 12, emberEdge, 3);
        DrawLine(pixels, size, 48, 50, 32, 32, emberEdge, 2);
        DrawLine(pixels, size, 32, 32, 14, 16, emberEdge, 2);

        DrawLine(pixels, size, 12, 52, 32, 32, crackDark, 2);
        DrawLine(pixels, size, 32, 32, 24, 22, crackDark, 2);
        DrawLine(pixels, size, 24, 22, 50, 12, crackDark, 2);
        DrawLine(pixels, size, 48, 50, 32, 32, crackDark, 2);
        DrawLine(pixels, size, 32, 32, 14, 16, crackDark, 2);
        DrawLine(pixels, size, 28, 48, 36, 34, crackDark, 1);
        DrawLine(pixels, size, 32, 32, 52, 36, crackDark, 1);

        tex.SetPixels32(pixels);
        tex.Apply();
        _cachedHeavyCrackSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 25f);
        return _cachedHeavyCrackSprite;
    }

    private static void DrawLine(Color32[] pixels, int size, int x0, int y0, int x1, int y1, Color32 color, int thickness)
    {
        int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)) * 2;
        if (steps <= 0)
        {
            return;
        }

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            int cx = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
            int cy = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
            for (int dy = -thickness; dy <= thickness; dy++)
            {
                for (int dx = -thickness; dx <= thickness; dx++)
                {
                    if (dx * dx + dy * dy > thickness * thickness)
                    {
                        continue;
                    }

                    int px = cx + dx;
                    int py = cy + dy;
                    if (px >= 0 && px < size && py >= 0 && py < size)
                    {
                        pixels[py * size + px] = color;
                    }
                }
            }
        }
    }
}
