# Digital Employee Discovery Ontology Slice

## Scope

该切片描述 HireBot 雇佣 Discovery 过程中的最小语义闭包：

- 参考模板（只读，作为基线附件输入）
- 工作模板（唯一可持续写入的目标包，工作区根目录即沙箱 working directory）
- 资料工单（来自 `material_handoff_summary` artifact 的 `items[]`，含 `source_path` 定位上传文件）
- 阶段完成信号（由各阶段 terminal artifact `isTerminal: true` 驱动，无独立状态机）

## Output Expectations

- 参考模板仅作为附件输入，不写入
- 工作模板作为唯一可持续写入的目标包；各 skill 写入路径遵守 `config/workspace.json` 约定：
  - 上传文件：`uploads/` （只读，解压 ZIP 后落此目录）
  - 本体切片：`ontology/`
  - 技能包：`skills/<skill-slug>/`
  - 外部配置：`external/`
- 技能、ontology、config 和外部配置都要围绕工作模板持续收敛
