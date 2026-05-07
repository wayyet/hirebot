# sample 评审说明

本文档是 [sample.json](sample.json) 的人工评审版，对应同一份 ontology slice 内容，但以更适合人阅读和评审的 Markdown 形式展开。

---

## 样例定位

- 角色：`READY` 正向参考样例。
- 用途：演示什么叫“结构合法且语义基本过关”的 ontology slice。
- 适用场景：团队需要一个可直接复制改写的基线样例时。

## 使用方式

```powershell
..\..\scripts\validate-slice.py .\sample.json --review-mode
```

上面的命令适用于当前目录位于 `ontology-extraction` 技能根目录内，调用的是支持 `--review-mode` 的真实校验器。

如果从仓库根目录执行普通结构校验：

```powershell
.\scripts\validate-ontology-slice.py .\src\OpenClaw.Gateway\skills\ontology-extraction\examples\ready\sample.json
```

仓库根目录包装入口只承载普通结构校验，不暴露 `--review-mode`。

## 结构层表现

- 结构结果：`PASS`
- 评审状态：`READY`
- 当前解读：这份样例既通过 schema，也被当作 ready baseline。

## 对应为什么会得到 READY

- 内置样例约定上直接作为 ready baseline。
- 多个高信任度来源共同支撑关键概念、关系和规则。
- 冲突已有处理结果，剩余不确定项不阻断当前主要用途。

## 推荐评审方式

1. 先确认它仍与当前仓库实现一致。
2. 再确认高信任度来源是否依然覆盖关键结论。
3. 最后检查术语口径是否需要随代码演进更新。

## 建议评审结论模板

- 结构合法性：已通过 schema 校验
- 评审状态：`READY`
- 当前结论：适合作为正向参考样例，可作为团队复制改写起点

## 最适合怎么用

- 作为团队正向基线样例
- 作为 onboarding 时的“合格输出”示例
- 作为后续自定义 slice 的起点

## 详细内容

### 切片请求

- 任务名称：OpenClaw 技能加载与筛选建模
- 切片主题：技能加载顺序、来源优先级与资格判定
- 任务目标：抽取 OpenClaw.NET 技能系统中与技能发现、来源覆盖、配置覆盖和资格筛选相关的核心概念、关系与约束，用于后续文档统一、代码生成和规则校验。
- 期望输出：
  - `concept_table`
  - `relation_table`
  - `constraint_list`
  - `schema_generation`

### 切片摘要

- 主题：技能加载顺序、来源优先级与资格判定
- 一句话结论：该切片定义了 OpenClaw 技能系统如何从多来源发现技能、按来源优先级覆盖同名技能，并在配置和 requirements 规则下筛选最终 eligible skills。
- 选取依据：优先纳入直接决定技能发现、覆盖、配置覆盖和 eligibility 的结构与规则。
- 排除依据：排除具体 skill 内容、安装分发过程和提示词格式化细节，以保持切片边界聚焦。

---

### 范围

### 纳入范围

- `C1` 技能系统配置：它是技能发现、筛选和覆盖行为的顶层入口。
- `C3` 技能定义：最终筛选与注入模型提示词的核心对象。
- `C4` 技能来源：来源决定扫描顺序、覆盖顺序和优先级。
- `R3` 技能定义来源关系：用于表达 `SkillDefinition` 与 `SkillSource` 的绑定。
- `K1` 来源优先级规则：它决定同名技能最终保留哪个版本。

### 排除范围

- 技能具体业务内容：本次切片只关注加载与筛选机制，不分析每个 skill 的指令正文。
- ClawHub 安装流程细节：安装过程属于技能分发与获取，不属于运行时加载语义核心。
- 模型提示词拼接细节：虽然与技能消费相关，但不属于本次加载与筛选切片范围。

---

### 依据来源

| 来源ID | 路径 | 类型 | 角色 | 优先级 | 信任度 |
| --- | --- | --- | --- | --- | --- |
| S1 | `docs/skillloader-loadall-analysis.md` | `document` | 解释技能加载主流程、来源顺序和筛选阶段语义。 | 1 | `high` |
| S2 | `src/OpenClaw.Core/Skills/SkillLoader.cs` | `code` | 给出技能扫描、覆盖和资格筛选的实际实现。 | 1 | `high` |
| S3 | `src/OpenClaw.Core/Skills/SkillModels.cs` | `code` | 定义 `SkillsConfig`、`SkillDefinition`、`SkillSource`、`SkillMetadata` 等核心结构。 | 1 | `high` |
| S4 | `docs/USER_GUIDE.md` | `document` | 补充技能目录位置、工作区与托管技能的用户侧语义。 | 2 | `medium` |

