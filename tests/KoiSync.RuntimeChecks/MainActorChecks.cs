using System;
using System.Collections.Generic;
using System.Reflection;
using KoiSync.Plugin;
using UnityEngine;

internal static class MainActorChecks
{
    private sealed class SceneActors { public ChaControl male; public List<ChaControl> lstFemale=new List<ChaControl>(); }
    internal static void Run(Action<bool,string> check)
    {
        var root=new GameObject("Inactive main actor ownership fixtures");root.SetActive(false);
        var bundle=AssetBundle.LoadFromFile(System.IO.Path.Combine(BepInEx.Paths.GameRootPath,"abdata/h/anim/male/01_00_00.unity3d"));
        try
        {
            var controllers=bundle==null?new RuntimeAnimatorController[0]:bundle.LoadAllAssets<RuntimeAnimatorController>();
            check(controllers.Length>0,"Pose regression loads an actual game animation controller without playing it");var controller=controllers[0];
            var source=Actor(root,"Main source");var target=Actor(root,"Main target");var second=Actor(root,"Second target");
            var scene=new SceneActors{male=source,lstFemale=new List<ChaControl>{target,second}};ChaControl a,b;
            check(MainGameAdapter.TryActors(scene,null,false,0,out a,out b)&&a==source&&b==target,"Main sampling uses the current scene actor list without player metadata");
            check(MainGameAdapter.TryActors(scene,null,false,1,out a,out b)&&a==source&&b==second,"Main swap mapping selects only the indexed interaction actor");
            check(!MainGameAdapter.TryActors(scene,null,false,2,out a,out b),"Missing main target never falls back to another actor");
            scene.lstFemale[0]=source;check(!MainGameAdapter.TryActors(scene,null,false,0,out a,out b),"Main scene rejects a source paired with itself");scene.lstFemale[0]=target;
            scene.male=null;check(!MainGameAdapter.TryActors(scene,null,false,0,out a,out b),"Missing scene source is not borrowed from the target");scene.male=source;
            string status;
            check(!MainGameAdapter.TryPosedActors(source,target,out status)&&status.Contains("来源"),"Source without body animator cannot expose rest-pose geometry");
            SetAnimator(source,source.gameObject.AddComponent<Animator>());SetAnimator(target,target.gameObject.AddComponent<Animator>());
            target.animBody.runtimeAnimatorController=controller;
            check(!MainGameAdapter.TryPosedActors(source,target,out status)&&status.Contains("来源"),"Animated target with an unposed source reproduces and rejects the caress-scene bug");
            source.animBody.runtimeAnimatorController=controller;
            check(MainGameAdapter.TryPosedActors(source,target,out status),"Both loaded body controllers provide independent pose sources");
            source.visibleAll=false;
            check(MainGameAdapter.TryPosedActors(source,target,out status),"Hiding the source mesh does not invalidate its loaded animation");
            target.animBody.runtimeAnimatorController=null;
            check(!MainGameAdapter.TryPosedActors(source,target,out status)&&status.Contains("目标"),"Target controller unloading during a transition waits for a valid pose");
            check(!MainGameAdapter.TryPosedActors(source,source,out status),"Pose validation independently rejects self interaction");
        }
        finally { UnityEngine.Object.Destroy(root);if(bundle!=null)bundle.Unload(false); }
    }
    private static ChaControl Actor(GameObject parent,string name){var go=new GameObject(name);go.transform.SetParent(parent.transform,false);return go.AddComponent<ChaControl>();}
    private static void SetAnimator(ChaControl actor,Animator animator){typeof(ChaInfo).GetProperty("animBody",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetSetMethod(true).Invoke(actor,new object[]{animator});}
}
