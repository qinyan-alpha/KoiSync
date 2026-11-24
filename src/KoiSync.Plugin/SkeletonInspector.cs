using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.Plugin
{
    public sealed class SkeletonInspector:MonoBehaviour
    {
        private Plugin host;
        private GameObject rig;
        private Camera view;
        private DebugGeometry skeleton,measurement;
        private readonly List<Body> bodies=new List<Body>();
        private readonly InspectionRange ranges=new InspectionRange();
        private Quaternion rotation;
        private Vector3 center;
        private float radius=.8f,zoom=1,next,nextBind;
        private string boundId="";
        private bool focus,measurementVisible=true;
        public RenderTexture Texture{get;private set;}
        public string Caption{get;private set;}
        public string Readings{get;private set;}
        public string RangeReadings{get;private set;}
        public string Calibration{get;private set;}
        public int JointCount{get;private set;}
        public int ActorCount{get{return bodies.Count;}}
        private static readonly Color[] BodyColors={new Color(.28f,.75f,1),new Color(1,.75f,.38f)};
        private static readonly string[] NodeNames={"hips","spine01","spine02","spine03","neck","head","shoulder_L","arm00_L","forearm01_L","hand_L","shoulder_R","arm00_R","forearm01_R","hand_R","thigh00_L","leg01_L","leg03_L","foot_L","toes_L","thigh00_R","leg01_R","leg03_R","foot_R","toes_R"};
        private static readonly int[,] Links={{0,1},{1,2},{2,3},{3,4},{4,5},{3,6},{6,7},{7,8},{8,9},{3,10},{10,11},{11,12},{12,13},{0,14},{14,15},{15,16},{16,17},{17,18},{0,19},{19,20},{20,21},{21,22},{22,23},{14,19},{7,11}};
        private sealed class Body{public Transform[] Joints;public string Name;}
        internal void Initialize(Plugin plugin)
        {
            host=plugin;rig=new GameObject("KoiSync full-body skeleton camera");DontDestroyOnLoad(rig);
            view=rig.AddComponent<Camera>();view.enabled=false;view.cullingMask=0;view.clearFlags=CameraClearFlags.SolidColor;view.backgroundColor=new Color32(10,20,30,255);
            view.orthographic=true;view.nearClipPlane=.01f;view.farClipPlane=100;
            Texture=new RenderTexture(1266,828,24);view.targetTexture=Texture;
            skeleton=rig.AddComponent<DebugGeometry>();skeleton.RenderCamera=view;skeleton.LabelsVisible=false;skeleton.Settings.Width=.008f;
            measurement=rig.AddComponent<DebugGeometry>();measurement.RenderCamera=view;measurement.LabelsVisible=false;
            measurement.Settings=new DebugViewSettings{Axes=true,Plane=false,Bounds=false,Arcs=true,Labels=false,Width=.0028f};
            MatchSceneView();Sample();
        }
        private void BindBodies()
        {
            bodies.Clear();foreach(ChaControl actor in host.InspectionActors())
            {
                Dictionary<string,Transform> bones=new Dictionary<string,Transform>(StringComparer.OrdinalIgnoreCase);
                Transform root=actor.objBodyBone!=null?actor.objBodyBone.transform:actor.transform;
                foreach(Transform t in root.GetComponentsInChildren<Transform>(true))if(!bones.ContainsKey(t.name))bones.Add(t.name,t);
                Body body=new Body{Name=actor.name,Joints=new Transform[NodeNames.Length]};
                for(int i=0;i<NodeNames.Length;i++){Transform t;if(bones.TryGetValue("cf_j_"+NodeNames[i],out t)||bones.TryGetValue("cm_j_"+NodeNames[i],out t))body.Joints[i]=t;}
                bodies.Add(body);
            }
        }
        internal void Sample()
        {
            if(host==null||!gameObject.activeInHierarchy||Time.realtimeSinceStartup<next)return;next=Time.realtimeSinceStartup+1f/30;
            string id=host.Frame.Session+"|"+host.Frame.Scene+"|"+host.SelectedId;
            if(id!=boundId||Time.realtimeSinceStartup>=nextBind){BindBodies();boundId=id;nextBind=Time.realtimeSinceStartup+1;}
            skeleton.BeginSkeleton();JointCount=0;bool hasBounds=false;Bounds bounds=new Bounds();
            for(int b=0;b<bodies.Count;b++)
            {
                Body body=bodies[b];Color color=BodyColors[b%BodyColors.Length];
                foreach(Transform joint in body.Joints)if(joint!=null)
                {
                    JointCount++;skeleton.SkeletonJoint(joint.position,color);
                    if(!hasBounds){bounds=new Bounds(joint.position,Vector3.zero);hasBounds=true;}else bounds.Encapsulate(joint.position);
                }
                for(int i=0;i<Links.GetLength(0);i++){Transform a=body.Joints[Links[i,0]],c=body.Joints[Links[i,1]];if(a!=null&&c!=null)skeleton.SkeletonLink(a.position,c.position,color);}
                Transform head=body.Joints[5],neck=body.Joints[4];
                if(head!=null){float r=neck==null?.055f:Mathf.Clamp(Vector3.Distance(head.position,neck.position)*.65f,.035f,.09f);skeleton.HeadRing(head.position,head.rotation,r,color);bounds.Encapsulate(head.position+Vector3.one*r);bounds.Encapsulate(head.position-Vector3.one*r);}
            }
            skeleton.EndSkeleton();
            var selected=host.Selected;
            measurement.Display(selected,measurementVisible&&hasBounds,(byte)host.Axis);
            if(hasBounds)
            {
                center=bounds.center;radius=Mathf.Max(.1f,bounds.extents.magnitude)*1.12f;
                if(focus&&selected!=null){Vector3 a=new Vector3(selected.Reference.X,selected.Reference.Y,selected.Reference.Z),b=new Vector3(selected.Target.X,selected.Target.Y,selected.Target.Z);center=(a+b)*.5f;radius=Mathf.Max(.18f,Vector3.Distance(a,b)*.8f);}
            }
            Caption=hasBounds?"当前角色真实骨架 · "+bodies.Count+" 人 / "+JointCount+" 个关节":"当前运动源没有可显示的角色骨架";
            if(selected!=null)
            {
                ranges.Observe(id+"|"+selected.MotionKey+"|"+host.Axis,host.Frame.Sequence,host.Pipeline.Raw);
                double[] r=host.Pipeline.Raw;
                Readings=string.Format(CultureInfo.InvariantCulture,"当前值（世界单位 / 角度）\nL0 {0:F4} u\nL1 {1:F4} u    L2 {2:F4} u\nR0 {3:F2}°\nR1 {4:F2}°    R2 {5:F2}°",r[0],r[1],r[2],r[3],r[4],r[5]);
                StringBuilder text=new StringBuilder("本次观察 · 最小 → 最大 / 跨度\n");
                for(int i=0;i<6;i++)text.AppendFormat(CultureInfo.InvariantCulture,"{0}  {1:F3} → {2:F3} / {3:F3}{4}\n",MotionPipeline.Axes[i],ranges.Minimum[i],ranges.Maximum[i],ranges.Maximum[i]-ranges.Minimum[i],i<3?" u":"°");
                RangeReadings=text.ToString();double lo,hi;
                Calibration=host.Pipeline.TryGetCalibrationRange(0,out lo,out hi)?string.Format(CultureInfo.InvariantCulture,"L0 校准输入：{0:F4} → {1:F4} u\n当前位置在该范围内：{2:F1}%\n此百分比尚未应用输出限幅和平滑。",lo,hi,100*Numbers.Clamp((r[0]-lo)/(hi-lo),0,1)):"L0 尚无有效校准范围。\n原始小数不是设备行程百分比。";
                if(host.Pipeline.NativeMode)Calibration=string.Format(CultureInfo.InvariantCulture,"参考长度：{0:F6} u\nL0 自动中点：{1:F6} · 倍率 ×{2:F2}\n整数刻度请查看六轴实时监控。",host.Pipeline.NativeLength,host.Pipeline.NativeMidpoint,host.Pipeline.NativeGain);
            }
            else{Readings="等待运动源";RangeReadings="";Calibration="";}
            Render();
        }
        public void ClearObservation(){ranges.Reset();}
        public void Orbit(float degrees){rotation=Quaternion.AngleAxis(degrees,Vector3.up)*rotation;Render();}
        public void Rotate(float yaw,float pitch){rotation=Quaternion.AngleAxis(yaw,Vector3.up)*rotation;rotation=Quaternion.AngleAxis(pitch,rotation*Vector3.right)*rotation;Render();}
        public void Tilt(float degrees){rotation=Quaternion.AngleAxis(degrees,rotation*Vector3.right)*rotation;Render();}
        public void Zoom(float factor){zoom=Mathf.Clamp(zoom*factor,.25f,2);Render();}
        public void FrameBody(){focus=false;zoom=1;next=0;Sample();}
        public void FocusMeasurement(){focus=true;zoom=1;next=0;Sample();}
        public void MatchSceneView(){rotation=Camera.main==null?Quaternion.Euler(15,-40,0):Camera.main.transform.rotation;Render();}
        public void SetMeasurementVisible(bool value){measurementVisible=value;next=0;Sample();}
        private void Render()
        {
            if(view==null)return;view.orthographicSize=radius*zoom;view.transform.rotation=rotation;view.transform.position=center+rotation*new Vector3(0,0,-radius*3-1);view.Render();
        }
        private void OnDestroy(){if(view!=null)view.targetTexture=null;if(rig!=null)Destroy(rig);if(Texture!=null){Texture.Release();Destroy(Texture);}}
    }
}
