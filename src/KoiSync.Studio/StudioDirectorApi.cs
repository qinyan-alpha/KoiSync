using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KoiSync.Core;
using LitJson;
using UnityEngine;

namespace KoiSync.Studio
{
    // Separate class so LitJSON is never referenced in Plugin's class metadata
    // before EmbeddedDependencies.Initialize() loads embedded LitJSON.dll in Awake().
    internal sealed class StudioDirectorApi
    {
        private readonly Plugin host;
        private static readonly string[] AxisNames = { "L0", "L1", "L2", "R0", "R1", "R2" };

        internal StudioDirectorApi(Plugin plugin)
        {
            host = plugin;
        }

        internal string Handle(StudioHttpRequest req)
        {
            string path = (req.Path ?? "/").TrimEnd('/');
            if (path.Length == 0) path = "/";

            if (req.Method == "GET" && (path == "/" || path == "/health" || path == "/state" || path == "/studio/state"))
                return BuildStateJson();

            if (req.Method == "GET" && (path == "/studio/segments" || path == "/segments"))
                return BuildSegmentsJson();

            if (req.Method == "GET" && path == "/studio/debug_shafts")
            {
                req.Deferred = true;
                host.EnqueueDirectorEndOfFrame(() =>
                {
                    try { req.Complete(200, BuildDebugShaftsJson()); }
                    catch (Exception ex) { req.Complete(500, StudioDirectorBridge.ErrorJson(ex.GetBaseException().Message)); }
                }, 0);
                return "";
            }

            JsonData body = ParseBody(req.Body);

            if (req.Method == "POST" && path == "/studio/segments")
                return HandleSaveSegments(body);

            if (req.Method == "POST" && path == "/studio/segments/reload")
            {
                bool loaded = host.ReloadDirectorPlanFromDisk();
                var res = BuildSegmentsData();
                res["reloadedFromDisk"] = loaded;
                return res.ToJson();
            }

            if (req.Method == "POST" && path == "/studio/chapters/rebuild")
            {
                host.DirectorTriggerAutoChapters();
                return BuildSegmentsJson();
            }

            if (req.Method == "POST" && path == "/studio/play_segment")
                return HandlePlaySegment(body);

            if (req.Method == "POST" && path == "/studio/seek")
                return HandleSeek(body);

            if (req.Method == "POST" && path == "/studio/speed")
                return HandleSpeed(body);

            if (req.Method == "POST" && path == "/studio/climax")
                return HandleClimax(body);

            if (req.Method == "POST" && path == "/studio/release")
                return HandleRelease(body);

            if (req.Method == "POST" && path == "/studio/record")
                return HandleRecord(body);

            if (req.Method == "POST" && path == "/studio/screenshot")
                return HandleScreenshot(req, body);

            if (req.Method == "POST" && path == "/studio/config")
                return HandleConfig(body);

            if (req.Method == "POST" && path == "/studio/load_scene")
                return HandleLoadScene(body);

            if (req.Method == "POST" && path == "/studio/feel")
                return HandleFeel(body);

            if (req.Method == "POST" && path == "/studio/step_bake")
                return HandleStepBake(body);

            if (req.Method == "POST" && path == "/studio/bake_scripts")
                return HandleBakeScripts(body);

            if (req.Method == "POST" && path == "/studio/configure_chapters")
                return HandleConfigureChapters(body);

            req.StatusCode = 404;
            return StudioDirectorBridge.ErrorJson("Unknown Studio Director endpoint: " + req.Method + " " + path);
        }

        internal int LoadPlanJson(string jsonText, double fallbackDuration)
        {
            JsonData root = ParseBody(jsonText);
            if (root == null || !root.IsObject)
                throw new ArgumentException("Invalid director plan JSON object");
            double dur = OptDouble(root, "duration", fallbackDuration);
            if (dur <= 0) dur = fallbackDuration;
            string sceneName = OptString(root, "sceneName", Path.GetFileNameWithoutExtension(host.DirectorScenePath ?? ""));
            var list = ParseSegmentsList(root);
            host.DirectorBook.LoadSegments(list, dur, sceneName, "director_json");
            return host.DirectorBook.Segments.Count;
        }