---

### 核心概念

| 概念ID | 中文名 | 英文名 | 类型 | 上位概念 | 定义 |
| --- | --- | --- | --- | --- | --- |
| C1 | 技能系统配置 | `SkillsConfig` | `entity` | 无 | 控制技能系统总开关、加载来源、allowlist 和 per-skill 覆盖条目的顶层配置对象。 |
| C2 | 技能加载配置 | `SkillLoadConfig` | `value_object` | C1 | 描述技能应从哪些来源加载以及是否启用 watch 等行为的配置值对象。 |
| C3 | 技能定义 | `SkillDefinition` | `entity` | 无 | 从 `SKILL.md` 解析得到的技能定义对象，包含名称、描述、指令、位置、来源和 metadata。 |
| C4 | 技能来源 | `SkillSource` | `value_object` | 无 | 表示技能从哪个来源被发现与加载的来源类型集合，决定同名技能覆盖优先级。 |
| C5 | 技能元数据 | `SkillMetadata` | `value_object` | C3 | 从 `SKILL.md` frontmatter 解析出的元数据对象，用于表达 `always`、`requireBins`、`requireEnv` 等 eligibility 相关条件。 |
| C6 | 技能条目覆盖配置 | `SkillEntryConfig` | `value_object` | C1 | 按技能名或 `skillKey` 存储的单个技能覆盖配置，用于启用禁用、注入 env、apiKey 和自定义 config。 |
| C7 | 技能资格判定规则 | `SkillEligibilityRule` | `rule` | 无 | 用于决定已发现技能能否进入 eligible 集合的组合规则，包括 `allowBundled`、entry disable 和 requirements gating。 |

### 关键属性

#### C1 `SkillsConfig`

| 属性 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `enabled` | `boolean` | 是 | 控制技能系统是否启用。 |
| `allowBundled` | `array` | 是 | 限定允许进入 eligible 集合的 bundled 技能名列表。 |
| `entries` | `object` | 是 | 按技能名或 `skillKey` 建立的 per-skill 覆盖配置映射。 |

#### C2 `SkillLoadConfig`

| 属性 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `includeBundled` | `boolean` | 是 | 是否加载应用自带 bundled skills。 |
| `includeManaged` | `boolean` | 是 | 是否从用户主目录下的 managed skills 加载技能。 |
| `includeWorkspace` | `boolean` | 是 | 是否加载工作区 skills 目录中的技能。 |
| `extraDirs` | `array` | 是 | 附加技能目录列表，优先级最低。 |

#### C3 `SkillDefinition`

| 属性 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `name` | `string` | 是 | 技能唯一名称，也是覆盖与查找的主要键。 |
| `location` | `string` | 是 | 技能文件系统位置。 |
| `source` | `enum` | 是 | 技能定义来源类型。 |
| `metadata` | `object` | 是 | 解析自 frontmatter 的结构化元数据。 |

#### C4 `SkillSource`

| 属性 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `kind` | `enum` | 是 | 可选值为 `Bundled`、`Managed`、`Workspace`、`Extra`、`Plugin`。 |
| `precedence` | `integer` | 是 | 用于表达来源覆盖顺序的相对优先级。 |

#### C5 `SkillMetadata`

| 属性 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `always` | `boolean` | 是 | 如果为 true，则跳过 requirements gating。 |
| `requireBins` | `array` | 是 | 所有必须存在于 PATH 的二进制名集合。 |
| `requireEnv` | `array` | 是 | 必须存在的环境变量名集合。 |
| `skillKey` | `string` | 否 | 用于映射 per-skill entry 覆盖项的替代 key。 |

#### C6 `SkillEntryConfig`

| 属性 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `enabled` | `boolean` | 是 | 控制该技能是否可进入 eligible 集合。 |
| `env` | `object` | 是 | 注入该技能运行环境的环境变量映射。 |
| `apiKey` | `string` | 否 | 与 `primaryEnv` 配套的 API key 简写配置。 |

#### C7 `SkillEligibilityRule`

| 属性 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `allowBundledCheck` | `boolean` | 是 | 是否对 bundled 技能应用 `allowBundled` 过滤。 |
| `entryDisableCheck` | `boolean` | 是 | 是否按 skill entry 的 `Enabled` 值禁用技能。 |
| `requirementsCheck` | `boolean` | 是 | 是否依据 metadata requirements 对技能做 gating。 |

---

### 核心关系

