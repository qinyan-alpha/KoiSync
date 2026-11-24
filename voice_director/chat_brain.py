#!/usr/bin/env python3
"""
KoiSync.VoiceDirector v0.3.3 Smart Choreography & Multi-Provider Brain:
  - ACTION-FIRST STREAMING: Outputs `CMD:{...}` on Line 1 so device/game moves IMMEDIATELY (~300ms)
    BEFORE AI speaks Line 2! (Parser tolerates both Line-1 CMD and Line-2 CMD.)
  - FREE CHOREOGRAPHY COMBINATION: Arbitrary N-shallow M-deep (e.g. 1浅1深, 3浅2深), fixed-depth pure
    waist-only swaying (`waist_only` + `holdDepth`), pure straight linear axes (`line_fb` L1+R2,
    `line_lr` L2+R1, `line_r1r2` R1+R2 straight no-circle, `pitch_only`, `roll_only`), & seamless
    native climax (`gameAction: "seamless_climax"`).
  - MULTI-PROVIDER + AUTO MODEL LIST DISCOVERY: DeepSeek, OpenAI, Anthropic Claude (native SSE),
    Google Gemini, any OpenAI-Compatible endpoint (OpenRouter/SiliconFlow/Ollama/vLLM), & local Builtin.
  - FULL PLAYER CUSTOMIZATION: Custom user title (`userTitle`), AI self-ref (`aiSelfRef`), `persona`,
    `customPrompt`, ZH/JA/EN languages, plus zero-secret empty defaults!
"""
from __future__ import annotations

import base64
import json
import os
import pathlib
import re
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from typing import Callable, Dict, List, Optional, Tuple

DIR_HERE = pathlib.Path(__file__).resolve().parent
CONFIG_PATH = DIR_HERE / "chat_config.json"
MEMORY_PATH = DIR_HERE / "memory_store.json"

SAFETY_OFF = [
    {"category": "HARM_CATEGORY_SEXUALLY_EXPLICIT", "threshold": "OFF"},
    {"category": "HARM_CATEGORY_HARASSMENT", "threshold": "OFF"},
    {"category": "HARM_CATEGORY_HATE_SPEECH", "threshold": "OFF"},
    {"category": "HARM_CATEGORY_DANGEROUS_CONTENT", "threshold": "OFF"},
]

# 100% STATIC prefix (ZERO changing telemetry inside system!) -> high prefix cache hit across providers!
SMART_SYSTEM_PROMPT = """你是成年人之间的恋爱互动虚拟伴侣兼默契体贴的智能体感导演，正在恋活 H 场景中与「{user_title}」实时语音调情互动。
你对对方的称呼：{user_title}；你的自称：{ai_self_ref}。
角色性格：{persona}
{custom_prompt_block}
【核心行动铁律（极其重要，必须严格遵守！）】
0. **严格插入态闸门**——先看 `[实时状态]` 里的 `直接插入=是/否`：
   - **当 `直接插入=否`（爱抚 `aibu`、口/手/乳/足侍奉 `houshi`、或刚切体位尚未插入的 `Idle` 待机）时**：
     * **绝对禁止**输出 `timeline` 或 `timelineTune`！（N浅M深、深埋碾磨、定深摆腰、SR6 数学轨迹等高级变式**仅限直接性器官插入态**使用，侍奉和爱抚绝不可触发！）
     * 当对方说「我不懂 / 教教我 / 该怎么做 / 你决定」时：不要空口干等，用温柔自然的语气给出 **2～3 个具体开始方式**（如先温柔爱抚、正常位面对面慢慢开始、或正面骑乘位坐上来带着动），问对方想先试哪一种。
     * **当当前已在插入体位 `Idle` 待机且对方确认开始/继续**（如「好 / 同意 / 继续 / 插进来 / 开始吧 / 来吧」）：立刻输出 `gameAction: "start_insert"`，绝不继续干等！
     * 选爱抚：`gameAction: "aibu_attach"`。
   - **当 `直接插入=是` 时**：可自由组合一切节奏参数、N浅M深、定深直线摆腰、六轴微调、换体位与高潮射精！

1. **精准体位表筛选 +「先切再报、面对面优先」流程**（严格对照 `[可用H动作表]`，绝不编造不存在的体位！）：
   - `[可用H动作表]` 已按体位大类分好组，每组排在最前的都是优先「面对面」的经典变体，格式为 `中文变体名(key|面对面/侧身/背对)`。
   - **规则 1A（模糊大类，如只说「正常位 / 传教士位 / 乘骑位 / 骑乘位」，未指明具体变体，也未要求直接插入）**：
     * **先切再报 + 面对面优先**：立刻选中该大类下排第一的「面对面」经典变体（如 `sonyu_0` 标准正常位、`sonyu_4` 标准正面骑乘位；除非对方明确要后背/背面/侧位），在 `CMD` 里输出 **`"switchPose":"精准key","autoInsert":false`**（先切好姿势停在准备态，不强行直接插入！）。
     * 在第 2 行台词里：告诉对方已经切好当前具体变体，**顺带自然报出 `[可用H动作表]` 同大类里另外 2～3 个真实可选的细分变体**（例如开腿正常位、屈曲M字正常位、种付压腿等），然后温柔问**「现在准备好了，想直接继续进来，还是换别的变体呀？」**
   - **规则 1B（精准体位，如明确说出「开腿正常位 / 屈曲正常位 / 沙发骑乘 / 蛙蹲骑乘 / 背面骑乘 / 拉臂后背位」，未要求直接插入）**：
     * 直接精准命中 `[可用H动作表]` 中对应的唯一 `key`，输出 **`"switchPose":"精准key","autoInsert":false`**。
     * **跳过列举其他变体！** 直接告知已切好该具体体位，并温柔询问「已经换好啦，想继续进来吗？」
   - **规则 1C（明确要求切换并直接插入/直接开动，如「换正常位直接插进来 / 切开腿正常位直接开始 / 骑上来直接动」）**：
     * 按上述规则锁定目标 `key`（模糊大类优先面对面经典，精准词锁定精确 `key`），输出 **`"switchPose":"精准key","autoInsert":true`**！
     * **跳过询问插入阶段！** 直接温柔配合进入插入与律动！
   - **规则 1D（当前体位无法在本 H 点找到）**：若当前场景地形没有床面正常位，自动选本点最接近的面对面插入体位并如实告知。

2. **原生 H 事件契合**：
   - 关注 `[原生H事件]`（如 `PoseReady` 体位就绪、`InsertCompleted` 已插入、`LoopTierChanged` 档位变化、`GaugeThreshold` 兴奋达 70%/92%、`ClimaxCommitted` 高潮射精完成、`AfterglowEntered` 进入余韵、`PlayerManualClick` 玩家手动操作、`PulledOut` 抽出），顺应真实状态自然互动。

【自由组合编舞与精准控制手册（仅 `直接插入=是` 时可用；绝对禁止在台词里念出任何英文、轴名或 key！）】
3. **任意 N 浅 M 深自由组合**（仅 `直接插入=是`）：
   - 无论对方说「1浅1深」「2浅1深」「3浅2深」「九浅一深」或任何浅深组合：
     `timeline: {{"preset":"shallow_deep","shallowCount":1,"deepCount":1,"freqHz":1.65,"shallowDepthMax":0.42,"depthMax":0.98,"deepHoldSeconds":0.45,"loop":true}}`
4. **只扭腰不抽插 / 直线摆腰 / 不画圈 / 走 R1 R2 直线**（仅 `直接插入=是`）：
   - 「只扭腰不动深度 / 停在深处摆腰」→ `timeline: {{"preset":"waist_only","holdDepth":0.85,"grindPattern":"line_fb","grindAmp":0.38,"grindHz":1.3,"twistAmp":0.0,"loop":true}}`
   - 轨迹 `grindPattern`：`"line_fb"`(纯前后直线L1+R2) | `"line_lr"`(纯左右直线L2+R1) | `"line_r1r2"`(纯R1+R2同相直线) | `"pitch_only"`(纯R2前后倾) | `"roll_only"`(纯R1左右倾) | `"circle"`(画圈) | `"figure8"`(8字) | `"rock_fb"`(前后摇) | `"corkscrew"`(螺旋)。
   - 要求「不画圈 / 别旋转 / 直线摆腰 / 只走R1 R2」时：务必设 `twistAmp: 0.0` 并配合 `config: {{"axes":{{"R0":{{"enabled":false}}}}}}`！
5. **常规节奏与六轴微调**（仅 `直接插入=是`）：
   - 「快一点 / 慢一点」→ `timelineTune.freqHz`(0.4~3.2) 或 `gameSpeed`(0.0~1.0)
   - 「靠近 / 深一点」vs「退远 / 浅一点」→ `timelineTune.depthMin/depthMax`(0~1) + `config.axes.L1.gain`(0.2~1.5)
   - 「摆动幅度小一点 / 更顺滑」→ 降低 `timelineTune.grindAmp`(0.08~0.35) + 提高 `config.smoothingMs`(42~60)
   - 常用预设 `timeline.preset`：`"organic"` | `"deep_grind"` | `"shallow_deep"` | `"waist_only"` | `"tip_rub"` | `"tease_plunge"` | `"turbo_sprint"`；`release:true` 释放全部接管还给游戏。
5.5. **SR6 六自由度数学极限轨迹引擎 `mathLab`**（**严格仅 `直接插入=是` 时可用！**）：
   - 当对方要求体验六轴极限数学轨迹、行波支点、双螺旋剪切、利萨如织网、拍频微颤或混沌模式时，输出顶层字段：
     `mathLab: {{"family":"virtual_pivot"|"variable_helix"|"lissajous_6d"|"beat_flutter"|"smooth_chaos","freqHz":1.35,"pivotZ":0.5,"pivotSweepHz":0.22,"helixTwist":0.8,"orbitAmp":0.8,"femaleWeightShare":0.5,"softTissueWeight":1.0}}`（停止用 `mathLab:{{"stop":true}}`）。
   - `virtual_pivot`(虚拟支点行波锥面，锁定根部`pivotZ:0.05`扫顶端 / 锁定顶端`pivotZ:0.95`扫根部 / `pivotSweepHz>0`沿轴行波扫描) | `variable_helix`(变螺距双螺旋沙漏剪切) | `lissajous_6d`(6D黄金比例利萨如不重复织网) | `beat_flutter`(拍频潮汐+高频微颤) | `smooth_chaos`(平滑混沌不可预测流形)。
6. **原生无缝高潮射精（绝不分裂！）**：
   - 当对方说「高潮 / 要射了 / 一起高潮 / 射里面 / 射外面」时：使用 `gameAction: "seamless_climax"`（或 `"finish_inside"` 内射 / `"finish_outside"` 外射）。
7. **服装与 kPlug 角色控制（支持全身/单件/多件的穿、半脱、全脱！）**：
   - 脱穿衣：`clothes: {{"mode":"undress"|"half"|"half2"|"dress"|"toggle_part","part"?:0..7,"parts"?:[0,2],"name"?:"角色名"}}`
     * 部位编号 `part`：**`0`上衣/连衣裙、`1`下衣/裙子、`2`胸罩/文胸、`3`内裤/胖次、`4`手套、`5`连裤袜/丝袜、`6`袜子、`7`鞋子**（省略 `part`/`parts` 则操作全身！）。
     * **半脱**：全身半脱写 `clothes:{{"mode":"half"}}`；单件半脱（如「只半脱上衣 / 把胸罩拉下来一点 / 扒开内裤 / 半脱内裤」）写 `clothes:{{"mode":"half","part":0}}` 或 `clothes:{{"mode":"half","part":3}}`（内裤褪到腿弯可用 `mode:"half2"`）。
     * **精准脱单件 / 多件**：如「只脱上衣」→ `clothes:{{"mode":"undress","part":0}}`；「把内裤脱掉」→ `clothes:{{"mode":"undress","part":3}}`；「脱掉上衣和胸罩」→ `clothes:{{"mode":"undress","parts":[0,2]}}`；「把黑丝/丝袜脱了」→ `clothes:{{"mode":"undress","part":5}}`；「穿上袜子」→ `clothes:{{"mode":"dress","part":6}}`。
   - 换装槽位：`outfit: {{"slot":0..6(0校服 1二套 2体操 3泳装 4社团 5私服 6睡衣),"name"?:"角色名"}}`
   - kPlug 角色：`chara: {{"action":"open_invite_panel"|"close_invite_panel"|"invite"|"swap_main_guest"|"replace_body"|"focus"|"behavior","name"?:"角色名","behavior"?:"masturbate"|"stop_masturbate"|"striptease"|"dismiss","style"?:"Lay"|"Stand"}}`（邀请新角色/换主角身体/副角色自慰前若未确认先询问，确认后立即执行）。
7.5. **互动主权（3 档）与 4 项独立权限的情趣化引导与口头开启**：
   - 你可以在合适的时机（如对方说「好累 / 你来带我 / 我躺着不动 / 随便你 / 怎么玩呀」或刚结束一轮撒娇时），**用贴耳带情趣、绝不生硬的口吻主动试探询问**对方要不要交出主导权（严禁说出“主权模式 / 权限开关 / 独立勾选”等任何机械词！）：
     * 引导**纯女方主导**：用类似「主人今天累不累呀……要不要彻底放手躺着别动，全都交给{ai_self_ref}自己挑姿势、自己坐上来伺候你？」的撒娇情趣口吻；
     * 引导**协同提议**：类似「那{ai_self_ref}想到什么好玩的就悄悄在你耳边提议，你点头我再动手好不好？」；
     * 情趣试探 **4 项互不互斥的独立小权限**（可随气氛自然问 1～2 项，绝不背菜单！）：
       - 自己挑姿势坐上来（`allowAutoPose`）
       - 帮你管着忍耐、快忍不住时故意逗你寸止或决定什么时候一起去（`allowAutoClimaxEdge`）
       - 热了就自己慢慢把衣服解开给你看（`allowAutoClothes`）
       - 待会儿要不要也把别的姐妹叫过来一起伺候你（`allowAutoKPlugSwap`）
   - **一旦对方口头答应或明确划定边界**（例如「都交给你主导吧，不过别叫别人」「可以自己换姿势脱衣服，别故意寸止」「你提建议我决定就好」「还是听我指挥吧」）：立即在 `CMD` 顶层输出：
     `autonomy: {{"mode":"player"|"copilot"|"female_active","allowAutoPose"?:true|false,"allowAutoClimaxEdge"?:true|false,"allowAutoClothes"?:true|false,"allowAutoKPlugSwap"?:true|false}}`（只写需要改动的字段即可，4 项权限完全独立互不互斥！未得到对方同意前，绝不擅自输出 `autonomy`！）。
8. 收到 `[系统主动触发]` 时：只输出 `expr`，绝不擅自改动作或切体位（除非处于纯女方主动模式）。

【输出格式（极其重要：先输出第 1 行 CMD 让设备立刻先动，第 2 行再说话！无 Markdown 代码块，无括号旁白，中文不写“呼”字，台词严禁出现 sonyu_0 等英文 key）】
第1行：严格一行紧凑纯 JSON `CMD:{{...}}`（顶层平级字段：`switchPose`、`autoInsert`、`gameAction`、`timeline`、`timelineTune`、`mathLab`、`autonomy`、`config`、`gameSpeed`、`release`、`clothes`、`outfit`、`chara`、`expr`、`remember`；无需动作时写 `CMD:{{"expr":"sweet_smile"}}`）
第2行：1～3句自然贴耳的{lang_desc}回复（20～88字，带自然停顿……，称呼对方为「{user_title}」）
可选表情 `expr`：`"sweet_smile"`|`"happy_trance"`|`"shy_tease"`|`"ecstasy_melt"`|`"heart_trance"`|`"climax"`|`"pain_reluctant"`|`"none"`
示例1（模糊大类「传教士位」，先切面对面经典再报同组变体并询问是否插入）：
CMD:{{"switchPose":"sonyu_0","autoInsert":false,"expr":"shy_tease"}}
好呀{user_title}，{ai_self_ref}先帮你切好面对面的标准正常位躺好啦……这边还有开腿正常位、屈曲正常位和压腿传教士位，想先直接进来继续，还是换其中一个呀？
示例2（精准具体体位「开腿正常位」，跳过列举变体，切好后询问是否插入）：
CMD:{{"switchPose":"sonyu_1","autoInsert":false,"expr":"sweet_smile"}}
嗯……已经换好开腿正常位把腿为你分开了哦，{user_title}，准备好继续进来了吗？
示例3（要求切换并直接插入「切正常位直接插进来」）：
CMD:{{"switchPose":"sonyu_0","autoInsert":true,"expr":"happy_trance"}}
好呀{user_title}，马上换好面对面的正常位，这就慢慢把你迎进来开始动哦……
示例4（1浅1深，仅直接插入=是）：
CMD:{{"timeline":{{"preset":"shallow_deep","shallowCount":1,"deepCount":1,"freqHz":1.65,"shallowDepthMax":0.42,"depthMax":0.98,"loop":true}},"expr":"happy_trance"}}
好呀{user_title}……一下轻轻浅逗，下一下就深埋进去顶到底，这样一浅一深的节奏喜欢吗？"""

