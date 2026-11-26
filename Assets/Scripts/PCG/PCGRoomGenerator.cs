using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GravityDrift
{
    public enum RoomArchetype
    {
        SplitBaffles = 0,
        CentralCitadel = 1,
        ZigZagGauntlet = 2,
        TwinChambers = 3,
        VerticalShafts = 4
    }

    public class PCGRoomGenerator : MonoBehaviour
    {
        public const int MaxRunLevels = 20;

        [Header("Level Progress & Seed")]
        public int currentLevelIndex = 1;
        public int currentSeed = 101;
        public bool generateOnStart = false;

        [Header("Base Dimensions (Level 1)")]
        public const int BaseRoomWidth = 20;
        public const int BaseRoomHeight = 12;

        [Header("Current Room Dimensions (Capped at 1.5x: 30x18)")]
        public int roomWidth = 20;
        public int roomHeight = 12;
        public Vector2 roomOrigin = new Vector2(-10f, -6f);

        [Header("Prefabs & Materials")]
        public GameObject floorPrefab;
        public GameObject wallPrefab;
        public GameObject cratePrefab;
        public GameObject dronePrefab;
        public GameObject hazardPrefab;

        [Header("VFX Prefabs")]
        public GameObject sparksEffectPrefab;
        public GameObject smallExplosionPrefab;
        public GameObject energyExplosionPrefab;

        public Material floorMat;
        public Material wallMat;
        public Material crateMat;
        public Material droneMat;
        public Material hazardMat;

        [Header("Camera Framing Reference")]
        public Camera mainCamera;

        private GameObject generatedRoomRoot;
        private bool isSolvable = false;
        private float lastAspect = -1f;

        private static readonly HashSet<int> usedSeeds = new HashSet<int>();
        private static readonly Queue<string> recentLayoutOrder = new Queue<string>();
        private static readonly HashSet<string> recentLayoutSet = new HashSet<string>();
        private static readonly RoomArchetype[] runArchetypeOrder =
        {
            RoomArchetype.SplitBaffles,
            RoomArchetype.CentralCitadel,
            RoomArchetype.ZigZagGauntlet,
            RoomArchetype.TwinChambers,
            RoomArchetype.VerticalShafts
        };
        private static int seedStepCounter = 0;
        private static int runSaltCounter = 0;
        private static int activeRunSalt = 0;
        private static bool archetypeOrderInitialized = false;
        private static Material cachedSparkMat;
        private static Material cachedRedHazardSparkMat;

        public string LastLayoutSignature { get; private set; } = "";

        public int LastObstacleCount { get; private set; }
        public int LastHazardCount { get; private set; }
        public int LastCrateCount { get; private set; }
        public int LastCoreCount { get; private set; }
        public int LastDroneCount { get; private set; }
        public float LastComplexityScore { get; private set; }
        public string LastArchetypeName { get; private set; } = "SplitBaffles";

        /// <summary>
        /// Generates a non-repeating 6-digit seed by mixing UTC ticks, OS tick count, GUID entropy,
        /// optional salt, and a monotonic step counter through SplitMix32 bit-avalanche hashing.
        /// </summary>
        public static int GenerateUniqueSeed(int? salt = null)
        {
            for (int attempt = 0; attempt < 64; attempt++)
            {
                seedStepCounter++;
                uint x = (uint)(Environment.TickCount ^
                                (int)(DateTime.UtcNow.Ticks & 0xFFFFFFFF) ^
                                (int)(DateTime.UtcNow.Ticks >> 32) ^
                                Guid.NewGuid().GetHashCode());
                if (salt.HasValue) x ^= (uint)salt.Value * 0x9E3779B9u;
                x += (uint)seedStepCounter * 0x85EBCA6Bu;

                x ^= x >> 16;
                x *= 0x7FEB352Du;
                x ^= x >> 15;
                x *= 0x846CA68Bu;
                x ^= x >> 16;

                int candidate = 1000 + (int)(x % 899999u);
                if (!usedSeeds.Contains(candidate))
                {
                    if (usedSeeds.Count > 10000) usedSeeds.Clear();
                    usedSeeds.Add(candidate);
                    return candidate;
                }
            }

            int fallback = 1000 + ((seedStepCounter * 7919) % 899999);
            usedSeeds.Add(fallback);
            return fallback;
        }

        /// <summary>
        /// Refreshes the active run salt and shuffles the 5-archetype rotation deck so each new run or replay
        /// experiences a fresh sequence of room archetypes, baffles, and entity layouts rather than repeating Lv1..20.
        /// </summary>
        public void BeginNewRunVariation()
        {
            runSaltCounter++;
            uint x = (uint)(Environment.TickCount ^
                            (int)(DateTime.UtcNow.Ticks & 0xFFFFFFFF) ^
                            Guid.NewGuid().GetHashCode() ^
                            (runSaltCounter * 0x9E3779B9));
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            activeRunSalt = (int)(x & 0x7FFFFFFF);

            var rng = new System.Random(activeRunSalt ^ (seedStepCounter * 48611));
            for (int i = runArchetypeOrder.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (runArchetypeOrder[i], runArchetypeOrder[j]) = (runArchetypeOrder[j], runArchetypeOrder[i]);
            }
            archetypeOrderInitialized = true;
        }

        private static void ShuffleList<T>(List<T> list, System.Random rand)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rand.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static void RecordLayoutSignature(string sig)
        {
            if (string.IsNullOrEmpty(sig) || recentLayoutSet.Contains(sig)) return;
            recentLayoutSet.Add(sig);
            recentLayoutOrder.Enqueue(sig);
            while (recentLayoutOrder.Count > 32)
            {
                string oldest = recentLayoutOrder.Dequeue();
                recentLayoutSet.Remove(oldest);
            }
        }

        private void Awake()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }
            if (!archetypeOrderInitialized) BeginNewRunVariation();
            currentSeed = GenerateUniqueSeed(currentSeed);
        }

        private void Start()
        {
            LoadResourcesIfNeeded();
            if (generateOnStart && (GameUIManager.Instance == null || GameUIManager.Instance.currentState == UIState.Gameplay))
            {
                GenerateLevel(currentLevelIndex, currentSeed);
            }
            else
            {
                // Boot cleanly into Start Menu without active drones, hazards, crates, or timers running
                TeardownActiveLevel();
            }
        }

        public void TeardownActiveLevel()
        {
            SessionReportRecorder.Instance?.ArchiveBeforeLevelChange("MenuReturn", saveImmediately: true);
            CleanPreviousLevel();

            var bootstrap = FindAnyObjectByType<GravityDriftBootstrap>();
            if (bootstrap != null)
            {
                bootstrap.ResetToMenuIdleState();
            }
        }

        private void Update()
        {
            // Keep framing aligned if window / fullscreen resolution changed
            if (mainCamera != null && Mathf.Abs(mainCamera.aspect - lastAspect) > 0.01f)
            {
                AdjustCameraFraming(roomWidth, roomHeight);
            }

            // Do not accept seed hotkeys while menus/pause freeze gameplay
            if (Time.timeScale <= 0.001f) return;

            if (InputHelper.GetKeyDown(Key.N))
            {
                currentSeed = GenerateUniqueSeed(currentSeed);
                GenerateLevel(currentLevelIndex, currentSeed);
            }
            else if (InputHelper.GetKeyDown(Key.P))
            {
                currentSeed = GenerateUniqueSeed(currentSeed ^ 0x51F3);
                GenerateLevel(currentLevelIndex, currentSeed);
            }
        }

        public void LoadResourcesIfNeeded()
        {
            if (floorPrefab == null) floorPrefab = Resources.Load<GameObject>("StationKit/station_floor_panel") ?? LoadAssetDirect("Assets/Models/StationKit/station_floor_panel.glb");
            if (wallPrefab == null) wallPrefab = Resources.Load<GameObject>("StationKit/station_wall_module") ?? LoadAssetDirect("Assets/Models/StationKit/station_wall_module.glb");
            if (cratePrefab == null) cratePrefab = Resources.Load<GameObject>("StationKit/cargo_crate") ?? LoadAssetDirect("Assets/Models/StationKit/cargo_crate.glb");
            if (dronePrefab == null) dronePrefab = Resources.Load<GameObject>("StationKit/patrol_drone") ?? LoadAssetDirect("Assets/Models/StationKit/patrol_drone.glb");
            if (hazardPrefab == null) hazardPrefab = Resources.Load<GameObject>("StationKit/hazard_pad") ?? LoadAssetDirect("Assets/Models/StationKit/hazard_pad.glb");
            if (sparksEffectPrefab == null) sparksEffectPrefab = LoadAssetDirect("Assets/UnityTechnologies/ParticlePack/EffectExamples/Misc Effects/Prefabs/SparksEffect.prefab");
            if (smallExplosionPrefab == null) smallExplosionPrefab = LoadAssetDirect("Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/SmallExplosion.prefab");
            if (energyExplosionPrefab == null) energyExplosionPrefab = LoadAssetDirect("Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/EnergyExplosion.prefab");

#if UNITY_EDITOR
            if (floorMat == null) floorMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_StationFloor.mat");
            if (wallMat == null) wallMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_StationWall.mat");
            if (crateMat == null) crateMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_CargoCrate.mat");
            if (droneMat == null) droneMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_PatrolDrone.mat");
            if (hazardMat == null) hazardMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_HazardPad.mat");
#endif
        }

        private GameObject LoadAssetDirect(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
            return null;
#endif
        }

        public void AdvanceToNextLevel()
        {
            currentLevelIndex = Mathf.Min(MaxRunLevels, currentLevelIndex + 1);
            currentSeed = GenerateUniqueSeed(currentSeed + currentLevelIndex * 97);
            GenerateLevel(currentLevelIndex, currentSeed);
        }

        public bool GenerateLevel(int levelIndex, int seed)
        {
            SessionReportRecorder.Instance?.ArchiveBeforeLevelChange("Reset_Seed", saveImmediately: true);
            LoadResourcesIfNeeded();
            levelIndex = Mathf.Clamp(levelIndex, 1, MaxRunLevels);
            currentLevelIndex = levelIndex;
            currentSeed = seed;
            usedSeeds.Add(seed);

            // 1. Progressive Level Scaling (Strictly clamped to Max 1.5x: 30x18)
            roomWidth = Mathf.Min(30, BaseRoomWidth + (levelIndex - 1) * 2);
            roomHeight = Mathf.Min(18, BaseRoomHeight + (levelIndex - 1) * 1);
            roomOrigin = new Vector2(-roomWidth * 0.5f, -roomHeight * 0.5f);

            // Sync dynamic bounds to A* and ECS
            GridAStar.SetActiveBounds(roomWidth, roomHeight, new float2(roomOrigin.x, roomOrigin.y));

            // 2. Adjust Camera Framing dynamically
            AdjustCameraFraming(roomWidth, roomHeight);

            // 3. Clean Entity Destruction
            CleanPreviousLevel();

            if (!archetypeOrderInitialized || activeRunSalt == 0)
            {
                BeginNewRunVariation();
            }

            // Pick layout parameters and avoid recently seen structural configurations across replays
            int[,] grid = new int[roomWidth, roomHeight];
            RoomArchetype archetype = RoomArchetype.SplitBaffles;
            int2 playerSpawn = new int2(2, 2);
            int2 airlockPos = new int2(roomWidth - 2, roomHeight / 2);
            int2 switchPos = new int2(roomWidth / 2, 3);
            var cratePositions = new List<int2>();
            var corePositions = new List<int2>();
            var hazardPositions = new List<int2>();
            var droneSpawns = new List<int2>();

            int cxMid = roomWidth / 2;
            int cyMid = roomHeight / 2;
            System.Random rand = null;
            string layoutSig = "";

            for (int layoutAttempt = 0; layoutAttempt < 6; layoutAttempt++)
            {
                System.Array.Clear(grid, 0, grid.Length);
                cratePositions.Clear();
                corePositions.Clear();
                hazardPositions.Clear();
                droneSpawns.Clear();

                int mixedSeed = unchecked(seed ^ (levelIndex * (int)0x9E3779B9) ^ (activeRunSalt * (int)0x85EBCA6B) ^ (layoutAttempt * (int)0xC2B2AE35));
                rand = new System.Random(mixedSeed);

                int cycleIndex = (levelIndex - 1 + layoutAttempt) % runArchetypeOrder.Length;
                archetype = runArchetypeOrder[cycleIndex];

                // Perimeter Walls & Floors
                for (int x = 0; x < roomWidth; x++)
                {
                    grid[x, 0] = 1;
                    grid[x, roomHeight - 1] = 1;
                }
                for (int y = 0; y < roomHeight; y++)
                {
                    grid[0, y] = 1;
                    grid[roomWidth - 1, y] = 1;
                }

                // Randomize spawn height band (low / mid / upper left) and right-wall airlock height
                int[] spawnYChoices = { 2, Mathf.Clamp(cyMid, 2, roomHeight - 3), Mathf.Clamp(roomHeight - 3, 2, roomHeight - 3) };
                playerSpawn = new int2(2, spawnYChoices[rand.Next(spawnYChoices.Length)]);
                airlockPos = new int2(roomWidth - 2, rand.Next(2, roomHeight - 2));

                int variantTag = 0;
                switch (archetype)
                {
                    case RoomArchetype.SplitBaffles:
                    {
                        bool invertVertical = rand.Next(2) == 1;
                        int plat1Y = invertVertical
                            ? rand.Next(Mathf.Max(5, roomHeight - 6), roomHeight - 3)
                            : rand.Next(3, Mathf.Min(6, roomHeight - 5));
                        int plat1Start = rand.Next(3, 7);
                        int plat1Len = rand.Next(4, 7);
                        for (int x = plat1Start; x < plat1Start + plat1Len && x < roomWidth - 3; x++) grid[x, plat1Y] = 1;

                        int plat2Y = invertVertical
                            ? rand.Next(3, Mathf.Min(6, plat1Y - 2))
                            : rand.Next(Mathf.Max(plat1Y + 2, roomHeight - 6), roomHeight - 3);
                        int plat2Start = rand.Next(Mathf.Max(4, cxMid - 1), Mathf.Max(cxMid + 1, roomWidth - 6));
                        int plat2Len = rand.Next(4, 7);
                        for (int x = plat2Start; x < plat2Start + plat2Len && x < roomWidth - 3; x++) grid[x, plat2Y] = 1;

                        int bX = Mathf.Clamp(rand.Next(plat1Start + plat1Len, Mathf.Max(plat1Start + plat1Len + 2, plat2Start)), 7, roomWidth - 6);
                        int bY0 = rand.Next(1, Mathf.Max(2, roomHeight - 6));
                        int bLen = rand.Next(4, Mathf.Max(5, roomHeight - 4));
                        for (int y = bY0; y < bY0 + bLen && y < roomHeight - 2; y++) grid[bX, y] = 1;

                        if (levelIndex >= 6 || rand.Next(3) == 0)
                        {
                            int b2X = Mathf.Clamp(bX + rand.Next(3, 6), bX + 3, roomWidth - 5);
                            int gapY = rand.Next(3, roomHeight - 3);
                            for (int y = 2; y < roomHeight - 2; y++)
                                if (Mathf.Abs(y - gapY) > 1) grid[b2X, y] = 1;
                        }
                        variantTag = plat1Y * 31 + bX * 7 + (invertVertical ? 1 : 0);
                        break;
                    }

                    case RoomArchetype.CentralCitadel:
                    {
                        int centerX = Mathf.Clamp(cxMid + rand.Next(-2, 3), 6, roomWidth - 7);
                        int centerY = Mathf.Clamp(cyMid + rand.Next(-1, 2), 4, roomHeight - 5);
                        int halfCoreW = (roomWidth >= 26) ? rand.Next(2, 4) : 2;
                        int halfCoreH = (roomHeight >= 15) ? rand.Next(1, 3) : 1;
                        bool hollowCenter = rand.Next(2) == 1;
                        for (int x = centerX - halfCoreW; x <= centerX + halfCoreW; x++)
                        {
                            for (int y = centerY - halfCoreH; y <= centerY + halfCoreH; y++)
                            {
                                if (hollowCenter && x == centerX && y == centerY) continue;
                                if (rand.Next(6) == 0 && (x == centerX || y == centerY)) continue; // Varied breech slits
                                grid[x, y] = 1;
                            }
                        }

                        int leftLedgeY = rand.Next(3, roomHeight - 3);
                        int leftLedgeX = rand.Next(3, Mathf.Max(4, centerX - halfCoreW - 2));
                        for (int x = leftLedgeX; x < leftLedgeX + rand.Next(3, 5) && x < roomWidth - 3; x++) grid[x, leftLedgeY] = 1;

                        int rightLedgeY = rand.Next(3, roomHeight - 3);
                        int rightLedgeX = rand.Next(Mathf.Min(roomWidth - 6, centerX + halfCoreW + 1), roomWidth - 3);
                        for (int x = rightLedgeX; x < rightLedgeX + rand.Next(3, 5) && x < roomWidth - 3; x++) grid[x, rightLedgeY] = 1;

                        variantTag = centerX * 19 + centerY * 11 + leftLedgeY * 3 + rightLedgeY;
                        break;
                    }

                    case RoomArchetype.ZigZagGauntlet:
                    {
                        int ribCount = (levelIndex >= 12) ? rand.Next(3, 5) : (levelIndex >= 5 ? rand.Next(2, 4) : 2);
                        int step = Mathf.Max(4, (roomWidth - 6) / (ribCount + 1));
                        int parity = rand.Next(2);
                        int jitterSum = 0;
                        for (int r = 0; r < ribCount; r++)
                        {
                            int rx = Mathf.Clamp(5 + r * step + rand.Next(-1, 2), 4, roomWidth - 5);
                            jitterSum += rx;
                            int clearance = rand.Next(3, 5);
                            if ((r + parity) % 2 == 0)
                            {
                                for (int y = 1; y < roomHeight - clearance; y++) grid[rx, y] = 1;
                            }
                            else
                            {
                                for (int y = clearance; y < roomHeight - 1; y++) grid[rx, y] = 1;
                            }
                        }
                        int ledgeRib = rand.Next(Mathf.Max(1, ribCount));
                        int ledgeX = Mathf.Clamp(4 + ledgeRib * step + step / 2 - 1, 3, roomWidth - 7);
                        int ledgeY = rand.Next(3, roomHeight - 3);
                        for (int x = ledgeX; x < ledgeX + rand.Next(3, 5) && x < roomWidth - 3; x++) grid[x, ledgeY] = 1;

                        variantTag = parity * 101 + ribCount * 17 + jitterSum + ledgeY;
                        break;
                    }

                    case RoomArchetype.TwinChambers:
                    {
                        int spineX = Mathf.Clamp(cxMid + rand.Next(-3, 4), 6, roomWidth - 7);
                        int lowPort = rand.Next(2, Mathf.Max(3, cyMid - 1));
                        int highPort = rand.Next(Mathf.Min(roomHeight - 3, cyMid + 2), roomHeight - 2);
                        int midPort = rand.Next(lowPort + 2, Mathf.Max(lowPort + 3, highPort));
                        for (int y = 1; y < roomHeight - 1; y++)
                        {
                            if (y == lowPort || y == midPort || y == highPort) continue;
                            grid[spineX, y] = 1;
                        }
                        int leftShelfY = rand.Next(3, roomHeight - 3);
                        int leftShelfEnd = Mathf.Max(5, spineX - rand.Next(1, 3));
                        for (int x = rand.Next(3, 5); x < leftShelfEnd; x++) grid[x, leftShelfY] = 1;

                        int rightShelfY = rand.Next(3, roomHeight - 3);
                        int rightShelfStart = Mathf.Min(roomWidth - 5, spineX + rand.Next(2, 4));
                        for (int x = rightShelfStart; x < roomWidth - rand.Next(3, 5); x++) grid[x, rightShelfY] = 1;

                        variantTag = spineX * 23 + lowPort * 7 + highPort * 5 + leftShelfY + rightShelfY;
                        break;
                    }

                    case RoomArchetype.VerticalShafts:
                    default:
                    {
                        int s1 = Mathf.Clamp(roomWidth / 3 + rand.Next(-2, 3), 5, roomWidth - 9);
                        int s2 = Mathf.Clamp((roomWidth * 2) / 3 + rand.Next(-2, 3), s1 + 4, roomWidth - 5);
                        int gap1 = rand.Next(3, roomHeight - 3);
                        int gap2A = rand.Next(2, Mathf.Max(3, cyMid));
                        int gap2B = rand.Next(Mathf.Min(roomHeight - 3, cyMid + 1), roomHeight - 2);
                        for (int y = 2; y < roomHeight - 2; y++)
                        {
                            if (Mathf.Abs(y - gap1) > 1) grid[s1, y] = 1;
                            if (y != gap2A && y != gap2B) grid[s2, y] = 1;
                        }
                        int bridgeY = rand.Next(3, roomHeight - 3);
                        for (int x = s1 + 1; x <= s2 - 1 && x < roomWidth - 3; x++) grid[x, bridgeY] = 1;

                        variantTag = s1 * 17 + s2 * 13 + gap1 * 5 + bridgeY;
                        break;
                    }
                }

                // Additional randomized security blocks (subtle even on lower levels, denser Lv 7..20)
                int extraPillars = (levelIndex >= 7) ? Mathf.Min(10, levelIndex - 5) : rand.Next(0, 3);
                int placed = 0;
                int attempts = 0;
                while (placed < extraPillars && attempts < 90)
                {
                    attempts++;
                    int px = rand.Next(3, roomWidth - 3);
                    int py = rand.Next(2, roomHeight - 2);
                    if (grid[px, py] != 0) continue;
                    if (Mathf.Abs(px - playerSpawn.x) <= 2 && Mathf.Abs(py - playerSpawn.y) <= 2) continue;
                    if (Mathf.Abs(px - airlockPos.x) <= 2 && Mathf.Abs(py - airlockPos.y) <= 2) continue;
                    grid[px, py] = 1;
                    placed++;
                }

                // Guarantee Airlock is flush against Right Wall with clear inward entry
                grid[airlockPos.x, airlockPos.y] = 0;
                if (airlockPos.x - 1 >= 1) grid[airlockPos.x - 1, airlockPos.y] = 0;

                // Lock Proximity Switch onto a randomly picked valid multi-tile platform surface
                var platformSwitchSpots = new List<int2>();
                for (int x = 2; x <= roomWidth - 4; x++)
                {
                    for (int y = 2; y <= roomHeight - 4; y++)
                    {
                        bool hasPlatformSupport = (grid[x, y] == 1) && (grid[x - 1, y] == 1 || grid[x + 1, y] == 1);
                        if (!hasPlatformSupport) continue;
                        if (grid[x, y + 1] != 0) continue;
                        if (x == playerSpawn.x && (y + 1) == playerSpawn.y) continue;
                        if (x == airlockPos.x && (y + 1) == airlockPos.y) continue;
                        platformSwitchSpots.Add(new int2(x, y + 1));
                    }
                }
                if (platformSwitchSpots.Count == 0)
                {
                    int fallbackY = Mathf.Clamp(cyMid + rand.Next(-1, 2), 3, roomHeight - 4);
                    int fallbackX = Mathf.Clamp(cxMid + rand.Next(-2, 3), 4, roomWidth - 5);
                    for (int x = fallbackX - 1; x <= fallbackX + 1; x++)
                    {
                        grid[x, fallbackY] = 1;
                    }
                    grid[fallbackX, fallbackY + 1] = 0;
                    platformSwitchSpots.Add(new int2(fallbackX, fallbackY + 1));
                }
                switchPos = platformSwitchSpots[rand.Next(platformSwitchSpots.Count)];

                layoutSig = $"{archetype}|{variantTag}|{playerSpawn.y}|{airlockPos.y}|{switchPos.x},{switchPos.y}";
                if (!recentLayoutSet.Contains(layoutSig)) break;
            }

            RecordLayoutSignature(layoutSig);
            LastLayoutSignature = layoutSig;
            LastArchetypeName = archetype.ToString();
            generatedRoomRoot = new GameObject($"--- PCG_ROOM_LVL{levelIndex}_{LastArchetypeName}_SEED{seed} ---");

            // Reserve & stamp player, airlock, and platform-mounted switch
            grid[playerSpawn.x, playerSpawn.y] = 6;
            grid[airlockPos.x, airlockPos.y] = 5;
            grid[switchPos.x, switchPos.y] = 3;

            // Escalating Crates across Levels 1..20 with randomized per-run selection
            int crateCount = (levelIndex >= 17) ? 6 : (levelIndex >= 11) ? 5 : (levelIndex >= 6) ? 4 : (levelIndex >= 3) ? 3 : 2;
            void TryPlaceItem(int tx, int ty, int code, List<int2> targetList, int maxWanted)
            {
                if (targetList.Count >= maxWanted) return;
                tx = Mathf.Clamp(tx, 2, roomWidth - 3);
                ty = Mathf.Clamp(ty, 1, roomHeight - 2);
                if (grid[tx, ty] == 0)
                {
                    grid[tx, ty] = code;
                    targetList.Add(new int2(tx, ty));
                }
            }

            var crateCandidates = new List<int2>
            {
                new int2(4 + rand.Next(-1, 2), Mathf.Clamp(cyMid + rand.Next(-1, 3), 2, roomHeight - 3)),
                new int2(cxMid + rand.Next(-2, 2), 2 + rand.Next(0, 2)),
                new int2(roomWidth - 5 - rand.Next(0, 2), Mathf.Clamp(cyMid + rand.Next(-1, 3), 2, roomHeight - 3)),
                new int2(cxMid + rand.Next(0, 4), Mathf.Clamp(roomHeight - 4 - rand.Next(0, 2), 2, roomHeight - 3)),
                new int2(5 + rand.Next(0, 3), Mathf.Clamp(roomHeight - 3, 2, roomHeight - 2)),
                new int2(roomWidth - 6 - rand.Next(0, 3), 2 + rand.Next(0, 2)),
                new int2(3 + rand.Next(0, 3), 3 + rand.Next(0, 3)),
                new int2(cxMid + rand.Next(-2, 3), cyMid + rand.Next(-2, 3))
            };
            ShuffleList(crateCandidates, rand);
            for (int c = 0; c < crateCandidates.Count && cratePositions.Count < crateCount; c++)
            {
                var cc = crateCandidates[c];
                if (Mathf.Abs(cc.x - playerSpawn.x) <= 1 && Mathf.Abs(cc.y - playerSpawn.y) <= 1) continue;
                TryPlaceItem(cc.x, cc.y, 8, cratePositions, crateCount);
            }
            int fallbackTries = 0;
            while (cratePositions.Count < crateCount && fallbackTries++ < 100)
            {
                int cx = rand.Next(3, roomWidth - 3);
                int cy = rand.Next(2, roomHeight - 2);
                if (Mathf.Abs(cx - playerSpawn.x) <= 1 && Mathf.Abs(cy - playerSpawn.y) <= 1) continue;
                TryPlaceItem(cx, cy, 8, cratePositions, crateCount);
            }

            // Escalating Energy Cores across Levels 1..20 with randomized per-run spread
            int targetCores = (levelIndex >= 16) ? 5 : (levelIndex >= 10) ? 4 : (levelIndex >= 5) ? 3 : (levelIndex >= 2) ? 2 : 1;
            var coreCandidates = new List<int2>
            {
                new int2(4 + rand.Next(0, 4), Mathf.Clamp(cyMid + rand.Next(-2, 3), 2, roomHeight - 3)),
                new int2(roomWidth - 5 - rand.Next(0, 3), Mathf.Clamp(cyMid + rand.Next(-2, 3), 2, roomHeight - 3)),
                new int2(3 + rand.Next(0, 4), Mathf.Clamp(roomHeight - 3 - rand.Next(0, 2), 2, roomHeight - 2)),
                new int2(roomWidth - 4 - rand.Next(0, 3), 2 + rand.Next(0, 3)),
                new int2(cxMid + rand.Next(-3, 4), Mathf.Clamp(roomHeight - 4, 2, roomHeight - 3)),
                new int2(cxMid + rand.Next(-4, 2), 2 + rand.Next(0, 3)),
                new int2(roomWidth - 7 - rand.Next(0, 3), 3 + rand.Next(0, 3))
            };
            ShuffleList(coreCandidates, rand);
            for (int c = 0; c < coreCandidates.Count && corePositions.Count < targetCores; c++)
            {
                var cc = coreCandidates[c];
                if (Mathf.Abs(cc.x - playerSpawn.x) <= 1 && Mathf.Abs(cc.y - playerSpawn.y) <= 1) continue;
                TryPlaceItem(cc.x, cc.y, 4, corePositions, targetCores);
            }
            fallbackTries = 0;
            while (corePositions.Count < targetCores && fallbackTries++ < 100)
            {
                int cx = rand.Next(3, roomWidth - 3);
                int cy = rand.Next(2, roomHeight - 2);
                if (Mathf.Abs(cx - playerSpawn.x) <= 1 && Mathf.Abs(cy - playerSpawn.y) <= 1) continue;
                TryPlaceItem(cx, cy, 4, corePositions, targetCores);
            }

            // Escalating Grounded Hazard Pads: strictly flush on solid floor/platform surfaces, randomized per run
            int targetHazards = Mathf.Clamp(1 + (levelIndex - 1) / 2, 1, 10);
            bool IsValidGroundedHazardCell(int hx, int hy)
            {
                if (hx < 2 || hx > roomWidth - 3 || hy < 1 || hy > roomHeight - 3) return false;
                if (grid[hx, hy] != 0 || grid[hx, hy + 1] != 0) return false;

                // Must sit directly on top of solid walkable support with no overhanging edge or air gap beneath
                if (grid[hx, hy - 1] != 1) return false;
                if (grid[hx - 1, hy - 1] != 1 || grid[hx + 1, hy - 1] != 1) return false;

                // Reject overlap/adjacency with spawn, platform switch (or its support), and right-wall airlock
                if (Mathf.Abs(hx - playerSpawn.x) <= 1 && Mathf.Abs(hy - playerSpawn.y) <= 1) return false;
                if (Mathf.Abs(hx - switchPos.x) <= 1 && Mathf.Abs(hy - switchPos.y) <= 1) return false;
                if (hx == switchPos.x && (hy - 1) == switchPos.y - 1 && hy == switchPos.y) return false;
                if (Mathf.Abs(hx - airlockPos.x) <= 1 && Mathf.Abs(hy - airlockPos.y) <= 1) return false;

                // Avoid clustering hazard pads side-by-side when spacing permits
                if (grid[hx - 1, hy] == 2 || grid[hx + 1, hy] == 2) return false;
                return true;
            }

            var groundedPool = new List<int2>();
            for (int x = 2; x <= roomWidth - 3; x++)
            {
                for (int y = 1; y <= roomHeight - 3; y++)
                {
                    if (IsValidGroundedHazardCell(x, y))
                    {
                        groundedPool.Add(new int2(x, y));
                    }
                }
            }
            ShuffleList(groundedPool, rand);
            for (int i = 0; i < groundedPool.Count && hazardPositions.Count < targetHazards; i++)
            {
                var cand = groundedPool[i];
                if (IsValidGroundedHazardCell(cand.x, cand.y))
                {
                    TryPlaceItem(cand.x, cand.y, 2, hazardPositions, targetHazards);
                }
            }

            // Escalating Drone Count across 20 Levels with randomized spawns
            int droneCount = (levelIndex >= 14) ? 5 : (levelIndex >= 7) ? 4 : (levelIndex >= 4) ? 3 : (levelIndex >= 2) ? 2 : 1;
            var droneCandidates = new List<int2>
            {
                new int2(roomWidth - 4 - rand.Next(0, 3), roomHeight - 3 - rand.Next(0, 3)),
                new int2(roomWidth - 5 - rand.Next(0, 3), 2 + rand.Next(0, 3)),
                new int2(4 + rand.Next(0, 3), roomHeight - 3 - rand.Next(0, 2)),
                new int2(cxMid + rand.Next(-2, 3), roomHeight - 4 - rand.Next(0, 2)),
                new int2(cxMid + rand.Next(-2, 4), Mathf.Clamp(cyMid + rand.Next(-2, 3), 2, roomHeight - 3)),
                new int2(roomWidth - 4, Mathf.Clamp(cyMid + rand.Next(-2, 3), 2, roomHeight - 3))
            };
            ShuffleList(droneCandidates, rand);
            for (int d = 0; d < droneCount; d++)
            {
                int dx = Mathf.Clamp(droneCandidates[d % droneCandidates.Count].x, 2, roomWidth - 3);
                int dy = Mathf.Clamp(droneCandidates[d % droneCandidates.Count].y, 2, roomHeight - 3);

                // Never overwrite switch, its supporting platform tile, airlock, walls, or player spawn
                if (grid[dx, dy] != 0 || (dx == switchPos.x && dy == switchPos.y - 1) ||
                    (Mathf.Abs(dx - playerSpawn.x) <= 2 && Mathf.Abs(dy - playerSpawn.y) <= 2))
                {
                    bool foundFree = false;
                    for (int tries = 0; tries < 80; tries++)
                    {
                        int tx = rand.Next(3, roomWidth - 3);
                        int ty = rand.Next(2, roomHeight - 2);
                        if (grid[tx, ty] == 0 && !(tx == switchPos.x && ty == switchPos.y - 1) && (Mathf.Abs(tx - playerSpawn.x) > 2 || Mathf.Abs(ty - playerSpawn.y) > 2))
                        {
                            dx = tx;
                            dy = ty;
                            foundFree = true;
                            break;
                        }
                    }
                    if (!foundFree && grid[dx, dy] == 1 && !(dx == switchPos.x && dy == switchPos.y - 1))
                    {
                        grid[dx, dy] = 0;
                    }
                }

                grid[dx, dy] = 7;
                droneSpawns.Add(new int2(dx, dy));
            }

            // 7. Guaranteed BFS Reachability Validation + Targeted Corridor Carving
            int2 switchSupport = new int2(switchPos.x, switchPos.y - 1);
            isSolvable = ValidateConnectivity(grid, playerSpawn, corePositions, airlockPos, switchPos);
            if (!isSolvable)
            {
                CarveConnectivityCorridor(grid, playerSpawn, switchPos, switchSupport);
                CarveConnectivityCorridor(grid, playerSpawn, airlockPos, switchSupport);
                for (int c = 0; c < corePositions.Count; c++)
                    CarveConnectivityCorridor(grid, playerSpawn, corePositions[c], switchSupport);
                isSolvable = ValidateConnectivity(grid, playerSpawn, corePositions, airlockPos, switchPos);
            }

            int obstacleTiles = 0;
            for (int x = 1; x < roomWidth - 1; x++)
                for (int y = 1; y < roomHeight - 1; y++)
                    if (grid[x, y] == 1) obstacleTiles++;

            LastObstacleCount = obstacleTiles;
            LastHazardCount = hazardPositions.Count;
            LastCrateCount = cratePositions.Count;
            LastCoreCount = corePositions.Count;
            LastDroneCount = droneSpawns.Count;
            LastComplexityScore = obstacleTiles * 0.45f + hazardPositions.Count * 4.5f + cratePositions.Count * 3.0f + corePositions.Count * 3.5f + droneSpawns.Count * 8.0f + (roomWidth * roomHeight) * 0.06f;

            Debug.Log($"<color=#00ff88><b>[PCG GENERATOR]</b></color> Generated Level {levelIndex}/20 [{LastArchetypeName}] (Seed #{seed}) | {roomWidth}x{roomHeight} | Obs={obstacleTiles} | Drones={droneCount} | Crates={cratePositions.Count} | Hazards={hazardPositions.Count} | Cores={corePositions.Count} | Complexity={LastComplexityScore:F1} | Solvable=<b>{isSolvable}</b>");

            // 8. Instantiate Visual Pieces
            InstantiateGridGeometry(grid, generatedRoomRoot.transform, playerSpawn, droneSpawns, switchPos, airlockPos, corePositions, cratePositions, hazardPositions);

            // 9. Position Player at spawn
            RepositionPlayer(playerSpawn);

            // 10. Sync RoomBoundsState & Re-bind ECS World
            var bootstrap = FindAnyObjectByType<GravityDriftBootstrap>();
            if (bootstrap != null)
            {
                bootstrap.SetRoomBounds(roomWidth, roomHeight, roomOrigin);
                bootstrap.RebuildLevelEntities(currentLevelIndex, currentSeed, droneCount, corePositions.Count);
            }

            return isSolvable;
        }

        private void CleanPreviousLevel()
        {
            if (generatedRoomRoot != null)
            {
                DestroyImmediate(generatedRoomRoot);
                generatedRoomRoot = null;
            }

            var oldEnv = GameObject.Find("--- ENVIRONMENT ---");
            if (oldEnv != null) DestroyImmediate(oldEnv);
            var oldCrates = GameObject.Find("--- DYNAMIC CRATES ---");
            if (oldCrates != null) DestroyImmediate(oldCrates);
            var oldRoot = GameObject.Find("--- Playable Room Environment ---");
            if (oldRoot != null) DestroyImmediate(oldRoot);
            var oldRoom = GameObject.Find("Room_Playable");
            if (oldRoom != null) DestroyImmediate(oldRoom);

            var allObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Include);
            for (int i = 0; i < allObjects.Length; i++)
            {
                var go = allObjects[i];
                if (go == null) continue;
                if (go.name.StartsWith("--- PCG_ROOM") ||
                    go.name.StartsWith("ExitAirlock") ||
                    go.name.StartsWith("EnergyCore") ||
                    go.name.StartsWith("PressureSwitch") ||
                    go.name.StartsWith("ProximitySwitch") ||
                    go.name.StartsWith("CargoCrate") ||
                    go.name.StartsWith("PatrolDrone") ||
                    go.name.StartsWith("HazardPad") ||
                    go.name.StartsWith("WallModule") ||
                    go.name.StartsWith("FloorPanel"))
                {
                    if (go.GetComponent<PlayerAuthoring>() == null &&
                        go.GetComponent<PCGRoomGenerator>() == null &&
                        go.GetComponent<Camera>() == null &&
                        go.GetComponent<Light>() == null)
                    {
                        DestroyImmediate(go);
                    }
                }
            }

            var bootstrap = FindAnyObjectByType<GravityDriftBootstrap>();
            if (bootstrap != null)
            {
                bootstrap.ClearProceduralEntities();
            }
        }

        public void AdjustCameraFraming(int width, int height)
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            lastAspect = mainCamera.aspect;
            float margin = 2.0f;
            float halfH = height * 0.5f + margin;
            float halfW = (width * 0.5f + margin) / Mathf.Max(0.5f, mainCamera.aspect);
            float targetOrtho = Mathf.Max(halfH, halfW);

            Vector3 basePos = new Vector3(0f, 0f, -22f);
            if (mainCamera.orthographic)
            {
                mainCamera.orthographicSize = targetOrtho;
            }
            else
            {
                float fovRad = mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
                float requiredDist = targetOrtho / Mathf.Tan(fovRad);
                basePos = new Vector3(0f, 0f, -requiredDist);
            }

            mainCamera.transform.position = basePos;
            var camData = mainCamera.GetComponent<UniversalAdditionalCameraData>() ?? mainCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            EnsureURPBloomVolume();

            var fb = mainCamera.GetComponent<CameraFeedbackController>();
            if (fb == null) fb = mainCamera.gameObject.AddComponent<CameraFeedbackController>();
            fb.SetBasePosition(basePos);
        }

        public static void EnsureURPBloomVolume()
        {
            var existing = GameObject.Find("GlobalBloom_Volume");
            if (existing != null) return;

            var volGo = new GameObject("GlobalBloom_Volume");
            volGo.layer = 0; // Default layer matches UniversalAdditionalCameraData.volumeLayerMask
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.enabled = true;
            vol.priority = 10f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.92f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.85f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.62f;
            bloom.clamp.overrideState = true;
            bloom.clamp.value = 6.0f;
            vol.sharedProfile = profile;
        }

        private static Material GetElectricSparkMaterial()
        {
            if (cachedSparkMat != null) return cachedSparkMat;
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Particles/Standard Unlit")
                     ?? Shader.Find("Sprites/Default");
            cachedSparkMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            cachedSparkMat.SetColor("_BaseColor", Color.white);
            cachedSparkMat.SetColor("_Color", Color.white);
            return cachedSparkMat;
        }

        private static Material GetHazardRedSparkMaterial(Material sourceTemplate)
        {
            if (cachedRedHazardSparkMat != null) return cachedRedHazardSparkMat;

            Color crimsonHdr = new Color(1f, 0.102f, 0.102f, 1f) * 3.8f; // #FF1A1A HDR bloom
            Shader unlitParticle = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                                ?? Shader.Find("Particles/Standard Unlit")
                                ?? Shader.Find("Sprites/Default");
            cachedRedHazardSparkMat = new Material(unlitParticle) { hideFlags = HideFlags.HideAndDontSave, name = "M_HazardSparksRed_Runtime" };
            if (sourceTemplate != null && sourceTemplate.HasProperty("_BaseMap") && cachedRedHazardSparkMat.HasProperty("_BaseMap"))
            {
                cachedRedHazardSparkMat.SetTexture("_BaseMap", sourceTemplate.GetTexture("_BaseMap"));
            }
            if (cachedRedHazardSparkMat.HasProperty("_BaseColor")) cachedRedHazardSparkMat.SetColor("_BaseColor", crimsonHdr);
            if (cachedRedHazardSparkMat.HasProperty("_Color")) cachedRedHazardSparkMat.SetColor("_Color", crimsonHdr);
            if (cachedRedHazardSparkMat.HasProperty("_EmissionColor"))
            {
                cachedRedHazardSparkMat.EnableKeyword("_EMISSION");
                cachedRedHazardSparkMat.SetColor("_EmissionColor", crimsonHdr);
            }
            return cachedRedHazardSparkMat;
        }

        /// <summary>
        /// Enforces SimulationSpace.Local, dense 3x-5x crimson HDR (#FF1A1A) spark emission,
        /// compact lifetimes/sizes, and small (0.01..0.1) spark stretch/trail lengths
        /// recursively across SparksEffect and every child / sub-emitter ParticleSystem.
        /// </summary>
        public static void OptimizeHazardSparksEffect(GameObject sparkRoot)
        {
            if (sparkRoot == null) return;

            Color crimsonStart = new Color(3.6f, 0.36f, 0.36f, 1f);     // #FF1A1A HDR (3.6x Bloom)
            Color crimsonCore = new Color(4.0f, 0.65f, 0.40f, 1f);      // Hot crimson-scarlet core
            var lifeColorGrad = new Gradient();
            lifeColorGrad.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(1f, 0.32f, 0.18f), 0f),
                    new GradientColorKey(new Color(1f, 0.102f, 0.102f), 0.45f),
                    new GradientColorKey(new Color(0.85f, 0.04f, 0.04f), 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.95f, 0.65f),
                    new GradientAlphaKey(0f, 1f)
                });

            var visited = new HashSet<ParticleSystem>();
            var queue = new Queue<ParticleSystem>();
            foreach (var ps in sparkRoot.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps != null && visited.Add(ps)) queue.Enqueue(ps);
            }

            while (queue.Count > 0)
            {
                var ps = queue.Dequeue();
                var sub = ps.subEmitters;
                if (sub.enabled)
                {
                    for (int s = 0; s < sub.subEmittersCount; s++)
                    {
                        var subPs = sub.GetSubEmitterSystem(s);
                        if (subPs != null && visited.Add(subPs))
                        {
                            queue.Enqueue(subPs);
                        }
                    }
                }

                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.20f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.85f, 2.25f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.085f);
                main.startColor = new ParticleSystem.MinMaxGradient(crimsonStart, crimsonCore);
                main.maxParticles = Mathf.Max(main.maxParticles, 256);

                var colOverLife = ps.colorOverLifetime;
                colOverLife.enabled = true;
                colOverLife.color = new ParticleSystem.MinMaxGradient(lifeColorGrad);

                var em = ps.emission;
                em.enabled = true;
                bool isBurstSubEmitter = ps.gameObject.name.Contains("Death");
                if (isBurstSubEmitter)
                {
                    em.rateOverTime = 0f;
                    em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 28, 42, 1, 0.22f) });
                }
                else
                {
                    float baseRate = em.rateOverTime.constantMax;
                    float boostedRate = (baseRate > 35f) ? Mathf.Clamp(baseRate * 3.5f, 240f, 340f) : Mathf.Clamp(Mathf.Max(12f, baseRate) * 4.5f, 45f, 95f);
                    em.rateOverTime = boostedRate;
                    em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 6, 12, 0, 0.24f) });
                }

                var sh = ps.shape;
                if (sh.enabled)
                {
                    if (sh.shapeType == ParticleSystemShapeType.Cone)
                    {
                        sh.angle = Mathf.Min(sh.angle, 14f);
                        sh.radius = Mathf.Clamp(sh.radius * 0.55f, 0.04f, 0.14f);
                    }
                    else if (sh.shapeType == ParticleSystemShapeType.Hemisphere || sh.shapeType == ParticleSystemShapeType.Sphere)
                    {
                        sh.radius = Mathf.Clamp(sh.radius * 0.55f, 0.04f, 0.14f);
                    }
                }

                var tr = ps.trails;
                if (tr.enabled)
                {
                    float clampedTrailLife = Mathf.Clamp(tr.lifetime.constantMax > 0.001f ? tr.lifetime.constantMax : 0.07f, 0.02f, 0.09f);
                    tr.lifetime = clampedTrailLife;
                    tr.widthOverTrail = new ParticleSystem.MinMaxCurve(0.055f);
                    tr.inheritParticleColor = true;
                    tr.colorOverLifetime = new ParticleSystem.MinMaxGradient(lifeColorGrad);
                    tr.colorOverTrail = new ParticleSystem.MinMaxGradient(lifeColorGrad);
                }

                var rend = ps.GetComponent<ParticleSystemRenderer>();
                if (rend != null)
                {
                    if (rend.renderMode == ParticleSystemRenderMode.None)
                    {
                        rend.renderMode = ParticleSystemRenderMode.Stretch;
                    }
                    rend.velocityScale = Mathf.Clamp(rend.velocityScale > 0.001f ? rend.velocityScale : 0.06f, 0.01f, 0.1f);
                    rend.lengthScale = Mathf.Clamp(rend.lengthScale > 0.001f ? rend.lengthScale : 0.08f, 0.01f, 0.1f);
                    Material redMat = GetHazardRedSparkMaterial(tr.enabled && rend.trailMaterial != null ? rend.trailMaterial : rend.sharedMaterial);
                    if (tr.enabled) rend.trailMaterial = redMat;
                    rend.sharedMaterial = redMat;
                }
            }
        }

        private void RepositionPlayer(int2 playerSpawn)
        {
            Vector3 worldSpawn = GridToWorldPos(playerSpawn.x, playerSpawn.y);
            var playerAuth = FindAnyObjectByType<PlayerAuthoring>();
            if (playerAuth != null)
            {
                playerAuth.transform.position = worldSpawn;
                playerAuth.transform.rotation = Quaternion.identity;
                var rb = playerAuth.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.useGravity = false;
                    if (!rb.isKinematic)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                }

                EnsurePlayerThrusterVFX(playerAuth.gameObject);
            }
        }

        public static void EnsurePlayerThrusterVFX(GameObject playerObj)
        {
            var existingPS = playerObj.GetComponentInChildren<ParticleSystem>();
            if (existingPS == null)
            {
                var psObj = new GameObject("JetpackThruster_VFX");
                psObj.transform.SetParent(playerObj.transform);
                psObj.transform.localPosition = new Vector3(0f, -0.4f, 0f);
                psObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                var ps = psObj.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.startLifetime = 0.35f;
                main.startSpeed = 5.0f;
                main.startSize = 0.22f;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.1f, 0.9f, 1f, 0.9f), new Color(1f, 0.5f, 0.1f, 0.8f));
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.loop = true;

                var emission = ps.emission;
                emission.rateOverTime = 35f;

                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 15f;
                shape.radius = 0.12f;

                var rend = psObj.GetComponent<ParticleSystemRenderer>();
                rend.renderMode = ParticleSystemRenderMode.Billboard;
            }
        }

        private bool ValidateConnectivity(int[,] grid, int2 start, List<int2> requiredTargets, int2 exit, int2 switchPos)
        {
            var targetsToFind = new HashSet<int2>(requiredTargets) { exit, switchPos };
            var visited = new bool[roomWidth, roomHeight];
            var queue = new Queue<int2>();

            queue.Enqueue(start);
            visited[start.x, start.y] = true;

            int2[] dirs = { new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1) };

            while (queue.Count > 0)
            {
                int2 curr = queue.Dequeue();
                if (targetsToFind.Contains(curr))
                {
                    targetsToFind.Remove(curr);
                    if (targetsToFind.Count == 0) return true;
                }

                for (int d = 0; d < 4; d++)
                {
                    int2 n = curr + dirs[d];
                    if (n.x >= 0 && n.x < roomWidth && n.y >= 0 && n.y < roomHeight)
                    {
                        if (!visited[n.x, n.y] && grid[n.x, n.y] != 1)
                        {
                            visited[n.x, n.y] = true;
                            queue.Enqueue(n);
                        }
                    }
                }
            }

            return targetsToFind.Count == 0;
        }

        private void CarveConnectivityCorridor(int[,] grid, int2 from, int2 to, int2 protectedCell)
        {
            void TryClear(int cx, int cy)
            {
                if (cx <= 0 || cx >= roomWidth - 1 || cy <= 0 || cy >= roomHeight - 1) return;
                if (cx == protectedCell.x && cy == protectedCell.y) return;
                if (grid[cx, cy] == 1) grid[cx, cy] = 0;
            }

            int x = from.x;
            int y = from.y;
            int approachX = (to.x > 2) ? (to.x - 1) : (to.x + 1);
            int stepX = (approachX >= x) ? 1 : -1;
            while (x != approachX)
            {
                TryClear(x, y);
                x += stepX;
            }
            int stepY = (to.y >= y) ? 1 : -1;
            while (y != to.y)
            {
                TryClear(x, y);
                y += stepY;
            }
            TryClear(x, y);
            TryClear(to.x, to.y);
        }

        private void InstantiateGridGeometry(int[,] grid, Transform parent, int2 playerSpawn, List<int2> droneSpawns, int2 switchPos, int2 airlockPos, List<int2> cores, List<int2> crates, List<int2> hazards)
        {
            // 1. Walls and Floors
            for (int x = 0; x < roomWidth; x++)
            {
                for (int y = 0; y < roomHeight; y++)
                {
                    Vector3 worldPos = GridToWorldPos(x, y);

                    if (grid[x, y] == 1)
                    {
                        GameObject prefab = (y == 0 || y == roomHeight - 1) ? floorPrefab : wallPrefab;
                        Material mat = (y == 0 || y == roomHeight - 1) ? floorMat : wallMat;
                        Quaternion rot = (y == 0) ? Quaternion.identity :
                                         (y == roomHeight - 1) ? Quaternion.Euler(180, 0, 0) :
                                         (x == 0) ? Quaternion.Euler(0, 0, 90) : Quaternion.identity;

                        SpawnPiece(prefab, mat, worldPos, rot, parent, new Vector3(1f, 1f, 0.25f), StationPieceType.WallModule);
                    }
                }
            }

            // 2. Grounded Hazard Pads: verify solid floor/platform collider directly underneath and snap flush onto top surface
            Physics.SyncTransforms();
            const float halfPadHeight = 0.086f;
            int hIdx = 1;
            foreach (var h in hazards)
            {
                float surfaceTopY = roomOrigin.y + h.y * 1.0f;
                Vector3 cellCenter = GridToWorldPos(h.x, h.y);
                Vector3 rayOrigin = new Vector3(cellCenter.x, cellCenter.y + 0.35f, 0f);
                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 1.35f, ~0, QueryTriggerInteraction.Ignore) &&
                    hit.collider != null)
                {
                    surfaceTopY = hit.point.y;
                }

                Vector3 padPos = new Vector3(cellCenter.x, surfaceTopY + halfPadHeight, 0f);

                var hObj = SpawnPiece(hazardPrefab, hazardMat, padPos, Quaternion.Euler(90f, 0f, 0f), parent, new Vector3(0.95f, 0.95f, 0.24f), StationPieceType.HazardPad);
                hObj.name = $"HazardPad_{hIdx++}";
                hObj.transform.localScale = new Vector3(0.94f, 0.94f, 0.72f);

                // Make Hazard Pads trigger colliders & ignite crimson HDR emission (#FF1A1A) for URP Bloom
                var hCol = hObj.GetComponent<BoxCollider>();
                if (hCol != null) hCol.isTrigger = true;

                Color crimsonRed = new Color(1f, 0.102f, 0.102f, 1f); // #FF1A1A
                var hRend = hObj.GetComponentInChildren<MeshRenderer>();
                if (hRend != null)
                {
                    var hPb = new MaterialPropertyBlock();
                    hRend.GetPropertyBlock(hPb);
                    hPb.SetTexture("_EmissionMap", Texture2D.whiteTexture);
                    hPb.SetColor("_BaseColor", crimsonRed);
                    hPb.SetColor("_Color", crimsonRed);
                    hPb.SetColor("_EmissionColor", crimsonRed * 3.4f);
                    hRend.SetPropertyBlock(hPb);
                }

                var beacon = new GameObject("HazardGlow");
                beacon.transform.SetParent(hObj.transform);
                beacon.transform.localPosition = new Vector3(0f, 0f, -0.22f);
                var glowLight = beacon.AddComponent<Light>();
                glowLight.type = LightType.Point;
                glowLight.color = crimsonRed;
                glowLight.intensity = 3.8f;
                glowLight.range = 3.0f;

                // Hazard Pad Dense Red SparksEffect (Local space, compact lifetimes/sizes, 0.01..0.1 stretch/trail)
                GameObject sparkGo;
                if (sparksEffectPrefab != null)
                {
                    sparkGo = Instantiate(sparksEffectPrefab, padPos + new Vector3(0f, 0.05f, -0.05f), Quaternion.identity, hObj.transform);
                    sparkGo.name = "HazardElectricSparks_VFX";
                    sparkGo.transform.localScale = Vector3.one;
                    OptimizeHazardSparksEffect(sparkGo);
                    var rootPs = sparkGo.GetComponent<ParticleSystem>();
                    if (rootPs != null) rootPs.Play(true);
                }
                else
                {
                    sparkGo = new GameObject("HazardElectricSparks_VFX");
                    sparkGo.transform.SetParent(hObj.transform, false);
                    sparkGo.transform.localPosition = new Vector3(0f, 0f, -0.08f);
                    sparkGo.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

                    var sparkPs = sparkGo.AddComponent<ParticleSystem>();
                    var main = sparkPs.main;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.20f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.35f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.032f);
                    main.startColor = new ParticleSystem.MinMaxGradient(crimsonRed, new Color(1f, 0.28f, 0.14f, 1f));
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    main.maxParticles = 256;
                    main.loop = true;

                    var emission = sparkPs.emission;
                    emission.rateOverTime = 95f;
                    emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 6, 12, 0, 0.24f) });

                    var shape = sparkPs.shape;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(0.56f, 0.12f, 0.04f);

                    var sRend = sparkGo.GetComponent<ParticleSystemRenderer>();
                    sRend.renderMode = ParticleSystemRenderMode.Stretch;
                    sRend.lengthScale = 0.08f;
                    sRend.velocityScale = 0.06f;
                    sRend.sharedMaterial = GetHazardRedSparkMaterial(GetElectricSparkMaterial());
                }
            }

            // 3. Dynamic Cargo Crates (2 to 3 per level - ONLY crates react to gravity)
            int crateId = 1;
            foreach (var c in crates)
            {
                Vector3 worldPos = GridToWorldPos(c.x, c.y);
                var crateObj = SpawnPiece(cratePrefab, crateMat, worldPos, Quaternion.identity, parent, new Vector3(0.8f, 0.8f, 0.8f), StationPieceType.CargoCrate);
                crateObj.name = $"CargoCrate_{crateId++}";
                var rb = crateObj.GetComponent<Rigidbody>();
                if (rb == null) rb = crateObj.AddComponent<Rigidbody>();
                rb.mass = 2.5f;
                rb.useGravity = false;
                rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
            }

            // 4. Proximity Switch (anchored onto platform surface + outward Bloom glow)
            float switchSurfaceY = roomOrigin.y + switchPos.y * 1.0f;
            Vector3 switchWorld = new Vector3(GridToWorldPos(switchPos.x, switchPos.y).x, switchSurfaceY + 0.08f, 0f);
            var switchObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            switchObj.name = "ProximitySwitch_01";
            switchObj.transform.SetParent(parent);
            switchObj.transform.position = switchWorld;
            switchObj.transform.localScale = new Vector3(0.95f, 0.09f, 0.95f);
            var switchCol = switchObj.GetComponent<Collider>();
            if (switchCol != null) switchCol.isTrigger = true;

            var swRend = switchObj.GetComponent<MeshRenderer>();
            if (swRend != null)
            {
                if (hazardMat != null) swRend.sharedMaterial = hazardMat;
                var swColor = new Color(1.0f, 0.50f, 0.12f, 1f);
                var swPb = new MaterialPropertyBlock();
                swRend.GetPropertyBlock(swPb);
                swPb.SetTexture("_EmissionMap", Texture2D.whiteTexture);
                swPb.SetColor("_BaseColor", swColor);
                swPb.SetColor("_Color", swColor);
                swPb.SetColor("_EmissionColor", swColor * 3.4f);
                swRend.SetPropertyBlock(swPb);
            }

            var swGlow = new GameObject("SwitchBloomLight");
            swGlow.transform.position = switchWorld + new Vector3(0f, 0.32f, -0.45f);
            swGlow.transform.SetParent(switchObj.transform, worldPositionStays: true);
            var swLight = swGlow.AddComponent<Light>();
            swLight.type = LightType.Point;
            swLight.color = new Color(1.0f, 0.56f, 0.14f);
            swLight.intensity = 4.2f;
            swLight.range = 3.8f;

            // 5. Energy Cores (crisp specular/rim shine + emissive highlight)
            for (int i = 0; i < cores.Count; i++)
            {
                Vector3 coreWorld = GridToWorldPos(cores[i].x, cores[i].y);
                var coreObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                coreObj.name = $"EnergyCore_{i + 1}";
                coreObj.transform.SetParent(parent);
                coreObj.transform.position = coreWorld;
                coreObj.transform.localScale = new Vector3(0.56f, 0.56f, 0.56f);
                var cRend = coreObj.GetComponent<MeshRenderer>();
                if (cRend != null)
                {
                    if (droneMat != null) cRend.sharedMaterial = droneMat;
                    var coreTint = new Color(0.32f, 0.94f, 1.0f, 1f);
                    var cPb = new MaterialPropertyBlock();
                    cRend.GetPropertyBlock(cPb);
                    cPb.SetTexture("_EmissionMap", Texture2D.whiteTexture);
                    cPb.SetColor("_BaseColor", coreTint);
                    cPb.SetColor("_Color", coreTint);
                    cPb.SetColor("_HighlightColor", Color.white);
                    cPb.SetFloat("_RimPower", 2.1f);
                    cPb.SetFloat("_RimIntensity", 1.85f);
                    cPb.SetColor("_EmissionColor", coreTint * 2.4f);
                    cRend.SetPropertyBlock(cPb);
                }
                var cGlow = new GameObject("CoreShineLight");
                cGlow.transform.position = coreWorld + new Vector3(0f, 0f, -0.35f);
                cGlow.transform.SetParent(coreObj.transform, worldPositionStays: true);
                var cLight = cGlow.AddComponent<Light>();
                cLight.type = LightType.Point;
                cLight.color = new Color(0.35f, 0.95f, 1.0f);
                cLight.intensity = 2.1f;
                cLight.range = 2.4f;

                var col = coreObj.GetComponent<SphereCollider>();
                col.isTrigger = true;
            }

            // 6. Stationary Exit Airlock Door anchored flush against the right wall with an outward room-facing trigger zone
            float rightWallInnerX = roomOrigin.x + (roomWidth - 1) * 1.0f;
            Vector3 airlockWorld = new Vector3(rightWallInnerX - 0.16f, GridToWorldPos(airlockPos.x, airlockPos.y).y, 0f);
            var airlockObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            airlockObj.name = "ExitAirlock_Door";
            airlockObj.transform.SetParent(parent);
            airlockObj.transform.position = airlockWorld;
            airlockObj.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
            airlockObj.transform.localScale = new Vector3(0.95f, 1.8f, 0.32f);

            var airlockCol = airlockObj.GetComponent<BoxCollider>();
            if (airlockCol != null)
            {
                airlockCol.isTrigger = true;
                // Offset trigger outward along local +Z (world -X into the room) so player clears smoothly without hugging wall
                airlockCol.center = new Vector3(0f, 0f, 1.85f);
                airlockCol.size = new Vector3(1.45f, 1.25f, 4.6f);
            }

            var airlockRend = airlockObj.GetComponent<MeshRenderer>();
            if (airlockRend != null)
            {
                if (wallMat != null) airlockRend.sharedMaterial = wallMat;
                var lockedRed = new Color(0.94f, 0.24f, 0.24f, 1f);
                var airlockBlock = new MaterialPropertyBlock();
                airlockRend.GetPropertyBlock(airlockBlock);
                airlockBlock.SetTexture("_EmissionMap", Texture2D.whiteTexture);
                airlockBlock.SetColor("_BaseColor", lockedRed);
                airlockBlock.SetColor("_Color", lockedRed);
                airlockBlock.SetColor("_EmissionColor", lockedRed * 1.15f);
                airlockRend.SetPropertyBlock(airlockBlock);
            }

            var airGlow = new GameObject("AirlockBeaconLight");
            airGlow.transform.position = airlockWorld + new Vector3(-0.45f, 0f, -0.45f);
            airGlow.transform.SetParent(airlockObj.transform, worldPositionStays: true);
            var airLight = airGlow.AddComponent<Light>();
            airLight.type = LightType.Point;
            airLight.color = new Color(0.94f, 0.24f, 0.24f);
            airLight.intensity = 2.6f;
            airLight.range = 3.2f;

            // 7. Patrol Drones (gravity-neutral, clamped patrol points)
            float innerMinX = roomOrigin.x + 1.5f;
            float innerMaxX = roomOrigin.x + roomWidth - 1.5f;

            for (int i = 0; i < droneSpawns.Count; i++)
            {
                Vector3 droneWorld = GridToWorldPos(droneSpawns[i].x, droneSpawns[i].y);
                var dObj = SpawnPiece(dronePrefab, droneMat, droneWorld, Quaternion.identity, parent, new Vector3(0.7f, 0.7f, 0.7f), StationPieceType.WallModule);
                dObj.name = $"PatrolDrone_{i + 1}";

                // Drones should not carry StationKitAuthoring as WallModule!
                var strayAuth = dObj.GetComponent<StationKitAuthoring>();
                if (strayAuth != null) DestroyImmediate(strayAuth);

                var dAuth = dObj.GetComponent<DroneAuthoring>();
                if (dAuth == null) dAuth = dObj.AddComponent<DroneAuthoring>();

                // Escalate patrol/pursuit speed & sensor range across Levels 1..20
                dAuth.moveSpeed = 4.2f * (1f + Mathf.Min(0.65f, (currentLevelIndex - 1) * 0.035f));
                dAuth.detectionRadius = Mathf.Min(16.0f, 9.0f + (currentLevelIndex - 1) * 0.38f);
                dAuth.loseTargetRadius = dAuth.detectionRadius + 4.0f;

                float innerMinY = roomOrigin.y + 1.5f;
                float innerMaxY = roomOrigin.y + roomHeight - 1.5f;
                if (i == 2)
                {
                    // Vertical corridor sweep
                    dAuth.patrolPointA = new Vector3(droneWorld.x, Mathf.Clamp(droneWorld.y - 3.0f, innerMinY, innerMaxY), 0f);
                    dAuth.patrolPointB = new Vector3(droneWorld.x, Mathf.Clamp(droneWorld.y + 3.0f, innerMinY, innerMaxY), 0f);
                }
                else if (i >= 3)
                {
                    // Diagonal cross-arena pressure patrol
                    dAuth.patrolPointA = new Vector3(Mathf.Clamp(droneWorld.x - 4.0f, innerMinX, innerMaxX), Mathf.Clamp(droneWorld.y - 2.5f, innerMinY, innerMaxY), 0f);
                    dAuth.patrolPointB = new Vector3(Mathf.Clamp(droneWorld.x + 4.0f, innerMinX, innerMaxX), Mathf.Clamp(droneWorld.y + 2.5f, innerMinY, innerMaxY), 0f);
                }
                else
                {
                    dAuth.patrolPointA = new Vector3(Mathf.Clamp(droneWorld.x - 3.5f, innerMinX, innerMaxX), droneWorld.y, 0f);
                    dAuth.patrolPointB = new Vector3(Mathf.Clamp(droneWorld.x + 3.5f, innerMinX, innerMaxX), droneWorld.y, 0f);
                }
                dAuth.baselineMode = false;

                var dRb = dObj.GetComponent<Rigidbody>();
                if (dRb == null) dRb = dObj.AddComponent<Rigidbody>();
                dRb.useGravity = false;
                dRb.isKinematic = true;
                dRb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;

                var trail = dObj.GetComponent<TrailRenderer>();
                if (trail == null) trail = dObj.AddComponent<TrailRenderer>();
                trail.time = 0.55f;
                trail.startWidth = 0.22f;
                trail.endWidth = 0.02f;
                trail.material = droneMat;
                trail.startColor = new Color(0.1f, 0.85f, 1.0f, 0.85f);
                trail.endColor = new Color(0.1f, 0.85f, 1.0f, 0.0f);
            }
        }

        private GameObject SpawnPiece(GameObject prefab, Material mat, Vector3 pos, Quaternion rot, Transform parent, Vector3 colSize, StationPieceType pieceType)
        {
            GameObject inst;
            if (prefab != null)
            {
                inst = Instantiate(prefab, pos, rot, parent);
            }
            else
            {
                inst = GameObject.CreatePrimitive(PrimitiveType.Cube);
                inst.transform.position = pos;
                inst.transform.rotation = rot;
                inst.transform.SetParent(parent);
            }

            if (mat != null)
            {
                foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
                {
                    mr.sharedMaterial = mat;
                }
            }

            var col = inst.GetComponent<BoxCollider>();
            if (col == null) col = inst.AddComponent<BoxCollider>();
            col.size = colSize;

            var auth = inst.AddComponent<StationKitAuthoring>();
            auth.pieceType = pieceType;
            auth.isDynamicPhysics = (pieceType == StationPieceType.CargoCrate);

            return inst;
        }

        public Vector3 GridToWorldPos(int gx, int gy)
        {
            return new Vector3(roomOrigin.x + gx * 1.0f + 0.5f, roomOrigin.y + gy * 1.0f + 0.5f, 0f);
        }

        public bool IsSolvable() => isSolvable;
    }
}
