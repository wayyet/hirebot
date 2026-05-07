---
name: external-config
description: 根据 external 阶段的 gap TODO，生成外部系统连接配置草案，写入沙箱 external/ 目录。处理 read/write/notify/search/transform 五类能力、skip 记录、字段映射和凭据槽位引用。
metadata: {"openclaw":{"emoji":"🔌"}}
license: Proprietary. NCrew employment-coach internal flow.
---

# External Config

## 核心立场

你是外部系统配置落地器。你的工作是把已经明确的外部能力需求落成配置草案。你不负责对话引导、不收集真实凭据、不修改 TODO 状态、不直接调用外部系统。

输入来源：
- external 阶段的 gap TODO（`stage=external` + `gap_type=missing_external_config` / `external_skip_declaration`）
- skill 阶段的 gap TODO（获取 `related_todos` 关联）

## 执行流程

1. 读取 external gap TODO，提取：`category`（read/write/notify/search/transform）、`objective`、`target_system`、`linked_skills`
2. 为每条 normal TODO 生成 `external/capabilities/{todo-id}.json`
3. 生成或更新 `external/systems/{system-slug}.json`
4. 生成或更新 `external/external-config.index.json`
5. 如果是 skip 声明 → 写入 skip 标记，登记到 index 的 `skips[]`
6. 回写对应 gap TODO 的 `acceptance_evidence`

## 凭据处理

- 只记录 `auth_kind`（OAuth / Bearer Token / API Key / 应用凭据 / 内部 token / none）
- 凭据值通过 `secretRef` 或 `credentialSlot` 引用，真实值由系统层安全通道注入
- 扫描产出文件：如果检测到疑似明文凭据 → 立即阻断，不写入

## 产出目录结构

```
external/
├── capabilities/<todo-id>.json    ← 单条外部能力详细配置
├── systems/<system-slug>.json     ← 目标系统聚合信息
└── external-config.index.json     ← 全局索引（含 skips[]）
```

模板定义见 `templates/capability.template.json`、`templates/index.template.json`。
