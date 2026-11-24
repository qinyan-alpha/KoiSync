using System;
using System.IO;
using System.Reflection;
using UnityEngine;

public static class KoiSyncV137Launcher
{
    public static void Main()
    {
        string pidPath=@"E:\project\c#\KoiSync\artifacts\v0137-contact-solver\studio-pid.txt";
        if(System.Diagnostics.Process.GetCurrentProcess().ProcessName!="CharaStudio"||!File.Exists(pidPath)||File.ReadAllText(pidPath).Trim()!=System.Diagnostics.Process.GetCurrentProcess().Id.ToString())return;
        Application.runInBackground=true;
        File.WriteAllText(@"E:\project\c#\KoiSync\artifacts\v0137-contact-solver\launcher-main.txt",Environment.CommandLine);
        if(KoiSyncV137LaunchWork.Dispatch())return;
        if(GameObject.Find("KoiSync v137 launcher")!=null)return;
        var host=new GameObject("KoiSync v137 launcher");UnityEngine.Object.DontDestroyOnLoad(host);host.AddComponent<KoiSyncV137LaunchWork>();
    }
    public static void Unload(){}
}
public sealed class KoiSyncV137LaunchWork:MonoBehaviour
{
    float next;bool done;
    public static bool Dispatch()
    {
        string output=@"E:\project\c#\KoiSync\artifacts\scene-preview\v036-native-contact-20261024-v0137-run1";
        Directory.CreateDirectory(output);
        if(File.Exists(Path.Combine(output,"launcher.txt")))return true;
        try
        {
            if(Studio.Studio.Instance==null){File.WriteAllText(Path.Combine(output,"launcher-wait.txt"),"Waiting for Studio");return false;}
            foreach(var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t=a.GetType("Unity.Console.Program",false);if(t==null)continue;
                var engine=t.GetProperty("MainEngine",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).GetValue(null,null);
                if(engine==null){File.WriteAllText(Path.Combine(output,"launcher-wait.txt"),"Waiting for Python engine");return false;}
                engine.GetType().GetMethod("ExecuteFile",new[]{typeof(string)}).Invoke(engine,new object[]{@"E:\project\c#\KoiSync\tools\capture_v036_solver_v0137.py"});
                File.WriteAllText(Path.Combine(output,"launcher.txt"),"Normal-plugin capture dispatched");return true;
            }
            File.WriteAllText(Path.Combine(output,"launcher-wait.txt"),"Waiting for Unity.Console assembly");
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(output,"launcher-error.txt"),ex.ToString());return true;}
        return false;
    }
    void Update()
    {
        if(done||Time.realtimeSinceStartup<next)return;next=Time.realtimeSinceStartup+2;
        if(Dispatch()){done=true;Destroy(gameObject);}
    }
}
