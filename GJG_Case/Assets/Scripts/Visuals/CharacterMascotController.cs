using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the TopBar character mascot with smooth alpha-crossfaded facial expressions
/// based on level progress and timer pace, with subtle grounded breathing motion.
/// </summary>
public class CharacterMascotController : MonoBehaviour
{
    public enum MascotMood
    {
        Normal,
        Blink,
        Focused,
        Happy,
        Worried,
        Panic
    }

    [Header("Target UI Image (Inside Character Frame)")]
    [SerializeField] private Image _portraitImage;

    [Header("Expression Sprites")]
    [SerializeField] private Sprite _normalSprite = null;
    [SerializeField] private Sprite _blinkSprite = null;
    [SerializeField] private Sprite _focusedSprite = null;
    [SerializeField] private Sprite _happySprite = null;
    [SerializeField] private Sprite _worriedSprite = null;
    [SerializeField] private Sprite _panicSprite = null;

    [Header("Gameplay Links")]
    [SerializeField] private LevelTimer _levelTimer;
    [SerializeField] private PowerUpManager _powerUpManager;
    [Tooltip("Optional MonoBehaviour implementing ILevelProgressProvider (auto-discovered if omitted).")]
    [SerializeField] private MonoBehaviour _progressProviderSource;

    [Header("Level Progress & Mood Thresholds")]
    [Range(0f, 1f)]
    [SerializeField] private float _levelProgress = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float _focusedProgressThreshold = 0.15f;
    [Range(0f, 1f)]
    [SerializeField] private float _happyProgressThreshold = 0.65f;
    [Range(0f, 1f)]
    [SerializeField] private float _levelCompleteThreshold = 0.999f;
    [Min(0f)]
    [SerializeField] private float _engageFocusedAfterSeconds = 5f;
    [Min(0f)]
    [SerializeField] private float _solvePartialPreviewStep = 0.25f;

    [Header("Time Pressure Thresholds")]
    [SerializeField] private bool _useTimePressureStages = true;
    [Min(0f)]
    [SerializeField] private float _worriedTimeThreshold = 22f;
    [Min(0f)]
    [SerializeField] private float _lowTimePanicThreshold = 10f;

    [Header("Smooth Transitions")]
    [Range(0.05f, 1f)]
    [SerializeField] private float _expressionFadeDuration = 0.34f;
    [Range(0.02f, 0.25f)]
    [SerializeField] private float _blinkFadeDuration = 0.06f;

    [Header("Subtle Breathing (Minimal Sway)")]
    [Range(0f, 0.03f)]
    [SerializeField] private float _breathAmplitude = 0.007f;
    [Range(0.5f, 4f)]
    [SerializeField] private float _breathSpeed = 1.55f;
    [Range(0f, 5f)]
    [SerializeField] private float _idleBobPixels = 0.6f;
    [Range(0f, 3f)]
    [SerializeField] private float _idleTiltDegrees = 0f;

    [Header("Blink Timing (Normal State Only)")]
    [SerializeField] private Vector2 _blinkIntervalRange = new Vector2(2.6f, 5.2f);
    [Min(0.05f)]
    [SerializeField] private float _blinkDuration = 0.14f;

    [Header("Optional Pointer Gaze")]
    [SerializeField] private bool _enablePointerLook = false;
    [Range(0f, 4f)]
    [SerializeField] private float _lookMaxTiltDegrees = 1.2f;
    [Range(0f, 8f)]
    [SerializeField] private float _lookMaxShiftPixels = 2.5f;

    private RectTransform _portraitRect;
    private Image _overlayImage;
    private ILevelProgressProvider _progressProvider;

    private Vector2 _baseAnchoredPos;
    private Vector3 _baseScale = Vector3.one;

    private float _blinkTimer;
    private float _nextBlinkCountdown;
    private bool _pendingDoubleBlink;

    private float _happyRemaining;
    private float _lookTargetNormalizedX;
    private float _currentLookNormalizedX;
    private float _lookIdleTimer;
    private Vector3 _lastMousePos;

    private MascotMood _currentMood = MascotMood.Normal;
    private MascotMood? _moodOverride;
    private Sprite _targetSprite;

    private bool _isFading;
    private float _fadeProgress;
    private float _activeFadeDuration = 0.34f;
    private float _settleImpulse;

    private Vector3 _smoothedScale = Vector3.one;
    private Vector2 _smoothedOffset;
    private float _smoothedTilt;

