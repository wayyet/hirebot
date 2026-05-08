# 雇佣流程与阶段推进

## 总览

流程固定为：

`material -> skill -> external -> ready_for_packaging`

规则只有一条：**全部按新流程执行，不做旧流程兼容。**

---

## 1. 资料阶段 `material`

### 目标

- 收到真实业务资料
- 完成资料分类
- 为每份资料写明抽取目标

### 关键规则

- **只有用户上传的文档、表格、文本等才算业务资料。** 模板包自带的 SOUL.md、AGENTS.md、skills/ 等文件不算。不要把它们当作”已有资料”来跳过资料阶段。
- **收到第一份用户上传资料后，立刻分类、设定抽取目标，在同一轮回复末尾输出 `<workflow_stage_facts>` 标签。不要等待用户上传更多。**
- 不要创建”资料阶段 required gap todo”来占位
- 不要只因为用户口头描述就推进到技能阶段
- 输出标签后等用户回应，不要在同一轮继续推进技能阶段

### 标签格式

直接原始输出，不要加代码块：

```
<workflow_stage_facts>
{“material_classified_files”: [“文件名1.txt”], “material_extraction_targets”: {“文件名1.txt”: “从该文件抽取的具体目标”}}
</workflow_stage_facts>
```

### 完成条件

- 至少 1 份上传资料存在
- `workflow_stage_facts.material_classified_files` 覆盖全部上传资料
- `workflow_stage_facts.material_extraction_targets` 为每份资料提供目标

---

## 2. 技能阶段 `skill`

### 目标

- 盘清默认技能基线
- 只把真正缺失的能力项转成待办
- 逐条引导用户完成每个待补充技能，通过 `<dispatch>` 落地为实际 SKILL.md

### 处理节奏

进入技能阶段后，按 `展示现状 → 逐条引导 → dispatch 落地 → 确认完成 → 下一条` 的循环推进。

#### 第一步：展示现状

把当前 skill 阶段的 gap todo 列表展示给用户：

```
当前需要补充以下 X 项技能能力：

1. 技能 A - 业务规则抽取（待处理）
2. 技能 B - 对话流程管理（待处理）
3. 技能 C - 异常与兜底处理（待处理）

我们逐条来。先从第 1 项开始：你想让它具体解决什么问题？
```

#### 第二步：逐条引导用户描述

对当前正在处理的 todo，引导用户补充：

- **触发条件**：什么时候需要这个技能介入
- **输入依赖**：需要什么信息才能执行（上游数据、其他技能产出）
- **输出形式**：产出什么、谁来消费
- **边界限制**：什么明确不做、什么交给下游

不要一次性问完所有问题；先拿到 2-3 个关键信息，确认方向正确后再追问细节。

#### 第三步：dispatch 到 skill-generation

用户描述足够明确后，用 `<dispatch>` 标签派发给 `skill-generation`：

```json
<dispatch>
{
  “target”: “skill-generation”,
  “todo_ids”: [“<当前 todo id>”],
  “note”: “根据用户上述描述，生成完整 SKILL.md。技能名称、触发条件、输入依赖、输出形式、边界限制均已在上文中明确。”
}
</dispatch>
```

dispatch 之后等待 `dispatch_callback` 确认产出。回调中 todo status 变为 `done` 即表示该条已完成。

如果当前 todo 的 `gap_type` 不是标准 `missing_skill_definition`，而是 need_clarification / incomplete_skill_fields，先和用户澄清再 dispatch。

#### 第四步：推进下一条

当前 todo 完成后，回到第一步展示剩余待处理项，循环直到全部完成。

#### 全部完成后

通过 `<workflow_stage_facts>` 回传：

```json
<workflow_stage_facts>
{
  “skill_baseline_reviewed”: true,
  “skill_baseline_confirmed”: true
}
</workflow_stage_facts>
```

