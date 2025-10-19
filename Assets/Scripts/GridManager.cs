using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    [Header("Config & Prefab")]
    public ExperimentConfig config;
    public GameObject tilePrefab;

    [Header("Ground")]
    public float groundY = 0f;

    [Header("Sample (preview) tile")]
    public bool spawnSampleTile = true;
    public Vector2 sampleWorldXZ = new Vector2(-1.5f, -1.5f);
    public float sampleYaw = 90f;

    // runtime public state
    public Dictionary<Vector2Int, Tile> Tiles { get; private set; } = new();
    public HashSet<Vector2Int> CurrentPath { get; private set; } = new();
    public Vector3 GridOriginWorld { get; private set; }

    // chosen for this trial
    public TextureFamily PathFamily { get; private set; }
    public int PathFamilyIndex { get; private set; } = -1;
    public int PathLevelMinUsed { get; private set; } = 0;
    public int PathLevelMaxUsed { get; private set; } = 0;
    public Dictionary<string, int> DistractorFamilyCounts { get; private set; } = new();

    Transform _sampleRoot;
    GameObject _sampleTileGO;

    MaterialPropertyBlock _mpb;

    void EnsureNormalKeyword(Material m)
    {
        if (!m) return;
        m.EnableKeyword("_NORMALMAP"); // make URP/Lit sample normal map
    }

    public void BuildGridAndPath(int seed)
    {
        if (config == null) { Debug.LogError("[GridManager] Missing ExperimentConfig."); return; }
        if (tilePrefab == null) { Debug.LogError("[GridManager] Missing Tile Prefab."); return; }
        if (config.basePathMat == null || config.baseDistractorMat == null)
        { Debug.LogError("[GridManager] Assign basePathMat and baseDistractorMat in config."); return; }
        if (config.families == null || config.families.Length == 0)
        { Debug.LogError("[GridManager] Assign TextureFamily assets in config."); return; }

        // ensure normal keyword on base materials
        EnsureNormalKeyword(config.basePathMat);
        EnsureNormalKeyword(config.baseDistractorMat);

        ClearGrid();

        int gx = Mathf.Max(1, config.gridSizeX);
        int gz = Mathf.Max(1, config.gridSizeZ);

        GridOriginWorld = transform.position;
        Tiles.Clear();
        CurrentPath.Clear();
        DistractorFamilyCounts = new Dictionary<string, int>();

        // RNGs derived from seed
        System.Random rng       = new System.Random(seed);
        System.Random rngLevels = new System.Random(rng.Next()); // path level choices
        System.Random rngDistr  = new System.Random(rng.Next()); // 1024 crop choices for distractors

        // === pick ONE family ===
        if (config.pathFamilyMode == ExperimentConfig.FamilyPickMode.Choose &&
            config.chosenFamilyIndex >= 0 && config.chosenFamilyIndex < config.families.Length)
            PathFamilyIndex = config.chosenFamilyIndex;
        else
            PathFamilyIndex = rng.Next(config.families.Length);

        PathFamily = config.families[PathFamilyIndex];
        if (PathFamily == null) { Debug.LogError("[GridManager] Chosen path family is null."); return; }
        foreach (var f in config.families) f?.BuildMapIfNeeded();

        // monotonic right/up path (0,0) -> (gx-1,gz-1) via start/goal in config
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
        foreach (var m in moves) { p += m; CurrentPath.Add(p); }

        _mpb ??= new MaterialPropertyBlock();
        PathLevelMinUsed = int.MaxValue;
        PathLevelMaxUsed = 0;

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
            go.name = isStart ? $"Tile_{x}_{z}_START_PATH" :
                     isGoal  ? $"Tile_{x}_{z}_GOAL_PATH"  :
                     isPath  ? $"Tile_{x}_{z}_PATH"       : $"Tile_{x}_{z}_DIST";

            var tile = go.GetComponent<Tile>();
            if (tile == null) { Debug.LogError("[GridManager] Tile prefab has no Tile component."); continue; }
            tile.Init(gp, isPath, isStart, isGoal);

            var mr = tile.Renderer;

            if (isPath) // start/goal included for visuals
            {
                mr.sharedMaterial = config.basePathMat;
                mr.sharedMaterial.EnableKeyword("_NORMALMAP");

                // level = specific OR random-in-range (per path tile)
                int level = (config.levelMode == ExperimentConfig.LevelMode.Specific)
                          ? config.specificLevel
                          : PathFamily.GetRandomLevelTexture(rngLevels, config.levelMin, config.levelMax, config.levelStep).level;

                Texture2D pathTex = PathFamily.GetLevelTexture(level);
                if (pathTex == null && config.levelMode == ExperimentConfig.LevelMode.Range)
                {
                    var rt = PathFamily.GetRandomLevelTexture(rngLevels, config.levelMin, config.levelMax, config.levelStep);
                    level = rt.level; pathTex = rt.tex;
                }
                if (pathTex == null) // fallback to specific
                {
                    level = config.specificLevel;
                    pathTex = PathFamily.GetLevelTexture(level);
                }

                _mpb.Clear();
                _mpb.SetTexture("_BumpMap", pathTex);
                _mpb.SetFloat("_BumpScale", 1f);
                mr.SetPropertyBlock(_mpb);

                tile.AssignedFamily = PathFamily.familyName;
                tile.AssignedLevelPx = level;

                if (level > 0)
                {
                    PathLevelMinUsed = Mathf.Min(PathLevelMinUsed, level);
                    PathLevelMaxUsed = Mathf.Max(PathLevelMaxUsed, level);
                }
            }
            else // DISTRACTOR: SAME FAMILY, random 1024 crop PER TILE
            {
                mr.sharedMaterial = config.baseDistractorMat;
                mr.sharedMaterial.EnableKeyword("_NORMALMAP");

                var tex = PathFamily.GetRandomFixed1024(rngDistr); // pick crop1/2/3 randomly per tile

                _mpb.Clear();
                _mpb.SetTexture("_BumpMap", tex);
                _mpb.SetFloat("_BumpScale", 1f);
                mr.SetPropertyBlock(_mpb);

                tile.AssignedFamily = PathFamily.familyName;
                tile.AssignedLevelPx = 0; // 0 == fixed 1024

                if (!DistractorFamilyCounts.ContainsKey(PathFamily.familyName))
                    DistractorFamilyCounts[PathFamily.familyName] = 0;
                DistractorFamilyCounts[PathFamily.familyName]++;
            }

            Tiles[gp] = tile;
            spawned++;
        }

        if (PathLevelMinUsed == int.MaxValue) PathLevelMinUsed = 0;
        Debug.Log($"[GridManager] Spawned {spawned} tiles ({gx}x{gz}). Family={PathFamily.familyName}, LevelsUsed={PathLevelMinUsed}-{PathLevelMaxUsed}");

        BuildSampleTile(); // preview tile outside grid
    }

    public Vector3 GridToWorld(Vector2Int gp)
    {
        float s = Mathf.Max(0.01f, config.tileSize);
        return GridOriginWorld + new Vector3((gp.x + 0.5f) * s, groundY, (gp.y + 0.5f) * s);
    }

    public bool TryGetTileAtWorld(Vector3 worldPos, out Tile tile)
    {
        Vector3 local = worldPos - GridOriginWorld;
        int x = Mathf.FloorToInt(local.x / config.tileSize);
        int z = Mathf.FloorToInt(local.z / config.tileSize);
        return Tiles.TryGetValue(new Vector2Int(x, z), out tile);
    }

    public void ClearGrid()
    {
        var kill = new List<GameObject>();
        foreach (Transform c in transform) kill.Add(c.gameObject);
        foreach (var go in kill) DestroyImmediate(go);
        Tiles.Clear();
        CurrentPath.Clear();
        if (_sampleRoot != null) DestroyImmediate(_sampleRoot.gameObject);
        _sampleRoot = null; _sampleTileGO = null;
    }

    void BuildSampleTile()
    {
        if (!spawnSampleTile) return;

        if (_sampleRoot == null)
        {
            _sampleRoot = new GameObject("SampleRoot").transform;
            _sampleRoot.SetParent(transform, false);
        }
        else
        {
            foreach (Transform c in _sampleRoot) DestroyImmediate(c.gameObject);
        }

        _sampleTileGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _sampleTileGO.name = "SampleTile_PathVariable";
        _sampleTileGO.transform.SetParent(_sampleRoot, false);

        var pos = new Vector3(sampleWorldXZ.x, groundY, sampleWorldXZ.y);
        _sampleTileGO.transform.position = GridOriginWorld + pos;
        _sampleTileGO.transform.rotation = Quaternion.Euler(90f, sampleYaw, 0f);
        _sampleTileGO.transform.localScale = new Vector3(config.tileSize, config.tileSize, 1f);

        var mr = _sampleTileGO.GetComponent<MeshRenderer>();
        mr.sharedMaterial = config.basePathMat;
        mr.sharedMaterial.EnableKeyword("_NORMALMAP");

        int sampleLevel = (config.levelMode == ExperimentConfig.LevelMode.Specific)
                            ? config.specificLevel
                            : config.levelMin;

        Texture2D tex = PathFamily != null ? PathFamily.GetLevelTexture(sampleLevel) : null;
        var mpb = new MaterialPropertyBlock();
        mpb.SetTexture("_BumpMap", tex);
        mpb.SetFloat("_BumpScale", 1f);
        mr.SetPropertyBlock(mpb);

        var col = _sampleTileGO.GetComponent<Collider>(); if (col) DestroyImmediate(col);
        _sampleTileGO.layer = LayerMask.NameToLayer("Default");
    }
}
