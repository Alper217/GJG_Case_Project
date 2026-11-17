using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages Win, Lose, and Settings modal popups using the scene's wood/ribbon UI styling.
/// Allows next-level progression, retry, audio toggles, and level skipping during prototyping.
/// </summary>
public class LevelResultPopupUI : MonoBehaviour
{
    [Header("Panel Elements")]
    [SerializeField] private TMP_Text _titleText;
    [SerializeField] private TMP_Text _subtitleText;

    [Header("Primary Action Button (Green)")]
    [SerializeField] private Button _primaryButton;
    [SerializeField] private TMP_Text _primaryButtonText;

    [Header("Secondary Action Button (Red)")]
    [SerializeField] private Button _secondaryButton;
    [SerializeField] private TMP_Text _secondaryButtonText;

    [Header("Settings Row (Shown in Settings Mode)")]
    [SerializeField] private GameObject _settingsSection;
    [SerializeField] private Button _sfxToggleButton;
    [SerializeField] private TMP_Text _sfxToggleText;
    [SerializeField] private Button _musicToggleButton;
    [SerializeField] private TMP_Text _musicToggleText;
    [SerializeField] private Button _prevLevelButton;
    [SerializeField] private Button _nextLevelSkipButton;

    public event Action OnPrimaryClicked;
    public event Action OnSecondaryClicked;
    public event Action OnSfxToggleClicked;
    public event Action OnMusicToggleClicked;
    public event Action<int> OnLevelStepRequested;
    public event Action OnTestBoostersClicked;
    public event Action OnClosed;

    private bool _listenersBound;
    private Button _testBoostersButton;
    private Vector2 _defaultSubtitlePos;
    private bool _subtitlePosCached;

    private void Awake()
    {
        EnsureOverlayCanvas();
        EnsureBound();
    }

    public void BindRuntimeReferences(
        TMP_Text titleText,
        TMP_Text subtitleText,
        Button primaryButton,
        TMP_Text primaryButtonText,
        Button secondaryButton,
        TMP_Text secondaryButtonText,
        GameObject settingsSection,
        Button sfxToggleButton,
        TMP_Text sfxToggleText,
        Button musicToggleButton,
        TMP_Text musicToggleText,
        Button prevLevelButton,
        Button nextLevelSkipButton)
    {
        _titleText = titleText;
        _subtitleText = subtitleText;
        _primaryButton = primaryButton;
        _primaryButtonText = primaryButtonText;
        _secondaryButton = secondaryButton;
        _secondaryButtonText = secondaryButtonText;
        _settingsSection = settingsSection;
        _sfxToggleButton = sfxToggleButton;
        _sfxToggleText = sfxToggleText;
        _musicToggleButton = musicToggleButton;
        _musicToggleText = musicToggleText;
        _prevLevelButton = prevLevelButton;
        _nextLevelSkipButton = nextLevelSkipButton;
        EnsureBound();
    }