    public float LevelProgress => _levelProgress;
    public MascotMood CurrentMood => _currentMood;

    private void Awake()
    {
        ResolveReferences();

        if (_portraitRect != null)
        {
            _baseAnchoredPos = _portraitRect.anchoredPosition;
            _baseScale = _portraitRect.localScale;
            _smoothedScale = _baseScale;
            EnsureOverlayImage();
        }

        _nextBlinkCountdown = Random.Range(_blinkIntervalRange.x, _blinkIntervalRange.y);
        _lastMousePos = Input.mousePosition;
    }

    private void OnEnable()
    {
        if (_powerUpManager != null)
        {
            _powerUpManager.OnPowerUpUsed += HandlePowerUpUsed;
        }

        if (_progressProvider != null)
        {
            _progressProvider.OnProgressChanged += HandleExternalProgressChanged;
        }
    }

    private void Start()
    {
        SyncFromProgressProvider();
        _currentMood = ResolveActiveMood();
        Sprite initialSprite = GetSpriteForMood(_currentMood);
        ApplySpriteImmediate(initialSprite);
    }

    private void Update()
    {
        if (_portraitRect == null)
        {
            return;
        }

        float dt = Time.deltaTime;
        SyncFromProgressProvider();
        UpdateTimers(dt);
        UpdatePointerLook(dt);

        MascotMood nextMood = ResolveActiveMood();
        if (nextMood != _currentMood)
        {
            StartMoodTransition(_currentMood, nextMood);
            _currentMood = nextMood;
        }

        UpdateCrossfade(dt);
        ApplyMotionTransform(dt, _currentMood);
    }

    private void OnDisable()
    {
        if (_powerUpManager != null)
        {
            _powerUpManager.OnPowerUpUsed -= HandlePowerUpUsed;
        }

        if (_progressProvider != null)
        {
            _progressProvider.OnProgressChanged -= HandleExternalProgressChanged;
        }
    }

    /// <summary>
    /// Attaches a runtime goal/progress provider and immediately reflects its state.
    /// </summary>
    public void SetProgressProvider(ILevelProgressProvider provider)
    {
        if (_progressProvider != null)
        {
            _progressProvider.OnProgressChanged -= HandleExternalProgressChanged;
        }

        _progressProvider = provider;
        _progressProviderSource = provider as MonoBehaviour;

        if (isActiveAndEnabled && _progressProvider != null)
        {
            _progressProvider.OnProgressChanged += HandleExternalProgressChanged;
        }

        SyncFromProgressProvider();
    }

    /// <summary>
    /// Sets normalized level completion progress in [0, 1] and reacts smoothly.
    /// </summary>
    public void SetLevelProgress(float normalizedProgress)
    {
        float clamped = Mathf.Clamp01(normalizedProgress);
        float delta = clamped - _levelProgress;
        _levelProgress = clamped;

        if (delta >= 0.18f && clamped < _levelCompleteThreshold)
        {
            TriggerHappy(0.85f);
        }
    }

    /// <summary>
    /// Increments normalized level progress by delta in [0, 1].
    /// </summary>
    public void AddLevelProgress(float delta)
    {
        SetLevelProgress(_levelProgress + delta);
    }

    /// <summary>
    /// Convenience helper for objective counters (completed / total goals).
    /// </summary>
    public void SetObjectiveProgress(int completed, int total)
    {
        float ratio = total > 0 ? (float)completed / total : 0f;
        SetLevelProgress(ratio);
    }

    /// <summary>
    /// Marks the level as completed and keeps the mascot in celebratory Happy mood.
    /// </summary>
    public void NotifyLevelCompleted()
    {
        SetLevelProgress(1f);
    }

    /// <summary>
    /// Plays a brief Happy reaction expression for the specified duration.
    /// </summary>
    public void TriggerHappy(float duration = 0.9f)
    {
        _happyRemaining = Mathf.Max(_happyRemaining, Mathf.Max(0.2f, duration));
        _blinkTimer = 0f;
    }

    /// <summary>
    /// Forces a fixed mood or passes null to resume progress-driven mood tracking.
    /// </summary>
    public void SetMoodOverride(MascotMood? moodOverride)
    {
        _moodOverride = moodOverride;
    }

    private void HandleExternalProgressChanged(float normalizedProgress)
    {
        SetLevelProgress(normalizedProgress);
    }

