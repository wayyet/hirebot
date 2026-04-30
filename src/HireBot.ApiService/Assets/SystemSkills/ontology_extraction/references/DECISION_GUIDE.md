# ontology_extraction 红黄绿决策指南

本文档把 `ontology_extraction` 的适用性矩阵压缩成一张红黄绿决策图，方便团队在真正开始切片前，先判断当前任务到底适不适合使用这套规范。

适用目标：

- 快速判断当前任务是否应该进入 ontology slice 流程
- 区分当前产物应当是 `READY` 候选、`WARNING` 草案，还是暂时不该用本规范
- 区分“受控语义切片”任务与“全量本体建模 / formal ontology”任务

---

## 红黄绿含义

- `绿灯`：适合按本规范继续做 slice / projection / review。
- `黄灯`：可以用，但只能作为受控草案、局部治理视图，或需要补证据后再推进。
- `红灯`：当前不适合，应先补主题、补来源，或改用其他 ontology / knowledge modeling 方法。

---

## 红黄绿决策图

```mermaid
flowchart TD
  A[开始: 准备使用 ontology_extraction] --> B{任务主题是否明确?}
  B -->|否| R1[红灯: 不适用\n先做探索研究或定义主题]
  B -->|是| C{目标是否是\n当前任务的最小相关子图?}
  C -->|否, 目标是全量 ontology / 知识图谱| R2[红灯: 不适用\n先拆成多个子域 slice]
  C -->|是| D{是否有可追溯来源?}
  D -->|否| R3[红灯: 不适用\n先补事实源, 不要补造本体]
  D -->|是| E{是否至少有一个高信任来源?}
  E -->|否| Y1[黄灯: 有条件可用\n只能作为 review 草案]
  E -->|是| F{来源之间是否存在未解决冲突/歧义/不确定项?}
  F -->|是| Y2[黄灯: 有条件可用\n记录 conflicts / ambiguities / uncertainties]
  F -->|否| G{是否需要结构化交付\n给评审、codegen、prompt 或 workflow?}
  G -->|否, 只是泛泛介绍或讨论| Y3[黄灯: 可用性有限\n可做人读版, 但不必强行套完整规范]
  G -->|是| H{是否接受\n人工 review 和 READY/WARNING/FAIL 治理?}
  H -->|否| R4[红灯: 不适用\n该框架不适合跳过人工评审直入生产]
  H -->|是| I{是否需要 OWL/RDF 级\n形式化推理或开放元模型?}
  I -->|是| Y4[黄灯: 部分适用\n可作治理视图, 不能替代正式本体语言]
  I -->|否| G1[绿灯: 适用\n按 slice -> validate -> projection -> review 落地]

  Y1 --> J{能否接受先产出 WARNING 草案\n后续补证据?}
  J -->|能| G2[绿灯: 可进入草案流程\n但不能直接定稿]
  J -->|不能| R5[红灯: 当前不适用\n先补高信任来源]

  Y2 --> K{这些未决项是否阻断下游消费?}
  K -->|是| R6[红灯: 当前不适用\n先收敛边界和冲突]
  K -->|否| G3[绿灯: 可作为受控 WARNING 产物推进]

  Y3 --> L{后续是否会进入代码生成或 prompt 编排?}
  L -->|会| G4[绿灯: 先补结构化字段\n再进入正式流程]
  L -->|不会| G5[绿灯: 仅用 TEMPLATE.md 做人工梳理即可]

  Y4 --> M{是否只是想保留\n概念/关系/约束治理视图?}
  M -->|是| G6[绿灯: 作为上层治理视图可用]
  M -->|否, 想直接承载全部 formal semantics| R7[红灯: 不适用\n应改用正式 ontology 表达体系]

  classDef green fill:#d9f7be,stroke:#389e0d,color:#135200,stroke-width:2px;
  classDef yellow fill:#fff1b8,stroke:#d48806,color:#613400,stroke-width:2px;
  classDef red fill:#ffccc7,stroke:#cf1322,color:#820014,stroke-width:2px;

  class G1,G2,G3,G4,G5,G6 green;
  class Y1,Y2,Y3,Y4 yellow;
  class R1,R2,R3,R4,R5,R6,R7 red;
```

---

## 快速判定口径

- 看到 `绿灯`：说明当前场景符合本规范的核心前提，可以继续按 `slice -> validate -> projection -> review` 流程推进。
- 看到 `黄灯`：说明结构化切片仍然有价值，但当前更适合作为 `WARNING` 草案、局部治理视图，或等待补证据后再定稿。
- 看到 `红灯`：说明当前问题不该直接交给 `ontology_extraction`；先缩范围、补事实源，或改用更适合的 ontology / knowledge modeling 方法。

## 这张图最适合回答的问题

- 当前任务到底该不该做 ontology slice？
- 当前阶段能不能直接用本规范沉淀结果？
- 这次产出应当是 `READY` 候选、`WARNING` 草案，还是先不要做？
- 当前需求更像“受控语义切片”，还是“全量本体建模 / formal ontology”任务？

## 推荐用法

1. 先用这张图判断当前任务是红灯、黄灯还是绿灯。
2. 如果是绿灯，再回到 [../README.md](../README.md) 继续看模板、样例、校验脚本和评审流程。
3. 如果是黄灯，优先补 `sources`、`conflicts`、`ambiguities` 和 `uncertainties`，不要直接往下游硬推。
4. 如果是红灯，先换问题定义方式，再决定是否回到本规范。

## 相关入口

- [../README.md](../README.md)：规范包总览与使用路径
- [./FIELD_GUIDE.md](./FIELD_GUIDE.md)：字段语义和填报口径
- [./REVIEW_CHECKLIST.md](./REVIEW_CHECKLIST.md)：slice / projection 统一评审标准
- [./DOWNSTREAM_MAPPING_GUIDE.md](./DOWNSTREAM_MAPPING_GUIDE.md)：下游映射规则
