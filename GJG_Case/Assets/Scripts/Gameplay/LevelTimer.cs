using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Manages the level countdown timer UI inside TopBar/Counter and fires events
/// on second ticks, bonus time additions, and expiration.
/// </summary>
public class LevelTimer : MonoBehaviour
{
    [Header("UI Reference")]
    [Tooltip("TMP_Text component on the child Count object inside Counter.")]
    [SerializeField] private TMP_Text _countText;

    [Header("Timer Settings")]
    [SerializeField] private LevelLimitType _limitMode = LevelLimitType.Time;
    [Min(1f)]
    [SerializeField] private float _initialDurationSeconds = 60f;
    [Min(1)]
    [SerializeField] private int _initialMoves = 20;
    [Min(1)]
    [SerializeField] private int _lowMovesThreshold = 4;
    [SerializeField] private bool _autoStart = true;
    [SerializeField] private bool _useUnscaledTime = false;

    [Header("Visual Feedback")]
    [Min(0f)]
    [SerializeField] private float _lowTimeThresholdSeconds = 10f;
    [SerializeField] private Color _lowTimeColor = new Color(1f, 0.35f, 0.3f, 1f);
    [SerializeField] private Color _bonusFlashColor = new Color(0.45f, 1f, 0.55f, 1f);
    [SerializeField] private bool _pulseWhenLow = true;

    private float _remainingSeconds;
    private int _remainingMoves;
    private int _lastDisplayedValue = -1;
    private bool _isRunning;
    private bool _isPaused;
    private bool _isExpired;
    private bool _configuredExternally;

    private Color _defaultColor = Color.white;
    private Vector3 _baseScale = Vector3.one;
    private float _bonusFlashRemaining;
    private Coroutine _punchCoroutine;

    public LevelLimitType LimitMode => _limitMode;
    public TMP_Text CountText
    {
        get
        {
            ResolveReferences();
            return _countText;
        }
    }
    public float RemainingSeconds => _remainingSeconds;
    public int RemainingMoves => _remainingMoves;
    public int InitialMoves => _initialMoves;
    public float InitialDurationSeconds => _initialDurationSeconds;
    public float ElapsedSeconds => _limitMode == LevelLimitType.Time
        ? Mathf.Max(0f, _initialDurationSeconds - _remainingSeconds)
        : Mathf.Max(0, _initialMoves - _remainingMoves);

    public float RemainingNormalized
    {
        get
        {
            if (_limitMode == LevelLimitType.Moves)
            {
                return _initialMoves > 0 ? Mathf.Clamp01((float)_remainingMoves / _initialMoves) : 0f;
            }

            return _initialDurationSeconds > 0f ? Mathf.Clamp01(_remainingSeconds / _initialDurationSeconds) : 0f;
        }
    }

    public bool IsCriticalPressure => _isExpired || (_limitMode == LevelLimitType.Moves
        ? _remainingMoves <= _lowMovesThreshold
        : _remainingSeconds <= _lowTimeThresholdSeconds);

    public bool IsModeratePressure => !IsCriticalPressure && (_limitMode == LevelLimitType.Moves
        ? _remainingMoves <= Mathf.Max(_lowMovesThreshold + 3, Mathf.CeilToInt(_initialMoves * 0.35f))
        : _remainingSeconds <= Mathf.Max(_lowTimeThresholdSeconds + 12f, _initialDurationSeconds * 0.38f));

    public int DisplayedSeconds => Mathf.Max(0, Mathf.CeilToInt(_remainingSeconds));
    public int DisplayedValue => _limitMode == LevelLimitType.Moves ? Mathf.Max(0, _remainingMoves) : DisplayedSeconds;
    public bool IsRunning => _isRunning && !_isPaused && !_isExpired;
    public bool IsExpired => _isExpired;

    public event Action<float> OnTimeChanged;
    public event Action OnTimeExpired;

    private void Awake()
    {
        ResolveReferences();

        if (_countText != null)
        {
            _defaultColor = _countText.color;
            _baseScale = _countText.rectTransform.localScale;
        }
    }

    private void Start()
    {
        if (!_configuredExternally)
        {
            ResetTimer(_initialDurationSeconds, autoStart: _autoStart);
        }
    }

