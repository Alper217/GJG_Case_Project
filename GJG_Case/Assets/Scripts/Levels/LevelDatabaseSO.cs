using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

/// <summary>
/// Ordered catalog of LevelConfigSO assets representing level progression.
/// Adding a new Level asset in Assets/Data/Levels and running CollectFromFolder appends it automatically.
/// </summary>
[CreateAssetMenu(menuName = "GJG Case/Level Database", fileName = "LevelDatabase")]
public class LevelDatabaseSO : ScriptableObject
{
    [SerializeField] private List<LevelConfigSO> _levels = new List<LevelConfigSO>();

    public int Count => _levels != null ? _levels.Count : 0;
    public IReadOnlyList<LevelConfigSO> Levels => _levels;

    public LevelConfigSO GetLevel(int zeroBasedIndex)
    {
        if (_levels == null || _levels.Count == 0)
        {
            return null;
        }

        int clamped = Mathf.Clamp(zeroBasedIndex, 0, _levels.Count - 1);
        return _levels[clamped];
    }

#if UNITY_EDITOR
    [ContextMenu("Collect All Levels From Same Folder")]
    public void CollectLevelsFromFolder()
    {
        string dbPath = AssetDatabase.GetAssetPath(this);
        string folder = string.IsNullOrEmpty(dbPath) ? "Assets/Data/Levels" : Path.GetDirectoryName(dbPath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(folder))
        {
            folder = "Assets/Data/Levels";
        }

        string[] guids = AssetDatabase.FindAssets("t:LevelConfigSO", new[] { folder });
        var found = new List<LevelConfigSO>(guids.Length);

        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            LevelConfigSO cfg = AssetDatabase.LoadAssetAtPath<LevelConfigSO>(assetPath);
            if (cfg != null)
            {
                found.Add(cfg);
            }
        }

        found.Sort((a, b) =>
        {
            int cmp = a.LevelNumber.CompareTo(b.LevelNumber);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.name, b.name);
        });

        _levels = found;
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
        Debug.Log($"[LevelDatabase] Synced {_levels.Count} level(s) from '{folder}'.", this);
    }

    [MenuItem("Tools/GJG Case/Sync Level Database")]
    private static void SyncSelectedOrDefaultDatabase()
    {
        string[] dbGuids = AssetDatabase.FindAssets("t:LevelDatabaseSO");
        if (dbGuids.Length == 0)
        {
            Debug.LogWarning("[LevelDatabase] No LevelDatabaseSO asset found.");
            return;
        }

        for (int i = 0; i < dbGuids.Length; i++)
        {
            LevelDatabaseSO db = AssetDatabase.LoadAssetAtPath<LevelDatabaseSO>(AssetDatabase.GUIDToAssetPath(dbGuids[i]));
            if (db != null)
            {
                db.CollectLevelsFromFolder();
            }
        }
    }
#endif
}