        private string BuildStateJson()
        {
            var res = new JsonData();
            res["ok"] = true;
            res["host"] = "KoiSync.Studio";
            res["version"] = "1.0.0";
            res["port"] = host.DirectorPort;
            res["scenePath"] = host.DirectorScenePath;
            res["sceneName"] = string.IsNullOrEmpty(host.DirectorScenePath) ? "" : Path.GetFileNameWithoutExtension(host.DirectorScenePath);
            res["scriptStem"] = host.DirectorScriptStem;
            res["directorPlanPath"] = host.DirectorPlanFilePath;
            res["recordedPath"] = host.DirectorRecordedPath;
            res["lastNote"] = host.DirectorLastNote ?? "";

            var clock = new JsonData();
            clock["available"] = host.Clock.Available;
            clock["playing"] = host.Clock.Playing;
            clock["time"] = Round3(host.Clock.Time);
            clock["duration"] = Round3(host.Clock.Duration);
            clock["speed"] = Round3(host.Clock.Speed);
            clock["canSetSpeed"] = host.Clock.CanSetSpeed;
            res["clock"] = clock;

            var effective = host.GetEffectiveDirectorBook();
            var loop = new JsonData();
            loop["mode"] = host.DirectorLoop.Mode.ToString().ToLowerInvariant();
            loop["loopCount"] = host.DirectorLoop.LoopCount;
            loop["elapsedSeconds"] = Round2(host.DirectorLoop.ActiveElapsedSeconds);
            loop["targetSpeed"] = Round3(host.DirectorLoop.TargetSpeed);
            loop["freezeCamera"] = host.DirectorLoop.FreezeCamera;
            loop["planSource"] = effective.SourceKind;
            loop["segmentCount"] = effective.Segments.Count;
            if (host.DirectorLoop.ActiveSegment != null)
                loop["activeSegment"] = SegmentToJson(host.DirectorLoop.ActiveSegment);
            var currentAt = effective.At(host.Clock.Time);
            if (currentAt != null)
                loop["segmentAtPlayhead"] = currentAt.Id;
            res["director"] = loop;

            var rec = new JsonData();
            rec["recording"] = host.DirectorIsRecording;
            rec["frames"] = host.DirectorRecordedFrames;
            rec["usingScripts"] = host.DirectorUsingScripts;
            rec["autoAnalysis"] = host.DirectorAutoAnalysis;
            res["recording"] = rec;

            var dev = new JsonData();
            dev["connected"] = host.Devices != null && host.Devices.Connected;
            dev["sendOutput"] = host.DirectorSendOutput;
            dev["climaxActive"] = host.DirectorClimaxActive;
            dev["feelPreset"] = host.Feel.Preset.ToString().ToLowerInvariant();
            var vals = host.DirectorOutputSnapshot;
            var axes = new JsonData();
            for (int i = 0; i < 6 && i < vals.Length; i++)
                axes[AxisNames[i]] = Round3(vals[i]);
            dev["output"] = axes;
            res["device"] = dev;

            var bg = new JsonData();
            bg["active"] = host.BodyGrind.Active;
            bg["mover"] = host.BodyGrind.ResolvedMover ?? "";
            bg["softBones"] = host.BodyGrind.SoftTissueBonesSynced;
            bg["hipDispMm"] = Round1(host.BodyGrind.PrimaryHipDispMm);
            bg["entryDriftMm"] = Round1(host.BodyGrind.PrimaryEntryDriftMm);
            res["bodyGrind"] = bg;

            var bake = new JsonData();
            bake["running"] = host.StepBakeRunning;
            bake["phase"] = host.StepBakePhaseName;
            bake["progress"] = Round3(host.StepBakeProgress);
            bake["status"] = host.StepBakeStatus;
            bake["reportPath"] = host.StepBakeReportPath;
            if (host.Binding != null) bake["binding"] = host.Binding.Description;
            if (host.LastStepBakeResult != null)
            {
                bake["chapters"] = host.LastStepBakeResult.Chapters.Count;
                bake["reviewCount"] = host.LastStepBakeResult.ReviewCount;
                bake["climaxCount"] = host.LastStepBakeResult.ClimaxCount;
            }
            res["stepBake"] = bake;

            var actorsArr = new JsonData();
            actorsArr.SetJsonType(JsonType.Array);
            var names = host.DirectorActorNamesSnapshot;
            for (int i = 0; i < names.Length; i++) actorsArr.Add(names[i]);
            res["actors"] = actorsArr;
            res["chapterCount"] = host.Book.Chapters.Count;
            return res.ToJson();
        }

        private JsonData BuildSegmentsData()
        {
            var effective = host.GetEffectiveDirectorBook();
            var res = new JsonData();
            res["ok"] = true;
            res["scenePath"] = host.DirectorScenePath;
            res["sceneName"] = effective.SceneName;
            res["directorPlanPath"] = host.DirectorPlanFilePath;
            res["source"] = effective.SourceKind;
            res["duration"] = Round3(host.Clock.Available ? host.Clock.Duration : effective.Duration);
            res["playhead"] = Round3(host.Clock.Time);

            var arr = new JsonData();
            arr.SetJsonType(JsonType.Array);
            for (int i = 0; i < effective.Segments.Count; i++)
                arr.Add(SegmentToJson(effective.Segments[i]));
            res["segments"] = arr;
            return res;
        }

