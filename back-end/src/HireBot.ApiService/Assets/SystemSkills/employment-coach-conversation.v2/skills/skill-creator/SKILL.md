---
name: skill-creator
description: 为 HireBot 当前模板包创建、更新或重构 skill。用于需要新增 `skills/<skill-slug>/SKILL.md`、补充 `references/`、决定是否接入 `ontology-extraction` projection consumer、或把零散需求整理成可加载 skill 时。不要用于直接根据 todo 落业务 skill 内容，那属于 `skill-generation`。
metadata: {"openclaw":{"emoji":"🧩"}}
---

# Skill Creator

你负责把“想新增 / 改造一个 skill”的需求，落成这个仓库可直接发现、可直接维护的 skill 目录。

默认目标包是当前活跃的发现模板包：`../../`，也就是 `employment-coach-conversation.v2`。如果用户明确指定别的模板包或 skill 根目录，再切换目标位置。

## 核心原则

- 先判断 skill 类型，再决定目录结构，不要一上来就写正文。
- 优先复用现有模板、参考文件和脚本，不要重复发明结构。
- `description` 是触发入口，必须写清“做什么 + 什么时候用”。
- 长说明放到 `references/`，可重复执行的初始化或校验放到 `scripts/`。
- 不为当前模板包新增无用文档；避免 `README.md`、`CHANGELOG.md`、`QUICK_REFERENCE.md` 这类旁支文件。
- 不把 OpenAI `agents/openai.yaml` 原样照搬进来；这个仓库真正依赖的是 `skills/<skill-slug>/SKILL.md` 约定，以及模板包自己的发现逻辑。

## 先做分类

先读 [references/skill-types.md](references/skill-types.md)，把目标 skill 归到以下三类之一：

1. 标准业务 skill：只有 `SKILL.md`，最多再带 `references/` 或 `scripts/`。
2. Projection consumer skill：会直接消费 `ontology-extraction` 产出的 projection contract。
3. 系统辅助 skill：服务于阶段引导、诊断、配置、生成等仓库内工作流，本身可能带额外参考资料或脚本。

如果属于 projection consumer，继续读：

- `../ontology-extraction/templates/CONSUMER_SKILL_SCAFFOLD.md`
- `../ontology-extraction/templates/NEW_CONSUMER_SKILL_CHECKLIST.md`
- `../ontology-extraction/references/PROJECTION_CONSUMPTION_GUIDE.md`
- `../ontology-extraction/references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md`

如果只是普通 skill，不要把 Projection Contracts 章节硬塞进去。

## 工作流程

按顺序执行，除非你能明确说明为什么某步不适用。

### 1. 先把需求讲具体

优先从用户输入中提炼这些稳定事实：

- 这个 skill 解决什么问题
- 用户会说哪些话触发它
- 它真正负责什么交付物
- 它明确不负责什么
- 它是否依赖 projection contract、模板包现有文件、或上游阶段产物

如果用户给的是一句模糊需求，先补齐 2-3 个具体触发例子和边界，再开始写 skill。

### 2. 选位置，不乱放

本仓库默认把 skill 放到目标模板包的 `skills/<skill-slug>/` 下。

在当前活跃模板包里，新 skill 的默认路径是：

```text
../../skills/<skill-slug>/
  SKILL.md
  references/        # optional
  scripts/           # optional
  contracts/         # optional, only for projection consumer skills
```

在 `employment-coach-conversation.v2` 这类按约定扫描的模板包里，新增 `skills/<skill-slug>/SKILL.md` 就会被模板包发现；这里不需要单独给 skill 再写一个 `manifest.json`。

### 3. 先初始化骨架，再写内容

优先使用本 skill 自带脚本初始化，而不是手工从零起目录：

```text
python scripts/init_skill.py <skill-slug> --package-root ../../
python scripts/init_skill.py <skill-slug> --package-root ../../ --consumer
```

脚本会：

- 校验 skill 名是否合法
- 创建目标目录和 `SKILL.md`
- 普通 skill 使用本 skill 的基础模板
- consumer skill 复用 `ontology-extraction` 的 consumer scaffold

如果脚本不适用，再手工创建，但结构和模板仍要遵循本 skill 的参考文件。

### 4. 再写正文

普通 skill 先读 [references/skill-template.md](references/skill-template.md)。

写 `SKILL.md` 时遵守这些规则：

- frontmatter 里至少有 `name` 和 `description`
- `name` 与目录名一致，使用小写字母、数字、短横线
- `description` 必须包含可发现的触发语境，不要只写抽象名词
- 用祈使句或执行型语气写步骤
- 只保留当前 skill 真正负责的边界和交付物
- 不把会变化的路由评分、topic 选择细节、草稿判断逻辑写死在普通 skill 里

### 5. 按类型补资源

- 普通 skill：只有在长说明或规范明显超出正文时，才补 `references/`
- projection consumer skill：复用 `ontology-extraction` 模板与 layout guide，按需补 `contracts/projections/...`
- 系统辅助 skill：如果初始化、校验、转换逻辑会重复出现，补 `scripts/`

只有“会被反复执行”或“需要确定性”的逻辑才值得写脚本。

### 6. 写完先校验

先跑本 skill 自带快速校验：

```text
python scripts/quick_validate.py ../../skills/<skill-slug>
```

再对照 [references/quality-checklist.md](references/quality-checklist.md) 做人工复核。

如果是 projection consumer，再额外对照：

- `../ontology-extraction/templates/NEW_CONSUMER_SKILL_CHECKLIST.md`

## 必做检查

- 不留 `<...>` 或 `{{...}}` 占位符
- `description` 能让运行时看懂何时触发
- skill 名、目录名、frontmatter `name` 一致
- 边界写清楚，不把“通用助手”当 skill
- 不泄漏 token、密钥、密码、连接串
- 不因为想“以后可能用到”就提前塞无关 references 或脚本

## 不要做的事

- 不把这个 skill 当成 `skill-generation` 的替代品
- 不在普通 skill 里硬写 projection consumer 章节
- 不复制大段与当前 skill 无关的模板说明
- 不创建只有标题没有执行价值的 skill 空壳
- 不默认修改 `employment-coach-conversation` 主 skill 的调度文案，除非用户明确要求把新 skill 接进主流程

## References

- [references/skill-types.md](references/skill-types.md)
- [references/skill-template.md](references/skill-template.md)
- [references/quality-checklist.md](references/quality-checklist.md)
- `../ontology-extraction/templates/CONSUMER_SKILL_SCAFFOLD.md`
- `../ontology-extraction/templates/NEW_CONSUMER_SKILL_CHECKLIST.md`
- `../ontology-extraction/references/PROJECTION_CONSUMPTION_GUIDE.md`
- `../ontology-extraction/references/CONSUMER_PROJECTION_LAYOUT_GUIDE.md`
