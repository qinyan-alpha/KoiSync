using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace KoiSync.Plugin
{
    internal sealed class GameHooks:IDisposable
    {
        private readonly Harmony harmony=new Harmony("local.koisync.playback");
        private readonly HashSet<MethodInfo> installed=new HashSet<MethodInfo>();
        private static Plugin host;
        private bool timelineReady;
        private static bool blockCamera;
        internal static Func<bool> CameraCapture;
        internal static bool BlockShortcuts;
        // Set while the pointer hovers KoiSync's timeline, so the playback key cannot also reach the
        // game's own shortcut handlers (which hide the interface in the studio and the H scene).
        internal static bool SuppressUiToggle;
        internal static bool BlockCamera{get{return CameraCapture==null?blockCamera:CameraCapture();}set{blockCamera=value;}}
        internal GameHooks(Plugin plugin)
        {
            host=plugin;
            foreach(string name in new[]{"Studio.CameraControl","BaseCameraControl","BaseCameraControl_Ver2","CameraControl_Ver2"})
            {
                Type type=StudioMembers.FindType(name);if(type==null)continue;
                foreach(string method in new[]{"InputMouseProc","InputMouseWheelZoomProc","InputKeyProc","InputTouchProc"})Patch(type,method,"CameraInput",null);
            }
            Type cursor=StudioMembers.FindType("GameCursor");Patch(cursor,"SetCursorLock","CursorLock",null);
            Patch(cursor,"Update","CursorUpdate",null);Patch(cursor,"LateUpdate","CursorUpdate",null);
            if(plugin.StudioAdapter!=null)
            {
                Patch(typeof(Studio.Studio),"InitScene",null,"SceneReset");
                // Studio polls these independently of the UI EventSystem. Otherwise R
                // and a right click in our editor can also operate the underlying scene.
                Patch(StudioMembers.FindType("Studio.ShortcutKeyCtrl"),"Update","ShortcutInput",null);
                Patch(StudioMembers.FindType("Studio.ShortcutMenuScene"),"Update","ShortcutInput",null);
            }
            else
            {
                Patch(typeof(HSceneProc),"Update","NativePlaybackUpdate",null);
                Patch(StudioMembers.FindType("VRHScene"),"Update","NativePlaybackUpdate",null);
                // SetPlay is the game's own state switch: it also drives animators, MotionIK, jiggle,
                // hands, items and collision toggles, so the climax shortcut reuses it verbatim.
                Patch(StudioMembers.FindType("HActionBase"),"SetPlay","CaptureAction",null);
                // The H-scene shortcut handlers also read keys that KoiSync binds to the timeline.
                Patch(StudioMembers.FindType("HTest"),"ShortCut","SkipShortCut",null);
                Patch(StudioMembers.FindType("HVRTest"),"ShortCut","SkipShortCut",null);
            }
            Refresh();
        }
        internal void Refresh()
        {
            if(timelineReady||host==null||host.StudioAdapter==null)return;
            Type type=StudioMembers.FindType("Timeline.Timeline");if(type==null)return;
            Patch(type,"OnSceneLoad",null,"SceneLoaded");Patch(type,"OnSceneSave",null,"SceneSaved");timelineReady=true;
        }
        private void Patch(Type type,string name,string prefix,string postfix)
        {
            if(type==null)return;
            var method=type.GetMethod(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
            if(method!=null&&method.DeclaringType!=type)method=method.DeclaringType.GetMethod(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
            if(method==null||installed.Contains(method))return;
            harmony.Patch(method,prefix==null?null:new HarmonyMethod(typeof(GameHooks),prefix),postfix==null?null:new HarmonyMethod(typeof(GameHooks),postfix));installed.Add(method);
        }
        private static bool CameraInput(ref bool __result){if(!BlockCamera)return true;__result=false;return false;}
        private static void CursorLock(ref bool __0){if(BlockCamera)__0=false;}
        private static bool CursorUpdate(){return !BlockCamera;}
        private static bool ShortcutInput(){return !BlockShortcuts&&!SuppressUiToggle;}
        private static bool SkipShortCut(){return !SuppressUiToggle;}
        private static bool NativePlaybackUpdate(){return host==null||host.NativePlayback==null||!host.NativePlayback.Paused;}
        // The main game keeps its H action object in a lambda closure, never in a field, so the only way
        // to reach it later is to remember it while the game itself changes animation state.
        private static object action;
        internal static object CurrentAction{get{return action;}}
        private static void CaptureAction(object __instance){if(__instance!=null)action=__instance;}
        private static void SceneLoaded(string __0){if(host!=null)host.Profiles.LoadScene(__0);}
        private static void SceneSaved(string __0){if(host!=null)host.Profiles.SceneSaved(__0);}
        private static void SceneReset(){if(host!=null)host.Profiles.ResetScene();}
        public void Dispose(){harmony.UnpatchSelf();CameraCapture=null;BlockCamera=BlockShortcuts=false;host=null;}
    }
}