| 关系ID | 主体 | 谓词 | 客体 | 基数 | 方向 | 说明 |
| --- | --- | --- | --- | --- | --- | --- |
| R1 | C1 | `contains` | C2 | `1:1` | `uni` | `SkillsConfig` 包含一个 `SkillLoadConfig`，用于描述加载来源与 watch 等策略。 |
| R2 | C1 | `contains` | C6 | `1:n` | `uni` | `SkillsConfig` 通过 entries 持有多个 `SkillEntryConfig`，用于 per-skill 覆盖。 |
| R3 | C3 | `originates_from` | C4 | `n:1` | `uni` | 每个 `SkillDefinition` 都携带一个 `SkillSource`，表示该技能来自哪个来源层级。 |
| R4 | C3 | `has_metadata` | C5 | `1:1` | `uni` | `SkillDefinition` 包含解析后的 `SkillMetadata`，用于后续 eligibility 判定。 |
| R5 | C6 | `overrides` | C3 | `n:1` | `uni` | `SkillEntryConfig` 可基于技能名或 `skillKey` 对 `SkillDefinition` 生效结果进行覆盖。 |
| R6 | C7 | `evaluates` | C3 | `1:n` | `uni` | 资格判定规则对每个已发现的 `SkillDefinition` 执行过滤，决定其是否进入 eligible 集合。 |
| R7 | C2 | `enables_loading_from` | C4 | `1:n` | `uni` | `SkillLoadConfig` 通过 `includeBundled`、`includeManaged`、`includeWorkspace` 和 `extraDirs` 决定从哪些 `SkillSource` 加载技能。 |

### 关系条件

- R5：只有当 entry key 命中技能名或 `metadata.skillKey` 时该覆盖关系才生效。
- R6：仅当 `SkillsConfig.enabled` 为 true 时才会发生筛选流程。

---

### 约束与规则

### K1 来源优先级规则

- 作用对象：C3、C4、R3
- 规则：当发现同名技能时，后扫描到的高优先级来源会覆盖先扫描到的低优先级来源，实际优先级为 `Workspace > Plugin > Managed > Bundled > Extra`。
- 触发时机：扫描多个来源并聚合 `allSkills` 字典时。
- 禁止项：
  - 假设低优先级来源可以覆盖高优先级来源。
  - 忽略来源顺序对最终技能定义的影响。
- 严重级别：`high`

### K2 Bundled allowlist 仅作用于 bundled 技能

- 作用对象：C1、C3、C4、R3、R6
- 规则：`allowBundled` 过滤只在技能来源为 `Bundled` 且 allowlist 非空时触发；其他来源不受该过滤影响。
- 触发时机：进入 eligibility filtering 阶段时。
- 禁止项：
  - 把 `allowBundled` 误用于 managed、workspace 或 plugin 技能。
- 严重级别：`medium`

### K3 Entry disable 优先于 requirements gating

- 作用对象：C3、C5、C6、C7、R4、R5、R6
- 规则：如果某个 skill entry 命中且 `Enabled` 为 false，则该技能会在 requirements 检查前被跳过，即使 `metadata.always` 为 true 也不能绕过显式禁用。
- 触发时机：遍历 `allSkills` 并执行 per-skill entry disable 检查时。
- 禁止项：
  - 认为 `metadata.always` 可以覆盖显式的 entry 禁用。
- 严重级别：`high`

### K4 Always 只绕过 requirements gating

- 作用对象：C3、C5、C7、R4、R6
- 规则：当 `SkillMetadata.always` 为 true 时，技能跳过 requirements gating，但不会绕过系统总开关、`allowBundled` 或显式 entry disable。
- 触发时机：执行 requirements gating 判断时。
- 禁止项：
  - 把 `always` 理解为无条件进入 eligible 集合。
- 严重级别：`high`

---

### 术语映射

| 术语 | 候选概念 | 选定概念 | 说明 |
| --- | --- | --- | --- |
| `eligible skills` | C3, C7 | C3 | 在当前子域中它指的是通过资格判定后保留下来的 `SkillDefinition` 集合，而不是规则本身。 |
| `skills entries` | C1, C6 | C6 | 在本次切片里它更具体地指 entries 映射中的单个 `SkillEntryConfig`，而不是整个 `SkillsConfig`。 |
| `来源优先级` | C4, C7 | C4 | 该术语主要描述 `SkillSource` 层级顺序，规则层只是消费这套顺序。 |

---

### 冲突、歧义与不确定项

### 冲突

