# 彼岸 AI Media OS · Control Plane

> `zero` 的主定位：**ChatGPT 自定义 GPT 的媒体控制面**。
>
> 这里负责 GPT 指令、Actions/OpenAPI 契约、路由规则、任务协议与架构文档；真正的媒体生成、状态账本和 Artifact/GCS 交付由私有执行仓库 `doubao-tts-bridge` 承担。原「净画 Android」构建内容继续作为遗留/附属模块保留。

## 当前稳定版本

- GPT 指令：`gpt-actions/instructions.txt`（V3，现网兼容）
- 媒体 Action：`gpt-actions/media.json` / `media.yaml`（V3.0.1）
- Gemini 文本 Action：`gpt-actions/gemini.json` / `gemini.yaml`

## V4 升级候选

V4 不直接覆盖 V3，而是先并行提供候选配置，验证后再切换自定义 GPT：

- `gpt-actions/instructions-v4.txt`
- `gpt-actions/media-v4.0.0.json`
- `schemas/media_task_schema.json`
- `router/media_router.json`
- `docs/ARCHITECTURE.md`
- `docs/V4-MIGRATION.md`

## 两仓职责

```text
User
  ↓
ChatGPT / Custom GPT
  ↓
zero  ── Control Plane
  ├─ instructions
  ├─ OpenAPI contract
  ├─ intent routing
  └─ task/status contract
  ↓
doubao-tts-bridge ── Execution Plane
  ├─ GitHub Actions gateway
  ├─ request_id durable ledger
  ├─ Google media bridge
  ├─ Drive/GCS input-output helpers
  └─ GitHub Actions Artifact
```

### Control Plane（本仓库）

负责：

- 判断用户是在聊天、写作，还是发起真实媒体生成；
- 将真实生成映射成 `google_tts`、`google_music`、`google_video`、`google_video_batch`；
- 生成唯一 `request_id`；
- 只提交一次创建型请求；
- 查询持久状态并交付已有结果；
- 约束模型、时长、分辨率、批量段数和图片输入字段。

### Execution Plane（doubao-tts-bridge）

负责：

- GitHub `repository_dispatch` / 文件队列入口；
- payload 二次校验；
- provider 调用；
- 防重复状态账本；
- 批量视频并行生成；
- Artifact 与可选 GCS 直链交付。

## 核心原则

1. **创建请求绝不自动重试。**
2. **同一个 request_id 永远代表同一份内容和参数。**
3. **聊天、文案、剧本、分镜、提示词默认由 ChatGPT 完成，不触发付费媒体生成。**
4. **TTS、音乐、视频互相独立，不自动串成流水线。**
5. **成功必须由持久状态账本确认；“GitHub 已接受请求”不等于生成成功。**
6. **交付失败不等于生成失败，下载/Drive/ZIP 问题不得触发重新生成。**
7. **运行时模型白名单以执行仓库的模型注册表为最终真源，控制面只做前置约束。**

## Android 遗留模块

仓库仍保留净画 Android / APK 构建相关内容，但它不再代表仓库主定位。

## 关联项目

- `doubao-tts-bridge`：媒体执行面与生产工作流
- `my-video-portfolio`：作品展示层
- `my-website`：产品应用层
