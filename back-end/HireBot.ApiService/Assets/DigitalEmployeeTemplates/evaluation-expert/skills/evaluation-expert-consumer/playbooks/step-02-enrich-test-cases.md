# STEP 2 — enrichTestCases（为策展用例绑定指标集，跳过已由 STEP 1.5 处理的合成用例）

**类型**：确定性（无 LLM）
**依据**：工作流合同 `S2` + K5
**输入**：
- `test-cases/<tc_id>.tc.json`（策展用例，由用户维护；STEP 2 的**主要**输入来源）
- `runs/<eval_id>/synthesized-cases/<tc_id>.tc.json`（STEP 1.5 合成，仅在 **无**对应 enriched 文件时才处理）
- `selected_metrics`（STEP 1.2 输出）
**输出**：`runs/<eval_id>/enriched-cases/<tc_id>.enriched.json`（通过 `runtime-schemas/enriched_test_case.schema.json` 验证）

> **STEP 1.5 快捷路径**：若 STEP 1.5 已成功运行（`selected_metrics` 可用），它会同时写出 `enriched-cases/*.enriched.json`。STEP 2 **不得**重复处理这些文件。仅当来自 `test-cases/*.tc.json` 的策展用例，或 `selected_metrics` 当时不可用而未完成 inline enrichment 的合成用例，才需要 STEP 2 介入。

> 文件扩展名必须是 `.enriched.json`（不是 `.json`）。STEP 2.5 通过 `glob("enriched-cases/*.enriched.json")` 发现文件；步骤 2e 写出时必须加 `.enriched` 中缀。

STEP 2 必须在 STEP 2.5（planRun）之前完成。STEP 2.5 会读取 `enriched-cases/` 中的每个文件；若有文件缺少 `input.opening_message`，STEP 3 的 driver 将以 `exit 2` 失败并报错：
```
enriched_test_case.input has neither opening_message nor (deprecated) user_message
```

## 执行流程

### 1. 收集待处理用例

```python
# 策展用例（永远需要 STEP 2 处理）
sources = glob("test-cases/*.tc.json")

# 合成用例：仅在 STEP 1.5 未写出对应 enriched 文件时才补处理
for tc_file in glob(f"runs/{eval_id}/synthesized-cases/*.tc.json"):
    tc_id = stem(tc_file)   # 去掉 .tc.json 后缀
    enriched_path = f"runs/{eval_id}/enriched-cases/{tc_id}.enriched.json"
    if not file_exists(enriched_path):
        sources.append(tc_file)   # STEP 1.5 未完成 inline enrichment，交由 STEP 2 补全
    # 否则跳过：STEP 1.5 已完成，不重复处理
```

> 若 `sources` 为空（全部合成用例都已由 STEP 1.5 处理，且无策展用例），STEP 2 无需写出任何文件，直接打印 `"STEP 2: all synthesized cases already enriched by STEP 1.5, skipping"` 并继续 STEP 2.5。

> **宽松的 applicable_roles/scenarios 策略**：如果 tc 文件缺少 `applicable_roles` 或 `applicable_scenarios`（老格式 tc 常见），使用 `["*"]` 作为通配符，并在 `enrichment.notes` 中注明 "applicable_roles inferred as wildcard — original tc lacks the field"。这样所有指标都会参与匹配，不会因字段缺失导致 `applicable_metrics` 为空而跳过用例。

### 2. 对每个源用例

**2a. 读取原始字段**

```
source_tc     ← json.load(source_file)
tc_id         ← source_tc.test_case_id
raw_input     ← source_tc.input  (object)
```

**2b. 解析 opening_message（REQUIRED — 不得跳过）**

按以下优先级顺序取值（取第一个非空的）：

```
opening_message ← raw_input.get("opening_message")
               or raw_input.get("user_message")                          # v1 兼容字段
               or source_tc.get("scenario",{}).get("messages",[{}])[0].get("content")  # 老格式 tc
               or None
```

