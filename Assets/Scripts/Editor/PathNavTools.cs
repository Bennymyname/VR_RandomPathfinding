#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GridManager))]
public class GridManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var grid = (GridManager)target;
        GUILayout.Space(10);

        if (GUILayout.Button("Generate Grid & Path (Preview)"))
        {
            int seed = Random.Range(int.MinValue, int.MaxValue);
            grid.BuildGridAndPath(seed);
        }
        if (GUILayout.Button("Clear Grid"))
        {
            grid.ClearGrid();
        }
    }
}
#endif
