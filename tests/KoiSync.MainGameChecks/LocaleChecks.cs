using KoiSync.MainGame;

internal static class LocaleChecks
{
    public static void Run(Action<bool,string> check)
    {
        foreach(var pair in new[]{("zh-CN","zh"),("zh-TW","zh-Hant"),("zh-HK","zh-Hant"),("zh-MO","zh-Hant"),("ru-RU","ru"),("ja-JP","ja"),("ko-KR","ko"),("en-US","en"),("de-DE","de"),("fr-FR","fr")})
            check(LocaleCatalog.Resolve(pair.Item1)==pair.Item2,"MainGame language resolution supports "+pair.Item1);
        check(LocaleCatalog.Keys.Count>=580,"MainGame locale catalog includes complete key set");
        foreach(string code in new[]{"zh-Hant","ru","ja","ko","de","fr"})
        foreach(string key in LocaleCatalog.Keys)
        {
            string value=LocaleCatalog.Text("简体",key,code);
            check(value.Length>0&&value!="简体","MainGame locale entry non-empty: "+code+" / "+key);
        }
        foreach(string code in new[]{"zh-Hant","ru","ja","ko","de","fr"})
        foreach(string key in new[]{"KoiSync Main","Device","Workbench","AI Hub","Settings","Current action","Calibration","Sync & feel","Save / Record","Live Chat","Manual Director","Autonomy & SR6 Lab","Virtual Pivot Traveling Cone","Variable-Pitch Dual Helix","6D Golden Lissajous Knot","Dual-Scale Beat Flutter","Bounded Smooth Chaos","Waiting for Main Game H-scene"})
            check(LocaleCatalog.Text("简体",key,code)!=key,"MainGame core UI keys have native translation: "+code+" / "+key);

        string prev=UiText.Language;
        try
        {
            UiText.Language="ja";
            check(ActionLabels.Actor(ActorSlot.Female0)=="女性 1","ActionLabels translates actor slots");
            check(UiText.TranslateComposite("正在连接 COM4…").Contains("接続中"),"Composite translator localizes DeviceHub status");
            check(UiText.TranslateComposite("均衡体感 · 1.5Hz · L0 10–80 · 绿区(150u/s)").Contains("バランス体感"),"Composite translator localizes FeelTranslator status");
            UiText.Language="en";
            check(UiText.T("预览等待绑定")=="Preview awaiting binding","UiText translates shared runtime strings");
            check(UiText.TranslateComposite("寸止制动中 · 剩 3.2s (已触发 1 次)").Contains("Edge brake active"),"Composite translator localizes DirectorModulator status");
        }
        finally
        {
            UiText.Language=prev;
        }
    }
}
