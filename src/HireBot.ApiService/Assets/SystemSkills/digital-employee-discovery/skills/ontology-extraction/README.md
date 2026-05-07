# ontology-extraction 规范包

这个目录 references + scripts + templates + examples”的结构：根目录承担技能入口和总览，`references/` 存放参考资料与评审准则，`scripts/` 提供本地可执行校验器，`templates/` 和 `examples/` 负责交付格式与三态样例。

适用场景：

- 需要从大型 ontology / taxonomy / schema 中抽取当前任务相关的最小切片
- 需要把概念、关系、约束整理成稳定结构，供模型、程序或团队协作复用
- 需要把 slice 稳定投影到 projection / codegen / prompt orchestration 等下游输入
- 需要让不同成员产出的 ontology slice 能按统一格式校验和审阅
- 需要先解析用户上传的任意格式文件，并把结果增量沉淀到沙箱 `ontology/`

---

## 目录结构

- `SKILL.md`：skill 主说明，定义何时触发、怎么切片、输出应覆盖什么。
- `README.md`：当前规范包总览。
- `templates/`
  - `TEMPLATE.md`：人工整理版模板。
  - `TEMPLATE.json`：工程化 JSON 模板。
  - `TEMPLATE.schema.json`：严格 JSON Schema，`meta.todo_context` 可携带雇佣教练系统 todo 的结构化回指。
  - `DISPATCH_CALLBACK.schema.json`：`ontology-extraction` 回传主 skill 的 `dispatch_callback` 结构校验规则。
  - `PROJECTION_TEMPLATE.json`：下游 codegen / prompt orchestration 投影模板。
  - `PROJECTION_TEMPLATE.schema.json`：projection 文件校验规则。
  - `CONSUMER_SKILL_PROJECTION_SECTION.md`：consumer skill 复用的最小 `Projection Contracts` 段落模板。
  - `CONSUMER_SKILL_SCAFFOLD.md`：创建新 consumer skill 时可直接复制的最小完整骨架。
  - `NEW_CONSUMER_SKILL_CHECKLIST.md`：复制 scaffold 后用于替换占位符和删减不适用字段的创建清单。
  - `EXAMPLE_CONSUMER_SKILL.md`：已经替换完占位符、删减完字段的完整 consumer skill 示例。
- `references/`
  - `FIELD_GUIDE.md`：字段语义说明。
  - `REVIEW_CHECKLIST.md`：同时覆盖 slice 和 projection 两层评审标准。
  - `DOWNSTREAM_MAPPING_GUIDE.md`：下游代码生成 / 提示词编排映射规范。
  - `PROJECTION_CONSUMPTION_GUIDE.md`：其他 skill 如何消费 `projection.json`。
  - `CONSUMER_PROJECTION_LAYOUT_GUIDE.md`：consumer skill 专用 projection 目录与命名规范。
  - `SCHEMA_MIGRATION.md`：slice 与 projection 的 schema 版本迁移说明。
- `../../../../docs/`
  - `SESSION_SUMMARY.md`：本次规范包从 slice skill 演进到完整治理包的总结文章。
- `examples/ready/`
  - `sample.json`：`READY` 基线样例。
  - `sample-projection.json`：基于 `sample.json` 的合法 projection 样例。
  - `json-schema-projection.json`：面向 `json_schema_projection` 的 `READY` 样例。
  - `workflow-contract-projection.json`：面向 `workflow_contract_projection` 的 `READY` 样例。
  - `minimal-projection.json`：最小可机读 projection 样例。
  - `employment-coach-todo-slice.json`：包含 `meta.todo_context` 的雇佣教练资料阶段 slice 样例。
  - `employment-coach-dispatch-callback.json`：雇佣教练下游回传 `dispatch_callback` 样例。
  - `sample.entry.md`：`sample.json` 的短入口页。
  - `sample-projection.entry.md`：`sample-projection.json` 的短入口页。
  - `json-schema-projection.entry.md`：`json-schema-projection.json` 的短入口页。
  - `workflow-contract-projection.entry.md`：`workflow-contract-projection.json` 的短入口页。
  - `sample.md`：`sample.json` 的人读评审版。
  - `sample-projection.md`：`sample-projection.json` 的人读评审版。
  - `json-schema-projection.md`：`json-schema-projection.json` 的人读评审版。
  - `workflow-contract-projection.md`：`workflow-contract-projection.json` 的人读评审版。
