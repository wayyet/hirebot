using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeTemplate;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.EmployeeTemplate;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.EmployeeTemplate;

public sealed class EmployeeTemplateService(
    ITemplateDataProvider templateDataProvider,
    ILogger<EmployeeTemplateService> logger) : IEmployeeTemplateService
{
    public async Task<ApiResponse<EmployeeTemplateDetailDto>> GetTemplateDetailAsync(
        string templateId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return ApiResponse<EmployeeTemplateDetailDto>.ErrorResponse(400, "templateId 不能为空");
        }

        EmployeeTemplateDefinition? template;
        try
        {
            template = await templateDataProvider.GetByIdAsync(templateId.Trim(), cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Template detail unavailable from upstream data source. TemplateId={TemplateId}", templateId);
            return ApiResponse<EmployeeTemplateDetailDto>.ErrorResponse(502, ex.Message);
        }

        if (template is null || !template.IsAvailable)
        {
            return ApiResponse<EmployeeTemplateDetailDto>.ErrorResponse(404, "模板不存在或已下架");
        }

        return ApiResponse<EmployeeTemplateDetailDto>.SuccessResponse(MapDetail(template));
    }

    private static EmployeeTemplateDetailDto MapDetail(EmployeeTemplateDefinition template)
    {
        return new EmployeeTemplateDetailDto(
            template.TemplateId,
            template.IconUrl,
            template.Name,
            template.Tagline,
            template.Description,
            template.DetailDoc,
            template.CoreAbilities,
            new TemplateResponsibilityBoundaryDto(template.InScope, template.OutOfScope),
            template.Prerequisites,
            template.SuccessCases,
            new TemplateCtaDto("开始雇佣", "/hire"));
    }
}
