using System;
using System.Collections.Generic;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.MainGame
{
    public sealed class GamePreview:MonoBehaviour
    {
        private GameObject sleeve,rod;
        private Transform entry,rodBase;
        private float sleeveLength,rodLength,rodRadius,retryAt;
        private readonly List<Material> materials=new List<Material>();
        public string Status {get;private set;}=UiText.T("预览等待绑定");
        public bool Loaded {get{return sleeve!=null&&rod!=null;}}
        public void Display(ContactPreviewPose pose,bool show,bool xray,string status)
        {
            if(!show||pose==null){Hide();Status=UiText.T(show?"预览等待有效姿态":"模型预览已隐藏");return;}
            if(!EnsureAssets())return;
            DisplayPose(pose,xray);
            Status=status;
        }
        private void DisplayPose(ContactPreviewPose pose,bool xray)
        {
            var rotation=Quaternion.LookRotation(V(pose.Sleeve.Z),V(pose.Sleeve.Y));
            var size=new Vector3(1,(float)pose.RodLength/sleeveLength,1);
            sleeve.transform.rotation=rotation;sleeve.transform.localScale=size;
            sleeve.transform.position=V(pose.Sleeve.Origin)-rotation*Vector3.Scale(entry.localPosition,size);
            rotation=Quaternion.FromToRotation(Vector3.up,V(pose.RodDirection));
            size=new Vector3(.012f/rodRadius,(float)pose.RodLength/rodLength,.012f/rodRadius);
            rod.transform.rotation=rotation;rod.transform.localScale=size;
            rod.transform.position=V(pose.RodRoot)-rotation*Vector3.Scale(rodBase.localPosition,size);
            foreach(var material in materials)material.SetInt("_ZTest",(int)(xray?UnityEngine.Rendering.CompareFunction.Always:UnityEngine.Rendering.CompareFunction.LessEqual));
            sleeve.SetActive(true);rod.SetActive(true);
        }
        public void Hide(){if(sleeve!=null)sleeve.SetActive(false);if(rod!=null)rod.SetActive(false);}
        private bool EnsureAssets()
        {
            if(Loaded)return true;if(Time.unscaledTime<retryAt)return false;retryAt=Time.unscaledTime+5;
            try
            {
                var a=CommonLib.LoadAsset<GameObject>("koisync/preview_assets.unity3d","KoiSyncSleeve",false,"");
                var b=CommonLib.LoadAsset<GameObject>("koisync/preview_assets.unity3d","KoiSyncFixedRod",false,"");
                if(a==null||b==null)throw new InvalidOperationException(UiText.T("缺少 KoiSync 预览 zipmod"));
                sleeve=Instantiate(a);rod=Instantiate(b);Prepare(sleeve,"KoiSync 本体 · 杯口");Prepare(rod,"KoiSync 本体 · 来源标尺");
                entry=sleeve.transform.Find("Entry");rodBase=rod.transform.Find("RodBase");var end=rod.transform.Find("RodEnd");
                var sm=sleeve.GetComponentInChildren<MeshFilter>(true);var rm=rod.GetComponentInChildren<MeshFilter>(true);
                if(entry==null||rodBase==null||end==null||sm==null||rm==null||sm.sharedMesh==null||rm.sharedMesh==null)throw new InvalidOperationException(UiText.T("预览模型缺少锚点或网格"));
                sleeveLength=sm.sharedMesh.bounds.size.y;rodLength=(end.localPosition-rodBase.localPosition).magnitude;rodRadius=rm.sharedMesh.bounds.size.x*.5f;
                if(sleeveLength<=0||rodLength<=0||rodRadius<=0)throw new InvalidOperationException(UiText.T("预览模型长度无效"));
                return true;
            }
            catch(Exception error){Release();Status=error.Message;return false;}
        }
        private void Prepare(GameObject instance,string label)
        {
            instance.name=label;instance.hideFlags=HideFlags.DontSave;instance.transform.SetParent(transform,false);instance.SetActive(false);
            foreach(var node in instance.GetComponentsInChildren<Transform>(true))node.gameObject.layer=10;
            foreach(var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var shared=renderer.sharedMaterials;
                for(int i=0;i<shared.Length;i++)
                {
                    if(shared[i]==null||shared[i].shader==null||!shared[i].shader.isSupported)throw new InvalidOperationException(UiText.T("预览材质不可用"));
                    shared[i]=new Material(shared[i]){hideFlags=HideFlags.HideAndDontSave};materials.Add(shared[i]);
                }
                renderer.sharedMaterials=shared;
            }
        }
        private static Vector3 V(MotionVector point){return new Vector3((float)point.X,(float)point.Y,(float)point.Z);}
        private void Release()
        {if(sleeve!=null)Destroy(sleeve);if(rod!=null)Destroy(rod);sleeve=rod=null;foreach(var material in materials)if(material!=null)Destroy(material);materials.Clear();}
        private void OnDisable(){Hide();}
        private void OnDestroy(){Release();}
    }
}
