using UnityEngine;

[RequireComponent(typeof(MeshCollider), typeof(MeshRenderer))]
public class Tile : MonoBehaviour
{
    public Vector2Int GridPos { get; private set; }
    public bool IsPath { get; private set; }
    public bool IsStart { get; private set; }
    public bool IsGoal  { get; private set; }

    MeshRenderer _mr;

    public void Init(Vector2Int gridPos, bool isPath, bool isStart, bool isGoal,
                     Material distractorMat, Material pathMat)
    {
        GridPos = gridPos;
        IsPath = isPath;
        IsStart = isStart;
        IsGoal  = isGoal;

        if (_mr == null) _mr = GetComponent<MeshRenderer>();
        _mr.sharedMaterial = IsPath ? pathMat : distractorMat;

        gameObject.layer = LayerMask.NameToLayer("Tile");
    }
}
