# invalid-sample 评审说明

本文档对应 [invalid-sample.json](invalid-sample.json)，目标不是做人类可用的 ontology slice，而是把常见失败场景集中到一个文件里，方便团队验证校验器报错是否准确、是否足够可读。

## 样例定位

- 角色：`FAIL` 失败路径样例。
- 用途：演示什么叫“连结构层都没过”的 ontology slice。
- 适用场景：团队需要验证校验器错误覆盖、错误可读性和失败路径处理时。

## 使用方式

```powershell
..\..\scripts\validate-slice.py .\invalid-sample.json
```

上面的命令适用于当前目录位于 `ontology_extraction` 技能根目录内，调用的是技能目录下的真实校验器；如果需要启发式评审结论，可改用 `--review-mode`。

如果从仓库根目录执行：

```powershell
.\scripts\validate-ontology-slice.py .\src\OpenClaw.Gateway\skills\ontology_extraction\examples\invalid\invalid-sample.json
```

仓库根目录包装入口只承载普通结构校验，不暴露 `--review-mode`。

---

## 结构层表现

- 结构结果：`FAIL`
- 评审状态：`FAIL`
- 当前解读：这份样例连结构都不合法，不应进入后续语义评审。

## 对应为什么会得到 FAIL

- 根对象存在非法额外字段。
- 必填字段缺失、类型错误、枚举错误同时出现。
- 多处字段违反模式、格式、去重和最小值约束。

## 推荐评审方式

1. 先看错误列表是否覆盖了预设失败点。
2. 再看错误路径和文案是否足够清晰可修复。
3. 不进入语义质量讨论，先把结构问题修完。

## 建议评审结论模板

- 结构合法性：未通过 schema 校验
- 评审状态：`FAIL`
- 当前结论：适合作为失败路径测试样例，不适合进入下游消费

## 最适合怎么用

- 作为失败路径测试样例
- 作为校验器报错可读性样例
- 作为 CI 或 PR 校验前的反向基准

## 详细内容

### 样例目的

- 验证 schema 常量约束是否生效
- 验证必填字段缺失时是否有明确提示
- 验证枚举值错误时是否能列出允许值
- 验证类型错误是否能定位到具体字段
- 验证额外字段拦截是否生效
- 验证格式校验，例如 ISO-8601 日期格式
- 验证数组去重约束是否生效

---

### 预期错误清单

| 序号 | 错误位置 | 当前值 | 违反规则 | 预期报错 | 评审要点 |
| --- | --- | --- | --- | --- | --- |
| 1 | `schema_version` | `2.0.0` | 必须等于 schema 固定值 `1.0.0` | `must equal '1.0.0'` | 用于验证常量约束生效 |
| 2 | `slice_request.expected_output` | `concept_table`, `concept_table` | 数组要求 `uniqueItems=true` | `must contain unique items` | 用于验证重复项检测 |
| 3 | `sources[0].id` | `source-1` | 来源 ID 必须匹配 `^S[A-Za-z0-9_-]*$`，但当前校验器只会通过 `source_ids` 引用链间接暴露这个问题 | 不一定直接报错 | 用于提醒团队注意 ID 规范和直接校验覆盖范围 |
| 4 | `sources[0].source_type` | `doc` | 必须是 `document/code/config/schema/ontology/data` 之一 | `must be one of: document, code, config, schema, ontology, data` | 用于验证枚举错误提示 |
| 5 | `sources[0].priority` | `0` | 最小值为 `1` | `must be >= 1` | 用于验证数值下界校验 |
| 6 | `sources[0].trust_level` | `critical` | 必须是 `low/medium/high` 之一 | `must be one of: low, medium, high` | 用于验证来源信任等级口径 |
| 7 | `summary.one_line_conclusion` | 缺失 | `summary` 要求必填 | `missing required property 'one_line_conclusion'` | 用于验证缺字段错误 |
| 8 | `concepts[0].id` | `bad-id` | 概念 ID 必须匹配 `^C[A-Za-z0-9_-]*$` | `must match pattern ^C[A-Za-z0-9_-]*$` | 用于验证 ID 模式约束 |
| 9 | `concepts[0].aliases` | `not-an-array` | `aliases` 必须是数组 | `expected type 'array' but got 'string'` | 用于验证字段类型错误 |
| 10 | `concepts[0].kind` | `model` | 必须是 `entity/value_object/event/rule` 之一 | `must be one of: entity, value_object, event, rule` | 用于验证概念类型口径 |
| 11 | `concepts[0].key_properties[0].type` | `map` | 必须是允许的属性类型枚举之一 | `must be one of: string, number, integer, boolean, object, array, enum` | 用于验证嵌套枚举错误 |
| 12 | `next_actions[0].owner` | `reviewer` | 必须是 `agent/user/system` 之一 | `must be one of: agent, user, system` | 用于验证动作归属口径 |
| 13 | `next_actions[0].priority` | `P0` | 必须是 `P1/P2/P3` 之一 | `must be one of: P1, P2, P3` | 用于验证优先级口径 |
| 14 | `meta.generated_at` | `not-a-date-time` | 必须是合法 ISO-8601 时间 | `must be a valid ISO-8601 date-time` | 用于验证格式校验 |
| 15 | `meta.generated_by` | `someone-else` | 必须固定为 `ontology_extraction` | `must equal 'ontology_extraction'` | 用于验证生成器身份约束 |
| 16 | `meta.workspace` | 空字符串 | 字符串最小长度为 `1` | `must have length >= 1` | 用于验证非空字符串约束 |
| 17 | `unexpected` | `true` | 根对象禁止额外字段 | `property is not allowed` | 用于验证 `additionalProperties: false` |

