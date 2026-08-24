# 彼岸 AI Media OS 架构

## 总体目标

zero 不再定位为单一 Android 工程，而是 AI 媒体生产基础设施。

## 架构

```
User
 |
ChatGPT Custom GPT
 |
GPT Actions
 |
Task Schema
 |
Workflow Engine
 |
Provider Layer
 |
Delivery Layer
 |
Artifact / Drive
```

## 模块

### GPT Brain
负责理解用户意图、选择任务类型、生成参数。

### Task Engine
负责统一 request_id、状态、幂等控制。

### Provider Layer
隔离不同 AI 服务：

- Google
- 豆包
- OpenAI
- 未来模型

### Delivery Layer
负责：

- Artifact
- Google Drive
- 文件交付

## 设计原则

1. 不重复消耗模型资源。
2. 一个 request_id 对应一个真实生成任务。
3. Provider 可以替换。
4. 媒体类型保持独立。
