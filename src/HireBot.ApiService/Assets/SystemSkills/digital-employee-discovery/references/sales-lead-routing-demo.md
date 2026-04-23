# Demo case: 销售线索分诊与分配

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

# 数字员工场景发现简报：销售线索分诊与分配

## 1. 为什么需要这个数字员工
- 当前问题：
  企业每天从官网表单、活动名单、渠道伙伴和广告投放接收大量销售线索，销售运营需要人工检查字段完整性、识别重复线索、判断优先级并分配给合适的销售队列或销售代表，导致响应慢、分配不一致、优质线索可能被延误。
- 现在主要是谁在处理：
  销售运营团队 / SDR 团队
- 这个工作通常在什么情况下发生：
  新线索进入 CRM 或营销自动化系统时，需要有人立即完成校验、去重、评分和归属判断。
- 企业最想改善的结果：
  新线索被快速校验、优先排序、正确分配，高价值线索优先跟进，重复线索和冲突分配被及时拦截。

## 2. 数字员工画像卡
- 名称：
  线索分诊专员
- 一句话角色：
  一个守在销售入口的数字员工，负责先看懂这条 lead 值不值得追、该归谁管。
- 使命：
  在线索进入流程后完成校验、去重、优先级判断、归属建议和 SLA 跟进提醒。
- 它替代/辅助的是谁：
  主要辅助销售运营和 SDR 管理者；在规则明确的场景下可替代部分人工分配动作。
- 它最终对什么结果负责：
  对“线索是否被及时、正确、公平地分给合适的人”负责。
- 工作边界：
  负责校验、打分建议、归属建议、SLA 提醒、审计记录。
- 人机分工边界：
  涉及重大客户归属争议、跨区域冲突、战略客户或特殊政策覆盖的情况由人工确认。
- 自主等级：
  审批后执行；规则清晰的普通线索可直接执行分配。

## 3. 它上岗后的工作方式（Day-1 work loop）
- 触发它开始工作的信号：
  新 lead 进入 CRM 或营销自动化系统。
- 它拿到的第一批输入：
  lead 字段、来源渠道、联系人信息、公司信息、历史记录。
- 它会先做什么：
  先检查关键字段是否完整，判断是否存在重复联系人、重复公司或历史归属冲突。
- 然后做什么：
  再结合画像和行为信号评估优先级，最后决定应该分给哪个队列或哪个销售。
- 在什么情况下交给人：
  命中战略客户、跨区域冲突、owner continuity 冲突或特殊覆盖规则时交给人工。
- 最终交付物是什么：
  分配结果、优先级、归属原因、SLA 任务、审计记录。

## 4. 它需要理解的业务世界（Ontology slice）
### 4.1 Entities
- 销售线索
- 联系人
- 公司
- 线索来源
- 客户等级
- 销售区域
- 行业标签
- 归属队列
- 销售代表
- SLA 任务
- 重复线索记录

### 4.2 Actions
- 校验字段
- 检查重复
- 丰富画像
- 线索评分
- 判断归属
- 分配
- 创建 SLA
- 通知
- 记录审计日志

### 4.3 Resources
- CRM
- 营销自动化系统
- 客户数据增强服务
- 企业微信 / 邮件
- 路由规则库
- 审计日志系统

### 4.4 Constraints
- 缺少关键字段的线索不得直接分配
- 战略客户和现有客户续购线索优先保留归属连续性
- 跨区域归属冲突必须人工确认
- 评分模型只能使用已批准字段
- SLA 超时必须提醒主管或回收到公共池

