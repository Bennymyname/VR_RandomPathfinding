// Assets/Scripts/TrialManager.cs
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.XR.CoreUtils; // for XROrigin.MoveCameraToWorldLocation

public class TrialManager : MonoBehaviour
{
    [Header("Refs")]
    public ExperimentConfig config;
    public GridManager grid;
    public Transform xrCamera;   // Main Camera (HMD)
    public Transform xrOrigin;   // XR Origin root
    public Canvas redOverlayCanvas;
    public Canvas popupCanvas;
    public TextMeshProUGUI hudTimerTMP;
    public TextMeshProUGUI hudDamageTMP;
    public Button popupSaveNextBtn;
    public TextMeshProUGUI popupSummaryTMP;

    [Header("Raycast")]
    public float rayLength = 5f;
    public LayerMask tileLayer; // set mask to "Tile"

    // runtime
    Tile _currentTile;
    Tile _prevTile;
    bool _trialRunning = false;
    bool _hasStarted = false;
    float _startGrace = 0.25f;

    float _trialStartTime;
    float _totalTime;
    float _damageTime;
    HashSet<Tile> _wrongTilesVisited = new();
    float[,] _heatmap;

    // CSV / bookkeeping
    string _logDir;
    string _sceneName;
    int _trialIndex = 0;
    int _seedUsed = 0;

    // XR
    XROrigin _xrOriginComp;

    void Start()
    {
        _sceneName = config.sceneName;
        _logDir = Path.Combine(Application.persistentDataPath, "PathNavLogs");
        Directory.CreateDirectory(_logDir);

        _xrOriginComp = xrOrigin.GetComponent<XROrigin>();

        // Build first grid & path
        _seedUsed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        grid.BuildGridAndPath(_seedUsed);

        // Place player at start
        TeleportPlayerToStart();

        // Ensure canvases start disabled
        popupCanvas.enabled = false;
        redOverlayCanvas.enabled = false;

        ResetMetrics();
    }

    void Update()
    {
        UpdateTileUnderPlayer();
        UpdateTimers(Time.deltaTime);
        UpdateHUD();
    }

    void UpdateTileUnderPlayer()
    {
        Ray r = new Ray(xrCamera.position, Vector3.down);
        if (Physics.Raycast(r, out var hit, rayLength, tileLayer))
        {
            grid.TryGetTileAtWorld(hit.point, out var t);
            _currentTile = t;
        }
        else
        {
            _currentTile = null;
        }

        // START: when leaving the start tile
        if (!_trialRunning)
        {
            if (_prevTile != null && _prevTile.IsStart && _currentTile != null && !_currentTile.IsStart)
            {
                _trialRunning = true;
                _trialStartTime = Time.time;
                _hasStarted = true;
            }
        }
        else // running
        {
            // STOP: on goal tile, but only after a short grace time
            if (_hasStarted && _currentTile != null && _currentTile.IsGoal && (Time.time - _trialStartTime) > _startGrace)
            {
                _totalTime = Time.time - _trialStartTime;
                _trialRunning = false;
                ShowPopup();
            }
        }

        // DAMAGE & ERRORS visual/logic
        bool onWrongTile = _currentTile != null && !_currentTile.IsPath && !_currentTile.IsStart && !_currentTile.IsGoal;
        if (onWrongTile)
        {
            redOverlayCanvas.enabled = true;
            if (_prevTile != _currentTile && !_wrongTilesVisited.Contains(_currentTile))
                _wrongTilesVisited.Add(_currentTile);
        }
        else
        {
            redOverlayCanvas.enabled = false;
        }

        _prevTile = _currentTile;
    }

    void UpdateTimers(float dt)
    {
        if (!_trialRunning) return;
        if (_currentTile != null)
        {
            var gp = _currentTile.GridPos;
            _heatmap[gp.x, gp.y] += dt;
            if (!_currentTile.IsPath && !_currentTile.IsStart && !_currentTile.IsGoal)
                _damageTime += dt;
        }
    }

