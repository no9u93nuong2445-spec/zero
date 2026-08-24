# V4 迁移与加固计划

## 为什么不是直接覆盖 V3

现网真正执行媒体生成的是 `doubao-tts-bridge`。V3 已经具备 request_id、防重复、状态账本、批量视频和 Artifact 交付，因此 V4 首先升级控制面契约，避免一次性改动生产链路。

建议顺序：

1. 合并 `zero` 的 V4 控制面候选；
2. 在 Custom GPT 测试环境导入 `media-v4.0.0.json` + `instructions-v4.txt`；
3. 只做无成本/低成本参数验证；
4. 运行现有 execution self-test；
5. 再处理执行面 hardening；
6. 验证后才把 V4 设为自定义 GPT 主配置。

## 已发现的 V3 契约漂移

### 1. 音乐 prompt 长度不一致

V3 Action 使用共享 `prompt maxLength=12000`，执行 registry 的音乐上限是 8000。

影响：GPT 可能认为请求合法，但执行面才拒绝。

V4：按事件类型拆分 payload，音乐限制为 8000。

### 2. 视频能力约束过宽

V3 Action 对所有视频统一允许 3-10 秒、720p/1080p/4k；执行 registry 实际是：

- Omni：3-10 秒，仅 720p；
- Veo / Veo Fast：4/6/8 秒，720p/1080p/4k；
- Veo Lite：4/6/8 秒，720p/1080p；
- Veo 1080p/4k：执行桥接器要求 8 秒。

V4：控制面提前说明并约束，最终仍以 execution registry 为准。

### 3. batch request_id 长度不同

单任务后端允许 4-100；批任务后端只允许 4-90，以便派生 `-s01...-s05`。

V4：分开定义 RequestId / BatchRequestId。

### 4. V3 MediaPayload 跨事件过宽

当前一个 `MediaPayload` 同时包含 TTS、音乐、视频、批量字段，只要求 request_id。

影响：

- google_tts 可以缺 text；
- google_music 可以缺 prompt；
- google_video_batch 可以缺 segments；
- TTS payload 理论上可以携带视频模型名，直到后端才失败。

V4：请求体使用按 event_type 区分的 event-specific payload。

## 执行面下一阶段需要加固的点

### A. stale in_progress 恢复策略

当前单任务在调用 provider 前先写 `in_progress`。如果 runner 在真正调用 provider 之前异常退出，后续同 request_id 会因为 `in_progress` 被阻止，从而永久卡住。

推荐 V4.1：

```text
accepted
↓
provider_call_armed
↓
provider_call_started / uncertain
↓
success | failed
```

只有能证明 `provider_call_armed=false/provider_called=false` 且从未进入 provider call 的任务，才允许安全恢复；一旦存在 provider 调用不确定性，继续保持禁止自动重试。

不要用“超时 N 分钟就自动重试 provider”的粗暴方案。

### B. Artifact retention 单一真源

执行 registry 写 `artifact_retention_days=14`，生产 workflow 的 `upload-artifact` 使用 `retention-days: 30`。

推荐：统一由一个 registry/config 生成 workflow 参数，或者至少 CI 检查二者一致。

### C. 版本命名收敛

当前同时存在：

- bridge 2.1.0
- fixed wrapper V3.0 Omni 能力
- workflow summary V3.0.6
- Action 3.0.1

推荐拆成：

- protocol_version
- control_plane_version
- execution_version
- provider_adapter_version

避免一个“V3”同时表示四种东西。

### D. Control/Execution 契约 CI

推荐增加 CI：

1. 读取 execution model registry；
2. 读取 zero 的 V4 schema/OpenAPI；
3. 比较模型白名单、prompt 上限、duration、resolution；
4. 发现漂移直接失败。

这是 V4 后续最值得做的自动化之一。

### E. GCS 直链与 Artifact 语义统一

当前执行面成功时可同时提供：

- GitHub Artifact ZIP；
- verified GCS signed direct URL（约 7 天）。

建议语义固定：

- direct_url = 便捷、可能过期；
- Artifact = 稳定备份、受 retention 限制；
- Drive = 用户明确要求时的二次交付；
- 任何交付问题都不重新生成媒体。

## V4 发布检查表

- [ ] V3 文件保持不变，可随时回滚。
- [ ] V4 OpenAPI 可被 Custom GPT 正常导入。
- [ ] TTS 缺 text 时控制面拒绝。
- [ ] Music 缺 prompt 时控制面拒绝。
- [ ] Music prompt >8000 时控制面拒绝。
- [ ] Batch 缺 segments 时控制面拒绝。
- [ ] Batch >5 段时控制面拒绝。
- [ ] Omni 1080p/4k 不应被建议。
- [ ] Veo Lite 4k 不应被建议。
- [ ] 同一 request_id 不发生第二次创建提交。
- [ ] status 查询可重复。
- [ ] Artifact 下载可重复。
- [ ] 下载/Drive 错误不触发 provider。
- [ ] batch partial failure 能说明成功片段仍可能存在。
