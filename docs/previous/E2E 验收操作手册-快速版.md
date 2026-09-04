# E2E 验收操作手册（快速版）

> 本手册用于在本机快速走通 **访客全流程体验官** 雇佣四阶段，验收终点为：**四阶段完成**、右侧 **「4 生成实例包」** 为 **已生成**、对话区出现 **template** 产物（`visitor-experience-pilot-artifacts.zip`），并可 **进入培训流程**（导入成功）。
>
> 与 [`E2E 验收操作手册-不接外部系统.md`](./E2E%20验收操作手册-不接外部系统.md) 的差异：**外部阶段必须配置 MCP**，**禁止**点击「跳过」或发送「不接外部系统」类话术。
>
> 完整 ZIP 下载与五件套校验见 [`打包 testcase 扩展功能 E2E 验收操作手册.md`](./打包%20testcase%20扩展功能%20E2E%20验收操作手册.md) §7–§8（**本快速版默认不执行**）。

---

## 1. 验收目标

| 项 | 通过标准 |
|----|----------|
| 流程 | 顶部进度条 1–4 步均为 **已产出**（或等价 completed） |
| 外部 | `submissionMode=configured`，且已保存 MCP（流式 HTTP） |
| 打包 | 对话区出现 **实例包已就绪** / `visitor-experience-pilot-artifacts.zip`（通常 > 10 KB） |
| 导入 | 出现 **「进入培训流程」**（或已点击 **「手动导入系统」** 并成功） |
| UI | 右侧 **「4 生成实例包」** 显示 **已生成** |

**本快速版不要求：** import 完成后的 final 实包下载、五件套 `dotnet test`（见 §8 可选）。

---

## 2. 固定素材

| 项 | 值 |
|----|-----|
| 模板 | 访客全流程体验官（Visitor Experience Pilot） |
| `templateId` | `019ddd2a-5143-73aa-8880-b8063164ed87` |
| 雇佣 URL | `http://localhost:5173/template-pool/hiring/019ddd2a-5143-73aa-8880-b8063164ed87` |
| 上传资料 | `%USERPROFILE%\Documents\1.ncrew\测试\访客预约与审核规则.md` |

---

## 3. 前置（约 3 分钟）

### 3.1 启动服务

雇佣流程依赖 **OpenSandbox + ApiService + 前端**，推荐 Skill：**`hirebot-opensandbox-mcp`**（含 8090 沙箱 + 5280 后端）；若仅刷新前后端、沙箱已正常，可用 **`hirebot-dev-start`**。

| 端口 | 服务 |
|------|------|
| 8090 | OpenSandbox |
| 5280 | ApiService |
| 5173 | Vite 前端 |

**PowerShell 健康检查**（含中文路径时先设 UTF-8）：

```powershell
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

Set-Location 'c:\Users\wayye\Documents\ai4c_Projects\hirebot'
@('http://localhost:8090/health','http://localhost:5280/swagger/index.html','http://localhost:5173/') |
  ForEach-Object { (Invoke-WebRequest -UseBasicParsing -Uri $_ -TimeoutSec 5).StatusCode; $_ }
```

期望均为 **200**。浏览器打开 `http://localhost:5173` 并登录 Keycloak（若已配置 OIDC）。

### 3.2 本地 API 鉴权说明

- 未配置 `Security:OidcAuthority` 时，ApiService 使用 **Development** 鉴权，**无需** `Authorization: Bearer` 即可调用下文 REST 示例。
- 若本地启用了 Keycloak/OIDC，上传与对话 API 需从浏览器 DevTools 复制 `access_token` 填入示例。

---

## 4. 发起雇佣与对话区说明

### 4.1 进入页面

1. 模板池 → **访客全流程体验官**，或打开 §2 雇佣 URL。
2. 等待中间 **对话区**（`HiringConversationPanel` / `.hb-hiring-chat-body`）完成初始化；记录 **`hireId`**、**`sessionId`**：
   - **推荐**：F12 → Network → 筛选 `hire` 或 `hirings`，从创建/详情响应的 `data.hireId`、`data.sessionId` 复制；
   - **备选**：`GET http://localhost:5280/api/v1/hirings/{hireId}`（Development 环境可直接调用）。

### 4.2 对话区你会看到什么（固定 + 动态）

对话区顶部有一张 **说明卡片**（不随轮次滚动消失），文案来自前端 i18n，与教练首条回复不同：

