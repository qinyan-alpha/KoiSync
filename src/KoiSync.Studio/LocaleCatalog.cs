using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace KoiSync.Studio
{
    public static class LocaleCatalog
    {
        public static readonly string[] Codes={"zh","zh-Hant","en","ru","ja","ko","de","fr"};
        public static readonly string[] Names={"简体中文","繁體中文","English","Русский","日本語","한국어","Deutsch","Français"};
        private static readonly string[] Columns={"zh-Hant","ru","ja","ko","de","fr"};
        private static readonly string[] ShortCodes={"ru","ja","ko","de","fr"};
        private static readonly Dictionary<string,string[]> Rows=Read();
        public static bool HasKey(string english){return english!=null&&Rows.ContainsKey(english);}
        public static ICollection<string> Keys {get{return Rows.Keys;}}
        public static string Resolve(string code)
        {
            code=(code??"").ToLowerInvariant();
            if(code.StartsWith("zh"))return code.Contains("hant")||code.EndsWith("tw")||code.EndsWith("hk")||code.EndsWith("mo")?"zh-Hant":"zh";
            foreach(string value in ShortCodes)if(code.StartsWith(value))return value;
            return "en";
        }
        public static string Text(string chinese,string english,string code)
        {
            if(code=="zh")return chinese;
            int index=Array.IndexOf(Columns,code);
            string[] row;
            return index>=0&&Rows.TryGetValue(english,out row)&&row[index].Length>0?row[index]:english;
        }
        private static Dictionary<string,string[]> Read()
        {
            var rows=new Dictionary<string,string[]>();
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("KoiSync.Locale.tsv"))
            {
                if(stream==null)return rows;
                using(var reader=new StreamReader(stream,Encoding.UTF8))
                {
                    string line;while((line=reader.ReadLine())!=null)
                    {
                        if(line.Length==0||line.StartsWith("#"))continue;
                        var fields=line.Split('\t');if(fields.Length!=7)throw new FormatException("Invalid locale row: "+fields[0]);
                        string key=fields[0].Replace("\\n","\n");
                        var values=new string[6];
                        for(int i=0;i<6;i++)values[i]=fields[i+1].Replace("\\n","\n");
                        rows.Add(key,values);
                    }
                }
            }
            return rows;
        }
    }
}