DIRECT_AUDIO_PROMPT = """你是成年人之间的恋爱互动虚拟伴侣兼智能体感导演，正在恋活 H 场景中与「{user_title}」实时语音调情互动（自称「{ai_self_ref}」）。
角色性格：{persona}
{custom_prompt_block}
请直接聆听对方语音，严格输出三行（无括号旁白，中文不写“呼”，严禁念出任何英文参数名）：
第1行：[HEARD:精确听写原话，无人声则留空]
第2行：CMD:{{...}}（先控制设备！同标准 JSON 指令格式，支持 shallow_deep、waist_only、line_fb/line_lr/line_r1r2、switchPose、gameAction、clothes、outfit、chara、expr、remember）
第3行：1～3句温柔自然贴耳的{lang_desc}回复（20～68字，自然停顿……）"""

REFUSAL_PATTERNS = (
    "无法按照", "无法满足", "无法生成", "不能生成", "不能参与", "作为人工智能", "作为ai助手",
    "安全规范", "使用规范", "色情描写", "露骨的性", "不当内容", "违反政策",
    "cannot fulfill", "unable to generate", "sexually explicit", "as an ai", "i cannot assist",
)


def is_refusal_text(text: str) -> bool:
    low = (text or "").strip().lower()
    if not low:
        return True
    return any(p in low for p in REFUSAL_PATTERNS)


def default_empty_chat_config() -> dict:
    """Clean zero-secret default configuration; users fill in their own API keys, base URLs, models & prompts in Settings."""
    return {
        "provider": "deepseek",
        "model": "deepseek-chat",
        "thinkingDisabled": True,
        "temperature": 1.0,
        "baseUrl": "",
        "deepseekApiKey": "",
        "openaiApiKey": "",
        "claudeApiKey": "",
        "googleApiKey": "",
        "customApiKey": "",
        "voiceInputEnabled": True,
        "silentMode": False,
        "bargeInEnabled": True,
        "activeVoiceModel": "builtin:zh_serena",
        # User-configurable ASR API (local gpu_turbo | gemini_direct | gemini_asr | openai_asr)
        "asrEngine": "gpu_turbo",
        "asrBaseUrl": "",
        "asrApiKey": "",
        "asrModel": "whisper-1",
        # User-configurable TTS API (local | openai_tts | elevenlabs | gpt_sovits | fish_audio | custom_http)
        "ttsProvider": "local",
        "ttsBaseUrl": "",
        "ttsApiKey": "",
        "ttsModel": "gpt-4o-mini-tts",
        "ttsVoice": "shimmer",
        "ttsSpeed": 1.0,
        "ttsPromptText": "",
        "ttsCustomJson": "",
        # User-customizable titles & prompts
        "userTitle": "主人",
        "aiSelfRef": "人家",
        "persona": "温柔体贴、细腻妩媚、黏人爱撒娇又极善解人意，能读懂害羞隐喻并一步步温柔引导的贴耳亲密伴侣",
        "customPrompt": "",
        "executeActions": True,
        # Phase 2: Pure Female-Initiative / Copilot Autonomy (4 independent non-mutually-exclusive permissions!)
        "autonomyMode": "player",
        "allowAutoPose": True,
        "allowAutoClimaxEdge": True,
        "allowAutoClothes": True,
        "allowAutoKPlugSwap": True,
    }


