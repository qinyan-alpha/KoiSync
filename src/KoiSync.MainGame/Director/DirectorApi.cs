using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KoiSync.Core;
using LitJson;
using UnityEngine;

namespace KoiSync.MainGame
{
    // Separate class so LitJSON is never referenced in Plugin's class metadata
    // before EmbeddedDependencies.Initialize() loads embedded LitJSON.dll in Awake().
    internal sealed class DirectorApi
    {
        private readonly Plugin host;
        private static readonly string[] AxisNames = { "L0", "L1", "L2", "R0", "R1", "R2" };

        internal DirectorApi(Plugin plugin)
        {
            host = plugin;
        }

        internal string Handle(DirectorHttpRequest req)
        {
            string path = (req.Path ?? "/").TrimEnd('/');
            if (path.Length == 0) path = "/";

            if (req.Method == "GET" && (path == "/" || path == "/health" || path == "/state"))
                return BuildStateJson();

            if (req.Method == "GET" && (path == "/poses" || path == "/poses/catalog"))
                return BuildPosesJson(req.Query, path == "/poses/catalog");

            if (req.Method == "GET" && path == "/events")
                return BuildEventsJson(req.Query);

            if (req.Method == "GET" && path == "/config")
                return BuildConfigJson();

            if (req.Method == "GET" && path == "/game/characters")
                return BuildCharactersJson(req.Query);

            JsonData body = ParseBody(req.Body);

            // Hard AI Takeover Disconnect Gate: when user disconnects AI in HUD, reject ALL mutating AI commands immediately!
            if (host.AiTakeoverBlocked && req.Method != "GET" && path != "/director/release" && path != "/game/screenshot")
            {
                bool allowCleanup =
                    (path == "/director/lip_rms" && OptBool(body, "clear", false)) ||
                    (path == "/director/audio" && !OptBool(body, "muteNativeSe", false) && !OptBool(body, "muteNativeVoice", false));
                if (!allowCleanup)
                {
                    req.StatusCode = 423;
                    return DirectorBridge.ErrorJson("AI 接管已在游戏内断开，请重新勾选「开启 AI 语音互动」后再试");
                }
            }

            if (req.Method == "POST" && path == "/config")
                return HandleSetConfig(body);

            if (req.Method == "POST" && path == "/feel")
                return HandleSetFeel(body);

            if (req.Method == "POST" && path == "/game/stage")
                return HandleSetStage(body);

            if (req.Method == "POST" && path == "/game/speed")
                return HandleSetSpeed(body);

            if (req.Method == "POST" && path == "/game/pose")
                return HandleSwitchPose(body);

            if (req.Method == "POST" && path == "/game/action")
                return HandleGameAction(body);

            if (req.Method == "POST" && path == "/game/clothes")
                return HandleClothes(body);

            if (req.Method == "POST" && path == "/game/outfit")
                return HandleOutfit(body);

            if (req.Method == "POST" && path == "/game/characters")
                return HandleCharacters(body);

            if (req.Method == "POST" && path == "/game/screenshot")
                return HandleScreenshot(req, body);

            if (req.Method == "POST" && path == "/director/override")
                return HandleDirectorOverride(body);

            if (req.Method == "POST" && path == "/director/sequence")
                return HandleDirectorSequence(body);

            if (req.Method == "POST" && path == "/director/edge_guard")
                return HandleDirectorEdgeGuard(body);

            if (req.Method == "POST" && path == "/director/timeline")
                return HandleDirectorTimeline(body);

            if ((req.Method == "POST" && path == "/director/timeline/tune") || (req.Method == "PATCH" && path == "/director/timeline"))
                return HandleDirectorTimelineTune(body);

            if (req.Method == "POST" && path == "/director/expression")
                return HandleDirectorExpression(body);

            if (req.Method == "POST" && path == "/director/gauges")
                return HandleDirectorGauges(body);

            if (req.Method == "POST" && path == "/director/audio")
                return HandleDirectorAudio(body);

            if (req.Method == "POST" && path == "/director/lip_rms")
                return HandleDirectorLipRms(body);

            if (req.Method == "POST" && path == "/director/autonomy")
                return HandleDirectorAutonomy(body);

            if (req.Method == "POST" && path == "/director/math_lab")
                return HandleDirectorMathLab(body);

            if (req.Method == "POST" && path == "/director/release")
                return HandleDirectorRelease(body);

            req.StatusCode = 404;
            return DirectorBridge.ErrorJson("Unknown endpoint: " + req.Method + " " + path);
        }