        private string BuildSegmentsJson()
        {
            return BuildSegmentsData().ToJson();
        }

        private string HandleSaveSegments(JsonData body)
        {
            if (body == null || !body.IsObject || !HasKey(body, "segments") || !body["segments"].IsArray)
                throw new ArgumentException("segments array is required");

            double dur = OptDouble(body, "duration", host.Clock.Available ? host.Clock.Duration : 0);
            string sceneName = OptString(body, "sceneName", string.IsNullOrEmpty(host.DirectorScenePath) ? "" : Path.GetFileNameWithoutExtension(host.DirectorScenePath));
            var list = ParseSegmentsList(body);
            if (list.Count == 0) throw new ArgumentException("segments array must contain at least one valid segment");

            host.DirectorBook.LoadSegments(list, dur, sceneName, "director_json");

            bool persist = OptBool(body, "save", true);
            if (persist)
            {
                var doc = new JsonData();
                doc["schema"] = "koisync.studio.director.v1";
                doc["sceneName"] = host.DirectorBook.SceneName;
                doc["scenePath"] = host.DirectorScenePath;
                doc["duration"] = Round3(host.DirectorBook.Duration);
                var arr = new JsonData();
                arr.SetJsonType(JsonType.Array);
                for (int i = 0; i < host.DirectorBook.Segments.Count; i++)
                    arr.Add(SegmentToJson(host.DirectorBook.Segments[i]));
                doc["segments"] = arr;
                host.SaveDirectorPlanJson(doc.ToJson());
            }

            var res = BuildSegmentsData();
            res["saved"] = persist;
            return res.ToJson();
        }

        private string HandlePlaySegment(JsonData body)
        {
            string segQuery = OptString(body, "segment", OptString(body, "id", OptString(body, "role", "")));
            if (segQuery.Length == 0) throw new ArgumentException("segment (id, index, role, or label) is required");
            string modeStr = OptString(body, "mode", "loop").ToLowerInvariant();
            bool loop = modeStr != "once" && OptBool(body, "loop", true);
            double speed = OptDouble(body, "speed", 0);
            bool freezeCamera = OptBool(body, "freezeCamera", false);
            double durationSeconds = OptDouble(body, "durationSeconds", 0);
            string nextSeg = OptString(body, "nextSegment", "");
            bool pauseAtEnd = OptBool(body, "pauseAtEnd", false);

            host.DirectorPlaySegment(segQuery, loop, speed, freezeCamera, durationSeconds, nextSeg, pauseAtEnd);
            return BuildStateJson();
        }

        private string HandleSeek(JsonData body)
        {
            if (!HasKey(body, "time")) throw new ArgumentException("time (seconds) is required");
            double t = OptDouble(body, "time", 0);
            bool play = OptBool(body, "play", host.Clock.Playing);
            bool releaseLoop = OptBool(body, "releaseLoop", true);
            if (releaseLoop && host.DirectorLoop.Mode != StudioPlaybackMode.Linear)
                host.DirectorReleaseLinear(false, host.Clock.Speed);
            host.DirectorSafeSeek(t, play, true);
            return BuildStateJson();
        }

        private string HandleSpeed(JsonData body)
        {
            if (!HasKey(body, "speed")) throw new ArgumentException("speed is required");
            double speed = OptDouble(body, "speed", 1.0);
            host.DirectorSetSpeed(speed);
            return BuildStateJson();
        }

        private string HandleClimax(JsonData body)
        {
            string seg = OptString(body, "segment", "");
            double speed = OptDouble(body, "speed", 1.0);
            string next = OptString(body, "nextSegment", "");
            bool pauseAtEnd = OptBool(body, "pauseAtEnd", true);
            host.DirectorGotoClimax(seg, speed, next, pauseAtEnd);
            return BuildStateJson();
        }

        private string HandleRelease(JsonData body)
        {
            bool resumePlay = OptBool(body, "resumePlay", OptBool(body, "resumeLinear", true));
            double speed = OptDouble(body, "speed", 1.0);
            host.DirectorReleaseLinear(resumePlay, speed);
            return BuildStateJson();
        }

        private string HandleRecord(JsonData body)
        {
            string action = OptString(body, "action", "start_full").ToLowerInvariant();
            if (action == "stop")
            {
                host.DirectorStopRecording();
            }
            else if (action == "start" || action == "start_full" || action == "full")
            {
                bool fullPass = action != "start";
                double speed = OptDouble(body, "speed", 1.0);
                bool enableAnalysis = OptBool(body, "enableAnalysis", true);
                host.DirectorStartRecording(fullPass, speed, enableAnalysis);
            }
            else
            {
                throw new ArgumentException("action must be start_full, start, or stop");
            }
            return BuildStateJson();
        }