    void UpdateHUD()
    {
        if (_trialRunning) hudTimerTMP.text = $"Time: {(Time.time - _trialStartTime):0.00}s";
        else               hudTimerTMP.text = $"Time: {_totalTime:0.00}s";
        hudDamageTMP.text = $"Damage: {_damageTime:0.00}s  |  Errors: {_wrongTilesVisited.Count}";
    }

    void ShowPopup()
    {
        popupCanvas.enabled = true;
        popupSummaryTMP.text =
            $"Trial {_trialIndex}\n" +
            $"Time: {_totalTime:0.00}s\n" +
            $"Damage: {_damageTime:0.00}s\n" +
            $"Errors: {_wrongTilesVisited.Count}\n" +
            $"Seed: {_seedUsed}";
        popupSaveNextBtn.onClick.RemoveAllListeners();
        popupSaveNextBtn.onClick.AddListener(() =>
        {
            SaveCsv();       // writes CSV + heatmap + path mask + annotated
            NextTrial();     // rebuild + teleport to start
        });
    }

    void ResetMetrics()
    {
        _trialRunning = false;
        _hasStarted = false;
        _totalTime = 0f;
        _damageTime = 0f;
        _wrongTilesVisited.Clear();
        _heatmap = new float[grid.config.gridSizeX, grid.config.gridSizeZ];
    }

    void NextTrial()
    {
        popupCanvas.enabled = false;
        _trialIndex++;

        if (config.randomizePathEachTrial)
            _seedUsed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

        grid.BuildGridAndPath(_seedUsed);

        TeleportPlayerToStart();             // flash back to beginning
        StartCoroutine(FlashOverlay(0.12f)); // optional quick flash

        ResetMetrics();
    }

    void TeleportPlayerToStart()
    {
        Vector3 startWorld = grid.GridToWorld(config.start);
        Vector3 targetCamPos = new Vector3(startWorld.x, xrCamera.position.y, startWorld.z);

        if (_xrOriginComp != null)
            _xrOriginComp.MoveCameraToWorldLocation(targetCamPos); // XR-friendly snap
        else
            xrOrigin.position = new Vector3(startWorld.x, xrOrigin.position.y, startWorld.z);
    }

    System.Collections.IEnumerator FlashOverlay(float seconds = 0.12f)
    {
        redOverlayCanvas.enabled = true;
        yield return new WaitForSeconds(seconds);
        redOverlayCanvas.enabled = false;
    }

    // ---- CSV & Heatmaps -----------------------------------------------------

    void SaveCsv()
    {
        string path = Path.Combine(_logDir, $"{_sceneName}.csv");
        bool writeHeader = !File.Exists(path);

        string participant = string.IsNullOrEmpty(config.participantId) ? "" : config.participantId;
        string dt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string scene = _sceneName;
        string trial = _trialIndex.ToString();

        // Your requested names:
        string varNormalName   = GetVarNormalName();
        string fixedNormalName = GetFixedNormalName();

        // Keep PathLevel for back-compat (set = variable normal name)
        string pathLevel = !string.IsNullOrEmpty(varNormalName)
            ? varNormalName
            : (config.pathVariable != null ? config.pathVariable.name : "");

        string total = _totalTime.ToString("0.000");
        string damage = _damageTime.ToString("0.000");
        string errors = _wrongTilesVisited.Count.ToString();
        string tilesVisited = CountTilesVisited().ToString();
        string seed = _seedUsed.ToString();

        using var sw = new StreamWriter(path, append: true);
        if (writeHeader)
        {
            sw.WriteLine("ParticipantID,DateTime,SceneName,Trial,PathLevel,VarNormalName,FixedNormalName,TotalTime,DamageTime,ErrorCount,TilesVisited,Seed");
        }
        sw.WriteLine($"{participant},{dt},{scene},{trial},{pathLevel},{varNormalName},{fixedNormalName},{total},{damage},{errors},{tilesVisited},{seed}");
        sw.Flush();

        WriteHeatmapFiles(); // writes heatmap + pathmask + annotated + pathcoords
    }

