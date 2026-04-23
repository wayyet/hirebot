# Demo case: 售后工单分诊与升级

This reference is a standard demo case for the `digital-employee-discovery` skill.
Use it when you want to show stakeholders what a completed discovery output looks like under the NCrew framing.

The demo is intentionally business-facing.
It keeps the focus on:
- digital employee
- ontology slice
- skills
- CLI

It does not expand into full platform architecture.

---

# 数字员工场景发现简报：售后工单分诊与升级

## 1. 为什么需要这个数字员工
- 当前问题：
  企业每天接收大量售后工单，一线客服需要人工判断问题类型、优先级、是否升级，导致处理标准不一致，高风险问题可能漏判。
- 现在主要是谁在处理：
  一线客服团队
- 这个工作通常在什么情况下发生：
  客户通过邮件、在线表单、企业微信提交问题后，系统生成新工单，需要有人在第一时间完成理解、分诊、优先级判断和升级建议。
- 企业最想改善的结果：
  普通问题快速归类并获得建议处理方案；紧急和复杂问题被及时识别并升级；整体 SLA 更稳定。

## 2. 数字员工画像卡
- 名称：
  售后分诊专员
- 一句话角色：
  一个在售后入口值班的数字员工，负责先看懂工单，再决定这件事该怎么被处理。
- 使命：
  在工单进入流程后，完成理解、分诊、优先级判断、升级建议和转派建议。
- 它替代/辅助的是谁：
  主要辅助一线客服；在低风险、规则清晰的场景下，可替代部分人工分诊动作。
- 它最终对什么结果负责：
  对“工单是否被正确理解、正确分类、正确升级或转派”负责。
- 工作边界：
  负责分诊、建议、转派建议、升级建议、SLA 风险提醒。
- 人机分工边界：
  涉及退款、赔偿、法律风险、重大客户争议、生产故障的最终处理由人工确认。
- 自主等级：
  审批后执行；部分低风险内部转派可直接执行。

## 3. 它上岗后的工作方式（Day-1 work loop）
- 触发它开始工作的信号：
  新工单进入客服工单系统。
- 它拿到的第一批输入：
  工单正文、客户身份、来源渠道、附件、历史记录。
- 它会先做什么：
  先把客户描述转成结构化问题摘要，识别涉及产品、问题类型和关键风险信号。
- 然后做什么：
  再判断优先级，匹配知识库答案，决定是否升级以及应该转给哪个处理队列。
- 在什么情况下交给人：
  命中赔偿、法律风险、重大客户争议、生产中断、敏感数据等高风险条件时立即升级给人工。
- 最终交付物是什么：
  分诊结果、优先级、升级建议、转派建议、处理依据、SLA 风险提醒。

## 4. 它需要理解的业务世界（Ontology slice)
### 4.1 Entities
- 工单
- 客户
- 客户等级
- 产品
- 问题分类
- 优先级
- SLA
- 升级单
- 知识库文章
- 附件 / 日志文件

### 4.2 Actions
- 解析工单
- 提取关键信息
- 分类
- 打标签
- 判断优先级
- 检索知识库
- 生成处理建议
- 升级
- 转派
- 通知

### 4.3 Resources
- 客服工单系统
- CRM
- 知识库
- 企业微信
- 邮件系统
- 附件解析能力

### 4.4 Constraints
- VIP 客户优先级更高
- 生产中断默认高优先级
- 涉及退款 / 赔偿必须人工介入
- 涉及敏感数据禁止直接回传
- 超 SLA 必须通知主管
- 对外正式回复默认需要人工确认

## 5. 它具备的 Skills
| Skill | 它用这个 Skill 做什么 | 何时触发 | 关键输入 | 关键输出 | 自主等级 |
|------|--------------------|---------|---------|---------|---------|
| 工单理解 Skill | 把非结构化工单转成可判断的结构化问题表示 | 新工单进入 | 工单正文、附件、来源渠道 | 问题摘要、涉及产品、关键字段 | 自动 |
| 分类与优先级判断 Skill | 判断问题类型、严重程度和风险标签 | 完成工单理解后 | 问题摘要、客户等级、历史记录 | 分类标签、优先级、风险标签 | 自动 |
| 知识匹配 Skill | 为常见问题找到知识依据和建议处理方式 | 识别为可知识化处理的问题 | 问题摘要、产品、分类 | 候选知识文章、建议回复依据 | 自动 |
| 升级与转派 Skill | 决定是否升级，以及应该进入哪个队列 | 命中高优先级、复杂问题或规则约束 | 风险标签、优先级、客户等级、队列规则 | 升级建议、目标队列、原因说明 | 审批后执行 |
| SLA 监控与通知 Skill | 监控超时风险并提醒相关人员 | 工单进入处理中或待响应状态 | 工单状态、创建时间、优先级、SLA 规则 | 预警、提醒、主管通知 | 自动 |

