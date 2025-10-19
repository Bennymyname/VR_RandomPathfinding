using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "TextureFamily", menuName = "PathNav/Texture Family", order = 1)]
public class TextureFamily : ScriptableObject
{
    [Tooltip("Display name, e.g., Bricks004")]
    public string familyName;

    [Header("Fixed 1024px (drag ALL variants here)")]
    public List<Texture2D> fixed1024List = new List<Texture2D>();

    [Header("Variable levels (4..1024 step 4). Name like Bricks004_12px, ...")]
    public List<Texture2D> variableLevels = new List<Texture2D>();

    // Runtime cache: Level(px) -> Texture
    Dictionary<int, Texture2D> _levelMap;

    public void BuildMapIfNeeded()
    {
        if (_levelMap != null) return;
        _levelMap = new Dictionary<int, Texture2D>();
        foreach (var tex in variableLevels)
        {
            if (!tex) continue;
            int lvl = ParseLevelFromName(tex.name);
            if (lvl > 0) _levelMap[lvl] = tex;
        }
    }

    public Texture2D GetLevelTexture(int level)
    {
        BuildMapIfNeeded();
        _levelMap.TryGetValue(level, out var tex);
        return tex;
    }

    public (int level, Texture2D tex) GetRandomLevelTexture(System.Random rng, int minLevel, int maxLevel, int step = 4)
    {
        BuildMapIfNeeded();
        var candidates = new List<int>();
        for (int l = minLevel; l <= maxLevel; l += step)
            if (_levelMap.ContainsKey(l)) candidates.Add(l);
        if (candidates.Count == 0) return (0, null);
        int pick = candidates[rng.Next(candidates.Count)];
        return (pick, _levelMap[pick]);
    }

    public Texture2D GetRandomFixed1024(System.Random rng)
    {
        if (fixed1024List == null || fixed1024List.Count == 0) return null;
        return fixed1024List[rng.Next(fixed1024List.Count)];
    }

    public static int ParseLevelFromName(string texName)
    {
        // expects suffix like "_12px" or "_1024px"
        int us = texName.LastIndexOf('_');
        int px = texName.LastIndexOf("px", System.StringComparison.OrdinalIgnoreCase);
        if (us < 0 || px < 0 || px <= us + 1) return 0;
        string num = texName.Substring(us + 1, px - (us + 1));
        return int.TryParse(num, out var level) ? level : 0;
    }
}
