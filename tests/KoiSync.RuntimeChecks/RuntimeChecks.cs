using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using KoiSync.Core;
using KoiSync.Wire;
using UnityEngine;
using UnityEngine.UI;

[BepInPlugin("local.koisync.runtimechecks","KoiSync runtime checks","0.1.0")]
[BepInDependency("local.koisync")]
public sealed class RuntimeChecks:BaseUnityPlugin
{
    private readonly StringBuilder report=new StringBuilder();
    private const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private string Output{get{return Path.Combine(Paths.GameRootPath,"native-runtime-checks.txt");}}
    private object Field(object o,string name){return o.GetType().GetField(name,F).GetValue(o);}
    private void Set(object o,string name,object value){o.GetType().GetField(name,F).SetValue(o,value);}
    private object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,F).Invoke(o,args);}
    private void Check(bool value,string message){if(!value)throw new Exception(message);report.AppendLine("PASS "+message);}
    private void Awake(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-runtime-checks")>=0)StartCoroutine(Run());}
    private IEnumerator Run()
    {
        Application.runInBackground=true;
        yield return new WaitForSecondsRealtime(10);
        KoiSync.Plugin.Plugin host=UnityEngine.Object.FindObjectOfType<KoiSync.Plugin.Plugin>();object panel=null;GameObject page=null;
        try
        {
            Check(host!=null,"Plugin loads in Unity "+Application.unityVersion);host.StopAllCoroutines();host.enabled=false;
            var pipeline=(MotionPipeline)Field(host,"Pipeline");object settings=Field(host,"Native");panel=Field(host,"panel");
            Check(((string)Field(settings,"MappingStatus")).Contains("268"),"Embedded animation table loads under CLR 2");
            Check(!(bool)Field(Field(host,"Devices"),"Connected"),"Runtime test opens no device connection");
            ((KoiSync.Plugin.RangeSlider)Field(panel,"outputRange")).SetRange(.1f,.9f);Call(panel,"UpdateRange");
            CheckTargetFrames(host.GetType().Assembly);
            Call(panel,"SetVisible",true);Call(panel,"ShowNativeSettings");page=(GameObject)Field(panel,"nativePage");
            Check(page.GetComponentsInChildren<Slider>().Length==6,"Six stage-gain sliders exist");
            Check(page.GetComponentsInChildren<InputField>().Length==12,"Six pairs of axis limits exist");
            Slider first=page.GetComponentsInChildren<Slider>()[0];first.value=(2f-.25f)/4.75f;
            Array gains=(Array)Field(settings,"Gains");object gain=gains.GetValue(0);Check(Math.Abs((float)gain.GetType().GetProperty("Value").GetValue(gain,null)-2)<.001,"Gain slider updates native configuration");
            Set(settings,"CurrentAnimation","Runtime neutral fixture");Set(settings,"CurrentProfile","RIGHTHAND");Set(settings,"SamplingStatus","synthetic reference vectors");
            var dropdown=(Dropdown)Field(panel,"nativeProfile");var choices=(NativeTarget[])Field(panel,"nativeChoices");dropdown.value=Array.IndexOf(choices,NativeTarget.RIGHTHAND);
            Call(settings,"SaveCurrent");Check(File.Exists((string)settings.GetType().GetProperty("MappingPath",F).GetValue(settings,null)),"Explicit animation mapping saves in isolated config directory");
            dropdown.value=0;
            var sample=new NativeMotionSample{Origin=new(0,0,0),End=new(.1,0,0),PlanePoint=new(0,0,-.1),TargetX=new(1,0,0),TargetZ=new(0,1,0),Gain=2};
            var candidate=new Candidate{Id="fixture",Label="Neutral reference fixture",ReferenceBone="Reference A",TargetBone="Target B",Active=true,Native=sample};
            var frame=new Snapshot{Session="runtime-checks",Context="Koikatu",Scene="Neutral fixture",State="WLoop / synthetic",Active=true};frame.Candidates.Add(candidate);
            for(int i=0;i<40;i++){frame.Sequence++;frame.SampleTime+=.02;sample.Target=new(i%2==0?.02:.04,.01,.015);pipeline.Update(frame,candidate,0,StrokeAxis.Z);}
            sample.Target=new(.03,.01,.015);for(int i=0;i<60;i++){frame.Sequence++;frame.SampleTime+=.02;pipeline.Update(frame,candidate,0,StrokeAxis.Z);}
            Set(host,"Frame",frame);Set(host,"Selected",candidate);Set(host,"SelectedId",candidate.Id);
            Check(pipeline.NativeMode&&pipeline.Calibrated&&!pipeline.Armed,"Native auto range maps without arming in actual Unity runtime");
            Check(Math.Abs(pipeline.Values[0]-.5)<.0001,"Native mapped midpoint is 50 percent");
            Call(panel,"Refresh");
            var geometry=(KoiSync.Plugin.DebugGeometry)Field(host,"geometry");geometry.Display(candidate,true,0);Check(geometry.EdgeCount>0,"Native reference frame builds actual world geometry");
        }
        catch(Exception ex){report.AppendLine("FAIL "+ex);File.WriteAllText(Output,report.ToString());Application.Quit();yield break;}
        yield return new WaitForEndOfFrame();
        try{Capture(page,"native-settings-v018.png");Call(panel,"ShowAxisMonitor");Call(panel,"Refresh");}
        catch(Exception ex){report.AppendLine("FAIL "+ex);}
        yield return new WaitForEndOfFrame();
        try
        {
            Capture((GameObject)Field(panel,"monitorPage"),"native-monitor-v018.png");
            Text[,] cells=(Text[,])Field(panel,"monitorCells");for(int i=0;i<6;i++)report.AppendLine(cells[i,0].text+" | "+cells[i,1].text+" | "+cells[i,2].text+" | "+cells[i,3].text+" | "+cells[i,4].text);
            Check(cells[0,1].text=="500"&&cells[0,2].text=="300"&&cells[0,3].text.Contains("0.030000"),"Monitor shows 0-999 input and mapped output alongside raw units");
            Check(((Text)Field(panel,"raw")).text.Contains("L0 300"),"Overview uses integer measurement input");
            Call(panel,"SetVisible",false);Call(panel,"Refresh");
            Check(((Text)Field(panel,"hudValues")).text.Contains("L0 300"),"In-scene HUD uses the same integer measurement input");
        }
        catch(Exception ex){report.AppendLine("FAIL "+ex);}
        File.WriteAllText(Output,report.ToString());Application.Quit();
    }
    private void Capture(GameObject page,string name)
    {
        Vector3[] corners=new Vector3[4];page.GetComponent<RectTransform>().GetWorldCorners(corners);
        int x=Mathf.RoundToInt(corners[0].x),y=Mathf.RoundToInt(corners[0].y),w=Mathf.RoundToInt(corners[2].x-corners[0].x),h=Mathf.RoundToInt(corners[2].y-corners[0].y);
        var texture=new Texture2D(w,h,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(x,y,w,h),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(Paths.GameRootPath,name),texture.EncodeToPNG());Destroy(texture);
    }
    private static MotionVector V(Vector3 v){return new MotionVector(v.x,v.y,v.z);}
    private void CheckTargetFrames(Assembly assembly)
    {
        var root=new GameObject("KoiSync synthetic target nodes");
        try
        {
            string[] names={"cf_j_hips","cf_j_kokan","cf_j_ana","cf_J_Mouthup","cf_J_MouthLow","cf_J_Mouth_L","cf_J_Mouth_R","k_f_munenipL_00","k_f_munenipR_00","cf_d_bust00","a_n_hand_L","cf_j_middle01_L","cf_j_ring01_L","a_n_hand_R","cf_j_middle01_R","cf_j_ring01_R","cf_j_foot_L","cf_j_toes_L","cf_j_foot_R","cf_j_toes_R"};
            Vector3[] points={new(0,1,0),new(0,0,0),new(0,0,-.1f),new(0,.1f,0),new(0,-.1f,0),new(-.1f,0,.1f),new(.1f,0,.1f),new(-.1f,0,.1f),new(.1f,0,.1f),new(0,0,0),new(0,0,0),new(.1f,0,0),new(.1f,.1f,0),new(0,0,0),new(.1f,0,0),new(.1f,.1f,0),new(0,0,0),new(0,0,.1f),new(0,0,0),new(0,0,.1f)};
            for(int i=0;i<names.Length;i++){var node=new GameObject(names[i]);node.transform.SetParent(root.transform,false);node.transform.localPosition=points[i];}
            object map=Activator.CreateInstance(assembly.GetType("KoiSync.Plugin.BoneMap"),F,null,new object[]{root.transform},null);
            MethodInfo frame=assembly.GetType("KoiSync.Plugin.MainGameAdapter").GetMethod("TargetFrame",BindingFlags.Static|BindingFlags.NonPublic);
            foreach(NativeTarget profile in new[]{NativeTarget.VAGINAL,NativeTarget.ANAL,NativeTarget.ORAL,NativeTarget.BREASTS,NativeTarget.LEFTHAND,NativeTarget.RIGHTHAND,NativeTarget.INTERCRURAL,NativeTarget.LEFTFOOT,NativeTarget.RIGHTFOOT})
            {
                root.transform.position=Vector3.zero;root.transform.rotation=Quaternion.identity;
                var sample=new NativeMotionSample{Origin=new(0,0,0),End=new(.1,0,0),PlanePoint=new(0,0,-.1)};frame.Invoke(null,new object[]{map,profile,sample});var first=NativeMotion.Solve(sample);
                root.transform.position=new Vector3(4,5,-3);root.transform.rotation=Quaternion.Euler(23,41,-18);
                sample.Origin=V(root.transform.TransformPoint(Vector3.zero));sample.End=V(root.transform.TransformPoint(new Vector3(.1f,0,0)));sample.PlanePoint=V(root.transform.TransformPoint(new Vector3(0,0,-.1f)));
                frame.Invoke(null,new object[]{map,profile,sample});var second=NativeMotion.Solve(sample);
                for(int i=0;i<6;i++)if(Math.Abs(first.Normalized[i]-second.Normalized[i])>.0001)throw new Exception(profile+" changes under rigid scene transform, axis "+i+": "+first.Normalized[i]+" -> "+second.Normalized[i]);
                Check(true,"Target frame rigid-transform invariance: "+profile);
            }
            root.transform.Find("cf_j_middle01_L").gameObject.SetActive(false);
            // Inactive transforms remain discoverable; missing ones must fail, never produce a fabricated zero point.
            DestroyImmediate(root.transform.Find("cf_j_middle01_L").gameObject);bool rejected=false;
            try{frame.Invoke(null,new object[]{map,NativeTarget.LEFTHAND,new NativeMotionSample()});}catch(TargetInvocationException ex){rejected=ex.InnerException is ArgumentException;}
            Check(rejected,"Destroyed required node rejects its measurement profile");
        }
        finally{Destroy(root);}
    }
}
