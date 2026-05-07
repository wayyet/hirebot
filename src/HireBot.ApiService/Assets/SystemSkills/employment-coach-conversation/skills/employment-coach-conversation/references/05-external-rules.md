# 接入外部系统的规则与限定

本文档定义雇佣教练在阶段三（外部系统对接）中**必须遵守**的规则。具体配置产出格式由 `external-config` 执行 skill 的模板定义。

> 名词速查: 不熟悉的术语见 [01-glossary.md](01-glossary.md)

---

## 1. 外部能力类型

每条外部能力属于以下五类之一：

| category | 含义 | 典型问法 | 示例 |
|---|---|---|---|
| `read` | 从外部系统读取数据 | "需要查什么数据？" | 从 CRM 拉订单信息 |
| `write` | 向外部系统写入数据 | "需要把结果写到哪里？" | 在工单系统建工单 |
| `notify` | 向 IM/通知系统发送消息 | "做完后要通知谁？" | 推送到企微群 |
| `search` | 在数据源中按条件检索 | "要在哪里搜？" | 在知识库中搜索政策 |
| `transform` | 数据格式转换 | "格式需要怎么转？" | CRM 字段映射为内部格式 |

一条能力同时跨两类时 → 拆成两条 TODO。

---

## 2. 凭据安全红线

### 核心原则

**凭据值（token/key/密码/API Key/连接串）绝不进入以下任何位置**：

- 会话对话
- TODO notes
- 模板包产物文件（`external/` 下的配置文件）

### 检测与拦截

用户如果在对话中输入了凭据值：
- 雇佣教练检测到 `HiringWorkflowSupport.ContainsSensitiveValue()` 中的模式（如 `sk-` 前缀、`Bearer` token 等）
- 立刻回复："这类信息请填到凭据表单，不要在对话里发。"
- 凭据值不被记录到任何 TODO 或消息历史中

### 凭据槽位机制

外部系统需要的凭据通过**凭据槽位**管理：

```
用户在独立表单填写凭据
  → 系统层用 DataProtectionProvider 加密
  → 存入数据库 HiringCredentialBindingEntity
  → 凭据槽位只记录 secretRef（引用）+ auth_kind（形式描述）
  → 系统层在需要时将凭据解密注入沙箱
```

雇佣教练在 TODO notes 中**只描述**：
- `auth_kind`: 凭据形式（OAuth / Bearer Token / API Key / 应用凭据 / 内部 token / none）
- 需要的字段名（不填写具体值）

```
✅ 正确: "需要 API Key 形式的凭据，由用户在表单中填写"
❌ 错误: "API Key 是 sk-live-abc123..."
```

---

## 3. 配置产出规范

外部配置由 `external-config` 执行 skill 写入沙箱 `external/` 目录：

```
external/
├── capabilities/<todo-id>.json    ← 每条外部能力的详细配置
├── systems/<system-slug>.json     ← 每个目标系统的聚合信息
├── external-config.index.json     ← 全局索引（含 skips[] 列表）
└── skip.json                      ← skip 声明
```

### 每条配置包含
- `category`: read/write/notify/search/transform
- `target_system`: 系统名 + 厂商或自研标识（如"销售易 CRM""自研 OA"）
- `objective`: 一句话目标
- `linked_skills`: 关联的 skill（对应 skill 阶段的 TODO id 列表）
- `auth_kind`: 凭据形式（不含值）
- `fields`: 需要的关键字段映射

### skip 声明
用户明确不需要外部系统时：
- 创建 `gap_type: external_skip_declaration` 的 TODO
- `external-config` 执行 skill 写入 `external/capabilities/{todo-id}.json`，标记为 skip
- 同时登记到 `external-config.index.json` 的 `skips[]`

---

## 4. 安全约束

| 约束 | 说明 |
|---|---|
| 不写凭据值 | `external/` 下所有文件中不得出现真实 token/key/密码 |
| 凭据只引用 | 配置文件中只保留 `secretRef`、`credentialSlot` 或等价引用 |
| 不直接调用外部系统 | 雇佣流程中的配置是**草案**，不做连通性测试 |
| 不跳过诊断校验 | 外部阶段完成后必须通过诊断 skill 的安全检查 |
| 单条配置独立 | 每条外部能力对应一个独立的 capability 文件，不混合 |
