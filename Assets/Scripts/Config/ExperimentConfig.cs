using UnityEngine;

[CreateAssetMenu(fileName = "ExperimentConfig", menuName = "PathNav/ExperimentConfig")]
public class ExperimentConfig : ScriptableObject
{
    [Header("Grid")]
    public int gridSizeX = 10;
    public int gridSizeZ = 10;
    public float tileSize = 1f;
    public bool randomizePathEachTrial = true;

    [Header("Path")]
    public Vector2Int start = new Vector2Int(0, 0);
    public Vector2Int goal  = new Vector2Int(9, 9);
    public bool monotonicRightUp = true; // (Right/Up only)

    [Header("Materials")]
    public Material distractorFixed; // 1024 normal
    public Material pathVariable;    // you assign per trial

    [Header("Trial / Logging")]
    public string sceneName = "MainPathNav";
    public string participantId = "";
    public bool writeHeatmap = true;

    [Header("UI")]
    public Canvas redOverlayCanvas; // assigned in scene (also cached here)
}