        private string HandleScreenshot(StudioHttpRequest req, JsonData body)
        {
            int width = (int)Numbers.Clamp(OptDouble(body, "width", 640), 160, 1280);
            int height = (int)Numbers.Clamp(OptDouble(body, "height", 360), 90, 720);
            bool includeBase64 = OptBool(body, "includeBase64", true);
            string savePath = OptString(body, "savePath", OptString(body, "path", ""));
            string cameraMode = OptString(body, "cameraMode", "main");
            bool captureUi = OptBool(body, "captureUi", false);
            bool hasSeek = HasKey(body, "time");
            double seekTime = OptDouble(body, "time", 0);
            int settleFrames = hasSeek ? (int)Numbers.Clamp(OptDouble(body, "settleFrames", 2), 0, 10) : 0;

            if (hasSeek)
            {
                if (!host.Clock.Available) throw new InvalidOperationException("Timeline 尚未就绪");
                host.Clock.Pause();
                host.DirectorSafeSeek(seekTime, false, false);
            }

            req.Deferred = true;
            host.EnqueueDirectorEndOfFrame(() =>
            {
                try
                {
                    string filePath;
                    byte[] png = host.DirectorCaptureScreenshot(width, height, savePath, out filePath, cameraMode, captureUi);
                    var res = new JsonData();
                    res["ok"] = true;
                    res["path"] = filePath;
                    res["time"] = Round3(host.Clock.Time);
                    res["width"] = width;
                    res["height"] = height;
                    res["bytes"] = png.Length;
                    if (includeBase64) res["base64Png"] = Convert.ToBase64String(png);
                    req.Complete(200, res.ToJson());
                }
                catch (Exception ex)
                {
                    req.Complete(500, StudioDirectorBridge.ErrorJson(ex.GetBaseException().Message));
                }
            }, settleFrames);
            return "";
        }

        private string HandleConfig(JsonData body)
        {
            if (HasKey(body, "sendOutput"))
                host.DirectorSendOutput = OptBool(body, "sendOutput", host.DirectorSendOutput);
            if (HasKey(body, "feelPreset"))
                host.SetFeelPreset(StudioFeelEngine.ParsePreset(OptString(body, "feelPreset", "balanced")));
            bool? showWin = HasKey(body, "showWindow") ? (bool?)OptBool(body, "showWindow", true) : null;
            int? tab = HasKey(body, "workbenchTab") ? (int?)(int)OptDouble(body, "workbenchTab", 2) : null;
            if (showWin.HasValue || tab.HasValue)
                host.DirectorSetUiState(showWin, tab);
            return BuildStateJson();
        }

        private string HandleConfigureChapters(JsonData body)
        {
            if (body == null || !HasKey(body, "chapters") || !body["chapters"].IsArray)
                throw new ArgumentException("chapters array is required");
            var arr = body["chapters"];
            int updated = 0;
            for (int i = 0; i < arr.Count; i++)
            {
                var row = arr[i];
                if (row == null || !row.IsObject) continue;
                int idx = HasKey(row, "index") ? (int)OptDouble(row, "index", -1) : i;
                int? src = HasKey(row, "sourceKey") ? (int?)(int)OptDouble(row, "sourceKey", -1) : null;
                int? dst = HasKey(row, "targetKey") ? (int?)(int)OptDouble(row, "targetKey", -1) : null;
                int? op = HasKey(row, "opening") ? (int?)(int)OptDouble(row, "opening", 0) : null;
                string phase = OptString(row, "phase", "");
                string cMode = OptString(row, "climaxMode", "");
                double? hz = HasKey(row, "climaxHz") ? (double?)OptDouble(row, "climaxHz", 7.0) : null;
                double? cMin = HasKey(row, "climaxMin") ? (double?)OptDouble(row, "climaxMin", 0.0) : null;
                double? cMax = HasKey(row, "climaxMax") ? (double?)OptDouble(row, "climaxMax", 240.0) : null;
                string cname = OptString(row, "customName", "");
                int? gPat = HasKey(row, "grindPattern") ? (int?)(int)OptDouble(row, "grindPattern", 0) : null;
                double? gInt = HasKey(row, "grindIntensity") ? (double?)OptDouble(row, "grindIntensity", 0.65) : null;
                double? gHz = HasKey(row, "grindHz") ? (double?)OptDouble(row, "grindHz", 1.2) : null;
                int? gMover = HasKey(row, "grindMover") ? (int?)(int)OptDouble(row, "grindMover", 0) : null;
                bool? gSkel = HasKey(row, "grindLinkSkeleton") ? (bool?)OptBool(row, "grindLinkSkeleton", true) : null;
                bool? gSoft = HasKey(row, "grindSoftTissue") ? (bool?)OptBool(row, "grindSoftTissue", true) : null;
                bool? gDepth = HasKey(row, "grindDepthWeighted") ? (bool?)OptBool(row, "grindDepthWeighted", true) : null;
                if (idx >= 0 && idx < host.Book.Chapters.Count)
                {
                    host.DirectorConfigureChapter(idx, src, dst, op, phase, cMode, hz, cMin, cMax, cname, gPat, gInt, gHz, gMover, gSkel, gSoft, gDepth);
                    updated++;
                }
            }
            return BuildStateJson();
        }

