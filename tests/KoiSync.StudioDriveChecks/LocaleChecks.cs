using KoiSync.Studio;

internal static class LocaleChecks
{
    public static void Run(Action<bool,string> check)
    {
        foreach(var pair in new[]{("zh-CN","zh"),("zh-TW","zh-Hant"),("zh-HK","zh-Hant"),("zh-MO","zh-Hant"),("ru-RU","ru"),("ja-JP","ja"),("ko-KR","ko"),("en-GB","en"),("de-DE","de"),("fr-FR","fr")})
            check(LocaleCatalog.Resolve(pair.Item1)==pair.Item2,"Language selection supports "+pair.Item1);
        foreach(string code in new[]{"zh-Hant","ru","ja","ko","de","fr"})
        foreach(string key in new[]{"Player usable length mm","Chapter interaction mm","Device L0 travel mm","Restore default length","Absolute","Soft","As faithful as possible","Apply lengths","Send current six axes to device","Selected chapter","Scripts / Record","Analysis / chapters","Left hand → vulva","Right hand → vulva","Reset finger length"})
        {
            string value=LocaleCatalog.Text("简体",key,code);
            check(value.Length>0&&value!=key&&value!="简体","Main mapping controls have a native translation: "+code+" / "+key);
        }
        foreach(string code in new[]{"zh-Hant","ru","ja","ko","de","fr"})
        foreach(string key in new[]{"Device","Workbench","Settings","Rename chapter","Delete selected chapter","Toggle device synchronization","Mouse side button 1","Expand workbench","Climax / ejaculation","Chapter type / effect","Normal chapter","Save chapter effect","Enable L0 vibration by default","Scene is loading…","Enter a seek time in seconds","Connecting ","Opened ","Connection timed out: ","Intiface connected · L0 position","Edits the playing chapter; unset chapters inherit this mode.\nManual: a shorter endpoint span increases L0 motion."})
            check(LocaleCatalog.Text("简体",key,code)!=key,"Workbench, device status and multiline entries are localized: "+code+" / "+key);
        check(LocaleCatalog.Keys.Count>=460,"Studio locale catalog includes complete key set");
        foreach(string code in new[]{"zh-Hant","ru","ja","ko","de","fr"})
        foreach(string key in LocaleCatalog.Keys)
        {
            string value=LocaleCatalog.Text("简体",key,code);
            check(value.Length>0&&value!="简体","Studio locale entry non-empty: "+code+" / "+key);
        }
        check(LocaleCatalog.Text("原文","Unknown diagnostic","ru")=="Unknown diagnostic","Untranslated diagnostics have an explicit English fallback");
    }
}
