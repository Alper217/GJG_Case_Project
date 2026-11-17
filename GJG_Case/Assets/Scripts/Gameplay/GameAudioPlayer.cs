using UnityEngine;

/// <summary>
/// Manages SFX and background music playback with PlayerPrefs-persisted mute toggles.
/// </summary>
public class GameAudioPlayer : MonoBehaviour
{
    private const string SfxMutePrefKey = "GJG_SFX_Muted";
    private const string MusicMutePrefKey = "GJG_Music_Muted";

    [Header("Audio Clips (Assets/Sound&Musics)")]
    [SerializeField] private AudioClip _blockPopClip;
    [SerializeField] private AudioClip _boosterBlastClip;
    [SerializeField] private AudioClip _uiClickClip;
    [SerializeField] private AudioClip _backgroundMusicClip;

    [Header("Volumes")]
    [Range(0f, 1f)]
    [SerializeField] private float _sfxVolume = 0.75f;
    [Range(0f, 0.6f)]
    [SerializeField] private float _musicVolume = 0.16f;

    private AudioSource _sfxSource;
    private AudioSource _musicSource;

    public bool IsSfxMuted { get; private set; }
    public bool IsMusicMuted { get; private set; }

    private void Awake()
    {
        IsSfxMuted = PlayerPrefs.GetInt(SfxMutePrefKey, 0) == 1;
        IsMusicMuted = PlayerPrefs.GetInt(MusicMutePrefKey, 0) == 1;

        _sfxSource = gameObject.AddComponent<AudioSource>();
        _sfxSource.playOnAwake = false;

        _musicSource = gameObject.AddComponent<AudioSource>();
        _musicSource.playOnAwake = false;
        _musicSource.loop = true;
        _musicSource.volume = IsMusicMuted ? 0f : _musicVolume;

        if (_backgroundMusicClip != null)
        {
            _musicSource.clip = _backgroundMusicClip;
            if (!IsMusicMuted)
            {
                _musicSource.Play();
            }
        }
    }

    public void PlayBlockPop(int groupSize)
    {
        if (IsSfxMuted || _blockPopClip == null || _sfxSource == null)
        {
            return;
        }

        _sfxSource.pitch = Mathf.Clamp(0.92f + (groupSize - 2) * 0.045f, 0.9f, 1.38f);
        _sfxSource.PlayOneShot(_blockPopClip, _sfxVolume);
    }

    public void PlayBoosterBlast()
    {
        AudioClip clip = _boosterBlastClip != null ? _boosterBlastClip : _blockPopClip;
        if (IsSfxMuted || clip == null || _sfxSource == null)
        {
            return;
        }

        _sfxSource.pitch = 1.0f;
        _sfxSource.PlayOneShot(clip, Mathf.Clamp01(_sfxVolume * 1.15f));
    }

    public void PlayUiClick()
    {
        if (IsSfxMuted || _uiClickClip == null || _sfxSource == null)
        {
            return;
        }

        _sfxSource.pitch = 1.0f;
        _sfxSource.PlayOneShot(_uiClickClip, _sfxVolume * 0.85f);
    }

    public bool ToggleSfxMute()
    {
        IsSfxMuted = !IsSfxMuted;
        PlayerPrefs.SetInt(SfxMutePrefKey, IsSfxMuted ? 1 : 0);
        return IsSfxMuted;
    }

    public bool ToggleMusicMute()
    {
        IsMusicMuted = !IsMusicMuted;
        PlayerPrefs.SetInt(MusicMutePrefKey, IsMusicMuted ? 1 : 0);

        if (_musicSource != null)
        {
            _musicSource.volume = IsMusicMuted ? 0f : _musicVolume;
            if (IsMusicMuted && _musicSource.isPlaying)
            {
                _musicSource.Pause();
            }
            else if (!IsMusicMuted && _backgroundMusicClip != null && !_musicSource.isPlaying)
            {
                _musicSource.Play();
            }
        }

        return IsMusicMuted;
    }
}
