using UnityEngine;
using TMPro;

namespace Internal.Visuals
{
    public class FloatingText : MonoBehaviour
    {
        [SerializeField] private TextMeshPro _textComponent;
        [SerializeField] private float _moveSpeed = 1.0f;
        [SerializeField] private float _fadeDuration = 1.0f;

        private float _timer;
        private Color _startColor;

        public void Init(string text, Color color)
        {
            if (_textComponent == null) _textComponent = GetComponent<TextMeshPro>();
            
            _textComponent.text = text;
            _textComponent.color = color;
            _startColor = color;
            _timer = 0;
            
            // Random variance in horizontal speed for "pop" feel
            float randomX = Random.Range(-0.5f, 0.5f);
            
            // Ensure we are active
            gameObject.SetActive(true);
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            
            // Move up
            transform.position += new Vector3(0, _moveSpeed * Time.deltaTime, 0);

            // Fade out
            float alpha = Mathf.Lerp(1, 0, _timer / _fadeDuration);
            _textComponent.color = new Color(_startColor.r, _startColor.g, _startColor.b, alpha);

            if (_timer >= _fadeDuration)
            {
                Destroy(gameObject);
            }
        }
    }
}
