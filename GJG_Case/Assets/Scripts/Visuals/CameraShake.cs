using UnityEngine;

namespace Internal.Visuals
{
    public class CameraShake : MonoBehaviour
    {
        private Vector3 _originalPos;
        private float _shakeTimer;
        private float _shakeMagnitude;

        private void Awake()
        {
            _originalPos = transform.localPosition;
        }

        public void Shake(float duration, float magnitude)
        {
            _shakeTimer = duration;
            _shakeMagnitude = magnitude;
            // Ensure we store the "base" position if not shaking currently, 
            // but this simple logic assumes we always return to originalPos when done.
            // If camera moves, we might need to handle that differently (e.g., shake a child, or add offset).
            // For this project, camera is static logic-wise in gameplay, so this is fine.
        }

        private void LateUpdate()
        {
            if (_shakeTimer > 0)
            {
                transform.localPosition = _originalPos + Random.insideUnitSphere * _shakeMagnitude;
                _shakeTimer -= Time.deltaTime;
            }
            else
            {
                _shakeTimer = 0;
                transform.localPosition = _originalPos;
            }
        }

        // Allow updating original pos if camera moves for other reasons (like resize)
        public void SetOriginalPos(Vector3 pos)
        {
            if (_shakeTimer <= 0)
            {
                _originalPos = pos;
            }
        }
    }
}
