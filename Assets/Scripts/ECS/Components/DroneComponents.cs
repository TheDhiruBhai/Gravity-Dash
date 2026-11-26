using Unity.Entities;
using Unity.Mathematics;

namespace GravityDrift
{
    public enum DroneFSMState : byte
    {
        Patrol = 0,
        GravityShift = 1,
        Reorient = 2,
        Pursue = 3
    }

    public struct DroneAITag : IComponentData
    {
    }

    [InternalBufferCapacity(64)]
    public struct GridCell : IBufferElementData
    {
        public int2 Coordinate;
        public float3 WorldPos;
    }

    public struct DroneState : IComponentData
    {
        public int DroneID;
        public DroneFSMState CurrentState;
        public DroneFSMState PreviousState;
        public DroneActionType ActiveAction;
        public float StateTimer;
        public float MoveSpeed;
        public float DetectionRadius;
        public float LoseTargetRadius;
        public float3 TargetPosition;
        public float3 PatrolPointA;
        public float3 PatrolPointB;
        public byte CurrentPatrolIndex;
        public int CurrentPathIndex;
        public float PathReplanCooldown;
        public float StuckTimer;
        public float3 LastPosition;

        // Counterplay & Gravity-Neutral Mag-Boots
        public bool HasMagBoots;
        public bool IsUnlatched;          // Brief stabilization shock on gravity shift (hovering, no gravity fall)
        public float UnlatchedDuration;   // Stabilization/reorient vulnerability window
        public bool IsDestroyed;          // Crushed by falling crate or vaporized on hazard pad
        public bool BaselineMode;         // Baseline ignores hazard cost & crate-dodge learning

        // Cross-Level Crate-Dodge & Escape Learning
        public float TrappedByCrateTimer;
        public bool IsTrappedByCrate;
        public float CrateEscapeTime;
        public float CrateDodgeCooldown;
        public int CrateDodgesAttempted;
        public int CrateDodgesSucceeded;
        public bool PendingDodgeOutcome;
        public float PendingDodgeTimer;
        public int ReorientCountRecent;
    }

    public struct DroneTelemetry : IComponentData
    {
        public int NodesExpanded;
        public float ReplanLatencyMs;
        public bool TargetReached;
        public bool StuckOnObstacle;
        public float CurrentBeliefEntropy;
        public int HazardCellsAvoided;
    }

    public struct DroneLearningState : IComponentData
    {
        public int TotalEscapesCompleted;
        public int TotalCrateDodgesAttempted;
        public int TotalCrateDodgesSucceeded;
        public float AverageEscapeTimeSec;
        public float EscapeImpulseMultiplier;   // Learned impulse strength (0.85..2.8, can regress)
        public float TrappedDetectionThreshold; // Detection sensitivity (0.20..0.95, can regress)
        public float CrateDodgeSkill;           // 0.08..0.92 dynamic foresight for dodging incoming crates
        public int SmartPlayerAttacksObserved;  // Count of tactical player crate/boost/flip attacks
        public float DroneImprovementPct;       // Dynamic regressive AI adaptation score (2..94%)
        public int CurrentLevelIndex;
        public int ActiveDroneCount;
        public int ConsecutiveEntropyRises;
        public int AdaptationStepStreak;
        public int OutplayedEvents;
        public float RegressionMomentum;        // Negative on outplay/entropy spike, positive on counter-adaptation

        // Player Gravity & Movement Pattern Adaptation
        public int4 GravityDirCounts;           // Flip counts: x=Down, y=Up, z=Left, w=Right
        public byte LastFlipDir;
        public byte PrevFlipDir;
        public float MeanFlipIntervalSec;
        public float FlipCadenceVariance;
        public float GravityPredictability;     // 0..1 predictability of player's flip direction & cadence
        public float2 DominantMoveLaneBias;     // EMA of player normalized movement/thrust habit vector
        public float BoostHabitRatio;           // EMA of boost frequency (0..1)
        public float MovementPredictability;    // 0..1 predictability of movement/boost lane
        public float PatternAdaptationScore;    // 0..1 combined drone punish adaptation (sharpens A* / intercept / recovery)
    }
}
