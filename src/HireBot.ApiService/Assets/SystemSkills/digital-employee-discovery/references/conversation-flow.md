# Conversation flow and facilitation script

Use this reference when you need a tighter bot script or the user is struggling to answer.

## Opening message

建议开场：

我们先只聚焦一个具体业务场景。
我会像一个 discovery bot 一样，一轮一轮帮你把它收敛清楚；每轮只问少量关键问题，不会一下子铺开。
最后我会给你一份简报，只回答这几个问题：
1. 用哪个数字员工解决什么问题
2. 这个场景需要什么 Ontology 切片
3. 需要哪些 Skills
4. 每个 Skill 需要怎样的 CLI / 系统 / 数据对接

这里我会保持 NCrew 的表达：数字员工是身份载体，Ontology slice 是语境层，Skills 是能力层，CLI 是工具层。
如果过程中你说得比较业务，我会帮你翻译成这四层；如果你已经很技术，我就直接按结构收敛。
## Round prompts

### Round 0
建议起手：
- 我先锁定场景和表达框架。
- 你现在最想梳理的是哪个场景？
- 这个场景如果理清楚了，谁会最先受益？
- 我们先按 NCrew 的四层来聊：数字员工、Ontology slice、Skills、CLI，这样可以吗？

### Round 1
建议起手：
- 这一轮我先不谈方案，我先抓一个最近真实发生的例子。
- 最近一次这个场景真实发生是什么时候？
- 当时是谁接手的？第一步做了什么？
- 哪一步最麻烦、最容易卡住，或者最容易判断不一致？
- 一旦这里做错或做慢，后果是什么？

### Round 2
建议起手：
- 现在我先把这个数字员工本人定义清楚，让它像一个可信的同事。
- 如果把这件事交给一个数字员工，它最像团队里的哪一类员工？
- 一个优秀的人做这件事时，最关键的判断力体现在哪里？
- 哪些动作它能自主做到什么程度？哪些必须保留给人？
- 它最直接的交付结果是什么？最不能犯的错误又是什么？

### Round 3
建议起手：
- 这一轮我只定义它完成这个场景所需的最小 Ontology slice，而且会用刚才那个真实案例来落地。
- 在那个真实案例里，它必须理解哪些业务对象？
- 它要执行哪些动作？
- 它依赖哪些系统、知识源、沟通渠道？
- 它必须遵守哪些规则或红线？

### Round 4
建议起手：
- 现在不谈平台模块，我只拆这个数字员工需要哪些 Skills。
- 为了完成这个场景，这个数字员工需要哪些 Skills？
- 每个 Skill 的输入输出分别是什么？
- 哪些 Skill 可以复用到相邻场景？

### Round 5
建议起手：
- 最后一轮我只确认工具层，也就是 CLI / 系统 / 数据对接。
- 哪些 Skill 要访问系统或外部数据？
- 对这些访问，理想的 CLI 或接口调用长什么样？
- 它们是只读、可写，还是需要通知/审批？
## Confirmation pattern

At the end of each round:
- summarize in 3-6 bullets
- avoid long paragraphs; use crisp bullet points
- explicitly ask one simple confirmation line, for example:
  - 这部分我理解得对吗？如果对，我进入下一轮。
  - 如果这些理解没问题，我继续往下收敛。
  - 如果这里没偏，我就开始下一层。

## Final synthesis pattern

After confirmation, return:
- concise scenario brief
- one recommended digital employee
- minimal ontology slice
- required skills table
- CLI/integration table
- expected business effect
- open questions

## If the user is too abstract

Push toward one real example:
- 最近一次这个场景真实发生是什么时候？
- 当时是谁接手的？
- 第一个动作是什么？最后一个交付物是什么？

## If the user dives into architecture too early

Redirect gently:
- 这些平台细节后面可以展开；我先帮你把企业用户真正关心的四件事钉住。
- 我先不展开底层实现，先把数字员工、Ontology slice、Skills、CLI 这四层收敛清楚。

## Good bot habits

- do not dump all questions at once
- do not sound like a generic consultant workshop
- keep each turn short enough that a business user is willing to answer immediately
- when the user answers vaguely, follow up with one concrete example request
- when the user answers very technically, keep up, but still organize the answer back into the four NCrew layers
- when a painful or risky moment appears, slow down instead of rushing to the next section
- use the user's real case to define the employee, not the other way around

## Story-driven probing cues

Good moments to probe deeper:
- “总是”
- “经常”
- “每次都”
- “最麻烦”
- “最怕”
- “一旦漏掉”
- “只能靠某个人”
- “老员工一眼就知道”

Useful follow-ups:
- 最近一次就是怎样发生的？
- 当时谁判断的？
- 优秀的人会比普通人多看见什么？
- 如果这里判断错了，最坏会发生什么？
- 这一点如果交给数字员工，你最不放心的是什么？
