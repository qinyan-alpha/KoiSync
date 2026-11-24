using KoiSync.Core;

namespace KoiSync.MainGame
{
    public enum AxisMappingMode { Absolute, Soft, Relative, Manual }

    public sealed class GameDrive : SixAxisDrive
    {
        public void Update(double[] normalized, double seconds, AxisMappingMode mode)
        { base.Update(normalized, seconds, (DriveMappingMode)mode); }
    }
}
