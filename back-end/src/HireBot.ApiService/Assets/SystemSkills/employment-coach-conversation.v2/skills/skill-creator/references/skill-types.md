# HireBot Skill Types

先判断你要创建的是哪一类 skill，再决定结构和模板。

## 1. 标准业务 skill

适用场景：

- 处理一个清晰的用户任务或业务动作
- 主要交付物是回答、报告、提示词、文档、代码片段或流程建议
- 不直接消费 `ontology-extraction` projection contract

推荐结构：

```text
skills/<skill-slug>/
  SKILL.md
  references/    # optional
  scripts/       # optional
```

要点：

- 从本 skill 的 `references/skill-template.md` 开始
- `description` 里写清触发语境
- 不要带无关的 Projection Contracts 章节

## 2. Projection Consumer Skill

适用场景：

- 这个 skill 会直接读取 `contracts/projections/**/contract-index.json`
- 下游行为依赖 `ontology-extraction` 已经选好的语义边界
- 需要把 projection 当作术语、约束和 blocking rule 的权威来源

推荐结构：

```text
skills/<skill-slug>/
  SKILL.md
  contracts/
    projections/
      ontology-extraction/
        <domain-slug>/
          <domain-slug>.<projection-type-short>.projection.json
          README.md
          REVIEW.md
  references/    # optional
```

要点：

- 先读 `../ontology-extraction/templates/CONSUMER_SKILL_SCAFFOLD.md`
- 再读 `../ontology-extraction/templates/NEW_CONSUMER_SKILL_CHECKLIST.md`
- 如果 skill 并不直接消费 projection，就不要走这条路

## 3. 系统辅助 skill

适用场景：

- 服务于雇佣教练、诊断、外部配置、技能生成等仓库内工作流
- 主要职责是整理输入、生成工单、校验结构、驱动阶段动作，或给其他 skill 提供稳定支撑
- 可能需要内置模板、参考规范或脚本

推荐结构：

```text
skills/<skill-slug>/
  SKILL.md
  references/
  scripts/
```

要点：

- 只补当前系统流程真正需要的资料
- 如果逻辑会重复执行，优先写脚本而不是把步骤堆进正文
- 不要把运行时会变化的状态机细节复制到多个 skill 里

## 放置规则

当前活跃发现模板包默认路径：

```text
back-end/src/HireBot.ApiService/Assets/SystemSkills/employment-coach-conversation.v2
```

在这个包里：

- 新增 `skills/<skill-slug>/SKILL.md` 就会被约定式扫描发现
- 不需要给单个 skill 再补 `manifest.json`
- 如果用户明确要求放到别的模板包，再切换目标位置
