#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class TextureFamilyEditor
{
    // ------------------------------------------------------------
    // Create ONE TextureFamily from whatever is selected (files or folders)
    // ------------------------------------------------------------
    [MenuItem("Tools/PathNav/Create TextureFamily From Any Selection")]
    private static void CreateFromAnySelection()
    {
        var guids = Selection.assetGUIDs;
        if (guids == null || guids.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "TextureFamily",
                "Select the 1024 texture(s) or its folder AND the variable textures (files or folder) for ONE family, then run again.",
                "OK");
            return;
        }

        // gather textures from selection (files and inside folders)
        var textures = new List<Texture2D>();
        foreach (var g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            if (AssetDatabase.IsValidFolder(path))
            {
                var sub = AssetDatabase.FindAssets("t:Texture2D", new[] { path });
                foreach (var sg in sub)
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(sg));
                    if (tex) textures.Add(tex);
                }
            }
            else
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex) textures.Add(tex);
            }
        }

        textures = textures.Distinct().ToList();
        if (textures.Count == 0)
        {
            EditorUtility.DisplayDialog("TextureFamily", "No textures found in selection.", "OK");
            return;
        }

        // split fixed (1024) and variable (_Npx)
        var fixedList = new List<Texture2D>();
        var varList   = new List<Texture2D>();
        foreach (var t in textures)
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("1024")) fixedList.Add(t);
            else if (n.Contains("px")) varList.Add(t);
        }

        if (fixedList.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "TextureFamily",
                "Could not find any 1024 textures in selection (names should include '1024' or end with '_1024px').",
                "OK");
            return;
        }

        // sort variables by parsed level
        varList.Sort((a, b) => TextureFamily.ParseLevelFromName(a.name).CompareTo(TextureFamily.ParseLevelFromName(b.name)));

        // infer family name from first fixed
        string familyName = fixedList[0].name;
        if (familyName.EndsWith("_1024px")) familyName = familyName.Substring(0, familyName.Length - "_1024px".Length);
        if (familyName.EndsWith("_Crops"))  familyName = familyName.Substring(0, familyName.Length - "_Crops".Length);

        var tf = ScriptableObject.CreateInstance<TextureFamily>();
        tf.familyName      = familyName;
        tf.fixed1024List   = fixedList;
        tf.variableLevels  = varList;

        // save next to the first fixed texture
        string folder   = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(fixedList[0]));
        string savePath = System.IO.Path.Combine(folder, $"{familyName}_TextureFamily.asset").Replace("\\", "/");
        savePath        = AssetDatabase.GenerateUniqueAssetPath(savePath);

        AssetDatabase.CreateAsset(tf, savePath);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(tf);
        EditorUtility.DisplayDialog("TextureFamily", "Created: " + savePath, "OK");
    }

    // ------------------------------------------------------------
    // Auto-create ALL families from the standard layout:
    // Assets/NormalMaps/1024/<Family>_Crops/*.png
    // Assets/NormalMaps/Variables/<Family>Normalmaps/*.png
    // ------------------------------------------------------------
    [MenuItem("Tools/PathNav/Auto-Create All TextureFamilies (NormalMaps layout)")]
    private static void AutoCreateAllFamilies()
    {
        string baseRoot = "Assets/NormalMaps";
        string fixRoot  = baseRoot + "/1024";
        string varRoot  = baseRoot + "/Variables";
        if (!AssetDatabase.IsValidFolder(fixRoot) || !AssetDatabase.IsValidFolder(varRoot))
        {
            EditorUtility.DisplayDialog(
                "Auto-Create",
                "Cannot find 'Assets/NormalMaps/1024' and 'Assets/NormalMaps/Variables'.",
                "OK");
            return;
        }

        // families from Variables subfolders
        var varSubFolders = AssetDatabase.GetSubFolders(varRoot);
        int created = 0;

        foreach (var vf in varSubFolders)
        {
            // e.g., "Bricks004Normalmaps" -> "Bricks004"
            string familyName = System.IO.Path.GetFileName(vf)
                .Replace("Normalmaps", "")
                .Replace("Normals", "");

            // find 1024 folder that matches (e.g., "Bricks004_Crops")
            string[] fixSub     = AssetDatabase.GetSubFolders(fixRoot);
            string fixedFolder  = fixSub.FirstOrDefault(s => System.IO.Path.GetFileName(s).StartsWith(familyName));
            if (string.IsNullOrEmpty(fixedFolder))
            {
                Debug.LogWarning($"[Auto-Create] No 1024 folder for family {familyName}");
                continue;
            }

            // collect ALL fixed 1024 textures in that folder
            var fixedGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { fixedFolder });
            var fixedList  = new List<Texture2D>();
            foreach (var g in fixedGuids)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g));
                if (tex != null && tex.name.ToLowerInvariant().Contains("1024"))
                    fixedList.Add(tex);
            }
            if (fixedList.Count == 0)
            {
                Debug.LogWarning($"[Auto-Create] No fixed 1024 textures found for {familyName}");
                continue;
            }

            // collect VARIABLE textures in vf
            var varGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { vf });
            var varList  = new List<Texture2D>();
            foreach (var g in varGuids)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g));
                if (tex != null && tex.name.ToLowerInvariant().Contains("px"))
                    varList.Add(tex);
            }
            varList.Sort((a, b) => TextureFamily.ParseLevelFromName(a.name).CompareTo(TextureFamily.ParseLevelFromName(b.name)));

            // create asset INSIDE the variables folder
            var tf = ScriptableObject.CreateInstance<TextureFamily>();
            tf.familyName     = familyName;
            tf.fixed1024List  = fixedList;
            tf.variableLevels = varList;

            string savePath = System.IO.Path.Combine(vf, $"{familyName}_TextureFamily.asset").Replace("\\", "/");
            savePath        = AssetDatabase.GenerateUniqueAssetPath(savePath);
            AssetDatabase.CreateAsset(tf, savePath);
            created++;
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Auto-Create", $"Created {created} TextureFamily asset(s).", "OK");
    }
}
#endif