        private string HandleLoadScene(JsonData body)
        {
            string path = OptString(body, "path", "");
            host.DirectorLoadScene(path);
            return BuildStateJson();
        }

        private string HandleFeel(JsonData body)
        {
            string presetStr = OptString(body, "preset", OptString(body, "feelPreset", "balanced"));
            host.SetFeelPreset(StudioFeelEngine.ParsePreset(presetStr));
            return BuildStateJson();
        }

        private string HandleStepBake(JsonData body)
        {
            string action = OptString(body, "action", "start").ToLowerInvariant();
            if (action == "cancel" || action == "stop")
            {
                host.CancelStepBake();
            }
            else if (action == "start")
            {
                double stepSec = OptDouble(body, "stepSeconds", 0.05);
                bool alsoBake = OptBool(body, "bakeScripts", false);
                double scriptStep = OptDouble(body, "scriptStepSeconds", 0.02);
                host.StartStepBake(stepSec, alsoBake, scriptStep);
            }
            else if (action != "status")
            {
                throw new ArgumentException("action must be start, status, or cancel");
            }
            return BuildStateJson();
        }

        private string HandleBakeScripts(JsonData body)
        {
            string action = OptString(body, "action", "start").ToLowerInvariant();
            if (action == "cancel" || action == "stop")
            {
                host.CancelStepBake();
                return BuildStateJson();
            }
            if (HasKey(body, "feelPreset"))
                host.SetFeelPreset(StudioFeelEngine.ParsePreset(OptString(body, "feelPreset", "balanced")));
            double stepSec = OptDouble(body, "stepSeconds", 0.02);
            bool prescanFirst = OptBool(body, "prescanFirst", false);
            if (prescanFirst)
                host.StartStepBake(OptDouble(body, "prescanStepSeconds", 0.05), true, stepSec);
            else
                host.StartScriptBake(stepSec, true);
            return BuildStateJson();
        }

        private string BuildDebugShaftsJson()
        {
            var res = new JsonData();
            res["ok"] = true;
            res["time"] = Round3(host.Clock.Time);
            res["binding"] = host.Binding != null ? host.Binding.Description : "";
            if (host.Binding != null)
            {
                var b = host.Binding;
                res["restLengthMm"] = Round1(b.RestLength * 1000.0);
                res["externalItem"] = b.Model != null && b.Model.ExternalItem;
                res["sourceBones"] = ReadActorBonePositions(b.Source, new[] { "cf_t_hips(work)", "cf_j_hips", "cf_j_waist02", "cm_J_dan100_00", "cm_J_dan109_00", "cf_j_spine01", "cf_j_spine03", "cf_j_head" });
                res["targetBones"] = ReadActorBonePositions(b.Target, new[] { "cf_t_hips(work)", "cf_j_hips", "cf_j_waist02", "cf_d_kokan", "cf_J_Vagina_root", "cf_j_spine01", "cf_j_spine03", "cf_j_head" });
                if (host.Sample != null)
                {
                    var s = new JsonData();
                    s["origin"] = Round3(host.Sample.Origin.X) + "," + Round3(host.Sample.Origin.Y) + "," + Round3(host.Sample.Origin.Z);
                    s["end"] = Round3(host.Sample.End.X) + "," + Round3(host.Sample.End.Y) + "," + Round3(host.Sample.End.Z);
                    s["target"] = Round3(host.Sample.Target.X) + "," + Round3(host.Sample.Target.Y) + "," + Round3(host.Sample.Target.Z);
                    res["sample"] = s;
                }
                var sHips = FindNamedBone(b.Source, "cf_j_waist02") ?? FindNamedBone(b.Source, "cf_j_hips");
                var tHips = FindNamedBone(b.Target, "cf_j_waist02") ?? FindNamedBone(b.Target, "cf_j_hips");
                if (sHips != null && tHips != null)
                    res["pelvisDistanceMm"] = Round1(Vector3.Distance(sHips.position, tHips.position) * 1000.0);
                if (b.Root != null && b.Opening != null)
                    res["rootToOpeningMm"] = Round1(Vector3.Distance(b.Root.position, b.Opening.position) * 1000.0);
            }
            if (host.Measured != null)
            {
                var m = new JsonData();
                m["depth"] = host.Measured.Raw != null && host.Measured.Raw.Length > 0 ? Round3(host.Measured.Raw[0]) : 0;
                m["engagement"] = Round3(host.Measured.Engagement);
                var snap = host.DirectorOutputSnapshot;
                m["outL0"] = snap != null && snap.Length > 0 ? Round3(snap[0]) : 0;
                res["measured"] = m;
            }
            var itemsArr = new JsonData();
            itemsArr.SetJsonType(JsonType.Array);
            var sceneModels = StudioShaftModel.DiscoverAllSceneItems();
            for (int i = 0; i < sceneModels.Count; i++)
            {
                var m = sceneModels[i];
                var row = new JsonData();
                row["itemKey"] = m.ItemKey;
                row["name"] = m.DisplayName;
                row["renderer"] = m.Renderer != null ? m.Renderer.name : "";
                row["visible"] = m.Visible;
                row["chainLen"] = m.Chain != null ? m.Chain.Length : 0;
                row["baseLengthMm"] = Round1(m.BaseLength * 1000.0);
                if (m.Chain != null && m.Chain.Length >= 2)
                {
                    row["rootBone"] = m.Chain[0].name;
                    row["tipBone"] = m.Chain[m.Chain.Length - 1].name;
                    var rp = m.Chain[0].position;
                    var tp = m.Chain[m.Chain.Length - 1].position;
                    row["rootPos"] = Round3(rp.x) + "," + Round3(rp.y) + "," + Round3(rp.z);
                    row["tipPos"] = Round3(tp.x) + "," + Round3(tp.y) + "," + Round3(tp.z);
                }
                itemsArr.Add(row);
            }
            res["sceneShaftItems"] = itemsArr;
            if (Camera.main != null)
            {
                var cp = Camera.main.transform.position;
                res["cameraPos"] = Round3(cp.x) + "," + Round3(cp.y) + "," + Round3(cp.z);
            }
            AppendRigDiagnostics(res);
            return res.ToJson();
        }

