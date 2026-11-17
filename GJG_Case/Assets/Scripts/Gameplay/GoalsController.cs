using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds and tracks up to 4 level objectives in TopBar/Panel and implements ILevelProgressProvider
/// so CharacterMascotController automatically reflects real goal progress.
/// </summary>
public class GoalsController : MonoBehaviour, ILevelProgressProvider
{
    [Header("Container & Theme Assets")]
    [SerializeField] private RectTransform _goalsContainer;
    [SerializeField] private TMP_FontAsset _countFontAsset;
    [SerializeField] private Material _countFontMaterial;
    [SerializeField] private Sprite _checkMarkSprite;

    private readonly List<GoalItemUI> _activeGoals = new List<GoalItemUI>(4);
    private bool _completedNotified;

    public float NormalizedProgress
    {
        get
        {
            int required = 0;
            int collected = 0;
            for (int i = 0; i < _activeGoals.Count; i++)
            {
                required += _activeGoals[i].TargetAmount;
                collected += _activeGoals[i].CollectedAmount;
            }

            return required > 0 ? Mathf.Clamp01((float)collected / required) : 0f;
        }
    }

    public bool IsLevelCompleted
    {
        get
        {
            if (_activeGoals.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < _activeGoals.Count; i++)
            {
                if (!_activeGoals[i].IsCompleted)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public event Action<float> OnProgressChanged;
    public event Action OnAllGoalsCompleted;

    public void BuildGoals(IReadOnlyList<LevelConfigSO.LevelGoalEntry> goalEntries, BoardSpriteCatalog catalog)
    {
        EnsureContainer();
        ClearExistingGoals();
        _completedNotified = false;

        if (goalEntries == null || goalEntries.Count == 0 || catalog == null)
        {
            OnProgressChanged?.Invoke(0f);
            return;
        }

        int count = Mathf.Min(4, goalEntries.Count);
        float cardWidth = count >= 4 ? 90f : (count == 3 ? 104f : 112f);
        float cardHeight = 122f;
        float iconSize = count >= 4 ? 66f : 74f;

        for (int i = 0; i < count; i++)
        {
            LevelConfigSO.LevelGoalEntry entry = goalEntries[i];
            GoalItemUI card = CreateGoalCard(i, cardWidth, cardHeight, iconSize);
            Sprite iconSprite = catalog.GetGoalIconSprite(entry.TargetType);
            card.Initialize(entry.TargetType, entry.TargetAmount, iconSprite, _checkMarkSprite);
            _activeGoals.Add(card);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(_goalsContainer);
        OnProgressChanged?.Invoke(0f);
    }

    public bool IsActiveGoalColor(BlockColor color)
    {
        GoalTargetType target = (GoalTargetType)(int)color;
        for (int i = 0; i < _activeGoals.Count; i++)
        {
            if (_activeGoals[i].TargetType == target && !_activeGoals[i].IsCompleted)
            {
                return true;
            }
        }

        return false;
    }

    public void RecordBlockCleared(BlockColor color, int count = 1)
    {
        RecordTargetProgress((GoalTargetType)(int)color, count);
    }

    public void RecordWoodCrateBroken(int count = 1)
    {
        RecordTargetProgress(GoalTargetType.WoodCrate, count);
    }

    public void RecordLockBroken(int count = 1)
    {
        RecordTargetProgress(GoalTargetType.LockedBlock, count);
    }

    public int[] CaptureRemainingCounts()
    {
        var snapshot = new int[_activeGoals.Count];
        for (int i = 0; i < _activeGoals.Count; i++)
        {
            snapshot[i] = _activeGoals[i].RemainingAmount;
        }

        return snapshot;
    }

    public void RestoreRemainingCounts(int[] snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        int count = Mathf.Min(snapshot.Length, _activeGoals.Count);
        for (int i = 0; i < count; i++)
        {
            _activeGoals[i].SetRemainingDirect(snapshot[i]);
        }

        _completedNotified = IsLevelCompleted;
        OnProgressChanged?.Invoke(NormalizedProgress);
    }

    private void RecordTargetProgress(GoalTargetType targetType, int count)
    {
        if (count <= 0)
        {
            return;
        }

        bool changed = false;
        for (int i = 0; i < _activeGoals.Count; i++)
        {
            if (_activeGoals[i].TargetType == targetType && _activeGoals[i].ApplyProgress(count))
            {
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        OnProgressChanged?.Invoke(NormalizedProgress);

        if (!_completedNotified && IsLevelCompleted)
        {
            _completedNotified = true;
            OnAllGoalsCompleted?.Invoke();
        }
    }

    private void EnsureContainer()
    {
        if (_goalsContainer == null)
        {
            Transform found = transform.Find("GoalsContainer");
            if (found != null)
            {
                _goalsContainer = found as RectTransform;
            }
        }

        if (_goalsContainer == null)
        {
            var go = new GameObject("GoalsContainer", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(transform, worldPositionStays: false);
            _goalsContainer = go.GetComponent<RectTransform>();
            _goalsContainer.anchorMin = new Vector2(0.5f, 0.5f);
            _goalsContainer.anchorMax = new Vector2(0.5f, 0.5f);
            _goalsContainer.anchoredPosition = new Vector2(-32f, -22f);
            _goalsContainer.sizeDelta = new Vector2(400f, 132f);
        }

        HorizontalLayoutGroup layout = _goalsContainer.GetComponent<HorizontalLayoutGroup>();
        if (layout == null)
        {
            layout = _goalsContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
        }

        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 8f;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
    }

    private void ClearExistingGoals()
    {
        _activeGoals.Clear();
        if (_goalsContainer == null)
        {
            return;
        }

        for (int i = _goalsContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(_goalsContainer.GetChild(i).gameObject);
        }
    }

    private GoalItemUI CreateGoalCard(int index, float cardWidth, float cardHeight, float iconSize)
    {
        var cardGo = new GameObject($"Goal_{index}", typeof(RectTransform), typeof(GoalItemUI));
        cardGo.transform.SetParent(_goalsContainer, worldPositionStays: false);

        RectTransform cardRect = cardGo.GetComponent<RectTransform>();
        cardRect.sizeDelta = new Vector2(cardWidth, cardHeight);

        // Icon child
        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconGo.transform.SetParent(cardGo.transform, worldPositionStays: false);
        RectTransform iconRect = iconGo.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = new Vector2(0f, 16f);
        iconRect.sizeDelta = new Vector2(iconSize, iconSize);
        Image iconImg = iconGo.GetComponent<Image>();
        iconImg.raycastTarget = false;

        // Remaining count label (matches left Counter/Count font & bold outline styling)
        var textGo = new GameObject("Count", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(cardGo.transform, worldPositionStays: false);
        RectTransform textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = new Vector2(0f, -38f);
        textRect.sizeDelta = new Vector2(cardWidth + 24f, 54f);

        TextMeshProUGUI tmp = textGo.GetComponent<TextMeshProUGUI>();
        TMP_Text counterReference = ResolveCounterStyleSource();
        if (counterReference != null)
        {
            tmp.font = counterReference.font;
            tmp.fontSharedMaterial = counterReference.fontSharedMaterial;
            tmp.fontStyle = counterReference.fontStyle;
            tmp.fontWeight = counterReference.fontWeight;
        }
        else
        {
            if (_countFontAsset != null) tmp.font = _countFontAsset;
            if (_countFontMaterial != null) tmp.fontSharedMaterial = _countFontMaterial;
            tmp.fontStyle = FontStyles.Bold;
        }

        tmp.fontSize = 46f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        // Checkmark icon shown when completed
        var checkGo = new GameObject("Check", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        checkGo.transform.SetParent(cardGo.transform, worldPositionStays: false);
        RectTransform checkRect = checkGo.GetComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0.5f, 0.5f);
        checkRect.anchorMax = new Vector2(0.5f, 0.5f);
        checkRect.anchoredPosition = new Vector2(0f, -38f);
        checkRect.sizeDelta = new Vector2(40f, 40f);
        Image checkImg = checkGo.GetComponent<Image>();
        checkImg.raycastTarget = false;
        checkGo.SetActive(false);

        GoalItemUI card = cardGo.GetComponent<GoalItemUI>();
        card.BindRuntimeReferences(iconImg, tmp, checkImg);
        return card;
    }

    private static TMP_Text ResolveCounterStyleSource()
    {
        LevelTimer timer = FindFirstObjectByType<LevelTimer>(FindObjectsInactive.Include);
        return timer != null ? timer.CountText : null;
    }
}
