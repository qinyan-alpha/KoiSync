using System;
using System.Collections.Generic;

namespace KoiSync.Core
{
    public sealed class PoseCatalogEntry
    {
        public string Key = "";
        public string Name = "";
        public string GameName = "";
        public string Mode = "";
        public int Id;
        public string Effector = "";
        public string Contact = "";
        public bool AnalAllowed;
        public bool Selectable = true;

        // Enriched taxonomy fields for LLM disambiguation & facing-priority selection
        public string Family = "其他";
        public string VariantLabel = "";
        public string Facing = "face_to_face";   // "face_to_face" | "side" | "back"
        public int FacingRank;                   // 0 = face_to_face (highest priority), 1 = side, 2 = back
        public int ClassicRank = 2;              // 0 = iconic native classic, 1 = other native, 2 = kPlug, 3 = mod
        public bool IsDirectPenetration;         // STRICTLY true only for sonyu* + Shaft + VAGINAL/ANAL
        public bool IsFemaleInitiative;
        public string SpotType = "bed_floor";    // "bed_floor" | "chair" | "desk" | "wall" | "pool" | "other"
        public string Source = "native";         // "native" | "kplug" | "mod"
    }

    public sealed class PoseResolveResult
    {
        public PoseCatalogEntry BestMatch;
        public bool IsExactMatch;
        public string MatchedFamily = "";
        public readonly List<PoseCatalogEntry> Alternatives = new List<PoseCatalogEntry>();
    }

    /// <summary>
    /// Classifies all available Koikatsu native, kPlug, and modded H animations into
    /// structured families with Chinese variant labels, facing priority (face-to-face first),
    /// and strict direct-penetration tagging.
    /// </summary>
    public static class PoseCatalogClassifier
    {
        private static readonly Dictionary<string, string> NativeVariantLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Native sonyu
            { "sonyu_0", "标准正常位" },
            { "sonyu_1", "开腿正常位" },
            { "sonyu_2", "标准后背位" },
            { "sonyu_3", "拉臂后背位" },
            { "sonyu_4", "标准正面骑乘位" },
            { "sonyu_6", "标准侧卧位" },
            { "sonyu_7", "面对面站立位" },
            { "sonyu_8", "火车便当抱立位" },
            { "sonyu_9", "椅子面对面坐位" },
            { "sonyu_10", "椅子背面坐位" },
            { "sonyu_11", "椅子后背位" },
            { "sonyu_12", "椅子拉臂后背位" },
            { "sonyu_13", "桌寝正常位" },
            { "sonyu_14", "桌子后背位" },
            { "sonyu_15", "桌子拉臂后背位" },
            { "sonyu_16", "桌子侧卧位" },
            { "sonyu_17", "靠墙单腿抬起面对面" },
            { "sonyu_18", "靠墙后背位" },
            { "sonyu_19", "靠墙抬腿后背位" },
            { "sonyu_20", "泳池后背位" },
            { "sonyu_21", "围栏后背位" },
            { "sonyu_22", "扶栏火车便当位" },
            { "sonyu_23", "种付压腿传教士位" },
            { "sonyu_24", "屈曲M字正常位" },
            { "sonyu_25", "跳箱后背位" },
            { "sonyu_26", "俯卧寝后背位" },
            { "sonyu_27", "沙发正面骑乘位" },
            { "sonyu_28", "按压俯身寝后背位" },
            // Native 3P sonyu
            { "sonyu3P_0", "3P正面骑乘舔阴" },
            { "sonyu3P_1", "3P正面骑乘舔阴(换人)" },
            { "sonyu3P_2", "3P后背位手爱" },
            { "sonyu3P_3", "3P后背位手爱(换人)" },
            { "sonyu3PMMF_0", "双男按压正常位" },
            { "sonyu3PMMF_1", "双男按压侧位" },
            { "sonyu3PMMF_2", "双男后背兼口交" },
            // Native aibu
            { "aibu_0", "站立爱抚" },
            { "aibu_1", "仰卧爱抚" },
            { "aibu_2", "跪趴爱抚" },
            { "aibu_3", "椅子正面坐爱抚" },
            { "aibu_4", "椅子背面爱抚" },
            { "aibu_5", "桌子背面爱抚" },
            { "aibu_6", "靠墙背面爱抚" },
            { "aibu_7", "桌坐爱抚" },
            { "aibu_8", "桌旁蹲姿爱抚" },
            { "aibu_9", "书架旁爱抚" },
            { "aibu_10", "恶作剧爱抚" },
            // Native houshi highlights
            { "houshi_0", "龟头挑逗手交" },
            { "houshi_1", "单手手交" },
            { "houshi_2", "双手手交" },
            { "houshi_5", "舔尖与舔茎口交" },
            { "houshi_7", "单手辅助口交" },
            { "houshi_8", "双手辅助口交" },
            { "houshi_11", "标准乳交" },
            { "houshi_12", "夹臂乳交" },
            { "houshi_13", "压胸乳交" },
            { "houshi_15", "乳交兼口含" },
            { "houshi_16", "椅子龟头挑逗" },
            { "houshi_17", "椅子单手手交" },
            { "houshi_21", "椅子舔茎口交" },
            { "houshi_22", "椅子无手舔尖" },
            { "houshi_24", "椅子双手口交" },
            { "houshi_27", "椅子乳交" },
            { "houshi_28", "椅子夹臂乳交" },
            { "houshi_30", "椅子乳交兼舔" },
            { "houshi_31", "椅子乳交兼口含" },
            { "houshi_32", "站立龟头挑逗" },
            { "houshi_33", "站立单手手交" },
            { "houshi_35", "站立舔蛋手交" },
            { "houshi_36", "站立揉蛋手交" },
            { "houshi_39", "站立单手口交" },
            { "houshi_40", "站立双手口交" },
            { "houshi_42", "站立无手口交" },
            { "houshi_43", "站立乳交" },
            { "houshi_44", "站立夹臂乳交" },
            { "houshi_47", "站立乳交兼口含" },
            { "houshi_48", "泳池乳交" },
            { "houshi_50", "椅子跨坐口交" },
            { "houshi_51", "长椅口交" },
            { "houshi_52", "贴身手交" }
        };