| 区块 | 典型文案（访客模板） |
|------|----------------------|
| 标题 | 我是**访客全流程体验官（Visitor Experience Pilot）** |
| 副标题 | 我们会基于「访客全流程体验官（Visitor Experience Pilot）」模板完成一条新的部门版雇佣流程。这次我会像一位即将上岗的新同事一样，主动告诉你我还缺什么。 |
| 详情 | 你好，我是数字员工**访客全流程体验官（Visitor Experience Pilot）**，本次会围绕 **report-synthesis、…**（模板 `coreAbilities` 前三项）等能力完成资料发现、技能整理、外部系统确认和实例交付。 |

其下为 **教练动态消息**（沙箱 LLM），例如：

- 邀请上传 **访客预约与审核规则**、**门禁与接待 SOP**、**PACE 接待话术** 等分类（以当次 `requested_categories` 为准）；
- 资料解析后的 **产物卡片**（`material_collection_progress` / `material_parsed` 等）；
- 阶段推进 **🚦 stage_gate**、技能工单、打包 ZIP 等。

> **操作提示**：右侧待办「待上传 2」仅表示还有 **建议分类** 未上传；快速版可用 §5.1 **口头确认** 收口，不必凑齐全部文件。

---

## 5. 四阶段操作清单（推荐顺序）

依赖沙箱 LLM，需人工在对话区拍板；单条对话 API 超时建议 **600s**。**不要**在 `isConversationResponding=true` 时连发相同话术。

```mermaid
flowchart LR
  A[上传资料] --> B[对话收口阶段1]
  B --> C[单技能确认 + 开始]
  C --> D[MCP 保存并继续]
  D --> E[生成实例包 + 导入]
```

| 步骤 | 阶段 | 主要动作 | 通过信号 |
|------|------|----------|----------|
| ① | 1 业务资料 | 上传 md → 发收口话术 | 顶部「1 业务资料」**已产出**；出现资料收口 / stage_gate → 技能 |
| ② | 2 技能补齐 | 单技能话术 → 教练询问后回复「开始」 | 教练进入外部配置；右侧「3 外部系统」可点 **继续配置** |
| ③ | 3 外部连接 | MCP 弹窗保存 → **保存并继续**（禁止跳过） | 对话自动插入外部已保存摘要；「3 外部系统」**已完成** |
| ④ | 4 打包准备 | 点「4 生成实例包」或对话确认打包 → **手动导入**（若需要） | 出现 `visitor-experience-pilot-artifacts.zip` + **进入培训流程** |

> **进度判断**：`GET /api/v1/hirings/{hireId}` 的 `currentStage` **可能滞后**于页面（例如 UI 已在技能阶段，API 仍显示 `material`）。以 **右侧待办 + 顶部进度条 + 对话产物** 为准，勿仅依赖轮询 API 阶段字段。

---

### 5.1 阶段 1：业务资料

#### A. 上传资料（二选一）

**方式 1 — 页面（直观）**

1. 右侧 **「1 业务资料」** → 在「访客预约与审核规则」分类下上传 `访客预约与审核规则.md`。
2. 等待对话区出现 **「已收到资料」** 类产物卡片后再做 B。

**方式 2 — API（与待办等价，Development 可省略 Bearer）**

```powershell
$hireId = 'hire-xxxxxxxx'
$sessionId = 'session-xxxxxxxx'
$filePath = Join-Path $env:USERPROFILE 'Documents\1.ncrew\测试\访客预约与审核规则.md'

curl.exe -s -X POST "http://localhost:5280/api/v1/hirings/$hireId/material-files/upload" `
  -F "session_id=$sessionId" `
  -F "requested_category_title=访客预约与审核规则" `
  -F "files=@$filePath"
