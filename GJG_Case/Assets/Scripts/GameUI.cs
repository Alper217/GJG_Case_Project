using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BoardController _boardController;
    
    [Header("Input Fields")]
    [SerializeField] private TMP_InputField _inputM; // Rows
    [SerializeField] private TMP_InputField _inputN; // Cols
    [SerializeField] private TMP_InputField _inputK; // Colors
    [SerializeField] private TMP_InputField _inputA;
    [SerializeField] private TMP_InputField _inputB;
    [SerializeField] private TMP_InputField _inputC;
    
    [SerializeField] private Button _restartButton;

    [Header("Preset Buttons")]
    [SerializeField] private Button _easyButton;
    [SerializeField] private Button _mediumButton;
    [SerializeField] private Button _hardButton;

    private void Start()
    {
        _restartButton.onClick.AddListener(OnRestartClicked);
        
        if (_easyButton != null) _easyButton.onClick.AddListener(() => ApplyPreset(8, 6, 3, 3, 5, 7));
        if (_mediumButton != null) _mediumButton.onClick.AddListener(() => ApplyPreset(10, 8, 5, 4, 7, 9));
        if (_hardButton != null) _hardButton.onClick.AddListener(() => ApplyPreset(12, 9, 6, 5, 8, 10));

        // Concise defaults (Normal)
        ApplyPreset(10, 8, 6, 4, 7, 9, false);
    }

    private void ApplyPreset(int m, int n, int k, int a, int b, int c, bool autoRestart = true)
    {
        _inputM.text = m.ToString();
        _inputN.text = n.ToString();
        _inputK.text = k.ToString();
        _inputA.text = a.ToString();
        _inputB.text = b.ToString();
        _inputC.text = c.ToString();

        if (autoRestart)
        {
            OnRestartClicked();
        }
    }

    private void OnRestartClicked()
    {
        if (int.TryParse(_inputM.text, out int m) &&
            int.TryParse(_inputN.text, out int n) &&
            int.TryParse(_inputK.text, out int k) &&
            int.TryParse(_inputA.text, out int a) &&
            int.TryParse(_inputB.text, out int b) &&
            int.TryParse(_inputC.text, out int c))
        {
            // Validation - Increased max size for Hard mode
            m = Mathf.Clamp(m, 2, 20);
            n = Mathf.Clamp(n, 2, 20);
            k = Mathf.Clamp(k, 1, 6);
            
            _boardController.RestartBoard(m, n, k, a, b, c);
        }
        else
        {
            Debug.LogError("Invalid Input");
        }
    }
}
