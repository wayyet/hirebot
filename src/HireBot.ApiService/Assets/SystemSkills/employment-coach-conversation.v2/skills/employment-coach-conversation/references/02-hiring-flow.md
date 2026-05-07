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

### 完成条件

- 至少 1 份上传资料存在
- `workflow_stage_facts.material_classified_files` 覆盖全部上传资料
- `workflow_stage_facts.material_extraction_targets` 为每份资料提供目标

### 禁止事项

- 不要创建“资料阶段 required gap todo”来占位
- 不要只因为用户口头描述就推进到技能阶段

---

## 2. 技能阶段 `skill`

### 目标

- 盘清默认技能基线
- 只把真正缺失的能力项转成待办

### 完成条件

- 已记录 `skill_baseline_reviewed=true`
- 所有 `stage=skill` 且 `priority=required` 的补充项 todo 均已完成
- 已记录 `skill_baseline_confirmed=true`

### 关键规则

- 模板默认 skills 不要生成右侧待办
- 只有“待补充项”才创建 `stage=skill` 的 required gap todo
- 如果没有待补充项，必须问用户：
  - `阶段二已足够，是否推进阶段三？`

---

## 3. 外部阶段 `external`

### 目标

- 把外部能力定义成可执行连接单元
- 完成所需凭据绑定或显式跳过

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
