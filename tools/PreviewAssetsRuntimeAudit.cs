using System;
using System.IO;
using System.Reflection;
using UnityEngine;

// Temporary entry for the existing C# Script Loader when the Python console
// is restricted to Studio. Copy to scripts only for the bounded asset audit.
public static class KoiSyncPreviewAssetAudit
{
    public static void Main()
    {
        if(BepInEx.Paths.ProcessName!="Koikatu")return;
        var host=new GameObject("KoiSync bounded asset audit");UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent<KoiSyncPreviewAssetAuditBootstrap>();
    }
    public static void Unload(){}
}

public sealed class KoiSyncPreviewAssetAuditBootstrap:MonoBehaviour
{
    private bool complete;
    private void Update()
    {
        if(complete||UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="Title")return;
        complete=true;
        string root=@"E:\project\c#\KoiSync";
        string output=Path.Combine(root,@"artifacts\preview-assets\v1.0.0-build03\runtime");
        try
        {
            Assembly checks=Assembly.Load(File.ReadAllBytes(Path.Combine(root,@"tests\KoiSync.PreviewChecks\bin\Release\net35\KoiSync.PreviewAssetChecks.dll")));
            checks.GetType("PreviewChecks").GetMethod("Run",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{output});
        }
        catch(Exception ex){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"Koikatu-bootstrap-error.txt"),ex.ToString());}
        Destroy(gameObject);
    }
}
