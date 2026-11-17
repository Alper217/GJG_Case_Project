using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Orchestrates the 10-level core loop: loads LevelConfigSO from LevelDatabaseSO,
/// configures BoardController, GoalsController, LevelTimer (Time or Moves), PowerUpManager,
/// CharacterMascotController, Audio, and Win/Lose/Settings modal flow.
/// </summary>
public class GameLoopManager : MonoBehaviour
{
    private const string CurrentLevelPrefKey = "GJG_CurrentLevelIndex";

    [Header("Level Database (Assets/Data/Levels)")]
    [SerializeField] private LevelDatabaseSO _levelDatabase;
    [Tooltip("Set >= 0 to force-start at a specific zero-based level index (e.g. 0 = Level 1); -1 uses saved progress.")]
    [SerializeField] private int _overrideStartLevelIndex = -1;

    [Header("Scene Subsystems")]
    [SerializeField] private BoardController _boardController;
    [SerializeField] private GoalsController _goalsController;
    [SerializeField] private LevelTimer _levelLimit;
    [SerializeField] private PowerUpManager _powerUpManager;
    [SerializeField] private CharacterMascotController _mascotController;
    [SerializeField] private GameAudioPlayer _audioPlayer;

    [Header("HUD & Popups")]
    [SerializeField] private TMP_Text _levelBannerText;
    [SerializeField] private Button _settingsButton;
    [SerializeField] private LevelResultPopupUI _resultPopup;

    private enum PopupMode
    {
        None,
        Win,
        Lose,
        Settings
    }

    private int _currentLevelIndex;
    private bool _levelEnded;
    private bool _pendingLimitExpiration;
    private bool _pendingWinPopup;
    private PopupMode _activePopup = PopupMode.None;

    private void Awake()
    {
        ResolveReferences();
        EnsureHudCanvasSorting();
        BindEvents();
    }

    private void Start()
    {
        if (_overrideStartLevelIndex >= 0)
        {
            _currentLevelIndex = _overrideStartLevelIndex;
        }
        else
        {
            _currentLevelIndex = PlayerPrefs.GetInt(CurrentLevelPrefKey, 0);
        }

        StartLevel(_currentLevelIndex);
    }

