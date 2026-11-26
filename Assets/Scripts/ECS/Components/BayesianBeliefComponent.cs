using Unity.Entities;
using Unity.Mathematics;

namespace GravityDrift
{
    public enum PlayerActionType : byte
    {
        Idle = 0,
        JetpackThrust = 1,
        JetpackBoost = 2,
        GravityFlip = 3,
        ObjectivePush = 4,
        CrateAttack = 5
    }

    public enum DroneActionType : byte
    {
        Patrol = 0,
        GravityShift = 1,
        Reorient = 2,
        Pursue = 3,
        EvadeCrateDodge = 4
    }

    [InternalBufferCapacity(540)]
    public struct BeliefCell : IBufferElementData
    {
        public int2 Coordinate;
        public float Probability;          // Posterior pi_t(s_i)
        public float PredictedProbability; // Transition sum_j T(s_i | s_j, a_drone, gravity) * pi_{t-1}(s_j)
    }

    /// <summary>
    /// Explicit Bayesian Posterior pi_t(s_i) tracker coupling observation o_t,
    /// player action a^player_t, drone action a^drone_t, and gravity vector g_t.
    /// </summary>
    public struct BayesianBeliefComponent : IComponentData
    {
        public float Entropy;                  // Posterior Shannon entropy H(pi_t) in bits
        public float PriorEntropy;             // Pre-update entropy H(pi_{t-1})
        public int2 EstimatedTargetCell;       // MAP estimation argmax_i pi_t(s_i)
        public float3 EstimatedWorldPos;
        public float Confidence;               // Max cell posterior pi_t(s*)
        public float TopClusterConfidence;     // Posterior mass in 3x3 neighborhood around s* (0..1)
        public float TimeSinceLastSighting;
        public float DiffusionRate;
        public bool HasVisualContact;
        public PlayerActionType LastPlayerAction;
        public DroneActionType LastDroneAction;
        public float LikelihoodSharpness;
        public float TransitionDriftWeight;
    }
}