- `examples/warning/`
  - `warning-sample.json`：`WARNING` 黄灯样例。
  - `warning-sample.entry.md`：短入口页。
  - `warning-sample.md`：黄灯原因说明。
  - `warning-projection.json`：projection 黄灯样例。
  - `warning-projection.entry.md`：`warning-projection.json` 的短入口页。
  - `warning-projection.md`：`warning-projection.json` 的人读评审版。
- `examples/invalid/`
  - `invalid-sample.json`：`FAIL` 失败样例。
  - `invalid-sample.entry.md`：短入口页。
  - `invalid-sample.md`：失败路径评审说明。
  - `invalid-projection.json`：projection 失败样例。
  - `invalid-projection.entry.md`：`invalid-projection.json` 的短入口页。
  - `invalid-projection.md`：`invalid-projection.json` 的失败路径说明。
- `scripts/`
  - `validate-slice.py`：slice 真实校验器，支持 `--schema-path` 与 `--review-mode`。
  - `validate-projection.py`：projection 真实校验器，支持 `--schema-path` 与 `--review-mode`。
- 仓库根目录 `scripts/`
  - `validate-ontology-slice.py`：slice 的包装入口，适合从仓库根目录或任意当前目录直接发起普通结构校验。
  - `validate-ontology-projection.py`：projection 的包装入口，适合从仓库根目录或任意当前目录直接发起普通结构校验。

---

## 核心思路

`ontology-extraction` 的核心不是“把整份 ontology 拿出来”，而是围绕当前任务抽取最小可验证子图：

- `concepts`：当前任务真正依赖的概念
- `relations`：这些概念之间必须保留的关系
- `constraints`：会影响实现、评审或生成结果的规则边界
- `sources`：所有结论的可追溯依据

这让它既能服务人工评审，也能作为 JSON 校验、代码生成和提示词编排的稳定输入。

如果输入还只是上传文件而不是现成 slice，`ontology-extraction` 应直接读取可用资料，围绕当前 todo 生成或更新 `ontology/*.slice.json` 与 `ontology/*.slice.md`，再在此基础上做校验和 projection。

---

## 上传资料处理口径

当前技能包不依赖额外的资料接入 skill 或工具。`ontology-extraction` 自己负责把可读资料转成正式 ontology slice。

它负责：

- 接收 material todo 中的 `payload.source_files`
- 读取 Markdown、文本、JSON、YAML 等可读资料
- 如果运行时提供了文档或 zip 的解析后文本路径，则读取解析后内容；不要假设存在额外解析工具
- 按 `incremental` 或 `full_replace` 更新当前主题 slice
- 同时生成 `.json` 与 `.md` slice，并在 `sources` 中记录资料来源
- 返回包含资料解析、切片范围、更新模式和产物路径的 `extraction_summary`

它不负责：

- 判定复杂冲突类型
- 请求用户对同名节点做人工裁决
- 直接替代后续的 slice 校验、projection 校验或人工 review
- 删除人工维护的其他 ontology 文件

当 `ontology-extraction` 作为 `employment-coach-conversation` 的下游被调起时，只处理 `stage: material`、`target_skill: ontology-extraction`、`status: ready_to_dispatch | dirty` 且出现在本次 `dispatch.todos` 中的系统 todo。todo 中的 `payload.mode` 缺失时使用 dispatch `mode`，仍缺失时默认 `incremental`。`incremental` 表示在现有同主题 slice 上增量合并，`full_replace` 表示替换同主题 slice 的内容；两者都只作用于本 skill 产出的 slice。回传主 skill 时应提供 `dispatch_callback`，包含 `source_dispatch_target`、业务用户可读的 `user_summary`、聚合 `technical_artifact`、artifact 相对路径、逐条 `todo_results`、整体 `status` 与 `errors`，以支持多 todo 批次中的部分成功 / 部分失败确认。