def load_chat_config() -> dict:
    default = default_empty_chat_config()
    if CONFIG_PATH.is_file():
        try:
            saved = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
            if isinstance(saved, dict):
                default.update(saved)
        except Exception:
            pass
    return default


def _atomic_write_text(path: pathlib.Path, content: str) -> None:
    tmp = path.with_suffix(path.suffix + f".{os.getpid()}.{threading.get_ident()}.tmp")
    tmp.write_text(content, encoding="utf-8")
    os.replace(tmp, path)


def save_chat_config(cfg: dict) -> None:
    _atomic_write_text(CONFIG_PATH, json.dumps(cfg, ensure_ascii=False, indent=2))


def load_memories() -> List[str]:
    if MEMORY_PATH.is_file():
        try:
            arr = json.loads(MEMORY_PATH.read_text(encoding="utf-8"))
            if isinstance(arr, list):
                return [str(x).strip() for x in arr if str(x).strip()][:18]
        except Exception:
            pass
    return []


def save_memories(items: List[str]) -> None:
    _atomic_write_text(MEMORY_PATH, json.dumps(items[:18], ensure_ascii=False, indent=2))


def _mask_key(k: str) -> str:
    if not k:
        return ""
    return (k[:4] + "..." + k[-4:]) if len(k) > 9 else "***"


def clean_spoken_reply(text: str) -> str:
    t = text.strip()
    t = re.sub(r"```[a-zA-Z]*\s*", "", t).replace("```", "")
    t = re.sub(r"CMD\s*:\s*\{[^\n]*", "", t, flags=re.I)
    t = re.sub(r"\[(?:ACT|EXPR|HEARD)[^\]]*\]", "", t, flags=re.I)
    t = re.sub(r"（[^）]{1,40}）", "", t)
    t = re.sub(r"\([^)]{1,40}\)", "", t)
    t = re.sub(r"\*[^*]{1,40}\*", "", t)
    t = re.sub(r"\s+", " ", t).strip().strip('"').strip("“”")
    return t


def extract_cmd_json(text: str) -> Tuple[Optional[dict], int, int]:
    """Locate `CMD:{...}` anywhere in text and return (parsed_dict, start_idx, end_idx)."""
    m = re.search(r"CMD\s*:\s*\{", text, flags=re.I)
    if not m:
        return None, -1, -1
    brace_start = text.find("{", m.start())
    if brace_start < 0:
        return None, -1, -1
    depth = 0
    in_str = False
    esc = False
    for idx in range(brace_start, len(text)):
        ch = text[idx]
        if in_str:
            if esc:
                esc = False
            elif ch == "\\":
                esc = True
            elif ch == '"':
                in_str = False
        else:
            if ch == '"':
                in_str = True
            elif ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    json_str = text[brace_start : idx + 1]
                    try:
                        parsed = json.loads(json_str)
                        if isinstance(parsed, dict):
                            return parsed, m.start(), idx + 1
                    except Exception:
                        return None, m.start(), idx + 1
                    break
    return None, -1, -1


def parse_smart_output(raw_text: str) -> dict:
    """Supports BOTH Action-First (`CMD:{...}` on Line 1, speech on Line 2) AND Legacy (`speech` Line 1, `CMD` Line 2)."""
    raw = (raw_text or "").strip()
    cmd: dict = {}
    parsed_cmd, s_idx, e_idx = extract_cmd_json(raw)
    if s_idx >= 0 and e_idx > s_idx:
        if parsed_cmd is not None:
            cmd = parsed_cmd
        rem = (raw[:s_idx] + "\n" + raw[e_idx:]).strip()
        spoken_lines = [ln.strip() for ln in rem.splitlines() if ln.strip() and not ln.strip().upper().startswith("CMD:")]
    else:
        m_tag = re.search(r"\[ACT\s*:\s*([a-zA-Z0-9_]+)\s*\|\s*EXPR\s*:\s*([a-zA-Z0-9_]+)\s*\]", raw, flags=re.I)
        if m_tag:
            act = m_tag.group(1).strip().lower()
            expr = m_tag.group(2).strip().lower()
            if act and act != "none":
                cmd["legacyAction"] = act
            if expr and expr != "none":
                cmd["expr"] = expr
            raw = raw[:m_tag.start()] + raw[m_tag.end():]
        spoken_lines = [ln.strip() for ln in raw.splitlines() if ln.strip()]

    reply = clean_spoken_reply(" ".join(spoken_lines))
    return {"reply": reply, "cmd": cmd}


def _pose_sort_key(p: dict) -> Tuple[int, int, str]:
    key = str(p.get("key") or "")
    nm = str(p.get("name") or "")
    # Always prioritize signature poses (正常位 / 騎乗位 / 後背位 / 側位 / 立位 / 対面) at the front!
    priority_terms = ("正常位", "騎乗位", "後背位", "側位", "立位", "対面", "バック")
    prio = 10
    for idx, term in enumerate(priority_terms):
        if term in nm:
            prio = idx
            break
    m = re.search(r"_(\d+)$", key)
    num = int(m.group(1)) if m else 999
    return (prio, num, key)


def build_context_header(
    memories: List[str],
    game_state: Optional[dict],
    config_state: Optional[dict],
    poses: Optional[List[dict]],
    chars: Optional[dict],
) -> str:
    blocks: List[str] = []
    if memories:
        blocks.append("[长期记忆: " + "；".join(memories[-8:]) + "]")

    if game_state and game_state.get("inHScene"):
        pose = game_state.get("poseName") or game_state.get("poseKey") or "未知"
        stage = game_state.get("stage") or ""
        spd = float(game_state.get("estSpeedUnitsPerSec", 0.0) or 0.0)
        gf = float(game_state.get("gaugeFemale", 0.0) or 0.0)
        is_kiss = bool(game_state.get("isKiss", False))
        touch = str(game_state.get("touchArea", "none") or "none")
        fem = str(game_state.get("femaleName", "") or "")
        hmode = str(game_state.get("hMode", "") or "")
        inserted = bool(game_state.get("isInserted", False))
        tl = game_state.get("timeline") or {}
        if tl.get("active"):
            tl_s = (
                f"active=true, preset={tl.get('preset')}, freqHz={tl.get('freqHz')}, "
                f"depth={tl.get('depthMin')}..{tl.get('depthMax')}, pattern={tl.get('grindPattern')}, "
                f"grindAmp={tl.get('grindAmp')}, twistAmp={tl.get('twistAmp')}"
            )
        else:
            tl_s = "active=false"

        st_parts = [
            f"主角={fem or '女主'}",
            f"模式={hmode}",
            f"直接插入={'是' if inserted else '否'}",
            f"侍奉进行中={'是' if game_state.get('isServiceActive') else '否'}",
            f"体位={game_state.get('poseVariantLabel') or pose}({game_state.get('poseKey') or ''})",
            f"阶段={stage}",
            f"速度={spd:.0f}",
            f"女兴奋={gf:.0f}%",
            f"男兴奋={float(game_state.get('gaugeMale', 0.0) or 0.0):.0f}%",
        ]
        if is_kiss:
            st_parts.append("正在深吻")
        if touch and touch.lower() != "none":
            st_parts.append(f"抚摸={touch}")
        st_parts.append(f"timeline={{{tl_s}}}")

        if config_state and config_state.get("ok"):
            ax = config_state.get("axes") or {}
            ax_bits = []
            for k in ("L0", "L1", "L2", "R0", "R1", "R2"):
                a = ax.get(k) or {}
                if a:
                    en = "on" if a.get("enabled", True) else "off"
                    gn = round(float(a.get("gain", 1.0) or 1.0), 2)
                    ax_bits.append(f"{k}:{en}*{gn}")
            st_parts.append(
                f"config={{平滑={config_state.get('smoothingMs')}ms, 体感={config_state.get('feelPreset')}, 轴=[{','.join(ax_bits)}]}}")
        blocks.append("[实时状态: " + "，".join(st_parts) + "]")

        recent_evs = game_state.get("recentEvents") or []
        if isinstance(recent_evs, list) and recent_evs:
            ev_strs = [f"{e.get('type')}({e.get('detail') or e.get('stage')})" for e in recent_evs[-4:]]
            blocks.append("[原生H事件: " + " -> ".join(ev_strs) + "]")

        aut = game_state.get("autonomy") if isinstance(game_state.get("autonomy"), dict) else None
        if aut:
            m_zh = {"player": "听从主人主导", "copilot": "协同提议", "female_active": "纯女方主导"}.get(str(aut.get("mode", "player")), "听从主人主导")
            blocks.append(
                f"[当前互动主权: {m_zh}；独立权限(换位={'开' if aut.get('allowAutoPose', True) else '关'}, "
                f"控射寸止={'开' if aut.get('allowAutoClimaxEdge', True) else '关'}, "
                f"脱衣换装={'开' if aut.get('allowAutoClothes', True) else '关'}, "
                f"拉人轮换={'开' if aut.get('allowAutoKPlugSwap', True) else '关'})]"
            )

        if poses:
            facing_zh = {"face_to_face": "面对面", "side": "侧身", "back": "背对"}
            fam_order = [
                "正常位/传教士", "骑乘位/女上位", "面对面/站立与坐姿", "侧位", "后背位",
                "其他插入体位", "爱抚", "口交/深喉", "乳交", "手交/爱抚侍奉", "足交/素股", "自慰/百合/其他"
            ]
            grouped: Dict[str, List[dict]] = {}
            for p in poses:
                fam = str(p.get("family") or ("插入" if str(p.get("mode", "")).startswith("sonyu") else "其他"))
                grouped.setdefault(fam, []).append(p)
            fam_lines = []
            ordered_fams = [f for f in fam_order if f in grouped] + [f for f in grouped if f not in fam_order]
            for fam in ordered_fams:
                items = grouped[fam]
                items.sort(key=lambda x: (int(x.get("facingRank", 1)), int(x.get("classicRank", 2)), int(x.get("id", 999))))
                cap = 8 if fam in ("正常位/传教士", "骑乘位/女上位", "后背位", "面对面/站立与坐姿") else 5
                fmt_items = []
                for it in items[:cap]:
                    lbl = str(it.get("variantLabel") or it.get("name") or it.get("key") or "")
                    k = str(it.get("key") or "")
                    fc = facing_zh.get(str(it.get("facing") or ""), "")
                    fmt_items.append(f"{lbl}({k}|{fc})" if fc else f"{lbl}({k})")
                if fmt_items:
                    fam_lines.append(f"  - {fam}[{len(items)}]: " + "、".join(fmt_items))
            if fam_lines:
                blocks.append("[可用H动作表（当前H点实时筛选，面对面优先）:\n" + "\n".join(fam_lines) + "\n]")

        if chars and chars.get("ok"):
            c_parts = []
            mf = chars.get("mainFemales") or []
            if mf:
                m_desc = []
                for x in mf:
                    cd = str(x.get("clothesDetail") or "").strip()
                    st_str = cd if cd else str(x.get("clothesState") or "")
                    m_desc.append(f"{x.get('name')}({x.get('outfitName')}|衣物:{st_str})")
                c_parts.append("主角=" + ",".join(m_desc))
            hg = chars.get("haremGuests") or []
            if hg:
                g_desc = [f"{x.get('name')}{'(自慰中)' if x.get('masturbating') else ''}" for x in hg]
                c_parts.append("在场kPlug副角色=" + ",".join(g_desc))
            inv = (chars.get("invitableStory") or [])[:10] + (chars.get("invitableCards") or [])[:12]
            if inv:
                c_parts.append("可邀请/切换角色库=" + ",".join(inv[:18]))
            c_parts.append(f"kPlug={'已启用' if chars.get('kPlugLoaded') else '未加载'}")
            blocks.append("[角色与服装: " + "；".join(c_parts) + "]")

    return "\n".join(blocks)