---

### 已知设计点

### 为什么 `scope.include[0].id = bad-concept-id` 不一定直接报错

当前 schema 对 `scope.include[].id` 的约束是非空字符串，而不是按 `type` 进一步细分成 `C...`、`R...`、`K...` 模式。因此这个字段本身未必直接失败。

这不是 `invalid-sample.json` 的问题，而是当前 schema 刻意保持相对宽松的地方。如果后续希望这类字段也严格化，可以在 schema 中继续细分：

- `type = concept` 时要求 `id` 匹配 `^C...`
- `type = relation` 时要求 `id` 匹配 `^R...`
- `type = constraint` 时要求 `id` 匹配 `^K...`

### 为什么 `sources[0].id = source-1` 也可能不直接单独报错

同理，当前失败样例里更稳定可见的错误来自 `source_type`、`priority`、`trust_level` 等直接约束；而 `sources[0].id` 是否单独在当前输出里暴露，取决于该字段是否被后续引用链或模式检查覆盖到。

如果团队需要把它也变成稳定失败项，可以继续收紧 schema 或在校验器里增加跨字段引用一致性检查。

---

### FAIL 详细解读

如果运行下面这条命令：

```powershell
..\..\scripts\validate-slice.py .\invalid-sample.json --review-mode
```

脚本会给出 `Heuristic verdict: FAIL`。对这个样例来说，`FAIL` 不是一种“偏保守的黄灯判断”，而是一个非常直接的结论：结构层已经失败，因此不应该进入后续语义评审。

### 为什么这个样例会得到 FAIL

| 观察点 | 当前样例中的对应位置 | 为什么支持 FAIL |
| --- | --- | --- |
| 根对象存在非法额外字段 | `unexpected = true` | 根对象启用了 `additionalProperties: false`，因此这个字段会直接触发结构失败。 |
| 必填字段缺失 | `summary.one_line_conclusion` 缺失 | 这说明对象连最基本的必填结构都不完整。 |
| 常量值不合法 | `schema_version = 2.0.0` | schema 明确要求固定值 `1.0.0`。 |
| 枚举值不合法 | `sources[0].source_type = doc`，`sources[0].trust_level = critical`，`concepts[0].kind = model`，`next_actions[0].owner = reviewer`，`next_actions[0].priority = P0` | 多个字段都超出允许枚举范围，说明不是单点疏漏，而是结构口径整体失真。 |
| 类型不合法 | `concepts[0].aliases = not-an-array` | 该字段应为数组，却给成字符串。 |
| 模式不合法 | `concepts[0].id = bad-id` | 概念 ID 不符合 `^C[A-Za-z0-9_-]*$`。 |
| 数值下界不合法 | `sources[0].priority = 0` | schema 要求最小值为 `1`。 |
| 格式不合法 | `meta.generated_at = not-a-date-time` | 不符合合法 ISO-8601 时间格式。 |
| 去重约束不合法 | `slice_request.expected_output` 包含重复 `concept_table` | 违反 `uniqueItems = true`。 |

### 为什么它是 FAIL 而不是 WARNING

和 `warning-sample.json` 的本质区别在于：

- `warning-sample.json` 的结构是合法的，只是语义上风险较高。
- `invalid-sample.json` 连结构都不合法，校验器已经无法把它视为一个稳定 slice。

因此它不需要先讨论“来源够不够强”“关系够不够精确”这类语义问题，因为更基础的问题还没过：

- 字段是否齐全
- 类型是否正确
- 枚举是否合规
- ID 和格式是否满足 schema

在这些问题修好之前，任何后续语义讨论都容易失焦。

### 脚本输出和人工评审怎么配合

推荐按下面顺序理解：

1. 先看 `[FAIL]` 和错误列表，确认结构失败发生在哪些字段。
2. 再看 `Heuristic verdict: FAIL`，确认当前阶段不应进入人工语义评审。
3. 最后回到本页上面的错误清单，逐项判断报错是否覆盖预设失败点、是否清晰可修复。

所以这份样例最合适的定位是：

- 不通过 schema
- 会触发 FAIL
- 适合作为失败路径测试样例
- 不适合进入下游消费或正式评审结论阶段

---

### 详细评审结论模板

评审 `invalid-sample.json` 时，可以直接按下面口径记录结论：

- 报错条目是否覆盖了预设失败点
- 报错路径是否足够精确到字段级别
- 报错文案是否能直接指导修复
- 是否存在应该失败但未失败的字段
- 是否存在报错过多、难以理解或缺少上下文的问题

建议评审结果至少回答两个问题：

1. 团队成员第一次看到这些报错，能不能知道该改哪里。
2. 这些报错是否足够支撑后续接入 CI 或 PR 校验。
