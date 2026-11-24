using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using KoiSync.Plugin;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed class NativePlaybackFixture:IDisposable
{
    private readonly List<AssetBundle> bundles=new List<AssetBundle>();
    private readonly List<GameObject> roots=new List<GameObject>();
    private readonly NativePlayback playback;
    private Animator male,female;
    private T Load<T>(string path,string name) where T:UnityEngine.Object
    {var b=AssetBundle.LoadFromFile(Path.Combine(BepInEx.Paths.GameRootPath,"abdata/"+path));bundles.Add(b);return b.LoadAsset<T>(name);}
    private Transform Copy(Transform original,Transform parent)
    {var go=new GameObject(original.name);go.transform.SetParent(parent,false);go.transform.localPosition=original.localPosition;go.transform.localRotation=original.localRotation;go.transform.localScale=original.localScale;foreach(Transform child in original)Copy(child,go.transform);return go.transform;}
    internal NativePlaybackFixture(NativePlayback controller,KoiSyncPanel panel,Action<bool,string> check)
    {
        playback=controller;
        var body=Load<GameObject>("chara/oo_base.unity3d","p_cf_body_bone");
        var mb=AssetBundle.LoadFromFile(Path.Combine(BepInEx.Paths.GameRootPath,"abdata/h/anim/male/02_00_00.unity3d"));bundles.Add(mb);
        var fb=AssetBundle.LoadFromFile(Path.Combine(BepInEx.Paths.GameRootPath,"abdata/h/anim/female/02_00_00.unity3d"));bundles.Add(fb);
        male=Rig(body,Illusion.Utils.Animator.SetupAnimatorOverrideController(mb.LoadAsset<RuntimeAnimatorController>("khs_m_base"),mb.LoadAsset<RuntimeAnimatorController>("khs_m_00")));
        female=Rig(body,Illusion.Utils.Animator.SetupAnimatorOverrideController(fb.LoadAsset<RuntimeAnimatorController>("khs_f_base"),fb.LoadAsset<RuntimeAnimatorController>("khs_f_00")));
        var boneReport=new System.Text.StringBuilder();
        foreach(var actor in new[]{male,female})foreach(var node in actor.GetComponentsInChildren<Transform>())
            if(node.name.ToLower().Contains("hips")||node.name.ToLower().Contains("spine")||node.name.ToLower().Contains("thigh00")||node.name.ToLower().Contains("waist"))boneReport.AppendLine((actor==male?"M ":"F ")+node.name+" p="+node.position.ToString("F6")+" up="+node.up.ToString("F6")+" right="+node.right.ToString("F6"));
        File.WriteAllText(Path.Combine(BepInEx.Paths.GameRootPath,"native-frame-bones-v0127.txt"),boneReport.ToString());
        male.speed=.75f;female.speed=1.25f;
        playback.Bind("playback fixture / WLoop","骨架动画播放检查 / WLoop",female,new[]{male,female});
        check(playback.Available&&playback.Duration>.1,"Current native clip duration is available from actual game controllers");
        playback.SetPlaying(false);check(playback.Paused&&male.speed==0&&female.speed==0,"Pause stops both actor animators");
        playback.Seek(playback.Duration*.72);
        check(Math.Abs(male.GetCurrentAnimatorStateInfo(0).normalizedTime-.72)<.01&&Math.Abs(female.GetCurrentAnimatorStateInfo(0).normalizedTime-.72)<.01,"Seeking places both real controllers at the same clip fraction");
        check(Math.Abs(playback.Time/playback.Duration-.72)<.01,"Native timeline playhead reports the sought position");
        var prefix=typeof(GameHooks).GetMethod("NativePlaybackUpdate",BindingFlags.NonPublic|BindingFlags.Static);
        check(!(bool)prefix.Invoke(null,null),"Paused native playback blocks H-scene advancement");
        EventSystem.current.SetSelectedGameObject(null);panel.PlaybackKeyDown=()=>true;panel.PollTimelineInput();
        check(!playback.Paused&&Math.Abs(male.speed-.75)<.001&&Math.Abs(female.speed-1.25)<.001,"J route restores each actor's prior speed");
        // The global travel page has no text fields any more, so the suppression path uses a fresh one.
        var input=(InputField)panel.GetType().GetMethod("Input",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(panel,new object[]{panel.transform,0f,0f,120f,"0"});
        roots.Add(input.gameObject);
        EventSystem.current.SetSelectedGameObject(input.gameObject);panel.PollTimelineInput();check(!playback.Paused,"Typing into a field suppresses the J shortcut");
        EventSystem.current.SetSelectedGameObject(null);panel.PollTimelineInput();check(playback.Paused,"J route pauses again");panel.PlaybackKeyDown=()=>UnityEngine.Input.GetKeyDown(KeyCode.J);
        // A body skeleton contains no visual character content.
        check(roots.TrueForAll(r=>r.GetComponentsInChildren<Renderer>().Length==0),"Playback regression uses skeletons without renderers");
    }
    private Animator Rig(GameObject body,RuntimeAnimatorController controller)
    {
        var root=new GameObject("Native playback skeleton fixture");roots.Add(root);var copy=Copy(body.transform,root.transform);var animator=copy.gameObject.AddComponent<Animator>();animator.avatar=body.GetComponent<Animator>().avatar;animator.runtimeAnimatorController=controller;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        foreach(var p in animator.parameters)if(p.type==AnimatorControllerParameterType.Float)animator.SetFloat(p.nameHash,p.name.ToLower().Contains("speed")?1:.5f);
        animator.Play("WLoop",0,.2f);animator.Update(0);return animator;
    }
    public void Dispose(){playback.Release();foreach(var root in roots)UnityEngine.Object.DestroyImmediate(root);foreach(var b in bundles)if(b!=null)b.Unload(false);}
}
