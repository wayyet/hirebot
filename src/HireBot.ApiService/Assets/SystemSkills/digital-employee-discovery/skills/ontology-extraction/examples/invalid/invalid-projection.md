# invalid-projection 评审说明

本文档对应 [invalid-projection.json](invalid-projection.json)。目标不是产出一个可用 projection，而是把 projection 侧最常见的一批结构错误集中到同一个文件里，方便团队验证 projection schema 的失败覆盖是否准确、是否足够可读。

## 样例定位

- 角色：`FAIL` projection 失败样例。
- 用途：演示什么叫“连 projection 结构层都没过”的下游映射文件。
- 适用场景：团队需要验证 projection schema、编辑器提示和失败路径处理时。

## 使用方式

```powershell
..\..\scripts\validate-projection.py .\invalid-projection.json --review-mode
```

上面的命令适用于当前目录位于 `ontology-extraction` 技能根目录内，调用的是支持 `--review-mode` 的真实校验器。

如果从仓库根目录执行：

```powershell
.\scripts\validate-ontology-projection.py .\src\OpenClaw.Gateway\skills\ontology-extraction\examples\invalid\invalid-projection.json
```

仓库根目录包装入口只承载普通结构校验，不暴露 `--review-mode`。

---

## 结构层状态

- 结构结果：`FAIL`
- 评审状态：`FAIL`
- 当前解读：这份 projection 连结构都不合法，不应进入任何下游消费讨论。

## 为什么这个 projection 是 FAIL

- 根对象存在非法额外字段。
- 常量、枚举、日期格式、布尔类型和非空字符串约束同时被破坏。
- 多处 ID 模式、数组类型和 `uniqueItems` 约束被故意打破。

## 推荐评审方式

1. 先确认 projection schema 是否覆盖了预设失败点。
2. 再确认报错路径和报错文案是否足够清晰可修复。
3. 不进入 projection 语义质量讨论，先把结构问题修完。

## 建议评审结论模板

- projection 合法性：未通过 schema 校验
- 评审状态：`FAIL`
- 当前结论：适合作为 projection 失败路径测试样例，不适合进入下游消费

## 预期错误方向

- `template_type` 不是 `ontology_projection`
- `projection_version` 不是 `1.0.0`
- `projection.projection_type` 不是允许枚举值
- `mapping_policy` 多个字段不满足允许值或类型约束
- `concept_mappings` / `relation_mappings` / `constraint_mappings` 的 ID、action、kind 都存在故意错误
- `prompt_projection.allowed_terms` 使用了错误类型
- `delivery_artifacts` 的 artifact 类型和 status 非法
- `meta.generated_at` 不是合法时间，`generated_by` 也不是固定值
- 根对象含有 `unexpected` 额外字段

## 最适合怎么用

- 作为 projection schema 的失败路径测试样例
- 作为编辑器或 CI 报错可读性检查样例
- 作为团队讲解“projection 也要分结构失败和语义失败”的反向基准
