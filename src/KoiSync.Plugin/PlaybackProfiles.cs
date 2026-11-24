using System;
using System.IO;
using System.Text;
using KoiSync.Core;
using LitJson;

namespace KoiSync.Plugin
{
    internal sealed class PlaybackProfiles
    {
        internal readonly string Root;
        internal SceneProfile Scene=new SceneProfile();
        internal AnimationProfiles Native=new AnimationProfiles();
        internal string ScenePath="",NativeKey="",Status="加载场景卡后自动读取分段配置";
        internal GlobalSettings Global;
        internal int Revision;
        internal bool Dirty;
        internal PlaybackProfiles(string gameRoot)
        {Root=Path.Combine(Path.Combine(gameRoot,"UserData"),"KoiSync");ReloadNative();}
        internal string SceneFile{get{return ScenePath.Length==0?"":Path.Combine(Path.Combine(Root,"cincaoizz"),Path.GetFileNameWithoutExtension(ScenePath)+".json");}}
        private string LegacyNativeFile{get{return Path.Combine(Root,"Animations.json");}}
        internal string NativeFile{get{return string.IsNullOrEmpty(NativeKey)?Path.Combine(Root,"Animations"):AnimationFile(NativeKey);}}
        internal string AnimationFile(string key)
        {
            var name=new StringBuilder("anim-");foreach(char c in key)if(c=='%'||Array.IndexOf(Path.GetInvalidFileNameChars(),c)>=0)name.Append('%').Append(((int)c).ToString("X4"));else name.Append(c);
            return Path.Combine(Path.Combine(Root,"Animations"),name+".json");
        }
        internal AnimationProfile Animation(string key)
        {
            if(string.IsNullOrEmpty(key))return null;
            var a=Native.Animations.Find(p=>p.Key==key);
            if(a==null)
            {
                a=new AnimationProfile{Key=key,Measurement=new MeasurementProfile{AlignmentVersion=1}};
                // A brand new animation starts from the global configuration instead of the type defaults.
                if(Global!=null)a.Measurement.NativeStroke=Global.ForAnimation(a.Measurement.NativeStroke);
                Native.Animations.Add(a);Revision++;
                try{Write(AnimationFile(key),a);}catch(Exception ex){Status="动画配置建立失败："+ex.Message;}
            }
            return a;
        }
        internal MeasurementProfile NativeMeasurement{get{var a=Animation(NativeKey);return a==null?null:a.Measurement;}}
        internal TimelineSegment CurrentSegment{get{return Scene.At(TimelineState.Time);}}
        internal MeasurementProfile StudioMeasurement{get{var s=CurrentSegment;return s==null?Scene.Defaults:s.Measurement;}}
        internal string StudioSource{get{var s=CurrentSegment;return s==null?Scene.DefaultSource:s.Source;}}
        internal void ResetScene(){ScenePath="";Scene=new SceneProfile();Dirty=false;Revision++;Status="新场景 · 保存场景卡后可保存分段";}
        internal void LoadScene(string path)
        {
            ScenePath=Path.GetFullPath(path);Scene=new SceneProfile{Scene=Path.GetFileName(path)};Dirty=false;Revision++;
            try{if(File.Exists(SceneFile)){var loaded=JsonMapper.ToObject<SceneProfile>(File.ReadAllText(SceneFile,Encoding.UTF8));loaded.Validate();Scene=loaded;Status="已自动加载 "+Path.GetFileName(SceneFile);}else Status="此场景尚无分段配置 · 可建立全长段";}
            catch(Exception ex){Status="场景配置读取失败："+ex.Message;}
        }
        internal void SceneSaved(string path)
        {ScenePath=Path.GetFullPath(path);Scene.Scene=Path.GetFileName(path);SaveScene();}
        internal void SaveScene()
        {
            if(ScenePath.Length==0){Status="尚未确定场景卡名称，请先在工作室保存或重新加载场景卡";return;}
            try{Scene.Validate();Write(SceneFile,Scene);Dirty=false;Status="已保存 "+Path.GetFileName(SceneFile);}catch(Exception ex){Status="保存失败："+ex.Message;}
        }
        internal void SaveNative()
        {try{foreach(var a in Native.Animations){a.Measurement.Validate();Write(AnimationFile(a.Key),a);}Dirty=false;Status="已保存各动画独立配置";}catch(Exception ex){Status="保存失败："+ex.Message;}}
        internal void SaveAnimation(string key)
        {try{var a=Animation(key);if(a==null)throw new ArgumentException("请先选择动画");a.Measurement.Validate();Write(AnimationFile(key),a);Dirty=false;Status="已保存动画："+key;}catch(Exception ex){Status="保存失败："+ex.Message;}}
        internal void ReloadNative()
        {
            try
            {
                var loaded=File.Exists(LegacyNativeFile)?JsonMapper.ToObject<AnimationProfiles>(File.ReadAllText(LegacyNativeFile,Encoding.UTF8)):new AnimationProfiles();
                if(loaded.Version!=1||loaded.Animations==null)throw new ArgumentException("本体配置版本无效");
                var keys=new System.Collections.Generic.HashSet<string>();foreach(var a in loaded.Animations){if(a==null||string.IsNullOrEmpty(a.Key)||!keys.Add(a.Key))throw new ArgumentException("动画标识重复或无效");a.Measurement.Validate();}
                string directory=Path.Combine(Root,"Animations");
                if(Directory.Exists(directory))foreach(string file in Directory.GetFiles(directory,"*.json"))
                {
                    var a=JsonMapper.ToObject<AnimationProfile>(File.ReadAllText(file,Encoding.UTF8));
                    if(a==null||string.IsNullOrEmpty(a.Key)||a.Measurement==null)throw new ArgumentException("动画文件缺少标识或配置："+Path.GetFileName(file));a.Measurement.Validate();
                    loaded.Animations.RemoveAll(p=>p.Key==a.Key);loaded.Animations.Add(a);
                }
                Native=loaded;Revision++;
            }
            catch(Exception ex){Status="本体配置读取失败："+ex.Message;}
        }
        internal void Changed(bool studio,string animation=null){Revision++;Dirty=true;if(studio)SaveScene();else SaveAnimation(animation??NativeKey);}
        // Global travel / pull-out / idle / climax configuration reaches every animation; each animation
        // keeps the stroke memory it measured for itself.
        internal void PushGlobalStroke(GlobalSettings settings)
        {
            if(settings==null)return;
            foreach(var a in Native.Animations)if(a!=null&&a.Measurement!=null)a.Measurement.NativeStroke=settings.ForAnimation(a.Measurement.NativeStroke);
            if(Scene.Defaults!=null)Scene.Defaults.NativeStroke=settings.ForAnimation(Scene.Defaults.NativeStroke);
            foreach(var s in Scene.Segments)if(s!=null&&s.Measurement!=null)s.Measurement.NativeStroke=settings.ForAnimation(s.Measurement.NativeStroke);
            Revision++;
        }
        private static void Write(string path,object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));string temporary=path+".tmp";
            using(var text=new StringWriter(System.Globalization.CultureInfo.InvariantCulture))
            {var writer=new JsonWriter(text){PrettyPrint=true};JsonMapper.ToJson(value,writer);File.WriteAllText(temporary,text.ToString(),Encoding.UTF8);}
            if(File.Exists(path))File.Replace(temporary,path,path+".bak");else File.Move(temporary,path);
        }
    }
}
