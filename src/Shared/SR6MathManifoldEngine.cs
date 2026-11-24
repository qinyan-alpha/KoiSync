using System;

namespace KoiSync.Core
{
    public enum SR6MathFamily
    {
        None = 0,
        VirtualPivotWave = 1,     // Traveling Virtual-Pivot Conical Nutation: conjugate (L1,L2) <-> (R2,R1) fulcrum lock/sweep
        VariablePitchHelix = 2,   // Variable-Pitch Dual Helix & Hourglass Shear: depth-modulated R0 twist + radial envelope
        LissajousKnot6D = 3,      // 6D Golden-Ratio Lissajous / Torus Knot: non-repeating irrational-ratio 6D manifold
        BeatFlutterResonance = 4, // Multi-Scale Beat-Frequency Tide + bandwidth-safe high-freq micro-flutter
        BoundedSmoothChaos = 5    // Bounded C-infinity Smooth Chaos (RK4 Rossler + Duffing 6D projection)
    }

    public sealed class SR6MathParams
    {
        public SR6MathFamily Family = SR6MathFamily.VirtualPivotWave;
        public double FreqHz = 1.35;              // Primary manifold cycle frequency (0.25..3.2 Hz)
        public double DepthMin = 0.08;            // Normalized depth lower bound [0..0.92]
        public double DepthMax = 0.96;            // Normalized depth upper bound [0.08..1.0]
        public double OuterCeilingL0 = FeelTranslator.DefaultOuterCeiling;

        // 6-DOF Geometric Control Knobs
        public double PivotZ = 0.50;              // Virtual fulcrum height: 0=base locked (sweeps tip), 1=tip locked (sweeps base)
        public double PivotSweepHz = 0.22;        // >0 sweeps virtual fulcrum z_p(t) along shaft like a peristaltic wave!
        public double HelixTwist = 0.78;          // 0..1.0 -> unleashes up to +-100 deg R0 helical torsion
        public double OrbitAmp = 0.78;            // 0..1.0 -> lateral/tilt manifold amplitude before workspace projection
        public double BeatHz = 0.22;              // Beat tide modulation frequency (0.08..0.60 Hz)
        public double FlutterHz = 4.6;            // Micro-flutter frequency (3.0..6.0 Hz)
        public double FlutterAmp = 0.14;          // Micro-flutter intensity (0..0.35)
        public int KnotP = 2;                     // Torus knot integer P (or 0 => golden-ratio irrational mode)
        public int KnotQ = 3;                     // Torus knot integer Q

        // Dual-Character 3D Bone & Soft-Tissue Weight Coupling
        public double FemaleWeightShare = -1.0;   // <0 => auto; 0.0=100% male, 0.5=50/50 counter-phase dance, 1.0=100% female
        public double SoftTissueWeight = 1.0;     // 0..1.5 DynamicBone chest/butt inertia & glute squash coupling
        public double BodyScale = 1.0;            // 0..1.5 overall 3D skeletal excursion scale

        public SR6MathParams Sanitized()
        {
            double ceil = Numbers.Clamp(Numbers.Finite(OuterCeilingL0) ? OuterCeilingL0 : FeelTranslator.DefaultOuterCeiling, 0.55, 0.99);
            double dMin = Numbers.Clamp(Numbers.Finite(DepthMin) ? DepthMin : 0.08, 0.0, 0.90);
            double dMax = Numbers.Clamp(Numbers.Finite(DepthMax) ? DepthMax : 0.96, Math.Min(1.0, dMin + 0.06), 1.0);
            return new SR6MathParams
            {
                Family = Family == SR6MathFamily.None ? SR6MathFamily.VirtualPivotWave : Family,
                FreqHz = Numbers.Clamp(Numbers.Finite(FreqHz) ? FreqHz : 1.35, 0.20, 3.20),
                DepthMin = dMin,
                DepthMax = dMax,
                OuterCeilingL0 = ceil,
                PivotZ = Numbers.Clamp(Numbers.Finite(PivotZ) ? PivotZ : 0.50, 0.0, 1.0),
                PivotSweepHz = Numbers.Clamp(Numbers.Finite(PivotSweepHz) ? PivotSweepHz : 0.22, 0.0, 1.20),
                HelixTwist = Numbers.Clamp(Numbers.Finite(HelixTwist) ? HelixTwist : 0.78, 0.0, 1.0),
                OrbitAmp = Numbers.Clamp(Numbers.Finite(OrbitAmp) ? OrbitAmp : 0.78, 0.0, 1.0),
                BeatHz = Numbers.Clamp(Numbers.Finite(BeatHz) ? BeatHz : 0.22, 0.05, 0.80),
                FlutterHz = Numbers.Clamp(Numbers.Finite(FlutterHz) ? FlutterHz : 4.6, 2.5, 6.5),
                FlutterAmp = Numbers.Clamp(Numbers.Finite(FlutterAmp) ? FlutterAmp : 0.14, 0.0, 0.35),
                KnotP = Math.Max(0, Math.Min(7, KnotP)),
                KnotQ = Math.Max(0, Math.Min(9, KnotQ)),
                FemaleWeightShare = Numbers.Finite(FemaleWeightShare) ? (FemaleWeightShare < 0 ? -1.0 : Numbers.Clamp(FemaleWeightShare, 0.0, 1.0)) : -1.0,
                SoftTissueWeight = Numbers.Clamp(Numbers.Finite(SoftTissueWeight) ? SoftTissueWeight : 1.0, 0.0, 1.5),
                BodyScale = Numbers.Clamp(Numbers.Finite(BodyScale) ? BodyScale : 1.0, 0.0, 1.5)
            };
        }
    }