        private static void AppendRigDiagnostics(JsonData res)
        {
            try
            {
                var ncType = RuntimeAccess.Find("NodesConstraints.NodesConstraints");
                var ncSelf = RuntimeAccess.Read(ncType, "_self");
                var ncList = RuntimeAccess.Read(ncSelf, "_constraints") as IEnumerable;
                var ncArr = new JsonData();
                ncArr.SetJsonType(JsonType.Array);
                int ncTotal = 0;
                if (ncList != null)
                {
                    foreach (var c in ncList)
                    {
                        ncTotal++;
                        if (ncTotal > 12) continue;
                        var pt = RuntimeAccess.Read(c, "parentTransform") as Transform;
                        var ct = RuntimeAccess.Read(c, "childTransform") as Transform;
                        var row = new JsonData();
                        row["en"] = RuntimeAccess.Flag(c, "enabled");
                        row["pos"] = RuntimeAccess.Flag(c, "position");
                        row["parent"] = pt != null ? pt.name + "@" + Round3(pt.position.x) + "," + Round3(pt.position.y) + "," + Round3(pt.position.z) : "null";
                        row["child"] = ct != null ? ct.name + "@" + Round3(ct.position.x) + "," + Round3(ct.position.y) + "," + Round3(ct.position.z) : "null";
                        ncArr.Add(row);
                    }
                }
                res["ncCount"] = ncTotal;
                res["ncSample"] = ncArr;
            }
            catch (Exception ex) { res["ncError"] = ex.GetBaseException().Message; }

            try
            {
                var studio = global::Studio.Studio.Instance;
                var rigArr = new JsonData();
                rigArr.SetJsonType(JsonType.Array);
                if (studio != null && studio.dicObjectCtrl != null)
                {
                    foreach (var kv in studio.dicObjectCtrl)
                    {
                        var oci = kv.Value;
                        if (oci == null || oci.treeNodeObject == null) continue;
                        string nm = oci.treeNodeObject.textName ?? "";
                        if (nm.IndexOf("PGL", StringComparison.OrdinalIgnoreCase) >= 0
                            || nm.IndexOf("VGNA", StringComparison.OrdinalIgnoreCase) >= 0
                            || nm.IndexOf("PENIS", StringComparison.OrdinalIgnoreCase) >= 0
                            || nm.IndexOf("TOR ", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var row = new JsonData();
                            row["key"] = kv.Key;
                            row["name"] = nm;
                            var go = oci.guideObject;
                            if (go != null && go.transformTarget != null)
                            {
                                var p = go.transformTarget.position;
                                row["pos"] = Round3(p.x) + "," + Round3(p.y) + "," + Round3(p.z);
                                var lp = go.transformTarget.localPosition;
                                row["lpos"] = Round3(lp.x) + "," + Round3(lp.y) + "," + Round3(lp.z);
                            }
                            rigArr.Add(row);
                        }
                    }
                }
                res["rigFolders"] = rigArr;
            }
            catch (Exception ex) { res["rigError"] = ex.GetBaseException().Message; }
        }

        private static Transform FindNamedBone(ChaControl actor, string name)
        {
            if (actor == null) return null;
            foreach (var t in actor.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == name) return t;
            return null;
        }

        private static JsonData ReadActorBonePositions(ChaControl actor, string[] names)
        {
            var obj = new JsonData();
            obj.SetJsonType(JsonType.Object);
            if (actor == null) return obj;
            var map = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var t in actor.GetComponentsInChildren<Transform>(true))
                if (t != null && !map.ContainsKey(t.name)) map[t.name] = t;
            for (int i = 0; i < names.Length; i++)
            {
                Transform node;
                if (map.TryGetValue(names[i], out node) && node != null)
                {
                    var p = node.position;
                    obj[names[i]] = Round3(p.x) + "," + Round3(p.y) + "," + Round3(p.z);
                }
            }
            return obj;
        }

