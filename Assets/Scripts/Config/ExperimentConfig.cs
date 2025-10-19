using UnityEngine;

[CreateAssetMenu(fileName = "ExperimentConfig", menuName = "PathNav/Experiment Config", order = 0)]
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
    public bool monotonicRightUp = true;

    [Header("Materials (URP/Lit base materials)")]
    public Material basePathMat;        // we override _BumpMap via MPB
    public Material baseDistractorMat;  // we override _BumpMap via MPB

    [Header("Texture Families (drag your 3 assets here)")]
    public TextureFamily[] families;

    public enum FamilyPickMode { Choose, RandomEachTrial }
    [Header("Path Family Selection")]
    public FamilyPickMode pathFamilyMode = FamilyPickMode.RandomEachTrial;
    public int chosenFamilyIndex = -1; // used when mode == Choose

    public enum LevelMode { Specific, Range }
    [Header("Variable Level Selection (4..1024, step 4)")]
    public LevelMode levelMode = LevelMode.Specific;
    [Range(4,1024)] public int specificLevel = 64;
    [Range(4,1024)] public int levelMin = 16;
    [Range(4,1024)] public int levelMax = 256;
    public int levelStep = 4;

    [Header("Trial / Logging")]
    public string sceneName = "MainPathNav";
    public string participantId = "";
    public bool writeHeatmap = true;
}