```

（若启用 OIDC，增加：`-H "Authorization: Bearer $token"`。）

#### B. 对话收口（资料阶段 → 技能）

在对话输入框发送（或 `POST .../conversation/messages`，`Content-Type: application/json`）：

```text
已上传《访客预约与审核规则.md》，请抽取预约审核、实名核验与门禁发码规则。
另外两类资料口头确认：门禁码动态刷新+08:00-18:00有效；接待按PACE框架推送。
请确认资料阶段完成并进入技能阶段。
```

教练若追问实名/门禁细节，可回复：

```text
两点确认：1）实名核验采用到访时安保人证比对，不接入第三方实名接口；
2）门禁动态码按分钟刷新。资料阶段请收口并继续技能生成。
```

**阶段 1 通过信号：** 对话出现 **资料已收口** / **stage1_material → stage2_skill** 阶段门；顶部「1 业务资料」**已产出**。

---

### 5.2 阶段 2：技能补齐

#### A. 收敛为单技能（缩短耗时）

```text
确认仅生成 1 个技能：visitor-booking-intake（触发词「访客预约/提交预约」）。
其余技能先不生成、不落盘，完成后进入外部系统配置。
```

#### B. 确认开始生成

当教练出现 **「是否现在开始生成技能实现」**（或同类确认）时，回复：

```text
开始
```

（亦可：**可以** / **确认生成**。）

#### C. 卡住时推进

若右侧 `visitor-booking-intake` 长期 **「生成中 0/1」**，但教练已提示进入外部配置，可先进入 §5.3；若仍停在技能阶段，发送：

```text
技能文件已写入，请继续完成 skill-generation 并进入外部系统配置。
```

**阶段 2 通过信号：** 教练话术转入 **外部能力配置**；右侧 **「3 外部系统」** 出现 **继续配置**（非灰显等待）。  
**可忽略：** 顶部「2 技能补齐」仍显示 **派发中**，而「4 已生成」已出现——属 WS 展示滞后，不阻断 §1 验收终点。

---

### 5.3 阶段 3：外部连接（MCP，禁止跳过）

> **禁止：** 点击 **「跳过」**、发送「不接外部系统 / 本阶段直接跳过」等话术。  
> **重要：** 除 MCP 弹窗内 **「保存」** 外，必须在卡片上点击 **「保存并继续」**；仅 `PUT /external-config` 或仅在对话里复述配置，**不能**替代该按钮完成阶段提交。

**操作顺序（建议全程在浏览器完成）：**

1. 右侧 **「3 外部系统」** → **「继续配置」**（不要点「跳过」）。
2. **MCP** 行 → **「编辑配置」**，填写：

| 字段 | 填写值 |
|------|--------|
| 名称 | 任意非空（示例：`ms-learn-mcp`） |
| 连接类型 | **流式 HTTP**（不要选 STDIO） |
| URL | `https://learn.microsoft.com/api/mcp` |
| Bearer 令牌环境变量 | 留空 |
| 固定 Header / 来自环境变量的 Header | 不添加 |

3. 弹窗右下角 **「保存」**（卡片应显示「已配置 MCP「…」（HTTP (远程服务)）」）。
4. 外部卡片底部主按钮 **「保存并继续」**（红框主按钮，勿与弹窗「保存」混淆）。

**预期：**

- 对话区自动插入类似：  
  `外部系统配置已保存：MCP ms-learn-mcp（HTTP (远程服务)） URL: https://learn.microsoft.com/api/mcp。外部阶段已完成，请继续下一步。`
- 右侧 **「3 外部系统」** → **已完成**；顶部 **「3 外部连接」** → **已产出**。

**API 备选（调试用，仍建议补点 UI「保存并继续」）：**

```powershell
$hireId = 'hire-xxxxxxxx'
$body = @{
  submissionMode = 'configured'
  cliTools = @()
  mcpServer = @{
    transport = 'http'
    name = 'ms-learn-mcp'
    url = 'https://learn.microsoft.com/api/mcp'
    bearerTokenEnv = ''
    headers = @{}
    headersFromEnv = @{}
  }
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Uri "http://localhost:5280/api/v1/hirings/$hireId/external-config" `
  -Method Put -Body $body -ContentType 'application/json; charset=utf-8'