        private string BuildStateJson()
        {
            string dMode, dStatus, dLast;
            int dIdx, dTotal, dEdgeCnt;
            double dTtl, dBlend;
            bool dEdgeOn, dEdgeBraking;
            host.Director.SnapshotState(out dMode, out dStatus, out dLast, out dIdx, out dTotal, out dTtl, out dBlend, out dEdgeOn, out dEdgeBraking, out dEdgeCnt);

            var data = new JsonData();
            data["ok"] = true;
            data["version"] = MainGameVersion.Number;
            data["scene"] = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            data["inHScene"] = host.Scene != null && !host.Scene.isEnd && host.Flags != null;
            data["aiTakeoverBlocked"] = host.AiTakeoverBlocked;
            data["status"] = host.Status ?? "";
            data["syncRequested"] = host.SyncRequested;
            data["deviceConnected"] = host.Devices != null && host.Devices.Connected;
            data["feelPreset"] = host.ActiveFeelPreset.ToString();
            data["nonShaftMode"] = host.ActiveNonShaftFeelMode.ToString();
            data["outerCeilingL0"] = Round1(host.ActiveOuterCeilingL0 * 100.0);
            data["feelStatus"] = host.Feel.Status ?? "";
            data["heatmapBand"] = host.Feel.HeatmapBand ?? "";
            data["estFreqHz"] = Round3(host.Feel.EstimatedFreqHz);
            data["estSpeedUnitsPerSec"] = Round1(host.Feel.EstimatedSpeedUnitsPerSec);

            if (host.Flags != null)
            {
                data["hMode"] = host.Flags.mode.ToString();
                data["isAnalPlay"] = host.Flags.isAnalPlay;
                data["gaugeFemale"] = Round1(host.Flags.gaugeFemale);
                data["gaugeMale"] = Round1(host.Flags.gaugeMale);
                data["lockGaugeFemale"] = host.Flags.lockGugeFemale;
                data["lockGaugeMale"] = host.Flags.lockGugeMale;
                data["speed"] = Round3(host.Flags.speed);
                data["speedCalc"] = Round3(host.Flags.speedCalc);
                data["speedHoushi"] = host.Flags.speedHoushi;
                data["orgasmFemaleCount"] = host.Flags.count != null ? host.Flags.count.sonyuOrg + host.Flags.count.sonyuAnalOrg + host.Flags.count.aibuOrg : 0;
                bool isKiss = false;
                string touchArea = "none";
                try
                {
                    var hand = host.Scene != null ? RuntimeAccess.Read(host.Scene, "hand") as HandCtrl : null;
                    if (hand != null)
                    {
                        isKiss = hand.IsKissAction();
                        touchArea = hand.SelectKindTouch.ToString();
                    }
                }
                catch { }
                data["isKiss"] = isKiss;
                data["touchArea"] = touchArea;
                data["isInserted"] = host.IsDirectPenetrativeInserted();
                data["isDirectPenetration"] = host.IsDirectPenetrativeInserted();
                data["isServiceActive"] = host.IsServiceMotionActive();
                data["climaxCompletedCount"] = host.ClimaxCompletedCount;
                data["femaleName"] = host.GetPrimaryFemaleName();
                data["femaleOutfitSlot"] = host.GetPrimaryFemaleOutfitSlot();
                data["kPlugLoaded"] = host.IsKPlugLoaded;
                data["haremCount"] = host.GetKPlugHaremCount();
                data["latestEventId"] = (int)host.EventBus.LatestEventId;
                var curPoseMeta = host.DirectorClassifyCurrentPose();
                if (curPoseMeta != null)
                {
                    data["poseFamily"] = curPoseMeta.Family;
                    data["poseVariantLabel"] = curPoseMeta.VariantLabel;
                    data["poseFacing"] = curPoseMeta.Facing;
                    data["poseIsDirectPenetration"] = curPoseMeta.IsDirectPenetration;
                    data["poseIsFemaleInitiative"] = curPoseMeta.IsFemaleInitiative;
                }
                var recentEvs = host.EventBus.Recent(6);
                var evArr = new JsonData();
                evArr.SetJsonType(JsonType.Array);
                for (int ei = 0; ei < recentEvs.Count; ei++) evArr.Add(SerializeEvent(recentEvs[ei]));
                data["recentEvents"] = evArr;
            }
            if (host.Profile != null)
            {
                data["poseKey"] = host.Profile.Key ?? "";
                data["poseName"] = host.Profile.Name ?? "";
                data["stage"] = !string.IsNullOrEmpty(host.Stage) ? host.Stage : host.GetLiveFemaleAnimStateName();
                data["phaseTime"] = Round3(host.DirectorPhaseTime);
                data["phaseDuration"] = Round3(host.DirectorPhaseDuration);
                data["effector"] = host.Profile.Effector.ToString();
                data["contact"] = (host.Binding != null ? host.Binding.Contact : host.Profile.Contact).ToString();
                data["mappingMode"] = host.Profile.Mode.ToString();
            }
            if (host.Measured != null)
            {
                data["engagement"] = Round3(host.Measured.Engagement);
                data["contactDistanceMm"] = Round1(host.Measured.ContactDistance * 1000.0);
                data["depthMm"] = Round1(host.Measured.TargetDepth * 1000.0);
                data["modelLengthMm"] = Round1(host.Measured.Length * 1000.0);
                data["materialInsertion"] = Round3(host.Measured.MaterialInsertion);
            }
            var axesData = new JsonData();
            for (int i = 0; i < 6; i++) axesData[AxisNames[i]] = Round1(host.Drive.Values[i] * 100.0);
            data["axes"] = axesData;

            var availStages = new JsonData();
            availStages.SetJsonType(JsonType.Array);
            var fem = host.Actor(ActorSlot.Female0);
            if (fem != null && fem.animBody != null)
            {
                foreach (var st in new[] { "Idle", "WLoop", "MLoop", "SLoop", "OLoop", "A_Idle", "A_WLoop", "A_SLoop", "A_OLoop", "IN_Loop", "SS_IN_Loop", "Orgasm", "Loop" })
                {
                    if (fem.animBody.HasState(0, Animator.StringToHash(st))) availStages.Add(st);
                }
            }
            data["availableStages"] = availStages;

            var dir = new JsonData();
            dir["active"] = host.Director.Active;
            dir["mode"] = dMode;
            dir["status"] = dStatus;
            dir["lastEvent"] = dLast;
            dir["stepIndex"] = dIdx;
            dir["stepTotal"] = dTotal;
            dir["remainingSec"] = Round2(dTtl);
            dir["blendWeight"] = Round3(dBlend);
            dir["speedLatched"] = host.DirectorSpeedLatched;
            dir["speedLatchValue"] = Round2(host.DirectorSpeedValue);
            dir["edgeGuardEnabled"] = dEdgeOn;
            dir["edgeBraking"] = dEdgeBraking;
            dir["edgeTriggerCount"] = dEdgeCnt;
            var egSnap = host.Director.SnapshotEdgeGuard();
            dir["edgeFemaleThreshold"] = Round1(egSnap.FemaleThreshold);
            dir["edgeMaleThreshold"] = Round1(egSnap.MaleThreshold);
            dir["gaugeManaged"] = host.DirectorGaugeManaged;
            dir["targetFemaleGauge"] = Round1(host.DirectorTargetFemaleGauge);
            dir["targetMaleGauge"] = Round1(host.DirectorTargetMaleGauge);
            data["director"] = dir;

            var pm = new JsonData();
            pm["ready"] = host.PhaseMap.Ready;
            pm["coverage"] = Round2(host.PhaseMap.Coverage);
            pm["minDepthMm"] = Round1(host.PhaseMap.MinDepthMeters * 1000.0);
            pm["maxDepthMm"] = Round1(host.PhaseMap.MaxDepthMeters * 1000.0);
            pm["shallowPhase"] = Round3(host.PhaseMap.ShallowPhase);
            pm["deepPhase"] = Round3(host.PhaseMap.DeepPhase);
            pm["moverMeasured"] = host.PhaseMap.MoverMeasured;
            pm["femaleMoverShare"] = Round3(host.PhaseMap.FemaleMoverShare);
            pm["femalePelvisSpanMm"] = Round1(host.PhaseMap.FemalePelvisSpanMeters * 1000.0);
            pm["malePelvisSpanMm"] = Round1(host.PhaseMap.MalePelvisSpanMeters * 1000.0);
            data["phaseMap"] = pm;

            bool tlActive, tlLoop; string tlName, tlStep; int tlIdx, tlTotal; double tlRem, tlDepth, tlL0, tlBodyW;
            GrindPattern tlPat; ActiveMoverMode tlMover;
            host.Timeline.Snapshot(out tlActive, out tlName, out tlStep, out tlIdx, out tlTotal, out tlRem, out tlDepth, out tlL0, out tlLoop, out tlPat, out tlMover, out tlBodyW);
            var tl = new JsonData();
            tl["active"] = tlActive;
            tl["name"] = tlName ?? "";
            tl["stepName"] = tlStep ?? "";
            tl["stepIndex"] = tlIdx;
            tl["stepTotal"] = tlTotal;
            tl["looping"] = tlLoop;
            tl["remainingSec"] = Round2(tlRem);
            tl["depthNorm"] = Round3(tlDepth);
            tl["deviceL0"] = Round1(tlL0 * 100.0);
            tl["grindPattern"] = tlPat.ToString();
            tl["moverMode"] = tlMover.ToString();
            tl["bodyGrindWeight"] = Round3(tlBodyW);
            double tlFreq, tlDMin, tlDMax, tlGrind, tlTwist;
            host.Timeline.SnapshotActiveParams(out tlFreq, out tlDMin, out tlDMax, out tlGrind, out tlTwist);
            tl["freqHz"] = Round2(tlFreq);
            tl["depthMin"] = Round2(tlDMin);
            tl["depthMax"] = Round2(tlDMax);
            tl["grindAmp"] = Round2(tlGrind);
            tl["twistAmp"] = Round2(tlTwist);
            tl["preset"] = host.Timeline.Active ? host.Timeline.ActivePreset.ToString() : "None";
            tl["climaxPhase"] = host.Timeline.CurrentClimaxPhase;
            tl["ejaculationPulseCount"] = host.Timeline.EjaculationPulseCount;
            tl["organicActive"] = host.Timeline.OrganicActive;
            tl["organicSubMode"] = host.Timeline.OrganicSubMode ?? "";
            tl["spasmAmp"] = Round3(host.ActiveTimelineFrame.Active ? host.ActiveTimelineFrame.SpasmAmp : 0.0);
            tl["status"] = host.Timeline.SummaryStatus();
            tl["lastEvent"] = host.Timeline.LastEvent ?? "";
            data["timeline"] = tl;

            var aud = new JsonData();
            aud["muteNativeSe"] = host.MuteNativeSeActive;
            aud["muteNativeVoice"] = host.MuteNativeVoiceActive;
            aud["lipRms"] = Round3(host.Expressions != null ? host.Expressions.ExternalLipRms : 0.0);
            aud["lipActive"] = host.Expressions != null && host.Expressions.ExternalLipActive;
            data["audio"] = aud;

            var bg = new JsonData();
            if (host.BodyGrind != null)
            {
                bg["active"] = host.BodyGrind.Active;
                bg["envelope"] = Round3(host.BodyGrind.Envelope);
                bg["resolvedMover"] = host.BodyGrind.ResolvedMover ?? "None";
                bg["femaleShare"] = Round2(host.BodyGrind.FemaleShareApplied);
                bg["maleShare"] = Round2(host.BodyGrind.MaleShareApplied);
                bg["softTissueBones"] = host.BodyGrind.SoftTissueBonesSynced;
                bg["hipDispMm"] = Round1(host.BodyGrind.PrimaryHipDispMm);
                bg["hipYawDeg"] = Round1(host.BodyGrind.PrimaryHipYawDeg);
                bg["footDriftMm"] = Round1(host.BodyGrind.PrimaryFootDriftMm);
                bg["entryDriftMm"] = Round1(host.BodyGrind.PrimaryEntryDriftMm);
                bg["hipWorldX"] = Round3(host.BodyGrind.PrimaryHipWorld.x);
                bg["hipWorldY"] = Round3(host.BodyGrind.PrimaryHipWorld.y);
                bg["hipWorldZ"] = Round3(host.BodyGrind.PrimaryHipWorld.z);
            }
            else
            {
                bg["active"] = false;
                bg["resolvedMover"] = "None";
            }
            data["bodyGrind"] = bg;

            var exp = new JsonData();
            if (host.Expressions != null)
            {
                var fSpec = host.Expressions.GetSpec(ActorSlot.Female0);
                var mSpec = host.Expressions.GetSpec(ActorSlot.Male);
                exp["anyActive"] = host.Expressions.AnyActive;
                exp["femaleActive"] = fSpec.Active;
                exp["femalePreset"] = fSpec.PresetName ?? "auto";
                exp["maleActive"] = mSpec.Active;
                exp["malePreset"] = mSpec.PresetName ?? "auto";
                var fActor = host.Actor(ActorSlot.Female0);
                if (fActor != null)
                {
                    exp["femaleEyebrowPtn"] = fActor.GetEyebrowPtn();
                    exp["femaleEyesPtn"] = fActor.GetEyesPtn();
                    exp["femaleMouthPtn"] = fActor.GetMouthPtn();
                    exp["femaleEyeOpen"] = Round2(fActor.GetEyesOpenMax());
                    exp["femaleMouthOpen"] = Round2(fActor.GetMouthOpenMax());
                    exp["femaleBlush"] = Round2(fActor.fileStatus != null ? fActor.fileStatus.hohoAkaRate : 0f);
                    exp["femaleTears"] = (int)fActor.tearsLv;
                }
                var mActor = host.Actor(ActorSlot.Male);
                if (mActor != null)
                {
                    exp["maleEyebrowPtn"] = mActor.GetEyebrowPtn();
                    exp["maleEyesPtn"] = mActor.GetEyesPtn();
                    exp["maleMouthPtn"] = mActor.GetMouthPtn();
                    exp["maleEyeOpen"] = Round2(mActor.GetEyesOpenMax());
                    exp["maleMouthOpen"] = Round2(mActor.GetMouthOpenMax());
                    exp["maleBlush"] = Round2(mActor.fileStatus != null ? mActor.fileStatus.hohoAkaRate : 0f);
                    exp["maleTears"] = (int)mActor.tearsLv;
                }
            }
            else
            {
                exp["anyActive"] = false;
            }
            data["expression"] = exp;

            var aut = new JsonData();
            aut["mode"] = host.AutonomyMode ?? "player";
            aut["allowAutoPose"] = host.AllowAutoPose;
            aut["allowAutoClimaxEdge"] = host.AllowAutoClimaxEdge;
            aut["allowAutoClothes"] = host.AllowAutoClothes;
            aut["allowAutoKPlugSwap"] = host.AllowAutoKPlugSwap;
            aut["kPlugGirlThinkPaused"] = host.KPlugGirlThinkPaused;
            data["autonomy"] = aut;

            var ml = new JsonData();
            var mp = host.MathEngine.CurrentParams;
            ml["active"] = host.MathEngine.Active;
            ml["family"] = host.MathEngine.Active ? host.MathEngine.ActiveFamily.ToString() : "None";
            ml["status"] = host.MathEngine.LastStatus ?? "";
            ml["freqHz"] = Round2(mp.FreqHz);
            ml["pivotZ"] = Round2(mp.PivotZ);
            ml["pivotSweepHz"] = Round2(mp.PivotSweepHz);
            ml["activePivotZ"] = Round2(host.MathEngine.LastActivePivotZ);
            ml["helixTwist"] = Round2(mp.HelixTwist);
            ml["orbitAmp"] = Round2(mp.OrbitAmp);
            ml["beatHz"] = Round2(mp.BeatHz);
            ml["flutterAmp"] = Round2(mp.FlutterAmp);
            ml["femaleWeightShare"] = Round2(mp.FemaleWeightShare);
            ml["softTissueWeight"] = Round2(mp.SoftTissueWeight);
            ml["bodyScale"] = Round2(mp.BodyScale);
            ml["workspaceContract"] = Round3(host.MathEngine.LastWorkspaceContract);
            data["mathLab"] = ml;

            return data.ToJson();
        }

