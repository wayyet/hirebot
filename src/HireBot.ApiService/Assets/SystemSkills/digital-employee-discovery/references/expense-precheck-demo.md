# Demo case: 财务报销预审

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

# 数字员工场景发现简报：财务报销预审

## 1. 为什么需要这个数字员工
- 当前问题：
  企业员工提交报销单后，财务共享中心或财务专员需要人工检查票据完整性、费用归类、预算占用、审批路径和政策合规性。由于规则多、单量大、资料不规范，导致处理慢、退单多、标准不一致。
- 现在主要是谁在处理：
  财务共享中心 / 财务审核专员
- 这个工作通常在什么情况下发生：
  员工在报销系统提交报销单、上传发票和附件后，需要有人在正式审批前完成材料检查、费用归类、制度校验和风险识别。
- 企业最想改善的结果：
  报销单在进入正式审批前被快速完成资料检查、费用分类、制度校验和风险识别，减少退单和人工反复沟通。

## 2. 数字员工画像卡
- 名称：
  报销预审专员
- 一句话角色：
  一个守在财务入口的数字员工，负责先看清这张报销单能不能顺利进入正式审批。
- 使命：
  在正式审批前完成材料完整性检查、费用归类、制度校验、风险提示和审批建议准备。
- 它替代/辅助的是谁：
  主要辅助财务共享中心和财务审核专员；在普通、规则清晰的单据上可替代部分人工初审动作。
- 它最终对什么结果负责：
  对“报销单是否被正确预审、异常是否被尽早识别、补件是否被及时触发”负责。
- 工作边界：
  负责预审、打标签、补件提示、审批路径建议、风险提示。
- 人机分工边界：
  涉及异常发票、超标报销、敏感费用、跨制度冲突或高金额例外时由财务人工确认。
- 自主等级：
  审批前自动预审；明确合规的普通单据可自动给出通过建议，异常单据只做提示不做最终裁决。

## 3. 它上岗后的工作方式（Day-1 work loop）
- 触发它开始工作的信号：
  新报销单进入报销系统。
- 它拿到的第一批输入：
  报销单字段、发票、附件、员工信息、费用类别、成本中心。
- 它会先做什么：
  先检查单据和附件是否完整，再提取票据信息并归类费用。
- 然后做什么：
  再对照制度校验是否合规，检查预算状态，并给出建议审批路径。
- 在什么情况下交给人：
  命中异常发票、超标报销、敏感费用、预算冲突、制度冲突等条件时交给财务人工确认。
- 最终交付物是什么：
  预审结果、风险标记、补件通知、建议审批路径。

## 4. 它需要理解的业务世界（Ontology slice）
### 4.1 Entities
- 报销单
- 费用项目
- 发票
- 附件
- 员工
- 部门
- 成本中心
- 预算
- 审批路径
- 费用制度条款
- 风险标记

### 4.2 Actions
- 读取报销单
- 提取票据信息
- 校验完整性
- 费用归类
- 检查制度合规
- 核验预算
- 建议审批路径
- 标记风险
- 通知补件

### 4.3 Resources
- 报销系统
- 发票/OCR 能力
- HR / 组织系统
- 预算 / ERP 系统
- 费用制度知识库
- 企业微信 / 邮件

### 4.4 Constraints
- 缺少必填票据或附件不得进入正式审批
- 超预算、超标准或制度冲突必须标记并转人工
- 敏感费用类型必须走指定审批路径
- 发票信息与报销单信息不一致时不得自动放行
- 涉及个人敏感信息时必须遵守最小披露原则

