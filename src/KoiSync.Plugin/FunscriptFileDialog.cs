using System;
using System.Runtime.InteropServices;

namespace KoiSync.Plugin
{
    internal static class FunscriptFileDialog
    {
        // Exact Win32 OPENFILENAMEW ABI without StringBuilder fields (Unity 5.6 / Mono 2.x
        // throws ArgumentException if StringBuilder appears inside a marshaled struct).
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        internal struct OpenFileName
        {
            internal int Size;
            internal IntPtr Owner,Instance;
            internal IntPtr Filter;
            internal IntPtr CustomFilter;
            internal int MaxCustomFilter,FilterIndex;
            internal IntPtr File;
            internal int MaxFile;
            internal IntPtr FileTitle;
            internal int MaxFileTitle;
            internal string InitialDirectory,Title;
            internal int Flags;
            internal short FileOffset,ExtensionOffset;
            internal string DefaultExtension;
            internal IntPtr CustomData,Hook;
            internal string TemplateName;
            internal IntPtr Reserved;
            internal int ReservedSize,FlagsEx;
        }
        [DllImport("comdlg32.dll",CharSet=CharSet.Unicode,EntryPoint="GetOpenFileNameW")]
        [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool Open(ref OpenFileName value);
        [DllImport("comdlg32.dll")]
        private static extern int CommDlgExtendedError();
        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        private static IntPtr AllocWideWithNulls(string text)
        {
            char[] chars=(text+"\0").ToCharArray();
            IntPtr ptr=Marshal.AllocHGlobal(chars.Length*2);
            Marshal.Copy(chars,0,ptr,chars.Length);
            return ptr;
        }

        private static string RunDialog(string initialDirectory,string filter,string title,string defaultExt)
        {
            const int maxChars=32768;
            IntPtr fileBuf=Marshal.AllocHGlobal(maxChars*2);
            IntPtr filterBuf=IntPtr.Zero;
            try
            {
                Marshal.WriteInt16(fileBuf,0);
                filterBuf=AllocWideWithNulls(filter);
                var value=new OpenFileName
                {
                    Size=Marshal.SizeOf(typeof(OpenFileName)),
                    Owner=GetActiveWindow(),
                    Filter=filterBuf,
                    FilterIndex=1,
                    File=fileBuf,
                    MaxFile=maxChars,
                    InitialDirectory=initialDirectory,
                    Title=title,
                    DefaultExtension=defaultExt,
                    Flags=0x80000|0x1000|0x800|0x8
                };
                if(Open(ref value))return Marshal.PtrToStringUni(fileBuf);
                int error=CommDlgExtendedError();
                if(error!=0)throw new InvalidOperationException("文件选择失败："+error);
                return null;
            }
            finally
            {
                if(filterBuf!=IntPtr.Zero)Marshal.FreeHGlobal(filterBuf);
                Marshal.FreeHGlobal(fileBuf);
            }
        }

        internal static string Select(string initialDirectory)
        {
            return RunDialog(
                initialDirectory,
                "Funscript 脚本 (*.funscript)\0*.funscript\0JSON 脚本 (*.json)\0*.json\0\0",
                "选择与 Timeline 同步的 funscript",
                "funscript");
        }

        internal static string SelectCharacterCard(string initialDirectory)
        {
            return RunDialog(
                initialDirectory,
                "女性人物卡 (*.png)\0*.png\0PNG 文件 (*.png)\0*.png\0\0",
                "选择要用于替换的女性人物卡",
                "png");
        }
    }
}