        private static JsonData SerializeEvent(NativeHEvent ev)
        {
            var row = new JsonData();
            row["id"] = (int)ev.Id;
            row["t"] = Round3(ev.RealtimeSec);
            row["type"] = ev.Type ?? "";
            row["mode"] = ev.Mode ?? "";
            row["stage"] = ev.Stage ?? "";
            row["poseKey"] = ev.PoseKey ?? "";
            row["poseName"] = ev.PoseName ?? "";
            row["family"] = ev.Family ?? "";
            row["facing"] = ev.Facing ?? "";
            row["isDirectPenetration"] = ev.IsDirectPenetration;
            row["isInserted"] = ev.IsInserted;
            row["detail"] = ev.Detail ?? "";
            return row;
        }

        private string BuildEventsJson(string query)
        {
            string sinceStr = ParseQueryParam(query, "since");
            string limitStr = ParseQueryParam(query, "limit");
            long sinceId = 0;
            int limit = 32;
            long.TryParse(sinceStr, out sinceId);
            if (!int.TryParse(limitStr, out limit) || limit <= 0) limit = 32;

            var list = host.EventBus.QuerySince(sinceId, limit);
            var arr = new JsonData();
            arr.SetJsonType(JsonType.Array);
            for (int i = 0; i < list.Count; i++) arr.Add(SerializeEvent(list[i]));

            var res = new JsonData();
            res["ok"] = true;
            res["latestEventId"] = (int)host.EventBus.LatestEventId;
            res["count"] = list.Count;
            res["events"] = arr;
            return res.ToJson();
        }

        private static JsonData SerializePoseEntry(PoseCatalogEntry e)
        {
            var row = new JsonData();
            row["key"] = e.Key;
            row["name"] = e.Name;
            row["gameName"] = e.GameName;
            row["variantLabel"] = e.VariantLabel;
            row["family"] = e.Family;
            row["facing"] = e.Facing;
            row["facingRank"] = e.FacingRank;
            row["classicRank"] = e.ClassicRank;
            row["mode"] = e.Mode;
            row["id"] = e.Id;
            row["effector"] = e.Effector;
            row["contact"] = e.Contact;
            row["analAllowed"] = e.AnalAllowed;
            row["selectable"] = e.Selectable;
            row["isDirectPenetration"] = e.IsDirectPenetration;
            row["isFemaleInitiative"] = e.IsFemaleInitiative;
            row["spotType"] = e.SpotType;
            row["source"] = e.Source;
            return row;
        }

