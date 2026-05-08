# ─── Stage 1: Build frontend ─────────────────────────────────────────────────
FROM node:22-alpine AS frontend-builder

WORKDIR /src/front-end

# 先只复制 lock 文件，利用 Docker 层缓存加速依赖安装
COPY front-end/package.json front-end/package-lock.json ./
RUN npm ci

# 构建生产包
COPY front-end/ .
RUN npm run build

# ─── Stage 2: Build API ───────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-builder

WORKDIR /src

# 先只复制 .csproj，利用 Docker 层缓存加速 NuGet 还原
COPY back-end/src/HireBot.Abstraction/HireBot.Abstraction.csproj         ./src/HireBot.Abstraction/
COPY back-end/src/HireBot.ApiService/HireBot.ApiService.csproj           ./src/HireBot.ApiService/
COPY back-end/src/HireBot.Core/HireBot.Core.csproj                       ./src/HireBot.Core/
COPY back-end/src/HireBot.Repository/HireBot.Repository.csproj           ./src/HireBot.Repository/
COPY back-end/HireBot.ServiceDefaults/HireBot.ServiceDefaults.csproj     ./HireBot.ServiceDefaults/

RUN dotnet restore src/HireBot.ApiService/HireBot.ApiService.csproj

# 复制所有源码并发布
COPY back-end/src/           ./src/
COPY back-end/HireBot.ServiceDefaults/ ./HireBot.ServiceDefaults/

RUN dotnet publish src/HireBot.ApiService/HireBot.ApiService.csproj \
    -c Release -o /app/publish --no-restore

# ─── Stage 3: Runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

COPY --from=api-builder /app/publish .

# 将前端产物放入 wwwroot，由 ASP.NET Core 静态文件中间件提供服务
COPY --from=frontend-builder /src/front-end/dist ./wwwroot/

# 确保资源上传目录存在
RUN mkdir -p /app/wwwroot/resources

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_NOLOGO=true

ENTRYPOINT ["dotnet", "HireBot.ApiService.dll"]