- Managed 技能目录路径说明：
  - 冲突来源：S1、S2
  - 当前处理：以 S2 的实际实现为准，即 managed 技能从用户主目录下的 `.openclaw/skills` 加载；S1 中相关示例表述视为说明性文本，不作为最终路径定义。
  - 状态：`resolved`

### 歧义

- 文档中对 managed 技能目录路径的说明与代码实现之间存在表述不一致，容易让使用者误判 managed 技能的实际位置。
  - 影响：会影响技能部署位置判断、排障路径和示例文档的准确性。
  - 状态：`resolved`
- `skills entries` 既可能指整个 entries 映射，也可能指单个 `SkillEntryConfig` 条目。
  - 影响：会影响配置文档、代码评审和 ontology 概念边界定义。
  - 状态：`open`

### 不确定项

- Plugin 来源路径语义的对外文档稳定定义
  - 缺失依据：当前样例主要依赖代码与分析文档，缺少更正式的用户向稳定说明。
  - 需要补充：如果团队已有插件技能来源的正式设计文档，可补充到 sources 以收紧该概念定义。

---

### 后续动作建议

- `P2` / `agent`：基于该切片补一份技能系统术语表，统一 eligible、entry、source、managed 等名词口径。
- `P2` / `user`：将来源优先级和 always 规则整理为面向用户的文档片段，减少实现与说明不一致。

---

### READY 详细解读

如果运行下面这条命令：

```powershell
..\..\scripts\validate-slice.py .\sample.json --review-mode
```

脚本会给出 `Heuristic verdict: READY`。对内置样例来说，这个结果首先是一个约定：`sample.json` 被当作 ready baseline，用于表达“什么叫结构和语义都基本过关”。

但它之所以适合承担这个 baseline，也确实能从当前内容里找到对应依据。

### 为什么这个样例适合作为 READY baseline

| 观察点 | 当前样例中的对应位置 | 为什么支持 READY |
| --- | --- | --- |
| 存在多个高信任度来源 | `sources[0].trust_level = high`，`sources[1].trust_level = high`，`sources[2].trust_level = high` | 关键概念、关系和规则都有高信任度文档或代码来源支撑，不是只靠说明性文本推断。 |
| 没有低信任度来源主导结论 | `sources` 中只有 `high` 和 `medium`，没有 `low` | 说明当前切片的核心判断没有明显依赖低可信解释层。 |
| 冲突已处理而不是悬置 | `conflicts[0].status = resolved` | 样例承认过冲突存在，但已经明确给出当前采用的处理方式。 |
| 主要歧义没有演化成黄灯阻塞项 | `ambiguities` 中一个是 `resolved`，一个是 `open`，但整体不影响当前子域的可工作边界 | 这表示样例并不是“没有任何歧义”，而是主要风险仍在可控范围内。 |
| 不确定项存在但不阻断当前落地目标 | `uncertainties[0]` 指向插件来源文档稳定性，而不是当前核心加载规则本身 | 说明剩余不确定性更多是“可继续补强”，而不是“当前切片无法用”。 |
| 概念、关系、约束都有明确落点 | `concepts`、`relations`、`constraints` 都完整且可追溯 | 这让它不仅结构合法，而且已经能支撑后续讨论、文档统一和规则校验。 |

### 为什么它是 READY 而不是 WARNING

和 `warning-sample.json` 相比，这个样例的差别不在于“完全没有未决问题”，而在于：

- 高信任度来源足够多
- 核心结论没有被低信任来源牵着走
- 冲突已经处理，而不是继续悬置
- 剩余歧义和不确定项没有直接削弱当前切片的主要用途

换句话说，`READY` 不表示“这份 slice 完美无缺”，而是表示“这份 slice 已经足够稳定，能作为当前团队的正向参考样例”。

### 脚本输出和人工评审怎么配合

推荐按下面顺序理解：

1. 先看 `Structure: PASS`，确认它通过了结构校验。
2. 再看 `Heuristic verdict: READY`，确认它没有命中当前快速风险信号，且被视为 ready baseline。
3. 最后回到本页和 `../../references/REVIEW_CHECKLIST.md`，确认它是否仍然贴合当前实现和当前团队口径。

所以这份样例最合适的定位是：

- 可通过 schema
- 可作为 READY baseline
- 适合作为正向参考样例
- 适合团队直接复制后按业务改写

---

### 元数据

- 生成时间：`2026-04-20T00:00:00Z`
- 生成者：`ontology-extraction`
- 工作区：`kingcrab`
- 备注：该样例选用 OpenClaw 技能加载与筛选子域，目标是提供一份贴近仓库真实结构且可直接复制修改的 ontology slice 示例。
