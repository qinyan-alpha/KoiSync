using System;
using System.IO;

namespace KoiSync.Studio
{
    public static class SceneScriptPath
    {
        public static string BasePath(string gameRoot,string scene)
        {
            string root=Path.GetFullPath(Path.Combine(gameRoot,"UserData/Studio/scene")).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string full=Path.GetFullPath(scene);
            string relative=full.StartsWith(root,StringComparison.OrdinalIgnoreCase)?full.Substring(root.Length):Path.Combine("External",Path.GetFileName(full));
            return Path.GetFullPath(Path.Combine(Path.Combine(gameRoot,"UserData/KoiSyncStudio"),Path.ChangeExtension(relative,null)));
        }
    }
}
