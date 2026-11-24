using System;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.MainGame
{
    internal sealed class ExpressionSpec
    {
        public bool Active;
        public string PresetName = "auto";
        public int EyebrowPtn;
        public int EyesPtn;
        public int MouthPtn;
        public float EyebrowOpenMin = 0.45f;
        public float EyebrowOpenMax = 0.85f;
        public float EyeOpenMin = 0.35f;
        public float EyeOpenMax = 0.70f;
        public float MouthOpenMin = 0.20f;
        public float MouthOpenMax = 0.65f;
        public float Blush = 0.60f;
        public byte Tears;             // 0=none, 1=glistening, 2=medium droplets, 3=streaming
        public bool HideHighlight;     // true=dimmed/vacant trance or pained eyes
        public bool EyeShake;          // true=trembling pupils (ecstasy/strain)
        public bool Blink = true;
        public bool RhythmSync = true; // Dynamically pulse eyes/mouth/blush with thrust & grind depth
        public int LookEyesPtn = -1;   // -1=auto(native), 0=front, 1=camera, 2=partner, 3=away

        public ExpressionSpec Copy()
        {
            return (ExpressionSpec)MemberwiseClone();
        }

        public static ExpressionSpec FromPreset(string rawPreset, bool isMale)
        {
            string key = NormalizePresetKey(rawPreset);
            switch (key)
            {
                case "happy_trance":
                    return isMale
                        ? new ExpressionSpec
                        {
                            Active = true, PresetName = "happy_trance",
                            EyebrowPtn = 13, EyesPtn = 4, MouthPtn = 1,
                            EyebrowOpenMin = 0.50f, EyebrowOpenMax = 0.85f,
                            EyeOpenMin = 0.50f, EyeOpenMax = 0.80f,
                            MouthOpenMin = 0.18f, MouthOpenMax = 0.46f,
                            Blush = 0.45f, Tears = 0, HideHighlight = false, EyeShake = false, Blink = true, RhythmSync = true
                        }
                        : new ExpressionSpec
                        {
                            Active = true, PresetName = "happy_trance",
                            EyebrowPtn = 13, EyesPtn = 4, MouthPtn = 3,
                            EyebrowOpenMin = 0.40f, EyebrowOpenMax = 0.78f,
                            EyeOpenMin = 0.42f, EyeOpenMax = 0.74f,
                            MouthOpenMin = 0.28f, MouthOpenMax = 0.72f,
                            Blush = 0.86f, Tears = 0, HideHighlight = false, EyeShake = false, Blink = true, RhythmSync = true
                        };

                case "heart_trance":
                    return isMale
                        ? new ExpressionSpec
                        {
                            Active = true, PresetName = "heart_trance",
                            EyebrowPtn = 2, EyesPtn = 4, MouthPtn = 1,
                            EyebrowOpenMin = 0.45f, EyebrowOpenMax = 0.78f,
                            EyeOpenMin = 0.48f, EyeOpenMax = 0.76f,
                            MouthOpenMin = 0.20f, MouthOpenMax = 0.50f,
                            Blush = 0.65f, Tears = 0, Blink = true, RhythmSync = true
                        }
                        : new ExpressionSpec
                        {
                            // EyesPtn=25 is Koikatsu's authentic heart-pupil eyes (瞳にハート)!
                            Active = true, PresetName = "heart_trance",
                            EyebrowPtn = 2, EyesPtn = 25, MouthPtn = 3,
                            EyebrowOpenMin = 0.35f, EyebrowOpenMax = 0.72f,
                            EyeOpenMin = 0.46f, EyeOpenMax = 0.76f,
                            MouthOpenMin = 0.35f, MouthOpenMax = 0.82f,
                            Blush = 0.94f, Tears = 1, HideHighlight = false, EyeShake = true, Blink = false, RhythmSync = true
                        };

                case "sweet_smile":
                    return isMale
                        ? new ExpressionSpec
                        {
                            Active = true, PresetName = "sweet_smile",
                            EyebrowPtn = 0, EyesPtn = 2, MouthPtn = 1,
                            EyebrowOpenMin = 0.70f, EyebrowOpenMax = 0.95f,
                            EyeOpenMin = 0.72f, EyeOpenMax = 0.95f,
                            MouthOpenMin = 0.18f, MouthOpenMax = 0.40f,
                            Blush = 0.28f, Tears = 0, Blink = true, RhythmSync = true
                        }
                        : new ExpressionSpec
                        {
                            Active = true, PresetName = "sweet_smile",
                            EyebrowPtn = 0, EyesPtn = 2, MouthPtn = 1,
                            EyebrowOpenMin = 0.65f, EyebrowOpenMax = 0.95f,
                            EyeOpenMin = 0.66f, EyeOpenMax = 0.94f,
                            MouthOpenMin = 0.22f, MouthOpenMax = 0.54f,
                            Blush = 0.58f, Tears = 0, Blink = true, RhythmSync = true
                        };

                case "shy_tease":
                    return isMale
                        ? new ExpressionSpec
                        {
                            Active = true, PresetName = "shy_tease",
                            EyebrowPtn = 14, EyesPtn = 20, MouthPtn = 19,
                            EyebrowOpenMin = 0.65f, EyebrowOpenMax = 0.90f,
                            EyeOpenMin = 0.68f, EyeOpenMax = 0.88f,
                            MouthOpenMin = 0.14f, MouthOpenMax = 0.34f,
                            Blush = 0.30f, Tears = 0, Blink = true, RhythmSync = true
                        }
                        : new ExpressionSpec
                        {
                            Active = true, PresetName = "shy_tease",
                            EyebrowPtn = 2, EyesPtn = 8, MouthPtn = 5,
                            EyebrowOpenMin = 0.50f, EyebrowOpenMax = 0.85f,
                            EyeOpenMin = 0.52f, EyeOpenMax = 0.82f,
                            MouthOpenMin = 0.18f, MouthOpenMax = 0.54f,
                            Blush = 0.80f, Tears = 0, Blink = true, RhythmSync = true
                        };

                case "ecstasy_melt":
                    return isMale
                        ? new ExpressionSpec
                        {
                            Active = true, PresetName = "ecstasy_melt",
                            EyebrowPtn = 2, EyesPtn = 7, MouthPtn = 6,
                            EyebrowOpenMin = 0.30f, EyebrowOpenMax = 0.65f,
                            EyeOpenMin = 0.30f, EyeOpenMax = 0.58f,
                            MouthOpenMin = 0.28f, MouthOpenMax = 0.62f,
                            Blush = 0.65f, Tears = 0, HideHighlight = false, EyeShake = false, Blink = false, RhythmSync = true
                        }
                        : new ExpressionSpec
                        {
                            Active = true, PresetName = "ecstasy_melt",
                            EyebrowPtn = 2, EyesPtn = 7, MouthPtn = 14,
                            EyebrowOpenMin = 0.20f, EyebrowOpenMax = 0.55f,
                            EyeOpenMin = 0.18f, EyeOpenMax = 0.46f,
                            MouthOpenMin = 0.42f, MouthOpenMax = 0.90f,
                            Blush = 0.95f, Tears = 1, HideHighlight = false, EyeShake = true, Blink = false, RhythmSync = true
                        };

                case "pain_reluctant":
                    return isMale
                        ? new ExpressionSpec
                        {
                            // Dominant/stern counterpart on male during rough/intense roleplay
                            Active = true, PresetName = "pain_reluctant",
                            EyebrowPtn = 1, EyesPtn = 9, MouthPtn = 19,
                            EyebrowOpenMin = 0.65f, EyebrowOpenMax = 0.95f,
                            EyeOpenMin = 0.72f, EyeOpenMax = 0.92f,
                            MouthOpenMin = 0.16f, MouthOpenMax = 0.40f,
                            Blush = 0.22f, Tears = 0, HideHighlight = false, EyeShake = false, Blink = true, RhythmSync = true
                        }
                        : new ExpressionSpec
                        {
                            // Live-verified clearest pained/unhappy face: angry brows (1), disgusted/strained eyes (13 嫌悪),
                            // clenched open mouth (11 sinken03), streaming tears (3) & dimmed highlight
                            Active = true, PresetName = "pain_reluctant",
                            EyebrowPtn = 1, EyesPtn = 13, MouthPtn = 11,
                            EyebrowOpenMin = 0.48f, EyebrowOpenMax = 0.84f,
                            EyeOpenMin = 0.72f, EyeOpenMax = 0.92f,
                            MouthOpenMin = 0.78f, MouthOpenMax = 0.98f,
                            Blush = 0.45f, Tears = 3, HideHighlight = true, EyeShake = true, Blink = false, RhythmSync = true
                        };

                case "pain_wince":
                    return isMale
                        ? new ExpressionSpec
                        {
                            Active = true, PresetName = "pain_wince",
                            EyebrowPtn = 10, EyesPtn = 10, MouthPtn = 10,
                            EyebrowOpenMin = 0.60f, EyebrowOpenMax = 0.90f,
                            EyeOpenMin = 0.65f, EyeOpenMax = 0.88f,
                            MouthOpenMin = 0.20f, MouthOpenMax = 0.48f,
                            Blush = 0.35f, Tears = 0, Blink = true, RhythmSync = true
                        }
                        : new ExpressionSpec
                        {
                            // EyesPtn=35 is Koikatsu's authentic tightly squeezed shut > < (両目ぎゅっと閉じ) + streaming tears (3) + gritting mouth (11)
                            Active = true, PresetName = "pain_wince",
                            EyebrowPtn = 11, EyesPtn = 35, MouthPtn = 11,
                            EyebrowOpenMin = 0.30f, EyebrowOpenMax = 0.68f,
                            EyeOpenMin = 0.00f, EyeOpenMax = 0.18f,
                            MouthOpenMin = 0.38f, MouthOpenMax = 0.86f,
                            Blush = 0.62f, Tears = 3, HideHighlight = true, EyeShake = true, Blink = false, RhythmSync = true
                        };

                case "dominant_smug":
                    return new ExpressionSpec
                    {
                        Active = true, PresetName = "dominant_smug",
                        EyebrowPtn = 14, EyesPtn = 20, MouthPtn = 19,
                        EyebrowOpenMin = 0.65f, EyebrowOpenMax = 0.92f,
                        EyeOpenMin = 0.60f, EyeOpenMax = 0.84f,
                        MouthOpenMin = 0.18f, MouthOpenMax = 0.48f,
                        Blush = isMale ? 0.25f : 0.38f, Tears = 0, Blink = true, RhythmSync = true
                    };

                case "climax":
                    return isMale
                        ? new ExpressionSpec
                        {
                            Active = true, PresetName = "climax",
                            EyebrowPtn = 2, EyesPtn = 35, MouthPtn = 10,
                            EyebrowOpenMin = 0.25f, EyebrowOpenMax = 0.55f,
                            EyeOpenMin = 0.05f, EyeOpenMax = 0.28f,
                            MouthOpenMin = 0.45f, MouthOpenMax = 0.82f,
                            Blush = 0.78f, Tears = 0, Blink = false, RhythmSync = true
                        }
                        : new ExpressionSpec
                        {
                            Active = true, PresetName = "climax",
                            EyebrowPtn = 2, EyesPtn = 35, MouthPtn = 14,
                            EyebrowOpenMin = 0.15f, EyebrowOpenMax = 0.45f,
                            EyeOpenMin = 0.04f, EyeOpenMax = 0.24f,
                            MouthOpenMin = 0.62f, MouthOpenMax = 0.98f,
                            Blush = 1.00f, Tears = 2, HideHighlight = false, EyeShake = true, Blink = false, RhythmSync = true
                        };

                case "custom":
                    return new ExpressionSpec
                    {
                        Active = true, PresetName = "custom",
                        EyebrowPtn = 0, EyesPtn = 0, MouthPtn = 0,
                        EyeOpenMin = 0.45f, EyeOpenMax = 0.80f,
                        MouthOpenMin = 0.20f, MouthOpenMax = 0.55f,
                        Blush = 0.50f, Tears = 0, Blink = true, RhythmSync = true
                    };

                default:
                    return new ExpressionSpec { Active = false, PresetName = "auto" };
            }
        }

        public static string NormalizePresetKey(string raw)
        {
            string s = (raw ?? "").Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
            switch (s)
            {
                case "happy_trance": case "bliss_daze": case "happy_daze": case "bliss": case "开心恍惚": case "高兴恍惚": return "happy_trance";
                case "heart_trance": case "heart_eyes": case "lovestruck": case "爱心眼": case "爱心沉醉": return "heart_trance";
                case "sweet_smile": case "happy": case "smile": case "gentle": case "温柔微笑": case "开心": return "sweet_smile";
                case "shy_tease": case "shy": case "bashful": case "害羞": return "shy_tease";
                case "ecstasy_melt": case "trance": case "dazed": case "melt": case "ahegao": case "迷离恍惚": case "沉醉": return "ecstasy_melt";
                case "pain_reluctant": case "pain": case "rough": case "forced": case "痛苦": case "痛忍": return "pain_reluctant";
                case "pain_wince": case "crying_pain": case "wince": case "紧闭痛苦": case "流泪痛苦": return "pain_wince";
                case "dominant_smug": case "smug": case "teasing": case "dom": case "坏笑": case "支配": return "dominant_smug";
                case "climax": case "orgasm": case "高潮": return "climax";
                case "custom": return "custom";
                case "release": case "neutral": case "auto": case "off": case "none": case "释放": return "auto";
                default: return "auto";
            }
        }

        public static int ParseLookEyesPtn(string raw)
        {
            switch ((raw ?? "").Trim().ToLowerInvariant())
            {
                case "front": case "0": return 0;
                case "camera": case "cam": case "1": return 1;
                case "partner": case "target": case "2": return 2;
                case "away": case "avoid": case "3": return 3;
                default: return -1;
            }
        }
    }

    // 60Hz AI Facial Expression Director for BOTH Female (Female0/Female1) and Male (Male/Male1).
    // Hooks cleanly via Harmony Prefix on FaceBlendShape.LateUpdate() (AFTER HVoiceCtrl & HMotionEyeNeck,
    // BEFORE Eyebrow/Eyes/Mouth CalcBlend) plus early pre-LateUpdate sync for tearsLv visibility.
    internal sealed class DirectorExpression
    {
        private sealed class SlotChannel
        {
            public ExpressionSpec Spec = new ExpressionSpec();
            public bool WasActive;
            public float CurEyebrowOpen = 0.8f;
            public float CurEyeOpen = 0.8f;
            public float CurMouthOpen = 0.2f;
            public float CurBlush;
            public float LastAppliedBlush = -1f;
            public double LastStepTime;
        }

        private readonly Plugin host;
        private readonly SlotChannel[] channels = new SlotChannel[4]; // indexed by (int)ActorSlot

        // v0.2.2 External real-time lip-sync RMS pushed by KoiSync.VoiceDirector (~30-60Hz)
        private float externalLipTarget;
        private float externalLipSmooth;
        private float externalLipUntil;
        private bool externalLipWasDriving;
        private double lastLipStepTime;

        internal float ExternalLipRms { get { return Time.realtimeSinceStartup < externalLipUntil ? externalLipSmooth : 0f; } }
        internal bool ExternalLipActive { get { return Time.realtimeSinceStartup < externalLipUntil; } }

        internal void SetExternalLipRms(float rms, float ttlSec = 0.25f)
        {
            externalLipTarget = Mathf.Clamp01(rms);
            externalLipUntil = Time.realtimeSinceStartup + Mathf.Clamp(ttlSec, 0.05f, 2.0f);
        }

        internal void ClearExternalLip()
        {
            externalLipTarget = 0f;
            externalLipSmooth = 0f;
            externalLipUntil = 0f;
            if (externalLipWasDriving)
            {
                externalLipWasDriving = false;
                var f0 = host.Actor(ActorSlot.Female0);
                if (f0 != null && !channels[(int)ActorSlot.Female0].Spec.Active)
                {
                    try
                    {
                        f0.ChangeMouthFixed(false);
                        if (f0.mouthCtrl != null) f0.mouthCtrl.FixedRate = -1f;
                    }
                    catch { }
                }
            }
        }

        internal bool AnyActive
        {
            get
            {
                for (int i = 0; i < channels.Length; i++)
                    if (channels[i].Spec.Active) return true;
                return false;
            }
        }

        internal DirectorExpression(Plugin plugin)
        {
            host = plugin;
            for (int i = 0; i < channels.Length; i++) channels[i] = new SlotChannel();
        }

        internal ExpressionSpec GetSpec(ActorSlot slot)
        {
            return channels[(int)slot].Spec;
        }

        internal void SetTarget(string target, string preset, Action<ExpressionSpec> customize = null)
        {
            string t = (target ?? "both").Trim().ToLowerInvariant();
            string pKey = ExpressionSpec.NormalizePresetKey(preset);
            if (pKey == "auto" && customize == null)
            {
                ReleaseTarget(t);
                return;
            }

            bool doF0 = t == "both" || t == "all" || t == "female" || t == "female0" || t == "f0";
            bool doF1 = t == "both" || t == "all" || t == "female" || t == "female1" || t == "f1";
            bool doM0 = t == "both" || t == "all" || t == "male" || t == "male0" || t == "m0";
            bool doM1 = t == "both" || t == "all" || t == "male" || t == "male1" || t == "m1";

            if (doF0) ApplySpecToSlot(ActorSlot.Female0, pKey, false, customize);
            if (doF1) ApplySpecToSlot(ActorSlot.Female1, pKey, false, customize);
            if (doM0) ApplySpecToSlot(ActorSlot.Male, pKey, true, customize);
            if (doM1) ApplySpecToSlot(ActorSlot.Male1, pKey, true, customize);
        }

        private void ApplySpecToSlot(ActorSlot slot, string presetKey, bool isMale, Action<ExpressionSpec> customize)
        {
            var ch = channels[(int)slot];
            ExpressionSpec next = presetKey == "auto"
                ? (ch.Spec.Active ? ch.Spec.Copy() : ExpressionSpec.FromPreset("custom", isMale))
                : ExpressionSpec.FromPreset(presetKey, isMale);
            if (customize != null) customize(next);
            next.Active = true;
            next.EyebrowPtn = Mathf.Clamp(next.EyebrowPtn, 0, 16);
            next.EyesPtn = Mathf.Clamp(next.EyesPtn, 0, 38);
            next.MouthPtn = Mathf.Clamp(next.MouthPtn, 0, 42);
            next.EyebrowOpenMin = Mathf.Clamp01(next.EyebrowOpenMin);
            next.EyebrowOpenMax = Mathf.Clamp(next.EyebrowOpenMax, next.EyebrowOpenMin, 1f);
            next.EyeOpenMin = Mathf.Clamp01(next.EyeOpenMin);
            next.EyeOpenMax = Mathf.Clamp(next.EyeOpenMax, next.EyeOpenMin, 1f);
            next.MouthOpenMin = Mathf.Clamp01(next.MouthOpenMin);
            next.MouthOpenMax = Mathf.Clamp(next.MouthOpenMax, next.MouthOpenMin, 1f);
            next.Blush = Mathf.Clamp01(next.Blush);
            if (next.Tears > 3) next.Tears = 3;

            if (!ch.WasActive)
            {
                ch.CurEyebrowOpen = (next.EyebrowOpenMin + next.EyebrowOpenMax) * 0.5f;
                ch.CurEyeOpen = (next.EyeOpenMin + next.EyeOpenMax) * 0.5f;
                ch.CurMouthOpen = (next.MouthOpenMin + next.MouthOpenMax) * 0.5f;
                ch.CurBlush = next.Blush;
                ch.LastAppliedBlush = -1f;
                ch.LastStepTime = Time.realtimeSinceStartup;
            }
            ch.Spec = next;
            ch.WasActive = true;
        }

        internal void ReleaseTarget(string target)
        {
            string t = (target ?? "both").Trim().ToLowerInvariant();
            if (t == "both" || t == "all" || t == "female" || t == "female0" || t == "f0") ReleaseSlot(ActorSlot.Female0);
            if (t == "both" || t == "all" || t == "female" || t == "female1" || t == "f1") ReleaseSlot(ActorSlot.Female1);
            if (t == "both" || t == "all" || t == "male" || t == "male0" || t == "m0") ReleaseSlot(ActorSlot.Male);
            if (t == "both" || t == "all" || t == "male" || t == "male1" || t == "m1") ReleaseSlot(ActorSlot.Male1);
        }

        internal void ReleaseAll()
        {
            ReleaseSlot(ActorSlot.Female0);
            ReleaseSlot(ActorSlot.Female1);
            ReleaseSlot(ActorSlot.Male);
            ReleaseSlot(ActorSlot.Male1);
        }

        private void ReleaseSlot(ActorSlot slot)
        {
            var ch = channels[(int)slot];
            bool had = ch.WasActive || ch.Spec.Active;
            ch.Spec = new ExpressionSpec { Active = false, PresetName = "auto" };
            ch.WasActive = false;
            ch.LastAppliedBlush = -1f;
            if (!had) return;

            var actor = host.Actor(slot);
            if (actor == null) return;
            try
            {
                actor.ChangeMouthFixed(false);
                if (actor.mouthCtrl != null) actor.mouthCtrl.FixedRate = -1f;
                actor.ChangeEyesBlinkFlag(true);
                actor.HideEyeHighlight(false);
                actor.ChangeEyesShaking(false);
                actor.tearsLv = 0;
            }
            catch { }
        }

        // Sync visibility-dependent properties (tearsLv, highlight, shake) during Update()
        // right before ChaControl.LateUpdateForce() runs UpdateVisible().
        internal void PrepareInUpdate()
        {
            if (host.Scene == null || host.Scene.isEnd || !AnyActive) return;
            for (int i = 0; i < channels.Length; i++)
            {
                var ch = channels[i];
                if (!ch.Spec.Active) continue;
                var actor = host.Actor((ActorSlot)i);
                if (actor == null || !actor.gameObject.activeInHierarchy) continue;
                if (actor.tearsLv != ch.Spec.Tears) actor.tearsLv = ch.Spec.Tears;
            }
        }

        // Invoked from Harmony Prefix on FaceBlendShape.LateUpdate()
        // Executes AFTER HVoiceCtrl & HMotionEyeNeck and RIGHT BEFORE Eyebrow/Eyes/Mouth CalcBlend().
        internal void ApplyForFaceBlendShape(FaceBlendShape fbs)
        {
            if (fbs == null || host.Scene == null || host.Scene.isEnd) return;

            // Handle live external VoiceDirector lip-sync even when no full expression preset is active
            bool lipNow = Time.realtimeSinceStartup < externalLipUntil;
            if (!lipNow && externalLipWasDriving) ClearExternalLip();

            if (!AnyActive && !lipNow) return;

            ActorSlot matchedSlot;
            ChaControl actor = MatchActor(fbs, out matchedSlot);
            if (actor == null) return;

            var ch = channels[(int)matchedSlot];
            if (ch.Spec.Active)
            {
                ApplyToActor(actor, ch, matchedSlot == ActorSlot.Female0 && lipNow);
            }
            else if (matchedSlot == ActorSlot.Female0 && lipNow)
            {
                ApplyStandaloneLipSync(actor);
            }
        }

        private void ApplyStandaloneLipSync(ChaControl actor)
        {
            double now = Time.realtimeSinceStartup;
            float dt = lastLipStepTime > 0 ? Mathf.Clamp((float)(now - lastLipStepTime), 0.001f, 0.05f) : (1f / 60f);
            lastLipStepTime = now;
            float alpha = 1f - Mathf.Exp(-dt * 24f);
            externalLipSmooth = Mathf.Lerp(externalLipSmooth, externalLipTarget, alpha);
            float open = Mathf.Clamp(0.06f + externalLipSmooth * 0.90f, 0.04f, 0.98f);
            actor.ChangeMouthOpenMax(open);
            if (!actor.GetMouthFixed()) actor.ChangeMouthFixed(true);
            if (actor.mouthCtrl != null) actor.mouthCtrl.FixedRate = open;
            externalLipWasDriving = true;
        }

        private ChaControl MatchActor(FaceBlendShape fbs, out ActorSlot slot)
        {
            bool lipNow = Time.realtimeSinceStartup < externalLipUntil;
            for (int i = 0; i < 4; i++)
            {
                if (!channels[i].Spec.Active && !(i == (int)ActorSlot.Female0 && lipNow)) continue;
                var s = (ActorSlot)i;
                var a = host.Actor(s);
                if (a != null && a.fbsCtrl == fbs)
                {
                    slot = s;
                    return a;
                }
            }
            slot = ActorSlot.Female0;
            return null;
        }

        private void ApplyToActor(ChaControl actor, SlotChannel ch, bool blendExternalLip = false)
        {
            var spec = ch.Spec;
            double now = Time.realtimeSinceStartup;
            float dt = ch.LastStepTime > 0 ? Mathf.Clamp((float)(now - ch.LastStepTime), 0.001f, 0.05f) : (1f / 60f);
            ch.LastStepTime = now;

            // Compute smooth rhythmic drive in [0..1] synced to live insertion depth + gentle breathing wave
            float drive = 0.5f;
            if (spec.RhythmSync)
            {
                float depthDrive = ReadLiveDepthDrive();
                float breath = 0.5f + 0.5f * Mathf.Sin((float)(now * 2.0 * Math.PI * 0.32));
                drive = Mathf.Clamp01(depthDrive * 0.72f + breath * 0.28f);
            }

            float targetEyebrowOpen = Mathf.Lerp(spec.EyebrowOpenMax, spec.EyebrowOpenMin, drive);
            float targetEyeOpen = Mathf.Lerp(spec.EyeOpenMax, spec.EyeOpenMin, drive);
            float targetMouthOpen = Mathf.Lerp(spec.MouthOpenMin, spec.MouthOpenMax, drive);
            if (blendExternalLip)
            {
                float lipAlpha = 1f - Mathf.Exp(-dt * 24f);
                externalLipSmooth = Mathf.Lerp(externalLipSmooth, externalLipTarget, lipAlpha);
                float lipOpen = Mathf.Clamp(0.08f + externalLipSmooth * 0.90f, 0.06f, 0.98f);
                targetMouthOpen = Mathf.Lerp(targetMouthOpen, lipOpen, 0.85f);
                externalLipWasDriving = true;
            }
            float targetBlush = Mathf.Clamp01(spec.Blush + (spec.RhythmSync ? 0.10f * drive : 0f));

            float alpha = 1f - Mathf.Exp(-dt * 9.5f);
            ch.CurEyebrowOpen = Mathf.Lerp(ch.CurEyebrowOpen, targetEyebrowOpen, alpha);
            ch.CurEyeOpen = Mathf.Lerp(ch.CurEyeOpen, targetEyeOpen, alpha);
            ch.CurMouthOpen = Mathf.Lerp(ch.CurMouthOpen, targetMouthOpen, alpha);
            ch.CurBlush = Mathf.Lerp(ch.CurBlush, targetBlush, alpha);

            // Clamp pattern indices against this specific head's actual max pattern count
            // Note: ChangeEyesPtn uses cha_eyeset_00 list IDs (0..38 via GetEyesPtnNum), NOT raw FBSTarget count!
            int ebMax = actor.eyebrowCtrl != null ? Math.Max(1, actor.eyebrowCtrl.GetMaxPtn()) : 17;
            int eyeMax = Math.Max(39, actor.GetEyesPtnNum());
            int mMax = actor.mouthCtrl != null ? Math.Max(1, actor.mouthCtrl.GetMaxPtn()) : 43;

            int ebPtn = Mathf.Clamp(spec.EyebrowPtn, 0, ebMax - 1);
            int eyePtn = Mathf.Clamp(spec.EyesPtn, 0, eyeMax - 1);
            int mPtn = Mathf.Clamp(spec.MouthPtn, 0, mMax - 1);

            // Only call Change*Ptn when pattern actually differs so FBSBase.blendTimeCtrl finishes smoothly!
            if (actor.GetEyebrowPtn() != ebPtn) actor.ChangeEyebrowPtn(ebPtn, true);
            if (actor.GetEyesPtn() != eyePtn) actor.ChangeEyesPtn(eyePtn, true);
            if (actor.GetMouthPtn() != mPtn) actor.ChangeMouthPtn(mPtn, true);

            actor.ChangeEyebrowOpenMax(ch.CurEyebrowOpen);
            actor.ChangeEyesOpenMax(ch.CurEyeOpen);
            if (actor.GetEyesBlinkFlag() != spec.Blink) actor.ChangeEyesBlinkFlag(spec.Blink);

            // Drive mouth opening cleanly via FixedRate so rhythmic panting/smiling/grimacing renders even when silent
            actor.ChangeMouthOpenMax(ch.CurMouthOpen);
            if (!actor.GetMouthFixed()) actor.ChangeMouthFixed(true);
            if (actor.mouthCtrl != null) actor.mouthCtrl.FixedRate = ch.CurMouthOpen;

            if (actor.fileStatus == null || Math.Abs(actor.fileStatus.hohoAkaRate - ch.CurBlush) >= 0.008f || Math.Abs(ch.LastAppliedBlush - ch.CurBlush) >= 0.01f)
            {
                actor.ChangeHohoAkaRate(ch.CurBlush);
                ch.LastAppliedBlush = ch.CurBlush;
            }
            if (actor.tearsLv != spec.Tears) actor.tearsLv = spec.Tears;
            if (actor.fileStatus == null || actor.fileStatus.hideEyesHighlight != spec.HideHighlight)
                actor.HideEyeHighlight(spec.HideHighlight);
            if (actor.GetEyesShaking() != spec.EyeShake)
                actor.ChangeEyesShaking(spec.EyeShake);
            if (spec.LookEyesPtn >= 0 && actor.GetLookEyesPtn() != spec.LookEyesPtn)
                actor.ChangeLookEyesPtn(spec.LookEyesPtn);
        }

        private float ReadLiveDepthDrive()
        {
            var tf = host.ActiveTimelineFrame;
            if (host.Timeline.Active && tf.Active)
                return Mathf.Clamp01((float)tf.DepthNorm);
            if (host.Measured != null)
            {
                if (host.PhaseMap.Ready && host.PhaseMap.DepthSpanMeters > 0.004)
                    return Mathf.Clamp01((float)((host.Measured.TargetDepth - host.PhaseMap.MinDepthMeters) / host.PhaseMap.DepthSpanMeters));
                return Mathf.Clamp01((float)host.Measured.MaterialInsertion);
            }
            return 0.5f;
        }
    }
}