    private void Update()
    {
        if (!IsRunning)
        {
            return;
        }

        float dt = _useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (dt <= 0f)
        {
            return;
        }

        if (_bonusFlashRemaining > 0f)
        {
            _bonusFlashRemaining = Mathf.Max(0f, _bonusFlashRemaining - dt);
            if (_bonusFlashRemaining <= 0f)
            {
                UpdateTimerVisualTint();
            }
        }

        if (_limitMode == LevelLimitType.Moves)
        {
            return;
        }

        _remainingSeconds = Mathf.Max(0f, _remainingSeconds - dt);
        UpdateDisplayIfNeeded();
        UpdateTimerVisualTint();

        if (_remainingSeconds <= 0f)
        {
            ExpireNow();
        }
        else
        {
            OnTimeChanged?.Invoke(_remainingSeconds);
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        ResolveReferences();
    }
#endif

    /// <summary>
    /// Configures the counter in countdown Timer mode.
    /// </summary>
    public void ConfigureForTime(float durationSeconds, bool autoStart = true)
    {
        _configuredExternally = true;
        _limitMode = LevelLimitType.Time;
        _initialDurationSeconds = Mathf.Max(1f, durationSeconds);
        ResetTimer(_initialDurationSeconds, autoStart);
    }

    /// <summary>
    /// Configures the counter in Move-Limit mode.
    /// </summary>
    public void ConfigureForMoves(int moveLimit, bool autoStart = true)
    {
        _configuredExternally = true;
        ResolveReferences();
        _limitMode = LevelLimitType.Moves;
        _initialMoves = Mathf.Max(1, moveLimit);
        _remainingMoves = _initialMoves;
        _remainingSeconds = 0f;
        _isExpired = false;
        _isPaused = false;
        _isRunning = autoStart;
        _lastDisplayedValue = -1;
        _bonusFlashRemaining = 0f;

        if (_countText != null)
        {
            _countText.rectTransform.localScale = _baseScale;
        }

        UpdateDisplayIfNeeded(force: true);
        UpdateTimerVisualTint();
        OnTimeChanged?.Invoke(_remainingMoves);
    }

    /// <summary>
    /// Resets the countdown to the specified duration (or configured default).
    /// </summary>
    public void ResetTimer(float durationSeconds = -1f, bool autoStart = true)
    {
        ResolveReferences();

        _limitMode = LevelLimitType.Time;
        _remainingSeconds = durationSeconds > 0f ? durationSeconds : _initialDurationSeconds;
        _initialDurationSeconds = _remainingSeconds;
        _remainingMoves = _initialMoves;
        _isExpired = false;
        _isPaused = false;
        _isRunning = autoStart;
        _lastDisplayedValue = -1;
        _bonusFlashRemaining = 0f;

        if (_countText != null)
        {
            _countText.rectTransform.localScale = _baseScale;
        }

        UpdateDisplayIfNeeded(force: true);
        UpdateTimerVisualTint();
        OnTimeChanged?.Invoke(_remainingSeconds);
    }

    public void StartTimer()
    {
        if (!_isExpired)
        {
            _isRunning = true;
            _isPaused = false;
        }
    }

    public void SetPaused(bool paused)
    {
        _isPaused = paused;
    }

    /// <summary>
    /// Decrements 1 move in Moves mode; expires when reaching 0.
    /// </summary>
    public void ConsumeMove()
    {
        if (_limitMode != LevelLimitType.Moves || !IsRunning)
        {
            return;
        }

        _remainingMoves = Mathf.Max(0, _remainingMoves - 1);
        UpdateDisplayIfNeeded(force: true);
        UpdateTimerVisualTint();

        if (_remainingMoves <= 0)
        {
            ExpireNow();
        }
        else
        {
            OnTimeChanged?.Invoke(_remainingMoves);
        }
    }

    /// <summary>
    /// Restores move count directly (used by Undo in Moves mode).
    /// </summary>
    public void RestoreMoves(int movesRemaining)
    {
        if (_limitMode != LevelLimitType.Moves)
        {
            return;
        }

        _remainingMoves = Mathf.Max(1, movesRemaining);
        _isExpired = false;
        _isRunning = true;
        UpdateDisplayIfNeeded(force: true);
        UpdateTimerVisualTint();
        OnTimeChanged?.Invoke(_remainingMoves);
    }

    /// <summary>
    /// Adds bonus seconds in Time mode or bonus moves in Moves mode.
    /// </summary>
    public bool AddExtraLimit(float extraSeconds, int extraMoves)
    {
        if (_isExpired)
        {
            return false;
        }

        if (_limitMode == LevelLimitType.Moves)
        {
            if (extraMoves <= 0)
            {
                return false;
            }

            _remainingMoves += extraMoves;
            _bonusFlashRemaining = 0.65f;
            UpdateDisplayIfNeeded(force: true);
            UpdateTimerVisualTint();
            PlayBonusPunch();
            OnTimeChanged?.Invoke(_remainingMoves);
            return true;
        }

        return AddTime(extraSeconds);
    }

    /// <summary>
    /// Adds bonus seconds when the Extra Time power-up is activated.
    /// </summary>
    public bool AddTime(float extraSeconds)
    {
        if (_limitMode == LevelLimitType.Moves)
        {
            return AddExtraLimit(extraSeconds, Mathf.Max(3, Mathf.RoundToInt(extraSeconds / 3f)));
        }

        if (extraSeconds <= 0f || _isExpired)
        {
            return false;
        }

        _remainingSeconds += extraSeconds;
        _bonusFlashRemaining = 0.65f;

        UpdateDisplayIfNeeded(force: true);
        UpdateTimerVisualTint();
        PlayBonusPunch();

        OnTimeChanged?.Invoke(_remainingSeconds);
        return true;
    }

    private void ExpireNow()
    {
        _isRunning = false;
        _isExpired = true;
        OnTimeChanged?.Invoke(0f);
        OnTimeExpired?.Invoke();
    }

    private void ResolveReferences()
    {
        if (_countText == null)
        {
            _countText = GetComponentInChildren<TMP_Text>(includeInactive: true);
        }
    }

    private void UpdateDisplayIfNeeded(bool force = false)
    {
        int value = DisplayedValue;
        if (!force && value == _lastDisplayedValue)
        {
            return;
        }

        int lowCutoff = _limitMode == LevelLimitType.Moves
            ? _lowMovesThreshold
            : Mathf.Min(5, Mathf.FloorToInt(_lowTimeThresholdSeconds));

        bool isTickingLow = _pulseWhenLow
            && !force
            && value < _lastDisplayedValue
            && value > 0
            && value <= lowCutoff;

        _lastDisplayedValue = value;

        if (_countText != null)
        {
            _countText.text = value.ToString();
            if (isTickingLow && _bonusFlashRemaining <= 0f)
            {
                PlayScalePulse(1.12f, 0.14f);
            }
        }
    }

    private void UpdateTimerVisualTint()
    {
        if (_countText == null)
        {
            return;
        }

        if (_bonusFlashRemaining > 0f)
        {
            _countText.color = _bonusFlashColor;
        }
        else if (IsCriticalPressure)
        {
            _countText.color = _lowTimeColor;
        }
        else
        {
            _countText.color = _defaultColor;
        }
    }

    private void PlayBonusPunch()
    {
        PlayScalePulse(1.25f, 0.24f);
    }

    private void PlayScalePulse(float peakScale, float duration)
    {
        if (_countText == null || !gameObject.activeInHierarchy)
        {
            return;
        }

        if (_punchCoroutine != null)
        {
            StopCoroutine(_punchCoroutine);
        }

        _punchCoroutine = StartCoroutine(AnimateScalePulse(_countText.rectTransform, peakScale, duration));
    }

    private IEnumerator AnimateScalePulse(RectTransform target, float peakScale, float duration)
    {
        float half = Mathf.Max(0.02f, duration * 0.5f);
        Vector3 peak = _baseScale * peakScale;

        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            target.localScale = Vector3.Lerp(_baseScale, peak, t / half);
            yield return null;
        }

        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            target.localScale = Vector3.Lerp(peak, _baseScale, t / half);
            yield return null;
        }

        target.localScale = _baseScale;
        _punchCoroutine = null;
    }
}
