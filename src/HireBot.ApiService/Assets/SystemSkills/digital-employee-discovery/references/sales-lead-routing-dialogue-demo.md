# Dialogue demo: 销售线索分诊与分配

This reference shows what the `digital-employee-discovery` skill should sound like in a live bot-style conversation.
Unlike the other demo files, this one preserves the turn-by-turn dialogue.

Use it when you want to demonstrate:
- the pacing of the bot
- how the bot pulls the user back to a real case instead of staying abstract
- how the bot identifies tension, judgment difficulty, and human role differences
- how the bot keeps the conversation anchored to the four NCrew layers
- how the final brief is assembled from the dialogue

---

## Dialogue transcript

### Round 0

Bot:
我们先只聚焦一个场景。
这一轮我先锁定场景和表达框架。

1. 你现在最想梳理的是哪个业务场景？
2. 这个场景如果理清楚了，谁会最先受益？
3. 我们这次按 NCrew 的四层来收敛：数字员工、Ontology slice、Skills、CLI，可以吗？

User:
我们想梳理销售线索进入之后，怎么快速判断价值、有没有重复、该分给谁。
最先受益的是销售运营和 SDR 团队。
可以，按 NCrew 的四层来。

Bot:
我先收一下这一轮：
- 场景：销售线索分诊与分配
- 直接受益方：销售运营、SDR 团队
- 收敛框架：数字员工、Ontology slice、Skills、CLI

如果这些理解没问题，我进入下一轮。

### Round 1

Bot:
这一轮我先不谈方案，我先抓一个最近真实发生的例子。

1. 最近一次这个场景真实发生是什么时候？
2. 当时是谁接手的？第一步做了什么？
3. 哪一步最麻烦、最容易卡住，或者最容易判断不一致？
4. 一旦这里做错或做慢，后果是什么？

User:
就昨天。我们有一条官网进来的 lead，公司规模挺大，标题看起来也很像高意向。
是销售运营同学先接手的，第一步先看字段全不全，再去 CRM 里查有没有重复。
最麻烦的是它其实已经在系统里有一个旧联系人，但这次进来的是另一个邮箱，背后还是同一家公司。一个同事觉得应该直接分给华东大客户销售，另一个同事觉得先给 SDR 池。
如果分错了，轻一点是内部扯皮，重一点就是高价值 lead 响应变慢，甚至客户觉得我们内部很乱。

Bot:
这个例子很好，我先不急着往下走，我想把这个张力点挖透。

1. 当时为什么两个同事会判断不一样？
2. 在你看来，一个做得好的老销售运营，看到这条 lead 时，会比普通人多注意到什么？

User:
差异主要在于有没有把“公司重复”和“联系人重复”分开看。
普通同事只会看邮箱或手机号是不是重复，老同事会顺着公司名、历史机会、客户等级、以前的 owner 一起看。
而且老同事会先判断这是不是 strategic account，或者是不是已有机会在推进。如果是，就不会随便分到 SDR 池。

Bot:
这段很关键，我先收一下：
- 张力点不只是“查重”，而是“如何判断重复的层级”
- 普通人更容易停留在联系人重复
- 老同事会额外看：
  - 公司层面的重复
  - 历史机会
  - 客户等级
  - owner continuity
- 一旦这里判断错：
  - 高价值线索可能被延误
  - 内部归属冲突会放大
  - 客户体验会变差

如果这里没偏，我就开始定义：到底应该让什么样的数字员工接这份工作。

### Round 2

Bot:
这一轮我先把这个数字员工本人定义清楚，让它像一个可信的同事。

1. 如果把这件事交给一个数字员工，它最像团队里的哪一类员工？
2. 一个优秀的人做这件事时，最关键的判断力体现在哪里？
3. 哪些动作它能自主做到什么程度？哪些必须保留给人？
4. 它最直接的交付结果是什么？最不能犯的错误又是什么？

User:
它最像一个很成熟的销售运营分诊专员，不是纯录入的人，而是懂业务、懂归属逻辑的人。
最关键的判断力就是：它能不能看出这条 lead 表面上是新来的，实际上是不是应该延续原来的客户归属。
普通规则内的 lead，它可以自动完成校验、去重、评分和分配。
但如果碰到 strategic account、跨区域冲突、owner 有争议，还是要人工确认。
它的交付结果应该是分配结果、原因说明、SLA 任务。
它最不能犯的错误就是把高价值 lead 分错，或者把应该延续的客户关系打断。