## 5. 它具备的 Skills
| Skill | 它用这个 Skill 做什么 | 何时触发 | 关键输入 | 关键输出 | 自主等级 |
|------|--------------------|---------|---------|---------|---------|
| 单据完整性检查 Skill | 检查报销单和附件是否齐全 | 新报销单提交 | 报销单字段、附件列表、票据类型 | 缺失项清单、是否可进入预审下一步 | 自动 |
| 票据识别与费用归类 Skill | 提取票据信息并映射费用类别 | 完整性检查通过后 | 发票图片/PDF、报销描述、费用标准 | 票据摘要、费用类别、金额明细 | 自动 |
| 制度合规校验 Skill | 对照费用制度检查是否合规 | 完成费用归类后 | 费用类别、金额、员工职级、出差类型、制度规则 | 合规结论、命中规则、异常原因 | 自动 |
| 预算与审批路径建议 Skill | 核验预算并建议审批流向 | 合规校验完成后 | 成本中心、预算余额、费用类别、金额阈值 | 预算状态、建议审批路径、风险标签 | 审批前自动建议 |
| 补件与风险通知 Skill | 生成补件提醒和风险提示 | 命中缺失材料或异常规则时 | 缺失项、风险原因、通知对象 | 补件通知、风险提示、待人工处理标记 | 自动 |

## 6. 它工作的工具入口（CLI / 系统与数据对接）
| Skill | 系统/数据源 | 它要完成的动作 | CLI 或接口形态 | 认证/权限边界 | 备注 |
|------|------------|---------------|---------------|--------------|------|
| 单据完整性检查 Skill | 报销系统 | 读取报销单字段和附件列表 | `expense:get-claim(claim_id) -> claim_detail` | 只读报销单内容 | 用于读取单据字段和附件列表 |
| 票据识别与费用归类 Skill | OCR / 发票识别能力 | 提取票据字段并转换成结构化信息 | `invoice:extract(file_id) -> invoice_fields` | 受限读取票据内容 | 支持图片、PDF、电子发票 |
| 制度合规校验 Skill | 费用制度知识库 | 搜索对应的报销规则与红线 | `policy:search-expense-rules(category, employee_level, trip_type) -> matched_rules[]` | 只读制度规则 | 用于命中报销标准和红线 |
| 预算与审批路径建议 Skill | ERP / 预算系统 | 获取预算余额和预算状态 | `budget:get-balance(cost_center, period, category) -> budget_status` | 只读预算信息 | 不直接改预算 |
| 预算与审批路径建议 Skill | HR / 组织系统 | 获取员工职级和组织信息 | `org:get-employee-profile(employee_id) -> employee_profile` | 只读组织与职级信息 | 用于审批路径建议 |
| 补件与风险通知 Skill | 报销系统 | 写回预审状态和风险标签 | `expense:update-precheck(claim_id, status, missing_items, risk_flags, suggested_route) -> update_result` | 允许写回预审状态和标签 | 不做最终审批动作 |
| 补件与风险通知 Skill | 企业微信 / 邮件 | 发送补件与风险通知 | `notify:send(channel, recipients, template, payload) -> notification_result` | 仅内部通知权限 | 用于提醒员工和财务 |

## 7. 它如何与人协作
- 哪些情况它可以自己处理：
  普通、规则清晰、资料完整的报销单，它可以自动完成预审建议。
- 哪些情况必须升级给人：
  异常发票、超标报销、敏感费用、预算冲突、制度冲突等场景。
- 人会在什么节点确认：
  高风险单据进入正式审批前、特殊审批路径确认前、异常规则命中后。
- 它需要向谁同步结果：
  员工本人、财务审核专员、必要时同步主管或成本中心负责人。

## 8. 预期业务效果
- 效率提升：
  财务初审工作量显著下降，普通报销单处理速度提升。
- 质量提升：
  报销资料检查、费用归类、制度匹配更一致。
- 响应速度：
  补件和异常更早暴露，减少来回沟通周期。
- 风险/合规改善：
  不合规、高风险、超标准单据更早被拦截。
- 组织影响：
  财务专员从机械初审转向处理例外、优化政策和处置高风险单据。

## 9. 待确认问题
- 是否允许对明确合规的普通报销单自动打上“预审通过”标记
- OCR 结果是否需要人工抽检比例
- 不同费用类型是否有不同审批路径覆盖规则
- 预算不足时是直接拦截还是允许带风险提示继续流转
- 海外票据、电子票据、特殊税务场景是否需要单独规则

---

## How to use this demo

Use this demo to explain the NCrew layering clearly:
- digital employee: 报销预审专员
- ontology slice: the minimal business context it must understand
- skills: the reusable capability units it needs to perform the work
- CLI: the tool interfaces used to reach systems and data

If needed, present this demo before running a live discovery session.