然后告知用户：`技能阶段 X 项补充能力全部完成，是否推进到外部系统配置阶段？`

### 完成条件

- 已记录 `skill_baseline_reviewed=true`
- 所有 `stage=skill` 且 `priority=required` 的补充项 todo 均已完成
- 已记录 `skill_baseline_confirmed=true`

### 关键规则

- 模板默认 skills 不要生成右侧待办
- 只有”待补充项”才创建 `stage=skill` 的 required gap todo
- 如果没有待补充项，必须问用户：
  - `阶段二已足够，是否推进阶段三？`

---

## 3. 外部阶段 `external`

### 目标

- 把外部能力定义成可执行连接单元
- 逐条引导用户完成每个外部系统配置，通过 `<dispatch>` 落地为 external config 文件
- 完成所需凭据绑定或显式跳过

### 处理节奏

与技能阶段相同的循环：`展示现状 → 逐条引导 → dispatch 落地 → 确认完成 → 下一条`。

#### 第一步：展示现状

把当前 external 阶段的 gap todo 列表展示给用户：

```
当前需要配置以下 X 项外部系统连接：

1. 外部系统 A - MCP 工具代理（待配置）
2. 外部系统 B - CLI 命令通道（待配置）
3. 外部系统 C - 数据库连接（待配置）

我们逐条来。先从第 1 项开始：你需要对接什么系统？做什么操作？
```

#### 第二步：逐条引导用户描述

对当前正在处理的 todo，引导用户补充：

- **目标系统**：对接什么系统（CRM / 数据库 / API / 内部服务）
- **操作类型**：read / write / notify / search / transform 中的哪个
- **操作目标**：具体要完成什么业务操作
- **认证方式**：OAuth / Bearer Token / API Key / 应用凭据 / 无
- **关联技能**：这个连接能力给哪些技能使用

如果用户明确表示某项不需要 → 创建 `external_skip_declaration` todo 并完成。

#### 第三步：dispatch 到 external-config

用户提供足够信息后，用 `<dispatch>` 标签派发给 `external-config`：

```json
<dispatch>
{
  "target": "external-config",
  "todo_ids": ["<当前 todo id>"],
  "note": "根据用户上述描述，生成外部连接配置。目标系统、操作类型、认证方式均已在上下文中明确。"
}
</dispatch>
```

dispatch 之后等待 `dispatch_callback` 确认产出。

**敏感凭据不要发进聊天框。** 如果用户尝试在对话中提供 token/密码/key：
- 拦截并提示走右侧"凭据绑定"表单
- 在 todo 的 `payload` 中只记录 `credential_slot` 引用，不记录凭据值

#### 第四步：推进下一条

循环直到全部完成，或用户显式跳过剩余项。

### 完成条件

- 每条 required 外部 todo 都对应一个连接能力单元
- 每条 todo 的 `payload` 至少包含：
  - `connector_type`
  - `connector_name`
  - `operation`
  - `objective`
  - `credential_slot`
  - `auth_kind`
  - `linked_skills`
- 需要凭据的连接能力已经完成绑定
- 或者用户已完成 `external_skip_declaration`

### 关键规则

- 一个连接能力单元对应一条 todo
- 不再用旧的 `target_skill` / `intent` 语义组织外部阶段

---

## 4. 打包阶段 `ready_for_packaging`

### 目标

- 只处理诊断阻塞、配置治理复核和最终打包条件

### 关键规则

- 这里不新增业务 gap todo
- 如果资料、技能、外部三个业务阶段都完成，但仍有复核项，当前阶段也应该停留在 `ready_for_packaging`

---

## 5. 阶段事实

服务端依赖这些显式事实：

- `material_ready`
- `material_classified_files`
- `material_extraction_targets`
- `skill_baseline_reviewed`
- `skill_baseline_confirmed`

教练需要通过 `<workflow_stage_facts>` 标签回传这些事实，服务端再据此做阶段诊断。