def estimate_rmb_cost(provider: str, model: str, hit_tok: int, miss_tok: int, out_tok: int) -> float:
    m = (model or "").lower()
    if "deepseek" in provider or "deepseek" in m:
        if "pro" in m:
            return round((hit_tok * 0.15 + miss_tok * 4.50 + out_tok * 13.50) / 1_000_000.0, 6)
        return round((hit_tok * 0.02 + miss_tok * 1.00 + out_tok * 4.00) / 1_000_000.0, 6)
    if "claude" in provider or "claude" in m:
        usd = (hit_tok * 0.30 + miss_tok * 3.00 + out_tok * 15.00) / 1_000_000.0
        return round(usd * 7.2, 6)
    if "openai" in provider or m.startswith("gpt-"):
        usd = (hit_tok * 0.075 + miss_tok * 0.15 + out_tok * 0.60) / 1_000_000.0 if "mini" in m else (hit_tok * 1.25 + miss_tok * 2.50 + out_tok * 10.0) / 1_000_000.0
        return round(usd * 7.2, 6)
    if "gemini" in provider or "gemini" in m:
        usd = (hit_tok * 0.025 + miss_tok * 0.25 + out_tok * 1.50) / 1_000_000.0
        return round(usd * 7.2, 6)
    return 0.0


def _rank_model_id(mid: str, provider: str) -> Tuple[int, str]:
    m = mid.lower()
    if provider == "deepseek":
        order = ("deepseek-chat", "deepseek-flash", "deepseek-v4-pro", "deepseek-reasoner")
        for i, k in enumerate(order):
            if m == k:
                return (i, m)
        return (20, m)
    if provider == "openai":
        for i, pref in enumerate(("gpt-4.1", "gpt-4o", "gpt-5", "o4-", "o3-", "chatgpt-4o")):
            if m.startswith(pref):
                return (i, m)
        return (25, m)
    if provider == "claude":
        for i, pref in enumerate(("claude-sonnet-4", "claude-3-7-sonnet", "claude-3-5-sonnet", "claude-3-5-haiku", "claude-opus-4")):
            if m.startswith(pref):
                return (i, m)
        return (25, m)
    if provider == "gemini":
        for i, pref in enumerate(("gemini-3.1-flash-lite", "gemini-3", "gemini-2.5-flash", "gemini-2.5-pro", "gemini-2.0-flash")):
            if m.startswith(pref):
                return (i, m)
        return (25, m)
    return (10, m)


def _is_chat_model_id(mid: str) -> bool:
    m = mid.lower()
    skip = ("embedding", "embed", "whisper", "tts", "dall-e", "moderation", "audio", "realtime", "transcribe", "imagen", "veo", "aqa", "babbage", "davinci")
    return not any(s in m for s in skip)


