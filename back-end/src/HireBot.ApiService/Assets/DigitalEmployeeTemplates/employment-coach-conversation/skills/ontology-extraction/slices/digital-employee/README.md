# digital-employee ontology slice

这个目录用于承载“数字员工”主题的 producer-side ontology slice。

目录约定：

- `digital-employee.slice.json`：结构化 slice，供 schema 校验、projection 映射和后续自动消费使用。
- `digital-employee.slice.md`：人类可读说明，补充主题范围、来源边界、术语说明和后续动作。

推荐流程：

1. 先在 `digital-employee.slice.md` 中收敛主题、范围、来源和术语边界。
2. 再把稳定内容固化到 `digital-employee.slice.json`。
3. 校验结构时按所在层级选择入口：如果当前目录位于本 skill 内，使用 `..\..\scripts\validate-slice.py`；如果从仓库根目录执行，则使用仓库根目录 `scripts/validate-ontology-slice.py`。
4. 需要交付给 consumer skill 时，再从这份 slice 生成对应 view 的 `*.projection.json`。

当前这份骨架只提供数字员工主题的初始 producer 入口，不等同于最终 consumer projection。
