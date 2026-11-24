using System;
using System.Collections.Generic;
using KoiSync.Core;
using KoiSync.Wire;
using UnityEngine;

namespace KoiSync.Plugin
{
    // The same Sideloader prefabs are used by Studio and the main game.
    // Skeleton registration and SR6 drive remain separate from asset loading.
    public sealed class ContactDevicePreview:MonoBehaviour
    {
        public const string AssetBundlePath="koisync/preview_assets.unity3d";
        // Koikatu renders characters and attached world-space helpers on layer 10.
        // Studio cameras commonly exclude Unity's Default layer, so every prefab
        // node must move to this layer after Sideloader instantiates it.
        private const int PreviewLayer=10;
        private GameObject sleeveInstance,rodInstance;
        private Transform sleeveEntry,rodBase;
        private readonly List<Material> materials=new List<Material>();
        private float sleeveAssetLength,rodAssetLength,rodAssetRadius,nextLoadAttempt;
        private string lastLoadError="";
        private NativeMotionSample sample;
        private ContactPreviewPose pose;
        private string driveStatus="等待 SR6 姿态";
        public DebugViewSettings Settings;
        public bool AssetsLoaded{get{return sleeveInstance!=null&&rodInstance!=null;}}
        public string Status{get;private set;}="等待有效性交绑定";

        public void Display(Candidate anchor,MotionPipeline pipeline,Candidate drive,bool show)
        {
            sample=anchor==null?null:anchor.Native;
            bool visible=show&&sample!=null&&sample.HasShaftPose&&sample.HasTargetFrame&&sample.ShaftRestLength>1e-7;
            pose=null;
            if(!visible){SetVisible(false);Status=!show?"飞机杯预览已隐藏":sample==null?"等待有效性交绑定":"当前来源不使用性交入口模型";return;}
            try
            {
                if(!EnsureResources()){SetVisible(false);return;}
                NativeMotionResult measured=NativeMotion.Solve(sample);double[] axes=(double[])measured.Normalized.Clone();bool externallyDriven;
                driveStatus=SelectDrive(axes,pipeline,drive,out externallyDriven);
                double modelLength=Settings!=null&&Settings.FollowVisibleRodLength&&measured.VisibleLength>1e-7?measured.VisibleLength:measured.Length;
                pose=ContactPreviewMotion.Solve(sample,externallyDriven?axes:null,modelLength,measured);
                ApplyPose();SetVisible(true);
                Status="zipmod 已加载 · "+driveStatus+" · "+(Settings!=null&&Settings.FollowVisibleRodLength?"BP 可见杆 ":"标定实体杆 ")+(pose.RodLength*1000).ToString("F1")+" mm · 接触 "+(pose.Engagement*100).ToString("F0")+"%";
            }
            catch(Exception ex){SetVisible(false);Status="预览姿态失败："+ex.Message;}
        }

        private static string SelectDrive(double[] axes,MotionPipeline pipeline,Candidate drive,out bool externallyDriven)
        {
            externallyDriven=false;
            bool script=drive!=null&&drive.ScriptMotion;
            if(pipeline!=null&&pipeline.ScriptMode)
            {
                if(pipeline.HasLiveSample&&CopyPipeline(axes,pipeline,pipeline.Values)){externallyDriven=true;return "SR6 脚本实际输出";}
                if(pipeline.HasDesired&&CopyPipeline(axes,pipeline,pipeline.Desired)){externallyDriven=true;return "SR6 脚本目标（暂停）";}
            }
            if(script&&CopyFinite(axes,drive.ScriptPositions)){externallyDriven=true;return drive.Active?"SR6 脚本姿态":"SR6 脚本姿态（暂停）";}
            if(pipeline!=null&&pipeline.NativeMode)
            {
                if(pipeline.HasLiveSample&&CopyPipeline(axes,pipeline,pipeline.Values)){externallyDriven=true;return "SR6 解算实际输出";}
                if(pipeline.HasDesired&&CopyPipeline(axes,pipeline,pipeline.Desired)){externallyDriven=true;return "SR6 解算目标（暂停）";}
            }
            return "性交骨架直接解算";
        }

        private static bool CopyPipeline(double[] target,MotionPipeline pipeline,double[] source)
        {
            if(source==null||source.Length<6)return false;bool copied=false;
            for(int i=0;i<6;i++)if(pipeline.AxisCalibrated[i]&&Numbers.Finite(source[i])){target[i]=source[i];copied=true;}
            return copied;
        }

        private static bool CopyFinite(double[] target,double[] source)
        {
            if(source==null||source.Length<6)return false;bool copied=false;
            for(int i=0;i<6;i++)if(Numbers.Finite(source[i])){target[i]=source[i];copied=true;}
            return copied;
        }

