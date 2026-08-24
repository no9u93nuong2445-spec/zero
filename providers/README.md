# Provider Layer

AI 服务抽象层。

计划结构：

```
providers/
 ├── google/
 ├── doubao/
 ├── openai/
 └── custom/
```

GPT 和 Workflow 不直接绑定具体模型。

示例：

```
video.provider=google
video.model=omni
```

未来切换模型时只修改 Provider 配置。
