using System.Collections.Concurrent;
using System.IO.Compression;
using System.IO;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeTemplate;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.Hiring.Artifacts;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.Storage;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Core.Services.Sandbox;
using HireBot.Core.Services.SystemSkills;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

internal sealed partial class EmployeeHiringService
{
    private bool TryResolveOwnerContext(string hireId, out HireOwnerContext ownerContext)
    {
        var runtimeContext = hiringRuntimeStore.Get(hireId);
        if (runtimeContext is null)
        {
            var persistedOwnerContext = ResolvePersistedOwnerContext(hireId);
            if (persistedOwnerContext is null)
            {
                ownerContext = default!;
                return false;
            }

            ownerContext = persistedOwnerContext;
            return true;
        }

        ownerContext = new HireOwnerContext(
            OwnerSubject: runtimeContext.OwnerSubject,
            TenantId: runtimeContext.TenantId,
            OperatorId: runtimeContext.OperatorId,
            TemplateId: runtimeContext.TemplateId,
            TemplateName: runtimeContext.TemplateName,
            EmployeeId: runtimeContext.EmployeeId);
        return true;
    }

    private HireOwnerContext? ResolvePersistedOwnerContext(string hireId)
    {
        var sandboxInstance = dbContext.SandboxInstances
            .AsNoTracking()
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefault(item =>
                item.ScopeType == SandboxScopeTypes.Hire &&
                item.ScopeKey == hireId &&
                item.State != "Deleted");
        if (sandboxInstance is null)
        {
            return null;
        }

        return new HireOwnerContext(
            OwnerSubject: sandboxInstance.OwnerSubject,
            TenantId: sandboxInstance.TenantId,
            OperatorId: sandboxInstance.OperatorId,
            TemplateId: string.Empty,
            TemplateName: string.Empty,
            EmployeeId: null);
    }

    private HireOwnerContext ResolveOwnerContextByHireId(string hireId)
    {
        if (TryResolveOwnerContext(hireId, out var ownerContext))
        {
            return ownerContext;
        }

        var ownerSubject = ResolveOwnerSubject();
        var (tenantId, operatorId) = ResolveTenantAndOperator(null, null);
        return new HireOwnerContext(
            OwnerSubject: ownerSubject,
            TenantId: tenantId,
            OperatorId: operatorId,
            TemplateId: string.Empty,
            TemplateName: string.Empty,
            EmployeeId: null);
    }

    private string ResolveSandboxRole(string hireId)
    {
        if (TryResolveOwnerContext(hireId, out var ownerContext) &&
            string.Equals(ownerContext.TemplateId, EvaluationWorkspaceTemplateId, StringComparison.OrdinalIgnoreCase))
        {
            return "evaluation-evaluator";
        }

        return "hiring";
    }

    private static bool TryParseOwnerSubject(string ownerSubject, out string tenantId, out string operatorId)
    {
        tenantId = string.Empty;
        operatorId = string.Empty;
        if (string.IsNullOrWhiteSpace(ownerSubject))
        {
            return false;
        }

        var delimiterIndex = ownerSubject.IndexOf(':');
        if (delimiterIndex <= 0 || delimiterIndex >= ownerSubject.Length - 1)
        {
            return false;
        }

        var tenant = ownerSubject[..delimiterIndex].Trim();
        var oper = ownerSubject[(delimiterIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(oper))
        {
            return false;
        }

        tenantId = tenant;
        operatorId = oper;
        return true;
    }

    private string ResolveOwnerByHireId(string hireId)
    {
        if (TryResolveOwnerContext(hireId, out var ownerContext))
        {
            return ownerContext.OwnerSubject;
        }

        return ResolveOwnerSubject();
    }

    /// <summary>
    /// 解析当前请求的所有者标识（ownerSubject）。
    /// 优先级：JWT sub claim > X-HireBot-Owner header > tenant:operator fallback。
    /// 注意：fallback 格式包含冒号，需要在传递给 Kubernetes 时进行转义（见 OpenSandboxProvisioner.ToK8sLabelValue）。
    /// </summary>
    /// <param name="tenantId">可选的租户 ID，用于 fallback</param>
    /// <param name="operatorId">可选的操作员 ID，用于 fallback</param>
    /// <returns>所有者标识字符串</returns>
    private string ResolveOwnerSubject(string? tenantId = null, string? operatorId = null)
    {
        var user = httpContextAccessor.HttpContext?.User;
        var sub =
            user?.FindFirst("sub")?.Value ??
            user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(sub))
        {
            return sub.Trim();
        }

        var ownerHeader = httpContextAccessor.HttpContext?.Request.Headers["X-HireBot-Owner"].ToString();
        if (!string.IsNullOrWhiteSpace(ownerHeader))
        {
            return ownerHeader.Trim();
        }

        var (resolvedTenantId, resolvedOperatorId) = ResolveTenantAndOperator(tenantId, operatorId);
        return $"{resolvedTenantId}:{resolvedOperatorId}";
    }

