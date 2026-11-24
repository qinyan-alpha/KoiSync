using System.Collections.Generic;

namespace KoiSync.MainGame
{
    internal static class UiText
    {
        internal static string Language="zh";
        internal static bool English {get{return Language!="zh"&&Language!="zh-Hant"&&Language!="ja"&&Language!="ko";}set{Language=value?"en":"zh";}}

        private static readonly Dictionary<string,string> entries=new Dictionary<string,string>
        {
            {"预览等待绑定","Preview awaiting binding"},
            {"预览等待有效姿态","Preview awaiting valid pose"},
            {"模型预览已隐藏","Model preview hidden"},
            {"缺少 KoiSync 预览 zipmod","KoiSync preview zipmod is missing"},
            {"预览模型缺少锚点或网格","Preview model lacks anchors or mesh"},
            {"预览模型长度无效","Invalid preview model length"},
            {"预览材质不可用","Preview material is unavailable"},
            {"等待本体 H 场景","Waiting for Main Game H-scene"},
            {"等待动作载入","Waiting for animation to load"},
            {"等待有效动画时钟","Waiting for valid animation clock"},
            {"动作未分类：请在工作台指定驱动来源","Action unclassified: assign a motion source in Workbench"},
            {"旁观入场 / 离场，等待动作阶段","Peeping entry / exit; waiting for action phase"},
            {"此动作配置为静止","This action is configured as stationary"},
            {"等待动作开始后捕获固定标尺","Waiting for action start to capture reference span"},
            {"等待目标角色最终动画姿态","Waiting for target actor animation pose"},
            {"等待来源角色最终动画姿态","Waiting for source actor animation pose"},
            {"等待固定原生动画道具 p_item_dildo","Waiting for native animated prop p_item_dildo"},
            {"等待来源角色动画","Waiting for source actor animation"},
            {"等待原生交互物件（当前未操作对应部位）","Waiting for native interaction item (target zone inactive)"},
            {"来源缺少固定驱动节点","Source lacks the required driving node"},
            {"目标缺少实际入口骨骼","Target lacks the opening bone"},
            {"目标缺少完整入口标记","Target lacks complete opening markers"},
            {"当前动作绑定正在切换","Action binding is switching"},
            {"动画资源尚未分类，请指定来源和部位","Animation resource unclassified; assign source and target zone"},
            {"动作尚未分类，请指定来源和部位","Action unclassified; assign source and target zone"},
            {"固定扩展动作表","Extension action table"},
            {"游戏插入类型元数据","Game penetration metadata"},
            {"原味几何","Raw geometry"},
            {"原味几何 · 1:1 物理映射","Raw geometry · 1:1 physical mapping"},
            {"非插入静默 · 待机等待插入","Non-shaft silent · standby until insertion"},
            {"等待接触 · 保持退出位","Waiting for contact · holding outer park"},
            {"静止阶段 · 保持几何原值","Idle phase · holding raw geometry"},
            {"时间轴变式未激活","Timeline variant inactive"},
            {"虚拟支点行波锥面","Virtual Pivot Traveling Cone"},
            {"变螺距双螺旋剪切","Variable-Pitch Dual Helix"},
            {"6D黄金比例利萨如纽结","6D Golden Lissajous Knot"},
            {"双尺度拍频微颤共振","Dual-Scale Beat Flutter"},
            {"有界平滑混沌吸引子","Bounded Smooth Chaos"},
            {"关闭","Off"},
            {"未连接","Not connected"},
            {"已断开","Disconnected"},
            {"已取消连接","Connection cancelled"},
        };

        internal static IEnumerable<string> AllEnglishValues {get{return entries.Values;}}

        internal static string L(string chinese,string english)
        {
            return LocaleCatalog.Text(chinese,english,Language);
        }

        internal static string T(string text)
        {
            if(string.IsNullOrEmpty(text))return text??"";
            string english;
            return LocaleCatalog.Text(text,entries.TryGetValue(text,out english)?english:text,Language);
        }

