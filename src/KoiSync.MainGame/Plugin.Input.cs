using HarmonyLib;
using UnityEngine;

namespace KoiSync.MainGame
{
    public sealed partial class Plugin
    {
        private static Plugin inputOwner;
        private readonly Harmony inputHarmony=new Harmony("local.koisync.maingame.input");
        private void InitializeInput()
        {
            inputOwner=this;
            foreach(var name in new[]{"InputMouseProc","InputMouseWheelZoomProc"})
            {
                var method=AccessTools.Method(typeof(CameraControl_Ver2),name);
                if(method!=null)method=AccessTools.DeclaredMethod(method.DeclaringType,name);
                if(method!=null&&method.ReturnType==typeof(bool))inputHarmony.Patch(method,new HarmonyMethod(typeof(Plugin),"BlockPointer"));
            }
            var collision=AccessTools.Method(typeof(HandCtrl),"OnCollision");
            if(collision!=null&&collision.ReturnType==typeof(bool))inputHarmony.Patch(collision,new HarmonyMethod(typeof(Plugin),"BlockPointer"));
            var initialize=AccessTools.DeclaredMethod(typeof(Lookat_dan),"SetMale");
            if(initialize!=null)inputHarmony.Patch(initialize,null,new HarmonyMethod(typeof(Plugin),"CaptureNativeModelReference"));
        }
        private static void CaptureNativeModelReference(Lookat_dan __instance){NativeModelReference.Capture(__instance);}
        private static bool BlockPointer(ref bool __result)
        {
            if(inputOwner==null||!inputOwner.PointerCaptured())return true;
            __result=false;return false;
        }
        private void ReleaseInput(){inputHarmony.UnpatchSelf();if(inputOwner==this)inputOwner=null;}
    }
}
