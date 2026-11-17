using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Coordinates the 4 bottom HUD power-ups:
/// 1. ExtraTime (Ek Sure)
/// 2. SolvePartial (Puzzle'in bir kismini coz)
/// 3. Undo (Geri al)
/// 4. BreakLock (Kilitli bloklarin kilidini kir)
/// </summary>
public class PowerUpManager : MonoBehaviour
{
    [Serializable]
    public class PowerUpSlot
    {
        public PowerUpType Type;
        public Button Button;
        public TMP_Text CountText;
        [Min(0)] public int InitialCharges = 3;

        public int RemainingCharges { get; internal set; }
        internal UnityAction ClickListener;
        internal Vector3 BaseScale;
        internal Color BaseGraphicColor;
        internal bool VisualsCached;
        internal Coroutine ActivePunch;
    }

    [Header("Dependencies")]
    [SerializeField] private LevelTimer _levelTimer;
    [Tooltip("Optional MonoBehaviour implementing IBoardPowerUpHandler (auto-discovered if omitted).")]
    [SerializeField] private MonoBehaviour _boardHandlerSource;

    [Header("Power-Up Settings")]
    [Min(1f)]
    [SerializeField] private float _extraTimeSeconds = 15f;
    [Min(1)]
    [SerializeField] private int _extraMovesCount = 5;
    [Tooltip("Allows testing SolvePartial/Undo/BreakLock button counts in UI before a board controller is attached.")]
    [SerializeField] private bool _allowUiPreviewWithoutBoard = true;

    [Header("UI Slots (Left to Right)")]
    [SerializeField] private PowerUpSlot[] _slots =
    {
        new PowerUpSlot { Type = PowerUpType.ExtraTime, InitialCharges = 3 },
        new PowerUpSlot { Type = PowerUpType.SolvePartial, InitialCharges = 3 },
        new PowerUpSlot { Type = PowerUpType.Undo, InitialCharges = 3 },
        new PowerUpSlot { Type = PowerUpType.BreakLock, InitialCharges = 3 }
    };

    [Header("Button Feedback")]
    [SerializeField] private Color _disabledTint = new Color(0.55f, 0.55f, 0.55f, 0.75f);

    private IBoardPowerUpHandler _boardHandler;

    public event Action<PowerUpType, int> OnPowerUpUsed;

    private void Awake()
    {
        ResolveDependencies();
        InitializeSlots();
    }

    private void OnEnable()
    {
        if (_levelTimer != null)
        {
            _levelTimer.OnTimeExpired += HandleTimerExpired;
        }
    }

    private void Start()
    {
        RefreshAllSlots();
    }

    private void OnDisable()
    {
        if (_levelTimer != null)
        {
            _levelTimer.OnTimeExpired -= HandleTimerExpired;
        }
    }

    private void OnDestroy()
    {
        UnbindSlotListeners();
    }

#if UNITY_EDITOR
    private void Reset()
    {
        if (_levelTimer == null)
        {
            _levelTimer = FindFirstObjectByType<LevelTimer>();
        }

        TryAutoWireSlotByChildName(PowerUpType.ExtraTime, "Time Power-Up");
        TryAutoWireSlotByChildName(PowerUpType.SolvePartial, "Sort Power-Up");
        TryAutoWireSlotByChildName(PowerUpType.Undo, "Reverse Power-Up");
        TryAutoWireSlotByChildName(PowerUpType.BreakLock, "Destroy Obstacle Power-Up");
    }

    private void TryAutoWireSlotByChildName(PowerUpType type, string childName)
    {
        PowerUpSlot slot = FindSlot(type);
        if (slot == null)
        {
            return;
        }

        Transform child = transform.Find(childName);
        if (child == null)
        {
            return;
        }

        if (slot.Button == null)
        {
            slot.Button = child.GetComponent<Button>();
        }

        if (slot.CountText == null)
        {
            slot.CountText = child.GetComponentInChildren<TMP_Text>(includeInactive: true);
        }
    }
#endif

    /// <summary>
    /// Attaches a runtime gameplay handler for board-level power-ups (SolvePartial, Undo, BreakLock).
    /// </summary>
    public void SetBoardHandler(IBoardPowerUpHandler handler)
    {
        _boardHandler = handler;
        _boardHandlerSource = handler as MonoBehaviour;
        RefreshAllSlots();
    }

    public int GetRemainingCharges(PowerUpType type)
    {
        PowerUpSlot slot = FindSlot(type);
        return slot != null ? slot.RemainingCharges : 0;
    }

    public void SetCharges(PowerUpType type, int amount)
    {
        PowerUpSlot slot = FindSlot(type);
        if (slot == null)
        {
            return;
        }

        slot.RemainingCharges = Mathf.Max(0, amount);
        RefreshSlotVisual(slot);
    }