        private bool EnsureResources()
        {
            if(AssetsLoaded)return true;
            if(Time.unscaledTime<nextLoadAttempt)return false;
            nextLoadAttempt=Time.unscaledTime+5;
            try
            {
                GameObject sleevePrefab=CommonLib.LoadAsset<GameObject>(AssetBundlePath,"KoiSyncSleeve",false,"");
                GameObject rodPrefab=CommonLib.LoadAsset<GameObject>(AssetBundlePath,"KoiSyncFixedRod",false,"");
                if(sleevePrefab==null||rodPrefab==null)throw new InvalidOperationException("请安装 KoiSync Device Preview Assets.zipmod 至 mods/qinnyann/KoiSync");
                sleeveInstance=Instantiate(sleevePrefab);rodInstance=Instantiate(rodPrefab);
                PrepareInstance(sleeveInstance,"KoiSync · SR6 飞机杯");PrepareInstance(rodInstance,"KoiSync · 固定实体杆");
                sleeveEntry=sleeveInstance.transform.Find("Entry");rodBase=rodInstance.transform.Find("RodBase");
                Transform end=rodInstance.transform.Find("RodEnd");MeshFilter mesh=rodInstance.GetComponentInChildren<MeshFilter>(true),sleeveMesh=sleeveInstance.GetComponentInChildren<MeshFilter>(true);
                if(sleeveEntry==null||rodBase==null||end==null||mesh==null||mesh.sharedMesh==null||sleeveMesh==null||sleeveMesh.sharedMesh==null)throw new InvalidOperationException("设备 zipmod 缺少 Entry / RodBase / RodEnd 或设备网格");
                sleeveAssetLength=sleeveMesh.sharedMesh.bounds.size.y;
                rodAssetLength=(end.localPosition-rodBase.localPosition).magnitude;
                rodAssetRadius=mesh.sharedMesh.bounds.size.x*.5f;
                if(sleeveAssetLength<1e-7||rodAssetLength<1e-7||rodAssetRadius<1e-7)throw new InvalidOperationException("设备 zipmod 的模型尺寸无效");
                lastLoadError="";return true;
            }
            catch(Exception ex)
            {
                ReleaseInstances();Status="设备 zipmod 加载失败："+ex.Message;
                if(lastLoadError!=Status){lastLoadError=Status;Debug.LogWarning("KoiSync: "+Status);}return false;
            }
        }

        private void PrepareInstance(GameObject instance,string name)
        {
            instance.name=name;instance.hideFlags=HideFlags.DontSave;instance.transform.SetParent(transform,false);instance.SetActive(false);
            foreach(Transform node in instance.GetComponentsInChildren<Transform>(true))node.gameObject.layer=PreviewLayer;
            foreach(Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] shared=renderer.sharedMaterials;
                for(int i=0;i<shared.Length;i++)
                {
                    if(shared[i]==null||shared[i].shader==null||!shared[i].shader.isSupported)throw new InvalidOperationException("设备 zipmod 的材质或着色器不可用");
                    shared[i]=new Material(shared[i]){hideFlags=HideFlags.HideAndDontSave};materials.Add(shared[i]);
                }
                renderer.sharedMaterials=shared;
            }
        }

        private static Vector3 V(MotionVector value){return new Vector3((float)value.X,(float)value.Y,(float)value.Z);}

        private void ApplyPose()
        {
            AlignmentFrame frame=pose.Sleeve;Vector3 y=V(frame.Y).normalized,z=V(frame.Z).normalized;
            Quaternion rotation=Quaternion.LookRotation(z,y);float scale=Settings==null?1:Settings.ContactModelScale;
            var sleeveScale=new Vector3(scale,(float)pose.RodLength/sleeveAssetLength,scale);
            sleeveInstance.transform.rotation=rotation;sleeveInstance.transform.localScale=sleeveScale;
            sleeveInstance.transform.position=V(frame.Origin)-rotation*Vector3.Scale(sleeveEntry.localPosition,sleeveScale);
            Quaternion rodRotation=Quaternion.FromToRotation(Vector3.up,V(pose.RodDirection).normalized);
            float radius=Settings==null?.012f:Settings.FixedRodRadius;
            var rodScale=new Vector3(radius/rodAssetRadius,(float)pose.RodLength/rodAssetLength,radius/rodAssetRadius);
            rodInstance.transform.rotation=rodRotation;rodInstance.transform.localScale=rodScale;
            rodInstance.transform.position=V(pose.RodRoot)-rodRotation*Vector3.Scale(rodBase.localPosition,rodScale);
            int depth=(int)(Settings!=null&&Settings.XRay?UnityEngine.Rendering.CompareFunction.Always:UnityEngine.Rendering.CompareFunction.LessEqual);
            foreach(Material material in materials)material.SetInt("_ZTest",depth);
        }

        private void SetVisible(bool show)
        {
            if(sleeveInstance!=null&&sleeveInstance.activeSelf!=show)sleeveInstance.SetActive(show);
            if(rodInstance!=null&&rodInstance.activeSelf!=show)rodInstance.SetActive(show);
        }

        private void ReleaseInstances()
        {
            if(sleeveInstance!=null)Destroy(sleeveInstance);if(rodInstance!=null)Destroy(rodInstance);
            sleeveInstance=rodInstance=null;sleeveEntry=rodBase=null;
            foreach(Material material in materials)if(material!=null)Destroy(material);materials.Clear();
        }
        private void OnDisable(){SetVisible(false);}
        private void OnDestroy(){ReleaseInstances();}
    }
}