        private string BuildPosesJson(string query, bool includeGroupedFamilies)
        {
            if (host.Scene == null || host.Flags == null) throw new InvalidOperationException("当前不在本体 H 场景中");
            string filter = ParseQueryParam(query, "q");
            string modeFilter = ParseQueryParam(query, "mode");
            string familyFilter = ParseQueryParam(query, "family");
            bool onlySelectable = ParseQueryParam(query, "all") != "1";

            Dictionary<string, HSceneProc.AnimationListInfo> infoMap;
            var entries = host.DirectorBuildPoseCatalog(onlySelectable, out infoMap);

            var list = new JsonData();
            list.SetJsonType(JsonType.Array);
            var famMap = new Dictionary<string, List<PoseCatalogEntry>>(StringComparer.Ordinal);

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (!string.IsNullOrEmpty(modeFilter) && e.Mode.IndexOf(modeFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!string.IsNullOrEmpty(familyFilter) && e.Family.IndexOf(familyFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!string.IsNullOrEmpty(filter) &&
                    e.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    e.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    e.VariantLabel.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    e.GameName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    e.Family.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                list.Add(SerializePoseEntry(e));
                List<PoseCatalogEntry> bucket;
                if (!famMap.TryGetValue(e.Family, out bucket))
                {
                    bucket = new List<PoseCatalogEntry>();
                    famMap[e.Family] = bucket;
                }
                bucket.Add(e);
            }

            var res = new JsonData();
            res["ok"] = true;
            res["currentPoseKey"] = host.Profile != null ? (host.Profile.Key ?? "") : "";
            res["count"] = list.Count;
            res["poses"] = list;

            var famObj = new JsonData();
            famObj.SetJsonType(JsonType.Object);
            foreach (var kv in famMap)
            {
                var arr = new JsonData();
                arr.SetJsonType(JsonType.Array);
                int cap = includeGroupedFamilies ? kv.Value.Count : Math.Min(12, kv.Value.Count);
                for (int j = 0; j < cap; j++)
                {
                    var item = new JsonData();
                    item["key"] = kv.Value[j].Key;
                    item["variantLabel"] = kv.Value[j].VariantLabel;
                    item["facing"] = kv.Value[j].Facing;
                    item["isDirectPenetration"] = kv.Value[j].IsDirectPenetration;
                    item["isFemaleInitiative"] = kv.Value[j].IsFemaleInitiative;
                    item["source"] = kv.Value[j].Source;
                    arr.Add(item);
                }
                famObj[kv.Key] = arr;
            }
            res["families"] = famObj;
            return res.ToJson();
        }

        private string HandleSetFeel(JsonData body)
        {
            string presetStr = OptString(body, "feelPreset", "");
            string nonShaftStr = OptString(body, "nonShaftMode", "");
            FeelPreset? fp = presetStr.Length > 0 ? (FeelPreset?)ParseFeelPreset(presetStr) : null;
            NonShaftFeelMode? ns = nonShaftStr.Length > 0 ? (NonShaftFeelMode?)ParseNonShaftMode(nonShaftStr) : null;
            host.DirectorApplyFeel(fp, ns);
            return BuildStateJson();
        }

        private string HandleSetStage(JsonData body)
        {
            if (host.Scene == null || host.Flags == null) throw new InvalidOperationException("当前不在本体 H 场景中");
            string stage = OptString(body, "stage", "");
            if (stage.Length == 0) throw new ArgumentException("stage is required (e.g. WLoop, SLoop, OLoop, Idle)");
            if (!host.TryDirectorSetStage(stage)) throw new ArgumentException("当前动作不支持阶段: " + stage);
            if (HasKey(body, "speed"))
                host.DirectorSetSpeedLatch(OptDouble(body, "speed", 0.65), OptDouble(body, "holdSeconds", 20.0));
            return BuildStateJson();
        }

        private string HandleSetSpeed(JsonData body)
        {
            if (host.Scene == null || host.Flags == null) throw new InvalidOperationException("当前不在本体 H 场景中");
            if (OptBool(body, "release", false))
                host.DirectorReleaseSpeedLatch();
            else
                host.DirectorSetSpeedLatch(OptDouble(body, "speed", 0.5), OptDouble(body, "holdSeconds", 25.0));
            return BuildStateJson();
        }

        private string HandleSwitchPose(JsonData body)
        {
            if (host.Scene == null || host.Flags == null) throw new InvalidOperationException("当前不在本体 H 场景中");
            string key = OptString(body, "key", "");
            if (key.Length == 0) throw new ArgumentException("key is required");
            bool anal = OptBool(body, "anal", false);
            bool autoInsert = OptBool(body, "autoInsert", false);
            bool startMotion = OptBool(body, "startMotion", autoInsert);
            var outcome = host.DirectorSwitchPose(key, anal, autoInsert, startMotion);
            var res = JsonMapper.ToObject(BuildStateJson());
            res["requestedPose"] = key;
            res["autoInsert"] = outcome.AutoInsert;
            res["startMotion"] = outcome.StartMotion;
            res["isExactMatch"] = outcome.IsExactMatch;
            if (outcome.Resolved != null)
            {
                res["resolvedKey"] = outcome.Resolved.Key;
                res["resolvedName"] = outcome.Resolved.VariantLabel;
                res["family"] = outcome.Resolved.Family;
                res["facing"] = outcome.Resolved.Facing;
                res["isDirectPenetration"] = outcome.Resolved.IsDirectPenetration;
            }
            var altArr = new JsonData();
            altArr.SetJsonType(JsonType.Array);
            for (int i = 0; i < outcome.Alternatives.Count; i++)
            {
                var a = outcome.Alternatives[i];
                var row = new JsonData();
                row["key"] = a.Key;
                row["variantLabel"] = a.VariantLabel;
                row["facing"] = a.Facing;
                altArr.Add(row);
            }
            res["alternatives"] = altArr;
            return res.ToJson();
        }

        private string HandleGameAction(JsonData body)
        {
            if (host.Scene == null || host.Flags == null) throw new InvalidOperationException("当前不在本体 H 场景中");
            string action = OptString(body, "action", "").ToLowerInvariant();
            if (action.Length == 0) throw new ArgumentException("action is required");
            double? f = HasKey(body, "female") ? (double?)OptDouble(body, "female", 0) : null;
            double? m = HasKey(body, "male") ? (double?)OptDouble(body, "male", 0) : null;
            host.DirectorGameAction(action, f, m);
            return BuildStateJson();
        }

        private string BuildCharactersJson(string query)
        {
            string q = ParseQueryParam(query, "q");
            var snap = host.DirectorListCharacters(q);
            var res = new JsonData();
            res["ok"] = true;
            res["inHScene"] = snap.InHScene;
            res["kPlugLoaded"] = snap.KPlugLoaded;
            res["invitePanelOpen"] = snap.InvitePanelOpen;

            var mainArr = new JsonData();
            mainArr.SetJsonType(JsonType.Array);
            for (int i = 0; i < snap.MainFemales.Count; i++)
            {
                var f = snap.MainFemales[i];
                var row = new JsonData();
                row["index"] = f.Index;
                row["name"] = f.Name ?? "";
                row["outfitSlot"] = f.OutfitSlot;
                row["outfitName"] = f.OutfitName ?? "";
                row["clothesState"] = f.ClothesState ?? "dressed";
                row["clothesDetail"] = f.ClothesDetail ?? "";
                mainArr.Add(row);
            }
            res["mainFemales"] = mainArr;

            var guestArr = new JsonData();
            guestArr.SetJsonType(JsonType.Array);
            for (int i = 0; i < snap.HaremGuests.Count; i++)
            {
                var g = snap.HaremGuests[i];
                var row = new JsonData();
                row["dealerId"] = g.DealerId;
                row["name"] = g.Name ?? "";
                row["masturbating"] = g.Masturbating;
                row["inOrgasm"] = g.InOrgasm;
                guestArr.Add(row);
            }
            res["haremGuests"] = guestArr;

            var storyArr = new JsonData();
            storyArr.SetJsonType(JsonType.Array);
            for (int i = 0; i < snap.InvitableStory.Count; i++) storyArr.Add(snap.InvitableStory[i]);
            res["invitableStory"] = storyArr;

            var cardArr = new JsonData();
            cardArr.SetJsonType(JsonType.Array);
            for (int i = 0; i < snap.InvitableCards.Count; i++) cardArr.Add(snap.InvitableCards[i]);
            res["invitableCards"] = cardArr;
            return res.ToJson();
        }

        private string HandleClothes(JsonData body)
        {
            int target = (int)OptDouble(body, "target", 0);
            string name = OptString(body, "name", "");
            string mode = OptString(body, "mode", "undress");
            int partIdx = -1;
            string partStr = "";
            if (HasKey(body, "part"))
            {
                var pv = body["part"];
                if (pv.IsInt || pv.IsLong || pv.IsDouble) partIdx = (int)OptDouble(body, "part", -1);
                else partStr = OptString(body, "part", "");
            }
            int[] explicitParts = null;
            if (HasKey(body, "parts") && body["parts"].IsArray && body["parts"].Count > 0)
            {
                var pList = new List<int>();
                for (int i = 0; i < body["parts"].Count; i++)
                {
                    var item = body["parts"][i];
                    if (item == null) continue;
                    int[] resolved;
                    if (item.IsInt || item.IsLong || item.IsDouble)
                        resolved = Plugin.ResolveClothParts(Convert.ToInt32(item.ToString()), null, null);
                    else
                        resolved = Plugin.ResolveClothParts(-1, item.ToString(), null);
                    for (int j = 0; j < resolved.Length; j++)
                        if (!pList.Contains(resolved[j])) pList.Add(resolved[j]);
                }
                if (pList.Count > 0) explicitParts = pList.ToArray();
            }
            var r = host.DirectorSetClothes(target, name, mode, partIdx, partStr, explicitParts);
            var res = new JsonData();
            res["ok"] = true;
            res["action"] = r.Action ?? "clothes";
            res["character"] = r.Character ?? "";
            res["detail"] = r.Detail ?? "";
            return res.ToJson();
        }

        private string HandleOutfit(JsonData body)
        {
            int target = (int)OptDouble(body, "target", 0);
            string name = OptString(body, "name", "");
            int slot = (int)OptDouble(body, "slot", 0);
            var r = host.DirectorSwitchOutfit(target, name, slot);
            var res = new JsonData();
            res["ok"] = true;
            res["action"] = r.Action ?? "outfit";
            res["character"] = r.Character ?? "";
            res["slot"] = r.Slot;
            res["detail"] = r.Detail ?? "";
            return res.ToJson();
        }

        private string HandleCharacters(JsonData body)
        {
            string action = OptString(body, "action", "");
            string name = OptString(body, "name", "");
            string style = OptString(body, "style", "");
            string behavior = OptString(body, "behavior", "");
            var r = host.DirectorCharacterCommand(action, name, style, behavior);
            var res = new JsonData();
            res["ok"] = true;
            res["action"] = r.Action ?? action;
            res["character"] = r.Character ?? "";
            res["detail"] = r.Detail ?? "";
            res["panelOpen"] = r.PanelOpen;
            return res.ToJson();
        }

        private string HandleScreenshot(DirectorHttpRequest req, JsonData body)
        {
            int width = (int)Numbers.Clamp(OptDouble(body, "width", 640), 160, 1280);
            int height = (int)Numbers.Clamp(OptDouble(body, "height", 360), 90, 720);
            bool includeBase64 = OptBool(body, "includeBase64", false);
            req.Deferred = true;
            host.EnqueueEndOfFrame(() =>
            {
                try
                {
                    string filePath;
                    byte[] png = host.DirectorCaptureScreenshot(width, height, out filePath);
                    var res = new JsonData();
                    res["ok"] = true;
                    res["path"] = filePath;
                    res["width"] = width;
                    res["height"] = height;
                    res["bytes"] = png.Length;
                    if (host.Measured != null) res["depthMm"] = Round1(host.Measured.TargetDepth * 1000.0);
                    if (includeBase64) res["base64Png"] = Convert.ToBase64String(png);
                    req.Complete(200, res.ToJson());
                }
                catch (Exception ex)
                {
                    req.Complete(500, DirectorBridge.ErrorJson(ex.GetBaseException().Message));
                }
            });
            return "";
        }

        private string HandleDirectorOverride(JsonData body)
        {
            var step = ParseStep(body);
            double ttl = OptDouble(body, "ttlSeconds", 45.0);
            host.Director.SetOverride(step, ttl);
            return BuildStateJson();
        }

        private string HandleDirectorSequence(JsonData body)
        {
            string name = OptString(body, "name", "AI节奏剧本");
            if (!HasKey(body, "steps") || !body["steps"].IsArray || body["steps"].Count == 0)
                throw new ArgumentException("steps array is required");
            var arr = body["steps"];
            var steps = new DirectorStep[arr.Count];
            for (int i = 0; i < arr.Count; i++) steps[i] = ParseStep(arr[i]);
            host.Director.StartSequence(name, steps);
            return BuildStateJson();
        }

        private string HandleDirectorEdgeGuard(JsonData body)
        {
            var guard = host.Director.SnapshotEdgeGuard();
            if (HasKey(body, "enabled")) guard.Enabled = OptBool(body, "enabled", guard.Enabled);
            if (HasKey(body, "femaleThreshold")) guard.FemaleThreshold = OptDouble(body, "femaleThreshold", guard.FemaleThreshold);
            if (HasKey(body, "maleThreshold")) guard.MaleThreshold = OptDouble(body, "maleThreshold", guard.MaleThreshold);
            if (HasKey(body, "brakeDurationSeconds")) guard.BrakeDurationSeconds = OptDouble(body, "brakeDurationSeconds", guard.BrakeDurationSeconds);
            if (HasKey(body, "cooldownSeconds")) guard.CooldownSeconds = OptDouble(body, "cooldownSeconds", guard.CooldownSeconds);
            if (HasKey(body, "brakeL0Min")) guard.BrakeL0Min = NormalizeUnit(OptDouble(body, "brakeL0Min", guard.BrakeL0Min));
            if (HasKey(body, "brakeL0Max")) guard.BrakeL0Max = NormalizeUnit(OptDouble(body, "brakeL0Max", guard.BrakeL0Max));
            if (HasKey(body, "brakeStage")) guard.BrakeStage = OptString(body, "brakeStage", guard.BrakeStage);
            if (HasKey(body, "brakeSpeed")) guard.BrakeSpeed = OptDouble(body, "brakeSpeed", guard.BrakeSpeed);
            host.Director.ConfigureEdgeGuard(guard);
            return BuildStateJson();
        }

        private string BuildConfigJson()
        {
            var cfg = host.DirectorReadConfig();
            var res = new JsonData();
            res["ok"] = true;
            res["playerLengthMm"] = cfg.PlayerLengthMm;
            res["deviceStrokeMm"] = cfg.DeviceStrokeMm;
            res["gameLengthMm"] = cfg.GameLengthMm;
            res["lengthAdaptation"] = cfg.LengthAdaptation;
            res["outerCeilingL0"] = cfg.OuterCeilingL0;
            res["feelPreset"] = cfg.FeelPreset ?? "Balanced";
            res["nonShaftMode"] = cfg.NonShaftMode ?? "Foreplay";
            res["smoothingMs"] = cfg.SmoothingMs;
            res["outputRatePercentPerSecond"] = cfg.OutputRatePercentPerSecond;
            res["sendDelayMs"] = cfg.SendDelayMs;
            res["previewEnabled"] = cfg.PreviewEnabled;
            res["xray"] = cfg.XRay;
            res["showEntryMarkers"] = cfg.ShowEntryMarkers;
            res["directorPort"] = cfg.DirectorPort;

            var soft = new JsonData();
            soft["R0"] = cfg.SoftRotation[0];
            soft["R1"] = cfg.SoftRotation[1];
            soft["R2"] = cfg.SoftRotation[2];
            res["softRotation"] = soft;

            var axes = new JsonData();
            for (int i = 0; i < 6; i++)
            {
                var ax = new JsonData();
                ax["min"] = cfg.AxisMin[i];
                ax["max"] = cfg.AxisMax[i];
                ax["gain"] = cfg.AxisGain[i];
                ax["enabled"] = cfg.AxisEnabled[i];
                axes[AxisNames[i]] = ax;
            }
            res["axes"] = axes;
            return res.ToJson();
        }

        private string HandleSetConfig(JsonData body)
        {
            var patch = new DirectorConfigPatch();
            if (HasKey(body, "playerLengthMm")) patch.PlayerLengthMm = OptDouble(body, "playerLengthMm", 150.0);
            if (HasKey(body, "deviceStrokeMm")) patch.DeviceStrokeMm = OptDouble(body, "deviceStrokeMm", 150.0);
            if (HasKey(body, "gameLengthMm")) patch.GameLengthMm = OptDouble(body, "gameLengthMm", 150.0);
            if (HasKey(body, "lengthAdaptation")) patch.LengthAdaptation = OptBool(body, "lengthAdaptation", true);
            if (HasKey(body, "outerCeilingL0")) patch.OuterCeilingL0 = OptDouble(body, "outerCeilingL0", 80.0);
            if (HasKey(body, "feelPreset")) patch.FeelPreset = ParseFeelPreset(OptString(body, "feelPreset", "Balanced"));
            if (HasKey(body, "nonShaftMode")) patch.NonShaftMode = ParseNonShaftMode(OptString(body, "nonShaftMode", "Foreplay"));
            if (HasKey(body, "smoothingMs")) patch.SmoothingMs = OptDouble(body, "smoothingMs", 25.0);
            if (HasKey(body, "outputRatePercentPerSecond")) patch.OutputRatePercentPerSecond = OptDouble(body, "outputRatePercentPerSecond", 450.0);
            if (HasKey(body, "sendDelayMs")) patch.SendDelayMs = OptDouble(body, "sendDelayMs", 0.0);
            if (HasKey(body, "previewEnabled")) patch.PreviewEnabled = OptBool(body, "previewEnabled", true);
            if (HasKey(body, "xray")) patch.XRay = OptBool(body, "xray", true);
            if (HasKey(body, "showEntryMarkers")) patch.ShowEntryMarkers = OptBool(body, "showEntryMarkers", true);
            patch.Save = OptBool(body, "save", true);

            if (HasKey(body, "softRotation") && body["softRotation"].IsObject)
            {
                var sr = body["softRotation"];
                if (HasKey(sr, "R0")) patch.SoftRotation[0] = OptBool(sr, "R0", true);
                if (HasKey(sr, "R1")) patch.SoftRotation[1] = OptBool(sr, "R1", true);
                if (HasKey(sr, "R2")) patch.SoftRotation[2] = OptBool(sr, "R2", true);
            }
            if (HasKey(body, "axes") && body["axes"].IsObject)
            {
                var axes = body["axes"];
                for (int i = 0; i < 6; i++)
                {
                    string k = AxisNames[i];
                    if (!HasKey(axes, k) || !axes[k].IsObject) continue;
                    var aObj = axes[k];
                    var ax = new DirectorAxisConfig();
                    if (HasKey(aObj, "min")) ax.Min = OptDouble(aObj, "min", 0.0);
                    if (HasKey(aObj, "max")) ax.Max = OptDouble(aObj, "max", 100.0);
                    if (HasKey(aObj, "gain")) ax.Gain = OptDouble(aObj, "gain", 1.0);
                    if (HasKey(aObj, "enabled")) ax.Enabled = OptBool(aObj, "enabled", true);
                    patch.Axes[i] = ax;
                }
            }
            host.DirectorApplyConfig(patch);
            return BuildConfigJson();
        }

        private string HandleDirectorTimeline(JsonData body)
        {
            string name = OptString(body, "name", "");
            bool loopSeq = OptBool(body, "loop", false);
            double ceil = host.ActiveOuterCeilingL0;
            TimelineStep[] steps;
            if (HasKey(body, "steps") && body["steps"].IsArray && body["steps"].Count > 0)
            {
                var arr = body["steps"];
                steps = new TimelineStep[arr.Count];
                for (int i = 0; i < arr.Count; i++) steps[i] = ParseTimelineStep(arr[i], ceil);
            }
            else
            {
                var single = ParseTimelineStep(body, ceil);
                if (loopSeq) single.Loop = true;
                steps = new[] { single };
            }
            host.DirectorStartTimeline(name, steps, loopSeq);

            // Optional paired expression preset right on Timeline launch
            string exprPreset = OptString(body, "expressionPreset", "");
            if (exprPreset.Length > 0 && host.Expressions != null)
            {
                string exprTarget = OptString(body, "expressionTarget", "both");
                host.Expressions.SetTarget(exprTarget, exprPreset);
            }
            return BuildStateJson();
        }

        private string HandleDirectorTimelineTune(JsonData body)
        {
            if (!host.Timeline.Active)
            {
                // Seamlessly start an appropriate custom/preset step on the fly when tuning while native animation runs!
                double ceil = host.ActiveOuterCeilingL0;
                var st = ParseTimelineStep(body, ceil);
                st.Loop = OptBool(body, "loop", true);
                if (!HasKey(body, "durationSeconds")) st.DurationSeconds = 45.0;
                host.DirectorStartTimeline(st.Name, new[] { st }, st.Loop);
                return BuildStateJson();
            }
            bool? loopSeq = HasKey(body, "loop") ? (bool?)OptBool(body, "loop", true) : null;
            host.DirectorTuneTimeline(st => ApplyTimelineOverrides(st, body), loopSeq);
            if (HasKey(body, "expressionPreset") && host.Expressions != null)
            {
                host.Expressions.SetTarget(OptString(body, "expressionTarget", "both"), OptString(body, "expressionPreset", ""));
            }
            return BuildStateJson();
        }

        private string HandleDirectorExpression(JsonData body)
        {
            if (host.Scene == null || host.Flags == null)
                throw new InvalidOperationException("当前不在本体 H 场景中");
            if (host.Expressions == null)
                throw new InvalidOperationException("表情导演模块未初始化");

            string target = OptString(body, "target", "both");
            if (OptBool(body, "release", false))
            {
                host.Expressions.ReleaseTarget(target);
                return BuildStateJson();
            }

            string preset = OptString(body, "preset", "");
            bool hasCustom =
                HasKey(body, "eyebrowPtn") || HasKey(body, "eyesPtn") || HasKey(body, "mouthPtn") ||
                HasKey(body, "eyebrowOpenMin") || HasKey(body, "eyebrowOpenMax") ||
                HasKey(body, "eyeOpenMin") || HasKey(body, "eyeOpenMax") ||
                HasKey(body, "mouthOpenMin") || HasKey(body, "mouthOpenMax") ||
                HasKey(body, "blush") || HasKey(body, "tears") ||
                HasKey(body, "hideHighlight") || HasKey(body, "eyeShake") ||
                HasKey(body, "blink") || HasKey(body, "rhythmSync") || HasKey(body, "lookEyes");

            if (preset.Length == 0 && !hasCustom)
                throw new ArgumentException("preset or custom facial parameters required (or set release=true)");

            Action<ExpressionSpec> customize = null;
            if (hasCustom)
            {
                customize = spec =>
                {
                    if (HasKey(body, "eyebrowPtn")) spec.EyebrowPtn = (int)OptDouble(body, "eyebrowPtn", spec.EyebrowPtn);
                    if (HasKey(body, "eyesPtn")) spec.EyesPtn = (int)OptDouble(body, "eyesPtn", spec.EyesPtn);
                    if (HasKey(body, "mouthPtn")) spec.MouthPtn = (int)OptDouble(body, "mouthPtn", spec.MouthPtn);
                    if (HasKey(body, "eyebrowOpenMin")) spec.EyebrowOpenMin = (float)NormalizeUnit(OptDouble(body, "eyebrowOpenMin", spec.EyebrowOpenMin));
                    if (HasKey(body, "eyebrowOpenMax")) spec.EyebrowOpenMax = (float)NormalizeUnit(OptDouble(body, "eyebrowOpenMax", spec.EyebrowOpenMax));
                    if (HasKey(body, "eyeOpenMin")) spec.EyeOpenMin = (float)NormalizeUnit(OptDouble(body, "eyeOpenMin", spec.EyeOpenMin));
                    if (HasKey(body, "eyeOpenMax")) spec.EyeOpenMax = (float)NormalizeUnit(OptDouble(body, "eyeOpenMax", spec.EyeOpenMax));
                    if (HasKey(body, "mouthOpenMin")) spec.MouthOpenMin = (float)NormalizeUnit(OptDouble(body, "mouthOpenMin", spec.MouthOpenMin));
                    if (HasKey(body, "mouthOpenMax")) spec.MouthOpenMax = (float)NormalizeUnit(OptDouble(body, "mouthOpenMax", spec.MouthOpenMax));
                    if (HasKey(body, "blush")) spec.Blush = (float)NormalizeUnit(OptDouble(body, "blush", spec.Blush));
                    if (HasKey(body, "tears")) spec.Tears = (byte)Numbers.Clamp(OptDouble(body, "tears", spec.Tears), 0, 3);
                    if (HasKey(body, "hideHighlight")) spec.HideHighlight = OptBool(body, "hideHighlight", spec.HideHighlight);
                    if (HasKey(body, "eyeShake")) spec.EyeShake = OptBool(body, "eyeShake", spec.EyeShake);
                    if (HasKey(body, "blink")) spec.Blink = OptBool(body, "blink", spec.Blink);
                    if (HasKey(body, "rhythmSync")) spec.RhythmSync = OptBool(body, "rhythmSync", spec.RhythmSync);
                    if (HasKey(body, "lookEyes")) spec.LookEyesPtn = ExpressionSpec.ParseLookEyesPtn(OptString(body, "lookEyes", "auto"));
                };
            }

            host.Expressions.SetTarget(target, preset.Length > 0 ? preset : "auto", customize);
            return BuildStateJson();
        }

        private string HandleDirectorGauges(JsonData body)
        {
            if (host.Scene == null || host.Flags == null) throw new InvalidOperationException("当前不在本体 H 场景中");
            bool managed = OptBool(body, "managed", true);
            double? f = HasKey(body, "female") ? (double?)OptDouble(body, "female", 0) : null;
            double? m = HasKey(body, "male") ? (double?)OptDouble(body, "male", 0) : null;
            double ramp = OptDouble(body, "rampSeconds", 3.0);
            bool preventAutoOLoop = OptBool(body, "preventAutoOLoop", true);
            host.DirectorConfigureGauges(managed, f, m, ramp, preventAutoOLoop);
            return BuildStateJson();
        }

        private string HandleDirectorAudio(JsonData body)
        {
            bool? muteSe = HasKey(body, "muteNativeSe") ? (bool?)OptBool(body, "muteNativeSe", false) : null;
            bool? muteVoice = HasKey(body, "muteNativeVoice") ? (bool?)OptBool(body, "muteNativeVoice", false) : null;
            double ttl = OptDouble(body, "ttlSeconds", 15.0);
            host.DirectorConfigureAudio(muteSe, muteVoice, ttl);
            var res = new JsonData();
            res["ok"] = true;
            res["muteNativeSe"] = host.MuteNativeSeActive;
            res["muteNativeVoice"] = host.MuteNativeVoiceActive;
            return res.ToJson();
        }

        private string HandleDirectorLipRms(JsonData body)
        {
            double rms = OptDouble(body, "rms", 0.0);
            double ttl = OptDouble(body, "ttlSeconds", 0.25);
            if (host.Expressions != null)
            {
                if (OptBool(body, "clear", false)) host.Expressions.ClearExternalLip();
                else host.Expressions.SetExternalLipRms((float)rms, (float)ttl);
            }
            var res = new JsonData();
            res["ok"] = true;
            res["lipRms"] = Round3(host.Expressions != null ? host.Expressions.ExternalLipRms : 0.0);
            return res.ToJson();
        }

        private string HandleDirectorAutonomy(JsonData body)
        {
            string mode = OptString(body, "mode", "");
            bool? pose = HasKey(body, "allowAutoPose") ? (bool?)OptBool(body, "allowAutoPose", host.AllowAutoPose) : null;
            bool? climax = HasKey(body, "allowAutoClimaxEdge") ? (bool?)OptBool(body, "allowAutoClimaxEdge", host.AllowAutoClimaxEdge) : null;
            bool? clothes = HasKey(body, "allowAutoClothes") ? (bool?)OptBool(body, "allowAutoClothes", host.AllowAutoClothes) : null;
            bool? kplug = HasKey(body, "allowAutoKPlugSwap") ? (bool?)OptBool(body, "allowAutoKPlugSwap", host.AllowAutoKPlugSwap) : null;
            host.DirectorConfigureAutonomy(mode, pose, climax, clothes, kplug);
            return BuildStateJson();
        }

        private string HandleDirectorMathLab(JsonData body)
        {
            if (OptBool(body, "stop", false) || string.Equals(OptString(body, "action", ""), "stop", StringComparison.OrdinalIgnoreCase))
            {
                host.DirectorStopMathLab("手动停止 SR6 数学实验台");
                return BuildStateJson();
            }

            bool tuneOnly = OptBool(body, "tuneOnly", false) && host.MathEngine.Active;
            if (tuneOnly)
            {
                host.DirectorTuneMathLab(p => ApplyMathLabOverrides(p, body));
                return BuildStateJson();
            }

            var pNew = host.MathEngine.Active ? host.MathEngine.CurrentParams : new SR6MathParams();
            ApplyMathLabOverrides(pNew, body);
            host.DirectorStartMathLab(pNew);
            return BuildStateJson();
        }

        private static void ApplyMathLabOverrides(SR6MathParams p, JsonData d)
        {
            if (d == null) return;
            if (HasKey(d, "family")) p.Family = SR6MathManifoldEngine.ParseFamily(OptString(d, "family", "virtual_pivot"));
            if (HasKey(d, "freqHz")) p.FreqHz = OptDouble(d, "freqHz", p.FreqHz);
            if (HasKey(d, "depthMin")) p.DepthMin = NormalizeUnit(OptDouble(d, "depthMin", p.DepthMin));
            if (HasKey(d, "depthMax")) p.DepthMax = NormalizeUnit(OptDouble(d, "depthMax", p.DepthMax));
            if (HasKey(d, "pivotZ")) p.PivotZ = NormalizeUnit(OptDouble(d, "pivotZ", p.PivotZ));
            if (HasKey(d, "pivotSweepHz")) p.PivotSweepHz = OptDouble(d, "pivotSweepHz", p.PivotSweepHz);
            if (HasKey(d, "helixTwist")) p.HelixTwist = OptDouble(d, "helixTwist", p.HelixTwist);
            if (HasKey(d, "orbitAmp")) p.OrbitAmp = OptDouble(d, "orbitAmp", p.OrbitAmp);
            if (HasKey(d, "beatHz")) p.BeatHz = OptDouble(d, "beatHz", p.BeatHz);
            if (HasKey(d, "flutterHz")) p.FlutterHz = OptDouble(d, "flutterHz", p.FlutterHz);
            if (HasKey(d, "flutterAmp")) p.FlutterAmp = OptDouble(d, "flutterAmp", p.FlutterAmp);
            if (HasKey(d, "knotP")) p.KnotP = (int)OptDouble(d, "knotP", p.KnotP);
            if (HasKey(d, "knotQ")) p.KnotQ = (int)OptDouble(d, "knotQ", p.KnotQ);
            if (HasKey(d, "femaleWeightShare")) p.FemaleWeightShare = OptDouble(d, "femaleWeightShare", p.FemaleWeightShare);
            if (HasKey(d, "softTissueWeight")) p.SoftTissueWeight = OptDouble(d, "softTissueWeight", p.SoftTissueWeight);
            if (HasKey(d, "bodyScale")) p.BodyScale = OptDouble(d, "bodyScale", p.BodyScale);
        }

        private string HandleDirectorRelease(JsonData body)
        {
            bool immediate = OptBool(body, "immediate", false);
            double fade = OptDouble(body, "fadeSeconds", 0.6);
            bool keepGaugeControl = OptBool(body, "keepGaugeControl", false);
            bool keepExpression = OptBool(body, "keepExpression", false);
            host.DirectorReleaseSpeedLatch();
            if (!keepGaugeControl) host.ReleaseDirectorGaugeControl();
            if (!keepExpression && host.Expressions != null) host.Expressions.ReleaseAll();
            if (host.MathEngine.Active) host.DirectorStopMathLab("AI 主动释放 SR6 数学实验台");
            if (host.Timeline.Active) host.DirectorStopTimeline("AI 主动释放时间轴变式");
            else if (!host.MathEngine.Active) host.RestoreTimelineActors();
            if (immediate)
            {
                if (host.BodyGrind != null) host.BodyGrind.Reset();
                host.Director.AbortImmediate("AI 主动立即释放");
            }
            else host.Director.Release(fade);
            return BuildStateJson();
        }

        private static TimelineStep ParseTimelineStep(JsonData d, double outerCeiling)
        {
            string presetStr = OptString(d, "preset", "custom");
            TimelinePreset preset = ParseTimelinePreset(presetStr);
            double dur = OptDouble(d, "durationSeconds", 12.0);
            var st = TimelineStep.FromPreset(preset, dur, outerCeiling);
            ApplyTimelineOverrides(st, d);
            return st;
        }

        private static void ApplyTimelineOverrides(TimelineStep st, JsonData d)
        {
            if (HasKey(d, "preset"))
            {
                var p = ParseTimelinePreset(OptString(d, "preset", ""));
                st.Preset = p;
                if (p == TimelinePreset.NineShallowOneDeep) { st.ShallowDeepMode = true; st.OrganicRandom = false; }
                else if (p == TimelinePreset.WaistOnly) { st.OrganicRandom = false; st.ShallowDeepMode = false; if (st.HoldDepth < 0) st.HoldDepth = 0.85; }
                else if (p != TimelinePreset.Organic) { st.OrganicRandom = false; }
            }
            if (HasKey(d, "name")) st.Name = OptString(d, "name", st.Name);
            if (HasKey(d, "durationSeconds")) st.DurationSeconds = OptDouble(d, "durationSeconds", st.DurationSeconds);
            if (HasKey(d, "rampSeconds")) st.RampSeconds = OptDouble(d, "rampSeconds", st.RampSeconds);
            if (HasKey(d, "loop")) st.Loop = OptBool(d, "loop", st.Loop);
            if (HasKey(d, "holdDepth"))
            {
                double hd = OptDouble(d, "holdDepth", -1.0);
                st.HoldDepth = hd >= 0.0 ? NormalizeUnit(hd) : -1.0;
                if (st.HoldDepth >= 0.0) { st.Preset = TimelinePreset.WaistOnly; st.OrganicRandom = false; st.ShallowDeepMode = false; }
            }
            if (HasKey(d, "depthMin")) { st.DepthMin = NormalizeUnit(OptDouble(d, "depthMin", st.DepthMin)); if (!HasKey(d, "holdDepth") && st.Preset != TimelinePreset.WaistOnly) st.HoldDepth = -1.0; }
            if (HasKey(d, "depthMax")) { st.DepthMax = NormalizeUnit(OptDouble(d, "depthMax", st.DepthMax)); if (!HasKey(d, "holdDepth") && st.Preset != TimelinePreset.WaistOnly) st.HoldDepth = -1.0; }
            if (HasKey(d, "l0Min")) st.L0Min = NormalizeUnit(OptDouble(d, "l0Min", st.L0Min));
            if (HasKey(d, "l0Max")) st.L0Max = NormalizeUnit(OptDouble(d, "l0Max", st.L0Max));
            if (HasKey(d, "freqHz")) st.FreqHz = OptDouble(d, "freqHz", st.FreqHz);
            if (HasKey(d, "freqEndHz")) st.FreqEndHz = OptDouble(d, "freqEndHz", st.FreqEndHz);
            if (HasKey(d, "inwardRatio")) st.InwardRatio = OptDouble(d, "inwardRatio", st.InwardRatio);
            if (HasKey(d, "pauseBottomSeconds")) st.PauseBottomSeconds = OptDouble(d, "pauseBottomSeconds", st.PauseBottomSeconds);
            if (HasKey(d, "pauseTopSeconds")) st.PauseTopSeconds = OptDouble(d, "pauseTopSeconds", st.PauseTopSeconds);
            if (HasKey(d, "shallowDeep"))
            {
                st.ShallowDeepMode = OptBool(d, "shallowDeep", st.ShallowDeepMode);
                if (st.ShallowDeepMode) { st.OrganicRandom = false; st.HoldDepth = -1.0; }
            }
            if (HasKey(d, "shallowCount"))
            {
                st.ShallowCount = (int)OptDouble(d, "shallowCount", st.ShallowCount);
                st.ShallowDeepMode = true;
                st.OrganicRandom = false;
                st.HoldDepth = -1.0;
            }
            if (HasKey(d, "deepCount"))
            {
                st.DeepCount = (int)OptDouble(d, "deepCount", st.DeepCount);
                st.ShallowDeepMode = true;
                st.OrganicRandom = false;
                st.HoldDepth = -1.0;
            }
            if (HasKey(d, "shallowBaseDepth")) st.ShallowBaseDepth = NormalizeUnit(OptDouble(d, "shallowBaseDepth", st.ShallowBaseDepth));
            if (HasKey(d, "shallowDepthMax")) st.ShallowDepthMax = NormalizeUnit(OptDouble(d, "shallowDepthMax", st.ShallowDepthMax));
            if (HasKey(d, "shallowL0Min")) st.ShallowL0Min = NormalizeUnit(OptDouble(d, "shallowL0Min", st.ShallowL0Min));
            if (HasKey(d, "deepHoldSeconds")) st.DeepHoldSeconds = OptDouble(d, "deepHoldSeconds", st.DeepHoldSeconds);
            if (HasKey(d, "twistAmp")) st.TwistAmp = NormalizeUnit(OptDouble(d, "twistAmp", st.TwistAmp));
            if (HasKey(d, "grindAmp")) st.GrindAmp = NormalizeUnit(OptDouble(d, "grindAmp", st.GrindAmp));
            if (HasKey(d, "grindHz")) st.GrindHz = OptDouble(d, "grindHz", st.GrindHz);
            if (HasKey(d, "grindPattern"))
            {
                st.GrindPattern = ParseGrindPattern(OptString(d, "grindPattern", "circle"));
                if (st.GrindPattern == GrindPattern.LineFB || st.GrindPattern == GrindPattern.LineLR ||
                    st.GrindPattern == GrindPattern.LineR1R2 || st.GrindPattern == GrindPattern.PitchOnly ||
                    st.GrindPattern == GrindPattern.RollOnly)
                {
                    st.OrganicRandom = false;
                    if (!HasKey(d, "twistAmp")) st.TwistAmp = 0.0;
                    if (!HasKey(d, "grindAmp") && st.GrindAmp < 0.15) st.GrindAmp = 0.36;
                }
            }
            if (HasKey(d, "mover")) st.MoverMode = ParseActiveMover(OptString(d, "mover", "auto"));
            if (HasKey(d, "bodyGrindScale")) st.BodyGrindScale = OptDouble(d, "bodyGrindScale", st.BodyGrindScale);
            if (HasKey(d, "pingPong")) st.PingPong = OptBool(d, "pingPong", st.PingPong);
            if (HasKey(d, "organicRandom")) st.OrganicRandom = OptBool(d, "organicRandom", st.OrganicRandom);
            if (HasKey(d, "organicIntensity")) st.OrganicIntensity = NormalizeUnit(OptDouble(d, "organicIntensity", st.OrganicIntensity));
            if (HasKey(d, "organicSeed")) st.OrganicSeed = (int)OptDouble(d, "organicSeed", st.OrganicSeed);
            if (HasKey(d, "spasmAmp")) st.SpasmAmp = OptDouble(d, "spasmAmp", st.SpasmAmp);
        }

        private static GrindPattern ParseGrindPattern(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant().Replace("-", "_"))
            {
                case "figure8": case "figure_8": case "eight": case "8": return GrindPattern.Figure8;
                case "rock_fb": case "rock": case "pelvic_rock": return GrindPattern.RockFB;
                case "corkscrew": case "screw": case "twist": return GrindPattern.Corkscrew;
                case "line_fb": case "linear_fb": case "fb_line": case "front_back": case "前后直线": case "前后摆腰": return GrindPattern.LineFB;
                case "line_lr": case "linear_lr": case "lr_line": case "left_right": case "左右直线": case "左右摆腰": return GrindPattern.LineLR;
                case "line_r1r2": case "r1r2": case "r1_r2": case "linear_r1r2": case "straight": case "直线": return GrindPattern.LineR1R2;
                case "pitch_only": case "r2_only": case "r2": return GrindPattern.PitchOnly;
                case "roll_only": case "r1_only": case "r1": return GrindPattern.RollOnly;
                case "auto": return GrindPattern.Auto;
                default: return GrindPattern.Circle;
            }
        }

        private static ActiveMoverMode ParseActiveMover(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "female": case "f": case "woman": return ActiveMoverMode.Female;
                case "male": case "m": case "man": return ActiveMoverMode.Male;
                case "both": case "shared": return ActiveMoverMode.Both;
                default: return ActiveMoverMode.Auto;
            }
        }

