using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Audio Sources")]
    [SerializeField] private AudioSource _musicSource;
    [SerializeField] private AudioSource _sfxSource;

    [Header("Clips")]
    [SerializeField] private AudioClip _menuMusic;
    [SerializeField] private AudioClip _gameplayMusic;
    [SerializeField] private AudioClip _buttonClickSFX;
    [SerializeField] private AudioClip _popSound;

    private float _lastMusicVolume = 1f;
    private float _lastSFXVolume = 1f;

    private void Awake()
    {
        // Singleton Pattern
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        PlayMenuMusic();
    }

    public void PlayMenuMusic()
    {
        PlayMusic(_menuMusic);
    }

    public void PlayGameMusic()
    {
        PlayMusic(_gameplayMusic);
    }

    private void PlayMusic(AudioClip clip)
    {
        if (clip == null) return;
        if (_musicSource.clip == clip && _musicSource.isPlaying) return;

        _musicSource.clip = clip;
        _musicSource.loop = true;
        _musicSource.Play();
    }

    public void PlaySFX(AudioClip clip)
    {
        if (clip == null) return;
        _sfxSource.pitch = 1f; // Reset pitch
        _sfxSource.PlayOneShot(clip);
    }

    public void PlaySFX(AudioClip clip, float pitchVariation)
    {
        if (clip == null) return;
        _sfxSource.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        _sfxSource.PlayOneShot(clip);
    }

    public void PlayButtonSound()
    {
        PlaySFX(_buttonClickSFX);
    }

    public void PlayPopSound()
    {
        PlaySFX(_popSound, 0.1F);
    }

    // --- Volume Control ---

    public void SetMusicVolume(float volume)
    {
        _musicSource.volume = volume;
        _musicSource.mute = false; // Slider moved -> unmute logic usually expected
    }

    public void SetSFXVolume(float volume)
    {
        _sfxSource.volume = volume;
        _sfxSource.mute = false;
    }

    // --- Mute Toggle ---

    public bool ToggleMusicMute()
    {
        _musicSource.mute = !_musicSource.mute;
        return _musicSource.mute;
    }

    public bool ToggleSFXMute()
    {
        _sfxSource.mute = !_sfxSource.mute;
        return _sfxSource.mute;
    }

    public bool IsMusicMuted => _musicSource.mute;
    public bool IsSFXMuted => _sfxSource.mute;
}
