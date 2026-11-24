using KoiSync.Wire;
using UnityEngine;

namespace KoiSync.Plugin
{
    public sealed class GeometryPreview:MonoBehaviour
    {
        private GameObject rig;
        private Camera cameraView;
        private DebugGeometry geometry;
        private Candidate fixture;
        private DebugViewSettings settings;
        private float yaw=-40,pitch=27,next;
        public RenderTexture Texture {get;private set;}
        public string Status {get{return geometry==null?"":geometry.RenderStatus;}}
        public void Initialize(DebugViewSettings options)
        {
            settings=options;fixture=DebugGeometry.Fixture();
            rig=new GameObject("KoiSync geometric preview");DontDestroyOnLoad(rig);
            cameraView=rig.AddComponent<Camera>();cameraView.enabled=false;cameraView.cullingMask=0;
            cameraView.clearFlags=CameraClearFlags.SolidColor;cameraView.backgroundColor=new Color32(12,23,34,255);
            cameraView.orthographic=true;cameraView.nearClipPlane=.01f;cameraView.farClipPlane=5;
            Texture=new RenderTexture(1198,722,24);cameraView.targetTexture=Texture;
            geometry=rig.AddComponent<DebugGeometry>();geometry.RenderCamera=cameraView;geometry.Settings=options;geometry.LabelsVisible=false;
            Render();
        }
        public void Orbit(float degrees){yaw+=degrees;Render();}
        public void Tilt(float degrees){pitch=Mathf.Clamp(pitch+degrees,-75,75);Render();}
        public void ResetView(){yaw=-40;pitch=27;Render();}
        private void Update(){if(Time.realtimeSinceStartup>=next){Render();next=Time.realtimeSinceStartup+.1f;}}
        public void Render()
        {
            if(cameraView==null)return;settings.Clamp();
            Vector3 center=new Vector3(.015f,.02f,.045f);
            cameraView.transform.position=center+Quaternion.Euler(pitch,yaw,0)*new Vector3(0,0,-1);
            cameraView.transform.LookAt(center);cameraView.orthographicSize=Mathf.Max(.19f,settings.Scale*.11f,settings.Extent*1.3f);
            geometry.Display(fixture,true,0);cameraView.Render();
        }
        private void OnDestroy()
        {
            if(cameraView!=null)cameraView.targetTexture=null;if(rig!=null)Destroy(rig);
            if(Texture!=null){Texture.Release();Destroy(Texture);}
        }
    }
}