    public struct SR6MathFrame
    {
        public bool Active;
        public SR6MathFamily Family;
        public double DepthNorm;          // 0..1 normalized animation depth
        public double ScrubPhase;         // 0..1 mapped Animator phase
        public double[] PhysicalAxes;     // [6] projected onto SR6 physical coordinate convention (L0: 1=deep, 0=out; others 0..1 centered 0.5)
        public double[] DeviceAxes;       // [6] exact T-Code [L0, L1, L2, R0, R1, R2] in [0..1] (L0: 0=deep, 1=out)
        public double[] AxisVel;          // [6] filtered velocity (units/s)
        public double[] AxisAcc;          // [6] filtered acceleration (units/s^2) -> drives 3D DynamicBone inertia!
        public double ActivePivotZ;       // Live virtual fulcrum height z_p(t) in [0..1]
        public double WorkspaceContract;  // 1.0 = fully inside SR6 polytope; <1.0 = smoothly contracted to avoid linkage bind
        public double BlendEnvelope;      // 0..1 smooth fade-in / fade-out envelope

        // Standardized 3D relative pelvic pose components in [-1..+1] for DualActorKinematicCoupler
        public double RelLat;             // L2 lateral sway
        public double RelFwd;             // L1 forward-back surge
        public double RelAxial;           // Axial pulse / breathing offset
        public double RelYaw;             // R0 twist (normalized; anatomically tanh-remapped inside coupler)
        public double RelRoll;            // R1 lateral tilt
        public double RelPitch;           // R2 pitch tilt
        public double RelYawLag;
        public double RelRollLag;
        public double RelPitchLag;
        public double FemaleWeightShare;  // <0 auto; 0..1 explicit dual-actor split
        public double SoftTissueWeight;   // 0..1.5 DynamicBone inertia & glute squash weight
        public double BodyScale;
    }

    /// <summary>
    /// True 6-DOF SR6 Mathematical Limit Trajectory Synthesizer with:
    ///   - 5 non-human-limited differential geometry / dynamical-system families
    ///   - Exact SR6 linkage workspace feasibility projection using SR6DeviceRotation.Carrier
    ///   - C2-smooth second-order velocity & acceleration/jerk bandwidth filter
    ///   - Full kinematic + acceleration export for Dual-Actor 3D Bone & Soft-Tissue Weight Coupling
    /// STRICTLY intended for direct genital penetration (sonyu*) only.
    /// </summary>
    public sealed class SR6MathManifoldEngine
    {
        private const double Phi = 0.6180339887498949;
        private const double Sqrt2 = 1.4142135623730951;
        private const double InvSqrt3 = 0.5773502691896257;
        private const double SqrtPhi = 0.7861513777574233;
        private const double InvSqrtPhi = 1.272019649514069;

        // Physically tuned SR6 servo bandwidth bounds (in normalized 0..1 T-Code units)
        private static readonly double[] MaxAxisVel = { 4.2, 3.6, 3.6, 3.8, 3.4, 3.6 };
        private static readonly double[] MaxAxisAcc = { 42.0, 34.0, 34.0, 36.0, 32.0, 34.0 };

        private bool active;
        private SR6MathParams cfg = new SR6MathParams();
        private double elapsed;
        private double phaseMain;
        private double phaseSweep;
        private double blendEnv;
        private bool prevInward = true;
        private double prevDepthNorm = 0.5;

        // C2 per-axis second-order dynamics state (T-Code space: L0=0 deep..1 out, others 0..1)
        private readonly double[] filtPos = { 0.5, 0.5, 0.5, 0.5, 0.5, 0.5 };
        private readonly double[] filtVel = new double[6];
        private readonly double[] filtAcc = new double[6];
        private bool filtInit;

        // Phase-lagged spine history buffer (3 samples ~ 0.09s)
        private double lagYaw, lagRoll, lagPitch;

        // RK4 smooth Rossler + Duffing chaos integrator state + smooth zero-mean DC blocker
        private double rx = 1.2, ry = 0.4, rz = 0.2;
        private double duffX = 0.6, duffV = 0.0;
        private readonly double[] chaosDc = new double[5];

        public bool Active { get { return active; } }
        public SR6MathFamily ActiveFamily { get { return active ? cfg.Family : SR6MathFamily.None; } }
        public SR6MathParams CurrentParams { get { return cfg.Sanitized(); } }
        public string LastStatus { get; private set; }
        public double LastWorkspaceContract { get; private set; }
        public double LastActivePivotZ { get; private set; }