        // Translates composite runtime strings produced by Shared/Transport helpers
        // without changing their internal Chinese strings used by core tests.
        internal static string TranslateComposite(string text)
        {
            if(string.IsNullOrEmpty(text)||Language=="zh")return text??"";
            string exact;
            if(entries.TryGetValue(text,out exact))return LocaleCatalog.Text(text,exact,Language);

            return text
                // DeviceHub.Status patterns
                .Replace("正在连接 ",L("正在连接 ","Connecting "))
                .Replace("已打开 ",L("已打开 ","Opened "))
                .Replace("连接错误: ",L("连接错误: ","Connection error: "))
                .Replace("通信线程错误：",L("通信线程错误：","Device worker error: "))
                .Replace("连接超时：",L("连接超时：","Connection timed out: "))
                .Replace("，请检查端口及设备占用",L("，请检查端口及设备占用","; check the port and device ownership"))
                .Replace("Intiface 已连接 · L0 位置通道",L("Intiface 已连接 · L0 位置通道","Intiface connected · L0 position"))
                .Replace("UDP 无设备应答",L("UDP 无设备应答","UDP has no device acknowledgement"))
                .Replace("Intiface 已断开",L("Intiface 已断开","Intiface disconnected"))
                .Replace("Intiface 通信错误",L("Intiface 通信错误","Intiface transport error"))
                .Replace("Intiface 连接超时",L("Intiface 连接超时","Intiface connection timed out"))
                .Replace("设备已从 Intiface 断开",L("设备已从 Intiface 断开","Device disconnected from Intiface"))
                .Replace("Handy 请使用 Intiface Central",L("Handy 请使用 Intiface Central","Use Intiface Central for The Handy"))
                // FeelTranslator.Status patterns
                .Replace("拔出保护 · ",L("拔出保护 · ","Pull-out guard · "))
                .Replace("前戏偏强",L("前戏偏强","Brisk foreplay"))
                .Replace("前戏柔抚",L("前戏柔抚","Gentle foreplay"))
                .Replace("非插入强爽",L("非插入强爽","Intense non-shaft"))
                .Replace("非插入全幅",L("非插入全幅","Full non-shaft"))
                .Replace("强爽激进",L("强爽激进","Intense feel"))
                .Replace("均衡体感",L("均衡体感","Balanced feel"))
                .Replace("深红(",L("深红(","Crimson("))
                .Replace("红区(",L("红区(","Red("))
                .Replace("橙区(",L("橙区(","Orange("))
                .Replace("黄区(",L("黄区(","Yellow("))
                .Replace("绿区(",L("绿区(","Green("))
                .Replace("蓝区(",L("蓝区(","Blue("))
                .Replace("静止(",L("静止(","Idle("))
                // DirectorModulator & TimelineEngine status patterns
                .Replace("寸止制动中 · 剩 ",L("寸止制动中 · 剩 ","Edge brake active · "))
                .Replace(" (已触发 ",L(" (已触发 "," (triggered "))
                .Replace(" 次)",L(" 次)","×)"))
                .Replace("正在平滑交还游戏同步…",L("正在平滑交还游戏同步…","Blending back to game sync…"))
                .Replace("待机 · 防射守护 ",L("待机 · 防射守护 ","Standby · Edge guard "))
                .Replace("待机 · 正常跟随游戏同步",L("待机 · 正常跟随游戏同步","Standby · Following game sync"))
                .Replace("AI 悬停挑逗 · ",L("AI 悬停挑逗 · ","AI hold tease · "))
                .Replace("AI 行程接管 · ",L("AI 行程接管 · ","AI stroke override · "))
                .Replace("(剩 ",L("(剩 ","(rem "))
                .Replace("剧本[",L("剧本[","Seq["))
                .Replace("变式[",L("变式[","Variant["))
                .Replace("(循环中)",L("(循环中)","(looping)"))
                .Replace("步骤 ",L("步骤 ","Step "))
                .Replace("手控微调",L("手控微调","Manual tune"))
                .Replace("1浅1深",L("1浅1深","1 Shallow 1 Deep"))
                .Replace("定深前后直线摆腰",L("定深前后直线摆腰","Fixed-depth F/B Sway"))
                .Replace("虚拟支点行波锥面",L("虚拟支点行波锥面","Virtual Pivot Traveling Cone"))
                .Replace("变螺距双螺旋剪切",L("变螺距双螺旋剪切","Variable-Pitch Dual Helix"))
                .Replace("6D黄金比例利萨如纽结",L("6D黄金比例利萨如纽结","6D Golden Lissajous Knot"))
                .Replace("双尺度拍频微颤共振",L("双尺度拍频微颤共振","Dual-Scale Beat Flutter"))
                .Replace("有界平滑混沌吸引子",L("有界平滑混沌吸引子","Bounded Smooth Chaos"))
                .Replace(" · 过渡中",L(" · 过渡中"," · Transitioning"));
        }
    }
}
