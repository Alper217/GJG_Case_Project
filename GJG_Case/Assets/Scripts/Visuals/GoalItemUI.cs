using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Single objective card displayed inside TopBar/Panel/GoalsContainer.
/// Shows the target sprite, remaining count, and glossy checkmark when completed.
/// </summary>
public class GoalItemUI : MonoBehaviour
{
    [SerializeField] private Image _iconImage;
    [SerializeField] private TMP_Text _countText;
    [SerializeField] private Image _checkMarkImage;

    private int _targetAmount;
    private int _remainingAmount;
    private float _punchTimer;
    private Vector3 _baseScale = Vector3.one;

    public GoalTargetType TargetType { get; private set; }
    public int TargetAmount => _targetAmount;
    public int RemainingAmount => _remainingAmount;
    public int CollectedAmount => Mathf.Max(0, _targetAmount - _remainingAmount);
    public bool IsCompleted => _remainingAmount <= 0;

    private void Awake()
    {
        _baseScale = transform.localScale;
    }

    private void Update()
    {
        if (_punchTimer <= 0f)
        {
            return;
        }

        _punchTimer = Mathf.Max(0f, _punchTimer - Time.deltaTime);
        float n = _punchTimer / 0.18f;
        float s = 1f + Mathf.Sin(n * Mathf.PI) * 0.2f;
        transform.localScale = _baseScale * s;
        if (_punchTimer <= 0f)
        {
            transform.localScale = _baseScale;
        }
    }

    public void BindRuntimeReferences(Image iconImage, TMP_Text countText, Image checkMarkImage)
    {
        _iconImage = iconImage;
        _countText = countText;
        _checkMarkImage = checkMarkImage;
    }

    public void Initialize(GoalTargetType targetType, int targetAmount, Sprite iconSprite, Sprite checkSprite)
    {
        TargetType = targetType;
        _targetAmount = Mathf.Max(1, targetAmount);
        _remainingAmount = _targetAmount;
        _punchTimer = 0f;
        transform.localScale = _baseScale;

        if (_iconImage != null)
        {
            _iconImage.sprite = iconSprite;
            _iconImage.preserveAspect = true;
            _iconImage.color = Color.white;
        }

        if (_checkMarkImage != null)
        {
            if (checkSprite != null)
            {
                _checkMarkImage.sprite = checkSprite;
            }
            _checkMarkImage.preserveAspect = true;
            _checkMarkImage.gameObject.SetActive(false);
        }

        RefreshVisuals();
    }

    public bool ApplyProgress(int amount)
    {
        if (amount <= 0 || _remainingAmount <= 0)
        {
            return false;
        }

        _remainingAmount = Mathf.Max(0, _remainingAmount - amount);
        _punchTimer = 0.18f;
        RefreshVisuals();
        return true;
    }

    public void SetRemainingDirect(int remaining)
    {
        _remainingAmount = Mathf.Clamp(remaining, 0, _targetAmount);
        _punchTimer = 0f;
        transform.localScale = _baseScale;
        RefreshVisuals();
    }

    private void RefreshVisuals()
    {
        bool done = IsCompleted;
        if (_countText != null)
        {
            _countText.gameObject.SetActive(!done);
            _countText.text = _remainingAmount.ToString();
        }

        if (_checkMarkImage != null)
        {
            _checkMarkImage.gameObject.SetActive(done);
        }
    }
}