> **老格式 tc**（如 `scenario.messages[{"role":"user","content":"..."}]`）在沙箱实际运行中最为常见。`scenario.messages[0].content` 是第一条用户消息，应映射为 `input.opening_message`。

如果上述全部为空/None：
- **若有 `input.context` 可推断**：记 warn，用 `input.context` 中最相关的描述性文本填充（不允许 LLM 合成），并在 `enrichment.notes` 中注明 "opening_message inferred from context"。
- **若无可用字段**：`fail_fast`，输出错误 `tc_id <X> has no opening_message/user_message/scenario.messages and cannot be enriched`，不写出文件。

**2c. 绑定指标集**

```
applicable_metrics = []
for metric in selected_metrics:
    if metric.applicable_roles 覆盖 source_tc.applicable_roles  (或含 "*")
    AND metric.applicable_scenarios 覆盖 source_tc.applicable_scenarios (或含 "*"):
        applicable_metrics.append({
            "metric_code": metric.metric_code,
            "binding_reason": "role_and_scenario_match"
        })
```

如果 `applicable_metrics` 为空 → 跳过该用例（warn，不阻断；不写出文件）。

**2d. 组装 enriched 文件**

```jsonc
{
  "test_case_id": "<tc_id>",
  "version": "<source_tc.version>",
  "display_name": "<source_tc.display_name 若存在>",
  "summary": "<source_tc.summary 若存在>",
  "applicable_roles": "<source_tc.applicable_roles>",
  "applicable_scenarios": "<source_tc.applicable_scenarios>",
  "difficulty": "<source_tc.difficulty 若存在>",
  "input": {
    "opening_message": "<resolved opening_message>",  // ← 必须，即使来源是 user_message
    "customer_persona": "<source_tc.input.customer_persona 若存在>",
    "initial_emotion": "<source_tc.input.initial_emotion 若存在>",
    "goal": "<source_tc.input.goal 若存在>",
    "context": "<source_tc.input.context 若存在>",
    "stop_conditions": "<source_tc.input.stop_conditions 若存在>"
    // 其他 input 字段透传
  },
  "expected_output": "<source_tc.expected_output>",
  "turn_budget": "<source_tc.turn_budget 若存在>",
  "applicable_metrics": [...],
  "enrichment": {
    "enriched_at": "<ISO timestamp>",
    "source": "always_runs_step_2",
    "added_metric_codes": [...],
    "notes": "<可选备注>"
  },
  "provenance": "<source_tc.provenance 若存在，原样透传>"
}
```

**关键规则**：
- `input.opening_message` 必须出现在 enriched 文件中，无论来源字段名是 `opening_message` 还是 `user_message`
- `input.user_message` **不得**出现在 enriched 文件中（已映射到 `opening_message`，避免歧义）
- 不得新增来源 tc 中不存在的字段（透明转换，不发明数据）

**2e. 验证并写出**

```
validated = jsonschema.validate(enriched, "runtime-schemas/enriched_test_case.schema.json")
write "runs/<eval_id>/enriched-cases/<tc_id>.enriched.json"    # 注意 .enriched.json 后缀
```

### 3. 完成检查

```
enriched_count = len(glob("runs/<eval_id>/enriched-cases/*.json"))
if enriched_count == 0:
    fail_fast("no enriched test cases produced; STEP 2.5 cannot proceed")
```

打印摘要：`enriched <N> test cases → runs/<eval_id>/enriched-cases/`

## 常见错误（来自实际运行日志）

| 错误 | 根因 | 解决 |
|---|---|---|
| `enriched_test_case.input has neither opening_message nor user_message` | enriched 文件的 `input` 里两个字段都没有 | 在步骤 2b 中确保映射 `user_message→opening_message` |
| `enrichment.schema.json` 验证失败：`required: ["opening_message"]` | enriched 文件里没写 `opening_message` | 步骤 2d 中 `input.opening_message` 是必填 |
| `applicable_metrics` 为空数组 | selected_metrics 与 tc 的 roles/scenarios 无交集 | 检查 metric 的 `applicable_roles` 是否覆盖 tc 的 `applicable_roles` |
