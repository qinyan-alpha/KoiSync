using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Editor-only builder. The bundle contains no custom MonoBehaviour scripts.
public static class BuildPreviewAssets
{
    private const string Generated="Assets/Generated";
    private const string BundleName="preview_assets.unity3d";
    private const float RodRadius=.012f,RodLength=.18f;

    [Serializable] private sealed class Report
    {
        public string status="BUILT_OFFLINE_NOT_GAME_VALIDATED",unityVersion,bundle;
        public int sleeveVertices,sleeveTriangles,rodVertices,rodTriangles;
        public Vector3 sleeveSize,entrance,rodBase,rodEnd;
        public float rodRadius;
        public string[] prefabs={"KoiSyncSleeve","KoiSyncFixedRod"};
    }

    public static void Build()
    {
        try
        {
            string input=Argument("-koisyncMesh"),output=Argument("-koisyncOutput");
            Directory.CreateDirectory(Generated);Directory.CreateDirectory(output);
            var report=new Report{unityVersion=Application.unityVersion};
            float entranceY;
            Mesh sleeve=ReadSleeve(input,out entranceY);
            Mesh edges=BuildEdges(sleeve);
            Mesh rod=BuildRod(32);
            AssetDatabase.CreateAsset(sleeve,Generated+"/Sleeve.asset");
            AssetDatabase.CreateAsset(edges,Generated+"/SleeveEdges.asset");
            AssetDatabase.CreateAsset(rod,Generated+"/FixedRod.asset");
            var sleeveFill=MakeMaterial("SleeveFill",new Color(1,.18f,.48f,.18f));
            var sleeveEdge=MakeMaterial("SleeveEdges",new Color(1,.3f,.62f,.82f));
            var rodFill=MakeMaterial("FixedRod",new Color(.18f,.95f,1,.34f));

            var sleeveRoot=new GameObject("KoiSyncSleeve");
            new GameObject("Entry").transform.SetParent(sleeveRoot.transform,false);
            MakeRenderer(sleeveRoot,"SleeveFill",sleeve,sleeveFill);
            MakeRenderer(sleeveRoot,"SleeveEdges",edges,sleeveEdge);
            var rodRoot=new GameObject("KoiSyncFixedRod");
            new GameObject("RodBase").transform.SetParent(rodRoot.transform,false);
            var end=new GameObject("RodEnd");end.transform.SetParent(rodRoot.transform,false);end.transform.localPosition=Vector3.up*RodLength;
            MakeRenderer(rodRoot,"RodMesh",rod,rodFill);
            PrefabUtility.CreatePrefab(Generated+"/KoiSyncSleeve.prefab",sleeveRoot);
            PrefabUtility.CreatePrefab(Generated+"/KoiSyncFixedRod.prefab",rodRoot);
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();

            string[] assets={Generated+"/KoiSyncSleeve.prefab",Generated+"/KoiSyncFixedRod.prefab"};
            var build=new AssetBundleBuild{assetBundleName=BundleName,assetNames=assets};
            var built=BuildPipeline.BuildAssetBundles(output,new[]{build},BuildAssetBundleOptions.ChunkBasedCompression,BuildTarget.StandaloneWindows64);
            if(built==null)throw new InvalidOperationException("Unity did not produce an AssetBundle manifest");
            report.bundle=Path.GetFullPath(Path.Combine(output,BundleName));
            report.sleeveVertices=sleeve.vertexCount;report.sleeveTriangles=sleeve.triangles.Length/3;
            report.rodVertices=rod.vertexCount;report.rodTriangles=rod.triangles.Length/3;
            report.sleeveSize=sleeve.bounds.size;report.entrance=Vector3.zero;
            report.rodBase=Vector3.zero;report.rodEnd=Vector3.up*RodLength;report.rodRadius=RodRadius;
            RenderPreview(sleeveRoot,rodRoot,Path.Combine(output,"preview.png"));
            File.WriteAllText(Path.Combine(output,"unity-build.json"),JsonUtility.ToJson(report,true));
            UnityEngine.Object.DestroyImmediate(sleeveRoot);UnityEngine.Object.DestroyImmediate(rodRoot);
            Debug.Log("KOISYNC_PREVIEW_ASSETS_BUILT "+report.bundle);
            EditorApplication.Exit(0);
        }
        catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }

    private static string Argument(string name)
    {
        string[] arguments=Environment.GetCommandLineArgs();
        for(int i=0;i<arguments.Length-1;i++)if(arguments[i]==name)return arguments[i+1];
        throw new ArgumentException("Missing "+name);
    }

