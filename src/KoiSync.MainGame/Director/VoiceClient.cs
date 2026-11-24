using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using LitJson;

namespace KoiSync.MainGame
{
    internal sealed class VoiceChatEntry
    {
        public string Role;
        public string Text;
        public string Meta;
    }

    internal sealed class VoiceStatusSnapshot
    {
        public bool Connected;
        public string Version = "";
        public string Language = "zh";
        public string Provider = "deepseek";
        public string Model = "deepseek-chat";
        public string BaseUrl = "";
        public string UserTitle = "主人";
        public string AiSelfRef = "人家";
        public bool HasApiKey;
        public string ApiKeyMasked = "";
        public string ActiveVoiceModel = "builtin:zh_serena";
        public string TtsProvider = "local";
        public string AsrEngine = "gpu_turbo";
        public string AsrModel = "";
        public bool VoiceInputEnabled = true;
        public bool SilentMode;
        public bool BargeInEnabled = true;
        public bool TtsReady;
        public bool AsrReady;
        public bool IsSpeaking;
        public bool Recording;
        public bool FreeMicEnabled;
        public float FreeMicThresholdDb = -34f;
        public bool ProactiveEnabled = true;
        public float ProactiveIdleSec = 600f;
        public float VoiceGain = 0.88f;
        public bool DuckNativeVoice;
        public int HistoryTurns;
        public double CacheHitRate;
        public double EstCostRmb;
        public string LastReply = "";
        public string LastUserText = "";
        public string LastActionSummary = "";
        public string LastError = "";
        public readonly List<string> Memories = new List<string>();
    }

    // Separate class so LitJSON is never referenced in Plugin's class metadata before EmbeddedDependencies.Initialize().
    internal sealed class VoiceClient
    {
        private readonly string baseUrl;
        private readonly object sync = new object();
        private VoiceStatusSnapshot snapshot = new VoiceStatusSnapshot();
        private readonly List<VoiceChatEntry> transcript = new List<VoiceChatEntry>();
        private volatile bool busy;
        private volatile bool polling;
        private string lastSeenProactiveReply = "";
        private string lastSeenAnyReply = "";

        static VoiceClient()
        {
            try
            {
                if (ServicePointManager.DefaultConnectionLimit < 16)
                    ServicePointManager.DefaultConnectionLimit = 16;
            }
            catch { }
        }

        internal VoiceClient(string url)
        {
            baseUrl = (url ?? "http://127.0.0.1:41295").TrimEnd('/');
        }

        internal bool Busy { get { return busy; } }

        internal VoiceStatusSnapshot GetSnapshot()
        {
            lock (sync) return snapshot;
        }

        internal VoiceChatEntry[] GetTranscriptCopy()
        {
            lock (sync) return transcript.ToArray();
        }

        internal void AppendLocal(string role, string text, string meta)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (sync)
            {
                transcript.Add(new VoiceChatEntry { Role = role, Text = text, Meta = meta ?? "" });
                if (transcript.Count > 36) transcript.RemoveRange(0, transcript.Count - 36);
            }
        }

