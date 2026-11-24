using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// One-shot, hot-reloadable Studio controller used for the v0.1.38 missionary
// binding check. It never opens a device transport and never saves the scene.
// Commands are read from missionary-live.command so leaving the helper installed
// cannot make a later Studio launch load a scene on its own.
public static class KoiSyncMissionaryLiveBridge
{
    public static void Main()
    {
        if (!string.Equals(BepInEx.Paths.ProcessName, "CharaStudio", StringComparison.OrdinalIgnoreCase)) return;
        Unload();
        var host = new GameObject("KoiSyncMissionaryLiveBridge");
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent<KoiSyncMissionaryLiveBehaviour>();
    }

    public static void Unload()
    {
        var old = GameObject.Find("KoiSyncMissionaryLiveBridge");
        if (old != null) UnityEngine.Object.Destroy(old);
    }
}

public sealed class KoiSyncMissionaryLiveBehaviour : MonoBehaviour
{
    private const string ScenePath = @"E:\Galgames\kk\UserData\Studio\scene\qinnyann\MMD_13_POV_TrimSelfFix\Single\MMD_04_table_missionary_loop_Native_BP.png";
    private const string ArtifactRoot = @"E:\project\c#\KoiSync\artifacts\v0138-missionary-binding";
    private const string CommandPath = ArtifactRoot + @"\missionary-live.command";
    private const string StatusPath = ArtifactRoot + @"\missionary-live.status.txt";
    private const string LogPath = ArtifactRoot + @"\missionary-live.log";
    private static readonly BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private bool busy;
    private bool playbackStarted;
    private float nextCommand;
    private float nextSample;
    private float nextStatus;
    private float sessionStart;
    private StreamWriter samples;
    private string samplePath;
    private Component plugin;

    private void Start()
    {
        Application.runInBackground = true;
        Directory.CreateDirectory(ArtifactRoot);
        AppendLog("bridge ready pid=" + System.Diagnostics.Process.GetCurrentProcess().Id);
    }

    private void Update()
    {
        if (Time.realtimeSinceStartup >= nextCommand)
        {
            nextCommand = Time.realtimeSinceStartup + 0.25f;
            PollCommand();
        }
        if (playbackStarted && Time.realtimeSinceStartup >= nextSample)
        {
            nextSample = Time.realtimeSinceStartup + 0.05f;
            WriteSample();
        }
    }

    private void PollCommand()
    {
        if (busy || !File.Exists(CommandPath)) return;
        string command = "";
        try { command = File.ReadAllText(CommandPath).Trim().ToLowerInvariant(); }
        catch (Exception e) { AppendLog("command read failed: " + e.Message); return; }
        try { File.Delete(CommandPath); } catch (Exception e) { AppendLog("command delete failed: " + e.Message); }
        if (command == "load" || command == "reload") StartCoroutine(LoadAndPlay());
        else if (command == "play") StartCoroutine(PlayCurrent());
        else AppendLog("ignored command=" + command);
    }