        private static readonly HashSet<string> IconicClassicKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sonyu_0", "sonyu_4", "sonyu_2", "sonyu_6", "sonyu_7", "sonyu_9", "sonyu_13", "sonyu_17",
            "aibu_1", "aibu_0", "houshi_7", "houshi_11", "houshi_1", "sonyu3P_0", "sonyu3PMMF_0"
        };

        private static readonly string[] DistinctiveVariantHints = new[]
        {
            "开腿", "開脚", "屈曲", "マングリ", "M字", "种付", "種付け", "压腿", "按压大腿", "打桩机", "十指相扣", "锁腿", "抱膝", "挂男肩", "拱桥", "尼尔森", "V字",
            "沙发", "ソファ", "蛙蹲", "观音坐莲", "反向", "后背骑乘", "背身", "后仰", "前倾", "趴身", "拉腕", "捏男乳头", "侧身骑坐", "跪膝", "交叉怼",
            "拉臂", "腕引っ張り", "寝バック", "寝后背", "俯卧", "跪着", "跪坐", "狗爬", "跳箱", "跳び箱", "泳池", "プール", "围栏", "フェンス", "压迫", "押し付け",
            "火车便当", "駅弁", "公主抱", "椅子", "桌子", "桌寝", "机", "靠墙", "壁", "书架", "长椅",
            "深喉", "无手", "ノーハンド", "单手", "双手", "夹臂", "压胸", "口含", "足交", "足部", "素股", "股间", "腋下", "69", "太极", "舔阴", "骑面", "乳交", "手交", "口交"
        };

        public static PoseCatalogEntry Classify(
            string key,
            string name,
            string gameName,
            string mode,
            int id,
            string effector,
            string contact,
            bool analAllowed,
            bool selectable,
            bool nativeFemaleInitiative)
        {
            string k = key ?? "";
            string n = name ?? "";
            string gn = gameName ?? "";
            string m = mode ?? "";
            string eff = effector ?? "";
            string cnt = contact ?? "";
            string combined = n + " " + gn + " " + k;

            bool isKPlug = k.StartsWith("kplug_", StringComparison.OrdinalIgnoreCase) ||
                           n.StartsWith("kPlug", StringComparison.OrdinalIgnoreCase) ||
                           n.StartsWith("ksPlug", StringComparison.OrdinalIgnoreCase);
            bool isNative = !isKPlug && (
                k.StartsWith("sonyu", StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith("aibu", StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith("houshi", StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith("masturbation", StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith("lesbian", StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith("peeping", StringComparison.OrdinalIgnoreCase));

            bool isDirectPen = m.StartsWith("sonyu", StringComparison.OrdinalIgnoreCase) &&
                               string.Equals(eff, "Shaft", StringComparison.OrdinalIgnoreCase) &&
                               (string.Equals(cnt, "VAGINAL", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(cnt, "ANAL", StringComparison.OrdinalIgnoreCase));

            string label = ResolveVariantLabel(k, n, gn);
            string family = ClassifyFamily(m, cnt, eff, combined, label);
            string facing = ClassifyFacing(family, combined, label);
            int facingRank = facing == "face_to_face" ? 0 : (facing == "side" ? 1 : 2);
            int classicRank = IconicClassicKeys.Contains(k) ? 0 : (isNative ? 1 : (isKPlug ? 2 : 3));

            bool femInit = nativeFemaleInitiative ||
                           family == "骑乘位/女上位" ||
                           ContainsAny(combined, "騎乗", "骑乘", "乘骑", "女上位", "女主导", "女主动", "观音坐莲", "cowgirl");

            string spot = ClassifySpot(combined, label);

            return new PoseCatalogEntry
            {
                Key = k,
                Name = n,
                GameName = gn,
                Mode = m,
                Id = id,
                Effector = eff,
                Contact = cnt,
                AnalAllowed = analAllowed,
                Selectable = selectable,
                Family = family,
                VariantLabel = label,
                Facing = facing,
                FacingRank = facingRank,
                ClassicRank = classicRank,
                IsDirectPenetration = isDirectPen,
                IsFemaleInitiative = femInit,
                SpotType = spot,
                Source = isKPlug ? "kplug" : (isNative ? "native" : "mod")
            };
        }

        private static string ResolveVariantLabel(string key, string name, string gameName)
        {
            string mapped;
            if (NativeVariantLabels.TryGetValue(key, out mapped))
                return mapped;
            string s = !string.IsNullOrEmpty(name) ? name : gameName;
            if (s.StartsWith("kPlug · ", StringComparison.OrdinalIgnoreCase))
                s = s.Substring("kPlug · ".Length).Trim();
            else if (s.StartsWith("kPlug·", StringComparison.OrdinalIgnoreCase))
                s = s.Substring("kPlug·".Length).Trim();
            else if (s.StartsWith("ksPlug · ", StringComparison.OrdinalIgnoreCase))
                s = s.Substring("ksPlug · ".Length).Trim();
            else if (s.StartsWith("ksPlug·", StringComparison.OrdinalIgnoreCase))
                s = s.Substring("ksPlug·".Length).Trim();
            return string.IsNullOrEmpty(s) ? key : s;
        }

        private static string ClassifyFamily(string mode, string contact, string effector, string combined, string label)
        {
            string ml = mode.ToLowerInvariant();
            if (ml == "aibu") return "爱抚";
            if (ml == "masturbation" || ml == "lesbian" || ml == "peeping") return "自慰/百合/其他";

            if (ml.StartsWith("houshi"))
            {
                if (string.Equals(contact, "ORAL", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(effector, "Head", StringComparison.OrdinalIgnoreCase) ||
                    ContainsAny(combined, "フェラ", "口交", "深喉", "舔", "69", "太极", "イラマ", "blowjob", "throat"))
                    return "口交/深喉";
                if (string.Equals(contact, "BREASTS", StringComparison.OrdinalIgnoreCase) ||
                    ContainsAny(combined, "パイズリ", "乳交", "boobs", "titjob"))
                    return "乳交";
                if (string.Equals(contact, "BOTH_FEET", StringComparison.OrdinalIgnoreCase) ||
                    ContainsAny(combined, "足交", "足部", "脚", "foot"))
                    return "足交/素股";
                if (string.Equals(contact, "INTERCRURAL", StringComparison.OrdinalIgnoreCase) ||
                    ContainsAny(combined, "素股", "股间", "大腿", "腋下"))
                    return "足交/素股";
                return "手交/爱抚侍奉";
            }

            // Penetration (sonyu / sonyu3P / sonyu3PMMF)
            if (ContainsAny(combined, "騎乗", "骑乘", "乘骑", "女上位", "观音坐莲", "跨坐", "骑坐", "ride", "cowgirl", "骑身坐"))
                return "骑乘位/女上位";
            if (ContainsAny(combined, "側位", "侧位", "侧卧", "侧入", "side"))
                return "侧位";
            if (ContainsAny(combined, "正常位", "伝道", "传教", "種付け", "种付", "マングリ", "机寝位", "正面位", "打桩机", "M字", "V字", "尼尔森", "拱桥", "腿挂男肩", "脚踝交叠", "missionary", "amazon"))
                return "正常位/传教士";
            if (ContainsAny(combined, "後背", "后背", "バック", "后入", "狗爬", "狗趴", "doggy", "青蛙蹲", "趴桌后入"))
                return "后背位";
            if (ContainsAny(combined, "対面", "面对面", "駅弁", "立位", "站立", "正对", "公主抱", "抱位", "抱起", "靠墙", "壁", "椅子", "坐位"))
                return "面对面/站立与坐姿";
            return "其他插入体位";
        }

        private static string ClassifyFacing(string family, string combined, string label)
        {
            // Explicit back / reverse indicators ALWAYS mark as back, even inside cowgirl or chair poses!
            if (ContainsAny(combined, "背面", "反向", "后背", "後背", "背身", "バック", "后入", "狗爬", "狗趴", "reverse", "doggy", "from behind"))
                return "back";
            if (family == "后背位")
                return "back";
            if (family == "侧位" || ContainsAny(combined, "侧身", "側位", "侧卧", "侧入"))
                return "side";
            return "face_to_face";
        }

        private static string ClassifySpot(string combined, string label)
        {
            if (ContainsAny(combined, "椅子", "座椅", "长椅", "ソファ", "沙发", "chair", "seat", "bench")) return "chair";
            if (ContainsAny(combined, "机", "桌", "跳び箱", "跳箱", "desk", "table")) return "desk";
            if (ContainsAny(combined, "壁", "墙", "围栏", "フェンス", "书架", "wall")) return "wall";
            if (ContainsAny(combined, "プール", "泳池", "シャワー", "淋浴")) return "pool";
            return "bed_floor";
        }

        public static string InferQueryFamily(string query)
        {
            string q = (query ?? "").Trim();
            if (q.Length == 0) return "";
            if (ContainsAny(q, "正常", "传教", "伝道", "种付", "種付け", "屈曲", "マングリ", "正面位", "missionary")) return "正常位/传教士";
            if (ContainsAny(q, "骑乘", "乘骑", "騎乗", "女上位", "上位", "坐上来", "骑上去", "观音坐莲", "蛙蹲", "cowgirl")) return "骑乘位/女上位";
            if (ContainsAny(q, "后背", "後背", "背后", "后入", "バック", "狗爬", "狗趴", "doggy")) return "后背位";
            if (ContainsAny(q, "侧卧", "侧位", "側位", "侧入")) return "侧位";
            if (ContainsAny(q, "面对面", "对面", "対面", "立位", "站立", "站着", "火车便当", "駅弁", "抱起")) return "面对面/站立与坐姿";
            if (ContainsAny(q, "爱抚", "愛撫", "前戏", "摸摸")) return "爱抚";
            if (ContainsAny(q, "口交", "深喉", "フェラ", "舔", "69", "用嘴", "口活")) return "口交/深喉";
            if (ContainsAny(q, "乳交", "胸推", "夹胸", "パイズリ")) return "乳交";
            if (ContainsAny(q, "足交", "用脚", "踩", "素股")) return "足交/素股";
            if (ContainsAny(q, "手交", "撸", "手コキ", "打飞机")) return "手交/爱抚侍奉";
            return "";
        }

        public static bool QueryHasDistinctiveVariantHint(string query)
        {
            string q = (query ?? "").Trim();
            if (q.Length == 0) return false;
            for (int i = 0; i < DistinctiveVariantHints.Length; i++)
            {
                if (q.IndexOf(DistinctiveVariantHints[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        public static int CompareForPriority(PoseCatalogEntry a, PoseCatalogEntry b, bool preferFaceToFace)
        {
            if (preferFaceToFace)
            {
                int fc = a.FacingRank.CompareTo(b.FacingRank);
                if (fc != 0) return fc;
            }
            int cc = a.ClassicRank.CompareTo(b.ClassicRank);
            if (cc != 0) return cc;
            int mc = a.Id.CompareTo(b.Id);
            if (mc != 0) return mc;
            return string.CompareOrdinal(a.Key, b.Key);
        }

        /// <summary>
        /// Resolves a player/AI pose request against the current scene's selectable pose catalog.
        /// Implements Option B (Switch-Best-Facing-First + Return Sibling Alternatives) with
        /// automatic Exact-Match detection when the query pinpoints a specific pose/variant.
        /// </summary>
        public static PoseResolveResult ResolveQuery(IList<PoseCatalogEntry> selectable, string query, bool preferFaceToFace = true, bool preferDirectPenetration = false)
        {
            var result = new PoseResolveResult();
            if (selectable == null || selectable.Count == 0) return result;

            string q = (query ?? "").Trim();
            if (q.Length == 0) return result;

            // 1) Exact Key match -> always exact!
            for (int i = 0; i < selectable.Count; i++)
            {
                var e = selectable[i];
                if (string.Equals(e.Key, q, StringComparison.OrdinalIgnoreCase))
                {
                    result.BestMatch = e;
                    result.IsExactMatch = true;
                    result.MatchedFamily = e.Family;
                    CollectAlternatives(selectable, e, preferFaceToFace, result.Alternatives);
                    return result;
                }
            }

            bool explicitBack = ContainsAny(q, "背面", "反向", "后背", "後背", "背身", "后入", "バック", "狗爬", "狗趴", "doggy", "reverse");
            bool effectiveFacePref = preferFaceToFace && !explicitBack;
            bool hasVariantHint = QueryHasDistinctiveVariantHint(q);
            string inferredFamily = InferQueryFamily(q);

            // 1.5) Broad generic family words ("传教士位", "正常位", "乘骑位", "骑乘位", "后背位", "侧位", etc.)
            // go straight to family priority resolution so iconic face-to-face classics ALWAYS win
            // instead of accidentally substring-matching an obscure variant label!
            if (IsBroadGenericPoseWord(q) && !string.IsNullOrEmpty(inferredFamily))
            {
                var famMatches = new List<PoseCatalogEntry>();
                for (int i = 0; i < selectable.Count; i++)
                {
                    if (string.Equals(selectable[i].Family, inferredFamily, StringComparison.Ordinal))
                        famMatches.Add(selectable[i]);
                }
                if (famMatches.Count > 0)
                {
                    famMatches.Sort((a, b) => CompareForPriority(a, b, effectiveFacePref));
                    var best = famMatches[0];
                    result.BestMatch = best;
                    result.IsExactMatch = famMatches.Count == 1;
                    result.MatchedFamily = best.Family;
                    CollectAlternatives(selectable, best, effectiveFacePref, result.Alternatives);
                    return result;
                }
            }

            // 2) Exact VariantLabel / Name / GameName match
            var exactLabelMatches = new List<PoseCatalogEntry>();
            for (int i = 0; i < selectable.Count; i++)
            {
                var e = selectable[i];
                if (string.Equals(e.VariantLabel, q, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.Name, q, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.GameName, q, StringComparison.OrdinalIgnoreCase))
                {
                    exactLabelMatches.Add(e);
                }
            }
            if (exactLabelMatches.Count > 0)
            {
                exactLabelMatches.Sort((a, b) => CompareForPriority(a, b, effectiveFacePref));
                var best = exactLabelMatches[0];
                result.BestMatch = best;
                // Generic family words like "正常位" / "騎乗位" / "後背位" are broad requests, NOT narrow sub-variant requests
                bool isBroadGenericName = IsBroadGenericPoseWord(q);
                result.IsExactMatch = !isBroadGenericName || hasVariantHint;
                result.MatchedFamily = best.Family;
                CollectAlternatives(selectable, best, effectiveFacePref, result.Alternatives);
                return result;
            }

            // 3) Substring match on specific variant hints (e.g. "开腿正常位", "蛙蹲骑乘", "背面骑乘", "沙发骑乘")
            var scored = new List<KeyValuePair<int, PoseCatalogEntry>>();
            for (int i = 0; i < selectable.Count; i++)
            {
                var e = selectable[i];
                int score = ScoreEntryAgainstQuery(e, q, inferredFamily, explicitBack, effectiveFacePref);
                if (score > 0)
                    scored.Add(new KeyValuePair<int, PoseCatalogEntry>(score, e));
            }

            if (scored.Count > 0)
            {
                scored.Sort((a, b) =>
                {
                    int sc = b.Key.CompareTo(a.Key);
                    if (sc != 0) return sc;
                    return CompareForPriority(a.Value, b.Value, effectiveFacePref);
                });
                var best = scored[0].Value;
                result.BestMatch = best;
                result.MatchedFamily = best.Family;
                bool uniqueStrong = scored.Count == 1 || (scored[0].Key - scored[1].Key >= 35);
                result.IsExactMatch = hasVariantHint && (uniqueStrong || scored[0].Key >= 110);
                CollectAlternatives(selectable, best, effectiveFacePref, result.Alternatives);
                return result;
            }

            // 4) Family match (e.g. user said "传教士位" or "乘骑位" -> pick highest-priority face-to-face pose in that family)
            if (!string.IsNullOrEmpty(inferredFamily))
            {
                var famMatches = new List<PoseCatalogEntry>();
                for (int i = 0; i < selectable.Count; i++)
                {
                    if (string.Equals(selectable[i].Family, inferredFamily, StringComparison.Ordinal))
                        famMatches.Add(selectable[i]);
                }
                if (famMatches.Count > 0)
                {
                    famMatches.Sort((a, b) => CompareForPriority(a, b, effectiveFacePref));
                    var best = famMatches[0];
                    result.BestMatch = best;
                    result.IsExactMatch = famMatches.Count == 1;
                    result.MatchedFamily = best.Family;
                    CollectAlternatives(selectable, best, effectiveFacePref, result.Alternatives);
                    return result;
                }
            }

            // 5) Spot-aware fallback: if user wanted penetration at a spot lacking bed missionary (e.g. wall/chair),
            // prioritize face-to-face direct-penetration poses available at this spot!
            if (preferDirectPenetration || IsPenetrationFamily(inferredFamily))
            {
                var pen = new List<PoseCatalogEntry>();
                for (int i = 0; i < selectable.Count; i++)
                {
                    if (selectable[i].IsDirectPenetration) pen.Add(selectable[i]);
                }
                if (pen.Count > 0)
                {
                    pen.Sort((a, b) => CompareForPriority(a, b, effectiveFacePref));
                    var best = pen[0];
                    result.BestMatch = best;
                    result.IsExactMatch = false;
                    result.MatchedFamily = best.Family;
                    CollectAlternatives(selectable, best, effectiveFacePref, result.Alternatives);
                    return result;
                }
            }

            return result;
        }

        private static bool IsBroadGenericPoseWord(string q)
        {
            string s = (q ?? "").Trim();
            switch (s)
            {
                case "正常位":
                case "传教士":
                case "传教士位":
                case "騎乗位":
                case "骑乘位":
                case "乘骑位":
                case "女上位":
                case "後背位":
                case "后背位":
                case "后入":
                case "侧位":
                case "側位":
                case "侧卧位":
                case "立位":
                case "站立位":
                case "爱抚":
                case "愛撫":
                case "口交":
                case "乳交":
                case "手交":
                case "足交":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsPenetrationFamily(string family)
        {
            return family == "正常位/传教士" ||
                   family == "骑乘位/女上位" ||
                   family == "后背位" ||
                   family == "侧位" ||
                   family == "面对面/站立与坐姿" ||
                   family == "其他插入体位";
        }

        private static int ScoreEntryAgainstQuery(PoseCatalogEntry e, string q, string inferredFamily, bool explicitBack, bool preferFaceToFace)
        {
            int score = 0;
            string hay = e.VariantLabel + " " + e.Name + " " + e.GameName + " " + e.Key;

            if (e.VariantLabel.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                e.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                e.GameName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 90;
            }

            if (!string.IsNullOrEmpty(inferredFamily) && string.Equals(e.Family, inferredFamily, StringComparison.Ordinal))
            {
                score += 45;
            }

            // Distinctive sub-variant token hits
            int tokenHits = 0;
            for (int i = 0; i < DistinctiveVariantHints.Length; i++)
            {
                string h = DistinctiveVariantHints[i];
                if (q.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    hay.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tokenHits++;
                }
            }
            score += tokenHits * 40;

            if (score <= 0) return 0;

            // Facing bonus: strongly favor face-to-face unless user explicitly asked for back/reverse!
            if (explicitBack)
            {
                if (e.Facing == "back") score += 28;
                else score -= 18;
            }
            else if (preferFaceToFace)
            {
                if (e.Facing == "face_to_face") score += 24;
                else if (e.Facing == "side") score += 6;
                else score -= 16;
            }

            // Iconic native classics win ties over obscure sub-variants
            score += Math.Max(0, 12 - e.ClassicRank * 4);
            return score;
        }

        private static void CollectAlternatives(IList<PoseCatalogEntry> selectable, PoseCatalogEntry chosen, bool preferFaceToFace, List<PoseCatalogEntry> dest)
        {
            var siblings = new List<PoseCatalogEntry>();
            for (int i = 0; i < selectable.Count; i++)
            {
                var e = selectable[i];
                if (string.Equals(e.Key, chosen.Key, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(e.Family, chosen.Family, StringComparison.Ordinal))
                    siblings.Add(e);
            }
            siblings.Sort((a, b) => CompareForPriority(a, b, preferFaceToFace));
            int cap = Math.Min(6, siblings.Count);
            for (int i = 0; i < cap; i++) dest.Add(siblings[i]);
        }

        private static bool ContainsAny(string haystack, params string[] needles)
        {
            if (string.IsNullOrEmpty(haystack)) return false;
            for (int i = 0; i < needles.Length; i++)
            {
                if (haystack.IndexOf(needles[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
