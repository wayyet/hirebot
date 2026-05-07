# NCrew Web 前端设计风格指引（agents.md）

本文件用于约束 `hirebot-web` 的前端视觉与交互风格，供人类开发者与 AI Agent 统一执行。

## 1. 设计定位

- 产品形态是 **B2B 管理后台 + 流程化工作台**，不是营销站点。
- 视觉基调是 **极简、理性、信息优先**：白底、浅边框、低噪音。
- 风格关键词：`clean`、`flat`、`status-driven`、`high-density but readable`。

## 2. 色彩系统（以代码现状为准）

- 中性色主轴（全局默认）：
- `bg-white` / `bg-slate-50` / `bg-slate-100`
- `text-slate-900`（标题） / `text-slate-700`（正文） / `text-slate-500`（说明） / `text-slate-400`（辅助）
- `border-slate-100` / `border-slate-200`

- 语义状态色（只在状态/流程中使用，不滥用）：
- 成功：`emerald-*`
- 警告：`amber-*` / `orange-*`
- 错误：`red-*` / `rose-*`
- 信息：`blue-*`
- 流程强调：`indigo-*` / `violet-*`（多用于训练、评估、步骤流、机器人场景）

- 主操作按钮优先使用：
- `bg-slate-900 text-white hover:bg-slate-800`

## 3. 字体与层级

- 全局字体栈：`-apple-system, BlinkMacSystemFont, 'PingFang SC', 'Hiragino Sans GB', 'Microsoft YaHei', sans-serif`
- 常用字号层级：
- 页面主标题：`text-2xl font-semibold`
- 区块标题：`text-lg font-semibold` 或 `text-sm font-semibold`
- 正文：`text-sm`
- 辅助：`text-xs`
- 微标签：`text-[10px]` / `text-[9px]`

- 字重分布以 `font-medium` 与 `font-semibold` 为主，`font-bold` 只用于关键数字/结论。

## 4. 间距、圆角、边框、阴影

- 容器常规：`max-w-7xl mx-auto px-8 py-8`（详情页常见 `max-w-5xl` / `max-w-4xl`）
- 高频间距：`gap-2`、`gap-3`、`p-4`、`p-5`、`py-2`、`px-3/px-4`
- 圆角基准：`rounded-lg`（主力） + `rounded-xl`（面板） + `rounded-full`（徽章/头像）
- 边框优先于阴影，阴影仅轻量使用：
- 常态：`border border-slate-100`
- Hover：`hover:border-slate-300 hover:shadow-sm`

## 5. 组件形态模板

- 基础卡片：
```tsx
className="bg-white rounded-xl border border-slate-100 p-5"
```

- 可点击卡片：
```tsx
className="bg-white border border-slate-100 rounded-lg p-5 hover:border-slate-300 hover:shadow-sm transition-all cursor-pointer"
```

- 主按钮：
```tsx
className="px-5 py-2.5 bg-slate-900 text-white rounded-lg text-sm font-medium hover:bg-slate-800 transition-colors"
```

- 次按钮：
```tsx
className="px-4 py-2 border border-slate-200 text-slate-600 rounded-lg text-sm hover:bg-slate-50 transition-colors"
```

- 输入框：
```tsx
className="w-full px-3 py-2 bg-slate-50 border border-slate-200 rounded-lg text-sm outline-none focus:border-slate-300 focus:bg-white"
```

- 对话框遮罩与容器：
```tsx
className="fixed inset-0 bg-black/50 flex items-center justify-center z-50"
className="bg-white rounded-xl p-6 max-w-md w-full"
```

## 6. 交互与动效

- 默认过渡优先 `transition-colors`，其次 `transition-all`。
- 动效保持克制，只用于反馈状态：
- `animate-spin`（加载）
- `animate-pulse`（运行中）
- 少量 `animate-bounce`（对话输入提示）

- 禁止炫技型动效、连续大范围位移动画。

## 7. 页面编排模式

- 常见结构：`Header（标题+说明+主动作） -> Summary Cards -> 主列表/主面板`。
- 信息密集页采用“卡片分区 + 清晰标题 + 轻量分隔线”。
- 流程页采用“步骤/进度 + 状态面板 + 行动按钮”的任务导向布局。
- 聊天/流程场景允许双栏（左对话、右配置输出），但保持同一套中性色框架。

## 8. 图标与内容语气

- 图标以 `lucide-react` 为主，emoji 作为业务语义增强（角色、平台、流程节点）。
- 文案语气：专业、简短、可执行，避免营销化夸张描述。
- 状态词建议固定：`待启动 / 实习中 / 已转正 / 异常 / 已归档 / 已确认`。

## 9. Do / Don’t

- Do：
- 保持 `Slate` 中性色为主体。
- 让颜色服务于状态语义，不服务于装饰。
- 优先用留白、字号、字重做层级。
- 使用统一卡片/按钮/输入框骨架，减少样式分叉。

- Don’t：
- 不引入大面积渐变、玻璃拟态、重阴影。
- 不随意新增高饱和品牌色作为主色。
- 不在同页混用多套圆角、边框和按钮语义。
- 不为了“好看”牺牲信息可读性与任务效率。

## 10. 新页面落地检查清单

- [ ] 是否以 `bg-white`/`bg-slate-50` + `border-slate-*` 为主？
- [ ] 主操作是否仍为 `slate-900` 深色按钮？
- [ ] 状态色是否仅用于成功/警告/失败/流程提示？
- [ ] 标题、正文、辅助文字的字号层级是否一致？
- [ ] 卡片、弹窗、表单是否复用既有样式骨架？
- [ ] Hover/动效是否克制且可解释？

