using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates one ShipConfig asset per ship (with today's default values) in Assets/ShipConfigs and assigns them on
/// the Player prefab's PlayerClass. Runs once automatically when an asset or an assignment is missing; also available
/// from the menu. Never overwrites existing assets or assignments.
/// </summary>
[InitializeOnLoad]
public static class ShipConfigSetup
{
    private const string Folder = "Assets/ShipConfigs";
    private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

    private static readonly (string field, string assetName)[] Ships =
    {
        ("galleonConfig", "Galleon"),
        ("caravelConfig", "Caravel"),
        ("drakkarConfig", "Drakkar"),
        ("sloopConfig", "Sloop"),
    };

    static ShipConfigSetup()
    {
        // Only in the main Editor (Multiplayer Play Mode virtual players run from Library/VP and share assets).
        if (Application.dataPath.Replace('\\', '/').Contains("/Library/VP/")) return;

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (NeedsSetup()) Run();
        };
    }

    [MenuItem("BombBattle/Create and Assign Ship Configs")]
    public static void Run()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets", "ShipConfigs");

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            var playerClass = root.GetComponent<PlayerClass>();
            if (playerClass == null)
            {
                Debug.LogError($"[ShipConfig] No PlayerClass on {PlayerPrefabPath}.");
                return;
            }

            var so = new SerializedObject(playerClass);
            bool prefabChanged = false;
            foreach (var (field, assetName) in Ships)
            {
                ShipConfig config = LoadOrCreate(assetName);
                SerializedProperty prop = so.FindProperty(field);
                if (prop != null && prop.objectReferenceValue == null)
                {
                    prop.objectReferenceValue = config;
                    prefabChanged = true;
                }
            }

            if (prefabChanged)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log($"[ShipConfig] Assigned ship configs on {PlayerPrefabPath}.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
    }

    private static ShipConfig LoadOrCreate(string assetName)
    {
        string path = $"{Folder}/{assetName}.asset";
        var config = AssetDatabase.LoadAssetAtPath<ShipConfig>(path);
        if (config != null) return config;

        config = ScriptableObject.CreateInstance<ShipConfig>();
        AssetDatabase.CreateAsset(config, path);
        Debug.Log($"[ShipConfig] Created {path} with default values.");
        return config;
    }

    private static bool NeedsSetup()
    {
        foreach (var (_, assetName) in Ships)
            if (AssetDatabase.LoadAssetAtPath<ShipConfig>($"{Folder}/{assetName}.asset") == null) return true;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var playerClass = prefab != null ? prefab.GetComponent<PlayerClass>() : null;
        if (playerClass == null) return false;

        var so = new SerializedObject(playerClass);
        foreach (var (field, _) in Ships)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop != null && prop.objectReferenceValue == null) return true;
        }
        return false;
    }
}
