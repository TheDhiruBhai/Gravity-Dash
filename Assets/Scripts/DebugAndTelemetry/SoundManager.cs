using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GravityDrift
{
    /// <summary>
    /// Singleton audio manager providing pooled pitch-varied SFX playback,
    /// looping jetpack thrust modulation, and smooth crossfading / ducked BGM.
    /// Safe when clips are unassigned (no-ops without errors until audio assets are linked in Inspector).
    /// </summary>
    public class SoundManager : MonoBehaviour
    {
        public static SoundManager Instance { get; private set; }

        [Header("Mixer & Pool Settings")]
        [Range(0f, 1f)] public float masterVolume = 1.0f;
        [Range(0f, 1f)] public float bgmVolume = 0.45f;
        [Range(0f, 1f)] public float sfxVolume = 0.85f;
        [Min(4)] public int sfxPoolSize = 14;
        [Range(0.8f, 1.0f)] public float pitchMin = 0.95f;
        [Range(1.0f, 1.2f)] public float pitchMax = 1.05f;
        [Min(0.05f)] public float bgmCrossfadeDuration = 0.65f;
        [Range(0f, 1f)] public float pauseDuckMultiplier = 0.45f;
        [Range(0f, 1f)] public float deathDuckMultiplier = 0.25f;
        public float pausedLowPassCutoffHz = 950f;

        [Header("BGM Clips")]
        [SerializeField] private AudioClip bgmMainMenu;
        [SerializeField] private AudioClip bgmGameplay;

        [Header("Player Movement & Objectives SFX")]
        [SerializeField] private AudioClip sfxJetpackThrustLoop;
        [SerializeField] private AudioClip sfxJetpackBoostBurst;
        [SerializeField] private AudioClip sfxJetpackOverload;
        [SerializeField] private AudioClip sfxGravityShiftWhoosh;
        [SerializeField] private AudioClip sfxCoreCollect;
        [SerializeField] private AudioClip sfxSwitchActivateChime;
        [SerializeField] private AudioClip sfxAirlockUnlockClunk;

        [Header("Combat, Drones & Hazards SFX")]
        [SerializeField] private AudioClip sfxDroneAlertChirp;
        [SerializeField] private AudioClip sfxDronePlasmaBlast;
        [SerializeField] private AudioClip sfxPlayerDamageGrunt;
        [SerializeField] private AudioClip sfxSuitSpark;
        [SerializeField] private AudioClip sfxHazardZap;

        [Header("Match, Roguelite & UI SFX")]
        [SerializeField] private AudioClip sfxPlayerDeath;
        [SerializeField] private AudioClip sfxLevelClearedFanfare;
        [SerializeField] private AudioClip sfxGameOverSting;
        [SerializeField] private AudioClip sfxUiButtonClick;
        [SerializeField] private AudioClip sfxUiButtonHover;

        private AudioSource[] sfxPool;
        private int sfxPoolCursor = 0;

        private AudioSource jetpackLoopSource;
        private AudioSource bgmPrimarySource;
        private AudioSource bgmSecondarySource;
        private AudioLowPassFilter bgmPrimaryLowPass;
        private AudioLowPassFilter bgmSecondaryLowPass;

        private AudioClip activeBgmClip;
        private float crossfadeT = 1f;
        private bool isPausedDuck = false;
        private bool isDeathDuck = false;
        private float targetJetpackLoopWeight = 0f;
        private float currentJetpackLoopWeight = 0f;

        private float lastHazardZapTime = -10f;
        private float lastDroneChirpTime = -10f;
        private float lastDamageGruntTime = -10f;
        private float lastUiHoverTime = -10f;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                InitializeAudioSources();
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void InitializeAudioSources()
        {
            if (sfxPool != null) return;

            sfxPool = new AudioSource[Mathf.Max(4, sfxPoolSize)];
            for (int i = 0; i < sfxPool.Length; i++)
            {
                var go = new GameObject($"SFX_Pool_{i:00}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0f;
                sfxPool[i] = src;
            }

            var jetGo = new GameObject("SFX_JetpackLoop");
            jetGo.transform.SetParent(transform, false);
            jetpackLoopSource = jetGo.AddComponent<AudioSource>();
            jetpackLoopSource.playOnAwake = false;
            jetpackLoopSource.loop = true;
            jetpackLoopSource.spatialBlend = 0f;
            jetpackLoopSource.volume = 0f;

            CreateBgmChannel("BGM_ChannelA", out bgmPrimarySource, out bgmPrimaryLowPass);
            CreateBgmChannel("BGM_ChannelB", out bgmSecondarySource, out bgmSecondaryLowPass);
        }

        private void CreateBgmChannel(string channelName, out AudioSource source, out AudioLowPassFilter lowPass)
        {
            var go = new GameObject(channelName);
            go.transform.SetParent(transform, false);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;

            lowPass = go.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = 22000f;
            lowPass.lowpassResonanceQ = 1.0f;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // Smooth crossfade + pause/death ducking & low-pass modulation on BGM
            float duckMult = isDeathDuck ? deathDuckMultiplier : (isPausedDuck ? pauseDuckMultiplier : 1.0f);
            float baseBgm = masterVolume * bgmVolume * duckMult;
            float targetCutoff = (isPausedDuck || isDeathDuck) ? pausedLowPassCutoffHz : 22000f;

            if (crossfadeT < 1f)
            {
                crossfadeT = Mathf.Min(1f, crossfadeT + dt / Mathf.Max(0.05f, bgmCrossfadeDuration));
            }

            if (bgmPrimarySource != null)
            {
                bgmPrimarySource.volume = Mathf.Lerp(0f, baseBgm, crossfadeT);
                if (bgmPrimaryLowPass != null)
                {
                    bgmPrimaryLowPass.cutoffFrequency = Mathf.Lerp(bgmPrimaryLowPass.cutoffFrequency, targetCutoff, Mathf.Clamp01(dt * 10f));
                }
            }

            if (bgmSecondarySource != null)
            {
                float fadeOutVol = Mathf.Lerp(baseBgm, 0f, crossfadeT);
                bgmSecondarySource.volume = fadeOutVol;
                if (bgmSecondaryLowPass != null)
                {
                    bgmSecondaryLowPass.cutoffFrequency = Mathf.Lerp(bgmSecondaryLowPass.cutoffFrequency, targetCutoff, Mathf.Clamp01(dt * 10f));
                }
                if (crossfadeT >= 1f && bgmSecondarySource.isPlaying)
                {
                    bgmSecondarySource.Stop();
                }
            }

            // Smoothly ramp looping jetpack thrust volume
            if (jetpackLoopSource != null)
            {
                float target = (isPausedDuck || isDeathDuck) ? 0f : targetJetpackLoopWeight;
                currentJetpackLoopWeight = Mathf.MoveTowards(currentJetpackLoopWeight, target, dt * 8f);
                if (sfxJetpackThrustLoop != null && currentJetpackLoopWeight > 0.005f)
                {
                    if (jetpackLoopSource.clip != sfxJetpackThrustLoop)
                    {
                        jetpackLoopSource.clip = sfxJetpackThrustLoop;
                    }
                    if (!jetpackLoopSource.isPlaying) jetpackLoopSource.Play();
                    jetpackLoopSource.volume = currentJetpackLoopWeight * masterVolume * sfxVolume * 0.65f;
                }
                else
                {
                    jetpackLoopSource.volume = 0f;
                    if (jetpackLoopSource.isPlaying && currentJetpackLoopWeight <= 0.001f)
                    {
                        jetpackLoopSource.Stop();
                    }
                }
            }
        }

        public void SetMasterVolume(float vol)
        {
            masterVolume = Mathf.Clamp01(vol);
            AudioListener.volume = masterVolume;
        }

        public void SetPausedDuck(bool paused)
        {
            isPausedDuck = paused;
        }

        public void SetDeathDuck(bool dead)
        {
            isDeathDuck = dead;
        }

        public void PlayMainMenuBGM() => PlayBGM(bgmMainMenu);
        public void PlayGameplayBGM() => PlayBGM(bgmGameplay);

        public void PlayBGM(AudioClip clip)
        {
            InitializeAudioSources();
            if (clip == null) return;
            if (activeBgmClip == clip && bgmPrimarySource != null && bgmPrimarySource.isPlaying) return;

            activeBgmClip = clip;

            // Swap primary -> secondary to smoothly fade out old track while fading in new track
            (bgmPrimarySource, bgmSecondarySource) = (bgmSecondarySource, bgmPrimarySource);
            (bgmPrimaryLowPass, bgmSecondaryLowPass) = (bgmSecondaryLowPass, bgmPrimaryLowPass);

            bgmPrimarySource.clip = clip;
            bgmPrimarySource.loop = true;
            bgmPrimarySource.volume = 0f;
            bgmPrimarySource.Play();
            crossfadeT = 0f;
        }

        public void SetJetpackThrustLoop(bool isThrusting, bool isBoosting)
        {
            targetJetpackLoopWeight = isThrusting ? (isBoosting ? 1.0f : 0.72f) : 0.0f;
            if (jetpackLoopSource != null)
            {
                jetpackLoopSource.pitch = isBoosting ? 1.14f : 1.0f;
            }
        }

        public void PlayJetpackBoostBurst() => PlaySFX(sfxJetpackBoostBurst, 0.9f, randomizePitch: true);
        public void PlayJetpackOverload() => PlaySFX(sfxJetpackOverload, 0.95f, randomizePitch: false);
        public void PlayGravityShift() => PlaySFX(sfxGravityShiftWhoosh, 0.9f, randomizePitch: true);
        public void PlayCoreCollect() => PlaySFX(sfxCoreCollect, 0.95f, randomizePitch: true);
        public void PlaySwitchActivate() => PlaySFX(sfxSwitchActivateChime, 0.95f, randomizePitch: false);
        public void PlayAirlockUnlock() => PlaySFX(sfxAirlockUnlockClunk, 1.0f, randomizePitch: false);

        public void PlayDroneAlertChirp()
        {
            if (Time.unscaledTime - lastDroneChirpTime < 0.35f) return;
            lastDroneChirpTime = Time.unscaledTime;
            PlaySFX(sfxDroneAlertChirp, 0.85f, randomizePitch: true);
        }

        public void PlayDronePlasmaBlast() => PlaySFX(sfxDronePlasmaBlast, 0.95f, randomizePitch: true);

        public void PlayPlayerHitAndSpark()
        {
            if (Time.unscaledTime - lastDamageGruntTime >= 0.25f)
            {
                lastDamageGruntTime = Time.unscaledTime;
                PlaySFX(sfxPlayerDamageGrunt, 0.95f, randomizePitch: true);
            }
            PlaySFX(sfxSuitSpark, 0.85f, randomizePitch: true);
        }

        public void PlayHazardZap()
        {
            if (Time.unscaledTime - lastHazardZapTime < 0.25f) return;
            lastHazardZapTime = Time.unscaledTime;
            PlaySFX(sfxHazardZap, 0.95f, randomizePitch: true);
        }

        public void PlayPlayerDeath() => PlaySFX(sfxPlayerDeath, 1.0f, randomizePitch: false);
        public void PlayLevelClearedFanfare() => PlaySFX(sfxLevelClearedFanfare, 1.0f, randomizePitch: false);
        public void PlayGameOverSting() => PlaySFX(sfxGameOverSting, 1.0f, randomizePitch: false);
        public void PlayUIClick() => PlaySFX(sfxUiButtonClick, 0.8f, randomizePitch: true);

        public void PlayUIHover()
        {
            if (Time.unscaledTime - lastUiHoverTime < 0.06f) return;
            lastUiHoverTime = Time.unscaledTime;
            PlaySFX(sfxUiButtonHover, 0.5f, randomizePitch: true);
        }

        public void PlaySFX(AudioClip clip, float volumeScale = 1.0f, bool randomizePitch = true)
        {
            if (clip == null) return;
            InitializeAudioSources();

            AudioSource chosen = null;
            for (int i = 0; i < sfxPool.Length; i++)
            {
                int idx = (sfxPoolCursor + i) % sfxPool.Length;
                if (!sfxPool[idx].isPlaying)
                {
                    chosen = sfxPool[idx];
                    sfxPoolCursor = (idx + 1) % sfxPool.Length;
                    break;
                }
            }
            if (chosen == null)
            {
                chosen = sfxPool[sfxPoolCursor];
                sfxPoolCursor = (sfxPoolCursor + 1) % sfxPool.Length;
            }

            chosen.pitch = randomizePitch ? UnityEngine.Random.Range(pitchMin, pitchMax) : 1.0f;
            chosen.volume = Mathf.Clamp01(masterVolume * sfxVolume * volumeScale);
            chosen.clip = clip;
            chosen.Play();
        }
    }
}