        private static TimelinePreset ParseTimelinePreset(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant().Replace("-", "_"))
            {
                case "tip_rub": case "glans_rub": case "tip_tease": return TimelinePreset.TipRub;
                case "deep_grind": case "bottom_grind": return TimelinePreset.DeepGrind;
                case "nine_shallow_one_deep": case "9s1d": case "shallow_deep": case "custom_shallow_deep": case "浅深": return TimelinePreset.NineShallowOneDeep;
                case "waist_only": case "grind_only": case "hold_grind": case "定深摆腰": case "只扭腰": return TimelinePreset.WaistOnly;
                case "tease_plunge": case "asymmetric": return TimelinePreset.TeasePlunge;
                case "edge_hover": case "hover": return TimelinePreset.EdgeHover;
                case "turbo_sprint": case "turbo": case "overdrive": return TimelinePreset.TurboSprint;
                case "climax_ejaculation": case "climax": case "ejaculate": case "cum": case "高潮射精": case "射精": return TimelinePreset.ClimaxEjaculation;
                case "organic": case "organic_random": case "markov": case "random": case "有机随机": return TimelinePreset.Organic;
                default: return TimelinePreset.Custom;
            }
        }

        private static DirectorStep ParseStep(JsonData d)
        {
            var step = new DirectorStep();
            step.Name = OptString(d, "name", "");
            step.DurationSeconds = OptDouble(d, "durationSeconds", 8.0);
            step.RampSeconds = OptDouble(d, "rampSeconds", 0.6);
            string m = OptString(d, "mode", "envelope").ToLowerInvariant();
            step.Mode = m == "hold" ? DirectorMode.Hold : m == "passthrough" ? DirectorMode.Passthrough : DirectorMode.Envelope;
            step.L0Min = NormalizeUnit(OptDouble(d, "l0Min", 0.0));
            step.L0Max = NormalizeUnit(OptDouble(d, "l0Max", 0.80));
            step.HoldL0 = NormalizeUnit(OptDouble(d, "holdL0", 0.72));
            step.IntensityScale = OptDouble(d, "intensityScale", 1.0);
            step.FlutterHz = OptDouble(d, "flutterHz", 0.0);
            step.FlutterAmp = NormalizeUnit(OptDouble(d, "flutterAmp", 0.0));
            step.GameStage = OptString(d, "gameStage", "");
            if (HasKey(d, "gameSpeed")) step.GameSpeed = OptDouble(d, "gameSpeed", 0.5);
            string fp = OptString(d, "feelPreset", "");
            if (fp.Length > 0) step.FeelPreset = ParseFeelPreset(fp);
            string ns = OptString(d, "nonShaftMode", "");
            if (ns.Length > 0) step.NonShaftMode = ParseNonShaftMode(ns);
            return step;
        }

