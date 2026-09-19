using System;
using System.IO;
using UnityEngine;

// Where every game's save file lives: Unity's per-player data folder
// (Windows: AppData/LocalLow/RoxtWorks/Pixel Casino Simulator; web: the browser's IndexedDB,
// kept by autoSyncPersistentDataPath in the web template). Saves used to sit next to the game's
// executable, which a web build doesn't have and an installed game can't write to — an old save
// there is copied across once, the first time its game looks for it.
public static class SavePaths
{
    public static string For(string fileName)
    {
        string dir = Application.persistentDataPath;
        string path = Path.Combine(dir, fileName);
        if (File.Exists(path)) return path;
        try
        {
            Directory.CreateDirectory(dir);
            if (Application.platform != RuntimePlatform.WebGLPlayer)
            {
                string legacy = Path.Combine(Directory.GetParent(Application.dataPath).FullName, fileName);
                if (File.Exists(legacy)) File.Copy(legacy, path);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SavePaths: couldn't prepare '{path}' — {e.Message}");
        }
        return path;
    }

    // A save that won't load is kept beside the original (renamed) before the game starts fresh,
    // so the next save can't silently overwrite the player's progress.
    public static void KeepBadCopy(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Copy(path, $"{path}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}", true);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SavePaths: couldn't back up '{path}' — {e.Message}");
        }
    }
}