    public void ShowWin(int levelNumber, bool hasNextLevel)
    {
        EnsureOverlayCanvas();
        EnsureBound();
        RestoreSubtitleLayout();
        if (_settingsSection != null) _settingsSection.SetActive(false);

        if (_titleText != null) _titleText.text = "TEBRIKLER!";
        if (_subtitleText != null)
        {
            _subtitleText.text = hasNextLevel
                ? $"Seviye {levelNumber} tamamlandi!"
                : $"Seviye {levelNumber} tamamlandi!\nTum prototip seviyeleri bitti!";
        }

        if (_primaryButton != null) _primaryButton.gameObject.SetActive(true);
        if (_primaryButtonText != null) _primaryButtonText.text = hasNextLevel ? "Sonraki Bolum" : "Basa Don";

        if (_secondaryButton != null) _secondaryButton.gameObject.SetActive(true);
        if (_secondaryButtonText != null) _secondaryButtonText.text = "Tekrar Oyna";

        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    public void ShowLose(int levelNumber, bool isMovesMode)
    {
        EnsureOverlayCanvas();
        EnsureBound();
        RestoreSubtitleLayout();
        if (_settingsSection != null) _settingsSection.SetActive(false);

        if (_titleText != null) _titleText.text = isMovesMode ? "HAMLE BITTI!" : "SURE DOLDU!";
        if (_subtitleText != null) _subtitleText.text = $"Seviye {levelNumber} basarisiz oldu.";

        if (_primaryButton != null) _primaryButton.gameObject.SetActive(true);
        if (_primaryButtonText != null) _primaryButtonText.text = "Tekrar Dene";

        if (_secondaryButton != null) _secondaryButton.gameObject.SetActive(false);

        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    public void ShowSettings(int currentLevelNumber, int totalLevels, bool sfxMuted, bool musicMuted)
    {
        EnsureOverlayCanvas();
        EnsureBound();
        EnsureTestBoosterRow();

        if (_subtitleText != null)
        {
            CacheSubtitleLayout();
            _subtitleText.rectTransform.anchoredPosition = new Vector2(_defaultSubtitlePos.x, 208f);
            _subtitleText.text = $"Seviye {currentLevelNumber} / {totalLevels}";
        }

        if (_settingsSection != null) _settingsSection.SetActive(true);

        if (_titleText != null) _titleText.text = "AYARLAR";

        UpdateAudioLabels(sfxMuted, musicMuted);

        if (_primaryButton != null) _primaryButton.gameObject.SetActive(true);
        if (_primaryButtonText != null) _primaryButtonText.text = "Devam Et";

        if (_secondaryButton != null) _secondaryButton.gameObject.SetActive(true);
        if (_secondaryButtonText != null) _secondaryButtonText.text = "Yeniden Baslat";

        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    public void UpdateAudioLabels(bool sfxMuted, bool musicMuted)
    {
        if (_sfxToggleText != null) _sfxToggleText.text = sfxMuted ? "Ses: Kapali" : "Ses: Acik";
        if (_musicToggleText != null) _musicToggleText.text = musicMuted ? "Muzik: Kapali" : "Muzik: Acik";
    }

    public void Hide()
    {
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
            OnClosed?.Invoke();
        }
    }

    private void EnsureOverlayCanvas()
    {
        Canvas parentCanvas = transform.parent != null ? transform.parent.GetComponentInParent<Canvas>() : null;
        if (parentCanvas != null && parentCanvas.rootCanvas.sortingOrder < 500)
        {
            parentCanvas.rootCanvas.sortingOrder = 500;
        }

        Canvas ownCanvas = GetComponent<Canvas>();
        if (ownCanvas == null)
        {
            ownCanvas = gameObject.AddComponent<Canvas>();
        }

        ownCanvas.overrideSorting = true;
        ownCanvas.sortingOrder = 2000;

        if (GetComponent<GraphicRaycaster>() == null)
        {
            gameObject.AddComponent<GraphicRaycaster>();
        }
    }

    private void CacheSubtitleLayout()
    {
        if (!_subtitlePosCached && _subtitleText != null)
        {
            _defaultSubtitlePos = _subtitleText.rectTransform.anchoredPosition;
            _subtitlePosCached = true;
        }
    }

    private void RestoreSubtitleLayout()
    {
        if (_subtitlePosCached && _subtitleText != null)
        {
            _subtitleText.rectTransform.anchoredPosition = _defaultSubtitlePos;
        }
    }

    private void EnsureTestBoosterRow()
    {
        if (_testBoostersButton != null || _settingsSection == null || _nextLevelSkipButton == null)
        {
            return;
        }

        // Slightly compact the existing 2x2 grid rows so a third full-width test button fits cleanly inside Parchment
        SetButtonRowY(_sfxToggleButton, 74f, 64f);
        SetButtonRowY(_musicToggleButton, 74f, 64f);
        SetButtonRowY(_prevLevelButton, 4f, 64f);
        SetButtonRowY(_nextLevelSkipButton, 4f, 64f);

        GameObject clone = Instantiate(_nextLevelSkipButton.gameObject, _settingsSection.transform);
        clone.name = "TestBoostersButton";
        RectTransform rt = clone.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(0f, -66f);
        rt.sizeDelta = new Vector2(520f, 66f);

        _testBoostersButton = clone.GetComponent<Button>();
        _testBoostersButton.onClick.RemoveAllListeners();
        _testBoostersButton.onClick.AddListener(() => OnTestBoostersClicked?.Invoke());

        TMP_Text label = clone.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = "Test: Roket + Bomba + Disco Ekle";
            label.fontSize = 26f;
        }
    }

    private static void SetButtonRowY(Button btn, float posY, float height)
    {
        if (btn == null) return;
        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, posY);
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
    }

    private void EnsureBound()
    {
        if (_listenersBound)
        {
            return;
        }

        _listenersBound = true;
        if (_primaryButton != null) _primaryButton.onClick.AddListener(() => OnPrimaryClicked?.Invoke());
        if (_secondaryButton != null) _secondaryButton.onClick.AddListener(() => OnSecondaryClicked?.Invoke());
        if (_sfxToggleButton != null) _sfxToggleButton.onClick.AddListener(() => OnSfxToggleClicked?.Invoke());
        if (_musicToggleButton != null) _musicToggleButton.onClick.AddListener(() => OnMusicToggleClicked?.Invoke());
        if (_prevLevelButton != null) _prevLevelButton.onClick.AddListener(() => OnLevelStepRequested?.Invoke(-1));
        if (_nextLevelSkipButton != null) _nextLevelSkipButton.onClick.AddListener(() => OnLevelStepRequested?.Invoke(1));
    }
}
