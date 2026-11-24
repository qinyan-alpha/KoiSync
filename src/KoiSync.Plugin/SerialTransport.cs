using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace KoiSync.Plugin
{
    // Koikatu's stripped Unity 5 System.dll does not include System.IO.Ports.SerialPort.
    // Use the OS API directly; no replacement System.dll or extra native DLL is needed.
    internal sealed class SerialTransport:ITransport
    {
        private static readonly IntPtr InvalidHandle=new IntPtr(-1);
        private IntPtr handle=InvalidHandle;
        private readonly string endpoint;
        private long nextCheck;
        internal static string[] GetPortNames()
        {
            const int capacity=65536;IntPtr buffer=Marshal.AllocHGlobal(capacity*2);string inventory;
            try
            {
                uint length=QueryDosDevice(null,buffer,capacity);
                if(length==0)throw new IOException("无法扫描串口 (Win32 "+Marshal.GetLastWin32Error()+")");
                inventory=Marshal.PtrToStringUni(buffer,(int)length);
            }
            finally{Marshal.FreeHGlobal(buffer);}
            var ports=new List<string>();
            foreach(string name in inventory.Split('\0'))
            {
                int number;if(name.StartsWith("COM",StringComparison.Ordinal)&&int.TryParse(name.Substring(3),NumberStyles.None,CultureInfo.InvariantCulture,out number)&&number>0)ports.Add(name);
            }
            ports.Sort((a,b)=>int.Parse(a.Substring(3),CultureInfo.InvariantCulture).CompareTo(int.Parse(b.Substring(3),CultureInfo.InvariantCulture)));
            return ports.ToArray();
        }
        internal static string PortPath(string value)
        {
            string name=(value??"").Trim().ToUpperInvariant();int number;
            if(!name.StartsWith("COM",StringComparison.Ordinal)||name.Length<=3||
                !int.TryParse(name.Substring(3),NumberStyles.None,CultureInfo.InvariantCulture,out number)||number<1)
                throw new ArgumentException("串口名称应为 COM4 这样的 COM 编号");
            return @"\\.\COM"+number.ToString(CultureInfo.InvariantCulture);
        }
        public SerialTransport(DeviceSettings settings)
        {
            string path=PortPath(settings.Endpoint);endpoint=settings.Endpoint;
            if(settings.Argument<1200||settings.Argument>2000000)throw new ArgumentException("串口波特率超出范围");
            handle=CreateFile(path,0xC0000000,0,IntPtr.Zero,3,0,IntPtr.Zero);
            if(handle==InvalidHandle)throw Error("打开串口");
            try
            {
                Dcb dcb=new Dcb{Length=(uint)Marshal.SizeOf(typeof(Dcb))};
                if(!GetCommState(handle,ref dcb))throw Error("读取串口参数");
                dcb.BaudRate=(uint)settings.Argument;dcb.Flags=1; // Binary, no flow control, DTR/RTS disabled.
                dcb.ByteSize=8;dcb.Parity=0;dcb.StopBits=0;dcb.XonChar=17;dcb.XoffChar=19;
                if(!SetCommState(handle,ref dcb))throw Error("设置串口参数");
                CommTimeouts timeouts=new CommTimeouts{ReadInterval=uint.MaxValue,WriteConstant=200};
                if(!SetCommTimeouts(handle,ref timeouts))throw Error("设置串口超时");
            }
            catch{Dispose();throw;}
        }
        private IOException Error(string operation)
        {
            int code=Marshal.GetLastWin32Error();
            string reason=code==5||code==32?"端口被其他程序占用或拒绝访问":code==2||code==3?"端口不存在或设备已拔出":new Win32Exception(code).Message;
            return new IOException(endpoint+" "+operation+"失败："+reason+" (Win32 "+code+")");
        }
        public void Send(string tcode,double l0)
        {
            if(handle==InvalidHandle)throw new ObjectDisposedException("SerialTransport");
            byte[] bytes=Encoding.ASCII.GetBytes(tcode);uint written;
            if(!WriteFile(handle,bytes,(uint)bytes.Length,out written,IntPtr.Zero))throw Error("写入串口");
            if(written!=bytes.Length)throw new IOException(endpoint+" 串口写入超时");
        }
        public void Stop(){Send("DSTOP\n",0);}
        public void Tick()
        {
            long now=Stopwatch.GetTimestamp();if(now<nextCheck)return;
            uint status;if(!GetCommModemStatus(handle,out status))throw Error("检查串口");
            nextCheck=now+Stopwatch.Frequency/2;
        }
        public void Dispose(){Release();GC.SuppressFinalize(this);}
        ~SerialTransport(){Release();}
        private void Release(){if(handle!=InvalidHandle){IntPtr old=handle;handle=InvalidHandle;CloseHandle(old);}}

        [StructLayout(LayoutKind.Sequential)]
        internal struct Dcb
        {
            public uint Length,BaudRate,Flags;
            public ushort Reserved,XonLim,XoffLim;
            public byte ByteSize,Parity,StopBits,XonChar,XoffChar,ErrorChar,EofChar,EvtChar;
            public ushort Reserved1;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct CommTimeouts{public uint ReadInterval,ReadMultiplier,ReadConstant,WriteMultiplier,WriteConstant;}
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="CreateFileW")]
        private static extern IntPtr CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="QueryDosDeviceW")]
        private static extern uint QueryDosDevice(string name,IntPtr target,int size);
        [DllImport("kernel32.dll",SetLastError=true)]
        private static extern bool GetCommState(IntPtr handle,ref Dcb dcb);
        [DllImport("kernel32.dll",SetLastError=true)]
        private static extern bool SetCommState(IntPtr handle,ref Dcb dcb);
        [DllImport("kernel32.dll",SetLastError=true)]
        private static extern bool SetCommTimeouts(IntPtr handle,ref CommTimeouts timeouts);
        [DllImport("kernel32.dll",SetLastError=true)]
        private static extern bool WriteFile(IntPtr handle,byte[] bytes,uint count,out uint written,IntPtr overlapped);
        [DllImport("kernel32.dll",SetLastError=true)]
        private static extern bool GetCommModemStatus(IntPtr handle,out uint status);
        [DllImport("kernel32.dll",SetLastError=true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
