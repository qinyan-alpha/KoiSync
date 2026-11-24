using System;
using System.Collections.Generic;
using System.Reflection;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.Studio
{
    // Studio Dual-Actor 3D Kinematic + Soft-Tissue DynamicBone Weight Reverse-Coupler.
    // Ported from MainGame's ProceduralBodyGrind with strict per-frame end-of-frame restoration,
    // finite-math guardrails, and max-displacement clamping so characters NEVER drift, clip, or vanish.
    internal sealed class StudioBodyGrind
    {
        private const BindingFlags DynFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private struct BoneSnapshot
        {
            public Transform Bone;
            public Vector3 LocalPos;
            public Quaternion LocalRot;
        }

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
            public Transform Hips, Waist01, Waist02;
            public Transform VaginaEntry, AnalEntry, MouthEntry;
            public Transform Spine01, Spine02, Spine03, Neck;
            public Transform SiriL, SiriR;
            public Transform ThighL, LegL, FootL;
            public Transform ThighR, LegR, FootR;
            public Transform ArmL, ForearmL, HandL;
            public Transform ArmR, ForearmR, HandR;
            public readonly List<DynBoneSnapshot> SoftBones = new List<DynBoneSnapshot>();
            public bool Valid { get { return Hips != null; } }
        }

        private readonly Dictionary<int, ActorRig> rigCache = new Dictionary<int, ActorRig>();
        private readonly List<BoneSnapshot> touchedBones = new List<BoneSnapshot>(48);
        private float currentEnvelope;

        internal bool Active { get { return currentEnvelope > 0.005f; } }
        internal string ResolvedMover { get; private set; }
        internal float FemaleShareApplied { get; private set; }
        internal float MaleShareApplied { get; private set; }
        internal int SoftTissueBonesSynced { get; private set; }
        internal float PrimaryEntryDriftMm { get; private set; }
        internal float PrimaryHipDispMm { get; private set; }

        internal StudioBodyGrind()
        {
            ResolvedMover = "待机";
        }

        // Restores exact pre-grind local bone transforms. Called BOTH at WaitForEndOfFrame
        // (immediately after rendering, before next frame's FixedUpdate/Update/Timeline/IK/DynamicBone!)
        // AND as a safety guard at start of Update(), SaveChapters(), and scene change.
        internal void RestorePreviousFrame()
        {
            for (int i = touchedBones.Count - 1; i >= 0; i--)
            {
                var snap = touchedBones[i];
                if (snap.Bone == null) continue;
                if (IsFinite(snap.LocalPos)) snap.Bone.localPosition = snap.LocalPos;
                if (IsFinite(snap.LocalRot)) snap.Bone.localRotation = snap.LocalRot;
            }
            touchedBones.Clear();
        }

        private void RollbackFrom(int startIndex)
        {
            for (int i = touchedBones.Count - 1; i >= startIndex; i--)
            {
                var snap = touchedBones[i];
                if (snap.Bone == null) continue;
                if (IsFinite(snap.LocalPos)) snap.Bone.localPosition = snap.LocalPos;
                if (IsFinite(snap.LocalRot)) snap.Bone.localRotation = snap.LocalRot;
            }
            if (startIndex >= 0 && startIndex <= touchedBones.Count)
                touchedBones.RemoveRange(startIndex, touchedBones.Count - startIndex);
        }

        internal void Reset()
        {
            RestorePreviousFrame();
            RestoreAllSoftBones();
            currentEnvelope = 0f;
            ResolvedMover = "待机";
            FemaleShareApplied = 0f;
            MaleShareApplied = 0f;
            SoftTissueBonesSynced = 0;
            PrimaryEntryDriftMm = 0f;
            PrimaryHipDispMm = 0f;
            rigCache.Clear();
        }

        internal void ApplyForFrame(ChaControl target, ChaControl source, OpeningKind opening, NativeMotionSample sample, StudioGrindFrame gf, ChapterGrindOptions opt, float dt)
        {
            RestorePreviousFrame();

            bool want = gf.Active && opt != null && opt.LinkSkeleton && StudioGrindEngine.IsDirectInsertionOpening((int)opening) && (target != null || source != null);
            float targetEnv = want ? Mathf.Clamp((float)gf.EffectiveWeight, 0f, 1.25f) : 0f;
            currentEnvelope = Mathf.MoveTowards(currentEnvelope, targetEnv, Mathf.Clamp(dt, 0.004f, 0.05f) * (want ? 4.0f : 5.0f));

            if (currentEnvelope <= 0.004f || !want)
            {
                RestoreAllSoftBones();
                ResolvedMover = "待机";
                FemaleShareApplied = 0f;
                MaleShareApplied = 0f;
                PrimaryEntryDriftMm = 0f;
                PrimaryHipDispMm = 0f;
                return;
            }

            float fWeight, mWeight;
            ResolveMoverWeights(opt.MoverMode, opening, target != null, source != null, out fWeight, out mWeight);
            FemaleShareApplied = fWeight;
            MaleShareApplied = mWeight;

            Vector3 axisRight, axisInward, axisFwd, contactPivot;
            ResolveBasis(target, source, sample, out axisRight, out axisInward, out axisFwd, out contactPivot);
            if (!IsFinite(axisRight) || !IsFinite(axisInward) || !IsFinite(axisFwd) || !IsFinite(contactPivot))
                return;

            bool targetPrimary = fWeight >= mWeight;
            if (target != null && fWeight > 0.01f)
                ApplyActor(target, opening, gf, opt.SoftTissue, currentEnvelope * fWeight, 1f, targetPrimary, axisRight, axisInward, axisFwd, contactPivot);
            if (source != null && mWeight > 0.01f)
                ApplyActor(source, opening, gf, false, currentEnvelope * mWeight, -1f, !targetPrimary, axisRight, axisInward, axisFwd, contactPivot);
            if (!opt.SoftTissue)
                RestoreAllSoftBones();
        }

        private void ResolveMoverWeights(ActiveMoverMode mode, OpeningKind opening, bool hasTarget, bool hasSource, out float targetW, out float sourceW)
        {
            if (hasTarget && !hasSource) { targetW = 1f; sourceW = 0f; ResolvedMover = "女方 100%"; return; }
            if (!hasTarget && hasSource) { targetW = 0f; sourceW = 1f; ResolvedMover = "男方 100%"; return; }
            if (mode == ActiveMoverMode.Female) { targetW = 0.86f; sourceW = 0.14f; ResolvedMover = "女 86% / 男 14%"; return; }
            if (mode == ActiveMoverMode.Male) { targetW = 0.16f; sourceW = 0.84f; ResolvedMover = "女 16% / 男 84%"; return; }
            if (mode == ActiveMoverMode.Both) { targetW = 0.55f; sourceW = 0.45f; ResolvedMover = "双向 55% / 45%"; return; }
            if (opening == OpeningKind.Oral) { targetW = 0.72f; sourceW = 0.28f; ResolvedMover = "自动(口部 72/28)"; }
            else { targetW = 0.70f; sourceW = 0.30f; ResolvedMover = "自动(女70/男30)"; }
        }

        private void ResolveBasis(ChaControl target, ChaControl source, NativeMotionSample sample,
            out Vector3 axisRight, out Vector3 axisInward, out Vector3 axisFwd, out Vector3 contactPivot)
        {
            if (sample != null)
            {
                var rf = sample.ReferenceFrame;
                axisInward = new Vector3((float)rf.Y.X, (float)rf.Y.Y, (float)rf.Y.Z);
                axisRight = new Vector3((float)rf.X.X, (float)rf.X.Y, (float)rf.X.Z);
                if (IsFinite(axisInward) && IsFinite(axisRight) && axisInward.sqrMagnitude > 1e-4f && axisRight.sqrMagnitude > 1e-4f)
                {
                    axisInward.Normalize();
                    axisRight = axisRight - axisInward * Vector3.Dot(axisRight, axisInward);
                    if (axisRight.sqrMagnitude > 1e-4f)
                    {
                        axisRight.Normalize();
                        axisFwd = Vector3.Cross(axisRight, axisInward);
                        if (axisFwd.sqrMagnitude > 1e-4f)
                        {
                            axisFwd.Normalize();
                            contactPivot = new Vector3((float)sample.Target.X, (float)sample.Target.Y, (float)sample.Target.Z);
                            if (IsFinite(contactPivot)) return;
                        }
                    }
                }
            }
            var rig = GetRig(target ?? source);
            if (rig != null && rig.Hips != null && IsFinite(rig.Hips.position))
            {
                axisRight = rig.Hips.right.normalized;
                axisInward = rig.Hips.up.normalized;
                axisFwd = rig.Hips.forward.normalized;
                contactPivot = rig.Hips.position;
            }
            else
            {
                axisRight = Vector3.right; axisInward = Vector3.up; axisFwd = Vector3.forward; contactPivot = Vector3.zero;
            }
        }

        private void RecordBone(Transform b)
        {
            if (b == null) return;
            for (int i = 0; i < touchedBones.Count; i++)
                if (touchedBones[i].Bone == b) return;
            touchedBones.Add(new BoneSnapshot { Bone = b, LocalPos = b.localPosition, LocalRot = b.localRotation });
        }

        private void ApplyActor(ChaControl actor, OpeningKind opening, StudioGrindFrame gf, bool syncSoftTissue, float amp, float phaseSign, bool isPrimary,
            Vector3 axisRight, Vector3 axisInward, Vector3 axisFwd, Vector3 contactPivot)
        {
            var rig = GetRig(actor);
            if (rig == null || !rig.Valid || !IsFinite(rig.Hips.position)) return;

            Transform rawEntry = opening == OpeningKind.Anal ? (rig.AnalEntry ?? rig.VaginaEntry)
                : opening == OpeningKind.Oral ? rig.MouthEntry
                : rig.VaginaEntry;

            // STRICT GUARDRAIL: Only pivot/pin around entryBone if it is genuinely inside rig.Hips
            // and anatomically close (< 45 cm) — NEVER pivot around a detached bone or world origin!
            Vector3 hipBefore = rig.Hips.position;
            Transform entryBone = (rawEntry != null && rawEntry.IsChildOf(rig.Hips) && IsFinite(rawEntry.position) && Vector3.Distance(rawEntry.position, hipBefore) < 0.45f)
                ? rawEntry
                : null;
            Vector3 entryBefore = entryBone != null ? entryBone.position : hipBefore;

            int snapStart = touchedBones.Count;
            RecordBone(rig.Hips); RecordBone(rig.Waist01); RecordBone(rig.Waist02);
            RecordBone(rig.Spine01); RecordBone(rig.Spine02); RecordBone(rig.Spine03); RecordBone(rig.Neck);
            RecordBone(rig.SiriL); RecordBone(rig.SiriR);
            RecordBone(rig.ThighL); RecordBone(rig.LegL); RecordBone(rig.FootL);
            RecordBone(rig.ThighR); RecordBone(rig.LegR); RecordBone(rig.FootR);
            RecordBone(rig.ArmL); RecordBone(rig.ForearmL); RecordBone(rig.HandL);
            RecordBone(rig.ArmR); RecordBone(rig.ForearmR); RecordBone(rig.HandR);

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

            float s = amp * phaseSign;
            // Anatomical safety nonlinear saturation (protects waist/thigh skinning weights)
            float yawDeg = Mathf.Clamp((float)(Math.Tanh(gf.RelYaw * 1.35) * 18.5 * s), -21f, 21f);
            float rollDeg = Mathf.Clamp((float)(Math.Tanh(gf.RelRoll * 1.20) * 13.5 * s), -16f, 16f);
            float pitchDeg = Mathf.Clamp((float)(Math.Tanh(gf.RelPitch * 1.20) * 12.0 * s), -15f, 15f);

            // 3-tier pelvic-lumbar distribution (40% Hips + 40% Waist01 + 20% Waist02)
            bool hasW2 = rig.Waist02 != null;
            float wHips = hasW2 ? 0.40f : 0.45f;
            float wW1 = hasW2 ? 0.40f : 0.55f;
            float wW2 = hasW2 ? 0.20f : 0.0f;

            Quaternion qHips = Quaternion.AngleAxis(yawDeg * wHips, axisInward) * Quaternion.AngleAxis(rollDeg * wHips, axisFwd) * Quaternion.AngleAxis(pitchDeg * wHips, axisRight);
            Vector3 nextHipPos = entryBefore + qHips * (hipBefore - entryBefore);
            Quaternion nextHipRot = qHips * rig.Hips.rotation;
            if (!IsFinite(nextHipPos) || !IsFinite(nextHipRot)) { RollbackFrom(snapStart); return; }

            rig.Hips.position = hipBefore + Vector3.ClampMagnitude(nextHipPos - hipBefore, 0.045f);
            rig.Hips.rotation = nextHipRot;

            if (rig.Waist01 != null)
            {
                Quaternion qW1 = Quaternion.AngleAxis(yawDeg * wW1, axisInward) * Quaternion.AngleAxis(rollDeg * wW1, axisFwd) * Quaternion.AngleAxis(pitchDeg * wW1, axisRight);
                Quaternion rW1 = qW1 * rig.Waist01.rotation;
                if (IsFinite(rW1)) rig.Waist01.rotation = rW1;
            }
            if (hasW2)
            {
                Quaternion qW2 = Quaternion.AngleAxis(yawDeg * wW2, axisInward) * Quaternion.AngleAxis(rollDeg * wW2, axisFwd) * Quaternion.AngleAxis(pitchDeg * wW2, axisRight);
                Quaternion rW2 = qW2 * rig.Waist02.rotation;
                if (IsFinite(rW2)) rig.Waist02.rotation = rW2;
            }

            // Entry-pinned zero-drift lock with hard 4.5 cm max displacement guardrail
            float giveLim = actor.sex == 0 ? 0.0080f : 0.0065f;
            Vector3 desiredOffset = axisRight * Mathf.Clamp((float)gf.RelLat * giveLim * s, -0.010f, 0.010f)
                + axisFwd * Mathf.Clamp((float)gf.RelFwd * giveLim * s, -0.010f, 0.010f)
                + axisInward * Mathf.Clamp((float)gf.RelAxial * 0.0075f * s, -0.011f, 0.011f);
            Vector3 pinnedHipPos = entryBone != null
                ? rig.Hips.position + (desiredOffset - (entryBone.position - entryBefore))
                : rig.Hips.position + desiredOffset;
            if (!IsFinite(pinnedHipPos)) { RollbackFrom(snapStart); return; }
            rig.Hips.position = hipBefore + Vector3.ClampMagnitude(pinnedHipPos - hipBefore, 0.045f);

            // Phase-lagged upward spine counter-wave (~0.10s natural S-curve)
            float yawLag = Mathf.Clamp((float)(Math.Tanh(gf.RelYawLag * 1.35) * 18.0 * s), -19f, 19f);
            float rollLag = Mathf.Clamp((float)(Math.Tanh(gf.RelRollLag * 1.20) * 13.0 * s), -15f, 15f);
            float pitchLag = Mathf.Clamp((float)(Math.Tanh(gf.RelPitchLag * 1.20) * 11.5 * s), -14f, 14f);
            ApplySpine(rig.Spine01, axisInward, axisFwd, axisRight, yawLag, rollLag, pitchLag, -0.24f);
            ApplySpine(rig.Spine02, axisInward, axisFwd, axisRight, yawLag, rollLag, pitchLag, -0.22f);
            ApplySpine(rig.Spine03, axisInward, axisFwd, axisRight, yawLag, rollLag, pitchLag, -0.16f);
            ApplySpine(rig.Neck, axisInward, axisFwd, axisRight, yawLag, rollLag, pitchLag, -0.14f);

            // Soft-tissue glute compression & DynamicBone chest/butt weight coupling
            if (syncSoftTissue && actor.sex == 1)
                ApplySoftTissueWeights(rig, gf, currentEnvelope, axisRight, axisInward, axisFwd);

            // Analytical 2-bone soft-IK limb grounding (prevents foot/hand sliding!)
            float dispMag = (rig.Hips.position - hipBefore).magnitude;
            float legPin = Mathf.Lerp(isPrimary ? 0.88f : 0.80f, 0.72f, Mathf.Clamp01(dispMag / 0.045f));
            float armPin = Mathf.Lerp(isPrimary ? 0.68f : 0.58f, 0.48f, Mathf.Clamp01(dispMag / 0.045f));
            if (rig.ThighL != null && rig.LegL != null && rig.FootL != null)
                SolveTwoBoneIK(rig.ThighL, rig.LegL, rig.FootL, footLPos, kneeLPos, footLRot, legPin, 0.72f);
            if (rig.ThighR != null && rig.LegR != null && rig.FootR != null)
                SolveTwoBoneIK(rig.ThighR, rig.LegR, rig.FootR, footRPos, kneeRPos, footRRot, legPin, 0.72f);
            if (rig.ArmL != null && rig.ForearmL != null && rig.HandL != null)
                SolveTwoBoneIK(rig.ArmL, rig.ForearmL, rig.HandL, handLPos, elbowLPos, handLRot, armPin, 0.55f);
            if (rig.ArmR != null && rig.ForearmR != null && rig.HandR != null)
                SolveTwoBoneIK(rig.ArmR, rig.ForearmR, rig.HandR, handRPos, elbowRPos, handRRot, armPin, 0.55f);

            // Final finite check — if any bone ever became non-finite, roll back cleanly!
            if (!IsFinite(rig.Hips.position) || !IsFinite(rig.Hips.rotation))
            {
                RollbackFrom(snapStart);
                return;
            }

            if (isPrimary)
            {
                PrimaryHipDispMm = Vector3.Distance(rig.Hips.position, hipBefore) * 1000f;
                PrimaryEntryDriftMm = entryBone != null ? Vector3.Distance(entryBone.position, entryBefore) * 1000f : 0f;
            }
        }

        private void ApplySoftTissueWeights(ActorRig rig, StudioGrindFrame gf, float softW, Vector3 axisRight, Vector3 axisInward, Vector3 axisFwd)
        {
            float deepPress = Mathf.Clamp01((float)((gf.DepthNorm - 0.70) / 0.30)) * softW;
            if (deepPress > 0.01f)
            {
                float spread = deepPress * 0.0036f;
                float yieldBack = deepPress * 0.0022f;
                if (rig.SiriL != null && IsFinite(rig.SiriL.position)) rig.SiriL.position += (-axisRight * spread - axisInward * yieldBack);
                if (rig.SiriR != null && IsFinite(rig.SiriR.position)) rig.SiriR.position += (axisRight * spread - axisInward * yieldBack);
            }

            if (rig.SoftBones.Count == 0) { SoftTissueBonesSynced = 0; return; }

            // Match MainGame's proven ProceduralBodyGrind world-space inertia force & !IsVer02 elasticity guardrail
            Vector3 inertiaWorld =
                (-axisInward * (float)gf.AccAxial * 0.00055f) +
                (-axisFwd * (float)gf.AccFwd * 0.00045f) +
                (-axisRight * (float)gf.AccLat * 0.00045f);
            if (!IsFinite(inertiaWorld)) return;
            float maxF = 0.016f * Mathf.Clamp01(softW);
            float mag = inertiaWorld.magnitude;
            if (mag > maxF && mag > 1e-6f) inertiaWorld *= (maxF / mag);

            int synced = 0;
            for (int i = 0; i < rig.SoftBones.Count; i++)
            {
                var sb = rig.SoftBones[i];
                if (sb == null || sb.Comp == null || sb.ForceField == null) continue;
                try
                {
                    float gain = sb.IsChest ? 1.0f : 0.80f;
                    Vector3 nextForce = sb.OrigForce + inertiaWorld * gain;
                    if (IsFinite(nextForce))
                    {
                        sb.ForceField.SetValue(sb.Comp, nextForce);
                        sb.Modified = true;
                        synced++;
                    }
                    if (!sb.IsVer02 && sb.ElastField != null)
                    {
                        float nextElast = Mathf.Clamp(sb.OrigElast * (1f + 0.12f * Mathf.Clamp01(softW)), 0.02f, 0.38f);
                        sb.ElastField.SetValue(sb.Comp, nextElast);
                        sb.Modified = true;
                    }
                }
                catch { }
            }
            SoftTissueBonesSynced = synced;
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
                        if (!sb.IsVer02 && sb.ElastField != null) sb.ElastField.SetValue(sb.Comp, sb.OrigElast);
                    }
                    catch { }
                    sb.Modified = false;
                }
            }
            SoftTissueBonesSynced = 0;
        }

        private static void ApplySpine(Transform bone, Vector3 axisInward, Vector3 axisFwd, Vector3 axisRight, float yaw, float roll, float pitch, float scale)
        {
            if (bone == null) return;
            Quaternion q = Quaternion.AngleAxis(yaw * scale, axisInward) * Quaternion.AngleAxis(roll * scale, axisFwd) * Quaternion.AngleAxis(pitch * scale, axisRight);
            Quaternion next = q * bone.rotation;
            if (IsFinite(next)) bone.rotation = next;
        }

        private static void SolveTwoBoneIK(Transform upper, Transform mid, Transform end, Vector3 targetPos, Vector3 hintMidPos, Quaternion targetRot, float weight, float rotWeight)
        {
            if (weight <= 0.001f) return;
            Vector3 pA = upper.position, pB = mid.position, pC = end.position;
            if (!IsFinite(pA) || !IsFinite(pB) || !IsFinite(pC) || !IsFinite(targetPos)) return;
            float len1 = Vector3.Distance(pA, pB), len2 = Vector3.Distance(pB, pC);
            if (len1 < 0.01f || len2 < 0.01f) return;

            Vector3 desiredC = Vector3.Lerp(pC, targetPos, weight);
            Vector3 toTarget = desiredC - pA;
            float rawDist = toTarget.magnitude;
            if (rawDist < 1e-4f || !IsFinite(rawDist)) return;
            float dist = Mathf.Clamp(rawDist, Mathf.Abs(len1 - len2) + 0.005f, (len1 + len2) * 0.985f);
            Vector3 dir = toTarget / rawDist;

            float cosA = Mathf.Clamp((len1 * len1 + dist * dist - len2 * len2) / (2f * len1 * dist), -1f, 1f);
            float sinA = Mathf.Sqrt(Mathf.Max(0f, 1f - cosA * cosA));
            Vector3 pole = Vector3.Lerp(pB, hintMidPos, 0.65f) - pA;
            pole -= dir * Vector3.Dot(pole, dir);
            if (pole.sqrMagnitude < 1e-6f)
            {
                pole = pB - pA;
                pole -= dir * Vector3.Dot(pole, dir);
            }
            if (pole.sqrMagnitude < 1e-6f) return;
            pole.Normalize();

            Vector3 desiredB = pA + dir * (len1 * cosA) + pole * (len1 * sinA);
            Vector3 oldU = pB - pA, newU = desiredB - pA;
            if (oldU.sqrMagnitude > 1e-6f && newU.sqrMagnitude > 1e-6f)
            {
                oldU.Normalize(); newU.Normalize();
                if (Vector3.Dot(oldU, newU) > -0.99f)
                {
                    Quaternion rU = Quaternion.FromToRotation(oldU, newU) * upper.rotation;
                    if (IsFinite(rU)) upper.rotation = rU;
                }
            }

            Vector3 pB2 = mid.position;
            Vector3 clampedTarget = pA + dir * dist;
            Vector3 oldL = end.position - pB2, newL = clampedTarget - pB2;
            if (oldL.sqrMagnitude > 1e-6f && newL.sqrMagnitude > 1e-6f)
            {
                oldL.Normalize(); newL.Normalize();
                if (Vector3.Dot(oldL, newL) > -0.99f)
                {
                    Quaternion rL = Quaternion.FromToRotation(oldL, newL) * mid.rotation;
                    if (IsFinite(rL)) mid.rotation = rL;
                }
            }

            if (rotWeight > 0.01f && IsFinite(targetRot))
            {
                Quaternion rE = Quaternion.Slerp(end.rotation, targetRot, weight * rotWeight);
                if (IsFinite(rE)) end.rotation = rE;
            }
        }

        private ActorRig GetRig(ChaControl actor)
        {
            if (actor == null) return null;
            int id = actor.GetInstanceID();
            ActorRig rig;
            if (rigCache.TryGetValue(id, out rig) && rig != null && rig.Hips != null)
                return rig;

            var map = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var t in actor.GetComponentsInChildren<Transform>(true))
                if (t != null && !map.ContainsKey(t.name)) map[t.name] = t;

            rig = new ActorRig
            {
                InstanceId = id,
                Hips = Pick(map, "cf_j_hips", "cm_j_hips", "cm_J_hips"),
                Waist01 = Pick(map, "cf_j_waist01", "cm_j_waist01", "cm_J_waist01"),
                Waist02 = Pick(map, "cf_j_waist02", "cm_j_waist02", "cm_J_waist02"),
                VaginaEntry = Pick(map, "cf_J_Vagina_root", "cf_j_vagina_root", "cf_j_kokan", "cm_J_dan100_00"),
                AnalEntry = Pick(map, "cf_j_ana", "cf_J_Ana_Root", "a_n_ana"),
                MouthEntry = Pick(map, "cf_J_Mouthup", "cf_J_MouthLow", "a_n_mouth"),
                Spine01 = Pick(map, "cf_j_spine01", "cm_j_spine01"),
                Spine02 = Pick(map, "cf_j_spine02", "cm_j_spine02"),
                Spine03 = Pick(map, "cf_j_spine03", "cm_j_spine03"),
                Neck = Pick(map, "cf_j_neck", "cm_j_neck"),
                SiriL = Pick(map, "cf_j_siri_L"),
                SiriR = Pick(map, "cf_j_siri_R"),
                ThighL = Pick(map, "cf_j_thigh00_L", "cm_j_thigh00_L"),
                LegL = Pick(map, "cf_j_leg01_L", "cm_j_leg01_L"),
                FootL = Pick(map, "cf_j_foot_L", "cf_j_leg03_L", "cm_j_foot_L"),
                ThighR = Pick(map, "cf_j_thigh00_R", "cm_j_thigh00_R"),
                LegR = Pick(map, "cf_j_leg01_R", "cm_j_leg01_R"),
                FootR = Pick(map, "cf_j_foot_R", "cf_j_leg03_R", "cm_j_foot_R"),
                ArmL = Pick(map, "cf_j_arm00_L", "cm_j_arm00_L"),
                ForearmL = Pick(map, "cf_j_forearm01_L", "cm_j_forearm01_L"),
                HandL = Pick(map, "cf_j_hand_L", "cm_j_hand_L"),
                ArmR = Pick(map, "cf_j_arm00_R", "cm_j_arm00_R"),
                ForearmR = Pick(map, "cf_j_forearm01_R", "cm_j_forearm01_R"),
                HandR = Pick(map, "cf_j_hand_R", "cm_j_hand_R")
            };

            if (actor.sex == 1)
            {
                foreach (var comp in actor.GetComponentsInChildren<Component>(true))
                {
                    if (comp == null) continue;
                    var ct = comp.GetType();
                    string tn = ct.Name;
                    if (!tn.StartsWith("DynamicBone", StringComparison.Ordinal)) continue;
                    bool isVer02 = tn.IndexOf("Ver02", StringComparison.OrdinalIgnoreCase) >= 0;
                    var rootField = ct.GetField(isVer02 ? "Root" : "m_Root", DynFlags) ?? ct.GetField("Root", DynFlags) ?? ct.GetField("m_Root", DynFlags);
                    var fForce = ct.GetField(isVer02 ? "Force" : "m_Force", DynFlags) ?? ct.GetField("Force", DynFlags) ?? ct.GetField("m_Force", DynFlags);
                    if (fForce == null || fForce.FieldType != typeof(Vector3)) continue;
                    var rootTr = rootField != null ? rootField.GetValue(comp) as Transform : null;
                    string rn = rootTr != null ? rootTr.name.ToLowerInvariant() : "";
                    var commentField = ct.GetField("Comment", DynFlags);
                    string cm = commentField != null ? (Convert.ToString(commentField.GetValue(comp)) ?? "").ToLowerInvariant() : "";
                    string bn = comp.gameObject != null ? comp.gameObject.name.ToLowerInvariant() : "";
                    string hay = rn + " " + cm + " " + bn;
                    bool isChest = hay.Contains("bust") || hay.Contains("mune") || hay.Contains("胸");
                    bool isButt = hay.Contains("siri") || hay.Contains("butt") || hay.Contains("尻") || hay.Contains("臀");
                    if (!isChest && !isButt) continue;
                    var fElast = isVer02 ? null : ct.GetField("m_Elasticity", DynFlags);
                    var snap = new DynBoneSnapshot { Comp = comp, ForceField = fForce, ElastField = fElast, IsChest = isChest, IsVer02 = isVer02 };
                    try
                    {
                        snap.OrigForce = (Vector3)fForce.GetValue(comp);
                        snap.OrigElast = fElast != null ? Convert.ToSingle(fElast.GetValue(comp)) : 0.1f;
                        rig.SoftBones.Add(snap);
                    }
                    catch { }
                }
            }

            rigCache[id] = rig;
            return rig;
        }

        private static Transform Pick(Dictionary<string, Transform> map, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                Transform t;
                if (map.TryGetValue(names[i], out t) && t != null) return t;
            }
            return null;
        }

        private static bool IsFinite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
        private static bool IsFinite(Vector3 v) { return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z); }
        private static bool IsFinite(Quaternion q) { return IsFinite(q.x) && IsFinite(q.y) && IsFinite(q.z) && IsFinite(q.w); }
    }
}