        public SR6MathManifoldEngine()
        {
            LastStatus = "空闲";
            LastWorkspaceContract = 1.0;
            LastActivePivotZ = 0.5;
        }

        public void Start(SR6MathParams parameters)
        {
            bool wasActive = active;
            cfg = (parameters ?? new SR6MathParams()).Sanitized();
            active = true;
            if (!wasActive)
            {
                elapsed = 0.0;
                blendEnv = 0.0;
                filtInit = false;
                for (int i = 0; i < 6; i++) { filtVel[i] = 0.0; filtAcc[i] = 0.0; }
                rx = 3.2; ry = -4.1; rz = 0.85;
                duffX = 0.75; duffV = 0.35;
                for (int i = 0; i < 5; i++) chaosDc[i] = 0.0;
            }
            else
            {
                // Seamless hot-switch between math families: keep C2 filter state continuous without dropping blendEnv!
                blendEnv = Math.Max(blendEnv, 1.0);
                if (cfg.Family == SR6MathFamily.BoundedSmoothChaos)
                {
                    for (int i = 0; i < 5; i++) chaosDc[i] = 0.0;
                }
            }
            LastWorkspaceContract = 1.0;
            LastStatus = FamilyDisplayName(cfg.Family);
        }

        public void Tune(Action<SR6MathParams> mutator)
        {
            if (mutator == null) return;
            var copy = cfg.Sanitized();
            mutator(copy);
            cfg = copy.Sanitized();
            if (active) LastStatus = FamilyDisplayName(cfg.Family);
        }

        public void Stop(string reason = "SR6 数学轨迹已停止")
        {
            active = false;
            blendEnv = 0.0;
            filtInit = false;
            LastStatus = string.IsNullOrEmpty(reason) ? "已停止" : reason;
        }

        public void UpdateOuterCeiling(double outerCeiling)
        {
            cfg.OuterCeilingL0 = Numbers.Clamp(Numbers.Finite(outerCeiling) ? outerCeiling : FeelTranslator.DefaultOuterCeiling, 0.55, 0.99);
        }

        public static string FamilyDisplayName(SR6MathFamily fam)
        {
            switch (fam)
            {
                case SR6MathFamily.VirtualPivotWave: return "虚拟支点行波锥面";
                case SR6MathFamily.VariablePitchHelix: return "变螺距双螺旋剪切";
                case SR6MathFamily.LissajousKnot6D: return "6D黄金比例利萨如纽结";
                case SR6MathFamily.BeatFlutterResonance: return "双尺度拍频微颤共振";
                case SR6MathFamily.BoundedSmoothChaos: return "有界平滑混沌吸引子";
                default: return "关闭";
            }
        }

        public static SR6MathFamily ParseFamily(string raw)
        {
            string s = (raw ?? "").Trim().ToLowerInvariant();
            if (s.Contains("pivot") || s.Contains("cone") || s.Contains("支点") || s.Contains("锥") || s.Contains("行波"))
                return SR6MathFamily.VirtualPivotWave;
            if (s.Contains("helix") || s.Contains("spiral") || s.Contains("screw") || s.Contains("螺旋") || s.Contains("沙漏"))
                return SR6MathFamily.VariablePitchHelix;
            if (s.Contains("lissajous") || s.Contains("knot") || s.Contains("torus") || s.Contains("利萨如") || s.Contains("纽结") || s.Contains("织网"))
                return SR6MathFamily.LissajousKnot6D;
            if (s.Contains("beat") || s.Contains("flutter") || s.Contains("tide") || s.Contains("拍频") || s.Contains("微颤") || s.Contains("潮汐") || s.Contains("共振"))
                return SR6MathFamily.BeatFlutterResonance;
            if (s.Contains("chaos") || s.Contains("rossler") || s.Contains("lorenz") || s.Contains("混沌") || s.Contains("吸引子"))
                return SR6MathFamily.BoundedSmoothChaos;
            return SR6MathFamily.VirtualPivotWave;
        }

