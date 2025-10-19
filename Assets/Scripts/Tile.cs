using UnityEngine;

[RequireComponent(typeof(MeshRenderer), typeof(MeshCollider))]
public class Tile : MonoBehaviour
{
    public Vector2Int GridPos { get; private set; }
    public bool IsPath  { get; private set; }
    public bool IsStart { get; private set; }
    public bool IsGoal  { get; private set; }

    // Assigned runtime (for debugging/logging)
    public string AssignedFamily;   // e.g., "Bricks004"
    public int    AssignedLevelPx;  // 0 => fixed 1024; otherwise 4..1024

    MeshRenderer _mr;

    public void Init(Vector2Int gp, bool isPath, bool isStart, bool isGoal)
    {
        GridPos = gp;
        IsPath  = isPath;
        IsStart = isStart;
        IsGoal  = isGoal;
        _mr = GetComponent<MeshRenderer>();
    }

    public MeshRenderer Renderer => _mr ??= GetComponent<MeshRenderer>();
}