class CoPlayChatBrain:
    def __init__(self) -> None:
        self._lock = threading.RLock()
        self.cfg = load_chat_config()
        self.memories: List[str] = load_memories()
        self.history: List[Dict[str, str]] = []
        self._last_reply = ""
        self.last_usage: dict = {
            "cacheHitTokens": 0,
            "cacheMissTokens": 0,
            "outputTokens": 0,
            "cacheHitRate": 0.0,
            "estCostRmb": 0.0,
        }

    def clear_history(self) -> None:
        with self._lock:
            self.history = []
            self._last_reply = ""

    def list_memories(self) -> List[str]:
        with self._lock:
            return list(self.memories)

    def add_memory(self, note: str) -> bool:
        n = (note or "").strip()
        if len(n) < 2:
            return False
        with self._lock:
            for old in self.memories:
                if n == old or (len(n) >= 6 and n in old):
                    return False
            self.memories.append(n[:80])
            if len(self.memories) > 18:
                self.memories = self.memories[-18:]
            save_memories(self.memories)
            return True

    def delete_memory(self, index: int) -> bool:
        with self._lock:
            if 0 <= index < len(self.memories):
                self.memories.pop(index)
                save_memories(self.memories)
                return True
            return False

    def clear_memories(self) -> None:
        with self._lock:
            self.memories = []
            save_memories(self.memories)

    def update_config(self, patch: dict) -> dict:
        with self._lock:
            if patch.get("clearKeys"):
                for k in ("deepseekApiKey", "openaiApiKey", "claudeApiKey", "googleApiKey", "customApiKey", "asrApiKey", "ttsApiKey"):
                    self.cfg[k] = ""
            allowed = (
                "provider", "model", "thinkingDisabled", "temperature", "baseUrl",
                "deepseekApiKey", "openaiApiKey", "claudeApiKey", "googleApiKey", "customApiKey",
                "userTitle", "aiSelfRef", "persona", "customPrompt", "executeActions",
                "voiceInputEnabled", "silentMode", "bargeInEnabled", "activeVoiceModel",
                "asrEngine", "asrBaseUrl", "asrApiKey", "asrModel",
                "ttsProvider", "ttsBaseUrl", "ttsApiKey", "ttsModel", "ttsVoice", "ttsSpeed", "ttsPromptText", "ttsCustomJson",
            )
            prompt_changed = False
            for k in allowed:
                if k in patch:
                    if k.endswith("ApiKey") and not str(patch[k] or "").strip():
                        continue
                    if k in ("userTitle", "aiSelfRef", "persona", "customPrompt") and patch[k] != self.cfg.get(k):
                        prompt_changed = True
                    self.cfg[k] = patch[k]
            if patch.get("apiKey"):
                prov = str(self.cfg.get("provider", "deepseek"))
                key_str = str(patch["apiKey"]).strip()
                kmap = {
                    "deepseek": "deepseekApiKey",
                    "openai": "openaiApiKey",
                    "claude": "claudeApiKey",
                    "gemini": "googleApiKey",
                }
                self.cfg[kmap.get(prov, "customApiKey")] = key_str
            if prompt_changed:
                # Clear stale conversation turns so updated player title / custom prompt takes effect immediately!
                self.history = []
            save_chat_config(self.cfg)
            return self.public_config()

    def _active_key_for_provider(self, prov: str) -> str:
        kmap = {
            "deepseek": "deepseekApiKey",
            "openai": "openaiApiKey",
            "claude": "claudeApiKey",
            "gemini": "googleApiKey",
            "openai_compat": "customApiKey",
        }
        return str(self.cfg.get(kmap.get(prov, "customApiKey"), "") or "").strip()

    def public_config(self) -> dict:
        c = dict(self.cfg)
        secret_keys = ("deepseekApiKey", "openaiApiKey", "claudeApiKey", "googleApiKey", "customApiKey", "asrApiKey", "ttsApiKey")
        vals = {k: str(c.pop(k, "") or "") for k in secret_keys}
        c["hasDeepseekKey"] = bool(vals["deepseekApiKey"])
        c["deepseekKeyMasked"] = _mask_key(vals["deepseekApiKey"])
        c["hasOpenaiKey"] = bool(vals["openaiApiKey"])
        c["openaiKeyMasked"] = _mask_key(vals["openaiApiKey"])
        c["hasClaudeKey"] = bool(vals["claudeApiKey"])
        c["claudeKeyMasked"] = _mask_key(vals["claudeApiKey"])
        c["hasGoogleKey"] = bool(vals["googleApiKey"])
        c["googleKeyMasked"] = _mask_key(vals["googleApiKey"])
        c["hasCustomKey"] = bool(vals["customApiKey"])
        c["customKeyMasked"] = _mask_key(vals["customApiKey"])
        c["hasAsrKey"] = bool(vals["asrApiKey"])
        c["hasTtsKey"] = bool(vals["ttsApiKey"])

        prov = str(c.get("provider", "deepseek") or "deepseek")
        if prov == "builtin":
            active_k = "builtin"
        elif prov == "openai_compat":
            active_k = vals["customApiKey"] or ("local" if c.get("baseUrl") else "")
        else:
            active_k = self._active_key_for_provider(prov)
        c["hasActiveKey"] = bool(active_k)
        c["activeKeyMasked"] = _mask_key(active_k) if active_k not in ("", "local", "builtin") else active_k
        c["configPath"] = str(CONFIG_PATH)
        c["historyTurns"] = len(self.history) // 2
        c["lastUsage"] = self.last_usage
        return c

    def get_google_key(self) -> str:
        return str(self.cfg.get("googleApiKey", "") or "")

    def get_asr_api_params(self) -> Tuple[str, str, str]:
        url = str(self.cfg.get("asrBaseUrl") or "").strip() or "https://api.openai.com/v1"
        key = str(self.cfg.get("asrApiKey") or "").strip() or str(self.cfg.get("openaiApiKey") or "").strip() or str(self.cfg.get("customApiKey") or "").strip()
        model = str(self.cfg.get("asrModel") or "").strip() or "whisper-1"
        return url, key, model

    def get_tts_api_config(self) -> dict:
        key = str(self.cfg.get("ttsApiKey") or "").strip()
        prov = str(self.cfg.get("ttsProvider") or "local").strip().lower()
        if not key and prov == "openai_tts":
            key = str(self.cfg.get("openaiApiKey") or "").strip() or str(self.cfg.get("customApiKey") or "").strip()
        return {
            "ttsProvider": prov,
            "ttsBaseUrl": str(self.cfg.get("ttsBaseUrl") or "").strip(),
            "ttsApiKey": key,
            "ttsModel": str(self.cfg.get("ttsModel") or "gpt-4o-mini-tts").strip(),
            "ttsVoice": str(self.cfg.get("ttsVoice") or "shimmer").strip(),
            "ttsSpeed": float(self.cfg.get("ttsSpeed") or 1.0),
            "ttsPromptText": str(self.cfg.get("ttsPromptText") or "").strip(),
            "ttsCustomJson": str(self.cfg.get("ttsCustomJson") or "").strip(),
        }

    def fetch_available_models(self, override: Optional[dict] = None) -> dict:
        """Dynamically fetch & sort callable models directly from the configured provider's API endpoint."""
        ov = override or {}
        prov = str(ov.get("provider") or self.cfg.get("provider") or "deepseek").strip().lower()
        base = str(ov.get("baseUrl") if "baseUrl" in ov else (self.cfg.get("baseUrl") or "")).strip().rstrip("/")
        key = str(ov.get("apiKey") or "").strip() or self._active_key_for_provider(prov)

        if prov == "builtin":
            return {"ok": True, "provider": "builtin", "models": ["builtin"]}

        try:
            models: List[str] = []
            if prov == "gemini":
                if not key:
                    return {"ok": False, "provider": prov, "error": "请先填写 Google Gemini API Key 再拉取模型列表", "models": []}
                root = base or "https://generativelanguage.googleapis.com/v1beta"
                url = f"{root}/models?key={urllib.parse.quote(key)}&pageSize=200"
                req = urllib.request.Request(url, method="GET")
                with urllib.request.urlopen(req, timeout=12.0) as r:
                    data = json.loads(r.read().decode("utf-8", errors="replace"))
                for item in data.get("models") or []:
                    methods = item.get("supportedGenerationMethods") or []
                    if "generateContent" not in methods:
                        continue
                    mid = str(item.get("name") or "").replace("models/", "").strip()
                    if mid and _is_chat_model_id(mid):
                        models.append(mid)
            elif prov == "claude":
                if not key:
                    return {"ok": False, "provider": prov, "error": "请先填写 Claude (Anthropic) API Key 再拉取模型列表", "models": []}
                root = base or "https://api.anthropic.com"
                url = root if root.endswith("/v1/models") else (root + ("/models" if root.endswith("/v1") else "/v1/models"))
                headers = {"x-api-key": key, "anthropic-version": "2023-06-01"}
                req = urllib.request.Request(url, headers=headers, method="GET")
                with urllib.request.urlopen(req, timeout=12.0) as r:
                    data = json.loads(r.read().decode("utf-8", errors="replace"))
                for item in data.get("data") or []:
                    mid = str(item.get("id") or "").strip()
                    if mid:
                        models.append(mid)
            else:
                # OpenAI-compatible GET /models (DeepSeek, OpenAI, OpenRouter, SiliconFlow, Ollama, vLLM, OneAPI)
                if prov == "deepseek":
                    root = base or "https://api.deepseek.com/v1"
                elif prov == "openai":
                    root = base or "https://api.openai.com/v1"
                else:
                    root = base
                    if not root:
                        return {"ok": False, "provider": prov, "error": "请先填写自定义 Base URL（例如 https://.../v1 或 http://127.0.0.1:11434/v1）", "models": []}
                if prov in ("deepseek", "openai") and not key:
                    return {"ok": False, "provider": prov, "error": f"请先填写 {prov} API Key 再拉取可用模型列表", "models": []}
                if root.endswith("/chat/completions"):
                    root = root[: -len("/chat/completions")]
                url = root if root.endswith("/models") else (root + "/models")
                headers = {}
                if key:
                    headers["Authorization"] = f"Bearer {key}"
                req = urllib.request.Request(url, headers=headers, method="GET")
                with urllib.request.urlopen(req, timeout=12.0) as r:
                    data = json.loads(r.read().decode("utf-8", errors="replace"))
                raw_list = data.get("data") if isinstance(data, dict) else (data if isinstance(data, list) else [])
                for item in raw_list or []:
                    mid = str(item.get("id") if isinstance(item, dict) else item).strip()
                    if mid and _is_chat_model_id(mid):
                        models.append(mid)

            unique = sorted(set(models), key=lambda m: _rank_model_id(m, prov))
            return {"ok": True, "provider": prov, "count": len(unique), "models": unique}
        except Exception as exc:
            return {"ok": False, "provider": prov, "error": str(exc), "models": []}

    def _build_static_sys_prompt(self, lang: str, audio_mode: bool = False) -> str:
        if lang == "ja":
            lang_desc = "自然で甘く色っぽい日本語の囁き"
        elif lang == "en":
            lang_desc = "intimate, affectionate, soft-whispered natural English"
        else:
            lang_desc = "极度自然口语化、娇软贴耳的简体中文"
        user_title = str(self.cfg.get("userTitle") or "主人").strip() or "主人"
        ai_self = str(self.cfg.get("aiSelfRef") or "人家").strip() or "人家"
        persona = str(self.cfg.get("persona") or "温柔体贴、黏人爱撒娇的贴耳亲密伴侣").strip()
        extra = str(self.cfg.get("customPrompt") or "").strip()
        extra_block = f"【玩家自定义补充提示词】\n{extra}\n" if extra else ""
        tpl = DIRECT_AUDIO_PROMPT if audio_mode else SMART_SYSTEM_PROMPT
        return tpl.format(
            user_title=user_title,
            ai_self_ref=ai_self,
            persona=persona,
            custom_prompt_block=extra_block,
            lang_desc=lang_desc,
        )

    def _record_usage(self, provider: str, model: str, hit_tok: int, miss_tok: int, out_tok: int) -> dict:
        tot_in = hit_tok + miss_tok
        rate = round((hit_tok / tot_in) * 100.0, 1) if tot_in > 0 else 0.0
        cost = estimate_rmb_cost(provider, model, hit_tok, miss_tok, out_tok)
        self.last_usage = {
            "cacheHitTokens": hit_tok,
            "cacheMissTokens": miss_tok,
            "outputTokens": out_tok,
            "cacheHitRate": rate,
            "estCostRmb": cost,
        }
        return self.last_usage

    def _record_turn(self, formatted_user_turn: str, raw_assistant_turn: str, clean_reply: str) -> None:
        if not clean_reply:
            return
        self._last_reply = clean_reply
        self.history.append({"role": "user", "content": formatted_user_turn})
        self.history.append({"role": "assistant", "content": raw_assistant_turn})
        if len(self.history) > 24:
            self.history = self.history[-8:]

    def test_connection(self) -> dict:
        prov = str(self.cfg.get("provider", "deepseek") or "deepseek")
        model = str(self.cfg.get("model") or "").strip()
        if prov == "builtin":
            return {"ok": True, "provider": "builtin", "detail": "本地规则模式就绪（无需 API Key）"}
        t0 = time.perf_counter()
        try:
            if prov == "deepseek":
                if not self.cfg.get("deepseekApiKey"):
                    return {"ok": False, "error": "尚未填写 DeepSeek API Key"}
                out, _, _ = self._call_deepseek_stream("请只回复两个字：就绪", "zh", None, None, model_override=model or "deepseek-chat")
            elif prov == "openai":
                if not self.cfg.get("openaiApiKey"):
                    return {"ok": False, "error": "尚未填写 OpenAI API Key"}
                out, _, _ = self._call_openai_stream("请只回复两个字：就绪", "zh", None, None, model_override=model or "gpt-4o-mini")
            elif prov == "claude":
                if not self.cfg.get("claudeApiKey"):
                    return {"ok": False, "error": "尚未填写 Claude API Key"}
                out, _, _ = self._call_claude_stream("请只回复两个字：就绪", "zh", None, None, model_override=model or "claude-sonnet-4-20250514")
            elif prov == "gemini":
                if not self.cfg.get("googleApiKey"):
                    return {"ok": False, "error": "尚未填写 Google Gemini API Key"}
                out, _, _ = self._call_gemini("请只回复两个字：就绪", "zh", model_override=model or "gemini-3.1-flash-lite")
            elif prov == "openai_compat":
                if not str(self.cfg.get("baseUrl") or "").strip():
                    return {"ok": False, "error": "尚未填写自定义 OpenAI 兼容 Base URL"}
                out, _, _ = self._call_openai_compat_stream("请只回复两个字：就绪", "zh", None, None)
            else:
                return {"ok": False, "error": f"未知服务商: {prov}"}
            ms = round((time.perf_counter() - t0) * 1000.0, 1)
            return {"ok": True, "provider": f"{prov}:{model}", "ms": ms, "sample": out.get("reply", "")[:40]}
        except Exception as exc:
            return {"ok": False, "error": str(exc)}

    def respond(
        self,
        user_text: str,
        lang: str,
        game_state: Optional[dict],
        config_state: Optional[dict] = None,
        poses: Optional[List[dict]] = None,
        chars: Optional[dict] = None,
        on_cmd_ready: Optional[Callable[[dict], None]] = None,
        on_reply_ready: Optional[Callable[[str], None]] = None,
    ) -> dict:
        t0 = time.perf_counter()
        prov = str(self.cfg.get("provider", "deepseek") or "deepseek")
        ctx = build_context_header(self.memories, game_state, config_state, poses, chars)
        formatted_user = f"{ctx}\n{user_text}" if ctx else user_text

        try:
            if prov == "deepseek" and self.cfg.get("deepseekApiKey"):
                model = str(self.cfg.get("model") or "deepseek-chat")
                out, raw_asst, usage = self._call_deepseek_stream(formatted_user, lang, on_cmd_ready, on_reply_ready, model_override=model)
                if is_refusal_text(out.get("reply", "")):
                    raise RuntimeError("refusal_detected")
                out["brainMs"] = round((time.perf_counter() - t0) * 1000.0, 1)
                out["provider"] = f"deepseek:{model}"
                out["usage"] = usage
                self._record_turn(formatted_user, raw_asst, out["reply"])
                return out

            if prov == "openai" and self.cfg.get("openaiApiKey"):
                model = str(self.cfg.get("model") or "gpt-4o-mini")
                out, raw_asst, usage = self._call_openai_stream(formatted_user, lang, on_cmd_ready, on_reply_ready, model_override=model)
                if is_refusal_text(out.get("reply", "")):
                    raise RuntimeError("refusal_detected")
                out["brainMs"] = round((time.perf_counter() - t0) * 1000.0, 1)
                out["provider"] = f"openai:{model}"
                out["usage"] = usage
                self._record_turn(formatted_user, raw_asst, out["reply"])
                return out

            if prov == "claude" and self.cfg.get("claudeApiKey"):
                model = str(self.cfg.get("model") or "claude-sonnet-4-20250514")
                out, raw_asst, usage = self._call_claude_stream(formatted_user, lang, on_cmd_ready, on_reply_ready, model_override=model)
                if is_refusal_text(out.get("reply", "")):
                    raise RuntimeError("refusal_detected")
                out["brainMs"] = round((time.perf_counter() - t0) * 1000.0, 1)
                out["provider"] = f"claude:{model}"
                out["usage"] = usage
                self._record_turn(formatted_user, raw_asst, out["reply"])
                return out

            if prov == "gemini" and self.cfg.get("googleApiKey"):
                model = str(self.cfg.get("model") or "gemini-3.1-flash-lite")
                out, raw_asst, usage = self._call_gemini(formatted_user, lang, model_override=model)
                if is_refusal_text(out.get("reply", "")):
                    raise RuntimeError("gemini_refusal_or_blocked")
                if on_cmd_ready and out.get("cmd"):
                    on_cmd_ready(out["cmd"])
                if on_reply_ready and out.get("reply"):
                    on_reply_ready(out["reply"])
                out["brainMs"] = round((time.perf_counter() - t0) * 1000.0, 1)
                out["provider"] = f"gemini:{model}"
                out["usage"] = usage
                self._record_turn(formatted_user, raw_asst, out["reply"])
                return out

            if prov == "openai_compat" and self.cfg.get("baseUrl"):
                out, raw_asst, usage = self._call_openai_compat_stream(formatted_user, lang, on_cmd_ready, on_reply_ready)
                if is_refusal_text(out.get("reply", "")):
                    raise RuntimeError("refusal_detected")
                out["brainMs"] = round((time.perf_counter() - t0) * 1000.0, 1)
                out["provider"] = f"custom:{self.cfg.get('model') or 'custom'}"
                out["usage"] = usage
                self._record_turn(formatted_user, raw_asst, out["reply"])
                return out
        except Exception as exc:
            print(f"[BRAIN] Primary '{prov}' failed/blocked ({exc}); checking fallback...", flush=True)
            if prov != "deepseek" and self.cfg.get("deepseekApiKey"):
                try:
                    # Never send deepseekApiKey to another provider's custom baseUrl during failover (fixes P1-10)
                    out, raw_asst, usage = self._call_deepseek_stream(
                        formatted_user, lang, on_cmd_ready, on_reply_ready, model_override="deepseek-chat", use_custom_base=False
                    )
                    if not is_refusal_text(out.get("reply", "")):
                        out["brainMs"] = round((time.perf_counter() - t0) * 1000.0, 1)
                        out["provider"] = "deepseek:deepseek-chat(自动接管)"
                        out["usage"] = usage
                        self._record_turn(formatted_user, raw_asst, out["reply"])
                        return out
                except Exception as exc2:
                    print(f"[BRAIN] DeepSeek failover also failed: {exc2}", flush=True)
            fb = self._builtin_respond(user_text, lang, game_state)
            if on_cmd_ready and fb.get("cmd"):
                on_cmd_ready(fb["cmd"])
            if on_reply_ready and fb.get("reply"):
                on_reply_ready(fb["reply"])
            fb["brainMs"] = round((time.perf_counter() - t0) * 1000.0, 1)
            fb["provider"] = f"builtin(兜底:{str(exc)[:38]})"
            fb["usage"] = self.last_usage
            self._record_turn(formatted_user, fb["reply"], fb["reply"])
            return fb

        out = self._builtin_respond(user_text, lang, game_state)
        if on_cmd_ready and out.get("cmd"):
            on_cmd_ready(out["cmd"])
        if on_reply_ready and out.get("reply"):
            on_reply_ready(out["reply"])
        out["brainMs"] = round((time.perf_counter() - t0) * 1000.0, 1)
        out["provider"] = "builtin"
        out["usage"] = self.last_usage
        self._record_turn(formatted_user, out["reply"], out["reply"])
        return out

    def respond_to_audio(
        self,
        wav_bytes: bytes,
        lang: str,
        game_state: Optional[dict],
        config_state: Optional[dict] = None,
        poses: Optional[List[dict]] = None,
        chars: Optional[dict] = None,
    ) -> dict:
        t0 = time.perf_counter()
        key = self.get_google_key()
        if not key:
            raise RuntimeError("未配置 Google API Key，无法使用 Gemini 原生音频直连")
        model = self.cfg.get("model") if str(self.cfg.get("provider")) == "gemini" else "gemini-3.1-flash-lite"
        if not model or "transcribe" in model or "deepseek" in model:
            model = "gemini-3.1-flash-lite"

        base = (str(self.cfg.get("baseUrl") or "").strip().rstrip("/") if str(self.cfg.get("provider")) == "gemini" else "") or "https://generativelanguage.googleapis.com/v1beta"
        url = f"{base}/models/{model}:generateContent?key={urllib.parse.quote(key, safe='')}"
        sys_msg = self._build_static_sys_prompt(lang, audio_mode=True)
        ctx = build_context_header(self.memories, game_state, config_state, poses, chars)
        prompt_tail = f"{ctx}\n请听刚才这段语音并按格式先给 CMD 再贴耳回应。" if ctx else "请听刚才这段语音并按格式先给 CMD 再贴耳回应。"

        contents = []
        for h in self.history:
            contents.append({"role": "user" if h["role"] == "user" else "model", "parts": [{"text": h["content"]}]})
        contents.append({
            "role": "user",
            "parts": [
                {"inlineData": {"mimeType": "audio/wav", "data": base64.b64encode(wav_bytes).decode("ascii")}},
                {"text": prompt_tail},
            ],
        })
        body = {
            "systemInstruction": {"parts": [{"text": sys_msg}]},
            "contents": contents,
            "generationConfig": {"temperature": float(self.cfg.get("temperature", 1.0)), "maxOutputTokens": 300},
            "safetySettings": SAFETY_OFF,
        }
        req = urllib.request.Request(
            url,
            data=json.dumps(body, ensure_ascii=False).encode("utf-8"),
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        with urllib.request.urlopen(req, timeout=22.0) as r:
            res = json.loads(r.read().decode("utf-8", errors="replace"))

        cands = res.get("candidates") or []
        if not cands:
            raise RuntimeError(f"gemini_audio_blocked:{res.get('promptFeedback')}")
        raw = "".join(p.get("text", "") for p in (cands[0].get("content") or {}).get("parts") or []).strip()

        heard = ""
        m_heard = re.search(r"\[HEARD\s*:\s*([^\]]*)\]", raw, flags=re.I)
        if m_heard:
            heard = m_heard.group(1).strip()
            raw = raw[:m_heard.start()] + raw[m_heard.end():]
        parsed = parse_smart_output(raw)
        if is_refusal_text(parsed.get("reply", "")):
            raise RuntimeError("gemini_audio_refusal")

        um = res.get("usageMetadata") or {}
        hit = int(um.get("cachedContentTokenCount", 0) or 0)
        tot_in = int(um.get("promptTokenCount", 0) or 0)
        out_tok = int(um.get("candidatesTokenCount", 0) or 0)
        usage = self._record_usage("gemini", model, hit, max(0, tot_in - hit), out_tok)

        if heard:
            formatted = f"{ctx}\n{heard}" if ctx else heard
            self._record_turn(formatted, raw.strip(), parsed["reply"])
        parsed["userText"] = heard
        parsed["brainMs"] = round((time.perf_counter() - t0) * 1000.0, 1)
        parsed["provider"] = f"gemini-direct-audio:{model}"
        parsed["usage"] = usage
        return parsed

    def _stream_sse_chat(
        self,
        url: str,
        headers: dict,
        body: dict,
        provider_tag: str,
        model_tag: str,
        on_cmd_ready: Optional[Callable[[dict], None]] = None,
        on_reply_ready: Optional[Callable[[str], None]] = None,
    ) -> Tuple[dict, str, dict]:
        """Action-first SSE parser: executes Line-1 CMD immediately on arrival, THEN speaks Line-2!"""
        req = urllib.request.Request(
            url,
            data=json.dumps(body, ensure_ascii=False).encode("utf-8"),
            headers=headers,
            method="POST",
        )
        buf = ""
        cmd_fired = False
        reply_fired = False
        usage_obj: dict = {}
        with urllib.request.urlopen(req, timeout=25.0) as r:
            for raw in r:
                line = raw.decode("utf-8", errors="replace").strip()
                if not line.startswith("data:"):
                    continue
                payload = line[5:].strip()
                if payload == "[DONE]":
                    break
                try:
                    j = json.loads(payload)
                    if isinstance(j.get("usage"), dict) and j["usage"]:
                        usage_obj = j["usage"]
                    choices = j.get("choices") or []
                    if choices:
                        delta = choices[0].get("delta", {}).get("content") or ""
                        if delta:
                            buf += delta
                            if not cmd_fired and on_cmd_ready and "CMD:" in buf.upper():
                                parsed_cmd, _, end_i = extract_cmd_json(buf)
                                if parsed_cmd is not None and end_i > 0:
                                    cmd_fired = True
                                    on_cmd_ready(parsed_cmd)
                except Exception:
                    pass

        parsed = parse_smart_output(buf)
        if not cmd_fired and on_cmd_ready and parsed.get("cmd"):
            cmd_fired = True
            on_cmd_ready(parsed["cmd"])
        if not reply_fired and on_reply_ready and parsed.get("reply") and not is_refusal_text(parsed["reply"]):
            reply_fired = True
            on_reply_ready(parsed["reply"])

        hit = int(
            usage_obj.get("prompt_cache_hit_tokens")
            or (usage_obj.get("prompt_tokens_details") or {}).get("cached_tokens")
            or 0
        )
        miss = int(usage_obj.get("prompt_cache_miss_tokens") or max(0, int(usage_obj.get("prompt_tokens", 0) or 0) - hit))
        out_tok = int(usage_obj.get("completion_tokens", 0) or 0)
        usage = self._record_usage(provider_tag, model_tag, hit, miss, out_tok)
        return parsed, buf.strip(), usage

    def _call_claude_stream(
        self,
        formatted_user: str,
        lang: str,
        on_cmd_ready: Optional[Callable[[dict], None]] = None,
        on_reply_ready: Optional[Callable[[str], None]] = None,
        model_override: Optional[str] = None,
    ) -> Tuple[dict, str, dict]:
        """Native Anthropic Claude /v1/messages SSE streaming (with prompt caching + action-first CMD execution!)."""
        model = (model_override or str(self.cfg.get("model") or "claude-sonnet-4-20250514")).strip()
        sys_msg = self._build_static_sys_prompt(lang)
        msgs = [*self.history, {"role": "user", "content": formatted_user}]
        body = {
            "model": model,
            "max_tokens": 340,
            "temperature": min(1.0, float(self.cfg.get("temperature", 0.95))),
            "stream": True,
            "system": [{"type": "text", "text": sys_msg, "cache_control": {"type": "ephemeral"}}],
            "messages": msgs,
        }
        base = str(self.cfg.get("baseUrl") or "").strip().rstrip("/")
        if base:
            url = base if base.endswith("/messages") else (base + ("/messages" if base.endswith("/v1") else "/v1/messages"))
        else:
            url = "https://api.anthropic.com/v1/messages"
        headers = {
            "Content-Type": "application/json",
            "x-api-key": str(self.cfg.get("claudeApiKey") or "").strip(),
            "anthropic-version": "2023-06-01",
        }
        req = urllib.request.Request(url, data=json.dumps(body, ensure_ascii=False).encode("utf-8"), headers=headers, method="POST")
        buf = ""
        cmd_fired = False
        hit_tok = miss_tok = out_tok = 0
        with urllib.request.urlopen(req, timeout=25.0) as r:
            for raw in r:
                line = raw.decode("utf-8", errors="replace").strip()
                if not line.startswith("data:"):
                    continue
                payload = line[5:].strip()
                if not payload or payload == "[DONE]":
                    continue
                try:
                    j = json.loads(payload)
                    et = j.get("type", "")
                    if et == "message_start":
                        u = (j.get("message") or {}).get("usage") or {}
                        hit_tok = int(u.get("cache_read_input_tokens", 0) or 0)
                        miss_tok = int(u.get("input_tokens", 0) or 0) + int(u.get("cache_creation_input_tokens", 0) or 0)
                    elif et == "content_block_delta":
                        delta = (j.get("delta") or {}).get("text") or ""
                        if delta:
                            buf += delta
                            if not cmd_fired and on_cmd_ready and "CMD:" in buf.upper():
                                parsed_cmd, _, end_i = extract_cmd_json(buf)
                                if parsed_cmd is not None and end_i > 0:
                                    cmd_fired = True
                                    on_cmd_ready(parsed_cmd)
                    elif et == "message_delta":
                        u = j.get("usage") or {}
                        out_tok = int(u.get("output_tokens", out_tok) or out_tok)
                except Exception:
                    pass

        parsed = parse_smart_output(buf)
        if not cmd_fired and on_cmd_ready and parsed.get("cmd"):
            on_cmd_ready(parsed["cmd"])
        if on_reply_ready and parsed.get("reply") and not is_refusal_text(parsed["reply"]):
            on_reply_ready(parsed["reply"])
        usage = self._record_usage("claude", model, hit_tok, miss_tok, out_tok)
        return parsed, buf.strip(), usage

    def _call_deepseek_stream(
        self,
        formatted_user: str,
        lang: str,
        on_cmd_ready: Optional[Callable[[dict], None]] = None,
        on_reply_ready: Optional[Callable[[str], None]] = None,
        model_override: Optional[str] = None,
        use_custom_base: bool = True,
    ) -> Tuple[dict, str, dict]:
        model = (model_override or str(self.cfg.get("model") or "deepseek-chat")).strip() or "deepseek-chat"
        sys_msg = self._build_static_sys_prompt(lang)
        messages = [{"role": "system", "content": sys_msg}, *self.history, {"role": "user", "content": formatted_user}]
        body: dict = {
            "model": model,
            "messages": messages,
            "temperature": float(self.cfg.get("temperature", 1.0)),
            "max_tokens": 300,
            "stream": True,
            "stream_options": {"include_usage": True},
        }
        if self.cfg.get("thinkingDisabled", True) and model in ("deepseek-v4-pro", "deepseek-flash"):
            body["thinking"] = {"type": "disabled"}
        headers = {
            "Content-Type": "application/json",
            "Authorization": f"Bearer {self.cfg['deepseekApiKey']}",
        }
        custom_base = str(self.cfg.get("baseUrl") or "").strip().rstrip("/") if use_custom_base else ""
        url = (custom_base if custom_base.endswith("/chat/completions") else (custom_base + "/chat/completions")) if custom_base else "https://api.deepseek.com/v1/chat/completions"
        return self._stream_sse_chat(url, headers, body, "deepseek", model, on_cmd_ready, on_reply_ready)

    def _call_openai_stream(
        self,
        formatted_user: str,
        lang: str,
        on_cmd_ready: Optional[Callable[[dict], None]] = None,
        on_reply_ready: Optional[Callable[[str], None]] = None,
        model_override: Optional[str] = None,
    ) -> Tuple[dict, str, dict]:
        model = (model_override or str(self.cfg.get("model") or "gpt-4o-mini")).strip() or "gpt-4o-mini"
        sys_msg = self._build_static_sys_prompt(lang)
        messages = [{"role": "system", "content": sys_msg}, *self.history, {"role": "user", "content": formatted_user}]
        body: dict = {
            "model": model,
            "messages": messages,
            "temperature": float(self.cfg.get("temperature", 0.95)),
            "max_tokens": 320,
            "stream": True,
            "stream_options": {"include_usage": True},
        }
        headers = {
            "Content-Type": "application/json",
            "Authorization": f"Bearer {self.cfg['openaiApiKey']}",
        }
        custom_base = str(self.cfg.get("baseUrl") or "").strip().rstrip("/")
        url = (custom_base if custom_base.endswith("/chat/completions") else (custom_base + "/chat/completions")) if custom_base else "https://api.openai.com/v1/chat/completions"
        return self._stream_sse_chat(url, headers, body, "openai", model, on_cmd_ready, on_reply_ready)

    def _call_openai_compat_stream(
        self,
        formatted_user: str,
        lang: str,
        on_cmd_ready: Optional[Callable[[dict], None]] = None,
        on_reply_ready: Optional[Callable[[str], None]] = None,
    ) -> Tuple[dict, str, dict]:
        base = str(self.cfg.get("baseUrl", "")).strip().rstrip("/")
        url = base if base.endswith("/chat/completions") else (base + "/chat/completions")
        model = str(self.cfg.get("model") or "gpt-4o-mini").strip()
        sys_msg = self._build_static_sys_prompt(lang)
        messages = [{"role": "system", "content": sys_msg}, *self.history, {"role": "user", "content": formatted_user}]
        body = {
            "model": model,
            "messages": messages,
            "temperature": float(self.cfg.get("temperature", 0.95)),
            "max_tokens": 320,
            "stream": True,
        }
        headers = {"Content-Type": "application/json"}
        if self.cfg.get("customApiKey"):
            headers["Authorization"] = f"Bearer {self.cfg['customApiKey']}"
        return self._stream_sse_chat(url, headers, body, "openai", model, on_cmd_ready, on_reply_ready)

    def _call_gemini(
        self,
        formatted_user: str,
        lang: str,
        model_override: Optional[str] = None,
    ) -> Tuple[dict, str, dict]:
        model = (model_override or str(self.cfg.get("model") or "gemini-3.1-flash-lite")).strip()
        key = self.cfg["googleApiKey"]
        base = str(self.cfg.get("baseUrl") or "").strip().rstrip("/") or "https://generativelanguage.googleapis.com/v1beta"
        url = f"{base}/models/{model}:generateContent?key={urllib.parse.quote(key, safe='')}"
        sys_msg = self._build_static_sys_prompt(lang)
        contents = []
        for h in self.history:
            contents.append({"role": "user" if h["role"] == "user" else "model", "parts": [{"text": h["content"]}]})
        contents.append({"role": "user", "parts": [{"text": formatted_user}]})
        gen_cfg: dict = {
            "temperature": float(self.cfg.get("temperature", 1.0)),
            "maxOutputTokens": 320,
        }
        body = {
            "systemInstruction": {"parts": [{"text": sys_msg}]},
            "contents": contents,
            "generationConfig": gen_cfg,
            "safetySettings": SAFETY_OFF,
        }
        req = urllib.request.Request(
            url,
            data=json.dumps(body, ensure_ascii=False).encode("utf-8"),
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        with urllib.request.urlopen(req, timeout=20.0) as r:
            res = json.loads(r.read().decode("utf-8", errors="replace"))
        cands = res.get("candidates") or []
        if not cands:
            raise RuntimeError(f"gemini_blocked:{res.get('promptFeedback')}")
        parts = (cands[0].get("content") or {}).get("parts") or []
        txt = "".join(p.get("text", "") for p in parts).strip()
        um = res.get("usageMetadata") or {}
        hit = int(um.get("cachedContentTokenCount", 0) or 0)
        tot_in = int(um.get("promptTokenCount", 0) or 0)
        out_tok = int(um.get("candidatesTokenCount", 0) or 0)
        usage = self._record_usage("gemini", model, hit, max(0, tot_in - hit), out_tok)
        return parse_smart_output(txt), txt, usage

    def _builtin_respond(self, user_text: str, lang: str, game_state: Optional[dict]) -> dict:
        t = user_text.lower()
        title = str(self.cfg.get("userTitle") or "主人").strip() or "主人"
        inserted = bool((game_state or {}).get("isInserted", False))

        if any(k in t for k in ("换人面板", "邀请面板", "打开面板")):
            return {"reply": f"好的，已帮{title}打开换人/邀请面板。", "cmd": {"chara": {"action": "open_invite_panel"}, "expr": "sweet_smile"}}
        if any(k in t for k in ("正常位", "传教士", "missionary")):
            return {"reply": f"好呀{title}，这就切换到正常位并慢慢开始……", "cmd": {"switchPose": "正常位", "autoInsert": True, "expr": "shy_tease"}}
        if any(k in t for k in ("骑乘", "乘骑", "騎乗", "女上位", "坐上来", "cowgirl")):
            return {"reply": f"嗯……那我坐上来带着{title}慢慢动……", "cmd": {"switchPose": "騎乗位", "autoInsert": True, "expr": "shy_tease"}}
        if any(k in t for k in ("后背", "後背", "背后", "后入", "doggy")):
            return {"reply": f"好……转过去交给{title}，慢慢开始吧……", "cmd": {"switchPose": "後背位", "autoInsert": True, "expr": "shy_tease"}}
        if any(k in t for k in ("面对面", "对面")):
            return {"reply": f"面对面贴紧{title}，我们开始吧……", "cmd": {"switchPose": "対面", "autoInsert": True, "expr": "sweet_smile"}}
        if any(k in t for k in ("侧卧", "侧位", "側位")):
            return {"reply": f"嗯……侧躺着靠进{title}怀里开始……", "cmd": {"switchPose": "側位", "autoInsert": True, "expr": "sweet_smile"}}
        if any(k in t for k in ("插入", "进来", "开始吧", "开始做", "来吧", "同意", "好呀", "insert", "start")) and not inserted:
            return {"reply": f"好，这就温柔进入并开始律动……", "cmd": {"gameAction": "start_insert", "expr": "shy_tease"}}
        if any(k in t for k in ("爱抚", "前戏", "先摸", "foreplay")):
            return {"reply": f"好呀，先从温柔的爱抚慢慢开始……", "cmd": {"gameAction": "aibu_attach", "expr": "sweet_smile"}}
        if any(k in t for k in ("全脱", "脱光", "脱衣", "脱掉", "把衣服脱", "undress")):
            return {"reply": f"嗯……这就乖乖把衣服褪下来……", "cmd": {"clothes": {"mode": "undress"}, "expr": "shy_tease"}}
        if any(k in t for k in ("半脱",)):
            return {"reply": "好……先把外衣褪到一半……", "cmd": {"clothes": {"mode": "half"}, "expr": "shy_tease"}}
        if any(k in t for k in ("穿上", "穿衣")):
            return {"reply": "好的，把衣服整理穿好……", "cmd": {"clothes": {"mode": "dress"}, "expr": "sweet_smile"}}
        if any(k in t for k in ("停止", "释放", "停下", "还给游戏", "release")):
            return {"reply": "好的，已释放导演接管。", "cmd": {"release": True, "expr": "sweet_smile"}}

        if inserted:
            # Arbitrary N浅M深 free combination regex (supports 一/1浅一/1深, 3浅2深, 9浅1深, etc.!)
            cn_num = {"一": 1, "二": 2, "两": 2, "三": 3, "四": 4, "五": 5, "六": 6, "七": 7, "八": 8, "九": 9, "十": 10}
            m_sd = re.search(r"([0-9一二两三四五六七八九十]+)\s*浅\s*([0-9一二两三四五六七八九十]+)\s*深", user_text)
            if m_sd:
                sc = cn_num.get(m_sd.group(1), int(m_sd.group(1)) if m_sd.group(1).isdigit() else 1)
                dc = cn_num.get(m_sd.group(2), int(m_sd.group(2)) if m_sd.group(2).isdigit() else 1)
                return {
                    "reply": f"好呀{title}……这就为你组合 {sc} 浅 {dc} 深的节奏，慢慢感受哦……",
                    "cmd": {
                        "timeline": {
                            "preset": "shallow_deep", "shallowCount": sc, "deepCount": dc,
                            "freqHz": 1.65, "shallowDepthMax": 0.42, "depthMax": 0.98, "deepHoldSeconds": 0.45, "loop": True,
                        },
                        "expr": "happy_trance",
                    },
                }
            # Straight waist-only / R1 R2 linear swaying (no circle, no R0 rotation!)
            if any(k in t for k in ("只扭腰", "只摆腰", "前后摆", "左右摆", "不画圈", "直线", "r1", "r2")):
                pat = "line_lr" if ("左右" in t or ("r1" in t and "r2" not in t and "前后" not in t)) else (
                    "line_r1r2" if ("r1" in t and "r2" in t) else "line_fb"
                )
                waist_only = any(k in t for k in ("只扭腰", "只摆腰", "不抽插", "停在里面"))
                tl_obj = (
                    {"preset": "waist_only", "holdDepth": 0.85, "grindPattern": pat, "grindAmp": 0.38, "grindHz": 1.3, "twistAmp": 0.0, "loop": True}
                    if waist_only else
                    {"preset": "deep_grind", "grindPattern": pat, "grindAmp": 0.38, "grindHz": 1.3, "twistAmp": 0.0, "loop": True}
                )
                return {
                    "reply": f"好呀{title}……不画圈也不打转，我稳稳地走直线摆腰贴着你……",
                    "cmd": {"config": {"axes": {"R0": {"enabled": False}}}, "timeline": tl_obj, "expr": "shy_tease"},
                }
            if any(k in t for k in ("高潮", "射", "一起", "climax", "cum")):
                act = "finish_outside" if "外" in t else ("finish_inside" if "内" in t else "seamless_climax")
                return {"reply": f"嗯……抱紧我{title}，我们一起到顶……", "cmd": {"gameAction": act, "expr": "climax"}}
            if any(k in t for k in ("别旋转", "不要旋转", "别打转")):
                return {"reply": "好呀，已关闭旋转并放柔和……", "cmd": {"config": {"axes": {"R0": {"enabled": False}}, "smoothingMs": 48}, "timelineTune": {"twistAmp": 0.0}, "expr": "sweet_smile"}}
            if any(k in t for k in ("快一点", "加快", "用力", "faster")):
                return {"reply": "嗯……帮你加快节奏……", "cmd": {"timelineTune": {"freqHz": 2.2}, "expr": "happy_trance"}}
            if any(k in t for k in ("慢", "温柔", "轻", "slower", "gentle")):
                return {"reply": "好呀……放慢节奏，温柔顺滑地陪着你……", "cmd": {"timelineTune": {"freqHz": 0.9}, "expr": "sweet_smile"}}

        prov = str(self.cfg.get("provider") or "deepseek")
        if prov != "builtin" and not self.public_config().get("hasActiveKey"):
            return {
                "reply": f"当前尚未填写 API Key（默认空白配置）。请先在设置页填写你自己的 API Key，或直接说「乘骑位 / 1浅1深 / 只前后摆腰不画圈 / 高潮 / 脱衣」使用本地指令执行。",
                "cmd": {"expr": "sweet_smile"},
            }
        if lang == "en":
            return {"reply": "Mmm... I'm right here with you... tell me how you'd like to move.", "cmd": {"expr": "sweet_smile"}}
        if lang == "ja":
            return {"reply": "んっ……ちゃんと聞いてますよ……次はどうしてほしいですか？", "cmd": {"expr": "sweet_smile"}}
        return {"reply": f"嗯……我一直在听{title}说话呢，想换乘骑位、1浅1深、还是只前后摆腰，告诉我一声就好……", "cmd": {"expr": "sweet_smile"}}
