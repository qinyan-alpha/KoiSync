using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using KoiSync.Core;
using KoiSync.Plugin;
using UnityEngine;
using UnityEngine.UI;

[BepInPlugin("local.koisync.funscriptchecks","KoiSync funscript checks","0.1.0")]
[BepInDependency("local.koisync")]
public sealed class FunscriptRuntimeChecks:BaseUnityPlugin
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private readonly StringBuilder report=new StringBuilder();
    private Plugin host;private KoiSyncPanel panel;private int count;
    private object Field(object owner,string name){return owner.GetType().GetField(name,Flags).GetValue(owner);}
    private object Call(object owner,string name,params object[] args){return owner.GetType().GetMethod(name,Flags).Invoke(owner,args);}
    private void Check(bool valid,string message){report.AppendLine((valid?"PASS ":"FAIL ")+message);if(!valid)throw new Exception(message);count++;}
    private void Awake()
    {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-funscript-checks")<0)return;
        AudioListener.volume=0;Application.runInBackground=true;StartCoroutine(Run());
    }
    private IEnumerator Run()
    {
        yield return new WaitForSecondsRealtime(8);
        try
        {
            host=FindObjectOfType<Plugin>();host.StopAllCoroutines();host.enabled=false;panel=(KoiSyncPanel)Field(host,"panel");
            Check(host.StudioAdapter!=null&&!host.Devices.Connected&&!host.Devices.Connecting,"Studio fixture starts without any device connection");
            TimelineState.Refresh();TimelineState.Seek(.5);TimelineState.SetPlaying(true);
            Call(panel,"ShowMotionTimeline");Call(panel,"RefreshFloatingTools");
            Check(!panel.LaboratoryOpen,"Timeline alone leaves synchronization available with alignment closed");
            string folder=Path.Combine(Paths.GameRootPath,"UserData/KoiSync/funscript-fixture");Directory.CreateDirectory(folder);
            string[] suffixes={"",".surge",".sway",".twist",".roll",".pitch"};
            for(int i=0;i<6;i++)File.WriteAllText(Path.Combine(folder,"同步测试"+suffixes[i]+".funscript"),"{\"inverted\":false,\"actions\":[{\"at\":0,\"pos\":"+(20+i*5)+"},{\"at\":1000,\"pos\":"+(80-i*5)+"}]}");
            panel.ImportFunscript(Path.Combine(folder,"同步测试.roll.funscript"));
            Check(host.ScriptPlayback.Scripts.Count==6&&host.ScriptPlayback.Enabled,"UI import of an auxiliary file automatically loads all six axes under Unity CLR 2");
            Check(Array.TrueForAll(host.Pipeline.EnabledAxes,v=>v),"Successful UI import automatically selects all loaded send channels");
            var notice=(Text)Field(panel,"scriptNotice");
            Check(notice.text.Contains("6 / 6")&&notice.text.Contains("同步测试.pitch.funscript"),"Timeline lists the matched six-axis filenames");
            var offset=(InputField)Field(panel,"scriptOffset");offset.text="0.25";offset.onEndEdit.Invoke("0.25");
            Check(host.ScriptPlayback.Offset==.25,"Timeline offset numeric field updates the shared script clock");
            offset.text="bad";offset.onEndEdit.Invoke("bad");Check(host.ScriptPlayback.Offset==.25,"Invalid offset does not replace the valid playback value");
            offset.text="0";offset.onEndEdit.Invoke("0");
            var strip=(TimelineStrip)Field(panel,"laboratoryTrack");
            Check(strip.Scripts.Count==6&&strip.CurveAxis==0,"Imported scripts supply all-axis preview curves before playback sampling");
            host.Pipeline.Minimum=0;host.Pipeline.Maximum=1;for(int i=0;i<6;i++){host.Pipeline.AxisMinimum[i]=0;host.Pipeline.AxisMaximum[i]=1;host.Pipeline.AxisGain[i]=1;}
            host.Sync.Enabled=true;host.StartCoroutine((IEnumerator)Call(host,"SampleAfterAnimation"));
        }
        catch(Exception ex){Finish(ex);yield break;}
        yield return new WaitForSecondsRealtime(.3f);
        try
        {
            Check(host.ScriptSource!=null&&host.ScriptSource.ScriptMotion&&host.Pipeline.HasLiveSample,"Actual plugin sampling uses Timeline script output without a selected geometry source");
            for(int i=0;i<6;i++)Check(Math.Abs(host.Pipeline.Desired[i]-.5)<.0001&&host.Pipeline.AxisCalibrated[i],"Actual six-axis sampling follows the same Timeline time: "+MotionPipeline.Axes[i]);
            Call(panel,"ToggleMotionPlayback");Check(!TimelineState.Playing,"Timeline play/pause button controls the shared clock");
        }
        catch(Exception ex){Finish(ex);yield break;}
        yield return new WaitForSecondsRealtime(.2f);
        try
        {
            Check(!host.Pipeline.HasLiveSample&&!host.Pipeline.Armed&&host.Sync.Enabled,"Pause stops output and retains synchronization intent");
            var strip=(TimelineStrip)Field(panel,"laboratoryTrack");strip.Seek(.1);
            Call(panel,"ToggleMotionPlayback");
        }
        catch(Exception ex){Finish(ex);yield break;}
        yield return new WaitForSecondsRealtime(.2f);
        try
        {
            Check(Math.Abs(host.Pipeline.Desired[0]-.26)<.0001&&Math.Abs(host.Pipeline.Desired[5]-.46)<.0001,"Seeking backward through the ruler samples each axis at its own script position");
            TimelineState.Seek(1);TimelineState.SetPlaying(false);Call(panel,"RefreshFloatingTools");
            Check(((Text)Field(panel,"trackStatus")).text.Contains("1.000"),"Timeline playhead and status refresh with the alignment window closed");
            Check(!host.Devices.Connected&&!host.Devices.Connecting,"Funscript checks never connect a device");
            Call(panel,"CloseWorkspace");var window=(RectTransform)Field(panel,"timelineWindow");window.anchoredPosition=Vector2.zero;window.SetAsLastSibling();
            Call(panel,"RefreshFloatingTools");Canvas.ForceUpdateCanvases();
            Vector3[] corners=new Vector3[4];window.GetWorldCorners(corners);Vector3[] child=new Vector3[4];((Text)Field(panel,"scriptNotice")).rectTransform.GetWorldCorners(child);
            Check(child[0].y>=corners[0].y&&child[2].y<=corners[2].y,"All six-axis script rows fit inside the timeline window");
        }
        catch(Exception ex){Finish(ex);yield break;}
        yield return new WaitForEndOfFrame();
        try
        {
            var window=(RectTransform)Field(panel,"timelineWindow");Vector3[] corners=new Vector3[4];window.GetWorldCorners(corners);
            int x=Mathf.RoundToInt(corners[0].x),y=Mathf.RoundToInt(corners[0].y),w=Mathf.RoundToInt(corners[2].x-corners[0].x),h=Mathf.RoundToInt(corners[2].y-corners[0].y);
            var image=new Texture2D(w,h,TextureFormat.RGB24,false);image.ReadPixels(new Rect(x,y,w,h),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(Paths.GameRootPath,"funscript-timeline.png"),image.EncodeToPNG());Destroy(image);Finish(null);
        }
        catch(Exception ex){Finish(ex);}
    }
    private void Finish(Exception error)
    {
        if(host!=null)host.StopAllCoroutines();
        if(error!=null)report.AppendLine("ERROR "+error);
        report.AppendLine("Completed "+count+" funscript runtime checks");File.WriteAllText(Path.Combine(Paths.GameRootPath,"funscript-runtime-checks.txt"),report.ToString());Application.Quit();
    }
}