    private static Mesh ReadSleeve(string path,out float entranceY)
    {
        using(var reader=new BinaryReader(File.OpenRead(path)))
        {
            if(System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4))!="KSPM"||reader.ReadInt32()!=1)throw new InvalidDataException("Invalid KSPM source");
            int vertexCount=reader.ReadInt32(),indexCount=reader.ReadInt32();entranceY=reader.ReadSingle();reader.ReadSingle();
            if(vertexCount<3||vertexCount>65000||indexCount<3||indexCount%3!=0)throw new InvalidDataException("Invalid source geometry");
            var vertices=new Vector3[vertexCount];
            for(int i=0;i<vertices.Length;i++)vertices[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle()-entranceY,reader.ReadSingle());
            var triangles=new int[indexCount];
            for(int i=0;i<triangles.Length;i++){triangles[i]=reader.ReadInt32();if(triangles[i]<0||triangles[i]>=vertices.Length)throw new InvalidDataException("Invalid source triangle");}
            var mesh=new Mesh{name="KoiSync sleeve"};mesh.vertices=vertices;mesh.triangles=triangles;WhiteVertices(mesh);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }

    private static Mesh BuildEdges(Mesh source)
    {
        var unique=new HashSet<long>();var indices=new List<int>();var triangles=source.triangles;
        for(int i=0;i<triangles.Length;i+=3)
        {AddEdge(triangles[i],triangles[i+1],unique,indices);AddEdge(triangles[i+1],triangles[i+2],unique,indices);AddEdge(triangles[i+2],triangles[i],unique,indices);}
        var mesh=new Mesh{name="KoiSync sleeve wireframe"};mesh.vertices=source.vertices;WhiteVertices(mesh);mesh.SetIndices(indices.ToArray(),MeshTopology.Lines,0);mesh.RecalculateBounds();return mesh;
    }

    private static void AddEdge(int a,int b,HashSet<long> unique,List<int> indices)
    {int lo=Math.Min(a,b),hi=Math.Max(a,b);if(unique.Add(((long)lo<<32)|(uint)hi)){indices.Add(a);indices.Add(b);}}

    private static Mesh BuildRod(int sides)
    {
        var vertices=new Vector3[sides*2+2];var indices=new List<int>();
        for(int i=0;i<sides;i++){float angle=i*Mathf.PI*2/sides;vertices[i*2]=new Vector3(Mathf.Cos(angle)*RodRadius,0,Mathf.Sin(angle)*RodRadius);vertices[i*2+1]=vertices[i*2]+Vector3.up*RodLength;}
        int bottom=sides*2,top=bottom+1;vertices[top]=Vector3.up*RodLength;
        for(int i=0;i<sides;i++){int next=(i+1)%sides,a=i*2,b=next*2,c=a+1,d=b+1;indices.AddRange(new[]{a,c,d,a,d,b,bottom,b,a,top,c,d});}
        var mesh=new Mesh{name="KoiSync fixed physical rod"};mesh.vertices=vertices;mesh.triangles=indices.ToArray();WhiteVertices(mesh);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }

    private static void WhiteVertices(Mesh mesh)
    {var colors=new Color32[mesh.vertexCount];for(int i=0;i<colors.Length;i++)colors[i]=new Color32(255,255,255,255);mesh.colors32=colors;}

    private static Material MakeMaterial(string name,Color color)
    {
        Shader shader=Shader.Find("KoiSync/DevicePreview");if(shader==null)throw new InvalidOperationException("DevicePreview shader is missing");
        var material=new Material(shader){name=name};material.SetColor("_Color",color);material.SetInt("_ZTest",(int)CompareFunction.LessEqual);
        AssetDatabase.CreateAsset(material,Generated+"/"+name+".mat");return material;
    }

    private static void MakeRenderer(GameObject parent,string name,Mesh mesh,Material material)
    {
        var child=new GameObject(name);child.transform.SetParent(parent.transform,false);child.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=child.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
    }

    private static void RenderPreview(GameObject sleeve,GameObject rod,string path)
    {
        rod.transform.position=new Vector3(.115f,0,0);
        var cameraObject=new GameObject("Preview camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.05f,.08f,1);
        camera.orthographic=true;camera.orthographicSize=.16f;camera.nearClipPlane=.01f;camera.farClipPlane=10;
        camera.transform.position=new Vector3(.42f,.33f,-.55f);camera.transform.LookAt(new Vector3(.035f,.115f,0));
        var target=new RenderTexture(640,640,24);camera.targetTexture=target;camera.Render();RenderTexture.active=target;
        var image=new Texture2D(640,640,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,640,640),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
        camera.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(cameraObject);
        rod.transform.position=Vector3.zero;
    }
}
