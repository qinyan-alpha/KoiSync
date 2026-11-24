using KoiSync.Core;

namespace KoiSync.Studio
{
    public enum AxisMappingMode { Absolute, Soft, Relative, Manual }

    public sealed class StudioDrive : SixAxisDrive
    {
        public void Update(double[] normalized, double seconds, AxisMappingMode mode)
        { base.Update(normalized, seconds, (DriveMappingMode)mode); }
    }
}
