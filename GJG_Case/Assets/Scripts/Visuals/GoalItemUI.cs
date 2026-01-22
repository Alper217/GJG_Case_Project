using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Internal.Visuals
{
    public class GoalItemUI : MonoBehaviour
    {
        [SerializeField] private Image _iconImage;
        [SerializeField] private TextMeshProUGUI _countText;
        [SerializeField] private GameObject _checkMark; // Optional visual for completion

        public int ColorId { get; private set; }
        private int _currentCount;

        public void Init(int colorId, int targetCount, Sprite icon)
        {
            ColorId = colorId;
            _currentCount = targetCount;
            
            if (_iconImage != null) _iconImage.sprite = icon;
            UpdateText();
            
            if (_checkMark != null) _checkMark.SetActive(false);
            
            // Ensure alpha is full
            var cg = GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = 1f;
        }

        public void DecreaseCount(int amount)
        {
            if (_currentCount <= 0) return;

            _currentCount -= amount;
            if (_currentCount < 0) _currentCount = 0;
            
            UpdateText();

            if (_currentCount == 0)
            {
                OnComplete();
            }
        }

        private void UpdateText()
        {
            if (_countText != null) _countText.text = _currentCount.ToString();
        }

        private void OnComplete()
        {
            // Visual feedback for completion
            if (_checkMark != null) 
            {
                _checkMark.SetActive(true);
                if (_countText != null) _countText.gameObject.SetActive(false);
            }
            else
            {
                 // Fallback: fade out or turn green
                 if (_countText != null) _countText.color = Color.green;
            }
        }
        public bool IsCompleted()
        {
            return _currentCount <= 0;
        }
    }
}