        /// <summary>
        /// Evaluates exact mechanical linkage feasibility for a candidate SR6 T-Code command
        /// using the real 4-arm carrier kinematics from SR6DeviceRotation.
        /// Returns true iff left/right arm span and roll remain strictly inside non-binding bounds.
        /// </summary>
        public static bool IsWithinSR6Workspace(double l0, double l1, double l2, double r1)
        {
            double cl0 = Numbers.Clamp(l0, 0.01, 0.99);
            double cl1 = Numbers.Clamp(l1, 0.04, 0.96);
            double cl2 = Numbers.Clamp(l2, 0.04, 0.96);
            double cr1 = Numbers.Clamp(r1, 0.05, 0.95);

            // Replicate exact SR6DeviceRotation carrier arm check (145.5mm mechanical span limit -> keep <= 143.8mm margin!)
            double roll = (cr1 / 0.9999 * 2.0 - 1.0) * 3000.0;
            double surge = (cl1 / 0.9999 * 2.0 - 1.0) * 3000.0;
            double stroke = (cl0 / 0.9999 * 2.0 - 1.0) * 6000.0;

            double a = ServoVal(16248.0 - surge, 1500.0 + stroke + roll);
            double b = ServoVal(16248.0 - surge, 1500.0 - stroke - roll);
            double c = ServoVal(16248.0 - surge, 1500.0 - stroke + roll);
            double d = ServoVal(16248.0 - surge, 1500.0 + stroke - roll);
            if (!Numbers.Finite(a) || !Numbers.Finite(b) || !Numbers.Finite(c) || !Numbers.Finite(d))
                return false;

            double lowerLeft = ServoAng(1515.105 - a, 0.5) - Math.PI;
            double upperLeft = ServoAng(1515.105 + b, 0.5);
            double lowerRight = ServoAng(1515.105 + d, -0.5) - Math.PI;
            double upperRight = ServoAng(1515.105 - c, -0.5);

            var uL = ArmVec(58.5, 0.0, upperLeft, 1.0);
            var lL = ArmVec(58.5, 30.0, lowerLeft, 1.0);
            var uR = ArmVec(-58.5, 0.0, upperRight, -1.0);
            var lR = ArmVec(-58.5, 30.0, lowerRight, -1.0);

            if ((uL - lL).Length >= 342.0 || (uR - lR).Length >= 342.0) return false;
            var left = IntersectArms(uL, lL);
            var right = IntersectArms(uR, lR);
            double dist = (right - left).Length;
            // Nominal neutral X spacing is 2 * (58.5 + 14.5) = 146.0 mm;
            // keep differential Y/Z carrier span smoothly inside 168.0 mm (<= ~84 mm differential)
            // so left/right bell-crank rods stay well clear of mechanical lock/bind!
            if (!Numbers.Finite(dist) || dist > 168.0) return false;

            // Also confirm coupled roll angle at this L0 stays within physical SR6RollMapping table
            double maxRollDeg = Math.Abs(SR6RollMapping.Angle(0.0, cl0));
            double curRollDeg = Math.Abs(SR6RollMapping.Angle(cr1, cl0));
            return curRollDeg <= maxRollDeg * 0.96;
        }

        /// <summary>
        /// Projects any ideal 6-DOF T-Code command [L0, L1, L2, R0, R1, R2] radially onto the
        /// exact non-binding SR6 reachable polytope while preserving L0 depth and R0 twist.
        /// </summary>
        public static double ProjectToSR6Workspace(double[] tcode)
        {
            tcode[0] = Numbers.Clamp(tcode[0], 0.01, 0.99);
            tcode[1] = Numbers.Clamp(tcode[1], 0.04, 0.96);
            tcode[2] = Numbers.Clamp(tcode[2], 0.04, 0.96);
            tcode[3] = Numbers.Clamp(tcode[3], 0.05, 0.95);
            tcode[4] = Numbers.Clamp(tcode[4], 0.05, 0.95);
            tcode[5] = Numbers.Clamp(tcode[5], 0.04, 0.96);

            if (IsWithinSR6Workspace(tcode[0], tcode[1], tcode[2], tcode[4]))
                return 1.0;

            double dL1 = tcode[1] - 0.5;
            double dL2 = tcode[2] - 0.5;
            double dR1 = tcode[4] - 0.5;
            double dR2 = tcode[5] - 0.5;

            double lo = 0.0, hi = 1.0;
            for (int iter = 0; iter < 12; iter++)
            {
                double mid = 0.5 * (lo + hi);
                if (IsWithinSR6Workspace(tcode[0], 0.5 + dL1 * mid, 0.5 + dL2 * mid, 0.5 + dR1 * mid))
                    lo = mid;
                else
                    hi = mid;
            }
            tcode[1] = 0.5 + dL1 * lo;
            tcode[2] = 0.5 + dL2 * lo;
            tcode[4] = 0.5 + dR1 * lo;
            tcode[5] = 0.5 + dR2 * lo;
            return lo;
        }

