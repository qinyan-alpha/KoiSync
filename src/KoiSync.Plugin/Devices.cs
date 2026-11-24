using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using KoiSync.Core;
using LitJson;
using WebSocket4Net;

namespace KoiSync.Plugin
{
    internal static class EmbeddedDependencies
    {
        public static void Initialize()
        {
            AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
            foreach(string name in new[]{"LitJSON","SuperSocket.ClientEngine","WebSocket4Net"}) Load(name);
        }
        private static Assembly Resolve(object sender,ResolveEventArgs e){return Load(new AssemblyName(e.Name).Name);}
        private static Assembly Load(string name)
        {
            if(name!="LitJSON"&&name!="SuperSocket.ClientEngine"&&name!="WebSocket4Net")return null;
            foreach(Assembly a in AppDomain.CurrentDomain.GetAssemblies())if(a.GetName().Name==name)return a;
            using(Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream("KoiSync.Dependencies."+name+".dll"))
            {if(s==null)return null;byte[] bytes=new byte[s.Length];int read=0;while(read<bytes.Length){int n=s.Read(bytes,read,bytes.Length-read);if(n==0)throw new EndOfStreamException();read+=n;}return Assembly.Load(bytes);}
        }
    }
    internal sealed class DeviceSettings
    {
        public int Transport,Argument=115200;
        public string Endpoint="COM3";
        public DeviceKind Kind;
    }
    internal interface ITransport:IDisposable
    {
        void Send(string tcode,double l0);
        void Stop();
        void Tick();
    }
    internal sealed class UdpTransport:ITransport
    {
        private readonly UdpClient socket=new UdpClient();
        public UdpTransport(DeviceSettings s){try{socket.Connect(s.Endpoint,s.Argument);}catch{socket.Close();throw;}}
        public void Send(string tcode,double l0){byte[] b=Encoding.ASCII.GetBytes(tcode);socket.Send(b,b.Length);}
        public void Stop(){Send("DSTOP\n",0);}
        public void Tick(){}
        public void Dispose(){socket.Close();}
    }
    internal sealed class IntifaceTransport:ITransport
    {
        private readonly WebSocket socket;
        private readonly ManualResetEvent opened=new ManualResetEvent(false),reply=new ManualResetEvent(false);
        private readonly object gate=new object();
        private bool disposed;
        private readonly Stopwatch clock=Stopwatch.StartNew();
        private readonly int device;
        private int sequence,waiting,pingMs;
        private double nextPing;
        private JsonData response;
        private Exception error;
        public IntifaceTransport(DeviceSettings settings)
        {
            device=settings.Argument;
            Uri url=new Uri(settings.Endpoint);
            if(url.Scheme!="ws" || !url.IsLoopback)throw new ArgumentException("当前 Intiface 适配使用本机 ws://127.0.0.1:12345");
            socket=new WebSocket(url.ToString());
            socket.Opened+=(s,e)=>opened.Set();
            socket.Closed+=(s,e)=>Fail(new IOException("Intiface 已断开"));
            socket.Error+=(s,e)=>Fail(e.Exception??new IOException("Intiface 通信错误"));
            socket.MessageReceived+=(s,e)=>OnMessage(e.Message);
            try
            {
                socket.Open();if(!opened.WaitOne(3000,false))throw new IOException("Intiface 连接超时");
                CheckError();
                JsonData info=Request("RequestServerInfo","\"ClientName\":\"KoiSync\",\"MessageVersion\":3");
                if(Has(info,"MaxPingTime"))pingMs=(int)info["MaxPingTime"];
                JsonData list=Request("RequestDeviceList","");
                JsonData selected=null;StringBuilder available=new StringBuilder();
                foreach(JsonData entry in list["Devices"])
                {
                    int index=(int)entry["DeviceIndex"];available.Append(index).Append(' ').Append((string)entry["DeviceName"]).Append("; ");
                    if(index==device)selected=entry;
                }
                if(selected==null)throw new IOException("请先在 Central 连接设备，索引可选："+available);
                JsonData messages=selected["DeviceMessages"];
                bool hasLinear=false;
                if(messages!=null&&messages.IsObject&&((IDictionary)messages).Contains("LinearCmd")&&messages["LinearCmd"]!=null)
                {
                    JsonData lc=messages["LinearCmd"];
                    // Buttplug v3 sends LinearCmd as a JSON array of feature descriptors; v2 used an object with FeatureCount.
                    if(lc.IsArray&&lc.Count>0)hasLinear=true;
                    else if(Has(lc,"FeatureCount")&&(int)lc["FeatureCount"]>=1)hasLinear=true;
                }
                if(!hasLinear)
                    throw new IOException("该设备没有声明 LinearCmd 位置通道");
                nextPing=clock.Elapsed.TotalMilliseconds+Math.Max(10,pingMs/2);
            }
            catch{Dispose();throw;}
        }
        private static bool Has(JsonData value,string key){return value!=null&&value.IsObject&&((IDictionary)value).Contains(key);}
        private void Fail(Exception ex){lock(gate){if(disposed)return;error=ex;reply.Set();opened.Set();}}
        private void CheckError(){lock(gate){if(error!=null)throw new IOException(error.Message,error);}}
        private JsonData Request(string type,string fields)
        {
            CheckError();int id=++sequence;
            lock(gate){waiting=id;response=null;reply.Reset();}
            socket.Send("[{\""+type+"\":{\"Id\":"+id+(fields.Length>0?",":"")+fields+"}}]");
            if(!reply.WaitOne(2000,false))throw new IOException("Intiface 响应超时: "+type);
            CheckError();lock(gate){if(response==null)throw new IOException("Intiface 未返回响应");return response;}
        }
        private void OnMessage(string text)
        {
            try
            {
                JsonData root=JsonMapper.ToObject(text);
                foreach(JsonData item in root)
                foreach(DictionaryEntry property in (IDictionary)item)
                {
                    string type=(string)property.Key;JsonData body=(JsonData)property.Value;
                    if(type=="DeviceRemoved"&&(int)body["DeviceIndex"]==device){Fail(new IOException("设备已从 Intiface 断开"));return;}
                    if(Has(body,"Id"))lock(gate)
                    {
                        if((int)body["Id"]!=waiting)continue;
                        if(type=="Error")error=new IOException((string)body["ErrorMessage"]);
                        else response=body;
                        reply.Set();
                    }
                }
            }
            catch(Exception ex){Fail(ex);}
        }
        public void Send(string tcode,double l0)
        {
            Request("LinearCmd","\"DeviceIndex\":"+device+",\"Vectors\":[{\"Index\":0,\"Duration\":60,\"Position\":"+
                Numbers.Clamp(l0,0,1).ToString("0.0000",CultureInfo.InvariantCulture)+"}]");
        }
        public void Stop(){Request("StopDeviceCmd","\"DeviceIndex\":"+device);}
        public void Tick()
        {
            CheckError();if(pingMs>0&&clock.Elapsed.TotalMilliseconds>=nextPing)
            {Request("Ping","");nextPing=clock.Elapsed.TotalMilliseconds+Math.Max(10,pingMs/2);}
        }
        public void Dispose()
        {
            lock(gate){if(disposed)return;disposed=true;}
            try{socket.Close();}catch{}
            try{socket.Dispose();}catch{}
            lock(gate)
            {
                try{opened.Close();}catch{}
                try{reply.Close();}catch{}
            }
        }
    }
    internal sealed class DeviceTelemetry
    {
        public bool Connected,OutputRequested;
        public string LastMovementCommand="";
        public double SecondsSinceMovement=double.NaN;
        public double[] LastMovementValues;
    }
    internal sealed class DeviceHub:IDisposable
    {
        private readonly object gate=new object();
        private readonly Thread worker;
        private readonly Timer watchdog;
        private readonly Func<DeviceSettings,ITransport> openTransport;
        private readonly Action<string> log;
        private readonly double connectTimeout;
        private volatile bool running=true;
        private int generation;
        private DeviceSettings desired;
        private Command latest;
        private readonly MotionDelayBuffer motionBuffer=new MotionDelayBuffer();
        private double delaySeconds;
        public double DelaySeconds
        {
            get{lock(gate)return delaySeconds;}
            set{lock(gate)delaySeconds=Numbers.Finite(value)?Math.Max(0,value):0;}
        }
        private string lastMovementCommand="";
        private long lastMovementAt;
        private double[] lastMovementValues;
        private long connectStarted;
        public volatile bool Connected,Connecting;
        public volatile string Status="未连接",LastCommand="设备输出：未启用";
        public bool LinearOnly {get;private set;}
        public DeviceTelemetry GetTelemetry()
        {
            lock(gate)
            {
                long now=Stopwatch.GetTimestamp();
                return new DeviceTelemetry{Connected=Connected,OutputRequested=Connected&&latest!=null&&(now-latest.Timestamp)/(double)Stopwatch.Frequency<MotionPipeline.FreshnessSeconds,
                    LastMovementCommand=lastMovementCommand,LastMovementValues=lastMovementValues==null?null:(double[])lastMovementValues.Clone(),SecondsSinceMovement=lastMovementAt==0?double.NaN:(now-lastMovementAt)/(double)Stopwatch.Frequency};
            }
        }
        private sealed class Command
        {
            public string Text;public double Position;public long Timestamp;
            public AxisOscillation Pulse;public double Timeline,Rate;public double[] Values;public bool[] Enabled;public DeviceKind Kind;
        }
        public DeviceHub():this(null,null,8){}
        public DeviceHub(Action<string> logger):this(null,logger,8){}
        internal DeviceHub(Func<DeviceSettings,ITransport> factory,Action<string> logger,double timeoutSeconds)
        {
            openTransport=factory??OpenTransport;log=logger;connectTimeout=timeoutSeconds;
            worker=new Thread(WorkEntry){IsBackground=true,Name="KoiSync.Device"};worker.Start();
            watchdog=new Timer(CheckConnectionTimeout,null,100,100);
        }
        private void Log(string message){if(log!=null)try{log(message);}catch{}}
        public void Connect(DeviceSettings settings)
        {
            if(settings.Kind==DeviceKind.Handy&&settings.Transport!=2)throw new ArgumentException("Handy 请使用 Intiface Central");
            lock(gate)
            {
                if(!running)throw new ObjectDisposedException("DeviceHub");
                if(!worker.IsAlive)throw new IOException("通信线程已退出，请查看 KoiSync 日志并重新启动游戏");
                if(Connecting)throw new InvalidOperationException("正在连接，请先取消当前连接");
                desired=new DeviceSettings{Transport=settings.Transport,Endpoint=settings.Endpoint,Argument=settings.Argument,Kind=settings.Kind};
                latest=null;lastMovementCommand="";lastMovementAt=0;lastMovementValues=null;generation++;Connected=false;Connecting=true;LinearOnly=false;
                connectStarted=Stopwatch.GetTimestamp();Status="正在连接 "+settings.Endpoint+"…";
            }
            Log("连接请求："+settings.Endpoint+" / "+settings.Argument+" / transport="+settings.Transport);
        }
        public void Disconnect()
        {
            lock(gate){Status=Connecting?"已取消连接":"已断开";desired=null;latest=null;generation++;Connected=false;Connecting=false;LinearOnly=false;}
            Log(Status);
        }
        private void CheckConnectionTimeout(object state)
        {
            string message=null;
            lock(gate)
            {
                if(!running||!Connecting||(Stopwatch.GetTimestamp()-connectStarted)/(double)Stopwatch.Frequency<connectTimeout)return;
                message="连接超时："+desired.Endpoint+"，请检查端口及设备占用";
                desired=null;latest=null;generation++;Connected=false;Connecting=false;Status=message;
            }
            Log(message);
        }
        public void Stop(){lock(gate){latest=null;motionBuffer.Clear();}LastCommand="设备输出：已停止";}
        public void Offer(double[] values,bool[] enabled,DeviceKind kind)
        {Offer(values,enabled,kind,null,0,0);}
        public void Offer(double[] values,bool[] enabled,DeviceKind kind,AxisOscillation pulse,double timeline,double rate)
        {
            lock(gate)
            {
                if(!Connected||!enabled[0]&&LinearOnly){latest=null;return;}
                string text=LinearOnly?"LinearCmd L0="+Numbers.Clamp(values[0],0,1).ToString("F4",CultureInfo.InvariantCulture):TCode.Encode(values,enabled,kind,20);
                if(text.Length==0){latest=null;return;}
                if(latest==null)motionBuffer.Clear();
                latest=new Command{Text=text,Position=values[0],Timestamp=Stopwatch.GetTimestamp(),Pulse=enabled[0]?pulse:null,
                    Timeline=timeline,Rate=Numbers.Finite(rate)?Math.Max(0,rate):0,Values=(double[])values.Clone(),Enabled=(bool[])enabled.Clone(),Kind=kind};
                if(delaySeconds>0)motionBuffer.Add(latest.Timestamp/(double)Stopwatch.Frequency,timeline,latest.Rate,values,latest.Pulse);
            }
        }
        private void WorkEntry()
        {
            // A missing runtime type can fail JIT compilation before Work's own try block runs.
            try{Work();}
            catch(Exception ex)
            {
                lock(gate){Connected=false;Connecting=false;desired=null;latest=null;Status="通信线程错误："+ex.GetType().Name+" · "+ex.Message;}
                Log(Status+Environment.NewLine+ex);
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ITransport OpenTransport(DeviceSettings config)
        {
            switch(config.Transport)
            {
                case 0:return OpenSerial(config);
                case 1:return OpenUdp(config);
                case 2:return OpenIntiface(config);
                default:throw new ArgumentException("不支持的连接方式");
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ITransport OpenSerial(DeviceSettings config){return new SerialTransport(config);}
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ITransport OpenUdp(DeviceSettings config){return new UdpTransport(config);}
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ITransport OpenIntiface(DeviceSettings config){return new IntifaceTransport(config);}
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Work()
        {
            ITransport transport=null;int current=-1;bool moving=false;
            while(running)
            {
                DeviceSettings config;Command command;int version;
                lock(gate){config=desired;command=latest;version=generation;}
                try
                {
                    if(current!=version)
                    {
                        Close(transport,moving);transport=null;moving=false;current=version;
                        if(config!=null)
                        {
                            lock(gate){if(version!=generation||!running)continue;}
                            transport=openTransport(config);
                            bool accepted;
                            lock(gate)
                            {
                                accepted=version==generation&&running;
                                if(accepted){Connected=true;Connecting=false;LinearOnly=config.Transport==2;Status=LinearOnly?"Intiface 已连接 · L0 位置通道":"已打开 "+config.Endpoint+(config.Transport==1?" · UDP 无设备应答":" · "+config.Argument+" baud");}
                            }
                            if(!accepted){Close(transport,false);transport=null;continue;}
                            Log(Status);
                        }
                    }
                    if(transport!=null)
                    {
                        double activeDelay;
                        lock(gate){command=version==generation?latest:null;activeDelay=delaySeconds;}
                        bool fresh=command!=null&&(Stopwatch.GetTimestamp()-command.Timestamp)/(double)Stopwatch.Frequency<MotionPipeline.FreshnessSeconds;
                        if(fresh)
                        {
                            string text=command.Text;double position=command.Position;double[] sentValues=command.Values;
                            bool readyToSend=true;
                            if(activeDelay>0)
                            {
                                double[] delayed;bool oscillating;
                                lock(gate){delayed=latest==null?null:motionBuffer.Read(Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency,activeDelay,out oscillating);}
                                if(delayed==null)readyToSend=false;
                                else{sentValues=delayed;position=delayed[0];text=LinearOnly?"LinearCmd L0="+position.ToString("F4",CultureInfo.InvariantCulture):TCode.Encode(delayed,command.Enabled,command.Kind,10);}
                            }
                            else if(command.Pulse!=null)
                            {
                                double elapsed=(Stopwatch.GetTimestamp()-command.Timestamp)/(double)Stopwatch.Frequency;
                                position=command.Pulse.Value(command.Timeline+elapsed*command.Rate);
                                var values=(double[])command.Values.Clone();values[0]=position;sentValues=values;
                                text=LinearOnly?"LinearCmd L0="+position.ToString("F4",CultureInfo.InvariantCulture):TCode.Encode(values,command.Enabled,command.Kind,10);
                            }
                            if(readyToSend)
                            {
                                moving=true;transport.Send(text,position);
                                lock(gate){if(version==generation){lastMovementCommand=text.Trim();lastMovementValues=sentValues;lastMovementAt=Stopwatch.GetTimestamp();LastCommand=lastMovementCommand;}}
                            }
                        }
                        else if(moving){moving=false;transport.Stop();LastCommand="已发送停止指令";}
                        transport.Tick();
                    }
                }
                catch(Exception ex)
                {
                    lock(gate){if(version==generation&&running){Connected=false;Connecting=false;desired=null;latest=null;Status="连接错误: "+ex.Message;}}
                    Log("连接失败 "+(config==null?"":config.Endpoint)+": "+ex);
                    Close(transport,moving);transport=null;moving=false;
                }
                Thread.Sleep(config!=null&&config.Transport==2?50:DelaySeconds>0||command!=null&&command.Pulse!=null?10:20);
            }
            Close(transport,moving);
        }
        private static void Close(ITransport transport,bool moving)
        {if(transport==null)return;if(moving)try{transport.Stop();}catch{}try{transport.Dispose();}catch{}}
        public void Dispose()
        {
            lock(gate){running=false;desired=null;latest=null;generation++;Connected=false;Connecting=false;Status="已断开";}
            watchdog.Dispose();if(worker.IsAlive)worker.Join(500);
        }
    }
}
