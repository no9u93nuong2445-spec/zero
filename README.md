# 彼岸 AI 媒体创作中枢（AI Media Workflow Hub）

> 原「净画 Android」测试仓库已升级定位。
>
> 本仓库现在主要承载 **GPT 自定义工作流 + GitHub Actions 媒体自动化系统**，Android 构建能力作为附属模块保留。

## 项目定位

这是一个面向 ChatGPT 自定义 GPT 的 AI 媒体生产中控系统。

核心目标：

- 通过 GPT Actions 调度图片、视频、语音、音乐生成任务；
- 使用 GitHub Actions 作为安全任务执行层；
- 使用 Artifact / Drive 完成交付；
- 保证任务幂等、防重复提交和状态追踪。

## 核心架构

```
ChatGPT 自定义 GPT
        |
        ↓
GPT Actions
        |
        ↓
GitHub Workflow Gateway
        |
        ↓
AI Media Provider
        |
        ↓
Artifact / Google Drive 输出
```

## GPT 工作流

目录：

`gpt-actions/`

包含：

- `instructions.txt`：GPT 行为总控规则
- `media.json/yaml`：媒体生成 Action 配置
- `gemini.json/yaml`：Gemini 文本模式配置

支持：

### 视频生成

- 文生视频
- 首帧图生视频
- 参考图生视频
- 多图输入
- 批量视频任务

### 配音生成

- Google TTS
- 音色选择
- 参数控制

### 音乐生成

- Google Music / Lyria
- 片段音乐
- 长音乐模式

## 设计原则

- 小而稳，不自动扩大任务范围；
- 媒体类型独立，不强制串联流水线；
- request_id 防止重复生成；
- 状态账本保证任务可追踪；
- 失败不自动重复消耗模型资源。

## Android 模块

当前保留：

- 净画 Android 构建测试
- GitHub Actions APK 编译
- 本地视频处理模式

## 后续方向

本仓库计划成为个人 AI 创作基础设施：

```
脚本
 ↓
配音
 ↓
音乐
 ↓
视频
 ↓
作品展示
 ↓
移动端应用
```

与其他项目组合：

- my-video-portfolio：作品展示层
- doubao-tts-bridge：语音能力层
- my-website：产品应用层

