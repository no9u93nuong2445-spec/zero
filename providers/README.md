# Provider Layer（控制面视图）

本目录不再维护第二套“运行时模型白名单”。

## 原则

Provider 的真实模型、时长、分辨率、API revision 和安全参数，以执行仓库 `no9u93nuong2445-spec/doubao-tts-bridge` 的运行时 registry 为最终真源。

`zero` 只维护：

- GPT 如何选择 Provider/模式；
- Action/OpenAPI 的前置约束；
- 用户可理解的能力说明；
- 与执行面的契约版本。

这样可以避免出现：

```text
zero 说支持 A
但 execution registry 已经改成 B
```

## 当前 Provider

### Google

当前媒体执行面支持：

- TTS
- Lyria 音乐
- Omni 视频
- Veo / Veo Fast / Veo Lite

具体模型名和能力限制不要在多个文件里各自手写后长期独立维护；V4 的目标是让发布流程自动检查 Control Plane schema 与 Execution Plane registry 是否漂移。

## 未来扩展

以后增加 Doubao / OpenAI / 其他 Provider 时，应先在执行面实现：

1. Provider adapter；
2. runtime registry；
3. 幂等与状态输出；
4. 自测；

再在 `zero` 增加对应 Action/路由，而不是先在控制面写一个实际上不能执行的 provider 配置。