        public SR6MathFrame Step(double dt, PhaseDepthMap phaseMap, double[] baselineNormalized)
        {
            if (!active) return new SR6MathFrame { Active = false };
            double stepDt = Numbers.Clamp(Numbers.Finite(dt) && dt > 0 ? dt : (1.0 / 60.0), 0.001, 0.05);
            elapsed += stepDt;
            blendEnv = Math.Min(1.0, blendEnv + stepDt / 0.45);
            double smoothEnv = blendEnv * blendEnv * (3.0 - 2.0 * blendEnv);

            phaseMain += stepDt * cfg.FreqHz * 2.0 * Math.PI;
            if (cfg.PivotSweepHz > 0.001)
                phaseSweep += stepDt * cfg.PivotSweepHz * 2.0 * Math.PI;

            double rawDepthNorm, rawL1, rawL2, rawR0, rawR1, rawR2, activePivotZ;
            EvaluateFamily(stepDt, out rawDepthNorm, out rawL1, out rawL2, out rawR0, out rawR1, out rawR2, out activePivotZ);
            LastActivePivotZ = activePivotZ;

            // Convert depthNorm [0=shallow .. 1=deep] to T-Code L0 [0=deep .. OuterCeilingL0=outer]
            double depthNorm = Numbers.Clamp(rawDepthNorm, 0.0, 1.0);
            double devL0 = Numbers.Clamp(cfg.OuterCeilingL0 * (1.0 - depthNorm), 0.01, cfg.OuterCeilingL0);

            // Candidate 6-DOF T-Code vector [L0, L1, L2, R0, R1, R2]
            var target = new[]
            {
                devL0,
                0.5 + Numbers.Clamp(rawL1, -0.46, 0.46),
                0.5 + Numbers.Clamp(rawL2, -0.46, 0.46),
                0.5 + Numbers.Clamp(rawR0, -0.45, 0.45),
                0.5 + Numbers.Clamp(rawR1, -0.44, 0.44),
                0.5 + Numbers.Clamp(rawR2, -0.46, 0.46)
            };

            // Blend smoothly from current pose on start
            if (baselineNormalized != null && baselineNormalized.Length >= 6 && smoothEnv < 0.999)
            {
                double baseL0 = 1.0 - Numbers.Clamp(baselineNormalized[0], 0.0, 1.0);
                target[0] = baseL0 + (target[0] - baseL0) * smoothEnv;
                for (int i = 1; i < 6; i++)
                {
                    double b = Numbers.Clamp(baselineNormalized[i], 0.0, 1.0);
                    target[i] = b + (target[i] - b) * smoothEnv;
                }
            }

            // 1) Exact SR6 linkage feasibility projection!
            double contract = ProjectToSR6Workspace(target);
            LastWorkspaceContract = contract;

            // 2) C2 velocity & acceleration/jerk bandwidth filter!
            if (!filtInit)
            {
                for (int i = 0; i < 6; i++)
                {
                    filtPos[i] = target[i];
                    filtVel[i] = 0.0;
                    filtAcc[i] = 0.0;
                }
                filtInit = true;
            }
            else
            {
                const double wn = 36.0; // Critically-damped natural tracking frequency (~5.7 Hz bandwidth)
                const double zeta = 0.95;
                // Substep to h <= 0.005s so explicit semi-implicit Euler stays strictly stable even below 30 FPS (fixes P0-4)
                int substeps = Math.Max(1, (int)Math.Ceiling(stepDt / 0.005));
                double h = stepDt / substeps;
                for (int i = 0; i < 6; i++)
                {
                    double pos = filtPos[i];
                    double vel = filtVel[i];
                    double prevVel = vel;
                    for (int s = 0; s < substeps; s++)
                    {
                        double err = target[i] - pos;
                        double desAcc = wn * wn * err - 2.0 * zeta * wn * vel;
                        double acc = Numbers.Clamp(desAcc, -MaxAxisAcc[i], MaxAxisAcc[i]);
                        vel = Numbers.Clamp(vel + acc * h, -MaxAxisVel[i], MaxAxisVel[i]);
                        pos = Numbers.Clamp(pos + vel * h, 0.01, 0.99);
                    }
                    filtAcc[i] = (vel - prevVel) / stepDt;
                    filtVel[i] = vel;
                    filtPos[i] = pos;
                }
                // Re-confirm workspace safety after dynamics integration
                ProjectToSR6Workspace(filtPos);
            }

            // Map filtered depth back to Animator scrub phase via PhaseDepthMap
            double effDepth = Numbers.Clamp(1.0 - filtPos[0] / Math.Max(0.05, cfg.OuterCeilingL0), 0.0, 1.0);
            if (effDepth > prevDepthNorm + 0.003) prevInward = true;
            else if (effDepth < prevDepthNorm - 0.003) prevInward = false;
            prevDepthNorm = effDepth;
            double scrub = (phaseMap != null && phaseMap.Ready)
                ? phaseMap.MapDepthToPhase(effDepth, prevInward, true)
                : effDepth;

            // Build PhysicalAxes (Drive convention: PhysicalAxes[0] = 1 - DeviceL0; [1..5] = T-Code [1..5])
            var phys = new[]
            {
                Numbers.Clamp(1.0 - filtPos[0], 0.0, 1.0),
                filtPos[1],
                filtPos[2],
                filtPos[3],
                filtPos[4],
                filtPos[5]
            };
            var devCopy = new double[6];
            var velCopy = new double[6];
            var accCopy = new double[6];
            Array.Copy(filtPos, devCopy, 6);
            Array.Copy(filtVel, velCopy, 6);
            Array.Copy(filtAcc, accCopy, 6);

            // Standardized [-1..+1] relative 3D components + lagged spine wave
            double relFwd = (filtPos[1] - 0.5) * 2.0;
            double relLat = (filtPos[2] - 0.5) * 2.0;
            double relYaw = (filtPos[3] - 0.5) * 2.0;
            double relRoll = (filtPos[4] - 0.5) * 2.0;
            double relPitch = (filtPos[5] - 0.5) * 2.0;
            double lagAlpha = 1.0 - Math.Exp(-stepDt / 0.10);
            lagYaw += (relYaw - lagYaw) * lagAlpha;
            lagRoll += (relRoll - lagRoll) * lagAlpha;
            lagPitch += (relPitch - lagPitch) * lagAlpha;

            return new SR6MathFrame
            {
                Active = true,
                Family = cfg.Family,
                DepthNorm = effDepth,
                ScrubPhase = scrub,
                PhysicalAxes = phys,
                DeviceAxes = devCopy,
                AxisVel = velCopy,
                AxisAcc = accCopy,
                ActivePivotZ = activePivotZ,
                WorkspaceContract = contract,
                BlendEnvelope = smoothEnv,
                RelLat = relLat,
                RelFwd = relFwd,
                RelAxial = Math.Sin(phaseMain) * 0.25,
                RelYaw = relYaw,
                RelRoll = relRoll,
                RelPitch = relPitch,
                RelYawLag = lagYaw,
                RelRollLag = lagRoll,
                RelPitchLag = lagPitch,
                FemaleWeightShare = cfg.FemaleWeightShare,
                SoftTissueWeight = cfg.SoftTissueWeight,
                BodyScale = cfg.BodyScale
            };
        }