        internal void PollStatusAsync()
        {
            if (polling) return;
            polling = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string raw = HttpRequest("GET", "/status", null, 900);
                    var j = JsonMapper.ToObject(raw);
                    var next = new VoiceStatusSnapshot { Connected = true };
                    next.Version = OptStr(j, "version", "");
                    next.Language = OptStr(j, "language", "zh");
                    next.VoiceInputEnabled = OptBool(j, "voiceInputEnabled", true);
                    next.SilentMode = OptBool(j, "silentMode", false);
                    next.BargeInEnabled = OptBool(j, "bargeInEnabled", true);
                    next.ActiveVoiceModel = OptStr(j, "activeVoiceModel", "builtin:zh_serena");
                    next.TtsProvider = OptStr(j, "ttsProvider", "local");
                    next.TtsReady = OptBool(j, "ttsReady", false);
                    next.AsrReady = OptBool(j, "asrReady", false);
                    next.AsrEngine = OptStr(j, "asrEngine", "gpu_turbo");
                    next.AsrModel = OptStr(j, "asrModel", "");
                    next.IsSpeaking = OptBool(j, "isSpeaking", false);
                    next.Recording = OptBool(j, "asrRecording", false);
                    next.FreeMicEnabled = OptBool(j, "freeMicEnabled", false);
                    next.FreeMicThresholdDb = (float)OptNum(j, "freeMicThresholdDb", -34.0);
                    next.ProactiveEnabled = OptBool(j, "proactiveEnabled", true);
                    next.ProactiveIdleSec = (float)OptNum(j, "proactiveIdleSec", 600.0);
                    next.DuckNativeVoice = OptBool(j, "duckNativeVoiceOnSpeak", false);
                    next.LastReply = OptStr(j, "lastReply", "");
                    next.LastUserText = OptStr(j, "lastUserText", "");
                    next.LastActionSummary = OptStr(j, "lastActionSummary", "");
                    if (Has(j, "gains") && j["gains"].IsObject)
                        next.VoiceGain = (float)OptNum(j["gains"], "voice", 0.88);
                    if (Has(j, "chatConfig") && j["chatConfig"].IsObject)
                    {
                        var cc = j["chatConfig"];
                        next.Provider = OptStr(cc, "provider", "deepseek");
                        next.Model = OptStr(cc, "model", "deepseek-chat");
                        next.BaseUrl = OptStr(cc, "baseUrl", "");
                        next.UserTitle = OptStr(cc, "userTitle", "主人");
                        next.AiSelfRef = OptStr(cc, "aiSelfRef", "人家");
                        next.HasApiKey = OptBool(cc, "hasActiveKey", false);
                        next.ApiKeyMasked = OptStr(cc, "activeKeyMasked", "");
                        next.HistoryTurns = (int)OptNum(cc, "historyTurns", 0);
                        if (Has(cc, "lastUsage") && cc["lastUsage"].IsObject)
                        {
                            next.CacheHitRate = OptNum(cc["lastUsage"], "cacheHitRate", 0);
                            next.EstCostRmb = OptNum(cc["lastUsage"], "estCostRmb", 0);
                        }
                    }
                    if (Has(j, "memories") && j["memories"].IsArray)
                    {
                        for (int i = 0; i < j["memories"].Count; i++)
                            next.Memories.Add(Convert.ToString(j["memories"][i]) ?? "");
                    }
                    // Capture proactive or hands-free Free-Mic turns into the in-game transcript automatically!
                    string proReply = OptStr(j, "lastProactiveReply", "");
                    bool addProactive = false;
                    bool addNormal = false;
                    lock (sync)
                    {
                        if (!string.IsNullOrEmpty(proReply) && proReply != lastSeenProactiveReply)
                        {
                            lastSeenProactiveReply = proReply;
                            lastSeenAnyReply = proReply;
                            addProactive = true;
                        }
                        else if (!string.IsNullOrEmpty(next.LastReply) && next.LastReply != lastSeenAnyReply && !busy)
                        {
                            lastSeenAnyReply = next.LastReply;
                            addNormal = true;
                        }
                        snapshot = next;
                    }
                    if (addProactive)
                    {
                        AppendLocal("ai", proReply, UiText.L("主动引导", "Proactive") + (string.IsNullOrEmpty(next.LastActionSummary) ? "" : (" · " + next.LastActionSummary)));
                    }
                    else if (addNormal)
                    {
                        if (!string.IsNullOrEmpty(next.LastUserText))
                            AppendLocal("user", next.LastUserText, UiText.L("自由麦", "Open mic"));
                        AppendLocal("ai", next.LastReply, string.IsNullOrEmpty(next.LastActionSummary) ? UiText.L("回复", "Reply") : next.LastActionSummary);
                    }
                }
                catch (Exception ex)
                {
                    lock (sync)
                    {
                        snapshot = new VoiceStatusSnapshot { Connected = false, LastError = ex.Message };
                    }
                }
                finally { polling = false; }
            });
        }

        internal void SendChatAsync(string text, string language, Action<string, string> onDone)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (sync) { if (busy) return; busy = true; }
            AppendLocal("user", text, UiText.L("文字", "Text"));
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string reply = "", summary = "";
                try
                {
                    var body = new JsonData();
                    body["text"] = text;
                    if (!string.IsNullOrEmpty(language)) body["language"] = language;
                    string raw = HttpRequest("POST", "/chat", body.ToJson(), 28000);
                    var j = JsonMapper.ToObject(raw);
                    reply = OptStr(j, "reply", "");
                    summary = OptStr(j, "actionSummary", "");
                    string prov = OptStr(j, "provider", "");
                    double bms = OptNum(j, "brainMs", 0);
                    string meta = prov + " " + Math.Round(bms) + "ms" + (summary.Length > 0 ? (" · " + summary) : "");
                    if (reply.Length > 0) { lock (sync) lastSeenAnyReply = reply; AppendLocal("ai", reply, meta); }
                }
                catch (Exception ex)
                {
                    AppendLocal("sys", UiText.L("对话请求失败：", "Chat request failed: ") + ex.Message, "");
                }
                finally { lock (sync) busy = false; }
                if (onDone != null) try { onDone(reply, summary); } catch { }
            });
        }

        internal void StartMicAsync(Action<bool, string> onDone)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok = false;
                string info = "";
                try
                {
                    string raw = HttpRequest("POST", "/mic/start", "{}", 4000);
                    var j = JsonMapper.ToObject(raw);
                    ok = OptBool(j, "ok", false);
                    string dev = OptStr(j, "device", "");
                    info = ok ? dev : OptStr(j, "error", UiText.L("麦克风启动失败", "Microphone failed to start"));
                }
                catch (Exception ex)
                {
                    ok = false;
                    info = ex.Message;
                }
                if (onDone != null) try { onDone(ok, info); } catch { }
            });
        }

        internal void StopMicAndChatAsync(string language, Action<string, string, string> onDone)
        {
            lock (sync) { if (busy) return; busy = true; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string userText = "", reply = "", summary = "";
                try
                {
                    var body = new JsonData();
                    if (!string.IsNullOrEmpty(language)) body["language"] = language;
                    string raw = HttpRequest("POST", "/mic/stop", body.ToJson(), 32000);
                    var j = JsonMapper.ToObject(raw);
                    userText = OptStr(j, "userText", "");
                    reply = OptStr(j, "reply", "");
                    summary = OptStr(j, "actionSummary", "");
                    double asrMs = OptNum(j, "asrMs", 0);
                    double bms = OptNum(j, "brainMs", 0);
                    if (userText.Length > 0) AppendLocal("user", userText, UiText.L("语音 ", "Voice ") + Math.Round(asrMs) + "ms");
                    if (reply.Length > 0) { lock (sync) lastSeenAnyReply = reply; AppendLocal("ai", reply, Math.Round(bms) + "ms" + (summary.Length > 0 ? (" · " + summary) : "")); }
                }
                catch (Exception ex)
                {
                    AppendLocal("sys", UiText.L("语音识别异常：", "ASR error: ") + ex.Message, "");
                }
                finally { lock (sync) busy = false; }
                if (onDone != null) try { onDone(userText, reply, summary); } catch { }
            });
        }

        internal void PostSimpleAsync(string path, string jsonBody)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { HttpRequest("POST", path, jsonBody ?? "{}", 15000); }
                catch { }
            });
        }

        internal void UpdateBrainPresetAsync(string provider, string model)
        {
            var body = new JsonData();
            body["provider"] = provider;
            body["model"] = model;
            body["thinkingDisabled"] = true;
            PostSimpleAsync("/chat/config", body.ToJson());
        }

        internal void SaveUserApiConfigAsync(string provider, string model, string baseUrl, string apiKey, string userTitle, string aiSelfRef, Action<bool, string> onDone)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var body = new JsonData();
                    body["provider"] = provider ?? "deepseek";
                    body["model"] = model ?? "";
                    body["baseUrl"] = baseUrl ?? "";
                    body["thinkingDisabled"] = true;
                    if (!string.IsNullOrEmpty(userTitle)) body["userTitle"] = userTitle.Trim();
                    if (!string.IsNullOrEmpty(aiSelfRef)) body["aiSelfRef"] = aiSelfRef.Trim();
                    string key = (apiKey ?? "").Trim();
                    if (key.Length > 0)
                    {
                        if (provider == "deepseek") body["deepseekApiKey"] = key;
                        else if (provider == "gemini") body["googleApiKey"] = key;
                        else if (provider == "openai") body["openaiApiKey"] = key;
                        else if (provider == "claude") body["claudeApiKey"] = key;
                        else body["customApiKey"] = key;
                    }
                    HttpRequest("POST", "/chat/config", body.ToJson(), 8000);
                    if (onDone != null) onDone(true, UiText.L("API、模型与称呼配置已保存", "API, model, and address settings saved"));
                }
                catch (Exception ex)
                {
                    if (onDone != null) onDone(false, ex.Message);
                }
            });
        }

        internal void ResetApiConfigEmptyAsync(Action<bool, string> onDone)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var body = new JsonData();
                    body["clearKeys"] = true;
                    body["baseUrl"] = "";
                    HttpRequest("POST", "/chat/config", body.ToJson(), 8000);
                    if (onDone != null) onDone(true, UiText.L("已清空全部 API Key 恢复空白默认配置", "All API keys cleared; restored blank defaults"));
                }
                catch (Exception ex)
                {
                    if (onDone != null) onDone(false, ex.Message);
                }
            });
        }

        internal void UpdateVoiceConfigAsync(string key, object val)
        {
            var body = new JsonData();
            if (val is bool) body[key] = (bool)val;
            else if (val is float) body[key] = (double)(float)val;
            else if (val is double) body[key] = (double)val;
            else if (val is int) body[key] = (int)val;
            else body[key] = Convert.ToString(val) ?? "";
            PostSimpleAsync("/config", body.ToJson());
        }

        internal void PostJsonFireAndForget(string path, string jsonBody)
        {
            PostSimpleAsync(path, jsonBody ?? "{}");
        }

        internal void ShutdownSidecarSync(int timeoutMs = 450)
        {
            try { HttpRequest("POST", "/shutdown", "{}", timeoutMs); }
            catch { }
        }

        private string HttpRequest(string method, string path, string jsonBody, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(baseUrl + path);
            req.Method = method;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.Proxy = null;
            if (jsonBody != null)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(jsonBody);
                req.ContentType = "application/json; charset=utf-8";
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
            }
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        private static bool Has(JsonData d, string k)
        {
            return d != null && d.IsObject && ((IDictionary)d).Contains(k) && d[k] != null;
        }
        private static string OptStr(JsonData d, string k, string fb)
        {
            return Has(d, k) ? (Convert.ToString(d[k]) ?? fb) : fb;
        }
        private static bool OptBool(JsonData d, string k, bool fb)
        {
            if (!Has(d, k)) return fb;
            if (d[k].IsBoolean) return (bool)d[k];
            string s = (Convert.ToString(d[k]) ?? "").Trim().ToLowerInvariant();
            if (s == "true" || s == "1") return true;
            if (s == "false" || s == "0") return false;
            return fb;
        }
        private static double OptNum(JsonData d, string k, double fb)
        {
            if (!Has(d, k)) return fb;
            var item = d[k];
            if (item.IsDouble) return (double)item;
            if (item.IsInt) return (int)item;
            if (item.IsLong) return (long)item;
            double v;
            return double.TryParse(Convert.ToString(item, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fb;
        }
    }
}
