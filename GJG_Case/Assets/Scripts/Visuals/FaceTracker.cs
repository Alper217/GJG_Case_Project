using UnityEngine;

namespace Internal.Visuals
{
    public class FaceTracker : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform _leftEyeCenter;
        [SerializeField] private Transform _rightEyeCenter;
        [SerializeField] private Transform _leftPupil;
        [SerializeField] private Transform _rightPupil;

        [Header("Settings")]
        [SerializeField] private float _eyeRadius = 0.5f; // How far the pupil can move from center
        [SerializeField] private float _followSpeed = 10f;

        [Header("Idle Behavior")]
        [SerializeField] private float _attentionSpan = 2.0f; // How long to stare at the target
        [SerializeField] private Vector3 _defaultLookOffset = new Vector3(0, 0, -10f); // Look at camera

        private Vector3 _targetPosition;
        private float _lastInteractionTime;

        private void Start()
        {
            _targetPosition = transform.position + _defaultLookOffset;
        }

        public void SetLookTarget(Vector3 worldPosition)
        {
            _targetPosition = worldPosition;
            _lastInteractionTime = Time.time;
        }

        [Header("Mouth Settings")]
        [SerializeField] private SpriteRenderer _mouthRenderer;
        [SerializeField] private Sprite _mouthNormal; // :)
        [SerializeField] private Sprite _mouthSilly;  // :P
        [SerializeField] private Sprite _mouthWandering; // :| or :/

        // Idle Internal State
        private float _nextRandomLookTime;
        private Vector3 _randomLookTarget;
        private bool _useDirectTarget = true; // If false, we force eyes to center

        private void Update()
        {
            // If interacting, look at target.
            // If interaction time passed -> Enter IDLE mode.
            if (Time.time > _lastInteractionTime + _attentionSpan)
            {
                // IDLE MODE
                if (Time.time > _nextRandomLookTime)
                {
                    // Pick a new random behavior
                    float randomChoice = Random.value;
                    
                    if (randomChoice < 0.5f)
                    {
                        // 50% Normal / Centered (Pupils centered)
                        _useDirectTarget = false; 
                        SetMouth(_mouthNormal);
                    }
                    else if (randomChoice < 0.7f)
                    {
                        // 20% Silly / Cross-eyed (Look at nose/close)
                        // Target is very close to face
                        _randomLookTarget = transform.position + new Vector3(0, -0.2f, -1.0f); 
                        _useDirectTarget = true;
                        SetMouth(_mouthSilly);
                    }
                    else
                    {
                        // 30% Look Randomly "Around"
                        Vector2 randomCircle = Random.insideUnitCircle * 5.0f; 
                        _randomLookTarget = transform.position + new Vector3(randomCircle.x, randomCircle.y, -5f);
                        _useDirectTarget = true;
                        SetMouth(_mouthWandering);
                    }

                    // Set next change time
                    _nextRandomLookTime = Time.time + Random.Range(1.5f, 4.0f);
                }

                if (_useDirectTarget)
                {
                     _targetPosition = Vector3.Lerp(_targetPosition, _randomLookTarget, Time.deltaTime * 5f);
                }
            }
            else
            {
                // Interaction Mode
                _useDirectTarget = true;
                SetMouth(_mouthNormal); // Or Happy?
            }

            UpdateEye(_leftEyeCenter, _leftPupil);
            UpdateEye(_rightEyeCenter, _rightPupil);
        }

        private void SetMouth(Sprite sprite)
        {
            if (_mouthRenderer != null && sprite != null)
            {
                _mouthRenderer.sprite = sprite;
            }
        }

        private void UpdateEye(Transform center, Transform pupil)
        {
            if (center == null || pupil == null) return;

            Vector3 desiredPos;

            if (!_useDirectTarget)
            {
                // Force Centered (Normal Look)
                desiredPos = center.position;
            }
            else
            {
                // Direction from eye center to target
                Vector3 direction = _targetPosition - center.position;
                direction.z = 0; 

                // Limit the distance to radius
                Vector3 clampedOffset = Vector3.ClampMagnitude(direction, _eyeRadius);
                desiredPos = center.position + clampedOffset;
            }

            // Smoothly move pupil
            pupil.position = Vector3.Lerp(pupil.position, desiredPos, Time.deltaTime * _followSpeed);
        }
    }
}
