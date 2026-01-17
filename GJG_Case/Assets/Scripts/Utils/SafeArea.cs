using UnityEngine;

namespace Internal.Core.Utils
{
    /// <summary>
    /// Safe Area implementation for UI.
    /// Attaching this to a Panel will resize it to fit within the Safe Area of the screen (excluding notches, home bars, etc.).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class SafeArea : MonoBehaviour
    {
        private RectTransform _rectTransform;
        private Rect _lastSafeArea = new Rect(0, 0, 0, 0);
        private Vector2Int _lastScreenSize = new Vector2Int(0, 0);
        private ScreenOrientation _lastOrientation = ScreenOrientation.AutoRotation;

        [SerializeField] private bool _conformX = true;  // Yatayda güvenli alana uy
        [SerializeField] private bool _conformY = true;  // Dikeyde güvenli alana uy

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            Refresh();
        }

        private void Update()
        {
            Refresh();
        }

        private void Refresh()
        {
            Rect safeArea = Screen.safeArea;

            if (safeArea != _lastSafeArea || Screen.width != _lastScreenSize.x || Screen.height != _lastScreenSize.y || Screen.orientation != _lastOrientation)
            {
                ApplySafeArea(safeArea);
            }
        }

        private void ApplySafeArea(Rect r)
        {
            _lastSafeArea = r;
            _lastScreenSize.x = Screen.width;
            _lastScreenSize.y = Screen.height;
            _lastOrientation = Screen.orientation;

            // Convert Safe Area rect to anchors
            Vector2 anchorMin = r.position;
            Vector2 anchorMax = r.position + r.size;

            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;

            // Overwrite if conformity is disabled
            if (!_conformX)
            {
                anchorMin.x = 0;
                anchorMax.x = 1;
            }

            if (!_conformY)
            {
                anchorMin.y = 0;
                anchorMax.y = 1;
            }

            if (anchorMin.x >= 0 && anchorMin.y >= 0 && anchorMax.x >= 0 && anchorMax.y >= 0)
            {
                _rectTransform.anchorMin = anchorMin;
                _rectTransform.anchorMax = anchorMax;
            }
            
            // Debug.Log($"Safe Area Applied: {r} | ConformX: {_conformX} | ConformY: {_conformY}");
        }
    }
}
