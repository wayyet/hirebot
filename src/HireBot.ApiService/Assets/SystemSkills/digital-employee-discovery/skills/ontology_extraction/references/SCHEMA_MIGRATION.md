# Schema 版本迁移说明

本文档用于定义 `ontology_extraction` 规范包里 slice schema 与 projection schema 的版本迁移规则、操作顺序和兼容策略。它的目标不是重复 schema 文件内容，而是回答另一个问题：当结构版本真的要升级时，团队应该如何迁移，才能不把模板、样例、校验器和评审口径弄乱。

相关文件：

- `../templates/TEMPLATE.schema.json`：slice 结构约束
- `../templates/PROJECTION_TEMPLATE.schema.json`：projection 结构约束
- `../templates/TEMPLATE.json`：slice 模板
- `../templates/PROJECTION_TEMPLATE.json`：projection 模板
- `../examples/ready/`、`../examples/warning/`、`../examples/invalid/`：内置三态样例
- `../scripts/validate-slice.py`：技能根目录内的 slice 真实校验器
- `../scripts/validate-projection.py`：技能根目录内的 projection 真实校验器
- `scripts/validate-ontology-slice.py`：仓库根目录 slice 包装入口
- `scripts/validate-ontology-projection.py`：仓库根目录 projection 包装入口
- `./FIELD_GUIDE.md`：slice 字段语义说明
- `./REVIEW_CHECKLIST.md`：slice 与 projection 统一评审标准

---

## 当前版本线

当前规范包里有两条独立的版本线：

- slice 结构版本：`templates/TEMPLATE.schema.json` 中的 `schema_version`
- projection 结构版本：`templates/PROJECTION_TEMPLATE.schema.json` 中的 `projection_version`

当前两者都固定为 `1.0.0`。这意味着：只要 schema 仍要求常量 `1.0.0`，所有 slice / projection 文件也都必须继续写 `1.0.0`，否则校验器会直接失败。

---

## 什么时候需要升级版本

只有在输出结构发生兼容性变化时才升级版本，例如：

- 新增必填字段
- 删除已有字段
- 修改字段类型、枚举、ID 模式或默认语义
- 改变 review、mapping 或 traceability 的强约束口径
- 调整 projection contract，导致旧样例或旧生成链路不能按原规则继续工作

以下情况通常不需要升级版本：

- 只补充文档说明
- 只增加新的合法样例
- 只调整描述文字，但不改变 JSON 结构约束
- 只优化校验器输出文案，但不改变 schema 要求

---

## 版本升级原则

### 1. 先确认是结构变化，不是内容变化

如果变化只影响样例内容、说明文字或 review 表达，不应升级 schema 版本。版本号只服务结构兼容边界，不服务业务内容演进。

### 2. Slice 与 Projection 分开判断

slice schema 与 projection schema 是两条独立版本线。不要因为 projection contract 变化就强行升级 slice 版本，也不要因为 slice 增补了说明文档就顺手改 projection 版本。

### 3. 模板、样例、校验器必须同批次同步

版本升级不是只改一个 schema 常量。只改 schema 而不改模板、样例、README、review 文档，会导致团队在一段时间内拿着旧模板写新结构，最终比不升级更混乱。

---

## 推荐迁移顺序

无论是 slice schema 还是 projection schema，建议都按下面顺序迁移：

1. 先修改对应 schema 文件里的常量版本和结构约束。
2. 再同步更新对应模板：`templates/TEMPLATE.json` 或 `templates/PROJECTION_TEMPLATE.json`。
3. 再同步更新所有内置样例：`examples/ready/`、`examples/warning/`、`examples/invalid/`。
4. 再更新说明文档：`README.md`、`references/FIELD_GUIDE.md`、`references/REVIEW_CHECKLIST.md`、`references/DOWNSTREAM_MAPPING_GUIDE.md`，以及当前这份迁移说明。
5. 最后运行校验脚本，确认新版本样例全部符合预期。

建议不要颠倒顺序，尤其不要先改样例版本号再补 schema。那样会在迁移中间态制造一批必然失败的文件。

---

## 团队兼容策略

如果未来出现 `2.0.0` 之类的新版本，推荐先明确采用哪一种兼容策略：

- 严格切换：schema 直接只接受新版本，旧文件必须批量迁移后再提交。
- 双版本过渡：短期内同时保留旧 schema 和新 schema，各自有独立模板与校验入口。

对当前仓库，更推荐严格切换，而不是把多个版本长期混在同一套模板和样例目录里。否则样例、review 口径和下游投影规则会很快失去一致性。

只有在以下情况，才值得考虑短期双版本过渡：

- 仓库里已有大量历史 slice / projection 需要逐批迁移
- 多个团队同时依赖旧模板，无法在一个提交窗口内完成切换
- 下游代码生成、prompt 编排或 CI 仍需要一段缓冲期

即便采用双版本过渡，也应给出明确截止时间，并把目录、模板和校验入口分清，不要让一个 schema 文件同时模糊接收两个版本。

---

## 最低迁移核对清单

每次升级 schema 版本前，至少确认下面几件事：

- 版本变化是否真的来自结构变化，而不是文档变化。
- 模板、样例、校验脚本和 README 是否已经同步。
- 技能根目录内 `validate-slice.py` 和 `validate-projection.py` 的默认样例是否仍能通过。
- 如需从仓库根目录做普通结构校验，`validate-ontology-slice.py` 和 `validate-ontology-projection.py` 的默认入口是否仍能通过。
- warning / invalid 样例是否仍保留各自的预期定位。
- review 文档是否已反映新版本的字段口径和迁移边界。
- 下游 codegen / prompt orchestration 是否仍能读懂新的 projection 结构。

---

## 实际迁移建议

### 对当前仓库的建议

在没有明确结构变更前，不要手动把任何 slice 或 projection 文件里的版本号改成非 `1.0.0`。正确顺序应该是：先改 schema，再改模板和样例，最后再让团队按新版本继续产出。

### 对 PR 的建议

如果一个 PR 声称升级 schema 版本，但没有同时修改以下至少大部分内容，应视为不完整迁移：

- 对应 schema 文件
- 对应模板文件
- ready / warning / invalid 样例
- README 或 references 中的口径说明
- 校验脚本验证结果

### 对评审的建议

评审迁移 PR 时，优先问三件事：

1. 旧版本为什么不够用了。
2. 新版本破坏了哪些兼容边界。
3. 模板、样例、校验器和评审文档是否已经一起收口。

如果这三个问题回答不清，就不应把版本升级当作已完成。
