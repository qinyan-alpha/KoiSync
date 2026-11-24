using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using KoiSync.Core;
using KoiSync.Plugin;
using KoiSync.Wire;
using UnityEngine;
using Object=UnityEngine.Object;

public static class PreviewChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    [Serializable] public sealed class Check {public string description;public bool passed;}
    [Serializable] public sealed class Sample
    {public double[] axes;public Vector3 entry;public float rodLength;public string source;}
    [Serializable] public sealed class Result
    {
        public string status="running",process,pluginVersion,error="";
        public bool hardwareOpened;
        [NonSerialized] public List<Check> checks=new List<Check>();
        [NonSerialized] public List<Sample> samples=new List<Sample>();
        public int checkCount,studioItemsCreated;
        public int modelPixelCount;
        public bool renderVisible;
        public string[] checkDescriptions,driveSources;
        public bool[] checkPassed;
        public double[] sampledAxes;
        public float[] sampledEntryXYZ,rodLengths;
        public List<string> studioItems=new List<string>();
        public List<string> studioFactoryMethods=new List<string>();
    }

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);string process=Process.GetCurrentProcess().ProcessName;
        string report=Path.Combine(output,process+"-preview-assets.json");
        var result=new Result{process=process,pluginVersion=typeof(ContactDevicePreview).Assembly.GetName().Version.ToString()};
        GameObject root=null,cameraObject=null;
        try
        {
            Object plugin=Object.FindObjectOfType(typeof(Plugin));
            CheckResult(result,plugin!=null,"KoiSync loaded in "+process);
            object devices=typeof(Plugin).GetField("Devices",Fields).GetValue(plugin);
            CheckResult(result,!(bool)devices.GetType().GetField("Connected",Fields).GetValue(devices),"No physical device is connected");
            bool embedded=false;foreach(string name in typeof(ContactDevicePreview).Assembly.GetManifestResourceNames())embedded|=name.Contains("Fleshlight");
            CheckResult(result,!embedded,"No sleeve geometry is embedded in the plugin DLL");
            root=new GameObject("KoiSync zipmod verification");
            var preview=root.AddComponent<ContactDevicePreview>();preview.Settings=new DebugViewSettings{XRay=false};
            var identity=AlignmentFrame.Build(new MotionVector(),new MotionVector(1,0,0),new MotionVector(0,1,0));
            var native=new NativeMotionSample{HasShaftPose=true,HasTargetFrame=true,RootDistance=true,
                Origin=new MotionVector(0,-.1,0),End=new MotionVector(0,.1,0),Target=new MotionVector(),
                ReferenceFrame=identity,TargetReferenceFrame=identity,SourceRootDirection=new MotionVector(0,1,0),
                SourceDeformedDirection=new MotionVector(0,1,0),ShaftRestLength=.2,
                ShaftPath=new[]{new MotionVector(0,-.1,0),new MotionVector(0,.1,0)}};
            var anchor=new Candidate{Id="asset-test-anchor",Native=native};
            var script=new Candidate{Id="asset-test-script",Active=true,ScriptMotion=true};
            var pipeline=new MotionPipeline{Minimum=0,Maximum=1};
            var snapshot=new Snapshot{Session="asset-test",Scene="device-assets-only",Active=true};
            cameraObject=new GameObject("KoiSync asset verification camera");var camera=cameraObject.AddComponent<Camera>();
            camera.enabled=false;camera.cullingMask=1<<10;camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.035f,.05f,.08f,1);camera.orthographic=true;camera.orthographicSize=.22f;
            camera.nearClipPlane=.01f;camera.farClipPlane=5;camera.transform.position=new Vector3(.42f,.33f,-.55f);camera.transform.LookAt(new Vector3(0,.06f,0));

            int sequence=0;
            foreach(double[] axes in new[]{new[]{.5,.5,.5,.5,.5,.5},new[]{.8,.56,.43,.62,.73,.66}})
            {
                script.ScriptPositions=axes;snapshot.Sequence=++sequence;snapshot.SampleTime=sequence/60.0;
                pipeline.Update(snapshot,script,0,StrokeAxis.Z);preview.Display(anchor,pipeline,script,true);
                CheckResult(result,preview.AssetsLoaded,"Both Sideloader prefabs loaded");
                var pose=(ContactPreviewPose)typeof(ContactDevicePreview).GetField("pose",Fields).GetValue(preview);
                CheckResult(result,pose!=null,"SR6 pose was reconstructed");
                for(int i=0;i<6;i++)CheckResult(result,Math.Abs(pose.Axes[i]-pipeline.Values[i])<1e-7,"Prefab uses final pipeline axis "+MotionPipeline.Axes[i]);
                var sleeve=(GameObject)typeof(ContactDevicePreview).GetField("sleeveInstance",Fields).GetValue(preview);
                var rod=(GameObject)typeof(ContactDevicePreview).GetField("rodInstance",Fields).GetValue(preview);
                foreach(Transform t in sleeve.GetComponentsInChildren<Transform>(true))CheckResult(result,t.gameObject.layer==10,"Sleeve node uses Koikatu character layer: "+t.name);
                foreach(Transform t in rod.GetComponentsInChildren<Transform>(true))CheckResult(result,t.gameObject.layer==10,"Rod node uses Koikatu character layer: "+t.name);
                float rodLength=Vector3.Distance(rod.transform.Find("RodBase").position,rod.transform.Find("RodEnd").position);
                CheckResult(result,Math.Abs(rodLength-.2)<1e-6,"Fixed rod is 200 mm for both script poses");
                float sleeveAssetLength=(float)typeof(ContactDevicePreview).GetField("sleeveAssetLength",Fields).GetValue(preview);
                CheckResult(result,Math.Abs(sleeve.transform.localScale.y*sleeveAssetLength-rodLength)<1e-6,"Sleeve interaction channel length equals the displayed rod length");
                CheckResult(result,Vector3.Distance(sleeve.transform.Find("Entry").position,V(pose.Sleeve.Origin))<1e-6,"Entry marker follows solved position");
                CheckResult(result,Vector3.Dot(sleeve.transform.up,V(pose.Sleeve.Y))>.99999&&Vector3.Dot(sleeve.transform.forward,V(pose.Sleeve.Z))>.99999,"Prefab rotation follows full solved orientation");
                foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))CheckResult(result,renderer.sharedMaterial.shader.isSupported,"Shader supported: "+renderer.name);
                result.samples.Add(new Sample{axes=(double[])pipeline.Values.Clone(),entry=sleeve.transform.Find("Entry").position,rodLength=rodLength,source=preview.Status});
                Capture(camera,Path.Combine(output,process+(sequence==1?"-neutral.png":"-driven.png")));
            }
            CheckResult(result,Math.Abs(result.samples[1].entry.y-result.samples[0].entry.y+.06)<1e-6,"Scripted L0 delta moves sleeve 60 mm toward root");
            native.ShaftPath=new[]{new MotionVector(0,-.1,0),new MotionVector(0,0,0)};preview.Settings.FollowVisibleRodLength=true;
            preview.Display(anchor,pipeline,script,true);
            var visiblePose=(ContactPreviewPose)typeof(ContactDevicePreview).GetField("pose",Fields).GetValue(preview);
            var visibleSleeve=(GameObject)typeof(ContactDevicePreview).GetField("sleeveInstance",Fields).GetValue(preview);
            float visibleSleeveAssetLength=(float)typeof(ContactDevicePreview).GetField("sleeveAssetLength",Fields).GetValue(preview);
            CheckResult(result,Math.Abs(visiblePose.RodLength-.1)<1e-6,"Optional diagnostic mode follows the current visible BP chain length");
            CheckResult(result,Math.Abs(visibleSleeve.transform.localScale.y*visibleSleeveAssetLength-.1)<1e-6,"Sleeve channel follows the optional visible rod length too");
            preview.Settings.FollowVisibleRodLength=false;native.ShaftPath=new[]{new MotionVector(0,-.1,0),new MotionVector(0,.1,0)};
            var originalSleeve=(GameObject)typeof(ContactDevicePreview).GetField("sleeveInstance",Fields).GetValue(preview);
            var originalRod=(GameObject)typeof(ContactDevicePreview).GetField("rodInstance",Fields).GetValue(preview);
            preview.Display(anchor,pipeline,script,false);
            CheckResult(result,!originalSleeve.activeSelf&&!originalRod.activeSelf,"Hide disables both instances");
            preview.Display(anchor,pipeline,script,true);
            CheckResult(result,originalSleeve==(GameObject)typeof(ContactDevicePreview).GetField("sleeveInstance",Fields).GetValue(preview)&&originalRod==(GameObject)typeof(ContactDevicePreview).GetField("rodInstance",Fields).GetValue(preview),"Show reuses the original instances");
            preview.Settings.XRay=true;preview.Display(anchor,pipeline,script,true);
            foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))CheckResult(result,renderer.sharedMaterial.GetInt("_ZTest")==8,"X-ray toggle updates instance material: "+renderer.name);
            if(process=="CharaStudio")InspectStudio(result);
            result.status="passed";
        }
        catch(Exception ex){result.status="error";result.error=ex.ToString();}
        finally
        {
            result.checkCount=result.checks.Count;result.checkDescriptions=new string[result.checkCount];result.checkPassed=new bool[result.checkCount];
            for(int i=0;i<result.checkCount;i++){result.checkDescriptions[i]=result.checks[i].description;result.checkPassed[i]=result.checks[i].passed;}
            result.sampledAxes=new double[result.samples.Count*6];result.sampledEntryXYZ=new float[result.samples.Count*3];result.rodLengths=new float[result.samples.Count];result.driveSources=new string[result.samples.Count];
            for(int i=0;i<result.samples.Count;i++){Sample sample=result.samples[i];Array.Copy(sample.axes,0,result.sampledAxes,i*6,6);result.sampledEntryXYZ[i*3]=sample.entry.x;result.sampledEntryXYZ[i*3+1]=sample.entry.y;result.sampledEntryXYZ[i*3+2]=sample.entry.z;result.rodLengths[i]=sample.rodLength;result.driveSources[i]=sample.source;}
            if(process=="Koikatu"&&result.status=="passed"&&root!=null&&cameraObject!=null)
            {
                // The Script Loader callback can run before new renderers are
                // registered with the player. Validate the rendered result on
                // actual subsequent game frames, then release these instances.
                result.status="render_pending";
                root.AddComponent<PreviewAssetCaptureRunner>().Begin(cameraObject,report,Path.Combine(output,process+"-driven.png"),result);
            }
            else{if(root!=null)Object.Destroy(root);if(cameraObject!=null)Object.Destroy(cameraObject);}
            File.WriteAllText(report,JsonUtility.ToJson(result,true));
        }
    }

    static Vector3 V(MotionVector v){return new Vector3((float)v.X,(float)v.Y,(float)v.Z);}
    static void CheckResult(Result result,bool passed,string description)
    {result.checks.Add(new Check{description=description,passed=passed});if(!passed)throw new InvalidOperationException(description);}
    static void Capture(Camera camera,string path)
    {
        var target=new RenderTexture(640,640,24);var image=new Texture2D(640,640,TextureFormat.RGB24,false);
        RenderTexture previous=RenderTexture.active;
        try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,640,640),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
        finally{camera.targetTexture=null;RenderTexture.active=previous;Object.Destroy(target);Object.Destroy(image);}
    }
    static Type FindType(string name)
    {foreach(Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()){Type t=assembly.GetType(name);if(t!=null)return t;}throw new TypeLoadException(name);}
    static void InspectStudio(Result result)
    {
        Type type=FindType("Studio.Info");object info=Object.FindObjectOfType(type);
        var groups=(IDictionary)type.GetField("dicItemGroupCategory",Fields).GetValue(info);
        CheckResult(result,groups.Contains(736240),"Studio item group is registered");
        var lists=(IDictionary)type.GetField("dicItemLoadInfo",Fields).GetValue(info);
        CheckResult(result,lists.Contains(736240),"Studio item list is registered");
        var categories=(IDictionary)lists[736240];var items=(IDictionary)categories[1];
        CheckResult(result,items!=null&&items.Count==2,"Studio registered both standard items");
        foreach(DictionaryEntry item in items)
        {
            FieldInfo name=item.Value.GetType().GetField("name",Fields);
            result.studioItems.Add(item.Key+": "+(name==null?item.Value.ToString():name.GetValue(item.Value).ToString()));
        }
        Type factory=FindType("Studio.AddObjectItem");
        foreach(MethodInfo method in factory.GetMethods(BindingFlags.Public|BindingFlags.Static|BindingFlags.NonPublic))if(method.DeclaringType.FullName=="Studio.AddObjectItem")result.studioFactoryMethods.Add(method.ToString());
        Type studioType=FindType("Studio.Studio");object studio=Object.FindObjectOfType(studioType);
        var objects=(IDictionary)studioType.GetField("dicObjectCtrl",Fields).GetValue(studio);
        // Exercise native item creation only in the empty disposable test scene.
        if(objects.Count==0)
        {
            MethodInfo add=factory.GetMethod("Add",BindingFlags.Public|BindingFlags.Static);
            foreach(DictionaryEntry item in items)
            {
                object controller=add.Invoke(null,new object[]{736240,1,(int)item.Key});
                CheckResult(result,controller!=null,"Native Studio item factory creates "+item.Key);
                var go=(GameObject)controller.GetType().GetField("objectItem",Fields).GetValue(controller);
                CheckResult(result,go!=null&&go.GetComponentsInChildren<MeshFilter>(true).Length>0,"Created Studio item has its zipmod mesh");
                result.studioItemsCreated++;
            }
        }
    }
}

