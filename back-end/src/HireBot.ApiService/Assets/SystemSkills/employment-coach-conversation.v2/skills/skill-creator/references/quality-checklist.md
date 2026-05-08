# Skill Creator Quality Checklist

在提交新 skill 前，逐项检查。

## 结构

- [ ] 目标目录位于正确模板包的 `skills/<skill-slug>/`
- [ ] `SKILL.md` 存在
- [ ] 只有真正需要的 `references/`、`scripts/`、`contracts/` 被创建
- [ ] 没有额外新增 `README.md`、`CHANGELOG.md`、`QUICK_REFERENCE.md`

## Frontmatter

- [ ] `name` 存在且与目录名一致
- [ ] `description` 非空，且同时包含“做什么 + 何时触发”
- [ ] `name` 只包含小写字母、数字、短横线

## 正文

- [ ] 开头就说清 skill 的核心职责
- [ ] 处理流程是执行型语气，不是空泛介绍
- [ ] 明确写了边界和不做事项
- [ ] 没有把“通用助手”伪装成 skill

## Projection Consumer

- [ ] 只有在直接消费 `ontology-extraction` projection contract 时，才保留 Projection Contracts 章节
- [ ] 如果是 consumer skill，已参考 `../ontology-extraction/templates/NEW_CONSUMER_SKILL_CHECKLIST.md`
- [ ] 如果不是 consumer skill，已删除 projection 相关占位内容

## 安全与可维护性

- [ ] 不含明文 token、密钥、密码、连接串、凭据
- [ ] 不含 `<...>` 或 `{{...}}` 占位符
- [ ] 长说明已拆到 `references/`，正文不过度膨胀
- [ ] 只有会重复执行或需要确定性的动作才写进 `scripts/`

## 仓库适配

- [ ] 没有照搬 OpenAI `agents/openai.yaml`
- [ ] 结构符合当前仓库的 `skills/<skill-slug>/SKILL.md` 习惯
- [ ] 如果用户要求接入主流程，已单独评估是否需要同步修改上游 skill 或调度说明