        private static double Round1(double v) { return Numbers.Finite(v) ? Math.Round(v, 1) : 0; }

        internal void PersistDirectorAndStepBakeReports(StepBakeAnalysisResult analysis)
        {
            if (analysis == null) return;
            var doc = new JsonData();
            doc["schema"] = "koisync.studio.director.v1";
            doc["sceneName"] = host.DirectorBook.SceneName;
            doc["scenePath"] = host.DirectorScenePath;
            doc["duration"] = Round3(analysis.Duration);
            doc["source"] = "step_bake";
            var segArr = new JsonData();
            segArr.SetJsonType(JsonType.Array);
            for (int i = 0; i < host.DirectorBook.Segments.Count; i++)
                segArr.Add(SegmentToJson(host.DirectorBook.Segments[i]));
            doc["segments"] = segArr;
            host.SaveDirectorPlanJson(doc.ToJson());

            string reportPath = host.StepBakeReportPath;
            if (string.IsNullOrEmpty(reportPath)) return;
            var rep = new JsonData();
            rep["schema"] = "koisync.studio.stepbake.v1";
            rep["sceneName"] = host.DirectorBook.SceneName;
            rep["scenePath"] = host.DirectorScenePath;
            rep["duration"] = Round3(analysis.Duration);
            rep["frameCount"] = analysis.FrameCount;
            rep["reviewCount"] = analysis.ReviewCount;
            rep["climaxCount"] = analysis.ClimaxCount;

            var chArr = new JsonData();
            chArr.SetJsonType(JsonType.Array);
            for (int i = 0; i < analysis.Chapters.Count; i++)
            {
                var c = analysis.Chapters[i];
                var row = new JsonData();
                row["id"] = "seg_" + (i + 1).ToString("D2", CultureInfo.InvariantCulture);
                row["start"] = Round3(c.Start);
                row["end"] = Round3(c.End);
                row["sourceKey"] = c.SourceKey;
                row["targetKey"] = c.TargetKey;
                row["sourceName"] = c.SourceName ?? "";
                row["targetName"] = c.TargetName ?? "";
                row["opening"] = c.Opening;
                row["openingName"] = StudioStepAnalyzer.OpeningLabelEn(c.Opening);
                row["confidence"] = Round2(c.Confidence);
                row["needsReview"] = c.NeedsReview;
                row["reviewNote"] = c.ReviewNote ?? "";
                if (!double.IsNaN(c.DepthP05)) row["depthP05"] = Round3(c.DepthP05);
                if (!double.IsNaN(c.DepthP95)) row["depthP95"] = Round3(c.DepthP95);
                row["peakHz"] = Round2(c.PeakHz);
                row["amplitudePercent"] = Round2(c.AmplitudePercent);
                row["loopStart"] = Round3(c.LoopStart);
                row["loopEnd"] = Round3(c.LoopEnd);
                row["loopable"] = c.Loopable;
                row["climaxDetected"] = c.ClimaxDetected;
                row["climaxScore"] = Round2(c.ClimaxScore);
                row["climaxEvidence"] = c.ClimaxEvidence ?? "";
                row["role"] = c.Role ?? "medium";
                row["label"] = c.Label ?? "";

                var candArr = new JsonData();
                candArr.SetJsonType(JsonType.Array);
                for (int k = 0; k < c.TopCandidates.Count; k++)
                {
                    var cand = c.TopCandidates[k];
                    var cr = new JsonData();
                    cr["sourceName"] = cand.SourceName ?? "";
                    cr["targetName"] = cand.TargetName ?? "";
                    cr["opening"] = cand.Opening;
                    cr["openingName"] = StudioStepAnalyzer.OpeningLabelEn(cand.Opening);
                    cr["score"] = Round2(cand.Score);
                    cr["meanDistanceMm"] = Round2(cand.MeanDistanceMm);
                    cr["meanInsertion"] = Round2(cand.MeanInsertion);
                    cr["explicitFrames"] = cand.ExplicitFrames;
                    cr["contactFrames"] = cand.ContactFrames;
                    candArr.Add(cr);
                }
                row["topCandidates"] = candArr;
                chArr.Add(row);
            }
            rep["chapters"] = chArr;
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath + ".tmp", rep.ToJson(), new System.Text.UTF8Encoding(false));
            if (File.Exists(reportPath)) File.Replace(reportPath + ".tmp", reportPath, null);
            else File.Move(reportPath + ".tmp", reportPath);
        }

