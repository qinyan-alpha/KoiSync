using System;
using System.Globalization;
using System.Xml;
using KoiSync.Core;

namespace KoiSync.Studio
{
    public enum StudioGrindPattern
    {
        Off = 0,
        Circle = 1,       // 圆周深磨 (360° 骨盆画圈)
        Figure8 = 2,      // ∞ 字八字扭腰研磨
        Corkscrew = 3,    // 螺旋钻磨 (强 R0 旋转 + 紧凑圆周)
        RockFB = 4,       // 前后深顶碾磨 (G点/宫颈强俯仰)
        LineFB = 5,       // 纯前后直线推磨 (L1+R2)
        LineLR = 6,       // 纯左右直线横磨 (L2+R1)
        Lissajous6D = 7   // 6D 黄金比例无重复李萨如科研流形
    }

    public sealed class ChapterGrindOptions
    {
        public StudioGrindPattern Pattern = StudioGrindPattern.Off;
        public double Intensity = 0.65;       // 0.05..1.00
        public double Hertz = 1.20;           // 0.30..3.50 Hz
        public bool DepthWeighted = true;     // Swells smoothly when inserted deep, softens on withdrawal
        public bool LinkSkeleton = true;      // Reverse-drive 3D pelvis/waist/spine + limb IK in Studio
        public bool SoftTissue = true;        // Reverse-couple female chest/butt DynamicBone & glute spread weights
        public ActiveMoverMode MoverMode = ActiveMoverMode.Auto; // Auto / Female / Male / Both

        public bool Enabled { get { return Pattern != StudioGrindPattern.Off && Intensity > 0.01; } }

        public ChapterGrindOptions Copy()
        {
            return new ChapterGrindOptions
            {
                Pattern = Pattern,
                Intensity = Intensity,
                Hertz = Hertz,
                DepthWeighted = DepthWeighted,
                LinkSkeleton = LinkSkeleton,
                SoftTissue = SoftTissue,
                MoverMode = MoverMode
            };
        }

        public void Validate()
        {
            if ((int)Pattern < 0 || (int)Pattern > 7) Pattern = StudioGrindPattern.Off;
            Intensity = Numbers.Finite(Intensity) ? Numbers.Clamp(Intensity, 0.0, 1.0) : 0.65;
            Hertz = Numbers.Finite(Hertz) ? Numbers.Clamp(Hertz, 0.3, 3.5) : 1.20;
            if ((int)MoverMode < 0 || (int)MoverMode > 3) MoverMode = ActiveMoverMode.Auto;
        }

        public static bool Equivalent(ChapterGrindOptions a, ChapterGrindOptions b)
        {
            bool aOff = a == null || !a.Enabled;
            bool bOff = b == null || !b.Enabled;
            if (aOff && bOff) return true;
            if (a == null || b == null) return false;
            return a.Pattern == b.Pattern
                && Math.Abs(a.Intensity - b.Intensity) < 1e-6
                && Math.Abs(a.Hertz - b.Hertz) < 1e-6
                && a.DepthWeighted == b.DepthWeighted
                && a.LinkSkeleton == b.LinkSkeleton
                && a.SoftTissue == b.SoftTissue
                && a.MoverMode == b.MoverMode;
        }