    public void ResetChargesToDefaults()
    {
        if (_slots == null)
        {
            return;
        }

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] != null)
            {
                _slots[i].RemainingCharges = Mathf.Max(0, _slots[i].InitialCharges);
            }
        }

        RefreshAllSlots();
    }

    public void AddCharges(PowerUpType type, int amount)
    {
        PowerUpSlot slot = FindSlot(type);
        if (slot == null || amount <= 0)
        {
            return;
        }

        slot.RemainingCharges += amount;
        RefreshSlotVisual(slot);
    }

    public void RefreshAllSlots()
    {
        if (_slots == null)
        {
            return;
        }

        for (int i = 0; i < _slots.Length; i++)
        {
            RefreshSlotVisual(_slots[i]);
        }
    }

    /// <summary>
    /// Attempts to execute the specified power-up, consuming one charge if successful.
    /// </summary>
    public bool TryTriggerPowerUp(PowerUpType type)
    {
        PowerUpSlot slot = FindSlot(type);
        if (slot == null || slot.RemainingCharges <= 0 || !CanUseSlot(slot))
        {
            return false;
        }

        if (!ExecuteSlot(slot))
        {
            return false;
        }

        slot.RemainingCharges--;
        RefreshAllSlots();
        PlayButtonPunch(slot);
        OnPowerUpUsed?.Invoke(type, slot.RemainingCharges);
        return true;
    }

    private void ResolveDependencies()
    {
        if (_levelTimer == null)
        {
            _levelTimer = FindFirstObjectByType<LevelTimer>();
        }

        if (_boardHandlerSource is IBoardPowerUpHandler explicitHandler)
        {
            _boardHandler = explicitHandler;
        }
        else
        {
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IBoardPowerUpHandler found)
                {
                    _boardHandler = found;
                    _boardHandlerSource = behaviours[i];
                    break;
                }
            }
        }
    }

    private void InitializeSlots()
    {
        if (_slots == null)
        {
            return;
        }

        for (int i = 0; i < _slots.Length; i++)
        {
            PowerUpSlot slot = _slots[i];
            if (slot == null)
            {
                continue;
            }

            slot.RemainingCharges = Mathf.Max(0, slot.InitialCharges);
            if (slot.Button != null)
            {
                CacheSlotVisuals(slot);
                PowerUpType capturedType = slot.Type;
                slot.ClickListener = () => TryTriggerPowerUp(capturedType);
                slot.Button.onClick.AddListener(slot.ClickListener);
            }
        }
    }

    private void UnbindSlotListeners()
    {
        if (_slots == null)
        {
            return;
        }

        for (int i = 0; i < _slots.Length; i++)
        {
            PowerUpSlot slot = _slots[i];
            if (slot?.Button != null && slot.ClickListener != null)
            {
                slot.Button.onClick.RemoveListener(slot.ClickListener);
                slot.ClickListener = null;
            }
        }
    }

    private bool CanUseSlot(PowerUpSlot slot)
    {
        if (slot.Type == PowerUpType.ExtraTime)
        {
            return _levelTimer != null && !_levelTimer.IsExpired;
        }

        if (_boardHandler != null)
        {
            return _boardHandler.CanUsePowerUp(slot.Type);
        }

        return _allowUiPreviewWithoutBoard;
    }

    private bool ExecuteSlot(PowerUpSlot slot)
    {
        if (slot.Type == PowerUpType.ExtraTime)
        {
            return _levelTimer != null && _levelTimer.AddExtraLimit(_extraTimeSeconds, _extraMovesCount);
        }

        if (_boardHandler != null)
        {
            return _boardHandler.TryExecutePowerUp(slot.Type);
        }

        if (!_allowUiPreviewWithoutBoard)
        {
            return false;
        }

        Debug.Log($"[PowerUpManager] '{slot.Type}' preview tetiklendi (henuz IBoardPowerUpHandler bagli degil).", this);
        return true;
    }

    private void HandleTimerExpired()
    {
        RefreshAllSlots();
    }

    private void CacheSlotVisuals(PowerUpSlot slot)
    {
        if (slot.VisualsCached || slot.Button == null)
        {
            return;
        }

        slot.BaseScale = slot.Button.transform.localScale;
        if (slot.Button.targetGraphic != null)
        {
            slot.BaseGraphicColor = slot.Button.targetGraphic.color;
        }
        else
        {
            slot.BaseGraphicColor = Color.white;
        }

        slot.VisualsCached = true;
    }

    private void RefreshSlotVisual(PowerUpSlot slot)
    {
        if (slot == null)
        {
            return;
        }

        if (slot.CountText != null)
        {
            slot.CountText.text = slot.RemainingCharges.ToString();
        }

        if (slot.Button != null)
        {
            CacheSlotVisuals(slot);
            bool usable = slot.RemainingCharges > 0 && CanUseSlot(slot);
            slot.Button.interactable = usable;

            if (slot.Button.targetGraphic != null)
            {
                slot.Button.targetGraphic.color = usable ? slot.BaseGraphicColor : _disabledTint;
            }
        }
    }

    private PowerUpSlot FindSlot(PowerUpType type)
    {
        if (_slots == null)
        {
            return null;
        }

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] != null && _slots[i].Type == type)
            {
                return _slots[i];
            }
        }

        return null;
    }

    private void PlayButtonPunch(PowerUpSlot slot)
    {
        if (slot?.Button == null || !gameObject.activeInHierarchy)
        {
            return;
        }

        if (slot.ActivePunch != null)
        {
            StopCoroutine(slot.ActivePunch);
        }

        slot.ActivePunch = StartCoroutine(AnimatePunch(slot));
    }

    private static IEnumerator AnimatePunch(PowerUpSlot slot)
    {
        Transform target = slot.Button.transform;
        Vector3 baseScale = slot.BaseScale == Vector3.zero ? Vector3.one : slot.BaseScale;
        Vector3 small = baseScale * 0.9f;
        Vector3 peak = baseScale * 1.07f;

        const float phase = 0.06f;
        for (float t = 0f; t < phase; t += Time.unscaledDeltaTime)
        {
            target.localScale = Vector3.Lerp(baseScale, small, t / phase);
            yield return null;
        }

        for (float t = 0f; t < phase; t += Time.unscaledDeltaTime)
        {
            target.localScale = Vector3.Lerp(small, peak, t / phase);
            yield return null;
        }

        for (float t = 0f; t < phase; t += Time.unscaledDeltaTime)
        {
            target.localScale = Vector3.Lerp(peak, baseScale, t / phase);
            yield return null;
        }

        target.localScale = baseScale;
        slot.ActivePunch = null;
    }
}
