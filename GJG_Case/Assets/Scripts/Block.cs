using UnityEngine;

namespace Internal.Core
{
    /// <summary>
    /// Represents a single block/tile in the game grid.
    /// Handles its own visual state, position, and animations.
    /// </summary>
    public class Block : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Reference to the SpriteRenderer component to change the block's appearance.")]
        [SerializeField] private SpriteRenderer _renderer;

        // Coordinates in the grid (Logical Position)
        public int Row { get; private set; }
        public int Col { get; private set; }
        
        // Block Type/Color ID (Used for matching logic)
        public int ColorId { get; private set; }

        /// <summary>
        /// Initializes the block with specific properties.
        /// Called when the block is created or pulled from the object pool.
        /// </summary>
        /// <param name="row">Grid Row Index</param>
        /// <param name="col">Grid Column Index</param>
        /// <param name="colorId">Logical Color ID</param>
        /// <param name="sprite">Visual Sprite</param>
        /// <param name="colorId">Logical Color ID</param>
        /// <param name="sprite">Visual Sprite</param>
        public void Init(int row, int col, int colorId, Sprite sprite)
        {
            Row = row;
            Col = col;
            ColorId = colorId;
            _renderer.sprite = sprite;
            
            // Ensure proper name for debugging in the Hierarchy.
            

            gameObject.name = $"Block_{row}_{col}";
            
            // Set Sorting Order based on Row to handle layering correctly.
            // Items in lower rows (visually lower) should be rendered on top of items above them if they overlap.
            if (_renderer != null)
            {
                _renderer.sortingOrder = Row - 1000; 
            }
            
            // Trigger the "Pop-In" animation (Scale from 0 to 1).
            StartCoroutine(PopInRoutine());
        }

        /// <summary>
        /// Coroutine for the Pop-In animation (Scaling up from 0).
        /// Used when a new block is spawned to refill the board.
        /// </summary>
        private System.Collections.IEnumerator PopInRoutine()
        {
            transform.localScale = Vector3.zero;
            float duration = 0.2f;
            float time = 0;
            
            while (time < duration)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / duration);
                
                // Linear lerp for scaling
                transform.localScale = Vector3.one  * t;
                yield return null;
            }
            
            transform.localScale = Vector3.one;
        }

        /// <summary>
        /// Updates the visual icon of the block.
        /// Used when the block changes state (e.g., group size grows/shrinks).
        /// </summary>
        /// <param name="newSprite">The new sprite to display.</param>
        public void UpdateVisual(Sprite newSprite)
        {
            _renderer.sprite = newSprite;
        }

        /// <summary>
        /// Updates the block's logical grid position and moves it visually to the new world position.
        /// </summary>
        /// <param name="row">New Grid Row</param>
        /// <param name="col">New Grid Column</param>
        /// <param name="worldPosition">New Visual World Position</param>
        public void UpdatePosition(int row, int col, Vector3 worldPosition)
        {
            Row = row;
            Col = col;
            
            // Stop any ongoing move animation to prevent conflicts
            if (_moveCoroutine != null) StopCoroutine(_moveCoroutine);
            
            // Start the new move animation (falling/bouncing)
            _moveCoroutine = StartCoroutine(MoveToPositionRoutine(worldPosition));
            
            // Update sorting order for the new row
            if (_renderer != null)
            {
                _renderer.sortingOrder = Row - 1000;
            }
        }

        private Coroutine _moveCoroutine;

        /// <summary>
        /// Coroutine that handles the smooth movement of the block to its target position.
        /// Uses an "Ease Out Bounce" easing function for a bouncy falling effect.
        /// </summary>
        private System.Collections.IEnumerator MoveToPositionRoutine(Vector3 targetPos)
        {
            Vector3 startPos = transform.position;
            float duration = 0.5f; 
            float time = 0;

            while (time < duration)
            {
                time += Time.deltaTime;
                float t = time / duration;
                
                // Apply "Ease Out Bounce" math to the time factor 't'
                float tBounce = EaseOutBounce(t);
                
                // Interpolate position based on the bounced time factor
                transform.position = Vector3.LerpUnclamped(startPos, targetPos, tBounce);
                yield return null;
            }
            
            // Ensure exact final position
            transform.position = targetPos;
        }

        /// <summary>
        /// Custom Easing Function: Ease Out Bounce.
        /// Returns a value that simulates a bouncing object (settling at 1.0).
        /// Source: https://easings.net/#easeOutBounce
        /// </summary>
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
