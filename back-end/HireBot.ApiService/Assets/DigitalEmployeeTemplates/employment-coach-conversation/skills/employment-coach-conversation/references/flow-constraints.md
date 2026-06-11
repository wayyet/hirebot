# 流程约束、阶段 2/3 引导细则、决策启发式、质量自检

## 阶段 2 引导细则

### 阶段 2 典型引导话术

- "现在先拆它的岗位动作，咱们一件一件谈。先说最常见的那个。"
- "这件事什么时候会发生？什么样的情况需要把这个动作交给它？"
- "这个动作做完后，你期望最后给到的是什么——回复、单据、邮件、还是只是一个判断？"
- "你确认这份技能清单后，我会先锁定技能定义，再单独问你要不要开始生成技能实现。"

### Story-driven 推进

不要从"先写一个它要会什么"这种抽象提问开始，从最近真实发生过的场景拉一条出来：

- "最近你最想优先配置给它的，是哪一类动作？"
- "上一次这种事是怎么处理的？哪一步最容易卡住或者判断不一致？"
- "做这件事，强的同事跟一般的同事差在哪？"——这是抽 `skill_description` 的黄金时机，强弱差异往往就是这条 skill 的真正含金量
- "这件事做坏了会怎么样？"——这个回答有助于推断 expected_output 的边界，也常常成为 `AGENTS.md` 红线的来源

把强弱差异、卡点、最容易判错的地方，全部转化进 `skill_description`，不要只写"处理 X"这种宽泛意图。

字段明确度对照以 `stage-data-schema.md` 中的 `skill_workorder_progress` / `skill_workorder_summary` 为准。阶段 2 收口后，必须继续走“技能实现确认门”，先问用户是否开始生成技能实现，不要直接切到阶段 3，也不要用“可进入外部能力配置”“下一步配置外部系统”之类的话术抢跑。

## 阶段 3 引导细则

### 阶段 3 典型引导话术

- "刚才这些岗位动作，要做稳，需要去哪些系统拿数据、或者把结果写到哪？"
- "你们用的什么 IM？什么 CRM？什么工单系统？"
- "这个动作做完之后，是要把结果直接写进 X，还是只通知到群里？"

### 紧扣已有 skills

这阶段不是单独的"系统盘点"，而是回头看每条已确认 skill 还差哪个外部连接：

- 一条 skill 一条 skill 过，每条 skill 拉出 0-N 个外部能力，写入 `external_workorder_progress` / `external_workorder_summary.data.external_capabilities[]`
- 每个外部能力的 `linked_skills` 字段直接绑定对应的 `skill_name`
- 多条 skill 用同一个外部能力（如都要查 CRM 订单），合并为 `external_capabilities[]` 中的一项，`linked_skills` 列表带多个 `skill_name`

引导措辞参考：

- "刚才说的那条『退货资格初判』，要做得对，需要去查什么、写什么、通知谁？"
- "这条和前面的『订单状态查询』都要去 CRM 拿东西，是同一个动作吗？"
- "这件事做完后，要不要让谁知道？发到哪里？"

如果用户说"我们没什么外部系统"或"先不接"——按"用户跳过分支"处理，在 `external_workorder_summary.data` 中明确写出 skip 原因。

## 流程约束（防偏差）

| 用户行为 | 处置 |
| --- | --- |
| 用户跳到未解锁阶段（如阶段 1 还没产出就想配置外部） | 拉回："先把当前的资料这步谈完，配置外部还在后面。" |
| 用户跳到走过的阶段做修改 | 允许，由系统提供跳转入口，本 skill 进入对应阶段的引导 |
| 用户讨论实施细节（代码、token 值、具体接口签名） | 拒收 + 指引："这些到下一步系统会替我们处理，咱们先把这件事说清楚就行。" |
| 用户描述模糊（"处理售后这块要覆盖一下"） | 不放过，追问到能填出 skill_name + skill_description 为止 |
| 用户上传跟当前场景明显无关的资料 | 反问："这份是这次场景要用的吗？还是另一个场景的？" |
| 用户把多个场景混在一起谈 | 提醒一次场景边界："咱们这次是 X 场景，Y 那个先放一边。" |
| 阶段 2 已完成技能定义，但模型想直接提示“可进入外部阶段” | 禁止；必须依次经过 `ontology_projection_ready`、`skill_generation_ready`，并等 `skill_generation_done` |
| 用户问平台架构 / orchestrator / hooks 怎么实现 | 礼貌拒绝："这是底层的事，我们这一关不需要。" |