    private void OnDestroy()
    {
        UnbindEvents();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.PageUp))
        {
            HandleLevelStepRequested(1);
        }
        else if (Input.GetKeyDown(KeyCode.PageDown))
        {
            HandleLevelStepRequested(-1);
        }
    }

    public void StartLevel(int zeroBasedIndex)
    {
        if (_levelDatabase == null || _levelDatabase.Count == 0)
        {
            Debug.LogWarning("[GameLoopManager] LevelDatabaseSO is missing or empty.", this);
            return;
        }

        _currentLevelIndex = Mathf.Clamp(zeroBasedIndex, 0, _levelDatabase.Count - 1);
        PlayerPrefs.SetInt(CurrentLevelPrefKey, _currentLevelIndex);

        LevelConfigSO config = _levelDatabase.GetLevel(_currentLevelIndex);
        if (config == null)
        {
            return;
        }

        _levelEnded = false;
        _pendingLimitExpiration = false;
        _pendingWinPopup = false;
        _activePopup = PopupMode.None;
        _resultPopup?.Hide();

        if (_levelBannerText != null)
        {
            _levelBannerText.text = $"Level {config.LevelNumber} / Goals";
        }

        // Configure Goals & bind ProgressProvider to Mascot
        if (_goalsController != null && _boardController != null)
        {
            _goalsController.BuildGoals(config.Goals, _boardController.SpriteCatalog);
            _mascotController?.SetProgressProvider(_goalsController);
            _mascotController?.SetMoodOverride(null);
        }

        // Configure Timer or Move limit
        if (_levelLimit != null)
        {
            if (config.LimitType == LevelLimitType.Moves)
            {
                _levelLimit.ConfigureForMoves(config.MoveLimit, autoStart: true);
            }
            else
            {
                _levelLimit.ConfigureForTime(config.TimeLimitSeconds, autoStart: true);
            }
        }

        // Configure Power-ups
        if (_powerUpManager != null)
        {
            _powerUpManager.ResetChargesToDefaults();
            if (config.ExtraLimitCharges >= 0) _powerUpManager.SetCharges(PowerUpType.ExtraTime, config.ExtraLimitCharges);
            if (config.SolvePartialCharges >= 0) _powerUpManager.SetCharges(PowerUpType.SolvePartial, config.SolvePartialCharges);
            if (config.UndoCharges >= 0) _powerUpManager.SetCharges(PowerUpType.Undo, config.UndoCharges);
            if (config.BreakLockCharges >= 0) _powerUpManager.SetCharges(PowerUpType.BreakLock, config.BreakLockCharges);

            if (_boardController != null)
            {
                _powerUpManager.SetBoardHandler(_boardController);
            }
        }

        _boardController?.LoadLevel(config);
    }

    private void HandleAllGoalsCompleted()
    {
        if (_levelEnded)
        {
            return;
        }

        _pendingLimitExpiration = false;
        _levelLimit?.SetPaused(true);
        _boardController?.SetInputEnabled(false);
        _mascotController?.NotifyLevelCompleted();
        _mascotController?.SetMoodOverride(CharacterMascotController.MascotMood.Happy);
        _audioPlayer?.PlayBoosterBlast();

        if (_boardController != null && _boardController.IsResolving)
        {
            _pendingWinPopup = true;
            return;
        }

        ShowWinNow();
    }

    private void ShowWinNow()
    {
        if (_levelEnded)
        {
            return;
        }

        _levelEnded = true;
        _pendingWinPopup = false;
        bool hasNext = _levelDatabase != null && _currentLevelIndex + 1 < _levelDatabase.Count;
        int displayLevelNum = GetActiveLevelNumber();
        _activePopup = PopupMode.Win;
        _resultPopup?.ShowWin(displayLevelNum, hasNext);
    }

    private void HandleLimitExpired()
    {
        if (_levelEnded || _pendingWinPopup)
        {
            return;
        }

        if (_goalsController != null && _goalsController.IsLevelCompleted)
        {
            HandleAllGoalsCompleted();
            return;
        }

        if (_boardController != null && _boardController.IsResolving)
        {
            _pendingLimitExpiration = true;
            _boardController.SetInputEnabled(false);
            return;
        }

        TriggerLose();
    }

    private void HandleMoveResolutionCompleted()
    {
        if (_levelEnded)
        {
            return;
        }

        if (_pendingWinPopup || (_goalsController != null && _goalsController.IsLevelCompleted))
        {
            ShowWinNow();
            return;
        }

        if (!_pendingLimitExpiration)
        {
            return;
        }

        _pendingLimitExpiration = false;
        if (_levelLimit != null && _levelLimit.IsExpired)
        {
            TriggerLose();
        }
        else
        {
            _boardController?.SetInputEnabled(true);
        }
    }

    private void TriggerLose()
    {
        if (_levelEnded)
        {
            return;
        }

        _levelEnded = true;
        _boardController?.SetInputEnabled(false);
        _mascotController?.SetMoodOverride(CharacterMascotController.MascotMood.Panic);

        bool isMoves = _levelLimit != null && _levelLimit.LimitMode == LevelLimitType.Moves;
        _activePopup = PopupMode.Lose;
        _resultPopup?.ShowLose(GetActiveLevelNumber(), isMoves);
    }

    private void HandleSettingsClicked()
    {
        if (_levelEnded || _resultPopup == null)
        {
            return;
        }

        _audioPlayer?.PlayUiClick();
        if (_activePopup == PopupMode.Settings)
        {
            ResumeFromSettings();
            return;
        }

        _activePopup = PopupMode.Settings;
        _boardController?.SetInputEnabled(false);
        _levelLimit?.SetPaused(true);

        bool sfxMuted = _audioPlayer != null && _audioPlayer.IsSfxMuted;
        bool musicMuted = _audioPlayer != null && _audioPlayer.IsMusicMuted;
        int total = _levelDatabase != null ? _levelDatabase.Count : 10;
        _resultPopup.ShowSettings(GetActiveLevelNumber(), total, sfxMuted, musicMuted);
    }

    private void HandlePopupPrimaryClicked()
    {
        _audioPlayer?.PlayUiClick();
        switch (_activePopup)
        {
            case PopupMode.Win:
                int nextIdx = (_levelDatabase != null && _currentLevelIndex + 1 < _levelDatabase.Count)
                    ? _currentLevelIndex + 1
                    : 0;
                StartLevel(nextIdx);
                break;

            case PopupMode.Lose:
                StartLevel(_currentLevelIndex);
                break;

            case PopupMode.Settings:
                ResumeFromSettings();
                break;
        }
    }

    private void HandlePopupSecondaryClicked()
    {
        _audioPlayer?.PlayUiClick();
        if (_activePopup == PopupMode.Win || _activePopup == PopupMode.Settings)
        {
            StartLevel(_currentLevelIndex);
        }
    }

    private void HandleSfxToggleClicked()
    {
        if (_audioPlayer == null) return;
        bool sfxMuted = _audioPlayer.ToggleSfxMute();
        _audioPlayer.PlayUiClick();
        _resultPopup?.UpdateAudioLabels(sfxMuted, _audioPlayer.IsMusicMuted);
    }

    private void HandleMusicToggleClicked()
    {
        if (_audioPlayer == null) return;
        bool musicMuted = _audioPlayer.ToggleMusicMute();
        _audioPlayer.PlayUiClick();
        _resultPopup?.UpdateAudioLabels(_audioPlayer.IsSfxMuted, musicMuted);
    }

    private void HandleLevelStepRequested(int delta)
    {
        if (_levelDatabase == null || _levelDatabase.Count == 0)
        {
            return;
        }

        _audioPlayer?.PlayUiClick();
        int target = (_currentLevelIndex + delta + _levelDatabase.Count) % _levelDatabase.Count;
        StartLevel(target);
    }

    private void HandleBoardPowerUpInvalidated()
    {
        _powerUpManager?.RefreshAllSlots();
    }

    private void HandleTestBoostersClicked()
    {
        ResumeFromSettings();
        _boardController?.SpawnDebugTestBoosters();
    }

    private void EnsureHudCanvasSorting()
    {
        Canvas hudCanvas = _goalsController != null
            ? _goalsController.GetComponentInParent<Canvas>()
            : (_settingsButton != null ? _settingsButton.GetComponentInParent<Canvas>() : null);

        if (hudCanvas != null && hudCanvas.rootCanvas.sortingOrder < 500)
        {
            hudCanvas.rootCanvas.sortingOrder = 500;
        }
    }

    private void ResumeFromSettings()
    {
        _activePopup = PopupMode.None;
        _resultPopup?.Hide();
        if (!_levelEnded)
        {
            _boardController?.SetInputEnabled(true);
            _levelLimit?.SetPaused(false);
        }
    }

    private int GetActiveLevelNumber()
    {
        LevelConfigSO cfg = _levelDatabase != null ? _levelDatabase.GetLevel(_currentLevelIndex) : null;
        return cfg != null ? cfg.LevelNumber : (_currentLevelIndex + 1);
    }

    private void BindEvents()
    {
        if (_goalsController != null) _goalsController.OnAllGoalsCompleted += HandleAllGoalsCompleted;
        if (_levelLimit != null) _levelLimit.OnTimeExpired += HandleLimitExpired;
        if (_boardController != null)
        {
            _boardController.OnPowerUpStateInvalidated += HandleBoardPowerUpInvalidated;
            _boardController.OnMoveResolutionCompleted += HandleMoveResolutionCompleted;
        }
        if (_settingsButton != null) _settingsButton.onClick.AddListener(HandleSettingsClicked);

        if (_resultPopup != null)
        {
            _resultPopup.OnPrimaryClicked += HandlePopupPrimaryClicked;
            _resultPopup.OnSecondaryClicked += HandlePopupSecondaryClicked;
            _resultPopup.OnSfxToggleClicked += HandleSfxToggleClicked;
            _resultPopup.OnMusicToggleClicked += HandleMusicToggleClicked;
            _resultPopup.OnLevelStepRequested += HandleLevelStepRequested;
            _resultPopup.OnTestBoostersClicked += HandleTestBoostersClicked;
        }
    }

    private void UnbindEvents()
    {
        if (_goalsController != null) _goalsController.OnAllGoalsCompleted -= HandleAllGoalsCompleted;
        if (_levelLimit != null) _levelLimit.OnTimeExpired -= HandleLimitExpired;
        if (_boardController != null)
        {
            _boardController.OnPowerUpStateInvalidated -= HandleBoardPowerUpInvalidated;
            _boardController.OnMoveResolutionCompleted -= HandleMoveResolutionCompleted;
        }
        if (_settingsButton != null) _settingsButton.onClick.RemoveListener(HandleSettingsClicked);

        if (_resultPopup != null)
        {
            _resultPopup.OnPrimaryClicked -= HandlePopupPrimaryClicked;
            _resultPopup.OnSecondaryClicked -= HandlePopupSecondaryClicked;
            _resultPopup.OnSfxToggleClicked -= HandleSfxToggleClicked;
            _resultPopup.OnMusicToggleClicked -= HandleMusicToggleClicked;
            _resultPopup.OnLevelStepRequested -= HandleLevelStepRequested;
            _resultPopup.OnTestBoostersClicked -= HandleTestBoostersClicked;
        }
    }

    private void ResolveReferences()
    {
        if (_boardController == null) _boardController = FindFirstObjectByType<BoardController>();
        if (_goalsController == null) _goalsController = FindFirstObjectByType<GoalsController>();
        if (_levelLimit == null) _levelLimit = FindFirstObjectByType<LevelTimer>();
        if (_powerUpManager == null) _powerUpManager = FindFirstObjectByType<PowerUpManager>();
        if (_mascotController == null) _mascotController = FindFirstObjectByType<CharacterMascotController>();
        if (_audioPlayer == null) _audioPlayer = FindFirstObjectByType<GameAudioPlayer>();
        if (_resultPopup == null) _resultPopup = FindFirstObjectByType<LevelResultPopupUI>(FindObjectsInactive.Include);

        if (_goalsController != null && (_levelBannerText == null || _levelBannerText.gameObject.name == "LevelBanner"))
        {
            Transform textPlace = _goalsController.transform.Find("TextPlace");
            TMP_Text headerText = textPlace != null ? textPlace.GetComponentInChildren<TMP_Text>(includeInactive: true) : null;
            if (headerText != null)
            {
                if (_levelBannerText != null && _levelBannerText.gameObject.name == "LevelBanner")
                {
                    _levelBannerText.gameObject.SetActive(false);
                }

                _levelBannerText = headerText;
            }
        }

        if (_goalsController != null && _goalsController.transform.parent != null)
        {
            Transform oldBanner = _goalsController.transform.parent.Find("LevelBanner");
            if (oldBanner != null && (_levelBannerText == null || oldBanner.gameObject != _levelBannerText.gameObject))
            {
                oldBanner.gameObject.SetActive(false);
            }
        }
    }
}
