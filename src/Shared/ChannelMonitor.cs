using System;
using System.Globalization;

namespace KoiSync.Core
{
    public static class ChannelMonitor
    {
        // Player-facing scale only. Transport quantization remains in TCode.
        public static string PositionText(double value)
        {
            if(!Numbers.Finite(value))return "—";
            // Remove sub-millionth display-unit noise at a rounding boundary (e.g. a settled midpoint).
            string position=Math.Round(Math.Round(Numbers.Clamp(value,0,1)*999,6),MidpointRounding.AwayFromZero).ToString("000",CultureInfo.InvariantCulture);
            return position+(value<0?" ↓":value>1?" ↑":"");
        }
        public static string InputText(MotionPipeline pipeline,int axis)
        {double value;return TryNormalized(pipeline,axis,out value)?PositionText(value):"—";}
        public static string MappedText(MotionPipeline pipeline,int axis)
        {double value;return pipeline.TryGetMappedValue(axis,out value)?PositionText(value):"—";}
        public static bool TryNormalized(MotionPipeline pipeline,int axis,out double value)
        {
            value=double.NaN;double lo,hi;
            if(pipeline.NativeMode)return pipeline.TryGetNativeInput(axis,out value);
            if(!pipeline.TryGetCalibrationRange(axis,out lo,out hi)||!Numbers.Finite(pipeline.Raw[axis])||hi<=lo)return false;
            value=Numbers.Clamp((pipeline.Raw[axis]-lo)/(hi-lo),0,1);return true;
        }
        // Interpret the serialized command, including transport quantization.
        // This is command telemetry, not a measurement from the physical device.
        public static bool TryLastSent(string command,int axis,out double value)
        {
            value=double.NaN;if(axis<0||axis>=6||string.IsNullOrEmpty(command))return false;
            const string linear="LinearCmd L0=";
            if(command.StartsWith(linear,StringComparison.Ordinal))
            {
                double p;if(axis!=0||!double.TryParse(command.Substring(linear.Length),NumberStyles.Float,CultureInfo.InvariantCulture,out p)||!Numbers.Finite(p)||p<0||p>1)return false;
                value=p;return true;
            }
            foreach(string token in command.Trim().Split(' '))
            {
                if(!token.StartsWith(MotionPipeline.Axes[axis],StringComparison.Ordinal)||token.Length<6)continue;
                if(token.Length>6&&token[6]!='I'&&token[6]!='S')continue;
                int n;if(!int.TryParse(token.Substring(2,4),NumberStyles.None,CultureInfo.InvariantCulture,out n))return false;
                value=n/10000.0;return true;
            }
            return false;
        }
    }
}
