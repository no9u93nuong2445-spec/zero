# 彼岸 AI Media OS V4 架构

## 目标

V4 不把所有东西都塞进 `zero`。系统拆成两个明确边界：

- **Control Plane：`zero`** —— GPT 指令、Action/OpenAPI、路由、任务契约、迁移文档。
- **Execution Plane：`doubao-tts-bridge`** —— GitHub Actions、持久状态账本、Provider Bridge、输入水合、Artifact/GCS 交付。

这样可以避免控制逻辑、Provider 实现和 Android 遗留代码互相耦合。

## 主链路

```text
User
  ↓
ChatGPT / Custom GPT
  ↓
Intent classification
  ↓
zero / Control Plane
  ├─ instructions-v4
  ├─ media Action contract
  ├─ media_router
  └─ media task schema
  ↓
GitHub repository_dispatch 或文件队列
  ↓
doubao-tts-bridge / Execution Plane
  ├─ payload validation
  ├─ durable request_id ledger
  ├─ Google media provider bridge
  ├─ Drive / staged image input
  ├─ single or batch generation
  └─ Artifact + optional verified GCS direct links
  ↓
Status ledger
  ↓
ChatGPT 查询并交付已有结果
```

## 真源（Source of Truth）

### 1. 模型能力真源

执行仓库的 `config/google_model_registry_v2_1.json` 是运行时最终真源。

控制面的 OpenAPI/schema 负责尽量提前阻止错误参数，但不得独立维护另一套“最终模型注册表”，否则会产生版本漂移。

### 2. 幂等真源

`.gpt-media-status/{request_id}.json` 是持久任务状态真源。

同一 request_id：

- 相同 fingerprint：复用/阻止重复生成；
- 不同 fingerprint：视为冲突；
- 已进入不确定或终态：不得自动重新调用 provider。

### 3. 生成结果真源

只有状态账本确认 `status=success` 且存在已验证交付信息时，GPT 才能说“已经生成”。

GitHub 返回 204 只表示任务被接受。

## 任务分类

### 非生成任务

聊天、文案、剧本、分镜、提示词、分析默认由 ChatGPT 本身完成，不触发媒体 Provider。

### 真实生成任务

- `google_tts`
- `google_music`
- `google_video`
- `google_video_batch`

TTS、音乐、视频彼此独立。除非用户明确要求，不自动串成一条付费流水线。

## 视频默认策略

默认：

- `video_mode=omni`
- `aspect_ratio=9:16`
- `duration_seconds=10`
- `resolution=720p`

运行时能力以模型注册表为准。当前执行面约束包括：

- Omni：3-10 秒、仅 720p；
- Veo / Veo Fast：4/6/8 秒，720p/1080p/4k；
- Veo Lite：4/6/8 秒，720p/1080p；
- 1080p/4k 的 Veo 请求运行时要求 8 秒。

## 图片输入

自定义 GPT 不应假设自己可以把聊天附件写入 Drive。

允许的稳定方式：

1. 已有授权 Drive file_id；
2. 在具备 Google Drive 写入连接的普通/项目 Chat 中先上传，再把 file_id 交给媒体任务；
3. 执行面明确支持的 staged/chat attachment 通道（仅由对应入口使用，不在 GPT Action 中伪造）。

## 状态生命周期

主要状态：

```text
in_progress
  ↓
success
failed
duplicate_or_uncertain_blocked
request_id_conflict
provider_uncertain
```

查询状态和下载 Artifact 都是只读操作，可以安全重复；创建请求不能自动重复。

## V4 的关键改进

1. Action payload 按事件类型拆分，不再用一个过宽的 MediaPayload。
2. 音乐 prompt 上限与执行注册表对齐为 8000。
3. 视频时长/分辨率限制在控制面提前说明并校验。
4. 批任务 request_id 上限与执行工作流对齐为 90。
5. 明确 zero 是控制面，避免重复实现 Provider Registry。
6. V3 稳定配置与 V4 候选并存，验证后再切换。
