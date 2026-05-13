# 评估专家 (Evaluation Expert) 技能包

这是一个运行在**评估沙箱**里的评估能力包，用于对**目标沙箱**中的数字员工做真实场景评估。

## 运行模型

技能包采用下面这条链路：

1. 平台创建**目标沙箱**，并把数字员工模板包加载进去。
2. 平台创建**评估沙箱**，并上传：
   - `evaluation-expert` 技能包
   - 数字员工模板包，或其中的评估材料（`testcases/`、`ontology/`）
   - 运行时上下文 `evaluation-context.json`
3. 评估沙箱先检查本地材料是否完整。
4. 若材料完整，评估沙箱在对话窗口中展示题卡。
5. 评估沙箱根据运行时上下文完成鉴权，与**目标沙箱**建立 WebSocket。
6. 评估沙箱逐题驱动**目标沙箱**执行测试用例，并采集 trace。
7. `evaluator` 根据 testcase、ontology、trace 做严格多维评分。
8. `report_generator` 生成报告。
9. 平台通过 `evaluation_report` / 后端接口把报告持久化到数据库。

## 职责边界

### 目标沙箱

- 加载被评估数字员工模板
- 真正执行业务逻辑
- 接收评估题目并返回执行证据

### 评估沙箱

- 持有 testcase / ontology / 模板副本
- 检查材料就绪状态
- 展示题卡
- 负责鉴权、建连、发题、采集 trace
- 负责评分、生成报告、触发改进建议

### 平台 / 后端

- 负责双沙箱 provisioning
- 负责把模板或评估材料上传到评估沙箱
- 负责写入运行时上下文
- 负责最终报告持久化到数据库

## 架构图

```text
┌─────────────────────────────────────────────────────────────┐
│                       评估沙箱                               │
│                                                             │
│  live_evaluation_coordinator                               │
│      ├─ inspect 本地材料                                    │
│      ├─ 展示题卡                                             │
│      ├─ 调用 live_evaluator/evaluate.py                     │
│      ├─ 调用 evaluator                                       │
│      ├─ 调用 report_generator                                │
│      └─ 调用 evaluation_report / 后端持久化接口             │
└─────────────────────────────────────────────────────────────┘
                            │
                            │ WebSocket / HTTP
                            ▼
┌─────────────────────────────────────────────────────────────┐
│                       目标沙箱                               │
│                                                             │
│  - 已加载数字员工模板                                        │
│  - 执行每一道测试题                                           │
│  - 返回消息、工具调用、思考块、状态变化等证据                │
└─────────────────────────────────────────────────────────────┘
```

## 目录结构

```text
evaluation-expert/
├── manifest.json
├── config/
│   ├── AGENTS.md
│   ├── IDENTITY.md
│   ├── MEMORY.md
│   ├── SOUL.md
│   └── workspace.json
├── ontology/
│   └── evaluation-baseline.md
├── skills/
│   ├── evaluation_orchestrator/
│   │   └── SKILL.md
│   ├── evaluator/
│   │   └── SKILL.md
│   ├── live_evaluation_coordinator/
│   │   └── SKILL.md
│   ├── live_evaluator/
│   │   ├── SKILL.md
│   │   ├── README.md
│   │   ├── evaluate.py
│   │   ├── auth_client.py
│   │   ├── material_loader.py
│   │   ├── ws_client.py
│   │   ├── http_client.py
│   │   ├── trace_builder.py
│   │   ├── runtime_context.example.json
│   │   └── test_cases/
│   ├── report_generator/
│   │   └── SKILL.md
│   ├── scenario_parser/
│   │   └── SKILL.md
│   ├── test_executor/
│   │   └── SKILL.md
│   └── training_advisor/
│       └── SKILL.md
├── README.md
└── test_evaluation_skill.py
```

## 主入口

推荐入口：

- [skills/live_evaluation_coordinator/SKILL.md](/E:/hirebot/back-end/src/HireBot.ApiService/Assets/DigitalEmployeeTemplates/evaluation-expert/skills/live_evaluation_coordinator/SKILL.md:1)

它代表“在评估沙箱里与用户交互并完成评估”的真实运行模式。

`evaluation_orchestrator` 作为更高层的自动编排定义，遵循同一套双沙箱模型。

## live_evaluator 输入契约

`live_evaluator/evaluate.py` 使用统一参数：

```bash
python evaluate.py \
  --runtime-context /workspace/runtime/evaluation-context.json \
  --mode inspect|execute \
  --output /tmp/output.json
```

## 运行时上下文

运行时上下文由平台写入评估沙箱，至少包含：

- `session`
- `materials`
- `target_sandbox`
- `execution`

材料优先级：

1. `materials.testcases_path` / `materials.ontology_path`
2. `materials.template_root`
3. `materials.template_package_zip`
4. `materials.workspace_root` 下的约定目录

## 典型流程

### 材料就绪场景

1. `live_evaluation_coordinator` 调用 `evaluate.py --mode inspect`
2. 展示题卡
3. 调用 `evaluate.py --mode execute`
4. 把输出交给 `evaluator`
5. 调用 `report_generator`
6. 持久化报告

### 材料缺失场景

1. `inspect` 返回 `materials_incomplete`
2. 协调器提示用户上传模板包或补充 testcase / ontology
3. 必要时调用 `scenario_parser` 生成 testcase
4. 重新执行 `inspect`

## 关键原则

1. **材料本地化**：testcase / ontology 在评估沙箱本地。
2. **执行远程化**：测试用例由目标沙箱执行，评估沙箱只负责驱动与采集。
3. **鉴权显式化**：账号密码换 token、client credentials、静态 token 都通过运行时上下文声明。
4. **评分证据化**：评分输入必须包含题卡、本体规则和真实 trace。
5. **持久化平台化**：技能包不直接写库，最终由平台/后端持久化。