---

## 推荐使用顺序

### 路径一：先人工梳理，再结构化落地

1. 先看 `SKILL.md`，确认当前任务适不适合做 ontology slice 或下游 projection。
2. 如果输入是上传文件而不是现成 slice，直接读取资料并按 `incremental` 或 `full_replace` 生成或更新当前主题 slice。
3. 用 `templates/TEMPLATE.md` 梳理范围、来源、核心概念和约束。
4. 参考 `references/FIELD_GUIDE.md`，统一字段语义和填报口径。
5. 如需接到 codegen 或 prompt orchestration，补看 `references/DOWNSTREAM_MAPPING_GUIDE.md`，先明确投影规则。
6. 再把结果落到 `templates/TEMPLATE.json` 对应结构。
7. 需要形成下游交付物时，复制 `templates/PROJECTION_TEMPLATE.json` 填写 projection。
8. 用 `templates/PROJECTION_TEMPLATE.schema.json` 校验 projection 文件结构。
9. 最后做 slice 校验：如果当前目录就是本技能根目录，使用 `scripts/validate-slice.py`；如果从仓库根目录执行，使用仓库根目录 `scripts/validate-ontology-slice.py`。

### 路径二：直接生成工程化产物

1. 先看 `SKILL.md` 明确切片目标、projection 语义和边界。
2. 如果输入是上传文件，先读取资料并确定本轮 `extraction_summary`。
3. 直接基于 `templates/TEMPLATE.json` 生成结果。
4. 遇到字段拿不准时，回看 `references/FIELD_GUIDE.md`。
5. 需要面向代码生成或提示词编排时，按 `references/DOWNSTREAM_MAPPING_GUIDE.md` 做投影。
6. 复制 `templates/PROJECTION_TEMPLATE.json`，形成可交付的 projection 文件。
7. 做 projection 结构校验：如果当前目录就是本技能根目录，使用 `scripts/validate-projection.py`；如果从仓库根目录执行，使用仓库根目录 `scripts/validate-ontology-projection.py`。
8. 做 slice 结构校验：如果当前目录就是本技能根目录，使用 `scripts/validate-slice.py`；如果从仓库根目录执行，使用仓库根目录 `scripts/validate-ontology-slice.py`。

### 路径三：按三态样例做团队对齐

1. 先看 `examples/ready/sample.json -> sample.entry.md -> sample.md` 建立正向基线。
2. 再看 `examples/ready/sample-projection.json -> sample-projection.entry.md -> sample-projection.md`，理解 READY slice 如何落成可交付 projection。
3. 再看 `examples/ready/json-schema-projection.json -> json-schema-projection.entry.md -> json-schema-projection.md`，理解同一份 slice 如何投影成 JSON Schema 契约。
4. 再看 `examples/ready/workflow-contract-projection.json -> workflow-contract-projection.entry.md -> workflow-contract-projection.md`，理解同一份 slice 如何投影成 workflow contract。
5. 再看 `examples/warning/warning-sample.json -> warning-sample.entry.md -> warning-sample.md` 学会识别黄灯风险。
6. 再看 `examples/warning/warning-projection.json -> warning-projection.entry.md -> warning-projection.md` 理解 projection 为什么也可能是黄灯。
7. 最后看 `examples/invalid/invalid-sample.json -> invalid-sample.entry.md -> invalid-sample.md` 理解失败路径和报错边界。
8. 再看 `examples/invalid/invalid-projection.json -> invalid-projection.entry.md -> invalid-projection.md` 理解 projection 结构失败长什么样。
9. 回到 `references/REVIEW_CHECKLIST.md`，把三态样例统一成同一套评审口径。

### README 版五步顺序

如果目标是把 slice 真正交付成 consumer skill 可加载的 projection contract，而不是只停留在 producer 侧文档，可直接按下面顺序执行：

