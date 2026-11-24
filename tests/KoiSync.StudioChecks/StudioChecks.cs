using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using KoiSync.Studio;
using UnityEngine;
using GameStudio=global::Studio.Studio;

[BepInPlugin("local.koisync.studio.checks","KoiSync Studio checks","0.2.0")]
[BepInDependency("local.koisync.studio")]
public sealed class StudioChecks:BaseUnityPlugin
{
    private readonly StringBuilder report=new StringBuilder();
    private readonly string shortCard=@"E:\Galgames\kk\UserData\Studio\scene\qinnyann\MMD_13_POV_TrimSelfFix\Single\MMD_04_table_missionary_loop_Native_BP.png";
    private readonly string longCard=@"E:\Galgames\kk\UserData\Studio\scene\qinnyann\MMD_13_POV_TrimSelfFix\MMD_14_All_13_Motions_Native_BP_Progression.png";
    private string Output {get{return Path.Combine(Paths.GameRootPath,"studio-checks.txt");}}
    private void Record(string value){report.AppendLine(DateTime.Now.ToString("s")+" "+value);File.WriteAllText(Output,report.ToString());}
    private void Check(bool value,string reason){if(!value)throw new Exception(reason);Record("PASS "+reason);}
    private void Awake(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--studio-lite-checks")>=0){Application.runInBackground=true;StartCoroutine(Run());}}
    private IEnumerator Run()
    {
        yield return new WaitForSecondsRealtime(5);
        float readyDeadline=Time.realtimeSinceStartup+180;
        while(UnityEngine.Object.FindObjectOfType<GameStudio>()==null&&Time.realtimeSinceStartup<readyDeadline)
            yield return new WaitForSecondsRealtime(1);
        yield return new WaitForSecondsRealtime(5);
        var host=UnityEngine.Object.FindObjectOfType<Plugin>();
        try
        {
            Check(host!=null,"Studio-only plugin loads");
            Check(host.ActorListReads==0&&host.BindOperations==0&&host.SampleCount==0,"Startup performs no scene scan or sample");
            Check(!host.Devices.Connected&&!host.Devices.Connecting,"No hardware connection opened");
            Check(host.GetType().Assembly.GetType("KoiSync.Plugin.KoiSyncPanel")==null,"Legacy UI and scene adapters excluded from new assembly");
            Check(typeof(Plugin).GetCustomAttributes(typeof(BepInProcess),false).Length==1,"Only one permitted process");
            Application.CaptureScreenshot(Path.Combine(Paths.GameRootPath,"studio-lite-idle.png"));
            Record("LOAD short card requested");
            Check(GameStudio.Instance.LoadScene(shortCard),"Short scene accepted");
        }
        catch(Exception error){Record("FAIL "+error);Application.Quit();yield break;}
        float shortDeadline=Time.realtimeSinceStartup+120;
        while(host.Clock.Duration<60&&Time.realtimeSinceStartup<shortDeadline)yield return new WaitForSecondsRealtime(1);
        yield return new WaitForSecondsRealtime(2);
        try
        {
            Check(host.ActorListReads==0&&host.SampleCount==0,"Scene load remains idle until explicit binding");
            host.ReadActors();host.BindPair();Check(host.Binding!=null,"Explicit pair binding succeeds");
            Check(host.Binding.Opening.name=="cf_J_Vagina_root"||host.Binding.Opening.name=="k_f_dan_entry","Binding uses vaginal attachment");
            if(!host.Clock.Playing)host.Clock.Toggle();
        }
        catch(Exception error){Record("FAIL "+error);Application.Quit();yield break;}
        yield return new WaitForSecondsRealtime(4);
        try
        {
            Record("Bound sample count="+host.SampleCount+" binding="+(host.Binding!=null)+" status="+typeof(Plugin).GetField("status",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host));
            Check(host.SampleCount>=2&&host.Measured!=null,"Only bound pair supplies live poses");
            Check(host.Measured.Length>.16&&host.Measured.Length<.2,"Fixed physical length retained");
            Check(host.Measured.Normalized[0]>.35&&host.Measured.Normalized[0]<.95,"Physical insertion remains in expected range");
            Check(host.ActorListReads==1&&host.BindOperations==1,"Playback does not rediscover actors or bindings");
            double beforeRead=host.Clock.Time;host.ReadAttachmentChapters();
            Check(Math.Abs(host.Clock.Time-beforeRead)<.5,"Reading attachment keys never seeks Timeline");
            Check(host.Book.Chapters.Count>=1,"Unchanged vaginal attachment remains one chapter");
            typeof(Plugin).GetField("showChannels",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(host,true);
            typeof(Plugin).GetField("showTimeline",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(host,true);
            host.Clock.Seek(.25);Record("Timeline quick seek invoked");
        }
        catch(Exception error){Record("FAIL "+error);Application.Quit();yield break;}
        yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();
        Application.CaptureScreenshot(Path.Combine(Paths.GameRootPath,"studio-lite-bound.png"));
        yield return new WaitForSecondsRealtime(1);
        long before=host.SampleCount;
        try
        {
            Check(!host.Devices.Connected,"Bound preview still opens no hardware");
            Record("LOAD long 151 MB card requested");
            Check(GameStudio.Instance.LoadScene(longCard),"Long scene accepted");
            Check(host.Binding==null,"Scene load prefix releases old binding synchronously");
        }
        catch(Exception error){Record("FAIL "+error);Application.Quit();yield break;}
        float longDeadline=Time.realtimeSinceStartup+150;
        while(host.Clock.Duration<500&&Time.realtimeSinceStartup<longDeadline)yield return new WaitForSecondsRealtime(1);
        yield return new WaitForSecondsRealtime(3);
        try
        {
            Check(host.SampleCount==before&&host.ActorListReads==1,"Long scene performs zero new sampling or discovery");
            Check(host.Clock.Duration>500,"Long Timeline restored, duration="+host.Clock.Duration);
            host.Clock.Seek(543.4);Record("Long Timeline seek 543.4 requested");
        }
        catch(Exception error){Record("FAIL "+error);Application.Quit();yield break;}
        yield return new WaitForSecondsRealtime(3);
        try
        {
            Check(Math.Abs(host.Clock.Time-543.4)<5||host.Clock.Playing,"Long Timeline remains responsive after quick seek");
            Check(host.Binding==null&&host.SampleCount==before,"Seeking never restarts automatic analysis");
            Check(!host.Devices.Connected,"No hardware opened during long scene");
            Application.CaptureScreenshot(Path.Combine(Paths.GameRootPath,"studio-lite-long.png"));
            Record("COMPLETE Studio runtime checks passed");
        }
        catch(Exception error){Record("FAIL "+error);}
        yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();Application.Quit();
    }
}
