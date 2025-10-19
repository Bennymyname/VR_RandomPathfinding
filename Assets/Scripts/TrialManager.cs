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

    [Header("Spawn / Start")]
    // Spawn OUTSIDE the grid so the user must step onto (0,0)
    public Vector2 preStartWorldXZ = new Vector2(-0.5f, -0.5f);

    // runtime
    Tile _currentTile;
    Tile _prevTile;
    bool _trialRunning = false;
    bool _hasStarted = false;
    float _startGrace = 0.25f; // prevent instant finish if already on goal at start

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

        // First grid & path
        _seedUsed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        grid.BuildGridAndPath(_seedUsed);

        // Place player at the pre-start location (-0.5, -0.5)
        TeleportPlayerToPrestart();

        // UI defaults
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
        // Downward ray from the HMD
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

        // START: when ENTERING Start tile (0,0) for the first time
        if (!_trialRunning && !_hasStarted)
        {
            if (_currentTile != null && _currentTile.IsStart && (_prevTile == null || !_prevTile.IsStart))
            {
                _trialRunning = true;
                _trialStartTime = Time.time;
                _hasStarted = true;
            }
        }
        else if (_trialRunning)
        {
            // STOP: on Goal tile (with small grace)
            if (_currentTile != null && _currentTile.IsGoal && (Time.time - _trialStartTime) > _startGrace)
            {
                _totalTime = Time.time - _trialStartTime;
                _trialRunning = false;
                ShowPopup();
            }
        }

        // DAMAGE & error counting (count once per wrong tile)
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
            try { SaveCsv(); }
            catch (Exception e) { Debug.LogError($"[TrialManager] SaveCsv failed: {e.Message}"); }
            finally { NextTrial(); }
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

        TeleportPlayerToPrestart();            // back to (-0.5, -0.5)
        StartCoroutine(FlashOverlay(0.12f));   // optional visual flash

        ResetMetrics();
    }

    void TeleportPlayerToPrestart()
    {
        Vector3 targetCamPos = new Vector3(
            grid.GridOriginWorld.x + preStartWorldXZ.x,
            xrCamera.position.y,
            grid.GridOriginWorld.z + preStartWorldXZ.y);

        var xro = _xrOriginComp;
        if (xro != null) xro.MoveCameraToWorldLocation(targetCamPos);
        else xrOrigin.position = new Vector3(targetCamPos.x, xrOrigin.position.y, targetCamPos.z);
    }

    System.Collections.IEnumerator FlashOverlay(float seconds = 0.12f)
    {
        redOverlayCanvas.enabled = true;
        yield return new WaitForSeconds(seconds);
        redOverlayCanvas.enabled = false;
    }

    // ---------- CSV & Heatmaps ----------------------------------------------

    StreamWriter OpenWriter(string path, bool append)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        FileMode mode = append ? FileMode.OpenOrCreate : FileMode.Create;
        var fs = new FileStream(path, mode, FileAccess.Write, FileShare.ReadWrite);
        if (append) fs.Seek(0, SeekOrigin.End);
        return new StreamWriter(fs);
    }

    string BuildDistractorMixString()
    {
        if (grid.DistractorFamilyCounts == null || grid.DistractorFamilyCounts.Count == 0) return "";
        var keys = new List<string>(grid.DistractorFamilyCounts.Keys);
        keys.Sort();
        var parts = new List<string>();
        foreach (var k in keys) parts.Add($"{k}:{grid.DistractorFamilyCounts[k]}");
        return string.Join("|", parts);
    }

    void SaveCsv()
    {
        string path = Path.Combine(_logDir, $"{_sceneName}.csv");
        bool writeHeader = !File.Exists(path);

        string participant = string.IsNullOrEmpty(config.participantId) ? "" : config.participantId;
        string dt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string scene = _sceneName;
        string trial = _trialIndex.ToString();

        // Keep legacy columns compatible
        string pathFamily = grid.PathFamily != null ? grid.PathFamily.familyName : "";
        string pathLevel = pathFamily;            // as before, PathLevel was "variable normal name"
        string fixedNormalName = "Mixed1024";     // we now mix 1024 distractors across the board

        string total = _totalTime.ToString("0.000");
        string damage = _damageTime.ToString("0.000");
        string errors = _wrongTilesVisited.Count.ToString();
        string tilesVisited = CountTilesVisited().ToString();
        string seed = _seedUsed.ToString();

        // New summary columns
        string levelMode  = config.levelMode.ToString();
        int lvlMin = (config.levelMode == ExperimentConfig.LevelMode.Specific) ? config.specificLevel : config.levelMin;
        int lvlMax = (config.levelMode == ExperimentConfig.LevelMode.Specific) ? config.specificLevel : config.levelMax;
        int usedMin = grid.PathLevelMinUsed;
        int usedMax = grid.PathLevelMaxUsed;
        string distractorMix = BuildDistractorMixString();

        using (var sw = OpenWriter(path, append: true))
        {
            if (writeHeader)
            {
                sw.WriteLine("ParticipantID,DateTime,SceneName,Trial,PathLevel,VarNormalName,FixedNormalName,TotalTime,DamageTime,ErrorCount,TilesVisited,Seed,PathFamily,LevelMode,LevelMin,LevelMax,UsedMin,UsedMax,DistractorMix");
            }

            sw.WriteLine($"{participant},{dt},{scene},{trial},{pathLevel},{pathFamily},{fixedNormalName},{total},{damage},{errors},{tilesVisited},{seed},{pathFamily},{levelMode},{lvlMin},{lvlMax},{usedMin},{usedMax},{distractorMix}");
            sw.Flush();
        }

        WriteHeatmapFiles();
    }

    void WriteHeatmapFiles()
    {
        if (!config.writeHeatmap) return;

        int gx = grid.config.gridSizeX;
        int gz = grid.config.gridSizeZ;
        string baseName = $"{_sceneName}_trial{_trialIndex}";

        // heatmap
        string heatPath = Path.Combine(_logDir, $"{baseName}_heatmap.csv");
        using (var hw = OpenWriter(heatPath, append: false))
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

        // path mask
        string maskPath = Path.Combine(_logDir, $"{baseName}_pathmask.csv");
        using (var mw = OpenWriter(maskPath, append: false))
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

        // annotated heatmap
        string annPath = Path.Combine(_logDir, $"{baseName}_heatmap_annotated.csv");
        using (var aw = OpenWriter(annPath, append: false))
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

        // exact path coords
        string coordsPath = Path.Combine(_logDir, $"{baseName}_pathcoords.csv");
        using (var cw = OpenWriter(coordsPath, append: false))
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

    // ---------- Helpers ------------------------------------------------------

    int CountTilesVisited()
    {
        int count = 0;
        for (int x = 0; x < grid.config.gridSizeX; x++)
        for (int z = 0; z < grid.config.gridSizeZ; z++)
            if (_heatmap[x, z] > 0f) count++;
        return count;
    }
}