Bot:
我先把这个数字员工讲活：
- 推荐数字员工：线索分诊专员
- 它像团队里的角色：成熟的销售运营分诊专员
- 它不是机械分配员，而是懂归属逻辑和风险边界的人
- 它最擅长的判断：
  - 识别表面新 lead 背后的真实客户归属
  - 区分联系人重复和公司/机会层面的重复
- 它最不能犯的错误：
  - 把高价值 lead 分错
  - 打断应该延续的 owner continuity
- 自主边界：
  - 普通规则内可自动处理
  - strategic account、跨区域冲突、owner 争议要人工确认
- 最终交付：
  - 分配结果
  - 原因说明
  - SLA 任务

如果这些理解没问题，我下一轮开始描述它上岗后的工作方式。

### Round 3

Bot:
这一轮我先不拆知识结构，先描述它上岗后的工作方式，让这个数字员工真的像在上班。

1. 什么信号会触发它开始工作？
2. 它一上岗先拿到什么输入？
3. 它第一步会做什么？第二步会做什么？
4. 什么情况下它会把线索交给人？

User:
新 lead 进入 CRM 或营销自动化系统，就是它开始工作的信号。
它会先拿到 lead 字段、来源、联系人信息、公司信息，有时候还有行为分数。
第一步先看字段是否完整，再检查是不是重复；第二步做评分和归属判断。
如果命中 strategic account、跨区域冲突、owner continuity 冲突或者规则互相打架，就交给人。

Bot:
我收一下它的 Day-1 work loop：
- 开工信号：新 lead 进入 CRM 或营销系统
- 第一批输入：lead 字段、来源、联系人信息、公司信息、行为信号
- 第一步：检查字段完整性、确认是否可继续处理
- 第二步：识别重复、做评分、判断归属
- 转人工条件：strategic account、跨区域冲突、owner continuity 冲突、规则冲突
- 最终交付：分配结果、原因说明、SLA 任务

如果这里没偏，我再定义它需要理解的业务世界，也就是 Ontology slice。

### Round 4

Bot:
这一轮我只定义它完成这个场景所需的最小业务世界，而且我会用刚才那个真实案例来落地。

1. 在那个真实案例里，它必须理解哪些业务对象？
2. 它要执行哪些动作？
3. 它依赖哪些系统、知识源、沟通渠道？
4. 它必须遵守哪些规则或红线？

User:
业务对象有 lead、contact、account、source、territory、industry、owner、queue、score、duplicate record、active opportunity、SLA task。
动作有字段校验、去重、补充画像、评分、判断归属、分配、创建 SLA、通知、写审计日志。
系统有 CRM、营销自动化系统、数据增强服务、企业微信、邮件、路由规则库。
规则包括：字段不完整不能直接分配；战略客户优先保持 owner continuity；跨区域冲突必须人工确认；评分模型只能用批准字段；超时要提醒主管。

Bot:
我收一下 Ontology slice：

Entities
- lead
- contact
- account
- source
- territory
- industry
- owner
- queue
- score
- duplicate record
- active opportunity
- SLA task

Actions
- 字段校验
- 去重
- 画像补充
- 评分
- 归属判断
- 分配
- 创建 SLA
- 通知
- 写审计日志

Resources
- CRM
- 营销自动化系统
- 数据增强服务
- 企业微信 / 邮件
- 路由规则库

Constraints
- 字段不完整不能直接分配
- 战略客户优先保持 owner continuity
- 跨区域冲突必须人工确认
- 评分模型只能使用批准字段
- 超时必须提醒主管

如果这里没偏，我开始拆它会调用哪些 Skills。

### Round 5

Bot:
现在不谈平台模块，我只拆这个数字员工在工作时会调用哪些 Skills。

1. 它需要哪些 Skills 才能像那个老同事一样工作？
2. 这些 Skills 分别负责哪一段判断？
3. 哪些 Skills 最值得在相邻场景复用？

User:
我觉得至少有五个：lead 校验、重复检测、lead 评分、归属分配、SLA 和审计。
其中重复检测和归属分配最关键，因为这两个地方最考验判断。
前四个都应该能复用到其他 lead routing 场景。

Bot:
我先把这层结构化一下：

1. 线索校验 Skill
- 它用这个 skill 判断这条 lead 能不能进入后续处理
- 输出缺失字段清单、是否可继续处理

2. 重复检测 Skill
- 它用这个 skill 判断重复是联系人层、公司层，还是机会层
- 输出重复结果、冲突标记、owner continuity 建议

