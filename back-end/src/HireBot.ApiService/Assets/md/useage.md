# 雇佣教练对话引导 skill · 使用说明书

> 本文档面向**集成方**（系统工程师 / 产品 / 上层主 skill 实现者），不是给 LLM 在会话中阅读的。LLM 运行时的指令在 [SKILL.md](./SKILL.md) 与 [references/](./references/)。
>
> 阅读对象与目标：
> - 把这个 skill 接到雇佣教练产品里的人
> - 想理解阶段产出、handoff 协议、与下游 skill 协作方式的人
> - 想验证产品流程对齐的人

---

## 目录

1. [这是什么](#1-这是什么) 
2. [在雇佣教练产品里的位置](#2-在雇佣教练产品里的位置)
3. [前置条件](#3-前置条件)
4. [阶段化流程总览](#4-阶段化流程总览)
5. [阶段一·资料：详细产出](#5-阶段一资料详细产出)
6. [阶段二·技能：详细产出](#6-阶段二技能详细产出)
7. [阶段三·外部：详细产出](#7-阶段三外部详细产出)
8. [Handoff 协议详解](#8-handoff-协议详解)
9. [横切产出：配置文件治理](#9-横切产出配置文件治理)
10. [中断恢复](#10-中断恢复)
11. [与诊断 skill 的协作边界](#11-与诊断-skill-的协作边界)
12. [端到端示例：客服退货咨询](#12-端到端示例客服退货咨询)
13. [集成 checklist](#13-集成-checklist)

---

## 1. 这是什么

雇佣教练 (`employment-coach-conversation`) 是雇佣教练产品中**对话引导子 skill**。

它的唯一职责是：在业务用户进入沙箱、选定模板后，按"资料 → 技能 → 外部"三个阶段进行对话引导，把用户的需求**沉淀成可被下游 skill 直接消化的结构化 todo 工单**，并在合适的时机发出 `<dispatch>` 信号让系统调起对应的下游 skill。

它**不**：
- 不实现本体提取、技能生成、外部配置、诊断、打包
- 不直接写 `ontology/` / `skills/` / `external/` 三个目录
- 不修改 `memory.md`
- 不暴露平台架构、orchestrator、hooks、沙箱机制等内部概念给用户

它**会**：
- 维护 todo 工单的生命周期（drafting → ... → confirmed）
- 监听用户对 `soul.md` / `identity.md` / `agent.md` 的修改意图，按混合反问机制写入
- 在 dispatch 后等回传期间继续合理对话
- 在所有阶段必需项达成时输出"建议进入阶段 4"的出口信号

---

## 2. 在雇佣教练产品里的位置

```
雇佣教练产品（用户感知 4 个阶段：上传资料 → 生成技能 → 配置外部 → 生成实例包）
│
├─ 主 skill（系统层 / 编排者）
│   ├─ 创建沙箱、加载模板、维护会话状态
│   ├─ 监听 <dispatch> 信号 → 调度下游子 skill
│   ├─ 收下游 user_summary → 推送回本 skill + 写入对应目录
│   └─ 阶段 4：归档沙箱为实例包（不在本 skill 范围）
│
└─ 子 skill 群
    ├─ employment-coach-conversation  ← 本 skill：对话引导
    ├─ ontology_extraction             下游：本体提取（写 ontology/）
    ├─ skill_generation                下游：技能生成（写 skills/）
    ├─ external_config                 下游：外部配置（写 external/）
    └─ diagnosis                       横切：诊断完备性，输出诊断 todo
```

本 skill 与下游 4 个子 skill 的协作方式：

- 本 skill 输出 `<dispatch>` → 主 skill 拦截 → 调起对应下游 skill
- 下游 skill 完成后通过主 skill 回传 `user_summary` + 已落盘的产物路径
- 本 skill 把 user_summary 复述给用户、等用户确认、把对应 todo 工单状态切到 `confirmed`

---

## 3. 前置条件

系统层在调起本 skill 前**必须**完成：

| 项 | 要求 |
|---|---|
| 沙箱目录 | 已创建，本 skill 进程对其有读写权限 |
| 模板 config 复制 | 模板 `config/{soul,identity,agent,memory}.md` 已完整复制到沙箱 |
| 4 个 md 加载 | soul / identity / agent / memory 内容已加载到运行时索引（供本 skill 引用） |
| 完备性清单加载 | 模板的"完备性清单"已加载（本 skill 读它推断阶段最低门槛；诊断 skill 主要使用方） |
| 会话窗口绑定 | 该雇佣任务的会话窗口已建立，与沙箱一一对应 |
| dispatch 钩子 | 系统层已注册"识别本 skill 输出中 `<dispatch>` 块并调起对应下游 skill"的钩子 |
| 文件上传通道 | 用户上传文件后落到沙箱可访问位置，文件名通知到本 skill |
| 表单凭据通道 | 用户在右侧表单填写凭据后，系统层把凭据交给 `external_config` 下游 skill，**不传给本 skill** |

如果以上任一项未就绪，本 skill 不应被启动。

---

## 4. 阶段化流程总览

```
[系统初始化沙箱]
       │
       ▼
[本 skill 启动 → 角色化开场（基于 soul / identity）]
       │
       ▼
┌────────────────────────────────────────────────────────────┐
│ 阶段 1：资料  ──→  todo 工单 (target=ontology_extraction)│
│   引导上传资料 → 边传边定 todo → 达到最低门槛 → dispatch  │
│   等回传 → 复述确认 → confirmed → 解锁阶段 2               │
└────────────────────────────────────────────────────────────┘
       │
       ▼
┌────────────────────────────────────────────────────────────┐
│ 阶段 2：技能  ──→  todo 工单 (target=skill_generation) │
│   story-driven 引导 → 抽出明确 name+description → dispatch │
│   等回传 → 复述确认 → confirmed → 解锁阶段 3               │
└────────────────────────────────────────────────────────────┘
       │
       ▼
┌────────────────────────────────────────────────────────────┐
│ 阶段 3：外部  ──→  todo 工单 (target=external_config)  │
│   紧扣已有 skill 引导 → category+objective+target_system   │
│   每条达到明确度 → dispatch（凭据走表单，不入会话）        │
│   等回传 → 复述确认 → confirmed                           │
└────────────────────────────────────────────────────────────┘
       │
       ▼
[出口：所有必需项 confirmed → <dispatch target=stage_transition>]
       │
       ▼
[本 skill 范围结束，主 skill 接管阶段 4 打包]
```

横切机制全程在线（不阻塞主流程）：
- **配置文件治理**：监听 soul / identity / agent 修改意图 → 混合反问 → 写入
- **改动反向触发复核**：判定 / 边界 / 数据访问范围层面的改动 → 提醒可能影响的已 confirmed todo

阶段解锁规则：
- 未走过的阶段：严格按 1 → 2 → 3 顺序解锁
- "走过" = 该阶段产生过至少一份 confirmed todo 工单（外部阶段也可以是 `kind: skip` 的 todo）
- 走过的阶段：用户可任意跳回修改（系统提供入口），本 skill 进入对应阶段引导

---

## 5. 阶段一·资料：详细产出

### 5.1 阶段目的

把用户的业务资料转换成"该抽什么本体"的明确指令，供下游 `ontology_extraction` skill 直接消化。

### 5.2 阶段最低门槛

至少 1 份资料被指认归类，且对应的 todo 工单明确写出"要从中抽什么分类的本体 + 目标"。

### 5.3 引导动作（本 skill 在会话中做什么）

1. 从 soul / identity 推断 `scene_hint`（客服 / 销售 / 内勤 / 营销 / 法务 / 技术 / 模糊）
2. 按场景类型发出"first ask"，让用户拿出最有用的第一批资料
3. 用 story-driven 方式追真实场景，不让用户感觉在填材料目录
4. 每收到一份资料 → 实时形成 / 更新 todo 工单（边传边归类）
5. 用户表示"先这些" + 至少 1 条 todo 达明确度 → 触发 dispatch

### 5.4 产出：todo 工单

每条 todo 的结构：

```yaml
id: m_<scene_hint>_<content_fingerprint>     # 稳定 ID
stage: material
target_skill: ontology_extraction
intent: <一句话目标，给用户读>
category: <业务对象定义 | 决策规则 | 流程 SOP | 案例库 | 边界与约束 | 风格语料 | 其他>
payload:
  objective: <一句话告诉下游：从这份资料里抽什么>
  source_files: [文件名1, 文件名2, ...]
  scene_hint: <客服 | 销售 | 内勤 | 营销 | 法务 | 技术 | 模糊>
  mode: incremental | full_replace        # 默认 incremental
source: <对话片段引用 / 上传时点>
acceptance: <下游做完什么算通过>
status: drafting → ready_to_dispatch → dispatched → confirmed
created_at, updated_at
```

### 5.5 产出示例（客服退货场景）

```yaml
- id: m_cs_nonstandard_rules_001
  stage: material
  target_skill: ontology_extraction
  intent: 抽出非标退货场景的判定规则与处置路径
  category: 决策规则
  payload:
    objective: 抽取《非标退货处理规则》里的判定条件、处置档位、分流到经理的触发条件
    source_files: ["非标退货处理规则.docx"]
    scene_hint: customer_service
    mode: incremental
  acceptance: ontology 中包含「退货-判定条件」「退货-处置档位」「退货-人工分流触发」节点
  status: ready_to_dispatch

- id: m_cs_dialogue_style_001
  stage: material
  target_skill: ontology_extraction
  intent: 学习一线客服的话术风格
  category: 风格语料
  payload:
    objective: 从工单截图归纳客服在标准退 / 非标退 / 拒退场景下的话术与口吻模式
    source_files: ["工单截图批次1/*.png"]
    scene_hint: customer_service
    mode: incremental
  acceptance: ontology 中包含至少 3 类话术模板的口吻特征
  status: ready_to_dispatch
```

### 5.6 dispatch 触发条件

同时满足：
1. 至少 1 条 todo 状态为 `ready_to_dispatch`
2. 用户表态"先这些 / 暂时这么多 / 好了 / 可以了"或语义等价表述

不能触发的情况：
- 任何 todo 仍在 `drafting`（明确度不足）
- 用户当前正在表达异议或修改某条 todo
- 处于配置治理的反问待确认状态

### 5.7 dispatch 信号

```
<dispatch>
target: ontology_extraction
handoff_ids: [m_cs_nonstandard_rules_001, m_cs_dialogue_style_001]
mode: incremental
note: 客服场景第一批，含决策规则与风格语料
</dispatch>
```

### 5.8 回传与确认

- 系统层接收下游 `ontology_extraction` 完成信号（含 user_summary + 已写入 `ontology/` 的产物路径）→ 推送给本 skill
- 本 skill 收到回传：
  1. 用一两句话向用户复述 user_summary
  2. 等用户口头确认（"差不多 / 嗯 / 可以"等）
  3. 对应 todo status `dispatched` → `confirmed`
- 用户表示有问题 → 进入对应 todo 修改流程；状态切回 `ready_to_dispatch` 走重发

### 5.9 阶段完成判定

- 至少 1 条 todo `confirmed` → 阶段 1 解锁阶段 2
- 用户可继续补充资料（追加新 todo），但已不阻塞进入阶段 2

---

## 6. 阶段二·技能：详细产出

### 6.1 阶段目的

把"它要会做什么"转换成结构化 skill 定义工单，供下游 `skill_generation` skill 直接消化。

### 6.2 阶段最低门槛

至少 1 条 skill 同时具备**明确的 `skill_name` + 明确的 `skill_description`**，并能说清 trigger 和 expected_output。

### 6.3 引导动作

1. 从用户最近真实场景拉出"它最应该顶的一类事"
2. 用 story-driven 方式追：上次怎么处理 / 哪步最容易卡 / 强弱差异 / 做坏会怎样
3. 把强弱差异、卡点、最容易判错的地方转化进 `skill_description`
4. 一条一条 skill 形成 todo 工单
5. 至少 1 条达明确度 + 用户表态 → dispatch

### 6.4 产出：todo 工单

```yaml
id: s_<keyword>_<seq>
stage: skill
target_skill: skill_generation
intent: <一句话目标>
category: <可省略；如分类则用：判定 / 信息查询 / 流程触发 / 内容生成 / 通知 / 其他>
payload:
  skill_name: <明确名词，如「退货资格初判」>
  skill_description: <完整描述：触发情境 + 判定逻辑 + 输入依赖 + 输出形式>
  trigger: <可识别的触发条件>
  expected_output: <输出形态 + 后续动作建议>
  from_upload: false      # 用户上传现成 skill 文件时为 true
source: <对话片段引用>
acceptance: skill_generation 产出的 skill 文件能匹配该 todo 的 name + description
status: drafting → ready_to_dispatch → dispatched → confirmed
```

### 6.5 字段明确度对照（用户描述 → 是否合格）

| 字段 | 不够明确 | 够明确 |
|---|---|---|
| skill_name | "处理售后" | "退货资格初判" |
| skill_description | "用户问退货怎么办时回应一下" | "在用户提出退货请求时，根据订单状态、商品类型、是否超过 7 天来判断是否符合退货条件，并把结论和理由回给用户" |
| trigger | "用户问起来" | "用户消息中出现退货 / 退款 / 退掉等关键词，且能匹配到具体订单" |
| expected_output | "回复用户" | "一条回复消息（含结论 + 依据），以及一条工单流转建议" |

### 6.6 产出示例（客服退货场景）

```yaml
- id: s_seven_day_init_001
  stage: skill
  target_skill: skill_generation
  payload:
    skill_name: 7天无理由退货初判
    skill_description: 用户提退货时拉订单创建时间，比对当前时间。在 7 天内且商品不在不退清单时，回复"可以退"+下一步指引；超过 7 天或商品在不退清单时，转入"非标退货资格预判"。
    trigger: 用户消息含退货 / 退掉关键词，且能匹配到具体订单
    expected_output: 一条结论回复（可退 / 转非标流程）+ 操作指引或下一条 skill 触发
    from_upload: false
  acceptance: skill_generation 产出的 skill 文件包含订单时间判定 + 不退清单匹配逻辑
  status: ready_to_dispatch
```

### 6.7 dispatch 信号

```
<dispatch>
target: skill_generation
handoff_ids: [s_seven_day_init_001, s_nonstandard_assessment_001, s_refund_progress_001]
note: 三条退货咨询主线 skill
</dispatch>
```

### 6.8 二级输入路径：用户上传现成 skill 文件

- 用户直接上传现成的 skill 定义文件 → 直接形成 todo（标记 `from_upload: true`）
- 不必再走明确度追问，可立刻 ready_to_dispatch
- 该入口在 UI 上属于二级位置（避免业务用户困惑）

---

## 7. 阶段三·外部：详细产出

### 7.1 阶段目的

把"它要能调用什么外部能力"转换成有分类、有目标的 CLI 工单，供下游 `external_config` skill 直接消化。

### 7.2 阶段最低门槛

每条外部能力都明确 `category` + `objective` + `target_system`；或用户明确表达"不需要外部系统"（标记 `kind: skip`）。

### 7.3 引导动作

1. 紧扣阶段 2 已有的 skill todo，逐条问"这条要做对，需要查什么 / 写什么 / 通知谁"
2. 多条 skill 用同一个外部能力 → 合并成一条 todo，`linked_skills` 列表带多个 id
3. 凭据红线：在会话里发现凭据值立刻挡回表单
4. 每条达明确度即可 dispatch（不必等"先这些"）

### 7.4 产出：todo 工单

```yaml
id: e_<system>_<verb>_<seq>
stage: external
target_skill: external_config
intent: <一句话目标>
category: read | write | notify | search | transform
payload:
  objective: <一句话目标，包含场景上下文>
  target_system: <系统名 + 厂商或自研标识，如「销售易 CRM」「自研 OA」>
  linked_skills: [skill_todo_id_1, skill_todo_id_2, ...]
  auth_kind: <OAuth | Bearer Token | API Key | 应用凭据 | 内部 token | none>
  required_fields: [字段1, 字段2]    # 可选：要拉 / 要写的关键字段
  kind: normal | skip
source: <对话片段引用>
acceptance: external_config 在 external/ 写入了对应配置初稿且校验通过
status: drafting → ready_to_dispatch → dispatched → confirmed
```

### 7.5 产出示例（客服退货场景）

```yaml
- id: e_xiaoshouyi_read_order_001
  stage: external
  target_skill: external_config
  category: read
  payload:
    objective: 在退货咨询时，从 CRM 拉指定订单的创建时间、状态、客户等级、商品类型
    target_system: 销售易 CRM
    linked_skills: [s_seven_day_init_001, s_nonstandard_assessment_001]
    auth_kind: API Key
    required_fields: [order_id, created_at, status, customer_tier, product_category]
    kind: normal
  acceptance: external/ 中包含可调用的销售易订单读取配置 + 字段映射
  status: ready_to_dispatch

- id: e_wxwork_create_ticket_001
  stage: external
  target_skill: external_config
  category: write
  payload:
    objective: 判定需转经理时，在企微工单系统建工单，含订单信息、判定原因、客户原话
    target_system: 企业微信工单系统
    linked_skills: [s_nonstandard_assessment_001]
    auth_kind: 应用凭据
    required_fields: [order_id, reason, customer_quote, assignee]
    kind: normal
  status: ready_to_dispatch
```

### 7.6 凭据规则（强约束 / 安全相关）

- token / 密钥 / 密码 / API Key 等**绝不在会话里收集**
- todo 工单的 `auth_kind` 字段只描述凭据形式，不带值
- 凭据由用户在右侧表单填写 → 系统层直接交给 `external_config` 下游 skill → 本 skill 永不接触凭据值

### 7.7 跳过分支

- 用户明确表达"不需要外部系统"或等价表述 → 形成一条 `kind: skip` 的 todo
- dispatch 仍然发出，target 仍是 `external_config`，下游识别 skip 后仅做"不需要外部系统"的状态记录
- 阶段视为已走过，可解锁出口

### 7.8 dispatch 时机的差异

阶段 1、2 是"批次" dispatch（攒够一批一起发），阶段 3 是"逐条" dispatch（每条达明确度即可发）。原因：外部能力之间相对独立，下游的撞车风险低，逐条发能让用户尽早开始填表单。

---

## 8. Handoff 协议详解

### 8.1 dispatch 信号格式

完整 schema：

```
<dispatch>
target: <下游 skill 名>
handoff_ids: [<handoff_id_1>, <handoff_id_2>, ...]
mode: <可选，阶段相关模式标记>
note: <可选，给系统/下游的简短上下文>
</dispatch>
```

字段含义：

| 字段 | 类型 | 必需 | 含义 |
|---|---|---|---|
| target | string | 是 | 取值见下表 |
| handoff_ids | string[] | 是 | 本次交接的 Handoff id 列表，必须存在且状态为 `ready_to_dispatch` 或 `dirty` |
| mode | string | 否 | 阶段相关，目前只有 ontology_extraction 用：`incremental` / `full_replace` |
| note | string | 否 | 给系统 / 下游的简短上下文，纯描述性 |

target 取值：

| target | 用途 |
|---|---|
| `ontology_extraction` | 阶段 1：本体提取 |
| `skill_generation` | 阶段 2：技能生成 |
| `external_config` | 阶段 3：外部配置 |
| `stage_transition` | 出口：进入阶段 4 |

`stage_transition` 的特殊字段：

```
<dispatch>
target: stage_transition
to: instance_packaging
note: 三个阶段的必需项均已完成，可进入打包
</dispatch>
```

不携带 handoff_ids，也不期待回传——它只是一个“建议进入下一阶段”的语义信号，主 skill 接收后接管。

### 8.2 系统层的解析合约

主 skill 在监听对话流时，遇到符合上述 schema 的 `<dispatch>` 块：

1. 解析出 `target` + `handoff_ids` + 可选字段
2. 校验 handoff_ids 是否都存在于本 skill 维护的 Handoff 索引中、状态合法
3. 调起对应下游 skill，把这些 Handoff 的 payload 作为输入传过去
4. 等下游完成 → 收 user_summary + 产物路径
5. 推送回本 skill（按下面的回传约定）

如果遇到格式不合规的 `<dispatch>` 块，应：
- 不静默忽略
- 反馈给本 skill 让它重新组织
- 不要把不合规块当普通对话发给用户

### 8.3 下游回传约定

下游 skill 完成后，主 skill 把以下结构推送回本 skill：

```yaml
dispatch_callback:
  source_dispatch_target: ontology_extraction    # 与之前 dispatch 的 target 对应
  handoff_ids: [m_cs_nonstandard_rules_001, m_cs_dialogue_style_001]
  user_summary: "已抽取：非标退货 6 条判定规则、3 档处置路径；从工单截图归纳出 4 类话术模板。"
  artifacts:
    - path: ontology/non_standard_return_rules.json
      kind: ontology_node
    - path: ontology/dialogue_styles.json
      kind: ontology_node
  status: success | partial | failed
  errors: []     # status != success 时填
```

本 skill 收到回传后：
1. 把 user_summary 用一两句话向用户复述，请确认
2. 用户确认 → 对应 Handoff status 切到 `confirmed`
3. 用户提出问题 → 进入对应 todo 的修改流程，状态切回 `ready_to_dispatch`
4. status 是 `failed` → 在会话中以"我让那边重新走一次"的口气重新 dispatch（不暴露 error 细节给用户，error 给系统层）

### 8.4 todo 状态机（运行时合约）

```
drafting ─┬─→ ready_to_dispatch ─┬─→ dispatched ─┬─→ confirmed ─┬─→ needs_review ─┬─→ confirmed
          │                      │               │              │                  └─→ ready_to_dispatch
          │                      │               └─→ dirty ────────────→ ready_to_dispatch
          │                      └─→ drafting（用户继续改）
          └─→ dismissed
```

| 状态 | 谁能写 | 何时写 |
|---|---|---|
| drafting | 本 skill | 新建 todo / 用户在引导中继续修改 |
| ready_to_dispatch | 本 skill | 字段达明确度 |
| dispatched | 本 skill | 发出 dispatch 后 |
| dirty | 本 skill | dispatched 期间 todo 内容被用户改动 |
| confirmed | 本 skill | 收到回传 + 用户确认 |
| needs_review | 本 skill | 配置文件改动反向触发复核 |
| dismissed | 本 skill | 用户主动撤销 |

主 skill 永不直接修改 todo 状态，只能通过推送 dispatch_callback 让本 skill 自行流转。

### 8.5 dispatch 等回传期间的会话行为

详见 [references/dispatch-protocol.md](./references/dispatch-protocol.md)。要点：

- 用户继续抛新意图 → 接住 + 形成 drafting todo + **不立刻发新 dispatch**（等当前回传后合并）
- 用户改正在 dispatched 的 todo → 切到 dirty，回传后告知重新走一次
- 用户跳到下一阶段 → 拉回（坚持的话允许，但当前阶段保持 dispatched 不强制 confirm）
- 用户跳到走过的阶段 → 允许（系统提供入口）
- 配置治理触发 → 独立运行，不被 dispatch 阻塞
- 用户长时间不说话 → 不主动追问"是否还有补充"

---

## 9. 横切产出：配置文件治理

### 9.1 监听机制

本 skill 持续监听对话，**同时**出现以下两类信号才触发治理流程：
1. 身份描述类关键词：名字、性别、形象、口吻、语气、使命、定位、责任、规则、约束、不能、必须、底线
2. 修改类动词：改、换、不是、应该叫、调整、补充、加上、去掉、不要

不满足两类同时出现，按普通对话处理。

### 9.2 4 个 md 文件的写入合约

| 文件 | 本 skill 写权限 | 写入语义 | 触发方式 |
|---|---|---|---|
| soul.md | ✅ | replace（小段替换）/ append | 监听到使命 / 定位类修改意图 + 通过混合反问 |
| identity.md | ✅ | replace（字段级） | 监听到名字 / 形象 / 口吻类修改意图 + 通过混合反问 |
| agent.md | ✅ | append / replace | 监听到规则 / 约束 / 红线类修改意图 + 通过混合反问 |
| memory.md | ❌ | 永远不写 | — |

每次写入后产出"反馈消息"格式：
- 高置信度直接执行：一行确认（"好的，名字已经改成『小琪』。"）
- 低置信度反问：短反问（"你是想把它的名字从『小智』改成『小琪』，对吗？"）

UI 同步：当前右侧视图正在展示这部分内容时同步刷新（系统层负责，本 skill 不主动控制）。

### 9.3 改动反向触发 todo 复核的产出

当 agent / soul / identity 改动属于**判定 / 边界 / 数据访问范围**层面（改名字 / 改口吻不算）时，本 skill 输出：

```yaml
governance_review_proposal:
  triggered_by:
    file: agent.md
    change_summary: "VIP 必转规则改为：金牌 VIP 必转，普通 VIP 走金额判定"
  potentially_affected_handoffs:
    - id: s_nonstandard_assessment_001
      reason: 该 skill 含 VIP 维度判定
    - id: e_xiaoshouyi_read_order_001
      reason: 该外部能力拉客户等级字段
  ask_user: "顺便——你刚才改的这条可能影响『非标退货资格预判』和那条 CRM 订单读取，要不要一起回头过一下？"
```

用户回应分支：
- 要 / 嗯好 → 相关 todo `confirmed` → `needs_review`，逐条复核
- 不要 / 先不管 → todo 状态不动，但 payload 里加 `pending_review_reason`
- 答非所问 → 默认不动，不再追问

详见 [references/config-file-governance.md](./references/config-file-governance.md)。

---

## 10. 中断恢复

由沙箱（持久化文件）+ 会话窗口（持久化对话和 UI 状态）天然支持，本 skill 无需特殊恢复逻辑。

用户回到会话时：
- 系统层把会话历史 + todo list 状态 + 4 个 md 当前内容还原到运行时
- 本 skill 启动后**不重新做角色化开场**（已经做过）
- 本 skill 短承接一句"咱们继续上次的"或类似口吻语，再根据当前 todo 状态推进
- 推进点判定：
  - 当前阶段有 `dispatched` 状态 todo → 提示"那边还在处理，要不要一边继续聊"
  - 当前阶段有 `drafting` / `ready_to_dispatch` todo → 直接进入引导对话
  - 上一阶段所有 todo `confirmed`、当前阶段无 todo → 进入当前阶段开场引导

---

## 11. 与诊断 skill 的协作边界

| 维度 | 本 skill 的 todo 工单 | 诊断 skill 的诊断 todo |
|---|---|---|
| 回答的问题 | "差的部分要交给谁、要带什么去" | "还差什么" |
| 触发条件 | 引导对话中沉淀出来 | 状态变化后重新评估完备性清单的差距 |
| 是否有写权限 | 写沙箱中 todo 索引 | 只读 |
| 跨阶段 | 不跨阶段（每条属于一个阶段） | 跨阶段（按完备性清单组织，可包含未解锁阶段） |
| 等级 | 没有"必需 / 推荐 / 可选" | 有 `level: 必需 / 推荐 / 可选` |
| 与下游 skill 的对应 | 一对一（每条 todo 都有 target_skill） | 多对一（多条诊断 todo 可能合并触发同一 dispatch） |
| 触发本 skill 重跑诊断 | 每次本 skill 收到回传后，主 skill 调诊断 skill 重跑 | — |

UI 合并展示规则由系统层决定，本 skill 不关心。建议合并展示策略：
- 同一阶段 + 同一意图的两类 todo 合并显示
- todo 工单强调"正在做什么 / 已经做到哪一步"
- 诊断 todo 强调"还差什么 / 是否必需"

---

## 12. 端到端示例：客服退货咨询

### 12.1 模板与初始化

- 模板：智能客服
- soul.md：你是负责退货咨询的客服数字员工。使命：让客户在 5 分钟内拿到清晰的退货处理结论。
- identity.md：名字"小琪"；口吻温和耐心。
- agent.md：不主动承诺退款金额；VIP 客户必须转人工；不在对话中收集敏感信息。
- memory.md：模板预设（不动）。
- 完备性清单（节选）：
  - 必需：决策规则类资料 ≥ 1、主线 skill ≥ 2、外部读能力 ≥ 1
  - 推荐：风格语料类资料 ≥ 1、外部 write 能力 ≥ 1

### 12.2 阶段产出全清单

| 阶段 | todo 工单 | dispatch | 回传后状态 |
|---|---|---|---|
| 阶段 1 资料 | M01: 非标退货判定规则<br>M02: 客服话术风格 | `<dispatch target=ontology_extraction handoff_ids=[M01, M02] mode=incremental>` | M01, M02 → confirmed |
| 阶段 2 技能 | S01: 7天无理由退货初判<br>S02: 非标退货资格预判<br>S03: 退款进度查询与解释 | `<dispatch target=skill_generation handoff_ids=[S01, S02, S03]>` | S01-S03 → confirmed |
| 配置治理 | agent.md 修改：VIP 规则改为分级 | （无 dispatch，治理路径独立） | agent.md 已写入 + 触发 S02 进入 needs_review → 复核后回 confirmed |
| 阶段 3 外部 | E01: 销售易 CRM 读订单<br>E02: 企微建工单<br>E03: 自研 OA 读审批进度 | 三次逐条 dispatch | E01-E03 → confirmed |
| 出口 | — | `<dispatch target=stage_transition to=instance_packaging>` | 本 skill 范围结束 |

### 12.3 关键时刻的对话节奏（精简版）

```
小琪 ─→ 角色化开场 + 阶段 1 first ask（基于 customer_service scene_hint）
用户 ─→ 上传工单截图 + 描述非标退货痛点
（情绪信号：来气、找经理 → 慢一句追问）
用户 ─→ 上传《非标退货处理规则》.docx
小琪 ─→ M01, M02 ready → dispatch ontology_extraction
（系统调下游 → 回传 user_summary）
小琪 ─→ 复述 → 用户确认 → confirmed → 解锁阶段 2

小琪 ─→ 阶段 2 引导（story-driven）
用户 ─→ 描述三件事
小琪 ─→ 逐条引导到明确度 → S01-S03 ready → dispatch skill_generation
（回传 → 用户确认 → confirmed → 解锁阶段 3）

[途中插入] 用户 ─→ "VIP 规则改了"
小琪 ─→ 监听双信号 → 低置信度反问 → 高置信度 → 写 agent.md
小琪 ─→ 改动属判定层 → 提示 S02 可能受影响 → 用户同意复核 → S02 → needs_review → 复核完成回 confirmed

小琪 ─→ 阶段 3 引导（紧扣 S01-S03）
用户 ─→ 提到 token → 拒收 → 引导到表单
小琪 ─→ E01-E03 各自 ready → 三次逐条 dispatch external_config
（用户在右侧表单填凭据 → 校验通过 → 回传 → 用户确认 → confirmed）

小琪 ─→ 三阶段必需项达成 → 简短总结 + dispatch stage_transition
[本 skill 范围结束，主 skill 接管打包]
```

---

## 13. 集成 checklist

接入本 skill 前，系统层确认：

**沙箱与会话**
- [ ] 沙箱目录创建完成，本 skill 进程读写权限就绪
- [ ] 模板 `config/{soul,identity,agent,memory}.md` 已完整复制
- [ ] 4 个 md 内容已加载到运行时索引
- [ ] 完备性清单已加载
- [ ] 会话窗口已建立，与沙箱一一对应

**dispatch 钩子**
- [ ] 监听本 skill 输出中的 `<dispatch>` 块，能正确解析 target / handoff_ids / mode / note
- [ ] target 取值映射到对应下游 skill 的调度逻辑（含 stage_transition 特例）
- [ ] 不合规的 dispatch 块不被静默忽略，给本 skill 反馈让它重组

**下游回传**
- [ ] 下游 skill 完成后，按 `dispatch_callback` 结构推送回本 skill
- [ ] 包含 user_summary / artifacts / status / errors
- [ ] 失败状态下让本 skill 自行决定是否重试

**用户输入通道**
- [ ] 文件上传：落到沙箱可访问位置，文件名通知到本 skill
- [ ] 表单凭据：直接交给 `external_config` 下游 skill，**不经过本 skill**
- [ ] todo list 跳转：用户点击 todo 时，主 skill 通知本 skill 进入对应阶段

**配置文件持久化**
- [ ] soul / identity / agent 写入路径就绪
- [ ] 写入时同步刷新当前展示该文件的右侧视图
- [ ] memory.md 写权限不开放给本 skill（防御性）

**诊断协作**
- [ ] 本 skill 每次收到 dispatch_callback 后，主 skill 调诊断 skill 重跑
- [ ] 诊断 todo 与 todo 工单在 UI 上合并展示

**中断恢复**
- [ ] 用户回到会话时，会话历史 + todo list + 4 个 md 内容能完整还原
- [ ] 本 skill 启动后能识别"是首次会话还是恢复"，避免重复开场

**安全**
- [ ] 本 skill 永远不接触凭据值（token / 密码 / API Key）
- [ ] 本 skill 永远不修改 memory.md
- [ ] 本 skill 输出中不暴露 orchestrator / hooks / 沙箱路径等内部概念给用户