## 6. 它工作的工具入口（CLI / 系统与数据对接）
| Skill | 系统/数据源 | 它要完成的动作 | CLI 或接口形态 | 认证/权限边界 | 备注 |
|------|------------|---------------|---------------|--------------|------|
| 工单理解 Skill | 工单系统 | 读取工单正文和基础字段 | `ticket:get(ticket_id) -> ticket_detail` | 数字员工只读工单内容 | 需包含附件元信息 |
| 工单理解 Skill | 工单系统 | 读取附件列表 | `ticket:get-attachments(ticket_id) -> attachments[]` | 只读附件列表 | 用于补充上下文 |
| 工单理解 Skill | 附件解析能力 | 提取日志、截图、PDF 中的文本或元信息 | `doc:extract(file_id) -> extracted_text_or_metadata` | 受限读取附件内容 | 支持日志、截图、PDF |
| 分类与优先级判断 Skill | CRM | 获取客户等级和历史信息 | `crm:get-account(account_id_or_email) -> account_profile` | 只读客户等级与历史信息 | 用于 VIP 判定 |
| 知识匹配 Skill | 知识库 | 搜索匹配的处理知识 | `kb:search(query, product, category) -> articles[]` | 只读知识内容 | 支持产品过滤 |
| 升级与转派 Skill | 工单系统 | 写回分诊结果和建议字段 | `ticket:update-triage(ticket_id, category, priority, risk_flags, suggested_queue) -> status` | 允许写回标签与建议字段 | 不直接发送最终客户答复 |
| 升级与转派 Skill | 工单系统 | 执行内部转派 | `ticket:route(ticket_id, queue, assignee?, reason) -> route_result` | 内部路由权限 | 高风险场景可要求审批 |
| SLA 监控与通知 Skill | 工单系统 | 搜索即将超时或已超时工单 | `ticket:list-sla-risk(window, queue) -> risky_tickets[]` | 只读队列状态 | 定时执行 |
| SLA 监控与通知 Skill | 企业微信 / 邮件 | 发送提醒和升级通知 | `notify:send(channel, recipients, template, payload) -> notification_result` | 仅内部通知权限 | 用于提醒客服和主管 |

## 7. 它如何与人协作
- 哪些情况它可以自己处理：
  普通、低风险、规则清晰的工单，它可以自动完成理解、分类、知识匹配和转派建议。
- 哪些情况必须升级给人：
  赔偿、法律风险、重大客户争议、生产中断、敏感数据等场景必须交给人工。
- 人会在什么节点确认：
  高风险升级前、特殊转派前、对外正式答复前。
- 它需要向谁同步结果：
  一线客服、客服主管、必要时同步二线技术支持。

## 8. 预期业务效果
- 效率提升：
  一线客服在工单入口的人工分诊负担显著下降。
- 质量提升：
  问题分类和优先级判断更一致。
- 响应速度：
  紧急问题识别更快，升级更及时。
- 风险/合规改善：
  VIP、生产中断、赔偿类问题更少漏判；敏感数据处理边界更清晰。
- 组织影响：
  一线客服从“逐单机械判断”转向“处理例外和高风险案例”。

## 9. 待确认问题
- 是否允许低风险标准问题自动转派而无需人工确认
- 是否允许生成建议回复草稿
- 附件解析是否必须支持 OCR、日志结构化、PDF 文本抽取
- SLA 规则是否因客户等级、产品线、问题类型而不同
- 工单系统里是否已有“升级原因”“风险标签”“推荐队列”等标准字段

---

## How to use this demo

Use this demo to explain the NCrew layering clearly:
- digital employee: 售后分诊专员
- ontology slice: the minimal business context it must understand
- skills: the reusable capability units it needs to perform the work
- CLI: the tool interfaces used to reach systems and data

If needed, present this demo before running a live discovery session.