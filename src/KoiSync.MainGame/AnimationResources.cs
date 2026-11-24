using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KoiSync.MainGame
{
    public static class AnimationResources
    {
        public static string Identity(string mode,int id,params string[] assets)
        {
            var parts=new string[assets.Length+1];parts[0]=mode+"_"+id.ToString(CultureInfo.InvariantCulture);
            for(int i=0;i<assets.Length;i++)parts[i+1]=(assets[i]??"").Replace('\\','/').ToLowerInvariant();
            return string.Join("|",parts);
        }
        // Unlisted assets must never share a native action's saved calibration.
        public static string UnknownKey(string resource)
        {
            using(var sha=SHA256.Create())
            {var bytes=sha.ComputeHash(Encoding.UTF8.GetBytes(resource));var key=new StringBuilder("unclassified_");for(int i=0;i<12;i++)key.Append(bytes[i].ToString("x2",CultureInfo.InvariantCulture));return key.ToString();}
        }
    }
}
