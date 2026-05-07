# warning-projection 评审说明

本文档对应 [warning-projection.json](warning-projection.json)。它的目标不是提供一个可直接落地的高质量 projection，而是演示另一类更容易被忽略的情况：projection 结构完全合法、能够通过 schema，但它依然不应该被当成稳定的下游交付物直接使用。

这类样例的价值在于提醒团队区分两件事：

- `schema 通过` 说明 projection 结构没有损坏
- `值得接受` 还取决于来源质量、概念边界、映射精度和未决问题是否足够可控

## 样例定位

- 角色：`WARNING` projection 黄灯样例。
- 用途：演示什么叫“projection 合法，但仍需人工 review”。
- 适用场景：团队需要训练识别低信任来源、未决概念边界和宽泛映射时。

## 使用方式

```powershell
..\..\scripts\validate-projection.py .\warning-projection.json --review-mode
```

上面的命令适用于当前目录位于 `ontology-extraction` 技能根目录内，调用的是支持 `--review-mode` 的真实校验器。

如果从仓库根目录执行：

```powershell
.\scripts\validate-ontology-projection.py .\src\OpenClaw.Gateway\skills\ontology-extraction\examples\warning\warning-projection.json
```

仓库根目录包装入口只承载普通结构校验，不暴露 `--review-mode`。

---

## 结构层状态

- 结构结果：`PASS`
- 评审状态：`WARNING`
- 当前解读：该 projection 在结构上合法，但语义上仍然是草案级映射。

## 为什么这个 projection 是 WARNING

- 它绑定的是 [warning-sample.json](warning-sample.json)，而不是 READY slice。
- `mapping_policy` 明确使用了 `warn_and_continue` 和 `warn_on_unmapped_terms`，说明它是一个继续讨论而非收敛定稿的 projection。
- `open_questions` 非空，表明下游如何消费这份 projection 仍取决于团队澄清。
- `source_digest` 明确写出了“没有高信任度来源”和“冲突尚未完全收敛”两类 warning。

## 推荐评审方式

1. 先确认 warning 是来自 source slice 本身，还是来自 projection 决策。
2. 再确认这些 warning 是否会阻断下游 codegen 或 prompt orchestration。
3. 最后决定它应继续保留为讨论草案，还是补证据后收紧成 READY projection。

## 建议评审结论模板

- projection 合法性：已通过 schema 约束
- 评审状态：`WARNING`
- 来源与边界状态：仍偏草案，尚不够稳定
- 当前结论：适合作为黄灯 projection 讨论样例，不建议直接当作最终交付物。

## 详细原因

### 1. 它保留了 projection，但没有假装本体已经收敛

这份文件没有回避 `C1` 和 `C2` 的边界粗糙问题，而是把它们作为 prompt terms 投影出来，并明确标注了语义仍不稳定。这样做是流程上正确的，但也意味着它不应被误读为最终模型。

### 2. 它允许继续使用，但要求显式带 warning 前进

`unresolved_item_policy = warn_and_continue` 和 `prompt_assumption_policy = warn_on_unmapped_terms` 的组合，本质上就是在告诉团队：当前 projection 可以作为草案继续推进，但任何消费方都必须知道这是一份带风险的中间结果。

### 3. 它把 warning 直接写进 prompt 侧约束

`forbidden_assumptions` 和 `required_clarifications` 不是装饰字段，而是在提醒下游：不要把宽泛关系误读成完整语义，不要在关键边界尚未澄清前直接硬化成最终逻辑。

### 4. 它有 open questions，所以不能视为 READY

`open_questions` 明确指出团队仍未决定“agent capability”属于哪一层概念模型。这类问题如果不先解决，就很难把 projection 安全地推进到强约束代码生成。

## 最适合怎么用

- 作为黄灯 projection 讨论样例
- 作为 prompt orchestration 风险提示训练材料
- 作为团队演示“projection 通过 schema 也不等于可以直接定稿”的案例
