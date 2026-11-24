using System;
using System.Collections.Generic;
using KoiSync.Core;
using KoiSync.Plugin;
using UnityEngine;

internal static class ShaftGeometryChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var obj=new GameObject("Unnamed geometry fixture");var mesh=new Mesh();var p=new Vector3[8];
        for(int i=0;i<8;i++)p[i]=new Vector3((i&1)==0?0:1,(i&2)==0?-.02f:.02f,(i&4)==0?-.02f:.02f);
        mesh.vertices=p;mesh.triangles=new[]{0,2,4,2,6,4,1,5,3,3,5,7};mesh.RecalculateBounds();obj.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=obj.AddComponent<MeshRenderer>();
        try
        {
            var tip=ShaftGeometry.Tip(new Renderer[]{renderer},Vector3.zero);check((tip-new MotionVector(1,0,0)).Length<.0001,"Mesh tip is read without any named shaft bone");
            check(Math.Abs((ShaftGeometry.Tip(new Renderer[]{renderer},new Vector3(-.2f,0,0))-new MotionVector(-.2,0,0)).Length-1.2)<.0001,"Attachment position determines total effective shaft length");
            obj.transform.localScale=new Vector3(2,3,4);check((ShaftGeometry.Tip(new Renderer[]{renderer},Vector3.zero)-new MotionVector(2,0,0)).Length<.0001,"Mesh length includes nonuniform model scale");
            obj.transform.rotation=Quaternion.Euler(23,37,-13);obj.transform.position=new Vector3(3,4,-5);
            check((ShaftGeometry.Tip(new Renderer[]{renderer},obj.transform.position)-StudioSource.V(obj.transform.TransformPoint(Vector3.right))).Length<.001,"World rotation and translation preserve the actual mesh tip");
            obj.transform.position=Vector3.zero;obj.transform.rotation=Quaternion.identity;obj.transform.localScale=new Vector3(-2,1,1);
            check((ShaftGeometry.Tip(new Renderer[]{renderer},Vector3.zero)-new MotionVector(-2,0,0)).Length<.0001,"Reflected model selects its distal end rather than a positive world axis");
            obj.transform.localScale=Vector3.one;check(ShaftGeometry.Tip(new Renderer[]{renderer},Vector3.zero,true).Length<.0001,"Explicit reverse selects the other geometric end");
            renderer.enabled=false;var skinObj=new GameObject("Generic skinned mesh");skinObj.transform.SetParent(obj.transform,false);var skin=skinObj.AddComponent<SkinnedMeshRenderer>();
            var rootBone=new GameObject("Bone A").transform;rootBone.SetParent(obj.transform,false);var tipBone=new GameObject("Bone B").transform;tipBone.SetParent(rootBone,false);
            mesh.bindposes=new[]{rootBone.worldToLocalMatrix*skin.transform.localToWorldMatrix,tipBone.worldToLocalMatrix*skin.transform.localToWorldMatrix};
            var weights=new BoneWeight[8];var offsets=new Vector3[8];for(int i=0;i<8;i++){weights[i]=new BoneWeight{boneIndex0=(i&1)==0?0:1,weight0=1};if((i&1)!=0)offsets[i]=Vector3.right*.2f;}mesh.boneWeights=weights;
            mesh.AddBlendShapeFrame("Length",100,offsets,new Vector3[8],new Vector3[8]);skin.sharedMesh=mesh;skin.bones=new[]{rootBone,tipBone};skin.rootBone=rootBone;skin.SetBlendShapeWeight(0,50);
            check(Math.Abs(ShaftGeometry.Tip(new Renderer[]{skin},Vector3.zero).X-1.1)<.001,"Skinned measurement reads applied blendshape deformation");
            tipBone.localPosition=new Vector3(0,.2f,0);ShaftGeometry.Clear();tip=ShaftGeometry.Tip(new Renderer[]{skin},Vector3.zero);
            check(tip.Y>.19&&tip.X>1.09,"Skinned measurement follows posed generic bones");
        }
        finally{ShaftGeometry.Clear();UnityEngine.Object.Destroy(obj);UnityEngine.Object.Destroy(mesh);}
        VisibleChain(check);
    }
    private static void VisibleChain(Action<bool,string> check)
    {
        var fixture=new GameObject("Visible chain selection fixture");var mesh=new Mesh();mesh.vertices=new[]{Vector3.zero,Vector3.up,Vector3.right};mesh.triangles=new[]{0,1,2};
        try
        {
            var actor=new StudioObject{Root=fixture.transform,Character=true,Bones=new BoneMap(fixture.transform)};
            var targetRoot=new GameObject("Target");targetRoot.transform.SetParent(fixture.transform,false);var target=new StudioObject{Root=targetRoot.transform,Character=true};
            var roots=new Transform[2];var skins=new SkinnedMeshRenderer[2];
            for(int k=0;k<2;k++)
            {
                var model=new GameObject("penis duplicate "+k);model.transform.SetParent(fixture.transform,false);var bones=new Transform[8];var parent=model.transform;
                for(int i=0;i<8;i++){bones[i]=new GameObject("shaft_"+i.ToString("00")).transform;bones[i].SetParent(parent,false);parent=bones[i];}
                roots[k]=bones[0];skins[k]=model.AddComponent<SkinnedMeshRenderer>();skins[k].sharedMesh=mesh;skins[k].bones=bones;skins[k].enabled=k==0;
            }
            var graph=new StudioGraph(new List<StudioObject>{actor,target},new List<StudioLink>());
            var source=new StudioSource{Source=actor,Target=target,Graph=graph};var selected=ShaftGeometry.Resolve(source);
            check(selected.DrivenChain.Length==8&&selected.Root==roots[0],"Visible skinned-mesh bones select the real chain; hidden same-name clone is ignored");
            check(selected.BendStart==selected.DrivenChain[0]&&selected.BendEnd==selected.DrivenChain[1],"Unconstrained visible rig keeps actual root segment, without dormant BP substitution");
            skins[0].enabled=false;skins[1].enabled=true;selected=ShaftGeometry.Resolve(new StudioSource{Source=actor,Target=target,Graph=graph});
            check(selected.Root==roots[1],"Changing the visible model switches chain identity rather than matching a duplicate bone name");
            skins[0].enabled=true;bool rejected=false;try{ShaftGeometry.Resolve(new StudioSource{Source=actor,Target=target,Graph=graph});}catch(ArgumentException){rejected=true;}
            check(rejected,"Two equally visible source rigs require explicit binding");
        }
        finally{UnityEngine.Object.Destroy(fixture);UnityEngine.Object.Destroy(mesh);}
    }
}