        private void EvaluateFamily(double dt, out double depthNorm, out double l1, out double l2, out double r0, out double r1, out double r2, out double activePivotZ)
        {
            double span = cfg.DepthMax - cfg.DepthMin;
            double orb = cfg.OrbitAmp;
            double tw = cfg.HelixTwist;
            activePivotZ = cfg.PivotZ;

            switch (cfg.Family)
            {
                case SR6MathFamily.VirtualPivotWave:
                {
                    // Dynamic virtual fulcrum z_p(t) in [0..1] (sweeping up & down shaft if PivotSweepHz > 0)
                    double zp = cfg.PivotSweepHz > 0.001
                        ? Numbers.Clamp(0.5 + 0.48 * Math.Sin(phaseSweep), 0.02, 0.98)
                        : cfg.PivotZ;
                    activePivotZ = zp;
                    // Smooth breathing depth wave centered deep-to-mid so wall pressure stays continuous
                    double dWave = 0.5 - 0.5 * Math.Cos(phaseMain * 0.5);
                    depthNorm = cfg.DepthMin + span * (0.35 + 0.65 * dWave);

                    // Conical nutation angles (R2=pitch, R1=roll)
                    double pitch = 0.40 * orb * Math.Cos(phaseMain);
                    double roll = 0.36 * orb * Math.Sin(phaseMain);

                    // EXACT conjugate translation-rotation pivot compensation:
                    // zp=0 (base pinned) -> lever = -1 -> L1/L2 shift WITH tilt so base holds still and tip sweeps huge cone!
                    // zp=1 (tip pinned)  -> lever = +1 -> L1/L2 oppose tilt so tip pins dead-center and base orbits around it!
                    double lever = 2.0 * zp - 1.0;
                    l1 = -0.34 * lever * (pitch / Math.Max(0.15, 0.40 * orb + 1e-6)) * orb;
                    l2 = +0.34 * lever * (roll / Math.Max(0.15, 0.36 * orb + 1e-6)) * orb;
                    r2 = pitch;
                    r1 = roll;
                    // Phase-locked peristaltic twist accentuating the moving fulcrum
                    r0 = 0.34 * tw * Math.Sin(phaseMain + zp * Math.PI);
                    return;
                }

                case SR6MathFamily.VariablePitchHelix:
                {
                    // Smooth asymmetric depth stroke (quick expressive plunge, sensual helical draw-back)
                    double u = (phaseMain / (2.0 * Math.PI)) % 1.0;
                    if (u < 0.0) u += 1.0;
                    double dWave = u < 0.38
                        ? 0.5 - 0.5 * Math.Cos((u / 0.38) * Math.PI)
                        : 0.5 + 0.5 * Math.Cos(((u - 0.38) / 0.62) * Math.PI);
                    depthNorm = cfg.DepthMin + span * dWave;

                    // Variable-pitch helical torsion: high-lead shallow twist + deep counter-torque lock!
                    double helixPhase = phaseMain + 0.85 * Math.Sin(phaseMain) * (1.0 - 0.5 * dWave);
                    r0 = 0.43 * tw * Math.Sin(helixPhase);

                    // Hyperboloid (hourglass) radial envelope: tight at shallow/deep extremes, blooming in mid-shaft!
                    double rad = (0.32 + 0.68 * Math.Sin(dWave * Math.PI)) * orb;
                    double phi = phaseMain * 1.5;
                    l1 = 0.34 * rad * Math.Cos(phi);
                    l2 = 0.34 * rad * Math.Sin(phi);
                    r2 = 0.36 * rad * Math.Cos(phi + 0.45);
                    r1 = 0.32 * rad * Math.Sin(phi + 0.45);
                    activePivotZ = dWave;
                    return;
                }

                case SR6MathFamily.LissajousKnot6D:
                {
                    // Either rational torus knot (p:q) or irrational golden-ratio non-repeating 6D manifold!
                    double f1 = cfg.KnotP > 0 ? (double)cfg.KnotP / Math.Max(1, cfg.KnotQ) : Phi;
                    double f2 = cfg.KnotP > 0 ? (double)(cfg.KnotP + 1) / Math.Max(1, cfg.KnotQ) : Sqrt2;
                    double fTwist = InvSqrt3;

                    double dWave = 0.5 - 0.5 * Math.Cos(phaseMain);
                    depthNorm = cfg.DepthMin + span * dWave;

                    l1 = 0.36 * orb * Math.Sin(phaseMain * f1 + 0.3);
                    l2 = 0.36 * orb * Math.Sin(phaseMain * f2 + 1.1);
                    r0 = 0.42 * tw * Math.Sin(phaseMain * fTwist);
                    r1 = 0.34 * orb * Math.Sin(phaseMain * SqrtPhi + 0.7);
                    r2 = 0.38 * orb * Math.Cos(phaseMain * InvSqrtPhi + 0.2);
                    activePivotZ = 0.5 + 0.4 * Math.Sin(phaseMain * Phi);
                    return;
                }

                case SR6MathFamily.BeatFlutterResonance:
                {
                    // Macro beat tide from two close frequencies f and f + BeatHz
                    double beatAngle = elapsed * cfg.BeatHz * 2.0 * Math.PI;
                    double tide = 0.5 - 0.5 * Math.Cos(beatAngle); // 0=calm trough, 1=intense peak

                    double baseWave = 0.5 - 0.5 * Math.Cos(phaseMain);
                    // Micro-flutter surges selectively near deep/high-tide peaks
                    double flutter = Math.Sin(elapsed * cfg.FlutterHz * 2.0 * Math.PI) * cfg.FlutterAmp * (0.25 + 0.75 * tide);

                    double dRaw = cfg.DepthMin + span * (0.25 + 0.75 * baseWave * (0.45 + 0.55 * tide)) + flutter * 0.28;
                    depthNorm = Numbers.Clamp(dRaw, 0.0, 1.0);

                    double env = (0.30 + 0.70 * tide) * orb;
                    l1 = 0.34 * env * Math.Cos(phaseMain * 0.75);
                    l2 = 0.34 * env * Math.Sin(phaseMain * 0.75 + beatAngle * 0.5);
                    r2 = 0.36 * env * Math.Cos(phaseMain + 0.4);
                    r1 = 0.32 * env * Math.Sin(phaseMain + 0.4);
                    // Superimpose crisp micro-torsional buzz on R0!
                    r0 = 0.28 * tw * Math.Sin(phaseMain * 0.5) + flutter * (0.65 + 0.35 * tw);
                    activePivotZ = tide;
                    return;
                }

                case SR6MathFamily.BoundedSmoothChaos:
                default:
                {
                    // RK4 integrate Rossler + forced Duffing chaotic oscillators (C-infinity smooth, zero jumps!)
                    double h = dt * (2.6 + cfg.FreqHz * 2.4);
                    StepRosslerRK4(h);
                    StepDuffingRK4(dt * (2.2 + cfg.FreqHz * 1.9));

                    // Orthonormal smooth projection + gentle zero-mean DC centering so ALL 6 DOFs stay centered around 50 & non-sparse!
                    double nx = Math.Tanh(rx / 6.2);
                    double ny = Math.Tanh(ry / 6.2);
                    double nz = Math.Tanh((rz - 2.0) / 4.8);
                    double nd = Math.Tanh(duffX / 1.10);
                    double nv = Math.Tanh(duffV / 1.30);

                    double dWave = 0.5 + 0.5 * Math.Tanh((0.75 * nx + 0.45 * nd) * 1.25);
                    depthNorm = cfg.DepthMin + span * Numbers.Clamp(dWave, 0.0, 1.0);

                    double raw0 = Math.Tanh((0.75 * ny + 0.45 * nz) * 1.2);
                    double raw1 = Math.Tanh((0.70 * nd - 0.55 * nx) * 1.2);
                    double raw2 = Math.Tanh((0.65 * nx + 0.65 * ny) * 1.2);
                    double raw3 = Math.Tanh((0.72 * ny - 0.55 * nv) * 1.2);
                    double raw4 = Math.Tanh((0.65 * nv + 0.55 * nz - 0.35 * nx) * 1.2);

                    double dcAlpha = 1.0 - Math.Exp(-dt / 1.8);
                    chaosDc[0] += (raw0 - chaosDc[0]) * dcAlpha;
                    chaosDc[1] += (raw1 - chaosDc[1]) * dcAlpha;
                    chaosDc[2] += (raw2 - chaosDc[2]) * dcAlpha;
                    chaosDc[3] += (raw3 - chaosDc[3]) * dcAlpha;
                    chaosDc[4] += (raw4 - chaosDc[4]) * dcAlpha;

                    l1 = 0.38 * orb * Numbers.Clamp(raw0 - 0.85 * chaosDc[0], -1.0, 1.0);
                    l2 = 0.38 * orb * Numbers.Clamp(raw1 - 0.85 * chaosDc[1], -1.0, 1.0);
                    r0 = 0.42 * tw * Numbers.Clamp(raw2 - 0.85 * chaosDc[2], -1.0, 1.0);
                    r1 = 0.36 * orb * Numbers.Clamp(raw3 - 0.85 * chaosDc[3], -1.0, 1.0);
                    r2 = 0.38 * orb * Numbers.Clamp(raw4 - 0.85 * chaosDc[4], -1.0, 1.0);
                    activePivotZ = 0.5 + 0.45 * Math.Tanh(0.6 * nz + 0.5 * nd);
                    return;
                }
            }
        }