1. 如果输入还是上传文件，先由 `ontology-extraction` 直接读取资料，按指定模式生成或更新当前主题 slice，并确认 `sources` 与 `extraction_summary` 对齐。
2. 再在 `ontology-extraction` 中收缩当前主题，产出最小可验证 slice，并先让 slice 通过对应校验器校验：在技能根目录使用 `validate-slice`，在仓库根目录使用 `validate-ontology-slice`。
3. 先决定本次只面向哪一种主交付视图：`domain-model`、`json-schema`、`prompt-constraint` 或 `workflow-contract`。
4. 基于已通过校验的 slice，由 `ontology-extraction` 按映射规范填充 `PROJECTION_TEMPLATE.json`，把 `concepts`、`relations`、`constraints` 显式映射到 projection。
5. 验证 projection 时按所在层级选择入口：在技能根目录使用 `validate-projection.py`，在仓库根目录使用 `validate-ontology-projection.py`，确保结构、关键字段和本地诊断全部通过。
6. projection 验证通过后，再将产物放入 consumer skill 的 `contracts/projections` 目录，并同步更新 `contract-index.json`、view 路由和必要的 routing hints。

---

## 快速选择

- 想把上传文件转成当前沙箱 `ontology/` 下的 slice：用本 `ontology-extraction`
- 只想先讨论概念边界：用 `templates/TEMPLATE.md`
- 想输出给程序、流水线或下游 projection：用 `templates/TEMPLATE.json`
- 想把 slice 投影成 projection / codegen / prompt orchestration 输入：用 `templates/PROJECTION_TEMPLATE.json`
- 想给 consumer skill 复用最小 `Projection Contracts` 段落：用 `templates/CONSUMER_SKILL_PROJECTION_SECTION.md`
- 想新建一个可消费 projection 的 consumer skill：用 `templates/CONSUMER_SKILL_SCAFFOLD.md`
- 想检查新 consumer skill 复制 scaffold 后还有哪些占位符和字段需要清理：用 `templates/NEW_CONSUMER_SKILL_CHECKLIST.md`
- 想直接参考一份已经完成占位符替换和字段删减的最终样板：用 `templates/EXAMPLE_CONSUMER_SKILL.md`
- 想检查结果合不合法：用 `templates/TEMPLATE.schema.json`
- 想检查 projection 结构是否合法：用 `templates/PROJECTION_TEMPLATE.schema.json`
- 想直接从一个合法 projection 样例开始改：用 `examples/ready/sample-projection.json`
- 想直接从 JSON Schema projection 样例开始改：用 `examples/ready/json-schema-projection.json`
- 想直接从 workflow contract projection 样例开始改：用 `examples/ready/workflow-contract-projection.json`
- 想从最小合法 projection 骨架开始改：用 `examples/ready/minimal-projection.json`
- 想看 projection 的短入口：用 `examples/ready/sample-projection.entry.md`
- 想看 projection 为什么算 READY：用 `examples/ready/sample-projection.md`
- 想看 JSON Schema projection 的短入口：用 `examples/ready/json-schema-projection.entry.md`
- 想看 JSON Schema projection 为什么算 READY：用 `examples/ready/json-schema-projection.md`
- 想看 workflow contract projection 的短入口：用 `examples/ready/workflow-contract-projection.entry.md`
- 想看 workflow contract projection 为什么算 READY：用 `examples/ready/workflow-contract-projection.md`
- 想统一字段口径：看 `references/FIELD_GUIDE.md`
- 想统一 slice 和 projection 两层评审标准：看 `references/REVIEW_CHECKLIST.md`
- 想看 schema 升级时模板、样例和校验器该怎么一起迁移：看 `references/SCHEMA_MIGRATION.md`
- 想快速理解这套规范为什么会演进成现在这套结构：看 `../../../../docs/SESSION_SUMMARY.md`
- 想把 slice 稳定接到 projection、codegen 或 prompt orchestration：看 `references/DOWNSTREAM_MAPPING_GUIDE.md`
- 想让其他 skill 正式消费 projection 文件：看 `references/PROJECTION_CONSUMPTION_GUIDE.md`
- 想统一 consumer skill 内 projection 的目录和命名：看 `references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md`
- 想直接在真实样例上改：用 `examples/ready/sample.json`（`READY`）
- 想看 `READY` 的短入口：用 `examples/ready/sample.entry.md`
- 想看 `READY` 的完整解释：用 `examples/ready/sample.md`
- 想演示“合法但仍需 review”：用 `examples/warning/warning-sample.json`（`WARNING`）
- 想看 `WARNING` 的短入口：用 `examples/warning/warning-sample.entry.md`
- 想看 `WARNING` 的完整解释：用 `examples/warning/warning-sample.md`
- 想看 projection 的黄灯样例：用 `examples/warning/warning-projection.json`
- 想看 projection 黄灯的短入口：用 `examples/warning/warning-projection.entry.md`
- 想看 projection 为什么是 WARNING：用 `examples/warning/warning-projection.md`
- 想验证失败报错是否可读：用 `examples/invalid/invalid-sample.json`（`FAIL`）
- 想看 `FAIL` 的短入口：用 `examples/invalid/invalid-sample.entry.md`
- 想逐条 review 失败点：用 `examples/invalid/invalid-sample.md`
- 想看 projection 的失败样例：用 `examples/invalid/invalid-projection.json`
- 想看 projection 失败的短入口：用 `examples/invalid/invalid-projection.entry.md`
- 想看 projection 为什么是 FAIL：用 `examples/invalid/invalid-projection.md`
- 想单独分享“该不该用这套规范”的判定入口：看 `references/DECISION_GUIDE.md`
- 想在技能根目录一条命令校验样例或自定义 slice：用 `scripts/validate-slice.py`
- 想在仓库根目录或任意当前目录一条命令校验 slice：用仓库根目录 `scripts/validate-ontology-slice.py`
- 想在技能根目录一条命令校验样例或自定义 projection：用 `scripts/validate-projection.py`
- 想在仓库根目录或任意当前目录一条命令校验 projection：用仓库根目录 `scripts/validate-ontology-projection.py`