    void WriteHeatmapFiles()
    {
        if (!config.writeHeatmap) return;

        int gx = grid.config.gridSizeX;
        int gz = grid.config.gridSizeZ;
        string baseName = $"{_sceneName}_trial{_trialIndex}";

        // 1) Plain heatmap
        string heatPath = Path.Combine(_logDir, $"{baseName}_heatmap.csv");
        using (var hw = new StreamWriter(heatPath, append: false))
        {
            hw.Write("x/z");
            for (int z = 0; z < gz; z++) hw.Write($",z{z}");
            hw.WriteLine();
            for (int x = 0; x < gx; x++)
            {
                hw.Write($"x{x}");
                for (int z = 0; z < gz; z++)
                    hw.Write($",{_heatmap[x, z]:0.000}");
                hw.WriteLine();
            }
        }

        // 2) Path mask (1 on path, 0 elsewhere)
        string maskPath = Path.Combine(_logDir, $"{baseName}_pathmask.csv");
        using (var mw = new StreamWriter(maskPath, append: false))
        {
            mw.Write("x/z");
            for (int z = 0; z < gz; z++) mw.Write($",z{z}");
            mw.WriteLine();
            for (int x = 0; x < gx; x++)
            {
                mw.Write($"x{x}");
                for (int z = 0; z < gz; z++)
                {
                    var gp = new Vector2Int(x, z);
                    int onPath = grid.CurrentPath.Contains(gp) ? 1 : 0;
                    mw.Write($",{onPath}");
                }
                mw.WriteLine();
            }
        }

        // 3) Annotated heatmap (e.g., 0.532* for tiles on true path)
        string annPath = Path.Combine(_logDir, $"{baseName}_heatmap_annotated.csv");
        using (var aw = new StreamWriter(annPath, append: false))
        {
            aw.Write("x/z");
            for (int z = 0; z < gz; z++) aw.Write($",z{z}");
            aw.WriteLine();
            for (int x = 0; x < gx; x++)
            {
                aw.Write($"x{x}");
                for (int z = 0; z < gz; z++)
                {
                    var gp = new Vector2Int(x, z);
                    bool onPath = grid.CurrentPath.Contains(gp);
                    float t = _heatmap[x, z];
                    aw.Write(onPath ? $",{t:0.000}*" : $",{t:0.000}");
                }
                aw.WriteLine();
            }
        }

        // 4) Exact path coordinates (for reproducibility / analysis)
        string coordsPath = Path.Combine(_logDir, $"{baseName}_pathcoords.csv");
        using (var cw = new StreamWriter(coordsPath, append: false))
        {
            cw.WriteLine("Index,X,Z");
            int idx = 0;
            foreach (var gp in grid.CurrentPath)
            {
                cw.WriteLine($"{idx},{gp.x},{gp.y}");
                idx++;
            }
        }
    }

    // ---- Helpers ------------------------------------------------------------

    string GetVarNormalName()   => GetNormalMapNameFromMat(config.pathVariable);
    string GetFixedNormalName() => GetNormalMapNameFromMat(config.distractorFixed);

    string GetNormalMapNameFromMat(Material mat)
    {
        if (mat == null) return "";
        Texture t = null;
        if (mat.HasProperty("_BumpMap")) t = mat.GetTexture("_BumpMap");           // URP Lit normal slot
        if (t == null && mat.HasProperty("_NormalMap")) t = mat.GetTexture("_NormalMap");
        if (t == null && mat.HasProperty("_DetailNormalMap")) t = mat.GetTexture("_DetailNormalMap");
        return t != null ? t.name : "";
    }

    int CountTilesVisited()
    {
        int count = 0;
        for (int x = 0; x < grid.config.gridSizeX; x++)
        for (int z = 0; z < grid.config.gridSizeZ; z++)
            if (_heatmap[x, z] > 0f) count++;
        return count;
    }
}
