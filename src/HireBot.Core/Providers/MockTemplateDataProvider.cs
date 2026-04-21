using HireBot.Abstraction.Models.EmployeeTemplate;
using HireBot.Abstraction.Providers;

namespace HireBot.Core.Providers;

public sealed class MockTemplateDataProvider : ITemplateDataProvider
{
    private static readonly IReadOnlyList<EmployeeTemplateDefinition> Templates =
    [
        new(
            TemplateId: "t001",
            IconUrl: "https://cdn.hirebot.local/icons/sales-followup.svg",
            Name: "销售跟进助理",
            Tagline: "自动追踪商机进度，帮销售团队不漏单",
            Description: "面向 B2B 销售团队，自动归集 CRM 线索、生成跟进建议并同步回写阶段信息。",
            CoreAbilityTags: ["销售", "线索跟进", "CRM", "提醒"],
            HiredCount: 268,
            SuccessRate: 92.4m,
            AvgRating: 4.7m,
            IsAvailable: true,
            CoreAbilities: ["线索优先级评分", "沉默线索唤醒", "阶段推进建议", "跟进纪要回写"],
            InScope: ["识别 72 小时未跟进线索", "按成交概率排序线索池", "自动生成下一步行动建议"],
            OutOfScope: ["最终报价审批", "折扣策略决策", "合同签署责任"],
            Prerequisites:
            [
                new("CRM", "Opportunity.Read", "read", "读取线索与商机状态"),
                new("企业 IM", "Bot.SendMessage", "write", "发送跟进提醒"),
                new("日历", "Calendar.Read", "read", "感知销售排期避免冲突提醒")
            ],
            SuccessCases: ["某 SaaS 团队 30 天内沉默线索占比从 34% 降至 11%"]),

        new(
            TemplateId: "t002",
            IconUrl: "https://cdn.hirebot.local/icons/customer-routing.svg",
            Name: "客服智能分流",
            Tagline: "让人工坐席专注复杂问题，简单问题交给 TA",
            Description: "用于客服中心高并发咨询场景，支持意图分类、常见问题自动回复与无缝转人工。",
            CoreAbilityTags: ["客服", "分流", "FAQ", "转人工"],
            HiredCount: 193,
            SuccessRate: 88.1m,
            AvgRating: 4.5m,
            IsAvailable: true,
            CoreAbilities: ["意图识别", "FAQ 检索", "会话分级", "转人工摘要生成"],
            InScope: ["7x24 处理标准化咨询", "低置信度问题自动升级", "工单自动打标签"],
            OutOfScope: ["退款审批", "价格承诺", "法律纠纷判定"],
            Prerequisites:
            [
                new("工单系统", "Ticket.Write", "write", "创建与更新工单"),
                new("知识库", "KB.Read", "read", "检索 FAQ 与标准答案"),
                new("企业 IM", "Bot.SendMessage", "write", "在群内回传处理结果")
            ],
            SuccessCases: ["某电商平台首周分流准确率达到 81%", "坐席平均响应时长下降 47%"]),

        new(
            TemplateId: "t003",
            IconUrl: "https://cdn.hirebot.local/icons/legal-review.svg",
            Name: "合同审核助理",
            Tagline: "逐条对比红线条款，分钟级输出风险摘要",
            Description: "适合法务初审场景，支持合同结构化解析、条款差异检测和风险等级标注。",
            CoreAbilityTags: ["法务", "合同", "风险识别", "审核"],
            HiredCount: 124,
            SuccessRate: 84.8m,
            AvgRating: 4.4m,
            IsAvailable: true,
            CoreAbilities: ["条款解析", "红线比对", "风险分级", "修订建议生成"],
            InScope: ["提取合同关键字段", "识别与标准模板偏差", "输出结构化风险报告"],
            OutOfScope: ["出具正式法律意见", "替代法务负责人做最终裁决", "处理诉讼文书"],
            Prerequisites:
            [
                new("文档系统", "Document.Read", "read", "读取待审合同原文"),
                new("法务知识库", "Policy.Read", "read", "获取标准模板与红线条款"),
                new("审批系统", "Approval.Notify", "write", "高风险时发起人工复核")
            ],
            SuccessCases: ["某制造企业合同初审平均耗时从 2.7 小时降至 21 分钟"]),

        new(
            TemplateId: "t004",
            IconUrl: "https://cdn.hirebot.local/icons/reporting.svg",
            Name: "数据报表生成员",
            Tagline: "每日自动生成业务报表，再也不用等数据同学",
            Description: "对接 BI 与数据库，定时产出日报/周报，附带异常指标说明。",
            CoreAbilityTags: ["数据分析", "报表", "定时任务", "预警"],
            HiredCount: 217,
            SuccessRate: 90.9m,
            AvgRating: 4.6m,
            IsAvailable: true,
            CoreAbilities: ["多源数据拉取", "可视化报表拼装", "异常波动解释", "定时推送"],
            InScope: ["按模板输出固定报表", "监控核心指标波动", "推送日报到团队群"],
            OutOfScope: ["构建数据仓库", "复杂建模与预测", "处理未授权数据"],
            Prerequisites:
            [
                new("BI 平台", "Dashboard.Read", "read", "读取已配置数据集"),
                new("数据库", "Sql.ReadOnly", "read", "补充查询明细数据"),
                new("企业 IM", "Bot.SendMessage", "write", "推送报表与告警")
            ],
            SuccessCases: ["运营团队日报发布时间从 10:40 提前到 08:35"]),

        new(
            TemplateId: "t005",
            IconUrl: "https://cdn.hirebot.local/icons/recruiting.svg",
            Name: "招聘初筛助理",
            Tagline: "批量解析简历并按 JD 输出优先级",
            Description: "适用于社招和校招高峰，自动提取候选人信息并生成初筛理由。",
            CoreAbilityTags: ["HR", "简历", "JD 匹配", "初筛"],
            HiredCount: 156,
            SuccessRate: 86.5m,
            AvgRating: 4.3m,
            IsAvailable: true,
            CoreAbilities: ["简历解析", "岗位匹配评分", "亮点风险提炼", "邀约建议"],
            InScope: ["按岗位要求打分排序", "输出结构化初筛结论", "批量生成邀约话术"],
            OutOfScope: ["薪资谈判", "终面决策", "背景调查"],
            Prerequisites:
            [
                new("招聘系统", "Candidate.Read", "read", "读取候选人简历"),
                new("岗位库", "Job.Read", "read", "获取岗位 JD 要求"),
                new("邮件系统", "Mail.Send", "write", "发送初筛结果或邀约")
            ],
            SuccessCases: ["HR 团队单日简历处理量提升 3.2 倍"]),

        new(
            TemplateId: "t006",
            IconUrl: "https://cdn.hirebot.local/icons/procurement.svg",
            Name: "内容运营助手",
            Tagline: "多平台内容排期、生成与发布，一个人顶三个",
            Description: "按内容日历自动生成草稿、协调审核并按平台规范发布。",
            CoreAbilityTags: ["内容运营", "排期管理", "多平台发布", "数据回收"],
            HiredCount: 98,
            SuccessRate: 83.7m,
            AvgRating: 4.2m,
            IsAvailable: true,
            CoreAbilities: ["内容草稿生成", "审核流程追踪", "多平台发布", "互动数据回收"],
            InScope: ["按日历生成内容草稿", "追踪审核状态并催办", "发布后回收互动指标"],
            OutOfScope: ["品牌策略制定", "付费广告投放", "舆情危机公关"],
            Prerequisites:
            [
                new("内容平台", "Content.Publish", "write", "按平台规范发布内容"),
                new("素材库", "Asset.Read", "read", "读取可复用素材"),
                new("企业 IM", "Bot.SendMessage", "write", "同步审核和发布状态")
            ],
            SuccessCases: ["内容团队月均产出从 20 篇提升到 63 篇"]),

        new(
            TemplateId: "t007",
            IconUrl: "https://cdn.hirebot.local/icons/finance-reconcile.svg",
            Name: "财务对账助手",
            Tagline: "自动比对流水并输出差异定位",
            Description: "适配多系统对账场景，支持日结差异识别、原因归类和补录建议。",
            CoreAbilityTags: ["财务", "对账", "流水", "差异分析"],
            HiredCount: 131,
            SuccessRate: 89.3m,
            AvgRating: 4.6m,
            IsAvailable: true,
            CoreAbilities: ["多源流水对齐", "差异聚类", "原因追溯", "补录建议"],
            InScope: ["银行与 ERP 对账", "识别缺失单据", "输出待处理清单"],
            OutOfScope: ["记账凭证审批", "税务申报", "财务决算"],
            Prerequisites:
            [
                new("ERP", "Voucher.Read", "read", "读取会计凭证与单据"),
                new("网银系统", "BankTxn.Read", "read", "拉取银行流水"),
                new("通知系统", "Alert.Send", "write", "发送差异处理提醒")
            ],
            SuccessCases: ["月末对账时间从 2 天缩短到 4 小时"]),

        new(
            TemplateId: "t008",
            IconUrl: "https://cdn.hirebot.local/icons/shift-planner.svg",
            Name: "门店排班助理",
            Tagline: "结合客流预测自动生成班表建议",
            Description: "服务连锁门店排班，综合客流、技能等级和工时规则输出排班方案。",
            CoreAbilityTags: ["门店", "排班", "客流预测", "工时"],
            HiredCount: 74,
            SuccessRate: 81.6m,
            AvgRating: 4.1m,
            IsAvailable: true,
            CoreAbilities: ["排班方案生成", "冲突检测", "替班建议", "人效分析"],
            InScope: ["按班次需求生成草案", "识别工时超限", "输出缺员预警"],
            OutOfScope: ["薪资结算", "劳动争议处理", "节假日政策制定"],
            Prerequisites:
            [
                new("考勤系统", "Attendance.Read", "read", "读取员工工时与排班限制"),
                new("销售系统", "Traffic.Read", "read", "获取门店客流预测"),
                new("企业 IM", "Bot.SendMessage", "write", "同步排班变更")
            ],
            SuccessCases: []),

        new(
            TemplateId: "t009",
            IconUrl: "https://cdn.hirebot.local/icons/pm-minute.svg",
            Name: "项目周会纪要官",
            Tagline: "自动沉淀会议结论并追踪行动项",
            Description: "用于项目管理例会，自动生成会议纪要、责任人和截止时间追踪清单。",
            CoreAbilityTags: ["项目管理", "会议纪要", "行动项", "追踪"],
            HiredCount: 203,
            SuccessRate: 91.2m,
            AvgRating: 4.7m,
            IsAvailable: true,
            CoreAbilities: ["语音转文本整理", "结论提炼", "行动项分发", "逾期提醒"],
            InScope: ["生成结构化会议纪要", "同步行动项到协作平台", "提醒逾期任务"],
            OutOfScope: ["替代项目经理做决策", "强制调整排期", "审批预算"],
            Prerequisites:
            [
                new("会议系统", "Meeting.Record.Read", "read", "读取会议录音或文本"),
                new("项目管理平台", "Task.Write", "write", "创建并更新行动项"),
                new("企业 IM", "Bot.SendMessage", "write", "推送纪要摘要")
            ],
            SuccessCases: ["跨团队项目周会行动项按期完成率提升 23%"]),

        new(
            TemplateId: "t010",
            IconUrl: "https://cdn.hirebot.local/icons/public-opinion.svg",
            Name: "舆情巡检助手",
            Tagline: "全网舆情监控并分级预警",
            Description: "支持品牌舆情监测与异常扩散预警，适用于公关应急体系。",
            CoreAbilityTags: ["舆情", "品牌", "监测", "预警"],
            HiredCount: 0,
            SuccessRate: 0m,
            AvgRating: 0m,
            IsAvailable: false,
            CoreAbilities: ["关键词监测", "舆情分级", "传播路径分析", "预警通知"],
            InScope: ["监测核心渠道提及", "对负面舆情分级", "触发预警通知"],
            OutOfScope: ["官方对外发言", "公关策略制定", "法律维权执行"],
            Prerequisites:
            [
                new("舆情平台", "Trend.Read", "read", "获取舆情原始数据"),
                new("企业 IM", "Bot.SendMessage", "write", "发送分级预警消息")
            ],
            SuccessCases: [])
    ];

    public Task<IReadOnlyList<EmployeeTemplateDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Templates);
    }

    public Task<EmployeeTemplateDefinition?> GetByIdAsync(string templateId, CancellationToken cancellationToken = default)
    {
        var template = Templates.FirstOrDefault(item =>
            string.Equals(item.TemplateId, templateId, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(template);
    }
}

