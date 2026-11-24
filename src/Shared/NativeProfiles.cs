using System;
using System.Collections.Generic;
using System.IO;

namespace KoiSync.Core
{
    public enum NativeTarget { AUTO,VAGINAL,ANAL,ORAL,BREASTS,LEFTHAND,RIGHTHAND,LEFTFOOT,RIGHTFOOT,INTERCRURAL,BOTH_FEET,VAGINALSWAP,ORALSWAP,BREASTSWAP,LEFTHANDSWAP,RIGHTHANDSWAP,INTERCRURALSWAP,LEFTFOOTSWAP,RIGHTFOOTSWAP }
    public static class NativeProfiles
    {
        public static Dictionary<string,NativeTarget> Read(TextReader reader)
        {
            var result=new Dictionary<string,NativeTarget>(StringComparer.Ordinal);string line;int number=0;
            while((line=reader.ReadLine())!=null)
            {
                number++;line=line.Trim().TrimStart('\uFEFF');if(line.Length==0||line.StartsWith("#"))continue;
                int comma=line.LastIndexOf(',');NativeTarget target;
                if(comma<=0)throw new FormatException("Invalid animation mapping at line "+number);
                try{target=(NativeTarget)Enum.Parse(typeof(NativeTarget),line.Substring(comma+1).Trim(),true);}
                catch(ArgumentException){throw new FormatException("Invalid animation target at line "+number);}
                if(!Enum.IsDefined(typeof(NativeTarget),target)||target==NativeTarget.AUTO||target==NativeTarget.BOTH_FEET)throw new FormatException("Unsupported animation target at line "+number);
                result[line.Substring(0,comma).Trim()]=target;
            }
            return result;
        }
        public static NativeTarget Primary(NativeTarget target){string name=target.ToString();return name.EndsWith("SWAP",StringComparison.Ordinal)?(NativeTarget)Enum.Parse(typeof(NativeTarget),name.Substring(0,name.Length-4)):target;}
        public static int ActorIndex(NativeTarget target){return target.ToString().EndsWith("SWAP",StringComparison.Ordinal)?1:0;}
        public static int Stage(string state)
        {
            if(state==null)return -1;
            if(state.StartsWith("A_",StringComparison.Ordinal))state=state.Substring(2);
            switch(state)
            {
                case "WLoop":return 0;
                case "MLoop":case "SLoop":return 1;
                case "OLoop":
                case "IN_Start":case "IN_Loop":
                case "M_IN_Start":case "M_IN_Loop":case "M_OUT_Start":case "M_OUT_Loop":
                case "WF_IN_Start":case "WF_IN_Loop":case "WS_IN_Start":case "WS_IN_Loop":case "WS_IN_A":
                case "SF_IN_Start":case "SF_IN_Loop":case "SS_IN_Start":case "SS_IN_Loop":case "SS_IN_A":
                case "IN_A":case "OUT_A":return 2;
                default:return -1;
            }
        }
        public static bool PhaseActive(string state,float speed){int stage=Stage(state);return stage>=0&&(stage==2||speed>=1);}
    }
}