        private static List<StudioDirectorSegment> ParseSegmentsList(JsonData root)
        {
            var list = new List<StudioDirectorSegment>();
            if (root == null || !HasKey(root, "segments") || !root["segments"].IsArray) return list;
            var arr = root["segments"];
            for (int i = 0; i < arr.Count; i++)
            {
                var row = arr[i];
                if (row == null || !row.IsObject) continue;
                list.Add(new StudioDirectorSegment
                {
                    Id = OptString(row, "id", ""),
                    Label = OptString(row, "label", ""),
                    Role = OptString(row, "role", "medium"),
                    Notes = OptString(row, "notes", ""),
                    Start = OptDouble(row, "start", 0),
                    End = OptDouble(row, "end", 0),
                    LoopStart = OptDouble(row, "loopStart", OptDouble(row, "start", 0)),
                    LoopEnd = OptDouble(row, "loopEnd", OptDouble(row, "end", 0)),
                    Loopable = OptBool(row, "loopable", true),
                    FrequencyHz = OptDouble(row, "frequencyHz", 0),
                    AmplitudePercent = OptDouble(row, "amplitudePercent", 0),
                    SeamErrorCm = OptDouble(row, "seamErrorCm", 0),
                    DefaultSpeed = OptDouble(row, "defaultSpeed", 1.0)
                });
            }
            return list;
        }

        private static JsonData SegmentToJson(StudioDirectorSegment s)
        {
            var row = new JsonData();
            row["id"] = s.Id ?? "";
            row["label"] = s.Label ?? "";
            row["role"] = s.Role ?? "medium";
            row["notes"] = s.Notes ?? "";
            row["start"] = Round3(s.Start);
            row["end"] = Round3(s.End);
            row["duration"] = Round3(s.End - s.Start);
            row["loopStart"] = Round3(s.EffectiveLoopStart);
            row["loopEnd"] = Round3(s.EffectiveLoopEnd);
            row["loopDuration"] = Round3(s.EffectiveLoopEnd - s.EffectiveLoopStart);
            row["loopable"] = s.Loopable;
            row["frequencyHz"] = Round3(s.FrequencyHz);
            row["amplitudePercent"] = Round2(s.AmplitudePercent);
            row["seamErrorCm"] = Round3(s.SeamErrorCm);
            row["defaultSpeed"] = Round3(s.DefaultSpeed);
            return row;
        }

        private static JsonData ParseBody(string body)
        {
            if (string.IsNullOrEmpty(body) || body.Trim().Length == 0) return new JsonData();
            try
            {
                return JsonMapper.ToObject(body);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Invalid JSON body: " + ex.Message);
            }
        }

        private static bool HasKey(JsonData d, string key)
        {
            return d != null && d.IsObject && ((IDictionary)d).Contains(key) && d[key] != null;
        }

        private static string OptString(JsonData d, string key, string fallback)
        {
            if (!HasKey(d, key)) return fallback;
            try { return Convert.ToString(d[key], CultureInfo.InvariantCulture) ?? fallback; }
            catch { return fallback; }
        }

        private static bool OptBool(JsonData d, string key, bool fallback)
        {
            if (!HasKey(d, key)) return fallback;
            var v = d[key];
            if (v.IsBoolean) return (bool)v;
            string s = Convert.ToString(v, CultureInfo.InvariantCulture).Trim().ToLowerInvariant();
            if (s == "true" || s == "1" || s == "yes") return true;
            if (s == "false" || s == "0" || s == "no") return false;
            return fallback;
        }

        private static double OptDouble(JsonData d, string key, double fallback)
        {
            if (!HasKey(d, key)) return fallback;
            var v = d[key];
            if (v.IsDouble) return (double)v;
            if (v.IsInt) return (int)v;
            if (v.IsLong) return (long)v;
            double parsed;
            if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && Numbers.Finite(parsed))
                return parsed;
            return fallback;
        }

        private static double Round2(double v) { return Numbers.Finite(v) ? Math.Round(v, 2) : 0; }
        private static double Round3(double v) { return Numbers.Finite(v) ? Math.Round(v, 3) : 0; }
    }
}
