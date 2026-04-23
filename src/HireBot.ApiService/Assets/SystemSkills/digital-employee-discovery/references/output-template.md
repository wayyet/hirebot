# Digital employee discovery brief template

Use this template to assemble the final user-facing output.

```markdown
# 数字员工场景发现简报：[场景名称]

## 1. 为什么需要这个数字员工
- 当前问题：
- 现在主要是谁在处理：
- 这个工作通常在什么情况下发生：
- 企业最想改善的结果：

## 2. 数字员工画像卡
- 名称：
- 一句话角色：
- 它像团队里的哪一类人：
- 使命：
- 它替代/辅助的是谁：
- 它最终对什么结果负责：
- 它最擅长的判断：
- 它最不能犯的错误：
- 工作边界：
- 人机分工边界：
- 自主等级：建议 / 准备 / 审批后执行 / 直接执行

## 3. 它上岗后的工作方式（Day-1 work loop）
- 触发它开始工作的信号：
- 它拿到的第一批输入：
- 它会先做什么：
- 然后做什么：
- 在什么情况下交给人：
- 最终交付物是什么：

## 4. 它需要理解的业务世界（Ontology slice）
### 4.1 Entities
- 

### 4.2 Actions
- 

### 4.3 Resources
- 

### 4.4 Constraints
- 

## 5. 它具备的 Skills
| Skill | 它用这个 Skill 做什么 | 何时触发 | 关键输入 | 关键输出 | 自主等级 |
|------|--------------------|---------|---------|---------|---------|
| | | | | | |

## 6. 它工作的工具入口（CLI / 系统与数据对接）
| Skill | 系统/数据源 | 它要完成的动作 | CLI 或接口形态 | 认证/权限边界 | 备注 |
|------|------------|---------------|---------------|--------------|------|
| | | 读/写/通知/搜索/转换 | `system:action(args) -> result` | | |

## 7. 它如何与人协作
- 哪些情况它可以自己处理：
- 哪些情况必须升级给人：
- 人会在什么节点确认：
- 同事最依赖它的时刻是什么：
- 它需要向谁同步结果：

## 8. 预期业务效果
- 效率提升：
- 质量提升：
- 响应速度：
- 风险/合规改善：
- 组织影响：

## 9. 待确认问题
- 
```

Guidance:
- Keep the wording understandable to a business stakeholder.
- Write it as a vivid digital-employee briefing, not a flat checklist.
- Use short section intros and compact bullets; the brief should feel like a bot-produced decision memo.
- Do not expose internal platform modules unless explicitly requested.
- Keep the NCrew framing explicit: digital employee = identity carrier, ontology slice = context layer, skills = capability layer, CLI = tool layer.
