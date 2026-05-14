# HireBot

## 项目目录结构

- `src/`：生产源码目录，包含 API、核心领域、仓储与迁移等项目。
- `tests/`：测试工程目录（单元/集成测试），与 `src/` 物理隔离，避免测试代码与业务源码互相干扰。
- `docs/`：方案与需求文档目录（例如雇佣流程改造清单）。

## 测试说明

- 核心单元测试工程：`tests/HireBot.Core.Tests/`
- 运行该测试工程：
  - `dotnet test tests/HireBot.Core.Tests/HireBot.Core.Tests.csproj`

## Docker 镜像构建

### 命名格式

```
ai4c-tcr.tencentcloudcr.com/agentfoundry/hirebot:<env>-<yyyyMMddHHmm>
```

| 字段 | 说明 |
|---|---|
| `env` | 环境标识，开发构建用 `dev` |
| `yyyyMMddHHmm` | 构建时间，精确到分钟 |

示例：`ai4c-tcr.tencentcloudcr.com/agentfoundry/hirebot:dev-202605081149`

### 构建命令

在**仓库根目录**（`hirebot/`）执行，构建上下文包含前端和后端：

```powershell
# 自动生成带时间戳的 tag 并构建
$tag = "ai4c-tcr.tencentcloudcr.com/agentfoundry/hirebot:dev-$(Get-Date -Format 'yyyyMMddHHmm')"
docker build -t $tag .
Write-Host "Built: $tag"
```

```bash
# Bash / Linux / macOS
tag="ai4c-tcr.tencentcloudcr.com/agentfoundry/hirebot:dev-$(date +%Y%m%d%H%M)"
docker build -t "$tag" .
echo "Built: $tag"
```

### 本地运行

```powershell
# 使用远程 PostgreSQL
docker run --rm -d `
  --name hirebot-test `
  -p 8080:8080 `
  -e "ConnectionStrings__DefaultConnection=Host=<pg-host>;Port=<port>;Database=<db>;Username=<user>;Password=<pass>;SSL Mode=Prefer;Trust Server Certificate=true;" `
  ai4c-tcr.tencentcloudcr.com/agentfoundry/hirebot:<tag>
```

访问 http://localhost:8080

停止：`docker stop hirebot-test`

### 推送镜像

```powershell
docker push $tag
```

---

## Helm 构建与部署

### 目录结构

```
helm/
  ncrew-hire/          # Helm chart
    Chart.yaml         # chart 元信息（version / appVersion）
    values.yaml        # 默认值
    values-saas.yaml   # SaaS 生产环境覆盖值
    templates/         # K8s 资源模板
  dist/                # helm package 产物（.tgz）
```

### 完整发布流程（PowerShell）

```powershell
# 1. 构建镜像（在仓库根目录执行）
$tag = "ai4c-tcr.tencentcloudcr.com/agentfoundry/hirebot:dev-$(Get-Date -Format 'yyyyMMddHHmm')"
docker build -t $tag .
Write-Host "TAG=$tag"

# 2. 推送到 TCR
docker push $tag

# 3. 更新 Helm chart 中的镜像 tag 和版本（手动编辑或用 sed/PowerShell 替换）
#    helm/ncrew-hire/values-saas.yaml  →  image.tag: <新 tag>
#    helm/ncrew-hire/Chart.yaml        →  appVersion: "<新 tag>", version: <递增 chart 版本>

# 4. （可选）打包 chart 到 dist/
helm package helm/ncrew-hire -d helm/dist

# 5. 部署 / 升级到 opensandbox 命名空间
helm upgrade --install ncrew-hire helm/ncrew-hire `
  -f helm/ncrew-hire/values-saas.yaml `
  -n opensandbox --create-namespace
```

### 单步说明

| 步骤 | 命令 | 说明 |
|---|---|---|
| 构建 | `docker build -t <tag> .` | 多阶段构建，同时编译前端（Node 22）和后端（.NET 10） |
| 推送 | `docker push <tag>` | 推送到腾讯云 TCR `agentfoundry` 仓库 |
| 更新 tag | 编辑 `values-saas.yaml` 和 `Chart.yaml` | `image.tag` 与 `appVersion` 保持一致，`Chart.yaml version` 递增 |
| 打包 | `helm package helm/ncrew-hire -d helm/dist` | 生成 `ncrew-hire-<version>.tgz`，版本取自 `Chart.yaml` |
| 部署 | `helm upgrade --install ... -n opensandbox` | 首次自动 install，后续为 upgrade |

### 查看部署状态

```powershell
# 查看 Helm release 历史
helm history ncrew-hire -n opensandbox

# 查看 Pod 状态
kubectl get pods -n opensandbox -l app.kubernetes.io/name=ncrew-hire

# 查看最新日志
kubectl logs -n opensandbox -l app.kubernetes.io/name=ncrew-hire --tail=100 -f
```

### TCR 登录

```powershell
docker login ai4c-tcr.tencentcloudcr.com --username <账号UIN> --password <临时token>
```

临时 token 通过腾讯云控制台「容器镜像服务 → 访问凭证」生成，有效期通常为 1 小时。

---

## 雇佣流程改造关键配置

以下配置位于 `src/HireBot.ApiService/appsettings.json`（开发环境可在 `appsettings.Development.json` 覆盖）：

- 模板包上传通道
  - 说明：模板包上传已固定为调用 Kingcrab `/admin/digital-employee/upload`，不再保留 `Legacy` 上传分支。
- `HireBot:ConversationKickoffPrompt`
  - 作用：会话启动后，当系统检测到尚无 assistant 首条消息时，用于触发 AI 首问的提示词。
  - 建议：按业务场景调整为引导式问题，促使用户在会话中逐步补齐模板包内容。
