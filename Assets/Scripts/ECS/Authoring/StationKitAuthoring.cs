using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

namespace GravityDrift
{
    public enum StationPieceType
    {
        WallModule,
        FloorPanel,
        CargoCrate,
        HazardPad
    }

    public class StationKitAuthoring : MonoBehaviour
    {
        public StationPieceType pieceType = StationPieceType.WallModule;
        public bool isDynamicPhysics = false;

        public class Baker : Baker<StationKitAuthoring>
        {
            public override void Bake(StationKitAuthoring authoring)
            {
                var entity = GetEntity(authoring.isDynamicPhysics ? TransformUsageFlags.Dynamic : TransformUsageFlags.Renderable);

                switch (authoring.pieceType)
                {
                    case StationPieceType.WallModule:
                    case StationPieceType.FloorPanel:
                        AddComponent(entity, new StationObstacleTag());
                        break;
                    case StationPieceType.CargoCrate:
                        AddComponent(entity, new CargoCrateTag());
                        AddComponent(entity, new PhysicsGravityFactor { Value = 1.0f }); // Only Cargo Crates react to global gravity!
                        AddComponent(entity, new PlanarPhysics2DTag { LockZPosition = 0f });
                        AddComponent(entity, new ResetableEntity
                        {
                            SpawnPosition = authoring.transform.position,
                            SpawnRotation = authoring.transform.rotation,
                            PendingReset = false
                        });
                        break;
                    case StationPieceType.HazardPad:
                        AddComponent(entity, new HazardPadTag
                        {
                            Position = authoring.transform.position,
                            PenaltyFuelDrain = 25f,
                            KnockbackImpulse = 7.5f
                        });
                        break;
                }
            }
        }
    }
}
