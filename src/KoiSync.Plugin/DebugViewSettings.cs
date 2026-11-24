using BepInEx.Configuration;
using UnityEngine;

namespace KoiSync.Plugin
{
    // Display preferences only; never used by the output pipeline.
    public sealed class DebugViewSettings
    {
        public bool Axes=true,Plane=true,Bounds=true,Arcs=true,Labels=true,XRay=true,ShowContactModel=true,FollowVisibleRodLength;
        // Global switch for the whole 3D wireframe layer; off by default so normal play stays clean.
        public bool ShowWireframe;
        public float Scale=1,Width=.0018f,Extent=.065f,ContactModelScale=1,FixedRodRadius=.012f;
        public void Reset(){ShowWireframe=FollowVisibleRodLength=false;Axes=Plane=Bounds=Arcs=Labels=XRay=ShowContactModel=true;Scale=1;Width=.0018f;Extent=.065f;ContactModelScale=1;FixedRodRadius=.012f;}
        public void Clamp(){Scale=Finite(Scale,.5f,3,1);Width=Finite(Width,.0005f,.006f,.0018f);Extent=Finite(Extent,.02f,.2f,.065f);ContactModelScale=Finite(ContactModelScale,.25f,3,1);FixedRodRadius=Finite(FixedRodRadius,.003f,.04f,.012f);}
        private static float Finite(float v,float lo,float hi,float fallback){return float.IsNaN(v)||float.IsInfinity(v)?fallback:Mathf.Clamp(v,lo,hi);}
        public void Load(ConfigFile config)
        {
            ShowWireframe=config.Bind("3D Display","ShowWireframe",false,"Show the 3D wireframe layer (F8 also toggles it for this session)").Value;
            Axes=config.Bind("3D Display","Axes",true,"Show reference and target axes").Value;
            Plane=config.Bind("3D Display","Plane",true,"Show reference grid").Value;
            Bounds=config.Bind("3D Display","Bounds",true,"Show projection box").Value;
            Arcs=config.Bind("3D Display","Arcs",true,"Show rotation arcs").Value;
            Labels=config.Bind("3D Display","Labels",true,"Show point labels").Value;
            XRay=config.Bind("3D Display","XRay",true,"Draw through geometry").Value;
            ShowContactModel=config.Bind("3D Display","ShowContactModel",true,"Show the script/output-driven in-world sleeve model and fixed physical shaft for intercourse bindings").Value;
            FollowVisibleRodLength=config.Bind("3D Display","FollowVisibleRodLength",false,"Use the current visible BP chain length for the diagnostic rod; off uses the calibrated physical length").Value;
            Scale=config.Bind("3D Display","Scale",1f,"Visual helper size only").Value;
            Width=config.Bind("3D Display","Width",.0018f,"World-space wire width").Value;
            Extent=config.Bind("3D Display","Extent",.065f,"Grid half extent in world units").Value;
            ContactModelScale=config.Bind("3D Display","ContactModelScale",1f,"Cross-section scale of the zipmod sleeve preview; channel length always matches the displayed rod").Value;
            FixedRodRadius=config.Bind("3D Display","FixedRodRadius",.012f,"Radius of the fixed physical shaft preview in world units").Value;Clamp();
        }
        public void Save(ConfigFile config)
        {
            Clamp();config.Bind("3D Display","ShowWireframe",false).Value=ShowWireframe;config.Bind("3D Display","Axes",true).Value=Axes;config.Bind("3D Display","Plane",true).Value=Plane;
            config.Bind("3D Display","Bounds",true).Value=Bounds;config.Bind("3D Display","Arcs",true).Value=Arcs;
            config.Bind("3D Display","Labels",true).Value=Labels;config.Bind("3D Display","XRay",true).Value=XRay;config.Bind("3D Display","ShowContactModel",true).Value=ShowContactModel;config.Bind("3D Display","FollowVisibleRodLength",false).Value=FollowVisibleRodLength;
            config.Bind("3D Display","Scale",1f).Value=Scale;config.Bind("3D Display","Width",.0018f).Value=Width;
            config.Bind("3D Display","Extent",.065f).Value=Extent;config.Bind("3D Display","ContactModelScale",1f).Value=ContactModelScale;config.Bind("3D Display","FixedRodRadius",.012f).Value=FixedRodRadius;config.Save();
        }
    }
}
