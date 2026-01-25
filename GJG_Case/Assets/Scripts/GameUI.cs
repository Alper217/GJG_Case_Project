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
    [SerializeField] private TMP_InputField _inputTime; // Duration
    [SerializeField] private TMP_InputField _inputGoals; // Goals Count
    
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

    [Header("Audio Settings")]
    [SerializeField] private Slider _musicSlider;
    [SerializeField] private Button _musicMuteButton; // Should have an Image to change icon
    [SerializeField] private Slider _sfxSlider;
    [SerializeField] private Button _sfxMuteButton;

    [Header("Settings Panel UI")]
    [SerializeField] private GameObject _settingsPanel;
    [SerializeField] private Button _openSettingsButton;
    [SerializeField] private Button _closeSettingsButton;

    [Header("Icons")]
    [SerializeField] private Sprite _soundOnSprite;
    [SerializeField] private Sprite _soundOffSprite;
    [SerializeField] private Sprite _musicOnSprite;
    [SerializeField] private Sprite _musicOffSprite;


    private void Start()
    {
        // Settings Panel Init
        if (_openSettingsButton != null) _openSettingsButton.onClick.AddListener(() => { PlayClickSound(); SetSettingsPanelState(true); });
        if (_closeSettingsButton != null) _closeSettingsButton.onClick.AddListener(() => { PlayClickSound(); SetSettingsPanelState(false); });
        SetSettingsPanelState(false); // Default closed

        // Audio Settings Bindings
        if (_musicSlider != null) 
        {
            _musicSlider.onValueChanged.AddListener((val) => 
            {
                if (AudioManager.Instance) AudioManager.Instance.SetMusicVolume(val);
                
                // MUSIC: Ses seviyesi 0 ise 'Off', >0 ise 'On' görselini ata
                UpdateVolumeButtonVisual(_musicMuteButton, val, _musicOnSprite, _musicOffSprite);
            });
            // Init value
            _musicSlider.value = 1f; 
        }

        if (_sfxSlider != null)
        {
             _sfxSlider.onValueChanged.AddListener((val) => 
            {
                if (AudioManager.Instance) AudioManager.Instance.SetSFXVolume(val);

                // SFX: Tıklama sesleri için de aynısı; 0 ise ikon değişsin
                UpdateVolumeButtonVisual(_sfxMuteButton, val, _soundOnSprite, _soundOffSprite);
            });
            _sfxSlider.value = 1f;
        }

        if (_musicMuteButton != null)
        {
            _musicMuteButton.onClick.AddListener(() => 
            {
                PlayClickSound();
                if (AudioManager.Instance) 
                {
                    bool isMuted = AudioManager.Instance.ToggleMusicMute();
                    UpdateMuteButtonVisual(_musicMuteButton, isMuted, _musicOnSprite, _musicOffSprite);
                    
                    // Mute tuşuna basınca slider değerini de görsel olarak sıfırlamak isteyebilirsiniz
                    // ancak şimdilik sadece ikon değişimi istendiği için slidera dokunmuyoruz.
                }
            });
            // Init Visual
            if (AudioManager.Instance) UpdateMuteButtonVisual(_musicMuteButton, AudioManager.Instance.IsMusicMuted, _musicOnSprite, _musicOffSprite);
        }

        if (_sfxMuteButton != null)
        {
            _sfxMuteButton.onClick.AddListener(() => 
            {
                PlayClickSound();
                if (AudioManager.Instance) 
                {
                    bool isMuted = AudioManager.Instance.ToggleSFXMute();
                    UpdateMuteButtonVisual(_sfxMuteButton, isMuted, _soundOnSprite, _soundOffSprite);
                }
            });
             // Init Visual
            if (AudioManager.Instance) UpdateMuteButtonVisual(_sfxMuteButton, AudioManager.Instance.IsSFXMuted, _soundOnSprite, _soundOffSprite);
        }
        _updateButton.onClick.AddListener(() => { PlayClickSound(); OnRestartClicked(); });
        
        if (_easyButton != null) _easyButton.onClick.AddListener(() => { PlayClickSound(); ApplyPreset(8, 6, 3, 3, 5, 7); });
        if (_mediumButton != null) _mediumButton.onClick.AddListener(() => { PlayClickSound(); ApplyPreset(10, 8, 5, 4, 7, 9); });
        if (_hardButton != null) _hardButton.onClick.AddListener(() => { PlayClickSound(); ApplyPreset(12, 9, 6, 5, 8, 10); });

        if (_extraTimeButton != null) _extraTimeButton.onClick.AddListener(() => { PlayClickSound(); _boardController.AddExtraTime(10f); });
        if (_sortButton != null) _sortButton.onClick.AddListener(() => { PlayClickSound(); _boardController.SortBoardByColor(); });
        if (_undoButton != null) _undoButton.onClick.AddListener(() => { PlayClickSound(); _boardController.UndoLastMove(); });

        // Panel Logic
        if (_openPanelButton != null) _openPanelButton.onClick.AddListener(() => { PlayClickSound(); SetPanelState(true); });
        if (_closePanelButton != null) _closePanelButton.onClick.AddListener(() => { PlayClickSound(); SetPanelState(false); });

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
        if (_playButton != null) _playButton.onClick.AddListener(() => { PlayClickSound(); OnPlayClicked(); });
        if (_customGameButton != null) _customGameButton.onClick.AddListener(() => { PlayClickSound(); OnCustomGameClicked(); });
        if (_returnToMenuButton != null) _returnToMenuButton.onClick.AddListener(() => { PlayClickSound(); OnReturnToMenuClicked(); });



        // Define Start State
        if (_startMenuPanel != null) _startMenuPanel.SetActive(true);
        if (_inputPanel != null) _inputPanel.SetActive(false);
        
        // Initial visibility check
        CheckOpenButtonVisibility();
    }

    private void OnPlayClicked()
    {
        if (_startMenuPanel != null) _startMenuPanel.SetActive(false);
        CloseAllPanels(); // Ensure clean slate
        _boardController.StartGame(); // Normal Mode
        
        if (AudioManager.Instance) AudioManager.Instance.PlayGameMusic(); // Switch Music
        
        UpdateLevelText(1);
        CheckOpenButtonVisibility();
    }

    private void OnCustomGameClicked()
    {
        if (_startMenuPanel != null) _startMenuPanel.SetActive(false);
        CloseAllPanels(); // Ensure clean slate

        if (AudioManager.Instance) AudioManager.Instance.PlayGameMusic(); // Switch Music
        
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
        CloseAllPanels(); // Ensure clean slate

        if (AudioManager.Instance) AudioManager.Instance.PlayMenuMusic(); // Back to Menu Music
        
        _boardController.ResetGame(); // Ensure game stops and resets
    }

    private void CloseAllPanels()
    {
        SetPanelState(false);
        SetSettingsPanelState(false);
    }

    public void UpdateLevelText(int level)
    {
        if (_levelText != null) _levelText.text = $"LEVEL {level}";
    }

   private void SetPanelState(bool isOpen)
    {
        if (_inputPanel != null) _inputPanel.SetActive(isOpen);
        
        // Panel açılınca buton etkileşimsiz olsun
        if (_openPanelButton != null) _openPanelButton.interactable = !isOpen;
        if (_closePanelButton != null) _closePanelButton.gameObject.SetActive(isOpen); 
        // MUTUAL EXCLUSION: If opening Dev panel, close Settings panel
        if (isOpen)
        {
            SetSettingsPanelState(false);
        }

        // PAUSE: Panel açıksa zamanı durdur (0), kapalıysa devam et (1)
        Time.timeScale = isOpen ? 0f : 1f;
    }

    private void ApplyPreset(int m, int n, int k, int a, int b, int c, bool autoRestart = true)
    {
        _inputM.text = m.ToString();
        _inputN.text = n.ToString();
        _inputK.text = k.ToString();
        _inputA.text = a.ToString();
        _inputB.text = b.ToString();
        _inputC.text = c.ToString();
        
        // Varsayılan değerler
        if (_inputTime != null) _inputTime.text = "60"; 
        if (_inputGoals != null) _inputGoals.text = Mathf.Min(k, 4).ToString();
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
            // Validasyon
            m = Mathf.Clamp(m, 2, 20);
            n = Mathf.Clamp(n, 2, 20);
            k = Mathf.Clamp(k, 1, 6);
            
            // SÜRE OKUMA (Eklendi)
            float duration = 60f;
            if (_inputTime != null && float.TryParse(_inputTime.text, out float t))
            {
                duration = Mathf.Max(10f, t);
            }
            
            // GOAL SAYISI OKUMA (Eklendi)
            int goalLimit = 6;
            if (_inputGoals != null && int.TryParse(_inputGoals.text, out int g))
            {
                goalLimit = Mathf.Clamp(g, 1, k);
            }
            // Yeni Parametrelerle Başlat
            _boardController.RestartBoard(m, n, k, a, b, c, duration, goalLimit);
            
            // Paneli kapat ve oyunu devam ettir (SetPanelState(false) içinde TimeScale 1 oluyor)
            SetPanelState(false);
            UpdateLevelText(0); 
            if (_levelText != null) _levelText.text = "CUSTOM";
            
            CheckOpenButtonVisibility();
        }
        else
        {
            Debug.LogError("Invalid Input");
        }
    }

    private void PlayClickSound()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonSound();
        }
    }

    private void SetSettingsPanelState(bool isOpen)
    {
        if (_settingsPanel != null) _settingsPanel.SetActive(isOpen);
        if (_openSettingsButton != null) _openSettingsButton.interactable = !isOpen; // Disable open button when open
        
        // AUTO-CLOSE Development Panel if Settings is opened
        if (isOpen)
        {
            SetPanelState(false);
        }
        
        // Optional: Pause time if you want settings to pause game
        // Time.timeScale = isOpen ? 0f : 1f; 
    }

    private void UpdateMuteButtonVisual(Button btn, bool isMuted, Sprite onSprite, Sprite offSprite)
    {
        if (btn == null) return;
        Image img = btn.GetComponent<Image>();
        if (img != null)
        {
            img.sprite = isMuted ? offSprite : onSprite;
        }
    }

    private void UpdateVolumeButtonVisual(Button btn, float volume, Sprite onSprite, Sprite offSprite)
    {
        if (btn == null) return;
        Image img = btn.GetComponent<Image>();
        if (img != null)
        {
            img.sprite = volume > 0 ? onSprite : offSprite;
        }
    }
}
