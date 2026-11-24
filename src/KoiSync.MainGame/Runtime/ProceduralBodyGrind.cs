using System;
using System.Collections.Generic;
using System.Reflection;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.MainGame
{
    // Full-body 3D Dual-Actor Kinematic + Soft-Tissue DynamicBone Weight Coupler.
    // Runs AFTER Animator + ChaControl + MotionIK/FullBodyBipedIK and BEFORE Lookat_dan + KK_BetterPenetration
    // + KoiSync SolveFrame(), strictly gated to direct genital penetration (sonyu*) so bones & skinning weights
    // of BOTH characters stay anatomically natural, entry-pinned, and 1:1 unified with SR6 6-DOF math manifolds.
    internal sealed class ProceduralBodyGrind
    {
        private const BindingFlags DynFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private sealed class DynBoneSnapshot
        {
            public Component Comp;
            public FieldInfo ForceField;
            public FieldInfo ElastField;
            public Vector3 OrigForce;
            public float OrigElast;
            public bool IsChest;
            public bool IsVer02;
            public bool Modified;
        }

        private sealed class ActorRig
        {
            public int InstanceId;
            public Transform Hips;
            public Transform Waist01;
            public Transform Waist02;
            public Transform EntryBone;
            public Transform AnalEntryBone;
            public Transform Spine01;
            public Transform Spine02;
            public Transform Spine03;
            public Transform Neck;
            public Transform SiriL, SiriR;

            public Transform ThighL, LegL, FootL;
            public Transform ThighR, LegR, FootR;
            public Transform ArmL, ForearmL, HandL;
            public Transform ArmR, ForearmR, HandR;

            public readonly List<DynBoneSnapshot> SoftBones = new List<DynBoneSnapshot>();
            public bool Valid { get { return Hips != null; } }
        }

        private struct UnifiedCouplerInput
        {
            public bool Active;
            public bool FromMathLab;
            public double DepthNorm;
            public double RelLat, RelFwd, RelAxial;
            public double RelYaw, RelRoll, RelPitch;
            public double RelYawLag, RelRollLag, RelPitchLag;
            public double SpasmAmp, SpasmPhase;
            public ActiveMoverMode MoverMode;
            public double ExplicitFemaleShare; // <0 => auto; 0..1 => exact dual-actor conservation split
            public double SoftTissueWeight;
            public double BodyWeight;
            public double AccAxial, AccFwd, AccLat;
        }

        private readonly Plugin host;
        private readonly Dictionary<int, ActorRig> rigCache = new Dictionary<int, ActorRig>();
        private float currentEnvelope;
        private double lastUpdateTime;
        private int lastAppliedFrame = -1;
        private UnifiedCouplerInput heldInput;

        // Live telemetry for /state & visual E2E verification
        internal bool Active { get { return currentEnvelope > 0.005f; } }
        internal float Envelope { get { return currentEnvelope; } }
        internal string ResolvedMover { get; private set; }
        internal float FemaleShareApplied { get; private set; }
        internal float MaleShareApplied { get; private set; }
        internal int SoftTissueBonesSynced { get; private set; }
        internal float PrimaryHipDispMm { get; private set; }
        internal float PrimaryHipYawDeg { get; private set; }
        internal float PrimaryFootDriftMm { get; private set; }
        internal float PrimaryEntryDriftMm { get; private set; }
        internal Vector3 PrimaryHipWorld { get; private set; }
        internal Vector3 PrimaryWaistDeltaWorld { get; private set; }

        internal ProceduralBodyGrind(Plugin plugin)
        {
            host = plugin;
            ResolvedMover = "None";
        }

        internal void Reset()
        {
            RestoreAllSoftBones();
            currentEnvelope = 0f;
            lastUpdateTime = 0;
            lastAppliedFrame = -1;
            heldInput = new UnifiedCouplerInput { Active = false };
            ResolvedMover = "None";
            FemaleShareApplied = 0f;
            MaleShareApplied = 0f;
            SoftTissueBonesSynced = 0;
            PrimaryHipDispMm = 0f;
            PrimaryHipYawDeg = 0f;
            PrimaryFootDriftMm = 0f;
            PrimaryEntryDriftMm = 0f;
            PrimaryWaistDeltaWorld = Vector3.zero;
            rigCache.Clear();
        }

        internal void ClearCache()
        {
            RestoreAllSoftBones();
            rigCache.Clear();
        }

        private void RestoreAllSoftBones()
        {
            foreach (var kv in rigCache)
            {
                var rig = kv.Value;
                if (rig == null) continue;
                for (int i = 0; i < rig.SoftBones.Count; i++)
                {
                    var sb = rig.SoftBones[i];
                    if (sb == null || !sb.Modified || sb.Comp == null) continue;
                    try
                    {
                        if (sb.ForceField != null) sb.ForceField.SetValue(sb.Comp, sb.OrigForce);
                        if (sb.ElastField != null) sb.ElastField.SetValue(sb.Comp, sb.OrigElast);
                    }
                    catch { }
                    sb.Modified = false;
                }
            }
            SoftTissueBonesSynced = 0;
        }

        // Called from Harmony Prefix on Lookat_dan.LateUpdate() AND as fallback from Plugin.LateUpdate().
        // Guarded by Time.frameCount so it executes at most ONCE per rendered frame.
        internal void ApplyForCurrentFrame()
        {
            int frameNum = Time.frameCount;
            if (lastAppliedFrame == frameNum) return;
            lastAppliedFrame = frameNum;

            double now = Time.realtimeSinceStartup;
            float dt = lastUpdateTime > 0 ? Mathf.Clamp((float)(now - lastUpdateTime), 0.001f, 0.05f) : (1f / 60f);
            lastUpdateTime = now;

            // STRICT GUARDRAIL: Dual-actor 3D kinematic & soft-tissue weight coupling ONLY during direct penetration (sonyu*)!
            bool directInserted = host.Scene != null && !host.Scene.isEnd && host.IsDirectPenetrativeInserted();
            UnifiedCouplerInput inp = BuildUnifiedInput(directInserted);
            bool wantActive = directInserted && inp.Active;
            float targetEnv = wantActive ? Mathf.Clamp((float)Math.Max(inp.BodyWeight, inp.SpasmAmp * 0.65), 0f, 1.5f) : 0f;

            if (wantActive)
            {
                heldInput = inp;
                currentEnvelope = Mathf.MoveTowards(currentEnvelope, targetEnv, dt * 2.5f);
            }
            else if (!directInserted)
            {
                currentEnvelope = 0f;
            }
            else
            {
                currentEnvelope = Mathf.MoveTowards(currentEnvelope, 0f, dt * 3.0f);
            }

            if (currentEnvelope <= 0.004f || !directInserted)
            {
                RestoreAllSoftBones();
                ResolvedMover = "None";
                FemaleShareApplied = 0f;
                MaleShareApplied = 0f;
                PrimaryHipDispMm = 0f;
                PrimaryHipYawDeg = 0f;
                PrimaryFootDriftMm = 0f;
                PrimaryWaistDeltaWorld = Vector3.zero;
                return;
            }

            var female = host.Actor(ActorSlot.Female0);
            var male = host.Actor(ActorSlot.Male);
            if (female == null && male == null) return;

            float fWeight, mWeight;
            string moverLabel;
            ResolveMoverWeights(heldInput, female != null, male != null, out fWeight, out mWeight, out moverLabel);
            ResolvedMover = moverLabel;
            FemaleShareApplied = fWeight;
            MaleShareApplied = mWeight;

            Vector3 axisRight, axisInward, axisFwd, contactPivot;
            ResolveGrindBasis(female, male, fWeight >= mWeight, out axisRight, out axisInward, out axisFwd, out contactPivot);

            bool femalePrimary = fWeight >= mWeight;
            // Consistent opposite phase signs (+1 female / -1 male) prevent 180-deg jumps across fWeight == 0.50 (fixes P2-2)
            if (female != null && fWeight > 0.01f)
                ApplyActorCoupling(female, heldInput, currentEnvelope * fWeight, 1f, femalePrimary, axisRight, axisInward, axisFwd, contactPivot);
            if (male != null && mWeight > 0.01f)
                ApplyActorCoupling(male, heldInput, currentEnvelope * mWeight, -1f, !femalePrimary, axisRight, axisInward, axisFwd, contactPivot);
        }

        private UnifiedCouplerInput BuildUnifiedInput(bool directInserted)
        {
            if (!directInserted) return new UnifiedCouplerInput { Active = false };

            // Priority 1: Phase 3 SR6 6-DOF Mathematical Limit Engine
            SR6MathFrame mf = host.ActiveMathFrame;
            if (host.MathEngine.Active && mf.Active && mf.BodyScale > 0.001)
            {
                double accAx = (mf.AxisAcc != null && mf.AxisAcc.Length >= 6) ? mf.AxisAcc[0] : 0.0;
                double accFwd = (mf.AxisAcc != null && mf.AxisAcc.Length >= 6) ? mf.AxisAcc[1] : 0.0;
                double accLat = (mf.AxisAcc != null && mf.AxisAcc.Length >= 6) ? mf.AxisAcc[2] : 0.0;
                return new UnifiedCouplerInput
                {
                    Active = true,
                    FromMathLab = true,
                    DepthNorm = mf.DepthNorm,
                    RelLat = mf.RelLat,
                    RelFwd = mf.RelFwd,
                    RelAxial = mf.RelAxial,
                    RelYaw = mf.RelYaw,
                    RelRoll = mf.RelRoll,
                    RelPitch = mf.RelPitch,
                    RelYawLag = mf.RelYawLag,
                    RelRollLag = mf.RelRollLag,
                    RelPitchLag = mf.RelPitchLag,
                    SpasmAmp = 0.0,
                    SpasmPhase = 0.0,
                    MoverMode = ActiveMoverMode.Auto,
                    ExplicitFemaleShare = mf.FemaleWeightShare,
                    SoftTissueWeight = mf.SoftTissueWeight,
                    BodyWeight = mf.BodyScale * mf.BlendEnvelope,
                    AccAxial = accAx,
                    AccFwd = accFwd,
                    AccLat = accLat
                };
            }

            // Priority 2: Standard TimelineEngine variants
            TimelineFrame tf = host.ActiveTimelineFrame;
            if (host.Timeline.Active && tf.Active && (tf.BodyGrindWeight > 0.001 || tf.SpasmAmp > 0.001))
            {
                return new UnifiedCouplerInput
                {
                    Active = true,
                    FromMathLab = false,
                    DepthNorm = tf.DepthNorm,
                    RelLat = tf.GrindLat,
                    RelFwd = tf.GrindFwd,
                    RelAxial = tf.GrindAxial,
                    RelYaw = tf.GrindYaw,
                    RelRoll = tf.GrindRoll,
                    RelPitch = tf.GrindPitch,
                    RelYawLag = tf.GrindYawLag,
                    RelRollLag = tf.GrindRollLag,
                    RelPitchLag = tf.GrindPitchLag,
                    SpasmAmp = tf.SpasmAmp,
                    SpasmPhase = tf.SpasmPhase,
                    MoverMode = tf.MoverMode,
                    ExplicitFemaleShare = -1.0,
                    SoftTissueWeight = 0.75,
                    BodyWeight = tf.BodyGrindWeight,
                    AccAxial = tf.GrindAxial * 8.0,
                    AccFwd = tf.GrindFwd * 6.0,
                    AccLat = tf.GrindLat * 6.0
                };
            }

            return new UnifiedCouplerInput { Active = false };
        }

        private void ResolveMoverWeights(UnifiedCouplerInput inp, bool hasFemale, bool hasMale, out float femaleW, out float maleW, out string label)
        {
            if (!hasFemale && hasMale) { femaleW = 0f; maleW = 1f; label = "Male"; return; }
            if (hasFemale && !hasMale) { femaleW = 1f; maleW = 0f; label = "Female"; return; }

            // Exact dual-actor conservation split alpha_F + alpha_M = 1 when explicitly configured (e.g. SR6 Math Lab!)
            if (inp.ExplicitFemaleShare >= 0.0)
            {
                femaleW = Mathf.Clamp01((float)inp.ExplicitFemaleShare);
                maleW = 1f - femaleW;
                label = string.Format("Dual({0:0}%F/{1:0}%M)", femaleW * 100f, maleW * 100f);
                return;
            }

            var mode = inp.MoverMode;
            if (mode == ActiveMoverMode.Female) { femaleW = 0.86f; maleW = 0.14f; label = "Female"; return; }
            if (mode == ActiveMoverMode.Male) { femaleW = 0.16f; maleW = 0.84f; label = "Male"; return; }
            if (mode == ActiveMoverMode.Both) { femaleW = 0.55f; maleW = 0.55f; label = "Both"; return; }

            // Auto mode: use real measured 1-cycle pelvis excursion ratio from PhaseMap (+ native isFemaleInitiative fallback)
            double share;
            if (host.PhaseMap.MoverMeasured)
                share = host.PhaseMap.FemaleMoverShare;
            else
                share = (host.Flags != null && host.Flags.nowAnimationInfo != null && host.Flags.nowAnimationInfo.isFemaleInitiative) ? 0.82 : 0.22;

            // For SR6 Math Lab in Auto mode, lean slightly toward dual-actor load sharing so extreme 6D manifolds never over-twist one pelvis!
            if (inp.FromMathLab)
                share = Numbers.Clamp(0.5 + (share - 0.5) * 0.65, 0.22, 0.78);

            femaleW = Mathf.Clamp((float)share, 0.16f, 0.86f);
            maleW = Mathf.Clamp(1f - femaleW, 0.14f, 0.84f);
            label = femaleW >= 0.60f ? "Female(Auto)" : (femaleW <= 0.40f ? "Male(Auto)" : "Both(Auto)");
        }

        private void ResolveGrindBasis(ChaControl female, ChaControl male, bool femalePrimary,
            out Vector3 axisRight, out Vector3 axisInward, out Vector3 axisFwd, out Vector3 contactPivot)
        {
            var primaryRig = GetRig(femalePrimary ? female : male);
            Vector3 fallbackPivot = primaryRig != null && primaryRig.Hips != null ? primaryRig.Hips.position : Vector3.zero;

            if (host.Sample != null && host.Binding != null)
            {
                var rf = host.Sample.ReferenceFrame;
                axisInward = ToV3(rf.Y);
                axisRight = ToV3(rf.X);
                if (axisInward.sqrMagnitude > 1e-4f && axisRight.sqrMagnitude > 1e-4f)
                {
                    axisInward.Normalize();
                    axisRight = (axisRight - axisInward * Vector3.Dot(axisRight, axisInward));
                    if (axisRight.sqrMagnitude > 1e-4f)
                    {
                        axisRight.Normalize();
                        axisFwd = Vector3.Cross(axisRight, axisInward).normalized;
                        contactPivot = ToV3(host.Binding.Entry.Observed.Origin);
                        if (contactPivot.sqrMagnitude < 1e-6f) contactPivot = fallbackPivot;
                        return;
                    }
                }
            }

            if (primaryRig != null && primaryRig.Hips != null)
            {
                axisRight = primaryRig.Hips.right;
                axisInward = primaryRig.Hips.up;
                axisFwd = primaryRig.Hips.forward;
                contactPivot = primaryRig.Hips.position;
            }
            else
            {
                axisRight = Vector3.right;
                axisInward = Vector3.up;
                axisFwd = Vector3.forward;
                contactPivot = Vector3.zero;
            }
        }

        private Transform ActiveEntryBone(ActorRig rig, ChaControl actor)
        {
            if (rig == null) return null;
            if (actor != null && actor.sex == 1 && host.Flags != null && host.Flags.isAnalPlay && rig.AnalEntryBone != null)
                return rig.AnalEntryBone;
            return rig.EntryBone;
        }

        private void ApplyActorCoupling(ChaControl actor, UnifiedCouplerInput inp, float amp, float phaseSign, bool isPrimary,
            Vector3 axisRight, Vector3 axisInward, Vector3 axisFwd, Vector3 contactPivot)
        {
            var rig = GetRig(actor);
            if (rig == null || !rig.Valid) return;

            // 1. Snapshot natural pose limb & contact-entry anchors BEFORE displacing pelvis/waist/spine
            Transform entryBone = ActiveEntryBone(rig, actor);
            Vector3 hipBefore = rig.Hips.position;
            Vector3 entryBefore = entryBone != null ? entryBone.position : contactPivot;
            Vector3 footLPos = rig.FootL != null ? rig.FootL.position : Vector3.zero;
            Quaternion footLRot = rig.FootL != null ? rig.FootL.rotation : Quaternion.identity;
            Vector3 kneeLPos = rig.LegL != null ? rig.LegL.position : Vector3.zero;

            Vector3 footRPos = rig.FootR != null ? rig.FootR.position : Vector3.zero;
            Quaternion footRRot = rig.FootR != null ? rig.FootR.rotation : Quaternion.identity;
            Vector3 kneeRPos = rig.LegR != null ? rig.LegR.position : Vector3.zero;

            Vector3 handLPos = rig.HandL != null ? rig.HandL.position : Vector3.zero;
            Quaternion handLRot = rig.HandL != null ? rig.HandL.rotation : Quaternion.identity;
            Vector3 elbowLPos = rig.ForearmL != null ? rig.ForearmL.position : Vector3.zero;

            Vector3 handRPos = rig.HandR != null ? rig.HandR.position : Vector3.zero;
            Quaternion handRRot = rig.HandR != null ? rig.HandR.rotation : Quaternion.identity;
            Vector3 elbowRPos = rig.ForearmR != null ? rig.ForearmR.position : Vector3.zero;

            // 2. Anatomical safety nonlinear remap (protects waist/thigh skinning weights even under +-105 deg SR6 R0!)
            float s = amp * phaseSign;
            float spasm = Mathf.Clamp((float)inp.SpasmAmp, 0f, 1.5f) * (actor.sex == 1 ? 1.0f : 0.45f);
            float sp1 = Mathf.Sin((float)inp.SpasmPhase);
            float sp2 = Mathf.Cos((float)(inp.SpasmPhase * 1.37));

            float yawDeg = Mathf.Clamp((float)(Math.Tanh(inp.RelYaw * 1.35) * 19.5 * s) + spasm * 4.8f * sp1, -22f, 22f);
            float rollDeg = Mathf.Clamp((float)(Math.Tanh(inp.RelRoll * 1.20) * 14.0 * s) + spasm * 4.2f * sp2, -17f, 17f);
            float pitchDeg = Mathf.Clamp((float)(Math.Tanh(inp.RelPitch * 1.20) * 12.5 * s) + spasm * (5.5f + 3.2f * sp1), -16f, 16f);

            // 3-tier pelvic-lumbar weight distribution (hips 40% + waist01 40% + waist02 20%) -> silky skinning weights!
            bool hasW2 = rig.Waist02 != null;
            float wHips = hasW2 ? 0.40f : 0.45f;
            float wW1 = hasW2 ? 0.40f : 0.55f;
            float wW2 = hasW2 ? 0.20f : 0.0f;

            Quaternion qHips =
                Quaternion.AngleAxis(yawDeg * wHips, axisInward) *
                Quaternion.AngleAxis(rollDeg * wHips, axisFwd) *
                Quaternion.AngleAxis(pitchDeg * wHips, axisRight);

            Quaternion qWaist1 =
                Quaternion.AngleAxis(yawDeg * wW1, axisInward) *
                Quaternion.AngleAxis(rollDeg * wW1, axisFwd) *
                Quaternion.AngleAxis(pitchDeg * wW1, axisRight);

            // Pivot 100% around the actor's live contact entry bone
            rig.Hips.position = entryBefore + qHips * (hipBefore - entryBefore);
            rig.Hips.rotation = qHips * rig.Hips.rotation;

            if (rig.Waist01 != null)
                rig.Waist01.rotation = qWaist1 * rig.Waist01.rotation;
            if (hasW2)
            {
                Quaternion qWaist2 =
                    Quaternion.AngleAxis(yawDeg * wW2, axisInward) *
                    Quaternion.AngleAxis(rollDeg * wW2, axisFwd) *
                    Quaternion.AngleAxis(pitchDeg * wW2, axisRight);
                rig.Waist02.rotation = qWaist2 * rig.Waist02.rotation;
            }

            // Exact entry-pinned contact lock: cancels sideways drift so Lookat_dan / BetterPenetration / kPlug stay dead-centered!
            float giveLimit = actor.sex == 0 ? 0.0085f : 0.0065f;
            float latGive = Mathf.Clamp((float)inp.RelLat * giveLimit * s, -0.010f, 0.010f);
            float fwdGive = Mathf.Clamp((float)inp.RelFwd * giveLimit * s, -0.010f, 0.010f);
            float axGive = Mathf.Clamp((float)inp.RelAxial * 0.0080f * s, -0.012f, 0.012f);
            Vector3 desiredEntryOffset = axisRight * latGive + axisFwd * fwdGive + axisInward * axGive;

            if (entryBone != null)
            {
                Vector3 actualEntryDrift = entryBone.position - entryBefore;
                rig.Hips.position += (desiredEntryOffset - actualEntryDrift);
            }
            else
            {
                rig.Hips.position += desiredEntryOffset;
            }

            // 3. Upward spine counter-wave radiation (~0.10s phase-lagged S-curve)
            float yawLag = Mathf.Clamp((float)(Math.Tanh(inp.RelYawLag * 1.35) * 19.0 * s), -20f, 20f);
            float rollLag = Mathf.Clamp((float)(Math.Tanh(inp.RelRollLag * 1.20) * 13.5 * s), -16f, 16f);
            float pitchLag = Mathf.Clamp((float)(Math.Tanh(inp.RelPitchLag * 1.20) * 12.0 * s), -14f, 14f);

            ApplySpineCounter(rig.Spine01, axisInward, axisFwd, axisRight, yawLag, rollLag, pitchLag, -0.24f);
            ApplySpineCounter(rig.Spine02, axisInward, axisFwd, axisRight, yawLag, rollLag, pitchLag, -0.22f);
            ApplySpineCounter(rig.Spine03, axisInward, axisFwd, axisRight, yawLag, rollLag, pitchLag, -0.16f);
            ApplySpineCounter(rig.Neck, axisInward, axisFwd, axisRight, yawLag, rollLag, pitchLag, -0.14f);

            // 3b. Additive climax body-spasm spine arch + shudder
            if (spasm > 0.02f)
            {
                float archDeg = spasm * (5.0f + 2.8f * sp1);
                float shudderDeg = spasm * 3.0f * sp2;
                ApplySpineCounter(rig.Spine01, axisInward, axisFwd, axisRight, shudderDeg, shudderDeg * 0.6f, -archDeg * 0.45f, 1f);
                ApplySpineCounter(rig.Spine02, axisInward, axisFwd, axisRight, -shudderDeg * 0.8f, shudderDeg * 0.5f, -archDeg * 0.65f, 1f);
                ApplySpineCounter(rig.Spine03, axisInward, axisFwd, axisRight, shudderDeg * 0.7f, -shudderDeg * 0.4f, -archDeg * 0.55f, 1f);
                ApplySpineCounter(rig.Neck, axisInward, axisFwd, axisRight, -shudderDeg * 0.5f, shudderDeg * 0.5f, -archDeg * 0.45f, 1f);
            }

            // 3c. Soft-Tissue Weight & Glute Compression Sync (female chest/butt DynamicBone inertia + cf_j_siri_L/R squash!)
            float softW = Mathf.Clamp((float)inp.SoftTissueWeight, 0f, 1.5f) * Mathf.Clamp01(currentEnvelope);
            if (actor.sex == 1 && softW > 0.01f)
            {
                ApplySoftTissueWeights(rig, inp, softW, axisRight, axisInward, axisFwd);
            }

            // 4. Dynamic-rigidity analytical 2-bone soft-IK pinning for legs & arms (zero knee/elbow inversion!)
            float dispMag = (rig.Hips.position - hipBefore).magnitude;
            float dynFlex = Mathf.Clamp01(dispMag / 0.045f);
            float legPin = Mathf.Lerp(isPrimary ? 0.90f : 0.82f, 0.74f, dynFlex);
            float armPin = Mathf.Lerp(isPrimary ? 0.72f : 0.62f, 0.52f, dynFlex);

            float driftSum = 0f;
            int driftCount = 0;
            if (rig.ThighL != null && rig.LegL != null && rig.FootL != null)
            {
                SolveTwoBoneIK(rig.ThighL, rig.LegL, rig.FootL, footLPos, kneeLPos, footLRot, legPin, 0.72f);
                driftSum += Vector3.Distance(rig.FootL.position, footLPos) * 1000f;
                driftCount++;
            }
            if (rig.ThighR != null && rig.LegR != null && rig.FootR != null)
            {
                SolveTwoBoneIK(rig.ThighR, rig.LegR, rig.FootR, footRPos, kneeRPos, footRRot, legPin, 0.72f);
                driftSum += Vector3.Distance(rig.FootR.position, footRPos) * 1000f;
                driftCount++;
            }
            if (rig.ArmL != null && rig.ForearmL != null && rig.HandL != null)
                SolveTwoBoneIK(rig.ArmL, rig.ForearmL, rig.HandL, handLPos, elbowLPos, handLRot, armPin, 0.55f);
            if (rig.ArmR != null && rig.ForearmR != null && rig.HandR != null)
                SolveTwoBoneIK(rig.ArmR, rig.ForearmR, rig.HandR, handRPos, elbowRPos, handRRot, armPin, 0.55f);

            if (isPrimary)
            {
                Vector3 delta = rig.Hips.position - hipBefore;
                PrimaryWaistDeltaWorld = delta;
                PrimaryHipWorld = rig.Hips.position;
                PrimaryHipDispMm = delta.magnitude * 1000f;
                PrimaryHipYawDeg = yawDeg;
                PrimaryFootDriftMm = driftCount > 0 ? driftSum / driftCount : -1f;
                PrimaryEntryDriftMm = entryBone != null ? Vector3.Distance(entryBone.position, entryBefore) * 1000f : 0f;
            }
        }

        private void ApplySoftTissueWeights(ActorRig rig, UnifiedCouplerInput inp, float softW, Vector3 axisRight, Vector3 axisInward, Vector3 axisFwd)
        {
            // A) Subtle glute cheek elastic spread during deep-stroke compression (depthNorm > 0.72)
            float deepPress = Mathf.Clamp01((float)((inp.DepthNorm - 0.72) / 0.28)) * softW;
            if (deepPress > 0.01f)
            {
                float spread = deepPress * 0.0038f;
                float yieldBack = deepPress * 0.0025f;
                if (rig.SiriL != null) rig.SiriL.position += (-axisRight * spread - axisInward * yieldBack);
                if (rig.SiriR != null) rig.SiriR.position += (axisRight * spread - axisInward * yieldBack);
            }

            // B) Newton's-law DynamicBone chest/butt inertial force + subtle elasticity weight coupling!
            if (rig.SoftBones.Count == 0) return;
            Vector3 inertiaWorld =
                (-axisInward * (float)inp.AccAxial * 0.00055f) +
                (-axisFwd * (float)inp.AccFwd * 0.00045f) +
                (-axisRight * (float)inp.AccLat * 0.00045f);
            float maxF = 0.016f * softW;
            if (inertiaWorld.magnitude > maxF && inertiaWorld.sqrMagnitude > 1e-8f)
                inertiaWorld = inertiaWorld.normalized * maxF;

            int synced = 0;
            for (int i = 0; i < rig.SoftBones.Count; i++)
            {
                var sb = rig.SoftBones[i];
                if (sb == null || sb.Comp == null || sb.ForceField == null) continue;
                try
                {
                    float gain = sb.IsChest ? 1.0f : 0.82f;
                    sb.ForceField.SetValue(sb.Comp, sb.OrigForce + inertiaWorld * gain);
                    if (!sb.IsVer02 && sb.ElastField != null)
                    {
                        float targetElast = Mathf.Clamp(sb.OrigElast * (1f + 0.12f * Mathf.Clamp01(softW)), 0.02f, 0.38f);
                        sb.ElastField.SetValue(sb.Comp, targetElast);
                    }
                    sb.Modified = true;
                    synced++;
                }
                catch { }
            }
            SoftTissueBonesSynced = synced;
        }

        private static void ApplySpineCounter(Transform bone, Vector3 axisInward, Vector3 axisFwd, Vector3 axisRight,
            float yawDeg, float rollDeg, float pitchDeg, float factor)
        {
            if (bone == null) return;
            Quaternion q =
                Quaternion.AngleAxis(yawDeg * factor, axisInward) *
                Quaternion.AngleAxis(rollDeg * factor, axisFwd) *
                Quaternion.AngleAxis(pitchDeg * factor, axisRight);
            bone.rotation = q * bone.rotation;
        }

        // Zero-GC analytical Law-of-Cosines 2-bone IK using minimal FromToRotation swing deltas
        // so Koikatsu's native bone roll/twist axes are 100% preserved with zero knee/elbow flipping.
        private static void SolveTwoBoneIK(Transform upper, Transform mid, Transform end,
            Vector3 anchorEndPos, Vector3 anchorMidPos, Quaternion anchorEndRot, float posWeight, float rotWeight)
        {
            Vector3 a = upper.position;
            Vector3 b = mid.position;
            Vector3 c = end.position;

            float lenAB = Vector3.Distance(a, b);
            float lenBC = Vector3.Distance(b, c);
            if (lenAB < 0.01f || lenBC < 0.01f) return;

            Vector3 targetPos = Vector3.Lerp(c, anchorEndPos, posWeight);
            Vector3 toTarget = targetPos - a;
            float rawDist = toTarget.magnitude;
            if (rawDist < 1e-4f) return;

            float minReach = Mathf.Abs(lenAB - lenBC) + 0.005f;
            float maxReach = (lenAB + lenBC) * 0.985f;
            float dist = Mathf.Clamp(rawDist, minReach, maxReach);
            Vector3 dirAT = toTarget / rawDist;

            float cosAlpha = Mathf.Clamp((lenAB * lenAB + dist * dist - lenBC * lenBC) / (2f * lenAB * dist), -1f, 1f);
            float alpha = Mathf.Acos(cosAlpha);

            Vector3 poleBlend = Vector3.Lerp(b, anchorMidPos, 0.65f) - a;
            Vector3 bendAxis = poleBlend - dirAT * Vector3.Dot(poleBlend, dirAT);
            if (bendAxis.sqrMagnitude < 1e-6f)
            {
                Vector3 fallbackPole = b - a;
                bendAxis = fallbackPole - dirAT * Vector3.Dot(fallbackPole, dirAT);
                if (bendAxis.sqrMagnitude < 1e-6f) return;
            }
            bendAxis.Normalize();

            Vector3 desiredB = a + (dirAT * Mathf.Cos(alpha) + bendAxis * Mathf.Sin(alpha)) * lenAB;
            Vector3 curUpperDir = (b - a).normalized;
            Vector3 wantUpperDir = (desiredB - a).normalized;
            if (curUpperDir.sqrMagnitude > 1e-5f && wantUpperDir.sqrMagnitude > 1e-5f)
                upper.rotation = Quaternion.FromToRotation(curUpperDir, wantUpperDir) * upper.rotation;

            Vector3 bNow = mid.position;
            Vector3 cNow = end.position;
            Vector3 clampedTarget = a + dirAT * dist;
            Vector3 curLowerDir = (cNow - bNow).normalized;
            Vector3 wantLowerDir = (clampedTarget - bNow).normalized;
            if (curLowerDir.sqrMagnitude > 1e-5f && wantLowerDir.sqrMagnitude > 1e-5f)
                mid.rotation = Quaternion.FromToRotation(curLowerDir, wantLowerDir) * mid.rotation;

            if (rotWeight > 0.001f)
                end.rotation = Quaternion.Slerp(end.rotation, anchorEndRot, rotWeight * posWeight);
        }

        private ActorRig GetRig(ChaControl actor)
        {
            if (actor == null) return null;
            int id = actor.GetInstanceID();
            ActorRig rig;
            if (rigCache.TryGetValue(id, out rig) && rig.Valid && rig.Hips != null)
                return rig;

            var bones = GameBinding.Bones(actor.transform);
            rig = new ActorRig
            {
                InstanceId = id,
                Hips = GameBinding.Get(bones, "cf_j_hips", "cm_J_hips"),
                Waist01 = GameBinding.Get(bones, "cf_j_waist01", "cm_J_waist01"),
                Waist02 = GameBinding.Get(bones, "cf_j_waist02", "cm_J_waist02"),
                EntryBone = actor.sex == 0
                    ? GameBinding.Get(bones, "cm_J_dan100_00", "cm_J_dan101_00", "cf_j_waist02")
                    : GameBinding.Get(bones, "cf_J_Vagina_root", "k_f_kokan_00", "cf_j_ana", "cf_J_Ana_Root", "cf_j_waist02"),
                AnalEntryBone = actor.sex == 1
                    ? GameBinding.Get(bones, "cf_j_ana", "cf_J_Ana_Root", "cf_J_Ana")
                    : null,
                Spine01 = GameBinding.Get(bones, "cf_j_spine01", "cm_J_spine01"),
                Spine02 = GameBinding.Get(bones, "cf_j_spine02", "cm_J_spine02"),
                Spine03 = GameBinding.Get(bones, "cf_j_spine03", "cm_J_spine03"),
                Neck = GameBinding.Get(bones, "cf_j_neck", "cm_J_neck"),
                SiriL = GameBinding.Get(bones, "cf_j_siri_L"),
                SiriR = GameBinding.Get(bones, "cf_j_siri_R"),
                ThighL = GameBinding.Get(bones, "cf_j_thigh00_L", "cm_J_thigh00_L"),
                LegL = GameBinding.Get(bones, "cf_j_leg01_L", "cm_J_leg01_L"),
                FootL = GameBinding.Get(bones, "cf_j_foot_L", "cm_J_foot_L", "cf_j_leg03_L", "cm_J_leg03_L", "cf_j_foot01_L", "cm_J_foot01_L"),
                ThighR = GameBinding.Get(bones, "cf_j_thigh00_R", "cm_J_thigh00_R"),
                LegR = GameBinding.Get(bones, "cf_j_leg01_R", "cm_J_leg01_R"),
                FootR = GameBinding.Get(bones, "cf_j_foot_R", "cm_J_foot_R", "cf_j_leg03_R", "cm_J_leg03_R", "cf_j_foot01_R", "cm_J_foot01_R"),
                ArmL = GameBinding.Get(bones, "cf_j_arm00_L", "cm_J_arm00_L"),
                ForearmL = GameBinding.Get(bones, "cf_j_forearm01_L", "cm_J_forearm01_L"),
                HandL = GameBinding.Get(bones, "cf_j_hand_L", "cm_J_hand_L"),
                ArmR = GameBinding.Get(bones, "cf_j_arm00_R", "cm_J_arm00_R"),
                ForearmR = GameBinding.Get(bones, "cf_j_forearm01_R", "cm_J_forearm01_R"),
                HandR = GameBinding.Get(bones, "cf_j_hand_R", "cm_J_hand_R")
            };

            if (actor.sex == 1) DiscoverSoftDynamicBones(actor, rig);
            rigCache[id] = rig;
            return rig;
        }

        private static void DiscoverSoftDynamicBones(ChaControl actor, ActorRig rig)
        {
            try
            {
                var comps = actor.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < comps.Length; i++)
                {
                    var c = comps[i];
                    if (c == null) continue;
                    var ct = c.GetType();
                    string tn = ct.Name;
                    if (!tn.StartsWith("DynamicBone", StringComparison.Ordinal)) continue;

                    // Support BOTH Koikatsu native DynamicBone_Ver02 (Root / Force / Comment) AND standard DynamicBone (m_Root / m_Force / m_Elasticity)!
                    bool isVer02 = tn.IndexOf("Ver02", StringComparison.OrdinalIgnoreCase) >= 0;
                    var rootField = ct.GetField(isVer02 ? "Root" : "m_Root", DynFlags) ?? ct.GetField("Root", DynFlags) ?? ct.GetField("m_Root", DynFlags);
                    var forceField = ct.GetField(isVer02 ? "Force" : "m_Force", DynFlags) ?? ct.GetField("Force", DynFlags) ?? ct.GetField("m_Force", DynFlags);
                    if (forceField == null || forceField.FieldType != typeof(Vector3)) continue;

                    var rootTr = rootField != null ? rootField.GetValue(c) as Transform : null;
                    string rn = rootTr != null ? rootTr.name.ToLowerInvariant() : "";
                    var commentField = ct.GetField("Comment", DynFlags);
                    string cm = commentField != null ? (Convert.ToString(commentField.GetValue(c)) ?? "").ToLowerInvariant() : "";
                    string goName = c.gameObject != null ? c.gameObject.name.ToLowerInvariant() : "";
                    string hay = rn + " " + cm + " " + goName;

                    bool isChest = hay.Contains("bust") || hay.Contains("mune") || hay.Contains("胸");
                    bool isButt = hay.Contains("siri") || hay.Contains("butt") || hay.Contains("臀");
                    if (!isChest && !isButt) continue;

                    var elastField = isVer02 ? null : ct.GetField("m_Elasticity", DynFlags);
                    var origForce = (Vector3)forceField.GetValue(c);
                    float origElast = elastField != null ? Convert.ToSingle(elastField.GetValue(c)) : 0.1f;
                    rig.SoftBones.Add(new DynBoneSnapshot
                    {
                        Comp = c,
                        ForceField = forceField,
                        ElastField = elastField,
                        OrigForce = origForce,
                        OrigElast = origElast,
                        IsChest = isChest,
                        IsVer02 = isVer02,
                        Modified = false
                    });
                }
            }
            catch { }
        }

        private static Vector3 ToV3(MotionVector v)
        {
            return new Vector3((float)v.X, (float)v.Y, (float)v.Z);
        }
    }
}