    /// <summary>
    /// 解析租户 ID 和操作员 ID。
    /// 优先从参数、JWT claims 中提取，最后 fallback 到默认值。
    /// </summary>
    /// <param name="tenantId">可选的租户 ID</param>
    /// <param name="operatorId">可选的操作员 ID</param>
    /// <returns>租户 ID 和操作员 ID 的元组</returns>
    private (string TenantId, string OperatorId) ResolveTenantAndOperator(string? tenantId, string? operatorId)
    {
        var user = httpContextAccessor.HttpContext?.User;

        var resolvedTenantId = FirstNonEmpty(
            tenantId,
            user?.FindFirst("tenant_id")?.Value,
            user?.FindFirst("tenant")?.Value,
            user?.FindFirst("tid")?.Value,
            "tenant-default");

        var resolvedOperatorId = FirstNonEmpty(
            operatorId,
            user?.FindFirst("operator_id")?.Value,
            user?.FindFirst("preferred_username")?.Value,
            user?.FindFirst(ClaimTypes.Name)?.Value,
            user?.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            "operator-default");

        return (resolvedTenantId, resolvedOperatorId);
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return string.Empty;
    }

    private static bool TryNormalizeArtifactPath(string artifactPath, out string normalizedArtifactPath, out string error)
    {
        if (string.IsNullOrWhiteSpace(artifactPath))
        {
            normalizedArtifactPath = string.Empty;
            error = "artifactName cannot be empty";
            return false;
        }

        var segments = artifactPath
            .Trim()
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            normalizedArtifactPath = string.Empty;
            error = "artifactName is invalid";
            return false;
        }

        if (segments.Any(static segment =>
                string.Equals(segment, ".", StringComparison.Ordinal) ||
                string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            normalizedArtifactPath = string.Empty;
            error = "artifactName is invalid";
            return false;
        }

        normalizedArtifactPath = string.Join('/', segments);
        error = string.Empty;
        return true;
    }

    private static string ResolveArtifactContentType(string artifactPath)
    {
        var extension = Path.GetExtension(artifactPath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "application/octet-stream";
        }

        return extension.ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".md" => "text/markdown; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            ".yaml" or ".yml" => "application/yaml",
            ".xml" => "application/xml",
            _ => "application/octet-stream"
        };
    }

    private static bool TryNormalizeHireId(string hireId, out string normalizedHireId, out string error)
    {
        if (string.IsNullOrWhiteSpace(hireId))
        {
            normalizedHireId = string.Empty;
            error = "hireId 不能为空";
            return false;
        }

        normalizedHireId = hireId.Trim();
        error = string.Empty;
        return true;
    }

    private sealed record ProvisionedSandboxBinding(
        string HireId,
        string SandboxId,
        string State,
        string? GatewayEndpoint);

    private sealed record SandboxGatewayTarget(
        string SandboxId,
        string GatewayEndpoint);

    private sealed record PersistedSourceZipInfo(
        string FileName,
        string StoragePath,
        string ContentHash,
        long SizeBytes);

    private sealed record TemplatePackageUploadResult(
        string HireId,
        string SandboxId,
        string PackageId,
        string PackageVersion,
        string PackageHash,
        string InstalledPath);

    private sealed record DigitalEmployeeUploadResponse(
        bool Success,
        string? Error,
        string? Name,
        int SkillsInstalled,
        IReadOnlyList<string>? InstalledFiles,
        int? TotalSkillsLoaded);

    private sealed record SandboxMcpConfigResponse(
        bool Success,
        string? Message,
        string? Error);

    private sealed record HireOwnerContext(
        string OwnerSubject,
        string TenantId,
        string OperatorId,
        string TemplateId,
        string TemplateName,
        string? EmployeeId);

    private sealed record RemoteCallResult<T>(bool Success, int StatusCode, string Message, T? Data)
    {
        public static RemoteCallResult<T> Ok(T data)
        {
            return new RemoteCallResult<T>(true, 200, string.Empty, data);
        }

        public static RemoteCallResult<T> Failure(int statusCode, string message)
        {
            var normalizedStatusCode = statusCode <= 0 ? 502 : statusCode;
            var normalizedMessage = string.IsNullOrWhiteSpace(message) ? "调用下游服务失败" : message;
            return new RemoteCallResult<T>(false, normalizedStatusCode, normalizedMessage, default);
        }
    }
    private sealed record RemoteBinaryCallResult(bool Success, int StatusCode, string Message, string? FileName, string? ContentType, byte[]? Data)
    {
        public static RemoteBinaryCallResult Ok(string fileName, string contentType, byte[] data)
        {
            return new RemoteBinaryCallResult(true, 200, string.Empty, fileName, contentType, data);
        }

        public static RemoteBinaryCallResult Failure(int statusCode, string message)
        {
            var normalizedStatusCode = statusCode <= 0 ? 502 : statusCode;
            var normalizedMessage = string.IsNullOrWhiteSpace(message) ? "璋冪敤涓嬫父鏈嶅姟澶辫触" : message;
            return new RemoteBinaryCallResult(false, normalizedStatusCode, normalizedMessage, null, null, null);
        }
    }
}
