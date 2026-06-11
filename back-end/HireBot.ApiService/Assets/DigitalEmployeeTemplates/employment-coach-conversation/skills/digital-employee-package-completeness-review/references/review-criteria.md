# 完整性审查参考：判定规则与审查维度

本文件是 `digital-employee-package-completeness-review` skill 的渐进式披露参考文件。当沙箱网关通过 `use skill digital-employee-package-completeness-review` 加载本 skill 时，本文随之进入上下文，提供审查判定所需的关键参考信息。

---

## 审查结果等级

| 条件 | 结论 |
|---|---|
| 有任何 P0 阻断项 | FAIL / not-production-ready |
| 无 P0，但有较多 P1/P2 警告 | PASS_WITH_CONCERNS / beta-ready |
| 无 P0，警告已人工确认可接受 | PASS_WITH_CONCERNS（附接受说明） |
| 无 P0、无实质性警告、工作流与测试用例完整 | PASS / release-ready |

---

## 不可标记为 release-ready 的情况

- `manifest.json` 中的路径无法安装
- 必需的 `SKILL.md` 文件缺失
- Projection view 文件缺失
- 业务规则严重性冲突未解决
- 缺少对下游推送的人工确认边界
- 包声称覆盖了某些字段但没有字段定义，且核心任务依赖这些字段

---

## 常见自动化发现码

| 发现码 | 含义 | 常规修复方式 |
|---|---|---|
| `manifest.ontology.not_installable` | manifest 中的 ontology 文件存在但上传器会丢弃其扩展名 | 更新上传器规则或修改 ontology 文件扩展名 |
| `skill.metadata_projection_path.missing` | `metadata.json` 指向了过期的 projection 路径 | 指向 `skills/<skill>/contracts/projections/...` 或移除过期 metadata |
| `projection.view_path.missing` | `contract-index.json` 中的 view 路径指向不存在的文件 | 添加 projection 文件或修正 index 路径 |
| `projection.source_slice.unresolved` | projection 的 `source_slice.path` 无法解析 | 使用相对于包根的路径或正确的相对于 projection 的路径 |
| `ontology.field_count_without_schema` | 包声称覆盖了某些字段但没有字段定义 | 添加机器可读的字段目录/schema |
| `evaluation.stale_skill_binding` | evaluation 文档说没有技能绑定，但 manifest 声明了技能 | 更新 evaluation 文档 |
| `rule.<keyword>.severity_conflict` | SOUL.md（warning）和 ontology（block）之间对业务术语的严重性不一致 | 选择一个严重性等级并更新所有来源 |
| `security.secret_boundary.missing` | config 缺少禁止秘密的边界声明 | 添加明确的凭据/token 禁止声明 |

---

## 自动化校验覆盖范围

| 区域 | 自动检查项 |
|---|---|
| package root | 存在且为目录 |
| manifest | 存在、合法 JSON、身份字段、`entry_skill` 可解析到已有文件 |
| config | 声明的 config 文件和必需 config 白名单、可选 `workspace.json` |
| ontology | manifest 路径存在、扩展名可安装性、约定模式 .md/.json 风险、JSON 组件、`NOT_RUN`、无 schema 的字段计数 |
| skills | 声明路径、`SKILL.md`（回退到 `SKILL.zh.md` / `SKILL.en.md` / `SKILL.*.md`）、frontmatter、`metadata.json` |
| metadata | 过期的 `source_projection_paths` |
| projection contracts | `contract-index.json`、消费者匹配、默认 view、view 路径、JSON 解析、开放问题、`source_slice.path` |
| evaluation | evaluation 文件、过期的"no skills bound"文本（中英文） |
| workflow | 从 manifest `stage_rules` skill 名称或 `--expected-skills` CLI 标志推导的工作流闭环 |
| rules | SOUL.md 和 ontology 之间的严重性冲突（可通过 `config/rule-patterns.json` 配置） |
| security | 人工确认和秘密边界检测（扩展的关键词/模式匹配） |
| scoring | 10 维度评分和发布就绪状态 |

---

## 人工审查盲点（自动化无法判定）

1. **业务规则冲突**
   - 例如：SOUL 说"唛头差异"是 warning，ontology 说是阻断
   - 例如：testcases 允许 `0.01 KG` 毛重差异为 warning，而 ontology 要求完全相等

2. **字段计数声明**
   - 如果文档声称 102/30/62/32 个字段，需要机器可读的字段定义或可追溯的来源摘要
   - 不接受仅基于散文描述的字段计数

3. **工作流语义**
   - 一个 skill 的输入必须可被下一个 skill 消费
   - 人工审查必须把关不可逆操作（如下游推送）
   - 修正流程必须在推送前重新进入校验

4. **安全与权限边界**
   - 没有编造的字段值
   - 聊天、通知链接、日志、测试数据或报告中不出现秘密
   - 人工确认后才可下游推送
   - 审查、推送、重试、修正有审计日志

5. **Evaluation 相关性**
   - `evaluation.md` 必须与实际的 manifest 绑定技能匹配
   - 测试用例必须覆盖：正常路径、缺失数据、冲突、合规阻断、人工拒绝、重试、修正

---

## 输出报告必需章节

```markdown
# Digital Employee Package Completeness Review

## Verdict
## Automated Validator Result
## Package Surface
## P0 Blockers
## Skill Matrix
## Ontology and Projection Findings
## Workflow Closure
## Rule Consistency
## Evaluation Coverage
## Security and Authority Boundaries
## Score
## Recommended Fix Order
```