        public void Write(XmlWriter xml)
        {
            Validate();
            if (!Enabled) return;
            xml.WriteStartElement("grindOptions");
            xml.WriteAttributeString("pattern", ((int)Pattern).ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("intensity", Intensity.ToString("R", CultureInfo.InvariantCulture));
            xml.WriteAttributeString("hz", Hertz.ToString("R", CultureInfo.InvariantCulture));
            xml.WriteAttributeString("depthWeighted", DepthWeighted ? "true" : "false");
            xml.WriteAttributeString("linkSkeleton", LinkSkeleton ? "true" : "false");
            xml.WriteAttributeString("softTissue", SoftTissue ? "true" : "false");
            xml.WriteAttributeString("mover", ((int)MoverMode).ToString(CultureInfo.InvariantCulture));
            xml.WriteEndElement();
        }

        public static ChapterGrindOptions Read(XmlElement node)
        {
            if (node == null) return null;
            var v = new ChapterGrindOptions();
            int p;
            if (node.HasAttribute("pattern") && int.TryParse(node.GetAttribute("pattern"), NumberStyles.Integer, CultureInfo.InvariantCulture, out p))
                v.Pattern = (StudioGrindPattern)p;
            double d;
            if (node.HasAttribute("intensity") && double.TryParse(node.GetAttribute("intensity"), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                v.Intensity = d;
            if (node.HasAttribute("hz") && double.TryParse(node.GetAttribute("hz"), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                v.Hertz = d;
            if (node.HasAttribute("depthWeighted")) v.DepthWeighted = node.GetAttribute("depthWeighted") != "false";
            if (node.HasAttribute("linkSkeleton")) v.LinkSkeleton = node.GetAttribute("linkSkeleton") != "false";
            if (node.HasAttribute("softTissue")) v.SoftTissue = node.GetAttribute("softTissue") != "false";
            int m;
            if (node.HasAttribute("mover") && int.TryParse(node.GetAttribute("mover"), NumberStyles.Integer, CultureInfo.InvariantCulture, out m))
                v.MoverMode = (ActiveMoverMode)m;
            v.Validate();
            return v;
        }
    }

    public struct StudioGrindFrame
    {
        public bool Active;
        public double DepthNorm;
        public double EffectiveWeight; // Intensity * depth gate [0..1]
        public double RelLat, RelFwd, RelAxial;
        public double RelYaw, RelRoll, RelPitch;
        public double RelYawLag, RelRollLag, RelPitchLag;
        public double AccAxial, AccFwd, AccLat;
    }

    // Pure mathematical core ported from MainGame's TimelineEngine + SR6MathManifoldEngine.
    // Deterministic in Timeline time `t` so live playback, 50Hz StepBake, and reverse skeleton
    // coupling stay 100% locked with zero drift.
    public static class StudioGrindEngine
    {
        private const double GoldenRatio = 1.6180339887498949;
        private const double Sqrt2 = 1.4142135623730951;
        private const double Sqrt3 = 1.7320508075688772;
        private const double HalfPi = 1.5707963267948966;

        // Direct penetrative openings: Vaginal (0), Anal (1), Oral (2)
        public static bool IsDirectInsertionOpening(int opening)
        {
            return opening == 0 || opening == 1 || opening == 2;
        }

        public static StudioGrindFrame Evaluate(double timeSec, double depthNorm, ChapterGrindOptions opt)
        {
            var res = new StudioGrindFrame { DepthNorm = Numbers.Clamp(depthNorm, 0.0, 1.0) };
            if (opt == null || !opt.Enabled || !Numbers.Finite(timeSec))
                return res;

            double intensity = Numbers.Clamp(opt.Intensity, 0.0, 1.0);
            double hz = Numbers.Clamp(opt.Hertz, 0.3, 3.5);
            double gate = opt.DepthWeighted ? SmoothStep(0.22, 0.80, res.DepthNorm) * 0.85 + 0.15 : 1.0;
            double eff = intensity * gate;
            if (eff <= 1e-4) return res;

            double omega = 2.0 * Math.PI * hz;
            double phi = timeSec * omega;
            double phiLag = Math.Max(0.0, timeSec - 0.10) * omega;

            EvalPattern(opt.Pattern, phi, out res.RelLat, out res.RelFwd, out res.RelAxial, out res.RelYaw, out res.RelRoll, out res.RelPitch);
            double dummyLat, dummyFwd, dummyAx;
            EvalPattern(opt.Pattern, phiLag, out dummyLat, out dummyFwd, out dummyAx, out res.RelYawLag, out res.RelRollLag, out res.RelPitchLag);

            // Second-derivative proxy for DynamicBone soft-tissue inertia
            double accScale = omega * omega * 0.08 * eff;
            res.AccAxial = -res.RelAxial * accScale;
            res.AccFwd = -res.RelFwd * accScale;
            res.AccLat = -res.RelLat * accScale;
            res.EffectiveWeight = eff;
            res.Active = true;
            return res;
        }

        // Additive 6-axis modulation onto physical axes[] (axes[0] = inward depth 0..1; axes[1..5] = L1,L2,R0,R1,R2 centered at 0.5)
        public static double[] ApplyToAxes(double[] axes, StudioGrindFrame gf)
        {
            if (axes == null || axes.Length != 6 || !gf.Active) return axes;
            var outAx = (double[])axes.Clone();
            double w = gf.EffectiveWeight;
            // Subtle micro-axial deep pulse on L0 so deep bottom-out breathes alive without distorting primary stroke
            outAx[0] = Numbers.Clamp(outAx[0] + gf.RelAxial * 0.055 * w, 0.0, 1.0);
            // Rich 5-DOF grinding superposition on L1/L2/R0/R1/R2
            outAx[1] = Numbers.Clamp(outAx[1] + gf.RelFwd * 0.30 * w, 0.02, 0.98);
            outAx[2] = Numbers.Clamp(outAx[2] + gf.RelLat * 0.30 * w, 0.02, 0.98);
            outAx[3] = Numbers.Clamp(outAx[3] + gf.RelYaw * 0.34 * w, 0.02, 0.98);
            outAx[4] = Numbers.Clamp(outAx[4] + gf.RelRoll * 0.32 * w, 0.02, 0.98);
            outAx[5] = Numbers.Clamp(outAx[5] + gf.RelPitch * 0.32 * w, 0.02, 0.98);
            return outAx;
        }

        private static void EvalPattern(StudioGrindPattern pattern, double phi,
            out double lat, out double fwd, out double axial, out double yaw, out double roll, out double pitch)
        {
            double s = Math.Sin(phi), c = Math.Cos(phi);
            double s2 = Math.Sin(2.0 * phi), c2 = Math.Cos(2.0 * phi);
            switch (pattern)
            {
                case StudioGrindPattern.Figure8:
                    lat = s; fwd = 0.85 * s2; axial = 0.30 * c2;
                    yaw = c; roll = s; pitch = 0.85 * s2;
                    break;
                case StudioGrindPattern.Corkscrew:
                    lat = 0.70 * s; fwd = 0.70 * c; axial = 0.45 * Math.Sin(phi * 0.5);
                    yaw = 1.00 * s; roll = 0.65 * c; pitch = 0.65 * s;
                    break;
                case StudioGrindPattern.RockFB:
                    lat = 0.25 * s; fwd = c; axial = 0.48 * c;
                    yaw = 0.30 * s; roll = 0.25 * s; pitch = 1.00 * c;
                    break;
                case StudioGrindPattern.LineFB:
                    lat = 0.0; fwd = s; axial = 0.28 * s;
                    yaw = 0.0; roll = 0.0; pitch = 1.00 * s;
                    break;
                case StudioGrindPattern.LineLR:
                    lat = s; fwd = 0.0; axial = 0.0;
                    yaw = 0.0; roll = 1.00 * s; pitch = 0.0;
                    break;
                case StudioGrindPattern.Lissajous6D:
                    // Non-repeating 6D Golden-Ratio Lissajous manifold from MainGame SR6MathManifoldEngine
                    lat = Math.Sin(phi * GoldenRatio);
                    fwd = Math.Cos(phi * Sqrt2 + 0.4);
                    axial = 0.42 * Math.Sin(phi * 0.5 + 0.8);
                    yaw = Math.Sin(phi * Sqrt3 + 1.1);
                    roll = Math.Sin(phi * GoldenRatio + 0.6);
                    pitch = Math.Cos(phi * HalfPi);
                    break;
                default: // Circle
                    lat = s; fwd = c; axial = 0.35 * c2;
                    yaw = 0.85 * Math.Sin(phi + 0.35); roll = s; pitch = c;
                    break;
            }
        }

        private static double SmoothStep(double edge0, double edge1, double x)
        {
            double t = Numbers.Clamp((x - edge0) / Math.Max(1e-6, edge1 - edge0), 0.0, 1.0);
            return t * t * (3.0 - 2.0 * t);
        }
    }
}