        private void StepRosslerRK4(double h)
        {
            const double a = 0.2, b = 0.2, c = 5.7;
            double k1x = -ry - rz, k1y = rx + a * ry, k1z = b + rz * (rx - c);
            double x2 = rx + 0.5 * h * k1x, y2 = ry + 0.5 * h * k1y, z2 = rz + 0.5 * h * k1z;
            double k2x = -y2 - z2, k2y = x2 + a * y2, k2z = b + z2 * (x2 - c);
            double x3 = rx + 0.5 * h * k2x, y3 = ry + 0.5 * h * k2y, z3 = rz + 0.5 * h * k2z;
            double k3x = -y3 - z3, k3y = x3 + a * y3, k3z = b + z3 * (x3 - c);
            double x4 = rx + h * k3x, y4 = ry + h * k3y, z4 = rz + h * k3z;
            double k4x = -y4 - z4, k4y = x4 + a * y4, k4z = b + z4 * (x4 - c);

            rx += (h / 6.0) * (k1x + 2.0 * k2x + 2.0 * k3x + k4x);
            ry += (h / 6.0) * (k1y + 2.0 * k2y + 2.0 * k3y + k4y);
            rz += (h / 6.0) * (k1z + 2.0 * k2z + 2.0 * k3z + k4z);

            if (!Numbers.Finite(rx) || !Numbers.Finite(ry) || !Numbers.Finite(rz) || Math.Abs(rx) > 40.0 || Math.Abs(ry) > 40.0 || Math.Abs(rz) > 60.0)
            {
                rx = 1.2; ry = 0.4; rz = 0.2;
            }
        }

