using Unity.Entities;
using Unity.Mathematics;

namespace GravityDrift
{
    public struct EnergyCoreTag : IComponentData
    {
        public int CoreID;
        public bool IsCollected;
        public float3 SpawnPosition;
    }

    public struct PressureSwitchTag : IComponentData
    {
        public int SwitchID;
        public bool IsPressed;
        public float3 SwitchPosition;
        public float ProximityRadius;
    }

    public struct ExitAirlockTag : IComponentData
    {
        public bool IsUnlocked;
        public float3 DoorPosition;
        public float3 ClosedPosition;
        public float3 OpenPosition;
        public float SlideProgress;
    }

    public struct LevelObjectiveState : IComponentData
    {
        public const int MaxRunLevels = 20;

        public int LevelIndex;
        public int TotalCores;
        public int CoresCollected;
        public int TotalSwitches;
        public int SwitchesActive;
        public bool SwitchFlipped;
        public bool AirlockUnlocked;
        public float DoorSlideProgress;
        public bool AirlockFullyOpen;
        public bool RoomCleared;
        public float RoomTimer;
        public int CurrentSeed;
        public int HazardHits;
        public float HazardWarningTimer;
        public PlayerActionType CurrentPlayerAction;

        // Roguelite Lives & 20-Level Run Progression
        public int MaxLives;
        public int LivesRemaining;
        public float LevelStartGraceTimer;
        public float RespawnGraceTimer;
        public bool IsRespawning;
        public bool PendingDeathSequence;
        public bool IsRunOver;
        public bool IsRunCompleted;
    }

    [System.Flags]
    public enum AudioCueFlags : uint
    {
        None = 0,
        GravityShift = 1 << 0,
        HazardZap = 1 << 1,
        PlayerDamage = 1 << 2,
        SuitSpark = 1 << 3,
        DroneAlertChirp = 1 << 4,
        DronePlasmaBlast = 1 << 5,
        PlayerDeath = 1 << 6
    }

    public struct AudioEventState : IComponentData
    {
        public AudioCueFlags PendingCues;
    }

    public struct PlayerCounterplayStats : IComponentData
    {
        public int DronesDestroyed;
        public int TimesTaggedByDrone;
        public int GravityFlipsUsed;
        public int EscapesObserved;
        public int SmartPlayerAttacks;
    }
}