public sealed class PreviewAssetCaptureRunner:MonoBehaviour
{
    private GameObject cameraObject;
    private string report,path;
    private PreviewChecks.Result result;

    public void Begin(GameObject cameraObject,string report,string path,PreviewChecks.Result result)
    {this.cameraObject=cameraObject;this.report=report;this.path=path;this.result=result;StartCoroutine(Capture());}

    private IEnumerator Capture()
    {
        yield return null;yield return null;yield return new WaitForEndOfFrame();
        var target=new RenderTexture(640,640,24);var image=new Texture2D(640,640,TextureFormat.RGB24,false);
        Camera camera=cameraObject.GetComponent<Camera>();RenderTexture previous=RenderTexture.active;
        try
        {
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,640,640),0,0);image.Apply();
            Color32[] pixels=image.GetPixels32();int count=0;
            foreach(Color32 color in pixels)if(color.r>75||color.g>75||color.b>75)count++;
            result.modelPixelCount=count;result.renderVisible=count>100;
            result.status=result.renderVisible?"passed":"error";
            if(!result.renderVisible)result.error="The main-game asset camera rendered no visible model pixels after two game frames";
            File.WriteAllBytes(path,image.EncodeToPNG());
        }
        catch(Exception ex){result.status="error";result.error=ex.ToString();}
        finally
        {
            camera.targetTexture=null;RenderTexture.active=previous;Object.Destroy(target);Object.Destroy(image);
            File.WriteAllText(report,JsonUtility.ToJson(result,true));Object.Destroy(cameraObject);Object.Destroy(gameObject);
        }
    }
}