        private void StepDuffingRK4(double h)
        {
            const double delta = 0.25, gamma = 0.50, omega = 1.2;
            double f = gamma * Math.Cos(omega * elapsed);
            double a1 = f - delta * duffV + duffX - duffX * duffX * duffX;
            double x2 = duffX + 0.5 * h * duffV, v2 = duffV + 0.5 * h * a1;
            double a2 = f - delta * v2 + x2 - x2 * x2 * x2;
            double x3 = duffX + 0.5 * h * v2, v3 = duffV + 0.5 * h * a2;
            double a3 = f - delta * v3 + x3 - x3 * x3 * x3;
            double x4 = duffX + h * v3, v4 = duffV + h * a3;
            double a4 = f - delta * v4 + x4 - x4 * x4 * x4;

            duffX += (h / 6.0) * (duffV + 2.0 * v2 + 2.0 * v3 + v4);
            duffV += (h / 6.0) * (a1 + 2.0 * a2 + 2.0 * a3 + a4);
            if (!Numbers.Finite(duffX) || !Numbers.Finite(duffV) || Math.Abs(duffX) > 10.0)
            {
                duffX = 0.5; duffV = 0.0;
            }
        }

        private static double ServoVal(double horizontal, double vertical)
        {
            horizontal /= 100.0;
            vertical /= 100.0;
            double sq = horizontal * horizontal + vertical * vertical;
            if (sq < 1e-6) return double.NaN;
            double cosArg = (sq - 28125.0) / (100.0 * Math.Sqrt(sq));
            if (cosArg < -0.995 || cosArg > 0.995) return double.NaN;
            return 637.0 * (Math.Atan2(horizontal, vertical) + Math.Acos(cosArg) - 3.14159);
        }

        private static double ServoAng(double pulse, double sign)
        {
            return (pulse / (1000000.0 / 330.0) * 2.0 - 1.0) * Math.PI * sign;
        }

        private static MotionVector ArmVec(double x, double y, double angle, double side)
        {
            return new MotionVector(x + 14.5 * side, y - 50.0 * Math.Cos(angle), 49.92 - 50.0 * Math.Sin(angle));
        }

        private static MotionVector IntersectArms(MotionVector upper, MotionVector lower)
        {
            var center = (upper + lower) * 0.5;
            var delta = upper - lower;
            double r = Math.Sqrt(Math.Max(0.0, 175.0 * 175.0 - MotionVector.Dot(delta, delta) * 0.25));
            return center + MotionVector.Cross(delta, new MotionVector(1, 0, 0)).Unit() * r;
        }
    }
}