---

## 红黄绿决策图

如果团队在使用前只想先判断一件事: `ontology-extraction` 到底适不适合当前场景，先看 `references/DECISION_GUIDE.md`。

这份独立指南专门回答三类问题：

- 当前任务到底该不该做 ontology slice。
- 当前阶段能不能直接用本规范沉淀结果。
- 当前产出更接近 `READY` 候选、`WARNING` 草案，还是暂时不该进入本规范。

判定口径保持不变：

- `绿灯`：适合按本规范继续做 slice / projection / review。
- `黄灯`：可以用，但只能作为受控草案、局部治理视图，或需要补证据后再推进。
- `红灯`：当前不适合，应先补主题、补来源，或改用其他 ontology / knowledge modeling 方法。

---

## 最低交付标准

一份可接受的 ontology slice，至少应满足：

- 明确当前任务和切片主题
- 明确纳入范围和排除范围
- 至少有一个可追溯来源
- 至少有一个定义清晰的核心概念
- 概念、关系、约束之间不存在引用断裂
- 已显式记录冲突、歧义或不确定项
- 能通过 `templates/TEMPLATE.schema.json` 校验

---

## ReviewMode 说明

`scripts/validate-slice.py --review-mode` 会在结构校验结果后，额外输出一个启发式判定：`READY / WARNING / FAIL`。

仓库根目录包装脚本 `scripts/validate-ontology-slice.py` 只承载普通结构校验入口，不暴露 `ReviewMode` / `--review-mode`。

当前启发式结论含义：

- `FAIL`：结构校验未通过。
- `READY`：结构校验通过，且没有命中当前 warning 信号；内置 `examples/ready/sample.json` 也直接视为 ready baseline。
- `WARNING`：结构校验通过，但命中了 warning 信号；内置 `examples/warning/warning-sample.json` 也直接视为 yellow-light baseline。

当前会触发 `WARNING` 的信号：

- `sources` 中没有任何 `trust_level = high` 的来源。
- `sources` 中存在任意 `trust_level = low` 的来源。
- `conflicts` 中存在 `status = open` 或 `status = deferred` 的冲突。
- `ambiguities` 中存在 `status = open` 或 `status = deferred` 的歧义。
- `uncertainties` 数组非空。

这套规则用于快速分流，不替代 `references/REVIEW_CHECKLIST.md` 的人工评审。