        private static double NormalizeUnit(double v)
        {
            if (!Numbers.Finite(v)) return 0.0;
            return v > 1.0 ? v / 100.0 : v;
        }

        private static FeelPreset ParseFeelPreset(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "raw": case "0": return FeelPreset.Raw;
                case "intense": case "2": return FeelPreset.Intense;
                default: return FeelPreset.Balanced;
            }
        }

        private static NonShaftFeelMode ParseNonShaftMode(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "fullsync": case "full": case "1": return NonShaftFeelMode.FullSync;
                case "silent": case "2": return NonShaftFeelMode.Silent;
                default: return NonShaftFeelMode.Foreplay;
            }
        }

        private static JsonData ParseBody(string raw)
        {
            if (string.IsNullOrEmpty(raw) || raw.Trim().Length == 0)
            {
                var empty = new JsonData();
                empty.SetJsonType(JsonType.Object);
                return empty;
            }
            var parsed = JsonMapper.ToObject(raw);
            if (parsed == null || !parsed.IsObject) throw new ArgumentException("JSON body must be an object");
            return parsed;
        }

        private static bool HasKey(JsonData d, string key)
        {
            return d != null && d.IsObject && ((IDictionary)d).Contains(key) && d[key] != null;
        }
        private static string OptString(JsonData d, string key, string fallback)
        {
            return HasKey(d, key) ? Convert.ToString(d[key], CultureInfo.InvariantCulture) : fallback;
        }
        private static double OptDouble(JsonData d, string key, double fallback)
        {
            if (!HasKey(d, key)) return fallback;
            double v;
            return double.TryParse(Convert.ToString(d[key], CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }
        private static bool OptBool(JsonData d, string key, bool fallback)
        {
            if (!HasKey(d, key)) return fallback;
            var node = d[key];
            if (node.IsBoolean) return (bool)node;
            string s = Convert.ToString(node, CultureInfo.InvariantCulture).Trim().ToLowerInvariant();
            if (s == "true" || s == "1") return true;
            if (s == "false" || s == "0") return false;
            return fallback;
        }
        private static string ParseQueryParam(string query, string key)
        {
            if (string.IsNullOrEmpty(query)) return "";
            foreach (var part in query.Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                if (string.Equals(part.Substring(0, eq), key, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(part.Substring(eq + 1).Replace('+', ' '));
            }
            return "";
        }
        private static double Round1(double v) { return Numbers.Finite(v) ? Math.Round(v, 1) : 0.0; }
        private static double Round2(double v) { return Numbers.Finite(v) ? Math.Round(v, 2) : 0.0; }
        private static double Round3(double v) { return Numbers.Finite(v) ? Math.Round(v, 3) : 0.0; }
    }
}
