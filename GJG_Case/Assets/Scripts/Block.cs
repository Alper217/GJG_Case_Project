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
        }

        public void UpdateVisual(Sprite newSprite)
        {
            _renderer.sprite = newSprite;
        }

        public void UpdatePosition(int row, int col, Vector3 worldPosition)
        {
            Row = row;
            Col = col;
            // TODO: Add interpolation/animation
            transform.position = worldPosition;
        }
    }
}