---

## 校验脚本用法

如果当前目录就是本技能根目录，可直接执行：

```text
# 校验默认 READY 基线样例
c:/python314/python.exe .\scripts\validate-slice.py

# 校验单个自定义 slice
c:/python314/python.exe .\scripts\validate-slice.py .\my-slice.json

# 一次校验多个 slice
c:/python314/python.exe .\scripts\validate-slice.py .\examples\ready\sample.json .\team-a.json .\team-b.json

# 输出 READY / WARNING / FAIL 的启发式提示
c:/python314/python.exe .\scripts\validate-slice.py .\examples\ready\sample.json --review-mode

# 查看失败样例的报错输出
c:/python314/python.exe .\scripts\validate-slice.py .\examples\invalid\invalid-sample.json
```

如果需要显式指定 schema 路径：

```text
c:/python314/python.exe .\scripts\validate-slice.py .\examples\ready\sample.json --schema-path .\templates\TEMPLATE.schema.json
```

在仓库根目录执行：

```text
c:/python314/python.exe .\scripts\validate-ontology-slice.py
c:/python314/python.exe .\scripts\validate-ontology-slice.py .\path\to\team-slice.json
c:/python314/python.exe .\scripts\validate-ontology-slice.py .\src\OpenClaw.Gateway\skills\ontology-extraction\examples\invalid\invalid-sample.json
```

根目录包装脚本仍保持“普通结构校验入口”的角色，不额外承载 `--review-mode`。

### Projection 校验

如果当前目录就是本技能根目录，可直接执行：

```text
# 校验默认 READY projection 基线样例
c:/python314/python.exe .\scripts\validate-projection.py

# 校验单个自定义 projection
c:/python314/python.exe .\scripts\validate-projection.py .\my-projection.json

# 一次校验多个 projection
c:/python314/python.exe .\scripts\validate-projection.py .\examples\ready\sample-projection.json .\team-a-projection.json .\team-b-projection.json

# 查看黄灯 projection 样例（应通过结构校验，但仍需人工 review）
c:/python314/python.exe .\scripts\validate-projection.py .\examples\warning\warning-projection.json

# 输出结构层结论，并提示 projection review 入口
c:/python314/python.exe .\scripts\validate-projection.py .\examples\warning\warning-projection.json --review-mode

# 输出 projection 的 READY / WARNING / FAIL 启发式提示
c:/python314/python.exe .\scripts\validate-projection.py .\examples\ready\sample-projection.json --review-mode

# 查看 projection 失败样例的报错输出
c:/python314/python.exe .\scripts\validate-projection.py .\examples\invalid\invalid-projection.json
```

如果需要显式指定 projection schema 路径：

```text
c:/python314/python.exe .\scripts\validate-projection.py .\examples\ready\sample-projection.json --schema-path .\templates\PROJECTION_TEMPLATE.schema.json
```

在仓库根目录执行：

```text
c:/python314/python.exe .\scripts\validate-ontology-projection.py
c:/python314/python.exe .\scripts\validate-ontology-projection.py .\path\to\team-projection.json
c:/python314/python.exe .\scripts\validate-ontology-projection.py .\src\OpenClaw.Gateway\skills\ontology-extraction\examples\invalid\invalid-projection.json
```

根目录 projection 包装脚本同样保持“普通结构校验入口”的角色，不额外承载 `--review-mode`。

---

## Schema 版本迁移说明

迁移规则已单独整理到 `references/SCHEMA_MIGRATION.md`，README 这里只保留最小入口。

当前规范包仍使用两条独立版本线：

- slice：`schema_version`
- projection：`projection_version`

当前两者都固定为 `1.0.0`。在没有明确结构变更前，不要单独修改任何样例、模板或产出文件里的版本号。

如果后续真的要升级版本，优先阅读 `references/SCHEMA_MIGRATION.md`，按“先 schema、再模板、再样例、再文档、最后校验”的顺序迁移。

---

## 后续可扩展方向

- 增加自动校验脚本或 CI 校验入口
- 继续增加按运行时或交付介质细分的 projection 样例（如更细的 prompt policy、tool contract、event schema）