3. 线索评分 Skill
- 它用这个 skill 判断这条 lead 值不值得优先跟进
- 输出评分结果、优先级、原因说明

4. 归属分配 Skill
- 它用这个 skill 决定应该分给谁，以及为什么
- 输出目标队列/owner、分配原因、风险标记

5. SLA 与审计 Skill
- 它用这个 skill 保证后续不被漏跟、可追踪
- 输出 SLA 任务、提醒、审计日志

我收一下：
- 所需 Skills：5 个
- 最关键的两个 Skills：
  - 重复检测 Skill
  - 归属分配 Skill
- 这两个 skills 承载了最核心的“老同事判断力”
- 前四个 Skills 复用价值最高

如果这些理解没问题，我进入最后一轮，确认它工作的工具入口。

### Round 6

Bot:
最后一轮我只确认工具层，也就是它为了工作要接哪些系统、走哪些 CLI。

1. 哪些 Skill 要访问系统或外部数据？
2. 对这些访问，理想的 CLI 或接口调用长什么样？
3. 哪些地方是只读，哪些地方允许它写回？
4. 哪些节点需要人确认或被通知？

User:
线索校验和重复检测主要读 CRM。
评分要读数据增强服务和营销行为数据。
归属分配要读规则，也要写回 CRM。
SLA 和审计要创建任务、发通知、写日志。
高冲突归属前还是要人工确认。

Bot:
我把工具入口先收敛成接口层，你确认：

- crm:get-lead(lead_id) -> lead_detail
- crm:find-duplicates(email, phone, company) -> duplicate_candidates[]
- enrich:get-company-profile(domain_or_name) -> company_profile
- marketing:get-engagement(lead_id) -> engagement_signals
- routing:assign-lead(lead_id, territory, segment, owner?) -> assignment_result
- crm:create-sla-task(lead_id, owner, due_at) -> task_id
- audit:log-routing(lead_id, decision_path, owner, score) -> log_id
- notify:send(channel, recipients, template, payload) -> notification_result

我收一下这一层：
- CRM：读 lead、查重、建 SLA
- 营销系统 / 增强服务：补充评分所需数据
- 路由规则 / 分配接口：完成归属决策
- 通知 / 审计：形成闭环
- 高冲突归属前保留人工确认
- 这里定义的是工具接口层，不是实现细节

如果这里没偏，我输出最终简报。

---

## Final brief produced from the dialogue

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
- 它像团队里的哪一类人：
  一个成熟的销售运营分诊专员。
- 使命：
  在线索进入流程后完成校验、去重、优先级判断、归属建议和 SLA 跟进提醒。
- 它替代/辅助的是谁：
  主要辅助销售运营和 SDR 管理者；在规则明确的场景下可替代部分人工分配动作。
- 它最终对什么结果负责：
  对“线索是否被及时、正确、公平地分给合适的人”负责。
- 它最擅长的判断：
  识别表面新 lead 背后的真实客户归属，区分联系人重复、公司重复和机会层重复。
- 它最不能犯的错误：
  把高价值 lead 分错，或打断应该延续的 owner continuity。
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
- active opportunity

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
| 重复检测 Skill | 判断重复是联系人层、公司层，还是机会层 | 完成字段校验后 | 邮箱、手机号、公司名称、CRM 历史记录 | 重复结果、冲突标记、保留归属建议 | 自动 |
| 线索评分 Skill | 判断这条线索值不值得优先跟进 | 去重完成后 | 公司画像、来源、职位、行为信号 | 评分结果、优先级、原因说明 | 自动 |
| 归属分配 Skill | 决定线索进入哪个队列或分配给哪个销售 | 评分和规则就绪后 | 区域、行业、客户等级、当前负载、归属规则 | 目标队列/销售、分配原因 | 审批后执行 |
| SLA 与审计 Skill | 让后续跟进不被漏掉、可追踪 | 分配完成后 | 分配结果、优先级、SLA 规则 | SLA 任务、提醒、审计日志 | 自动 |

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
- 同事最依赖它的时刻是什么：
  当大量 lead 同时涌入，而团队最怕把高价值线索分错或分慢的时候。
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

## What this demo is for

Use this file when you want to demonstrate not just the final output, but also the conversational behavior expected from the bot:
- short round intro
- compact questions
- structured summary
- explicit confirmation
- clean final brief
- real-case-driven discovery rather than abstract questioning
