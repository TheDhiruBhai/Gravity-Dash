using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace GravityDrift
{
    /// <summary>
    /// Captures a cargo crate's pre-collision velocity before PhysX damps or zeroes it at impact
    /// so DroneAISystem evaluates crate crush damage using the true impact speed.
    /// </summary>
    public class CargoCrateImpactRelay : MonoBehaviour
    {
        [System.NonSerialized] public Entity CrateEntity = Entity.Null;
        [System.NonSerialized] public Vector3 PrePhysicsVelocity = Vector3.zero;
        [System.NonSerialized] public bool ImpactFrozen = false;

        private void OnCollisionEnter(Collision collision)
        {
            if (ImpactFrozen || CrateEntity == Entity.Null || collision == null || collision.collider == null) return;

            var drone = collision.collider.GetComponentInParent<DroneAuthoring>();
            if (drone == null) return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            if (!em.Exists(CrateEntity) || !em.HasComponent<CargoCrateTag>(CrateEntity)) return;

            var tag = em.GetComponentData<CargoCrateTag>(CrateEntity);
            tag.PendingImpactVelocity = (float3)PrePhysicsVelocity;
            tag.PendingImpactTargetPos = (float3)drone.transform.position;
            tag.PendingImpactTimer = 0.12f;
            tag.HasPendingImpact = true;
            em.SetComponentData(CrateEntity, tag);
        }
    }
}
