using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

namespace GravityDrift.Editor
{
    public static class RoomBuilder
    {
        [MenuItem("GravityDrift/Build Test Room Scene")]
        public static void BuildScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject floorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/StationKit/station_floor_panel.glb");
            if (floorPrefab == null) floorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/StationKit/station_floor_panel.obj");

            GameObject wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/StationKit/station_wall_module.glb");
            if (wallPrefab == null) wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/StationKit/station_wall_module.obj");

            GameObject cratePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/StationKit/cargo_crate.glb");
            if (cratePrefab == null) cratePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/StationKit/cargo_crate.obj");

            GameObject dronePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/StationKit/patrol_drone.glb");
            if (dronePrefab == null) dronePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/StationKit/patrol_drone.obj");

            GameObject hazardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/StationKit/hazard_pad.glb");
            if (hazardPrefab == null) hazardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/StationKit/hazard_pad.obj");

            GameObject sparksEffectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UnityTechnologies/ParticlePack/EffectExamples/Misc Effects/Prefabs/SparksEffect.prefab");
            GameObject smallExplosionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/SmallExplosion.prefab");
            GameObject energyExplosionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/EnergyExplosion.prefab");

            Material matFloor = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_StationFloor.mat");
            Material matWall = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_StationWall.mat");
            Material matCrate = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_CargoCrate.mat");
            Material matDrone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_PatrolDrone.mat");
            Material matHazard = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_HazardPad.mat");

            // Camera
            GameObject camObj = new GameObject("Main Camera");
            camObj.tag = "MainCamera";
            Camera cam = camObj.AddComponent<Camera>();
            cam.transform.position = new Vector3(0, 0, -14.5f);
            cam.transform.rotation = Quaternion.identity;
            cam.orthographic = true;
            cam.orthographicSize = 7.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 50f;
            camObj.AddComponent<AudioListener>();
            camObj.AddComponent<CameraFeedbackController>();
            PCGRoomGenerator.EnsureURPBloomVolume();

            // Lighting
            GameObject lightObj = new GameObject("Directional Light");
            Light dirLight = lightObj.AddComponent<Light>();
            dirLight.type = LightType.Directional;
            dirLight.color = new Color(1.0f, 0.96f, 0.90f);
            dirLight.intensity = 1.3f;
            dirLight.shadows = LightShadows.Soft;
            lightObj.transform.rotation = Quaternion.Euler(38f, -42f, 0f);

            GameObject fillLightObj = new GameObject("Ambient Fill Light");
            Light fillLight = fillLightObj.AddComponent<Light>();
            fillLight.type = LightType.Point;
            fillLight.color = new Color(0.2f, 0.5f, 0.8f);
            fillLight.intensity = 2.0f;
            fillLight.range = 30f;
            fillLightObj.transform.position = new Vector3(0, 0, -5f);

            GameObject envRoot = new GameObject("--- ENVIRONMENT ---");
            GameObject cratesRoot = new GameObject("--- DYNAMIC CRATES ---");

            GameObject PlacePiece(GameObject prefab, Material mat, Vector3 pos, Quaternion rot, Transform parent, Vector3 colSize, StationPieceType pieceType)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetParent(parent);
                instance.transform.position = pos;
                instance.transform.rotation = rot;

                foreach (var mr in instance.GetComponentsInChildren<MeshRenderer>())
                {
                    mr.sharedMaterial = mat;
                }

                var col = instance.GetComponent<BoxCollider>();
                if (col == null) col = instance.AddComponent<BoxCollider>();
                col.size = colSize;

                var auth = instance.AddComponent<StationKitAuthoring>();
                auth.pieceType = pieceType;
                auth.isDynamicPhysics = (pieceType == StationPieceType.CargoCrate);

                return instance;
            }

            // Room Perimeter
            for (int x = -9; x <= 9; x++)
            {
                PlacePiece(floorPrefab, matFloor, new Vector3(x, -6f, 0f), Quaternion.identity, envRoot.transform, new Vector3(1f, 1f, 0.25f), StationPieceType.FloorPanel);
                PlacePiece(floorPrefab, matFloor, new Vector3(x, 6f, 0f), Quaternion.Euler(180f, 0f, 0f), envRoot.transform, new Vector3(1f, 1f, 0.25f), StationPieceType.FloorPanel);
            }
            for (int y = -5; y <= 5; y++)
            {
                PlacePiece(wallPrefab, matWall, new Vector3(-9.5f, y, 0f), Quaternion.Euler(0f, 0f, 90f), envRoot.transform, new Vector3(1f, 1f, 0.25f), StationPieceType.WallModule);
                PlacePiece(wallPrefab, matWall, new Vector3(9.5f, y, 0f), Quaternion.Euler(0f, 0f, -90f), envRoot.transform, new Vector3(1f, 1f, 0.25f), StationPieceType.WallModule);
            }

            // Purposeful Hazard Pads laid flat on floor lane (+Y up)
            var h1 = PlacePiece(hazardPrefab, matHazard, new Vector3(0f, -5.42f, 0f), Quaternion.Euler(90f, 0f, 0f), envRoot.transform, new Vector3(0.95f, 0.95f, 0.24f), StationPieceType.HazardPad);
            h1.transform.localScale = new Vector3(0.94f, 0.94f, 0.72f);
            h1.GetComponent<BoxCollider>().isTrigger = true;