## 5. 它具备的 Skills
| Skill | 它用这个 Skill 做什么 | 何时触发 | 关键输入 | 关键输出 | 自主等级 |
|------|--------------------|---------|---------|---------|---------|
| 线索校验 Skill | 检查这条线索是否具备可分配的基础信息 | 新线索进入 | 线索字段、来源渠道 | 缺失字段清单、是否可继续处理 | 自动 |
| 重复检测 Skill | 识别联系人、公司、历史机会的重复或冲突 | 完成字段校验后 | 邮箱、手机号、公司名称、CRM 历史记录 | 重复结果、冲突标记、保留归属建议 | 自动 |
| 线索评分 Skill | 评估线索质量与优先级 | 去重完成后 | 公司画像、来源、职位、行为信号 | 评分结果、优先级、原因说明 | 自动 |
| 归属分配 Skill | 决定线索进入哪个队列或分配给哪个销售 | 评分和规则就绪后 | 区域、行业、客户等级、当前负载、归属规则 | 目标队列/销售、分配原因 | 审批后执行 |
| SLA 与审计 Skill | 创建跟进时限并记录分配过程 | 分配完成后 | 分配结果、优先级、SLA 规则 | SLA 任务、提醒、审计日志 | 自动 |

## 6. 它工作的工具入口（CLI / 系统与数据对接）
| Skill | 系统/数据源 | 它要完成的动作 | CLI 或接口形态 | 认证/权限边界 | 备注 |
|------|------------|---------------|---------------|--------------|------|
| 线索校验 Skill | CRM | 读取 lead 字段和基础信息 | `crm:get-lead(lead_id) -> lead_detail` | 只读线索内容 | 用于字段完整性检查 |
| 重复检测 Skill | CRM | 搜索重复联系人和公司记录 | `crm:find-duplicates(email, phone, company) -> duplicate_candidates[]` | 只读历史记录 | 检查重复和 owner continuity |
| 线索评分 Skill | 数据增强服务 | 获取公司画像和补充信息 | `enrich:get-company-profile(domain_or_name) -> company_profile` | 受控外部数据访问 | 用于公司规模、行业等补充 |
| 线索评分 Skill | 营销自动化系统 | 获取行为热度信号 | `marketing:get-engagement(lead_id) -> engagement_signals` | 只读行为数据 | 用于计算优先级 |
| 归属分配 Skill | 路由规则库 / CRM | 读取归属规则并执行分配 | `routing:assign-lead(lead_id, territory, segment, owner?) -> assignment_result` | 受控分配权限 | 高冲突场景可要求审批 |
| SLA 与审计 Skill | CRM | 创建跟进任务 | `crm:create-sla-task(lead_id, owner, due_at) -> task_id` | 允许创建任务 | 跟进超时提醒 |
| SLA 与审计 Skill | 审计日志系统 | 写入分配决策路径 | `audit:log-routing(lead_id, decision_path, owner, score) -> log_id` | 只允许写审计记录 | 保存分配路径 |
| SLA 与审计 Skill | 企业微信 / 邮件 | 发送提醒和通知 | `notify:send(channel, recipients, template, payload) -> notification_result` | 仅内部通知权限 | 提醒 SDR / 主管 |

## 7. 它如何与人协作
- 哪些情况它可以自己处理：
  普通规则内、字段完整、无冲突的线索，它可以完成校验、去重、评分和自动分配。
- 哪些情况必须升级给人：
  战略客户、跨区域冲突、owner 争议、特殊覆盖规则命中时。
- 人会在什么节点确认：
  高冲突归属前、特殊客户分配前、例外路由决策前。
- 它需要向谁同步结果：
  销售运营、SDR 主管、必要时同步被分配的销售代表。

## 8. 预期业务效果
- 效率提升：
  新线索分配速度显著提升，减少人工逐条检查。
- 质量提升：
  去重、评分、归属规则执行更加一致。
- 响应速度：
  高价值线索更快进入正确销售队列。
- 风险/合规改善：
  重复线索和归属冲突更少，分配路径可审计。
- 组织影响：
  销售运营从机械分配转向处理例外和优化规则。

## 9. 待确认问题
- 是否允许普通线索在无人工确认下直接分配
- 战略客户、续购线索、渠道线索的优先级是否有特殊覆盖规则
- 路由规则是以区域优先还是行业优先
- 数据增强服务是否允许对外部数据源发起实时查询
- 线索分配后的 SLA 回收机制是否已有固定规则

---

## How to use this demo

Use this demo to explain the NCrew layering clearly:
- digital employee: 线索分诊专员
- ontology slice: the minimal business context it must understand
- skills: the reusable capability units it needs to perform the work
- CLI: the tool interfaces used to reach systems and data

If needed, present this demo before running a live discovery session.