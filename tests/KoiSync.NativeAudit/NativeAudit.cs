using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using KoiSync.Core;
using KoiSync.Plugin;
using UnityEngine;

[BepInPlugin("local.koisync.nativeaudit","KoiSync main animation audit","0.1.0")]
[BepInDependency("local.koisync")]
public sealed class NativeAudit:BaseUnityPlugin
{
    internal static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    internal static string Output= @"E:\project\c#\KoiSync\artifacts\native-audit-20260913";
    private bool auditing;
    private void Awake(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--koisync-native-audit")>=0){auditing=true;AudioListener.volume=0;Application.runInBackground=true;StartCoroutine(Run());}}
    private void LateUpdate(){if(auditing)AudioListener.volume=0;}
    private IEnumerator Run()
    {
        yield return new WaitForSecondsRealtime(8);
        Directory.CreateDirectory(Output);
        var log=new StringBuilder();GameObject owner=null;
        try
        {
            var host=UnityEngine.Object.FindObjectOfType<KoiSync.Plugin.Plugin>();host.StopAllCoroutines();host.enabled=false;host.Devices.Disconnect();
            log.AppendLine("Runtime="+Application.unityVersion+" process="+Paths.ProcessName+" plugin="+host.GetType().Assembly.GetName().Version+" device="+host.Devices.Connected);
            owner=new GameObject("Inactive catalog reader");owner.SetActive(false);var scene=owner.AddComponent<HSceneProc>();
            typeof(HSceneProc).GetMethod("CreateAllAnimationList",F).Invoke(scene,null);
            var rows=(IEnumerable)typeof(HSceneProc).GetField("lstAnimInfo",F).GetValue(scene);
            var records=new List<HSceneProc.AnimationListInfo>();foreach(IEnumerable group in rows)foreach(HSceneProc.AnimationListInfo info in group)records.Add(info);
            var catalog=new StringBuilder("mode\tid\tname\tkindHoushi\tisAnal\tmaleBaseBundle\tmaleBaseAsset\tmaleBundle\tmaleAsset\tfemaleBaseBundle\tfemaleBaseAsset\tfemaleBundle\tfemaleAsset\tsecondBaseBundle\tsecondBaseAsset\tsecondBundle\tsecondAsset\tmaleLayers\tfemaleLayers\tsecondLayers\n");
            foreach(var info in records)catalog.AppendLine(Tsv(info.mode,info.id,info.nameAnimation,info.kindHoushi,info.paramFemale.isAnal,info.pathMaleBase.assetpath,info.pathMaleBase.file,info.paramMale.path.assetpath,info.paramMale.path.file,info.pathFemaleBase.assetpath,info.pathFemaleBase.file,info.paramFemale.path.assetpath,info.paramFemale.path.file,info.pathFemaleBase1.assetpath,info.pathFemaleBase1.file,info.paramFemale1.path.assetpath,info.paramFemale1.path.file,Join(info.paramMale.lstIdLayer),Join(info.paramFemale.lstIdLayer),Join(info.paramFemale1.lstIdLayer)));
            File.WriteAllText(Path.Combine(Output,"catalog.tsv"),catalog.ToString(),Encoding.UTF8);log.AppendLine("Catalog="+records.Count);
            foreach(var relative in new[]{"chara/oo_base.unity3d","chara/mm_base.unity3d","h/anim/male/01_00_00.unity3d"}){
                var bundle=AssetBundle.LoadFromFile(Path.Combine(Paths.GameRootPath,"abdata/"+relative));if(bundle==null){log.AppendLine("No bundle "+relative);continue;}
                foreach(var name in bundle.GetAllAssetNames())log.AppendLine(relative+" :: "+name);
                bundle.Unload(false);
            }
            StartCoroutine(SkeletalAudit.Run(records));
        }
        catch(Exception ex){log.AppendLine("ERROR "+ex);}
        finally{if(owner!=null)Destroy(owner);File.WriteAllText(Path.Combine(Output,"catalog-run.txt"),log.ToString());}
    }
    internal static string Join(List<int> ids){return string.Join(",",ids.ConvertAll(i=>i.ToString()).ToArray());}
    internal static string Tsv(params object[] values){return string.Join("\t",Array.ConvertAll(values,v=>(v==null?"":v.ToString()).Replace("\t"," ").Replace("\r"," ").Replace("\n"," ")));}
}