            // Dynamic Crates
            Vector3[] cratePositions = new Vector3[]
            {
                new Vector3(-4.5f, -1.4f, 0f),
                new Vector3(4.5f, 2.1f, 0f)
            };
            for (int i = 0; i < cratePositions.Length; i++)
            {
                GameObject crateObj = PlacePiece(cratePrefab, matCrate, cratePositions[i], Quaternion.identity, cratesRoot.transform, new Vector3(0.8f, 0.8f, 0.8f), StationPieceType.CargoCrate);
                crateObj.name = $"CargoCrate_{i + 1}";
                var rb = crateObj.GetComponent<Rigidbody>();
                if (rb == null) rb = crateObj.AddComponent<Rigidbody>();
                rb.mass = 2.5f;
                rb.useGravity = false;
                rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }

            // Gravity-Neutral Player Avatar
            GameObject playerObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            playerObj.name = "Player_Avatar";
            playerObj.transform.position = new Vector3(-6.5f, -4.5f, 0f);
            playerObj.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            playerObj.GetComponent<MeshRenderer>().sharedMaterial = matFloor;
            var playerRb = playerObj.AddComponent<Rigidbody>();
            playerRb.mass = 1.0f;
            playerRb.useGravity = false;
            playerRb.isKinematic = true;
            playerRb.linearDamping = 2.0f;
            playerRb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;
            playerRb.interpolation = RigidbodyInterpolation.Interpolate;

            var playerAuth = playerObj.AddComponent<PlayerAuthoring>();
            playerAuth.moveSpeed = 10.0f;
            playerAuth.rotationSpeed = 14.0f;

            // Gravity-Neutral Drone Entity
            GameObject droneObj = (GameObject)PrefabUtility.InstantiatePrefab(dronePrefab);
            droneObj.name = "PatrolDrone_Alpha";
            droneObj.transform.position = new Vector3(4.0f, 3.5f, 0f);
            foreach (var mr in droneObj.GetComponentsInChildren<MeshRenderer>())
            {
                mr.sharedMaterial = matDrone;
            }
            var droneCol = droneObj.GetComponent<SphereCollider>();
            if (droneCol == null) droneCol = droneObj.AddComponent<SphereCollider>();
            droneCol.radius = 0.35f;

            var droneRb = droneObj.AddComponent<Rigidbody>();
            droneRb.mass = 1.5f;
            droneRb.useGravity = false;
            droneRb.isKinematic = true;
            droneRb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;

            var droneAuth = droneObj.AddComponent<DroneAuthoring>();
            droneAuth.moveSpeed = 4.2f;
            droneAuth.detectionRadius = 9.0f;
            droneAuth.loseTargetRadius = 13.0f;
            droneAuth.patrolPointA = new Vector3(6.5f, 3.5f, 0f);
            droneAuth.patrolPointB = new Vector3(-4.0f, 3.5f, 0f);

            // Managers
            GameObject managersObj = new GameObject("--- MANAGERS ---");
            var boot = managersObj.AddComponent<GravityDriftBootstrap>();
            boot.smallExplosionPrefab = smallExplosionPrefab;
            boot.energyExplosionPrefab = energyExplosionPrefab;
            managersObj.AddComponent<SoundManager>();
            managersObj.AddComponent<HapticFeedbackSystem>();
            managersObj.AddComponent<GameUIManager>();
            managersObj.AddComponent<GameHUD>();
            managersObj.AddComponent<DebugOverlay>();
            managersObj.AddComponent<BenchmarkSystem>();
            managersObj.AddComponent<SessionReportRecorder>();
            var styleMgr = managersObj.AddComponent<StyleManager>();
            styleMgr.managedMaterials = new Material[] { matFloor, matWall, matCrate, matDrone, matHazard };

            var pcg = managersObj.AddComponent<PCGRoomGenerator>();
            pcg.generateOnStart = false; // Boot cleanly into MainMenu before generating Level 1
            pcg.floorPrefab = floorPrefab;
            pcg.wallPrefab = wallPrefab;
            pcg.cratePrefab = cratePrefab;
            pcg.dronePrefab = dronePrefab;
            pcg.hazardPrefab = hazardPrefab;
            pcg.sparksEffectPrefab = sparksEffectPrefab;
            pcg.smallExplosionPrefab = smallExplosionPrefab;
            pcg.energyExplosionPrefab = energyExplosionPrefab;
            pcg.floorMat = matFloor;
            pcg.wallMat = matWall;
            pcg.crateMat = matCrate;
            pcg.droneMat = matDrone;
            pcg.hazardMat = matHazard;

            string scenePath = "Assets/Scenes/GravityDrift_Room.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            GravityDriftCanvasBuilder.BuildAndWireSceneUI();
            Debug.Log($"<color=#00ff88><b>[ROOM BUILDER] Test Room Scene saved to: {scenePath}</b></color>");

            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            bool found = false;
            foreach (var s in buildScenes)
            {
                if (s.path == scenePath) { found = true; break; }
            }
            if (!found)
            {
                buildScenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
                EditorBuildSettings.scenes = buildScenes.ToArray();
            }
        }
    }
}