    private IEnumerator LoadAndPlay()
    {
        busy = true;
        playbackStarted = false;
        CloseSamples();
        sessionStart = Time.realtimeSinceStartup;
        WriteStatus("正在请求加载传教式场景……");
        AppendLog("LOAD begin scene=" + ScenePath);
        try
        {
            object timeline = TimelineInstance();
            Call(timeline, "Pause");
            Type studioType = Type.GetType("Studio.Studio, Assembly-CSharp");
            object studio = studioType == null ? null : UnityEngine.Object.FindObjectOfType(studioType);
            MethodInfo load = studioType == null ? null : studioType.GetMethod("LoadScene", F, null, new[] { typeof(string) }, null);
            if (studio == null || load == null) throw new InvalidOperationException("Studio.LoadScene(string) 不可用");
            object result = load.Invoke(studio, new object[] { ScenePath });
            AppendLog("LOAD call result=" + Text(result));
        }
        catch (Exception e)
        {
            AppendLog("LOAD ERROR " + BaseMessage(e));
            WriteStatus("场景加载调用失败：" + BaseMessage(e));
            busy = false;
            yield break;
        }

        // A 19 MB card and its Timeline extended data arrive in stages. Wait for
        // two characters plus a stable Studio object count, then give Timeline a
        // final two seconds to apply its tracks before starting playback.
        int previousCount = -1;
        float stableSince = -1f;
        float nextReport = 0f;
        while (Time.realtimeSinceStartup - sessionStart < 90f)
        {
            yield return new WaitForSecondsRealtime(0.25f);
            int objects = StudioObjectCount();
            int actors = ActorCount();
            object timeline = TimelineInstance();
            if (objects != previousCount)
            {
                previousCount = objects;
                stableSince = Time.realtimeSinceStartup;
            }
            float elapsed = Time.realtimeSinceStartup - sessionStart;
            if (elapsed >= nextReport)
            {
                nextReport = elapsed + 2f;
                string line = "加载中 " + elapsed.ToString("F1", CultureInfo.InvariantCulture) + "s · 对象 " + objects + " · 角色 " + actors + " · Timeline " + (timeline == null ? "未就绪" : "已就绪");
                WriteStatus(line);
                AppendLog(line);
            }
            if (elapsed >= 5f && actors >= 2 && timeline != null && stableSince > 0f && Time.realtimeSinceStartup - stableSince >= 3f) break;
        }
        yield return new WaitForSecondsRealtime(2f);
        busy = false;
        yield return StartCoroutine(PlayCurrent());
    }

    private IEnumerator PlayCurrent()
    {
        busy = true;
        float deadline = Time.realtimeSinceStartup + 20f;
        while (TimelineInstance() == null && Time.realtimeSinceStartup < deadline) yield return new WaitForSecondsRealtime(0.25f);
        plugin = FindPlugin();
        if (plugin != null)
        {
            Set(plugin, "DebugVisible", true);
            // Let normal v0.1.38 selection happen first. If this scene exposes
            // several candidates and nothing is selected, choose its explicit
            // BP vaginal attachment for this visual session without persisting it.
            yield return new WaitForSecondsRealtime(2f);
            EnsureExplicitBindingSelected(plugin);
        }
        object timeline = TimelineInstance();
        if (timeline == null)
        {
            AppendLog("PLAY ERROR Timeline instance not found");
            WriteStatus("Timeline 未就绪，无法开始播放");
            busy = false;
            yield break;
        }
        Time.timeScale = 1f;
        Call(timeline, "SeekPlaybackTime", 0f);
        Call(timeline, "Play");
        OpenSamples();
        playbackStarted = true;
        busy = false;
        AppendLog("PLAY started duration=" + Text(Get(timeline, "_duration")) + " sample=" + samplePath);
        WriteStatus("场景已加载并播放 · 正在采样 KoiSync 0.1.38 · 设备连接状态：" + DeviceState());
    }

    private void EnsureExplicitBindingSelected(Component instance)
    {
        if (Get(instance, "Selected") != null) return;
        object adapter = Get(instance, "StudioAdapter");
        IEnumerable sources = Get(adapter, "Sources") as IEnumerable;
        if (sources == null) return;
        object best = null;
        foreach (object source in sources)
        {
            if (source == null) continue;
            bool bp = Bool(Get(source, "Bp"));
            bool collider = Bool(Get(source, "IsCollider"));
            string profile = Text(Get(source, "Profile"));
            object entry = Get(source, "Entry");
            if (!collider && bp && entry != null && string.Equals(profile, "VAGINAL", StringComparison.OrdinalIgnoreCase)) { best = source; break; }
            if (best == null && !collider && entry != null) best = source;
        }
        if (best == null) return;
        string id = Text(Get(best, "Id"));
        Set(instance, "SelectedId", id);
        AppendLog("temporary visual selection id=" + id + " profile=" + Text(Get(best, "Profile")) + " entry=" + Text(Get(Get(best, "Entry"), "name")));
    }

