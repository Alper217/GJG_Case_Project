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
    
    [SerializeField] private Button _updateButton;

    [Header("Preset Buttons")]
    [SerializeField] private Button _easyButton;
    [SerializeField] private Button _mediumButton;
    [SerializeField] private Button _hardButton;
    
    [Header("Power-Up Buttons")]
    [SerializeField] private Button _extraTimeButton;
    [SerializeField] private Button _sortButton;
    [SerializeField] private Button _undoButton;

    [Header("Panel Controls")]
    [SerializeField] private GameObject _inputPanel;
    [SerializeField] private Button _openPanelButton;
    [SerializeField] private Button _closePanelButton;

    [Header("Start Menu & HUD")]
    [SerializeField] private GameObject _startMenuPanel;
    [SerializeField] private Button _playButton;
    [SerializeField] private Button _customGameButton; // New button
    [SerializeField] private TextMeshProUGUI _levelText;
    [SerializeField] private Button _returnToMenuButton; // On Game Over panel

    private void Start()
    {
        _updateButton.onClick.AddListener(OnRestartClicked);
        
        if (_easyButton != null) _easyButton.onClick.AddListener(() => ApplyPreset(8, 6, 3, 3, 5, 7));
        if (_mediumButton != null) _mediumButton.onClick.AddListener(() => ApplyPreset(10, 8, 5, 4, 7, 9));
        if (_hardButton != null) _hardButton.onClick.AddListener(() => ApplyPreset(12, 9, 6, 5, 8, 10));

        if (_extraTimeButton != null) _extraTimeButton.onClick.AddListener(() => _boardController.AddExtraTime(10f));
        if (_sortButton != null) _sortButton.onClick.AddListener(() => _boardController.SortBoardByColor());
        if (_undoButton != null) _undoButton.onClick.AddListener(() => _boardController.UndoLastMove());

        // Panel Logic
        if (_openPanelButton != null) _openPanelButton.onClick.AddListener(() => SetPanelState(true));
        if (_closePanelButton != null) _closePanelButton.onClick.AddListener(() => SetPanelState(false));

        // Start with panel closed (optional, keeps UI clean)
        SetPanelState(false);

        // Concise defaults (Normal)
        ApplyPreset(10, 8, 6, 4, 7, 9, false);

        // Fix UI Layering: Force Canvas to be on top of everything
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvas.sortingOrder = 1000;
        }

        // Start Menu Logic
        if (_playButton != null) _playButton.onClick.AddListener(OnPlayClicked);
        if (_customGameButton != null) _customGameButton.onClick.AddListener(OnCustomGameClicked);
        if (_returnToMenuButton != null) _returnToMenuButton.onClick.AddListener(OnReturnToMenuClicked);



        // Define Start State
        if (_startMenuPanel != null) _startMenuPanel.SetActive(true);
        if (_inputPanel != null) _inputPanel.SetActive(false);
        
        // Initial visibility check
        CheckOpenButtonVisibility();
    }

    private void OnPlayClicked()
    {
        if (_startMenuPanel != null) _startMenuPanel.SetActive(false);
        _boardController.StartGame(); // Normal Mode
        UpdateLevelText(1);
        CheckOpenButtonVisibility();
    }

    private void OnCustomGameClicked()
    {
        if (_startMenuPanel != null) _startMenuPanel.SetActive(false);
        // Start directly with default settings (Medium)
        ApplyPreset(10, 8, 5, 4, 7, 9, true); 
        
        CheckOpenButtonVisibility();
    }
    
    // Helper to control Open Panel button visibility
    private void CheckOpenButtonVisibility()
    {
         bool isCustom = _boardController.IsCustomMode;
         // Only show the Open Panel button in Custom Mode
         if (_openPanelButton != null) 
         {
             _openPanelButton.gameObject.SetActive(isCustom);
         }
    }
    
    private void OnReturnToMenuClicked()
    {
        if (_startMenuPanel != null) _startMenuPanel.SetActive(true);
        _boardController.ResetGame(); // Ensure game stops and resets
    }

    public void UpdateLevelText(int level)
    {
        if (_levelText != null) _levelText.text = $"LEVEL {level}";
    }

    private void SetPanelState(bool isOpen)
    {
        if (_inputPanel != null) _inputPanel.SetActive(isOpen);
        
        // Keep button visible but disable interaction if panel is open
        if (_openPanelButton != null) _openPanelButton.interactable = !isOpen;
        if (_closePanelButton != null) _closePanelButton.gameObject.SetActive(isOpen); // Show close button only when panel is open
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
            
            // Close panel on successful start
            SetPanelState(false);
            UpdateLevelText(0); // 0 or "Custom"
            if (_levelText != null) _levelText.text = "CUSTOM";
            
            CheckOpenButtonVisibility();
        }
        else
        {
            Debug.LogError("Invalid Input");
        }
    }
}