```

---

### 5.4 阶段 4：打包与导入

1. 确认阶段 1–3 顶部为 **已产出** / 右侧外部 **已完成**。
2. **生成包（二选一）：**
   - **推荐 UI：** 右侧 **「4 生成实例包」** → 点击生成；
   - **对话：** 发送  
     `是，现在开始打包。请生成 visitor-experience-pilot-artifacts.zip 实例包并自动导入。`
3. 等待对话区产物卡片出现 **`visitor-experience-pilot-artifacts.zip`**（体积通常 > 10 KB）。
4. 若未自动导入，点击产物卡片上的 **「手动导入系统」**，直至出现 **「进入培训流程」**。
5. 确认右侧 **「4 生成实例包」** 为 **已生成**。

**本快速版验收通过（满足即可收工）：**

- 对话区有 **`visitor-experience-pilot-artifacts.zip`**；
- **「进入培训流程」** 或导入成功提示；
- 右侧 **「4 生成实例包」→ 已生成**。

**可忽略（不阻断）：**

- 对话区红色 **「工作流异常 HTTP 404/500」**（网关 fileUrl 偶发失败时，template 包仍可能在 artifact-store 生成）；
- 教练提示未发现 `skills/visitor-booking-intake/SKILL.md`（快速版不强制沙箱 skills 目录完整）；
- **无需** 下载 ZIP 或执行 §8 五件套强校验。

**后端默认行为（2026-06 起）：** 外部配置保存为 `configured` 或 **手动/自动 import** 时，后端会**默认执行** packaging testcase staging，final ZIP 中通常会出现 `testcases/evaluation-test-cases.json`（`source` 可为 `packaging-merged` 或降级 `packaging-fallback` 空 `test_cases`）。快速版仍不要求你为此单独发打包话术或校验五件套内容。

---

## 6. 状态自检（可选，PowerShell）

将 `hireId` 替换为实际值；Development 环境无需 Token。

```powershell
$hireId = 'hire-xxxxxxxx'
$h = Invoke-RestMethod "http://localhost:5280/api/v1/hirings/$hireId"
$ext = Invoke-RestMethod "http://localhost:5280/api/v1/hirings/$hireId/external-config"
Write-Host "stage=$($h.data.currentStage) status=$($h.data.status) responding=$($h.data.isConversationResponding)"
Write-Host "external=$($ext.data.submissionMode) mcp=$($ext.data.mcpServer.name)"
```

**可选：** import 后解压 final ZIP，确认存在 `testcases/evaluation-test-cases.json`（不强制 `test_cases` 非空）。

---

## 7. 常见卡点

| 现象 | 处理 |
|------|------|
| 对话区只有说明卡片、无教练回复 | 查 8090 沙箱与 ApiService 日志；必要时执行 `hirebot-opensandbox-mcp` |
| 上传后教练一直追问资料 | 先等解析产物卡片，再发 §5.1 B 收口话术；勿连发 |
| 「生成实例包」灰色 | 阶段 3 须点卡片 **保存并继续**（非仅 MCP 弹窗保存）；勿用「跳过」 |
| 「保存并继续」灰色 | MCP 弹窗须先 **保存**，且名称、URL 有效 |
| 外部仍显示待配置 | 刷新页面；`GET .../external-config` 应为 `configured` |
| 技能「生成中」很久 | §5.2 C 推进话术；若教练已让配外部，可直接 §5.3 |
| API `currentStage` 不变 | 以 UI 待办为准（见 §5 说明） |
| 有 ZIP 无「进入培训流程」 | 点击 **手动导入系统** |
| 504 / 超时 | 勿连发；查 `conversation/cache` 或沙箱日志 |

---

## 8. 可选：Template 包一键校验

四阶段走通且对话区已出现 template 产物后，若需快速确认 ZIP 结构，执行（将路径替换为实际文件）：

```powershell
Set-Location 'c:\Users\wayye\Documents\ai4c_Projects\hirebot'

.\.cursor\skills\hirebot-final-package-e2e\scripts\verify-final-package.ps1 `
  -ZipPath 'C:\Users\wayye\Documents\1.ncrew\测试\e2e-template-hire-xxxxxxxx.zip' `
  -SessionId 'session-xxxxxxxx' `
  -SkipPreflight
```

- **ZIP 从哪来：** 对话区下载，或按 [打包手册 §7.1](<./打包 testcase 扩展功能 E2E 验收操作手册.md#71-template-包优先网关404-则-artifact-store>) 从 `artifact-store\...\packages\intermediate\package.zip` 复制。
- **Final 包与完整五件套：** 见 [打包手册 §7.2–§8](<./打包 testcase 扩展功能 E2E 验收操作手册.md#72-final-包import-之后>)。

---

## 9. 与「不接外部系统」手册对照

| 项目 | 不接外部（参考对话导出） | 本快速版 |
|------|-------------------------|----------|
| 外部阶段 | `submissionMode: skipped` | `configured` + MCP |
| UI 操作 | 点「跳过」 | **继续配置** → MCP → **保存并继续** |
| MCP URL | — | `https://learn.microsoft.com/api/mcp` |
| 验收终点 | 实例包 + 导入 | 同左（template 产物 + **进入培训流程**） |

---

*文档版本：2026-06-01 · 模板：访客全流程体验官 · 外部：MCP 流式 HTTP（Microsoft Learn 公共端点）· 对话区结构对齐 `HiringConversationPanel`*