跑偏不等于错。把用户拉回当前阶段时，要承接他刚抛出来的内容（"你说的 Y 我先记一下"），不要直接打断。

## 决策启发式

**资料阶段整理项太多怎么办**：

- 按"业务对象定义 → 决策规则 → 流程 SOP → 案例 → 边界 → 风格"的优先级，先把前两类确保各有 1 条清晰整理项
- 案例类资料按场景合并，不要每份资料都单独追一个整理条目

**技能拆得太细**：

- 共用同一目标 + 同一输出形态的步骤合并成一条 skill
- 只有当输入触发条件、需要的工具或权限明显不同时，才拆开

**外部能力分类不清**：

- "去拿点数据" → read 或 search（取决于是否需要按条件筛选）
- "把结果写进系统" → write
- "通知到 IM" → notify
- "数据格式转换" → transform
- 一条能力同时跨两类时，拆成两条 external capability，`linked_skills` 都指向同一条 skill

## 质量自检

每次发 terminal artifact 前、和最终给出口信号前，对照检查：

- [ ] 当前阶段要写入 artifact data 的条目是否都达到下游可消化的明确度
- [ ] 当前阶段是否仍有用户未确认的关键项；如果有，不能抢先发 terminal artifact 或进入下一阶段
- [ ] 阶段 1 收口时，是否已先向用户确认"可以推进到技能定义阶段吗？"并收到肯定回应，才发出 `material_handoff_summary`；禁止在用户未确认推进的情况下自动解锁阶段 2
- [ ] 如果当前阶段存在上传文件条目，是否每条都已补全 `source_path`，且没有“内容未能读取到但仍标记 ready”的情况；若任一不满足，不能发 `material_handoff_summary`
- [ ] 如果用户刚上传文件而 `source_path` 一时未出现，是否已经给过最多 5 秒的有界等待；不要把短暂同步竞态直接当成最终失败
- [ ] 是否存在同一资料、同一来源文件或父子包含关系的重复整理项；如果有，先合并或撤销旧范围
- [ ] 是否在配置文件治理的反问待确认状态中错误地发了 terminal artifact
- [ ] 阶段 2 是否已经依次发出 `skill_definition_ready`、`ontology_projection_ready`、`skill_generation_ready` 三个确认门，且每个确认门都等待了用户明确回应
- [ ] 是否在对话里收集了凭据值（如发现，立刻删除并指引到表单）
- [ ] 给用户的反馈是否保持"一行确认"风格，没有变成大段汇报
- [ ] **资料阶段 terminal artifact 发出后，是否已立即触发 `ontology-extraction`**（不等用户输入，不先进入技能阶段；若已在执行则不重复触发）
- [ ] **发 `skill_workorder_progress` 之前，`ontology_extraction_done` 是否已到达**；若未到达，禁止进入技能定义收集
- [ ] 用户确认开始准备业务资料后，是否按 `downstream-handoff-registry.md` 的 R2 触发 projection pass，并等待 `ontology_projection_done`
- [ ] `ontology_projection_done` 是否包含可消费的 `projection_paths[]`；若没有，是否停留在阶段 2，而不是降级触发 `skill-generation`
- [ ] `ontology_projection_done` 可消费后，是否先发 `skill_generation_ready` 并等待用户确认；触发 `skill-generation` 时是否按 R3 传入 `projection_binding_confirmed: true`，且避免把该字段写进 `skill_generation_ready`
- [ ] 外部配置保存或跳过后，若用户选择生成测试用例，是否按 R4 触发 `packaging-test-cases`；若用户说“打包/生成数字员工”，是否视为跳过测试用例并继续审查门
- [ ] Manifest 同步后、打包工具调用前，是否发出 `review_readiness`；用户确认审查时是否按 R5 触发 `digital-employee-package-completeness-review`，而不是由主 skill 直接跑 validator
