# ywork Helm Chart

部署 HireBot（ywork 租户）到 Kubernetes 的 Helm Chart。

## 命名空间

部署目标命名空间：`opensandbox`

> 与 `ncrew-hire` chart 共享同一命名空间，所有 Kubernetes 资源名均以 `ywork` 为前缀，不会产生冲突。

## 部署指令

### 首次安装

```bash
helm install ywork ./helm/ywork \
  -f helm/ywork/values-ywork.yaml \
  --namespace opensandbox \
  --create-namespace
```

### 更新（或首次安装兼容写法）

```bash
helm upgrade --install ywork ./helm/ywork \
  -f helm/ywork/values-ywork.yaml \
  --namespace opensandbox \
  --create-namespace
```

### 卸载

```bash
helm uninstall ywork --namespace opensandbox
```

## Values 文件说明

| 文件 | 用途 |
|---|---|
| `values.yaml` | 默认基础配置，包含所有可配置项的默认值 |
| `values-ywork.yaml` | ywork 生产环境覆盖值（镜像版本、数据库、OIDC 等） |

## 敏感配置

`LlmApiKey` 和 `ClientSecret` 通过 Kubernetes Secret 注入，不写入 values 文件。

`values-ywork.yaml` 中 `secrets.create: true` 时会自动创建 Secret `ywork-env`，
`secretEnv` 中引用的 Secret 名均为 `ywork-env`。

数据库连接字符串存储在 Secret `ywork-db` 中。

## 生成的 Kubernetes 资源

| 资源类型 | 资源名 |
|---|---|
| Deployment | `ywork` |
| Service | `ywork` |
| Secret (数据库) | `ywork-db` |
| Secret (环境变量) | `ywork-env` |
| PVC (数据目录) | `ywork-data`（需 `hireData.persistence.enabled: true`） |
