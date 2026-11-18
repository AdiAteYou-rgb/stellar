using UnityEngine;

/// <summary>
/// Manages 2D gameplay background music playback, pausing during 3D arena clashes, and resuming seamlessly.
/// </summary>
public class GameplayAudioController : MonoBehaviour
{
    private GridManager grid;
    private AudioSource backgroundMusic2DSource;
    private bool music2DStarted = false;
    private bool music2DPaused = false;
    private float music2DSavedTime = 0f;

    public void Initialize(GridManager manager)
    {
        grid = manager;
    }

    private void Ensure2DBackgroundMusicReady()
    {
        if (grid == null) return;

        if (grid.backgroundMusic2D == null)
        {
            grid.backgroundMusic2D = Resources.Load<AudioClip>("Audio/Gameplay2DTheme");
        }
        if (grid.backgroundMusic2D == null) return;

        if (backgroundMusic2DSource == null)
        {
            Transform existing = transform.Find("BackgroundMusic2DSource");
            GameObject audioObj = existing != null ? existing.gameObject : new GameObject("BackgroundMusic2DSource");
            if (existing == null)
            {
                audioObj.transform.SetParent(transform, false);
            }
            backgroundMusic2DSource = audioObj.GetComponent<AudioSource>();
            if (backgroundMusic2DSource == null)
            {
                backgroundMusic2DSource = audioObj.AddComponent<AudioSource>();
            }
            backgroundMusic2DSource.playOnAwake = false;
            backgroundMusic2DSource.spatialBlend = 0f;
            backgroundMusic2DSource.priority = 0;
        }

        if (backgroundMusic2DSource.clip != grid.backgroundMusic2D)
        {
            backgroundMusic2DSource.clip = grid.backgroundMusic2D;
        }
        backgroundMusic2DSource.loop = true;
        backgroundMusic2DSource.volume = grid.backgroundMusicVolume;
    }

    public void Pause2DMusic()
    {
        if (backgroundMusic2DSource == null || !music2DStarted || music2DPaused) return;
        music2DSavedTime = backgroundMusic2DSource.time;
        backgroundMusic2DSource.Pause();
        music2DPaused = true;
    }

    public void Stop2DMusic()
    {
        if (backgroundMusic2DSource != null)
        {
            backgroundMusic2DSource.Stop();
        }
        music2DStarted = false;
        music2DPaused = false;
        music2DSavedTime = 0f;
    }

    public void Update2DBackgroundMusic(bool isIn3DArena, bool gameplayActive, MultiplayerMenuManager menu)
    {
        if (!Application.isPlaying) return;

        if (isIn3DArena)
        {
            Pause2DMusic();
            return;
        }

        bool in2DScene = gameplayActive ||
            (menu != null && (menu.currentState == MultiplayerMenuManager.MenuState.RoleSelection ||
                              menu.currentState == MultiplayerMenuManager.MenuState.Gameplay));

        if (!in2DScene)
        {
            if (backgroundMusic2DSource != null && music2DStarted)
            {
                Stop2DMusic();
            }
            return;
        }

        Ensure2DBackgroundMusicReady();
        if (backgroundMusic2DSource == null || grid == null || grid.backgroundMusic2D == null) return;

        if (!music2DStarted)
        {
            backgroundMusic2DSource.time = 0f;
            backgroundMusic2DSource.Play();
            music2DStarted = true;
            music2DPaused = false;
        }
        else if (music2DPaused)
        {
            backgroundMusic2DSource.UnPause();
            if (!backgroundMusic2DSource.isPlaying)
            {
                float maxTime = Mathf.Max(0.01f, grid.backgroundMusic2D.length - 0.02f);
                backgroundMusic2DSource.time = Mathf.Clamp(music2DSavedTime, 0f, maxTime);
                backgroundMusic2DSource.Play();
            }
            music2DPaused = false;
        }
        else if (!backgroundMusic2DSource.isPlaying)
        {
            backgroundMusic2DSource.time = 0f;
            backgroundMusic2DSource.Play();
        }
    }
}
