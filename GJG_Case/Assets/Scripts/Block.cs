using UnityEngine;

namespace Internal.Core
{
    public class Block : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _renderer;

        // Coordinates in the grid
        public int Row { get; private set; }
        public int Col { get; private set; }
        
        // Block Type/Color ID
        public int ColorId { get; private set; }

        public void Init(int row, int col, int colorId, Sprite sprite)
        {
            Row = row;
            Col = col;
            ColorId = colorId;
            _renderer.sprite = sprite;
            
            // Ensure proper name for debugging
            gameObject.name = $"Block_{row}_{col}";
            
            // Fixed sorting order as requested
            if (_renderer != null)
            {
                // Dynamic sorting order based on Row:
                // Lower rows (bottom of screen) should be rendered ON TOP of higher rows.
                // Assuming Row increases upwards.
                _renderer.sortingOrder = Row - 1000; 
            }
            
            // Pop-In Animation (Scale 0 -> 1)
            StartCoroutine(PopInRoutine());
        }

        private System.Collections.IEnumerator PopInRoutine()
        {
            transform.localScale = Vector3.zero;
            float duration = 0.2f;
            float time = 0;
            
            // Simple ease-out feel
            while (time < duration)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / duration);
                
                // Back/Overshoot effect or simple curve? Let's do simple smooth step.
                // t = t * t * (3f - 2f * t); // SmoothStep
                
                transform.localScale = Vector3.one  * t;
                yield return null;
            }
            
            transform.localScale = Vector3.one;
        }

        public void UpdateVisual(Sprite newSprite)
        {
            _renderer.sprite = newSprite;
        }

        public void UpdatePosition(int row, int col, Vector3 worldPosition)
        {
            Row = row;
            Col = col;
            
            // Stop any existing movement to prevent conflict
            if (_moveCoroutine != null) StopCoroutine(_moveCoroutine);
            _moveCoroutine = StartCoroutine(MoveToPositionRoutine(worldPosition));
            
            if (_renderer != null)
            {
                _renderer.sortingOrder = Row - 1000;
            }
        }

        private Coroutine _moveCoroutine;
        private System.Collections.IEnumerator MoveToPositionRoutine(Vector3 targetPos)
        {
            Vector3 startPos = transform.position;
            float duration = 0.5f; 
            float time = 0;

            while (time < duration)
            {
                time += Time.deltaTime;
                float t = time / duration;
                
                // Ease Out Bounce
                float tBounce = EaseOutBounce(t);
                
                transform.position = Vector3.LerpUnclamped(startPos, targetPos, tBounce);
                yield return null;
            }
            
            transform.position = targetPos;
        }

        private float EaseOutBounce(float x)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;

            if (x < 1 / d1) {
                return n1 * x * x;
            } else if (x < 2 / d1) {
                return n1 * (x -= 1.5f / d1) * x + 0.75f;
            } else if (x < 2.5 / d1) {
                return n1 * (x -= 2.25f / d1) * x + 0.9375f;
            } else {
                return n1 * (x -= 2.625f / d1) * x + 0.984375f;
            }
        }
    }
}