    private void HandlePowerUpUsed(PowerUpType type, int remaining)
    {
        if (_progressProvider == null && type == PowerUpType.SolvePartial)
        {
            AddLevelProgress(_solvePartialPreviewStep);
        }

        if (type == PowerUpType.ExtraTime || type == PowerUpType.SolvePartial || type == PowerUpType.BreakLock)
        {
            TriggerHappy(0.85f);
        }
    }

    private void SyncFromProgressProvider()
    {
        if (_progressProvider == null)
        {
            return;
        }

        float target = _progressProvider.IsLevelCompleted ? 1f : Mathf.Clamp01(_progressProvider.NormalizedProgress);
        if (!Mathf.Approximately(target, _levelProgress))
        {
            _levelProgress = target;
        }
    }

    private void ResolveReferences()
    {
        if (_portraitImage == null)
        {
            _portraitImage = GetComponentInChildren<Image>(includeInactive: true);
        }

        if (_portraitImage != null)
        {
            _portraitRect = _portraitImage.rectTransform;
        }

        if (_levelTimer == null)
        {
            _levelTimer = FindFirstObjectByType<LevelTimer>();
        }

        if (_powerUpManager == null)
        {
            _powerUpManager = FindFirstObjectByType<PowerUpManager>();
        }

        if (_progressProviderSource is ILevelProgressProvider explicitProvider)
        {
            _progressProvider = explicitProvider;
        }
        else if (_progressProvider == null)
        {
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ILevelProgressProvider found)
                {
                    _progressProvider = found;
                    _progressProviderSource = behaviours[i];
                    break;
                }
            }
        }
    }

    private void EnsureOverlayImage()
    {
        Transform existing = _portraitRect.Find("CrossfadeOverlay");
        if (existing != null)
        {
            _overlayImage = existing.GetComponent<Image>();
        }

        if (_overlayImage == null)
        {
            GameObject overlayGo = new GameObject("CrossfadeOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            overlayGo.transform.SetParent(_portraitRect, worldPositionStays: false);

            RectTransform rect = overlayGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            _overlayImage = overlayGo.GetComponent<Image>();
        }

        _overlayImage.preserveAspect = _portraitImage.preserveAspect;
        _overlayImage.raycastTarget = false;
        _overlayImage.maskable = _portraitImage.maskable;
        _overlayImage.material = _portraitImage.material;
        SetImageAlpha(_overlayImage, 0f);
        _overlayImage.enabled = false;
    }

    private void UpdateTimers(float dt)
    {
        if (_happyRemaining > 0f)
        {
            _happyRemaining = Mathf.Max(0f, _happyRemaining - dt);
        }

        if (_settleImpulse > 0f)
        {
            _settleImpulse = Mathf.Max(0f, _settleImpulse * Mathf.Exp(-dt * 10f));
            if (_settleImpulse < 0.002f)
            {
                _settleImpulse = 0f;
            }
        }

        if (_blinkTimer > 0f)
        {
            _blinkTimer = Mathf.Max(0f, _blinkTimer - dt);
            return;
        }

        // Only blink while resting in calm Normal expression so mouth shapes stay consistent.
        if (_currentMood != MascotMood.Normal || _isFading)
        {
            return;
        }

        _nextBlinkCountdown -= dt;
        if (_nextBlinkCountdown <= 0f)
        {
            _blinkTimer = _blinkDuration;
            if (!_pendingDoubleBlink && Random.value < 0.18f)
            {
                _pendingDoubleBlink = true;
                _nextBlinkCountdown = 0.18f;
            }
            else
            {
                _pendingDoubleBlink = false;
                _nextBlinkCountdown = Random.Range(_blinkIntervalRange.x, _blinkIntervalRange.y);
            }
        }
    }

    private void UpdatePointerLook(float dt)
    {
        if (!_enablePointerLook)
        {
            _currentLookNormalizedX = Mathf.Lerp(_currentLookNormalizedX, 0f, dt * 6f);
            return;
        }

        Vector3 mousePos = Input.mousePosition;
        bool pointerActive = Input.GetMouseButton(0) || (mousePos - _lastMousePos).sqrMagnitude > 4f;
        _lastMousePos = mousePos;

        if (pointerActive && Screen.width > 0)
        {
            _lookTargetNormalizedX = Mathf.Clamp((mousePos.x / Screen.width - 0.5f) * 2f, -1f, 1f);
            _lookIdleTimer = 1.4f;
        }
        else
        {
            _lookIdleTimer = Mathf.Max(0f, _lookIdleTimer - dt);
            if (_lookIdleTimer <= 0f)
            {
                _lookTargetNormalizedX = 0f;
            }
        }

        _currentLookNormalizedX = Mathf.Lerp(_currentLookNormalizedX, _lookTargetNormalizedX, dt * 5f);
    }

    private MascotMood ResolveActiveMood()
    {
        if (_moodOverride.HasValue)
        {
            return _moodOverride.Value;
        }

        if (_levelProgress >= _levelCompleteThreshold)
        {
            return MascotMood.Happy;
        }

        if (_happyRemaining > 0f && _happySprite != null)
        {
            return MascotMood.Happy;
        }

        if (_useTimePressureStages && _levelTimer != null)
        {
            bool isCritical = _levelTimer.LimitMode == LevelLimitType.Moves
                ? _levelTimer.IsCriticalPressure
                : (_levelTimer.IsExpired || _levelTimer.RemainingSeconds <= _lowTimePanicThreshold);

            if (isCritical && _levelProgress < 0.90f && _panicSprite != null)
            {
                return MascotMood.Panic;
            }

            float elapsedRatio = 1f - _levelTimer.RemainingNormalized;
            bool behindPace = elapsedRatio > 0.45f && (_levelProgress + 0.25f) < elapsedRatio;
            bool isWorried = _levelTimer.LimitMode == LevelLimitType.Moves
                ? (_levelTimer.IsModeratePressure || behindPace)
                : (_levelTimer.RemainingSeconds <= _worriedTimeThreshold || behindPace);

            if (isWorried && _levelProgress < 0.80f && _worriedSprite != null)
            {
                return MascotMood.Worried;
            }
        }

        if (_levelProgress >= _happyProgressThreshold && _happySprite != null)
        {
            return MascotMood.Happy;
        }

        bool timerEngaged = _levelTimer != null && (_levelTimer.LimitMode == LevelLimitType.Moves
            ? _levelTimer.ElapsedSeconds >= 1f
            : _levelTimer.ElapsedSeconds >= _engageFocusedAfterSeconds);

        bool engagedInLevel = _levelProgress >= _focusedProgressThreshold || timerEngaged;

        if (engagedInLevel && _focusedSprite != null)
        {
            return MascotMood.Focused;
        }

        if (_blinkTimer > 0f && _blinkSprite != null)
        {
            return MascotMood.Blink;
        }

        return MascotMood.Normal;
    }

    private Sprite GetSpriteForMood(MascotMood mood)
    {
        return mood switch
        {
            MascotMood.Blink => _blinkSprite != null ? _blinkSprite : _normalSprite,
            MascotMood.Focused => _focusedSprite != null ? _focusedSprite : _normalSprite,
            MascotMood.Happy => _happySprite != null ? _happySprite : _normalSprite,
            MascotMood.Worried => _worriedSprite != null ? _worriedSprite : (_panicSprite != null ? _panicSprite : _normalSprite),
            MascotMood.Panic => _panicSprite != null ? _panicSprite : (_worriedSprite != null ? _worriedSprite : _normalSprite),
            _ => _normalSprite
        };
    }

    private void StartMoodTransition(MascotMood fromMood, MascotMood toMood)
    {
        Sprite nextSprite = GetSpriteForMood(toMood);
        if (nextSprite == null || nextSprite == _targetSprite)
        {
            return;
        }

        bool isBlinkTransition = fromMood == MascotMood.Blink || toMood == MascotMood.Blink;
        float duration = isBlinkTransition ? _blinkFadeDuration : _expressionFadeDuration;

        if (!isBlinkTransition)
        {
            _settleImpulse = 1f;
        }

        BeginSpriteCrossfade(nextSprite, duration);
    }

    private void ApplySpriteImmediate(Sprite sprite)
    {
        if (_portraitImage == null || sprite == null)
        {
            return;
        }

        _portraitImage.sprite = sprite;
        _portraitImage.color = Color.white;
        _targetSprite = sprite;
        _isFading = false;
        _fadeProgress = 0f;

        if (_overlayImage != null)
        {
            SetImageAlpha(_overlayImage, 0f);
            _overlayImage.enabled = false;
        }
    }

    private void BeginSpriteCrossfade(Sprite nextSprite, float duration)
    {
        if (_portraitImage == null || _overlayImage == null)
        {
            ApplySpriteImmediate(nextSprite);
            return;
        }

        if (_portraitImage.sprite == null || duration <= 0.005f)
        {
            ApplySpriteImmediate(nextSprite);
            return;
        }

        if (_isFading && _overlayImage.sprite != null)
        {
            // Reverse cleanly back to Base sprite if returning mid-fade.
            if (nextSprite == _portraitImage.sprite)
            {
                _portraitImage.sprite = _overlayImage.sprite;
                _overlayImage.sprite = nextSprite;
                _fadeProgress = 1f - Mathf.Clamp01(_fadeProgress);
                _targetSprite = nextSprite;
                _activeFadeDuration = duration;
                SetImageAlpha(_overlayImage, Mathf.SmoothStep(0f, 1f, _fadeProgress));
                return;
            }

            // Promote dominant layer to avoid visual popping on rapid chained transitions.
            if (_fadeProgress >= 0.5f)
            {
                _portraitImage.sprite = _overlayImage.sprite;
            }
        }

        _overlayImage.sprite = nextSprite;
        _overlayImage.color = new Color(1f, 1f, 1f, 0f);
        _overlayImage.enabled = true;
        _targetSprite = nextSprite;
        _activeFadeDuration = duration;
        _fadeProgress = 0f;
        _isFading = true;
    }

    private void UpdateCrossfade(float dt)
    {
        if (!_isFading || _overlayImage == null || _portraitImage == null)
        {
            return;
        }

        _fadeProgress += dt / Mathf.Max(0.01f, _activeFadeDuration);
        if (_fadeProgress >= 1f)
        {
            _portraitImage.sprite = _overlayImage.sprite;
            SetImageAlpha(_overlayImage, 0f);
            _overlayImage.enabled = false;
            _isFading = false;
            _fadeProgress = 0f;
            return;
        }

        float smoothAlpha = Mathf.SmoothStep(0f, 1f, _fadeProgress);
        SetImageAlpha(_overlayImage, smoothAlpha);
    }

    private void ApplyMotionTransform(float dt, MascotMood mood)
    {
        float t = Time.time;
        float speedMult = mood == MascotMood.Panic ? 1.35f : (mood == MascotMood.Worried ? 1.15f : 1f);
        float ampMult = mood == MascotMood.Panic ? 1.15f : 1f;

        float breathWave = Mathf.Sin(t * _breathSpeed * speedMult);
        float amp = _breathAmplitude * ampMult;

        float settleScaleY = _settleImpulse * 0.018f;
        float settleScaleX = -_settleImpulse * 0.008f;

        Vector3 targetScale = new Vector3(
            _baseScale.x * (1f - breathWave * (amp * 0.4f) + settleScaleX),
            _baseScale.y * (1f + breathWave * amp + settleScaleY),
            _baseScale.z);

        float bobY = (Mathf.Sin(t * _breathSpeed * speedMult - 0.5f) * 0.5f + 0.5f) * _idleBobPixels;
        float lookShiftX = _enablePointerLook ? _currentLookNormalizedX * _lookMaxShiftPixels : 0f;
        Vector2 targetOffset = new Vector2(lookShiftX, bobY + _settleImpulse * 1.2f);

        float idleTilt = _idleTiltDegrees > 0f ? Mathf.Sin(t * 1.1f) * _idleTiltDegrees : 0f;
        float lookTilt = _enablePointerLook ? -_currentLookNormalizedX * _lookMaxTiltDegrees : 0f;
        float targetTilt = idleTilt + lookTilt;

        float smoothFactor = 1f - Mathf.Exp(-dt * 12f);
        _smoothedScale = Vector3.Lerp(_smoothedScale, targetScale, smoothFactor);
        _smoothedOffset = Vector2.Lerp(_smoothedOffset, targetOffset, smoothFactor);
        _smoothedTilt = Mathf.LerpAngle(_smoothedTilt, targetTilt, smoothFactor);

        _portraitRect.localScale = _smoothedScale;
        _portraitRect.anchoredPosition = _baseAnchoredPos + _smoothedOffset;
        _portraitRect.localRotation = Quaternion.Euler(0f, 0f, _smoothedTilt);
    }

    private static void SetImageAlpha(Image image, float alpha)
    {
        Color c = image.color;
        c.r = 1f;
        c.g = 1f;
        c.b = 1f;
        c.a = alpha;
        image.color = c;
    }
}
