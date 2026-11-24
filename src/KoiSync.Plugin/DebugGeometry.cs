using System;
using System.Collections.Generic;
using KoiSync.Wire;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.Plugin
{
    // World-space mesh with square-section edges. Immediate drawing bypasses
    // Studio's layer mask without changing the user's camera configuration.
    public sealed class DebugGeometry:MonoBehaviour
    {
        private readonly List<Vector3> vertices=new List<Vector3>(4096);
        private readonly List<Color> vertexColors=new List<Color>(4096);
        private readonly List<int> indices=new List<int>(12000);
        private Mesh mesh;
        private Material material;
        private Candidate current;
        private Vector3[] axes;
        private Vector3 origin,target,local;
        private float[] angles;
        private GUIStyle style;
        private readonly List<Rect> labels=new List<Rect>();
        public DebugViewSettings Settings=new DebugViewSettings();
        public MotionPipeline MonitorPipeline;
        public Camera RenderCamera;
        public bool LabelsVisible=true;
        public int EdgeCount {get;private set;}
        internal BpDirectionObservation BpObservation;
        public int DrawCount {get;private set;}
        public string RenderStatus {get;private set;}
        private static readonly Color[] colors={new Color(.25f,1,.88f),new Color(1,.7f,.25f),new Color(.7f,.5f,1)};

        private bool Shown(AlignmentPart part){return current.Native==null||current.Native.ObjectVisible((int)part);}
        public void Display(Candidate candidate,bool visible,byte strokeAxis)
        {
            current=visible?candidate:null;vertices.Clear();vertexColors.Clear();indices.Clear();EdgeCount=0;
            if(mesh==null){mesh=new Mesh{name="KoiSync world wireframe"};mesh.MarkDynamic();}
            if(current==null){mesh.Clear();return;}
            Settings.Clamp();Pose a=current.Reference,b=current.Target;
            origin=new Vector3(a.X,a.Y,a.Z);target=new Vector3(b.X,b.Y,b.Z);
            Quaternion qa=new Quaternion(a.Qx,a.Qy,a.Qz,a.Qw),qb=new Quaternion(b.Qx,b.Qy,b.Qz,b.Qw);
            Vector3[] basis=strokeAxis==2?new[]{Vector3.right,Vector3.up,Vector3.forward}:
                strokeAxis==1?new[]{Vector3.up,Vector3.forward,Vector3.right}:new[]{Vector3.forward,Vector3.up,Vector3.right};
            axes=new[]{qa*basis[0],qa*basis[1],qa*basis[2]};Vector3[] targetAxes={qb*basis[0],qb*basis[1],qb*basis[2]};
            NativeMotionResult native=current.Native==null?null:NativeMotion.Solve(current.Native);
            if(native!=null)
            {
                axes=new[]{From(native.X),From(native.Y),From(native.Z)};
                MotionVector tx=current.Native.TargetX.Unit(),tz=current.Native.TargetZ.Unit();
                if(current.Native.RootDistance){var tilt=StudioRootMotion.TiltVector(current.Native);tx=tilt.Length>1e-7?tilt.Unit():current.Native.TargetReferenceFrame.Y;tz=current.Native.TargetReferenceFrame.Z;}
                var targetCross=MotionVector.Cross(tx,tz);targetAxes=new[]{From(tx),From(targetCross.Length>1e-7?targetCross.Unit():current.Native.TargetReferenceFrame.X),From(tz)};
                if(Shown(AlignmentPart.End)){Line(Color.white,.7f,origin,From(current.Native.End));Marker(From(current.Native.End),.005f*Settings.Scale,Color.white);}
                if(current.ReferencePath!=null&&Shown(AlignmentPart.End))
                {
                    Vector3[] path=new Vector3[current.ReferencePath.Length];
                    for(int i=0;i<path.Length;i++){path[i]=From(current.ReferencePath[i]);if(!current.Native.RootDistance)Marker(path[i],.0025f*Settings.Scale,Color.white);}
                    Line(current.Native.RootDistance?new Color(.7f,.7f,.7f,.5f):Color.white,.7f,path);
                }
                if(current.ColliderRadius>0&&Settings.Bounds){var shaft=From(current.ColliderB-current.ColliderA);ColliderOutline(From(current.ColliderA),From(current.ColliderB),(float)current.ColliderRadius,shaft.sqrMagnitude>1e-12f?shaft.normalized:axes[0]);}
            }
            Vector3 delta=target-origin;
            local=new Vector3(Vector3.Dot(delta,axes[0]),Vector3.Dot(delta,axes[1]),Vector3.Dot(delta,axes[2]));
            float[] distances={local.x,local.y,local.z};float size=Settings.Scale;
            for(int i=0;i<3;i++)
            {
                if(Settings.Axes){if(Shown(AlignmentPart.Root))Arrow(origin,axes[i],.12f*size,colors[i]);if(Shown(AlignmentPart.Target))Arrow(target,targetAxes[i],.075f*size,colors[i]*.85f);}
                if(Shown(AlignmentPart.Root)&&(current.Native==null||!current.Native.RootDistance||i>0))Line(colors[i],1.5f,origin,origin+axes[i]*distances[i]);
            }
            if(Shown(AlignmentPart.Root)&&Shown(AlignmentPart.Target))Line(current.Native!=null&&current.Native.RootDistance?colors[0]:Color.white,1.3f,origin,target);
            if(Shown(AlignmentPart.Root))Marker(origin,.006f*size,Color.white);if(Shown(AlignmentPart.Target))Marker(target,.006f*size,new Color(1,.4f,.55f));
            if(Settings.Bounds&&Shown(AlignmentPart.Plane)&&current.Native!=null&&current.Native.RootDistance)
            {
                Vector3 p=origin+axes[1]*distances[1]+axes[2]*distances[2];
                Line(colors[1],.6f,origin,origin+axes[1]*distances[1],p);
                Line(colors[2],.6f,origin,origin+axes[2]*distances[2],p);
                Line(new Color(.6f,.6f,.6f,.65f),.5f,target,p);
            }
            else if(Settings.Bounds&&(current.Native==null||!current.Native.RootDistance))
            {
                Vector3[] corners=new Vector3[8];
                for(int j=0;j<8;j++)corners[j]=origin+((j&1)!=0?axes[0]*distances[0]:Vector3.zero)+((j&2)!=0?axes[1]*distances[1]:Vector3.zero)+((j&4)!=0?axes[2]*distances[2]:Vector3.zero);
                for(int j=0;j<8;j++)for(int i=0;i<3;i++)if((j&(1<<i))==0)Line(colors[i]*.8f,.55f,corners[j],corners[j|(1<<i)]);
            }
            if(Settings.Plane&&Shown(AlignmentPart.Plane))
            {
                bool body=current.Native!=null&&current.Native.HasBodyFrame;
                Vector3 planeOrigin=body?From(current.Native.ReferenceFrame.Origin):current.Native!=null&&current.Native.Adjusted?From(current.Native.DisplayPlane):origin;
                Vector3 u=(body?From(current.Native.ReferenceFrame.X):axes[1])*(body?(float)current.Native.Width*.5f:Settings.Extent),v=(body?From(current.Native.ReferenceFrame.Z):axes[2])*(body?(float)current.Native.Depth*.5f:Settings.Extent);
                Line(new Color(.4f,.7f,.8f,.85f),.8f,planeOrigin-u-v,planeOrigin+u-v,planeOrigin+u+v,planeOrigin-u+v,planeOrigin-u-v);
                for(int i=-1;i<=1;i++){float t=i*.5f;Line(new Color(.4f,.7f,.8f,.35f),.35f,planeOrigin+u*t-v,planeOrigin+u*t+v);Line(new Color(.4f,.7f,.8f,.35f),.35f,planeOrigin+v*t-u,planeOrigin+v*t+u);}
                if(body){Marker(planeOrigin,.004f*size,new Color(.4f,.7f,.8f));Line(new Color(.4f,.7f,.8f),.5f,origin,planeOrigin);}
                else if(current.Native!=null&&current.Native.Adjusted){Marker(From(current.Native.PlanePoint),.004f*size,new Color(.4f,.7f,.8f));Line(new Color(.4f,.7f,.8f),.5f,origin,From(current.Native.PlanePoint));}
            }
            if(current.Native!=null&&current.Native.HasTargetFrame&&Settings.Plane&&Shown(AlignmentPart.TargetPlane))
            {
                var f=current.Native.TargetReferenceFrame;var line=current.Native.Contact==null?f.X:StudioContactMotion.Twist(current.Native);
                Vector3 p=From(f.Origin),right=From(line.Length>1e-7?line.Unit():f.X)*Settings.Extent;
                Line(new Color(1,.45f,.7f),2,p-right,p+right);Marker(p,.005f*size,new Color(1,.45f,.7f));
                var reference=current.Native.ReferenceFrame;if(Shown(AlignmentPart.Plane))Line(new Color(.4f,.75f,1),2,From(reference.Origin-reference.X*current.Native.Width*.5),From(reference.Origin+reference.X*current.Native.Width*.5));
                if(current.Native.RootDistance)Line(new Color(1,.7f,.8f),1.5f,p,p+From(StudioRootMotion.TiltVector(current.Native)));
            }
            double[] solved=native==null?RelativeMotion.Solve(current,(StrokeAxis)strokeAxis):native.Raw;angles=new[]{(float)solved[3],(float)solved[4],(float)solved[5]};
            if(Settings.Arcs&&Shown(AlignmentPart.TargetPlane))for(int i=0;i<3;i++)
            {
                Vector3[] arc=new Vector3[33];float radius=(.037f+i*.013f)*size;
                bool pelvis=i==0&&current.Native!=null&&current.Native.RootDistance&&current.Native.HasTargetFrame;
                Vector3 around=pelvis?From(current.Native.ReferenceFrame.Y):axes[i],start=pelvis?From(current.Native.ReferenceFrame.X):axes[(i+1)%3];
                for(int j=0;j<arc.Length;j++)arc[j]=origin+Quaternion.AngleAxis(angles[i]*j/(arc.Length-1),around)*start*radius;
                Line(colors[i],1,arc);Line(colors[i]*.6f,.45f,arc[0],origin,arc[arc.Length-1]);
            }
            if(BpObservation!=null&&BpObservation.Valid)
            {
                Vector3 entry=From(BpObservation.Entry),end=From(BpObservation.End);
                Color bpColor=new Color(1,.35f,.85f);
                Arrow(entry,(end-entry).normalized,(end-entry).magnitude,bpColor);
                Marker(entry,.005f*size,bpColor);Marker(end,.005f*size,bpColor);
            }
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetColors(vertexColors);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
        }
        private static Vector3 From(MotionVector v){return new Vector3((float)v.X,(float)v.Y,(float)v.Z);}
        private void ColliderOutline(Vector3 a,Vector3 b,float radius,Vector3 direction)
        {
            Vector3 u=Vector3.Cross(direction,Mathf.Abs(direction.y)<.9f?Vector3.up:Vector3.right).normalized,v=Vector3.Cross(direction,u).normalized;
            Color color=new Color(1,.85f,.4f,.9f);
            for(int end=0;end<2;end++)
            {
                Vector3 center=end==0?a:b;Vector3 outward=(end==0?-1:1)*(b-a).normalized;if((b-a).sqrMagnitude<1e-12f)outward=(end==0?-1:1)*direction;
                Vector3[] ring=new Vector3[25];for(int i=0;i<ring.Length;i++){float angle=i*Mathf.PI*2/24;ring[i]=center+radius*(Mathf.Cos(angle)*u+Mathf.Sin(angle)*v);}Line(color,.7f,ring);
                for(int side=0;side<4;side++)
                {
                    Vector3 radial=side==0?u:side==1?-u:side==2?v:-v;var arc=new Vector3[13];
                    for(int i=0;i<arc.Length;i++){float angle=i*Mathf.PI/24;arc[i]=center+radius*(Mathf.Cos(angle)*radial+Mathf.Sin(angle)*outward);}Line(color,.7f,arc);
                    if(end==0)Line(color,.7f,a+radial*radius,b+radial*radius);
                }
            }
        }
        internal void BeginSkeleton()
        {
            current=null;vertices.Clear();vertexColors.Clear();indices.Clear();EdgeCount=0;
            if(mesh==null){mesh=new Mesh{name="KoiSync skeleton"};mesh.MarkDynamic();}
        }
        internal void SkeletonLink(Vector3 a,Vector3 b,Color color){Line(color,1,a,b);}
        internal void SkeletonJoint(Vector3 p,Color color){Marker(p,.01f,color);}
        internal void HeadRing(Vector3 p,Quaternion rotation,float radius,Color color)
        {
            for(int axis=0;axis<3;axis++)
            {
                Vector3[] circle=new Vector3[25];
                for(int i=0;i<circle.Length;i++){float angle=i*Mathf.PI*2/(circle.Length-1);Vector3 v=axis==0?new Vector3(0,Mathf.Cos(angle),Mathf.Sin(angle)):axis==1?new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle)):new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0);circle[i]=p+rotation*v*radius;}
                Line(color,.5f,circle);
            }
        }
        internal void EndSkeleton(){mesh.Clear();mesh.SetVertices(vertices);mesh.SetColors(vertexColors);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();}
        internal void Arrow(Vector3 p,Vector3 direction,float length,Color color)
        {
            Vector3 tip=p+direction*length;Line(color,1,p,tip);
            Vector3 side=Vector3.Cross(direction,Mathf.Abs(direction.y)<.9f?Vector3.up:Vector3.right).normalized*length*.08f;
            Line(color,1,tip-direction*length*.16f+side,tip,tip-direction*length*.16f-side);
        }
        internal void Marker(Vector3 p,float r,Color color)
        {
            Vector3[] ring={p+Vector3.right*r,p+Vector3.forward*r,p-Vector3.right*r,p-Vector3.forward*r,p+Vector3.right*r};
            Line(color,.8f,ring);for(int i=0;i<4;i++)Line(color,.8f,p+Vector3.up*r,ring[i],p-Vector3.up*r);
        }
        internal void Line(Color color,float weight,params Vector3[] points)
        {
            for(int j=1;j<points.Length;j++)
            {
                Vector3 a=points[j-1],b=points[j],d=b-a;if(d.sqrMagnitude<1e-12f)continue;d.Normalize();
                float half=Settings.Width*weight*.5f;
                Vector3 u=Vector3.Cross(d,Mathf.Abs(d.y)<.9f?Vector3.up:Vector3.right).normalized*half,v=Vector3.Cross(d,u).normalized*half;
                int first=vertices.Count;
                Add(a-u-v,color);Add(a+u-v,color);Add(a+u+v,color);Add(a-u+v,color);
                Add(b-u-v,color);Add(b+u-v,color);Add(b+u+v,color);Add(b-u+v,color);
                for(int k=0;k<4;k++){int n=(k+1)%4;Triangle(first+k,first+n,first+n+4);Triangle(first+k,first+n+4,first+k+4);}
                EdgeCount++;
            }
        }
        private void Add(Vector3 p,Color c){vertices.Add(p);vertexColors.Add(c);}
        private void Triangle(int a,int b,int c){indices.Add(a);indices.Add(b);indices.Add(c);}
        private void OnRenderObject()
        {
            Camera camera=RenderCamera!=null?RenderCamera:Camera.main;
            if(mesh==null||EdgeCount==0||camera==null||Camera.current!=camera)return;
            if(material==null)
            {
                Shader shader=Shader.Find("Hidden/Internal-Colored");
                if(shader==null||!shader.isSupported){RenderStatus="当前环境缺少线框着色器";return;}
                material=new Material(shader);material.hideFlags=HideFlags.HideAndDontSave;
                material.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.SrcAlpha);material.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_Cull",(int)UnityEngine.Rendering.CullMode.Off);material.SetInt("_ZWrite",0);
            }
            material.SetInt("_ZTest",(int)(Settings.XRay?UnityEngine.Rendering.CompareFunction.Always:UnityEngine.Rendering.CompareFunction.LessEqual));
            if(material.SetPass(0)){Graphics.DrawMeshNow(mesh,Matrix4x4.identity);DrawCount++;RenderStatus=camera.name+" · "+EdgeCount+" 条世界空间边";}
        }
        private void OnGUI()
        {
            if(!LabelsVisible||!Settings.Labels||current==null||RenderCamera!=null||Camera.main==null||Event.current.type!=EventType.Repaint)return;
            if(style==null){style=new GUIStyle(GUI.skin.label);style.fontSize=13;}
            labels.Clear();if(Shown(AlignmentPart.Root))Label(origin,"A · "+current.ReferenceBone,Color.white);if(Shown(AlignmentPart.Target))Label(target,"B · "+current.TargetBone,new Color(1,.55f,.65f));
            if(current.Native!=null&&current.Native.RootDistance)
            {
                if(Shown(AlignmentPart.End))Label(From(current.Native.End),"E · "+current.Native.EndLabel,Color.white);
                if(Shown(AlignmentPart.TargetPlane))Label(From(current.Native.TargetReferenceFrame.Origin+StudioRootMotion.TiltVector(current.Native)),"H · "+current.Native.RotationLabel+"角度参考端",new Color(1,.7f,.8f));
            }
            if(current.Native!=null&&current.Native.HasTargetFrame&&current.Native.UseReferenceUp&&Shown(AlignmentPart.TargetPlane))Label(From(current.Native.TargetReferenceFrame.Origin),"T / R0 · "+current.Native.RotationLabel+"夹角 "+angles[0].ToString("F1")+"°",new Color(1,.45f,.7f));
            if(BpObservation!=null&&BpObservation.Valid)Label(From(BpObservation.End),"BP entry → inner · 偏差 "+BpObservation.Deviation.ToString("F1")+"°",new Color(1,.35f,.85f));
            float[] distances={local.x,local.y,local.z};
            if(current.Native!=null&&current.Native.RootDistance){var raw=NativeMotion.Solve(current.Native).Raw;for(int i=0;i<3;i++)distances[i]=(float)raw[i];}
            for(int i=0;i<3;i++)
            {
                string input=MonitorPipeline==null?"":"L"+i+" "+ChannelMonitor.InputText(MonitorPipeline,i)+"  /  R"+i+" "+ChannelMonitor.InputText(MonitorPipeline,i+3)+"  [0–999]\n";
                Label(origin+axes[i]*(.14f*Settings.Scale),input+"原始 "+distances[i].ToString("F4")+" u  /  "+angles[i].ToString("F1")+"°",colors[i]);
            }
        }
        private void Label(Vector3 world,string text,Color color)
        {
            Vector3 p=Camera.main.WorldToScreenPoint(world);if(p.z<=0||p.x<0||p.x>Screen.width||p.y<0||p.y>Screen.height)return;
            float height=text.IndexOf('\n')>=0?43:23;
            Rect r=new Rect(Mathf.Clamp(p.x+10,4,Mathf.Max(4,Screen.width-294)),Mathf.Clamp(Screen.height-p.y,4,Mathf.Max(4,Screen.height-height-4)),290,height);
            for(int n=0;n<8;n++){bool overlap=false;foreach(Rect other in labels)if(r.Overlaps(other)){overlap=true;break;}if(!overlap)break;r.y-=height+2;}
            if(r.y<0)return;labels.Add(r);GUI.Box(r,GUIContent.none);style.normal.textColor=color;GUI.Label(r,text,style);
        }
        private void OnDestroy(){if(material!=null)Destroy(material);if(mesh!=null)Destroy(mesh);}
        internal static Candidate Fixture()
        {
            Candidate c=new Candidate{ReferenceBone="Reference A",TargetBone="Target B",Active=true};c.Target.X=.055f;c.Target.Y=.06f;c.Target.Z=.115f;
            Quaternion q=Quaternion.Euler(24,-18,35);c.Target.Qx=q.x;c.Target.Qy=q.y;c.Target.Qz=q.z;c.Target.Qw=q.w;return c;
        }
        internal static void CaptureFixture(string path)
        {
            GameObject root=new GameObject("KoiSync diagnostic geometry");DebugGeometry display=root.AddComponent<DebugGeometry>();
            GameObject cameraObject=new GameObject("KoiSync diagnostic camera");Camera camera=cameraObject.AddComponent<Camera>();
            camera.enabled=false;camera.cullingMask=0;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color32(16,27,41,255);
            camera.orthographic=true;camera.orthographicSize=.18f;camera.nearClipPlane=.01f;camera.farClipPlane=5;
            camera.transform.position=new Vector3(.37f,.27f,-.4f);camera.transform.LookAt(new Vector3(.015f,.02f,.05f));
            display.RenderCamera=camera;display.Display(Fixture(),true,0);
            RenderTexture render=new RenderTexture(1000,760,24);camera.targetTexture=render;RenderTexture old=RenderTexture.active;
            Texture2D picture=new Texture2D(1000,760,TextureFormat.RGB24,false);
            try{camera.Render();RenderTexture.active=render;picture.ReadPixels(new Rect(0,0,1000,760),0,0);picture.Apply();System.IO.File.WriteAllBytes(path,picture.EncodeToPNG());}
            finally{RenderTexture.active=old;camera.targetTexture=null;render.Release();Destroy(render);Destroy(picture);Destroy(cameraObject);Destroy(root);}
        }
    }
}