    private void OpenSamples()
    {
        CloseSamples();
        samplePath = Path.Combine(ArtifactRoot, "missionary-live-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".tsv");
        samples = new StreamWriter(samplePath, false, new UTF8Encoding(true));
        samples.AutoFlush = true;
        samples.WriteLine("seconds\ttimelineTime\tplaying\tselectedId\tlabel\ttargetBone\tactive\trawL0\tdesiredL0\tvalueL0\trawL1\trawL2\trawR0\trawR1\trawR2\torigin\tend\ttarget\tshaftRestLength\tvisibleLength\tcontact\tpreviewStatus\tdeviceConnected");
        File.WriteAllText(Path.Combine(ArtifactRoot, "missionary-live-latest.txt"), samplePath + Environment.NewLine, Encoding.UTF8);
    }

    private void WriteSample()
    {
        try
        {
            if (plugin == null) plugin = FindPlugin();
            object timeline = TimelineInstance();
            object selected = Get(plugin, "Selected");
            object sample = Get(selected, "Native");
            object pipeline = Get(plugin, "Pipeline");
            double[] raw = Get(pipeline, "Raw") as double[];
            double[] desired = Get(pipeline, "Desired") as double[];
            double[] values = Get(pipeline, "Values") as double[];
            object contact = Get(sample, "Contact");
            string[] row = new[]
            {
                N(Time.realtimeSinceStartup - sessionStart), N(Get(timeline, "_playbackTime")), Text(Get(timeline, "_isPlaying")),
                Clean(Text(Get(plugin, "SelectedId"))), Clean(Text(Get(selected, "Label"))), Clean(Text(Get(selected, "TargetBone"))), Clean(Text(Get(selected, "Active"))),
                A(raw, 0), A(desired, 0), A(values, 0), A(raw, 1), A(raw, 2), A(raw, 3), A(raw, 4), A(raw, 5),
                Vec(Get(sample, "Origin")), Vec(Get(sample, "End")), Vec(Get(sample, "Target")), N(Get(sample, "ShaftRestLength")), N(Get(sample, "VisibleShaftLength")),
                Clean(Text(Get(contact, "Name"))), Clean(Text(Get(plugin, "ContactPreviewStatus"))), DeviceState()
            };
            if (samples != null) samples.WriteLine(string.Join("\t", row));
            if (Time.realtimeSinceStartup >= nextStatus)
            {
                nextStatus = Time.realtimeSinceStartup + 1f;
                double depth = ValueAt(raw, 0);
                double restLength = Number(Get(sample, "ShaftRestLength"));
                double normalizedL0 = restLength > 1e-7 ? depth / restLength : ValueAt(desired, 0);
                WriteStatus("播放中 · t=" + N(Get(timeline, "_playbackTime"))
                    + " · 深度=" + depth.ToString("F4", CultureInfo.InvariantCulture) + " m"
                    + " · L0=" + (normalizedL0 * 999).ToString("F0", CultureInfo.InvariantCulture)
                    + " · " + Clean(Text(Get(selected, "TargetBone"))) + " · 设备=" + DeviceState());
            }
        }
        catch (Exception e)
        {
            AppendLog("SAMPLE ERROR " + BaseMessage(e));
        }
    }

    private static Component FindPlugin()
    {
        foreach (MonoBehaviour value in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
            if (value != null && value.GetType().FullName == "KoiSync.Plugin.Plugin") return value;
        return null;
    }

    private string DeviceState()
    {
        object hub = Get(plugin, "Devices");
        return hub == null ? "unknown" : (Bool(Get(hub, "Connected")) ? "connected" : "disconnected");
    }

    private static object TimelineInstance()
    {
        Type type = Type.GetType("Timeline.Timeline, Timeline");
        FieldInfo field = type == null ? null : type.GetField("_self", F);
        return field == null ? null : field.GetValue(null);
    }

    private static int StudioObjectCount()
    {
        Type type = Type.GetType("Studio.Studio, Assembly-CSharp");
        object studio = type == null ? null : UnityEngine.Object.FindObjectOfType(type);
        ICollection objects = Get(studio, "dicObjectCtrl") as ICollection;
        return objects == null ? 0 : objects.Count;
    }

    private static int ActorCount()
    {
        int count = 0;
        foreach (ChaControl actor in UnityEngine.Object.FindObjectsOfType<ChaControl>()) if (actor != null && actor.gameObject.activeInHierarchy) count++;
        return count;
    }

    private static object Get(object owner, string name)
    {
        if (owner == null) return null;
        Type type = owner.GetType();
        FieldInfo field = type.GetField(name, F);
        if (field != null) return field.GetValue(owner);
        PropertyInfo property = type.GetProperty(name, F);
        if (property == null || property.GetIndexParameters().Length != 0) return null;
        try { return property.GetValue(owner, null); } catch { return null; }
    }

    private static void Set(object owner, string name, object value)
    {
        if (owner == null) return;
        Type type = owner.GetType();
        FieldInfo field = type.GetField(name, F);
        if (field != null) { field.SetValue(owner, value); return; }
        PropertyInfo property = type.GetProperty(name, F);
        if (property != null && property.CanWrite) property.SetValue(owner, value, null);
    }

    private static object Call(object owner, string name, params object[] args)
    {
        if (owner == null) return null;
        MethodInfo best = null;
        foreach (MethodInfo method in owner.GetType().GetMethods(F))
            if (method.Name == name && method.GetParameters().Length == args.Length) { best = method; break; }
        if (best == null) return null;
        try { return best.Invoke(owner, args); } catch (Exception e) { throw new InvalidOperationException(name + " failed: " + BaseMessage(e)); }
    }

    private static bool Bool(object value)
    {
        try { return value != null && Convert.ToBoolean(value, CultureInfo.InvariantCulture); } catch { return false; }
    }

    private static string A(double[] values, int index)
    {
        return values == null || index < 0 || index >= values.Length ? "" : values[index].ToString("F6", CultureInfo.InvariantCulture);
    }

    private static double ValueAt(double[] values, int index)
    {
        return values == null || index < 0 || index >= values.Length ? double.NaN : values[index];
    }

    private static double Number(object value)
    {
        if (value == null) return double.NaN;
        try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); } catch { return double.NaN; }
    }

    private static string N(object value)
    {
        if (value == null) return "";
        try { return Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("F6", CultureInfo.InvariantCulture); }
        catch { return Clean(Text(value)); }
    }

    private static string Vec(object vector)
    {
        if (vector == null) return "";
        return N(Get(vector, "X")) + "," + N(Get(vector, "Y")) + "," + N(Get(vector, "Z"));
    }

    private static string Text(object value) { return value == null ? "" : value.ToString(); }
    private static string Clean(string value) { return value == null ? "" : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
    private static string BaseMessage(Exception e) { return e == null ? "" : (e.GetBaseException() == null ? e.Message : e.GetBaseException().Message); }

    private static void WriteStatus(string value)
    {
        try { File.WriteAllText(StatusPath, DateTime.Now.ToString("s") + Environment.NewLine + value + Environment.NewLine, Encoding.UTF8); } catch { }
    }

    private static void AppendLog(string value)
    {
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("s") + " " + value + Environment.NewLine, Encoding.UTF8); } catch { }
    }

    private void CloseSamples()
    {
        if (samples != null) { try { samples.Dispose(); } catch { } samples = null; }
    }

    private void OnDestroy()
    {
        CloseSamples();
        AppendLog("bridge unloaded");
    }
}
