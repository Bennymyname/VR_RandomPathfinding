using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    public ExperimentConfig config;
    public GameObject tilePrefab;

    public Dictionary<Vector2Int, Tile> Tiles { get; private set; } = new();
    public HashSet<Vector2Int> CurrentPath { get; private set; } = new();
    public Vector3 GridOriginWorld { get; private set; }

    // Call this with a concrete seed. No nullable.
    public void BuildGridAndPath(int seed)
    {
        if (config == null)
        {
            Debug.LogError("[GridManager] Missing ExperimentConfig reference.");
            return;
        }
        if (tilePrefab == null)
        {
            Debug.LogError("[GridManager] Missing Tile Prefab reference.");
            return;
        }

        ClearGrid();

        int gx = Mathf.Max(1, config.gridSizeX);
        int gz = Mathf.Max(1, config.gridSizeZ);
        float s = Mathf.Max(0.01f, config.tileSize);

        GridOriginWorld = transform.position;
        Tiles.Clear();
        CurrentPath.Clear();

        // Generate monotonic Right/Up path from start to goal
        System.Random rng = new System.Random(seed);
        int dx = config.goal.x - config.start.x;
        int dz = config.goal.y - config.start.y;

        var moves = new List<Vector2Int>();
        for (int i = 0; i < dx; i++) moves.Add(new Vector2Int(1, 0));
        for (int i = 0; i < dz; i++) moves.Add(new Vector2Int(0, 1));
        for (int i = moves.Count - 1; i > 0; --i)
        {
            int j = rng.Next(i + 1);
            (moves[i], moves[j]) = (moves[j], moves[i]);
        }

        Vector2Int p = config.start;
        CurrentPath.Add(p);
        foreach (var m in moves)
        {
            p += m;
            CurrentPath.Add(p);
        }

        int spawned = 0;
        for (int z = 0; z < gz; z++)
        for (int x = 0; x < gx; x++)
        {
            Vector2Int gp = new(x, z);
            bool isPath  = CurrentPath.Contains(gp);
            bool isStart = gp == config.start;
            bool isGoal  = gp == config.goal;

            Vector3 worldPos = GridToWorld(gp);
            var go = Instantiate(tilePrefab, worldPos, Quaternion.Euler(90, 0, 0), transform);
            go.name = isStart ? $"Tile_{x}_{z}_START" : isGoal ? $"Tile_{x}_{z}_GOAL" : $"Tile_{x}_{z}";
            var tile = go.GetComponent<Tile>();
            if (tile == null)
            {
                Debug.LogError("[GridManager] Tile prefab has no Tile component.");
                continue;
            }
            tile.Init(gp, isPath, isStart, isGoal, config.distractorFixed, config.pathVariable);
            Tiles[gp] = tile;
            spawned++;
        }

        Debug.Log($"[GridManager] Spawned {spawned} tiles ({gx}x{gz}). Path length={CurrentPath.Count}, seed={seed}");
    }

    public Vector3 GridToWorld(Vector2Int gp)
    {
        float s = Mathf.Max(0.01f, config.tileSize);
        return GridOriginWorld + new Vector3((gp.x + 0.5f) * s, 0f, (gp.y + 0.5f) * s);
    }

    public bool TryGetTileAtWorld(Vector3 worldPos, out Tile tile)
    {
        Vector3 local = worldPos - GridOriginWorld;
        int x = Mathf.FloorToInt(local.x / config.tileSize);
        int z = Mathf.FloorToInt(local.z / config.tileSize);
        var gp = new Vector2Int(x, z);
        return Tiles.TryGetValue(gp, out tile);
    }

    public void ClearGrid()
    {
        var kill = new List<GameObject>();
        foreach (Transform c in transform) kill.Add(c.gameObject);
        foreach (var go in kill) DestroyImmediate(go);
        Tiles.Clear();
        CurrentPath.Clear();
    }
}
