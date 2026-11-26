using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace GravityDrift
{
    public static class GridAStar
    {
        public const int MaxGridWidth = 30;
        public const int MaxGridHeight = 18;
        public const float CellSize = 1.0f;

        // Dynamic active grid dimensions synced with PCGRoomGenerator / RoomBoundsState
        public static int ActiveWidth = 20;
        public static int ActiveHeight = 12;
        public static float2 ActiveOrigin = new float2(-10f, -6f);

        public static void SetActiveBounds(int width, int height, float2 origin)
        {
            ActiveWidth = math.clamp(width, 10, MaxGridWidth);
            ActiveHeight = math.clamp(height, 8, MaxGridHeight);
            ActiveOrigin = origin;
        }

        public static int2 WorldToGrid(float3 worldPos)
        {
            return WorldToGrid(worldPos, ActiveWidth, ActiveHeight, ActiveOrigin);
        }

        public static int2 WorldToGrid(float3 worldPos, int width, int height, float2 origin)
        {
            int gx = (int)math.floor((worldPos.x - origin.x) / CellSize);
            int gy = (int)math.floor((worldPos.y - origin.y) / CellSize);
            gx = math.clamp(gx, 0, width - 1);
            gy = math.clamp(gy, 0, height - 1);
            return new int2(gx, gy);
        }

        public static float3 GridToWorld(int2 gridPos, float z = 0f)
        {
            return GridToWorld(gridPos, ActiveOrigin, z);
        }

        public static float3 GridToWorld(int2 gridPos, float2 origin, float z = 0f)
        {
            float wx = origin.x + (gridPos.x + 0.5f) * CellSize;
            float wy = origin.y + (gridPos.y + 0.5f) * CellSize;
            return new float3(wx, wy, z);
        }

        public static int ToFlatIndex(int2 pos, int width)
        {
            return pos.y * width + pos.x;
        }

        public static int2 FromFlatIndex(int index, int width)
        {
            return new int2(index % width, index / width);
        }

        public struct PathResult
        {
            public bool Success;
            public int NodesExpanded;
            public float LatencyMs;
            public int HazardCellsAvoided;
        }

        public static PathResult FindPath(
            int2 startCoord,
            int2 goalCoord,
            NativeParallelHashSet<int2> obstacles,
            NativeParallelHashSet<int2> hazards,
            bool avoidHazards,
            DynamicBuffer<GridCell> pathBuffer)
        {
            return FindPath(startCoord, goalCoord, ActiveWidth, ActiveHeight, ActiveOrigin, obstacles, hazards, avoidHazards, pathBuffer);
        }

        public static PathResult FindPath(
            int2 startCoord,
            int2 goalCoord,
            int gridWidth,
            int gridHeight,
            float2 gridOrigin,
            NativeParallelHashSet<int2> obstacles,
            NativeParallelHashSet<int2> hazards,
            bool avoidHazards,
            DynamicBuffer<GridCell> pathBuffer)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            pathBuffer.Clear();

            gridWidth = math.clamp(gridWidth, 10, MaxGridWidth);
            gridHeight = math.clamp(gridHeight, 8, MaxGridHeight);
            startCoord = new int2(math.clamp(startCoord.x, 0, gridWidth - 1), math.clamp(startCoord.y, 0, gridHeight - 1));
            goalCoord = new int2(math.clamp(goalCoord.x, 0, gridWidth - 1), math.clamp(goalCoord.y, 0, gridHeight - 1));

            int totalNodes = gridWidth * gridHeight;
            var gCosts = new NativeArray<float>(totalNodes, Allocator.Temp);
            var fCosts = new NativeArray<float>(totalNodes, Allocator.Temp);
            var cameFrom = new NativeArray<int>(totalNodes, Allocator.Temp);
            var closedSet = new NativeParallelHashSet<int>(totalNodes, Allocator.Temp);
            var openSet = new NativeList<int>(Allocator.Temp);

            for (int i = 0; i < totalNodes; i++)
            {
                gCosts[i] = float.MaxValue;
                fCosts[i] = float.MaxValue;
                cameFrom[i] = -1;
            }

            int startIndex = ToFlatIndex(startCoord, gridWidth);
            int goalIndex = ToFlatIndex(goalCoord, gridWidth);

            gCosts[startIndex] = 0f;
            fCosts[startIndex] = Heuristic(startCoord, goalCoord);
            openSet.Add(startIndex);

            int nodesExpanded = 0;
            int hazardCellsAvoided = 0;
            bool reached = false;

            int2[] directions = new int2[]
            {
                new int2(1, 0),
                new int2(-1, 0),
                new int2(0, 1),
                new int2(0, -1),
                new int2(1, 1),
                new int2(-1, 1),
                new int2(1, -1),
                new int2(-1, -1)
            };

            while (openSet.Length > 0)
            {
                int lowestIndexInList = 0;
                float lowestF = fCosts[openSet[0]];
                for (int i = 1; i < openSet.Length; i++)
                {
                    float f = fCosts[openSet[i]];
                    if (f < lowestF)
                    {
                        lowestF = f;
                        lowestIndexInList = i;
                    }
                }

                int currentIndex = openSet[lowestIndexInList];
                int2 currentCoord = FromFlatIndex(currentIndex, gridWidth);

                if (currentIndex == goalIndex)
                {
                    reached = true;
                    break;
                }

                openSet.RemoveAtSwapBack(lowestIndexInList);
                closedSet.Add(currentIndex);
                nodesExpanded++;

                for (int d = 0; d < 8; d++)
                {
                    int2 neighborCoord = currentCoord + directions[d];
                    if (neighborCoord.x < 1 || neighborCoord.x >= gridWidth - 1 ||
                        neighborCoord.y < 1 || neighborCoord.y >= gridHeight - 1)
                    {
                        // Keep path strictly inside perimeter inner room walls
                        continue;
                    }

                    int neighborIndex = ToFlatIndex(neighborCoord, gridWidth);
                    if (closedSet.Contains(neighborIndex))
                    {
                        continue;
                    }

                    // Solid obstacle check (interior walls + crates)
                    if (neighborIndex != goalIndex && obstacles.Contains(neighborCoord))
                    {
                        continue;
                    }

                    // Hazard Pad Cost:
                    // Smart / Bayesian drones assign high traversal penalty (+24.0f cost) to hazard pads so they route cleanly around them,
                    // while Baseline drones assign 0 penalty and ignore hazards!
                    float hazardWeight = 0f;
                    if (hazards.Contains(neighborCoord))
                    {
                        if (avoidHazards)
                        {
                            hazardWeight = 24.0f;
                            hazardCellsAvoided++;
                        }
                        else
                        {
                            hazardWeight = 0f;
                        }
                    }

                    // Diagonal corner cutting check
                    if (d >= 4)
                    {
                        int2 corner1 = new int2(currentCoord.x, neighborCoord.y);
                        int2 corner2 = new int2(neighborCoord.x, currentCoord.y);
                        if (obstacles.Contains(corner1) || obstacles.Contains(corner2))
                        {
                            continue;
                        }
                        if (avoidHazards && (hazards.Contains(corner1) || hazards.Contains(corner2)))
                        {
                            hazardWeight += 12.0f;
                        }
                    }

                    float stepCost = ((d < 4) ? 1.0f : 1.414f) + hazardWeight;
                    float tentativeG = gCosts[currentIndex] + stepCost;

                    if (tentativeG < gCosts[neighborIndex])
                    {
                        cameFrom[neighborIndex] = currentIndex;
                        gCosts[neighborIndex] = tentativeG;
                        fCosts[neighborIndex] = tentativeG + Heuristic(neighborCoord, goalCoord);

                        if (!openSet.Contains(neighborIndex))
                        {
                            openSet.Add(neighborIndex);
                        }
                    }
                }
            }

            if (reached)
            {
                var tempPath = new List<int2>();
                int curr = goalIndex;
                while (curr != -1)
                {
                    tempPath.Add(FromFlatIndex(curr, gridWidth));
                    curr = cameFrom[curr];
                }

                for (int i = tempPath.Count - 1; i >= 0; i--)
                {
                    int2 pt = tempPath[i];
                    pathBuffer.Add(new GridCell
                    {
                        Coordinate = pt,
                        WorldPos = GridToWorld(pt, gridOrigin, 0f)
                    });
                }
            }

            sw.Stop();
            float latency = (float)sw.Elapsed.TotalMilliseconds;

            gCosts.Dispose();
            fCosts.Dispose();
            cameFrom.Dispose();
            closedSet.Dispose();
            openSet.Dispose();

            return new PathResult
            {
                Success = reached,
                NodesExpanded = nodesExpanded,
                LatencyMs = latency,
                HazardCellsAvoided = hazardCellsAvoided
            };
        }

        private static float Heuristic(int2 a, int2 b)
        {
            float dx = math.abs(a.x - b.x);
            float dy = math.abs(a.y - b.y);
            return 1.0f * (dx + dy) + (1.414f - 2.0f * 1.0f) * math.min(dx, dy);
        }
    }
}
