import { useEffect, useMemo, useRef, useState } from "react";
import {
  AlertCircle,
  ArrowLeft,
  Bot,
  CheckCircle2,
  Loader2,
  Settings2,
  Trash2,
  Wifi,
  Link2,
} from "lucide-react";
import { useLocation, useNavigate, useParams } from "react-router-dom";
import { useUxOverlay } from "@/app/context/UxOverlayContext";
import { instanceBasePath } from "@/shared/utils/instancePath";
import { Breadcrumb } from "@/shared/components/Breadcrumb";
import {
  api,
  type EmployeeDetail,
  type ImConfigRequest,
  type ImPlatformId,
} from "@/infra/api";
import {
  fetchFeishuChannelConfig,
  fetchDingTalkChannelConfig,
  fetchWeComChannelConfig,
  updateFeishuChannelConfig,
  updateDingTalkChannelConfig,
  updateWeComChannelConfig,
  deleteFeishuChannelOverride,
  deleteDingTalkChannelOverride,
  deleteWeComChannelOverride,
  type GatewayFeishuChannelConfig,
  type GatewayDingTalkChannelConfig,
  type GatewayWeComChannelConfig,
} from "@/infra/sandbox/sandbox-api";
import {
  firstCharacter,
  ownershipClass,
  ownershipLabel,
  statusClass,
  statusLabel,
  toEmployeeDetailSummary,
  withEmployeeView,
} from "@/features/hiring/pages/employeeView";

type DraftValueKey = Exclude<keyof ImConfigRequest, "connectionMode">;

type ModeSpec = {
  label: string;
  help: string;
  fields: Array<{
    key: DraftValueKey;
    label: string;
    placeholder: string;
    required: boolean;
    type?: "text" | "password" | "number";
    kind?: "input" | "checkbox";
  }>;
};

type PlatformSchema = {
  label: string;
  accent: "blue" | "orange" | "green";
  intro: string;
  mode: ModeSpec;
};

const PLATFORM_ORDER: ImPlatformId[] = ["feishu", "dingtalk", "wecom"];

const PLATFORM_SCHEMAS: Record<ImPlatformId, PlatformSchema> = {
  feishu: {
    label: "飞书",
    accent: "blue",
    intro: "飞书使用WebSocket模式。",
    mode: {
      label: "WebSocket",
      help: "使用WebSocket模式进行飞书事件流传输。",
      fields: [
        { key: "appId", label: "应用ID", placeholder: "飞书 app_id", required: true },
        { key: "appSecret", label: "应用密钥", placeholder: "飞书 app_secret", required: true, type: "password" },
      ],
    },
  },
  dingtalk: {
    label: "钉钉",
    accent: "orange",
    intro: "钉钉只需要App ID、App Key和App Secret。",
    mode: {
      label: "Stream",
      help: "钉钉Stream模式需要三个凭证。",
      fields: [
        { key: "appId", label: "应用ID", placeholder: "钉钉 App ID", required: true },
        { key: "appKey", label: "应用密钥", placeholder: "钉钉 App Key / Client ID", required: true },
        { key: "appSecret", label: "应用密钥", placeholder: "钉钉 App Secret", required: true, type: "password" },
      ],
    },
  },
  wecom: {
    label: "企业微信",
    accent: "green",
    intro: "企业微信使用智能机器人长连接模式。",
    mode: {
      label: "长连接",
      help: "企业微信智能机器人仅需Bot ID和Bot Secret。",
      fields: [
        { key: "botId", label: "Bot ID", placeholder: "企业微信智能机器人 Bot ID", required: true },
        { key: "botSecret", label: "Bot Secret", placeholder: "企业微信智能机器人 Bot Secret", required: true, type: "password" },
      ],
    },
  },
};

const DEFAULT_DRAFT = (): ImConfigRequest => ({
  connectionMode: "websocket",
  appId: "",
  appSecret: "",
  appKey: "",
  appIdRef: "",
  appKeyRef: "",
  appSecretRef: "",
  encryptKey: "",
  token: "",
  aesKey: "",
  verificationToken: "",
  corpId: "",
  agentId: "",
  agentSecret: "",
  botId: "",
  botSecret: "",
  robotCode: "",
  robotCodeRef: "",
  groupPolicy: "",
  allowedFromUserIds: "",
  allowedGroupIds: "",
  maxInboundChars: "",
  requireMentionInGroup: "",
  exposeInboundMediaUrls: "",
  streamPollIntervalMs: "",
});

function draftFromFeishuEffectiveConfig(
  config: GatewayFeishuChannelConfig | null,
): ImConfigRequest {
  const draft = DEFAULT_DRAFT();
  if (!config) return draft;
  return {
    ...draft,
    appId: config.appId ?? "",
    appSecret: config.appSecret ?? "",
    appIdRef: config.appIdRef ?? "",
    appSecretRef: config.appSecretRef ?? "",
  };
}

function draftFromDingTalkEffectiveConfig(
  config: GatewayDingTalkChannelConfig | null,
): ImConfigRequest {
  const draft = DEFAULT_DRAFT();
  if (!config) return draft;
  return {
    ...draft,
    appId: config.appId ?? "",
    appKey: config.appKey ?? "",
    appSecret: config.appSecret ?? "",
  };
}

function draftFromWeComEffectiveConfig(
  config: GatewayWeComChannelConfig | null,
): ImConfigRequest {
  const draft = DEFAULT_DRAFT();
  if (!config) return draft;
  return {
    ...draft,
    botId: config.botId ?? "",
    botSecret: config.botSecret ?? "",
  };
}

function statusTone(configured: boolean): "green" | "gray" {
  return configured ? "green" : "gray";
}

function statusText(configured: boolean) {
  return configured ? "已配置" : "未配置";
}

function fieldType(key: DraftValueKey): "text" | "password" {
  if (key === "appSecret" || key === "botSecret") return "password";
  return "text";
}

function isPersonalAssetOwnership(ownership?: string | null) {
  return ownership === "personal_clone" || ownership === "private_branch";
}

export default function InstanceImConfigPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const { showToast } = useUxOverlay();

  const [employee, setEmployee] = useState<EmployeeDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [selectedPlatform, setSelectedPlatform] = useState<ImPlatformId>("feishu");
  const [configuredPlatforms, setConfiguredPlatforms] = useState<Record<ImPlatformId, boolean>>({
    feishu: false,
    dingtalk: false,
    wecom: false,
  });
  const [drafts, setDrafts] = useState<Record<ImPlatformId, ImConfigRequest>>({
    feishu: DEFAULT_DRAFT(),
    dingtalk: DEFAULT_DRAFT(),
    wecom: DEFAULT_DRAFT(),
  });

  const employeeView = useMemo(() => {
    if (!employee) return null;
    return withEmployeeView(toEmployeeDetailSummary(employee));
  }, [employee]);

  const isPersonalAsset = employeeView
    ? isPersonalAssetOwnership(employeeView.ownership)
    : false;

  const currentSchema = PLATFORM_SCHEMAS[selectedPlatform];
  const currentModeSpec = currentSchema.mode;
  const isCurrentConfigured = configuredPlatforms[selectedPlatform];
  const configuredCount = PLATFORM_ORDER.filter((p) => configuredPlatforms[p]).length;
  const gatewayEndpointRef = useRef<string | null>(null);

  async function loadPage() {
    if (!id) return;
    setLoading(true);
    setError("");
    setNotice("");
    try {
      const [detail, gatewayEndpoint] = await Promise.all([
        api.employeeRuntime.getEmployee(id),
        api.employeeRuntime.getSandboxGatewayEndpoint(id),
      ]);

      setEmployee(detail);

      if (!gatewayEndpoint) {
        setError("沙箱网关端点未就绪");
        return;
      }

      gatewayEndpointRef.current = gatewayEndpoint;

      const [feishuEffective, dingTalkEffective, wecomEffective] = await Promise.all([
        fetchFeishuChannelConfig(gatewayEndpoint).catch(() => null),
        fetchDingTalkChannelConfig(gatewayEndpoint).catch(() => null),
        fetchWeComChannelConfig(gatewayEndpoint).catch(() => null),
      ]);

      setDrafts({
        feishu: draftFromFeishuEffectiveConfig(feishuEffective),
        dingtalk: draftFromDingTalkEffectiveConfig(dingTalkEffective),
        wecom: draftFromWeComEffectiveConfig(wecomEffective),
      });

      const nextConfigured: Record<ImPlatformId, boolean> = {
        feishu: feishuEffective?.appId != null && feishuEffective.appId !== "",
        dingtalk: dingTalkEffective?.enabled === true && dingTalkEffective.appId != null && dingTalkEffective.appId !== "",
        wecom: wecomEffective?.enabled === true && wecomEffective.botId != null && wecomEffective.botId !== "",
      };
      setConfiguredPlatforms(nextConfigured);

      const firstActive = PLATFORM_ORDER.find((p) => nextConfigured[p]);
      if (firstActive) setSelectedPlatform(firstActive);
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : "加载IM配置失败");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void loadPage(); }, [id]);

  function updateField(key: DraftValueKey, value: string) {
    setDrafts((prev) => ({ ...prev, [selectedPlatform]: { ...prev[selectedPlatform], [key]: value } }));
  }

  function updateCheckboxField(key: DraftValueKey, checked: boolean) {
    updateField(key, checked ? "true" : "false");
  }

  function selectPlatform(platform: ImPlatformId) {
    setSelectedPlatform(platform);
  }

  async function refreshConfigs() {
    const endpoint = gatewayEndpointRef.current;
    if (!endpoint) return;
    const [feishuEffective, dingTalkEffective, wecomEffective] = await Promise.all([
      fetchFeishuChannelConfig(endpoint).catch(() => null),
      fetchDingTalkChannelConfig(endpoint).catch(() => null),
      fetchWeComChannelConfig(endpoint).catch(() => null),
    ]);
    setDrafts({
      feishu: draftFromFeishuEffectiveConfig(feishuEffective),
      dingtalk: draftFromDingTalkEffectiveConfig(dingTalkEffective),
      wecom: draftFromWeComEffectiveConfig(wecomEffective),
    });
    setConfiguredPlatforms({
      feishu: feishuEffective?.appId != null && feishuEffective.appId !== "",
      dingtalk: dingTalkEffective?.enabled === true && dingTalkEffective.appId != null && dingTalkEffective.appId !== "",
      wecom: wecomEffective?.enabled === true && wecomEffective.botId != null && wecomEffective.botId !== "",
    });
  }

  async function saveConfig() {
    if (!id) return;
    if (!isPersonalAsset) { showToast("部门成员无法配置IM。请先创建个人分身。", "error"); return; }

    const endpoint = gatewayEndpointRef.current;
    if (!endpoint) { showToast("沙箱网关端点未就绪", "error"); return; }

    const draft = drafts[selectedPlatform];
    const missing = currentModeSpec.fields.find((f) => f.required && !(draft[f.key] ?? "").trim());
    if (missing) { showToast(`${missing.label} 是必填项`, "error"); return; }

    setSaving(true);
    setError("");
    setNotice("");
    try {
      let result: { message?: string | null; success: boolean };
      if (selectedPlatform === "feishu") {
        result = await updateFeishuChannelConfig(endpoint, {
          enabled: true,
          appId: draft.appId?.trim() || null,
          appSecret: draft.appSecret?.trim() || null,
          appIdRef: "env:FEISHU_APP_ID",
          appSecretRef: "env:FEISHU_APP_SECRET",
          groupPolicy: "open",
          allowedFromUserIds: [],
        });
      } else if (selectedPlatform === "dingtalk") {
        result = await updateDingTalkChannelConfig(endpoint, {
          enabled: true,
          appId: draft.appId?.trim() || null,
          appKey: draft.appKey?.trim() || null,
          appSecret: draft.appSecret?.trim() || null,
        });
      } else {
        result = await updateWeComChannelConfig(endpoint, {
          enabled: true,
          botId: draft.botId?.trim() || null,
          botSecret: draft.botSecret?.trim() || null,
          botIdRef: "env:WECOM_BOT_ID",
          botSecretRef: "env:WECOM_BOT_SECRET",
        });
      }
      setNotice(`${currentSchema.label} - ${result.message ?? "配置已保存"}`);
      showToast(`${currentSchema.label} 已保存`, "success");
      await refreshConfigs();
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : "保存IM配置失败");
    } finally {
      setSaving(false);
    }
  }

  async function deleteConfig() {
    if (!id) return;
    if (!isPersonalAsset) { showToast("部门成员无法配置IM。请先创建个人分身。", "error"); return; }

    const endpoint = gatewayEndpointRef.current;
    if (!endpoint) { showToast("沙箱网关端点未就绪", "error"); return; }

    if (!window.confirm(`确定要移除 ${currentSchema.label} 配置吗？`)) return;

    setSaving(true);
    setError("");
    try {
      if (selectedPlatform === "feishu") {
        await deleteFeishuChannelOverride(endpoint);
      } else if (selectedPlatform === "dingtalk") {
        await deleteDingTalkChannelOverride(endpoint);
      } else {
        await deleteWeComChannelOverride(endpoint);
      }
      setNotice(`${currentSchema.label} 配置已移除`);
      showToast(`${currentSchema.label} 已解绑`, "success");
      setDrafts((prev) => ({ ...prev, [selectedPlatform]: DEFAULT_DRAFT() }));
      await refreshConfigs();
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : "删除IM配置失败");
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return (
      <div className="hb-page">
        <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[#737373]">
          <Loader2 size={16} className="animate-spin" /> 正在加载IM配置...
        </div>
      </div>
    );
  }

  if (!employee || !employeeView) {
    return (
      <div className="hb-page space-y-4">
        <button type="button" onClick={() => navigate("/department-employees")} className="hb-btn-ghost">
          <ArrowLeft size={14} /> 返回列表
        </button>
        <div className="hb-card p-8 text-sm text-[#737373]">实例不存在</div>
      </div>
    );
  }

  return (
    <div className="hb-page space-y-5">
      <Breadcrumb items={[{ label: '员工详情', to: instanceBasePath(location.pathname, employee.employeeId) }, { label: 'IM 配置' }]} />

      {error ? <div className="hb-alert hb-alert-error"><AlertCircle size={14} /><span>{error}</span></div> : null}
      {notice ? <div className="hb-alert hb-alert-success"><CheckCircle2 size={14} /><span>{notice}</span></div> : null}

      <section className="hb-card p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <span className="hb-kicker">多平台IM</span>
            <h1 className="hb-page-title">IM配置 - {employee.nickname}</h1>
            <p className="hb-page-copy">配置各平台IM设置，绑定飞书/钉钉/企业微信。</p>
          </div>
          <div className="flex flex-col items-end gap-2">
            <span className={`hb-pill ${statusClass(employeeView.mappedStatus, employeeView.lifecycleStatus)}`}>
              {statusLabel(employeeView.mappedStatus, employeeView.lifecycleStatus)}
            </span>
            <span className={`hb-pill ${ownershipClass(employeeView.ownership)}`}>
              {ownershipLabel(employeeView.ownership)}
            </span>
          </div>
        </div>
      </section>

      {!isPersonalAsset ? (
        <section className="hb-card p-6">
          <div className="flex items-start justify-between gap-4">
            <div>
              <div className="flex items-center gap-2 text-base font-semibold text-[#0a0a0a]">
                <Bot size={16} /> 部门成员不支持IM配置
              </div>
              <p className="mt-2 max-w-2xl text-sm leading-relaxed text-[#737373]">
                IM设置仅适用于个人分身和私有分支。请先创建一个，然后在此绑定频道。
              </p>
            </div>
            <button type="button" className="hb-btn-primary" onClick={() => navigate(`/clone/${employee.employeeId}`)}>
              Create clone
            </button>
          </div>
        </section>
      ) : (
        <>
          <section className="hb-stat-grid">
            <div className="hb-stat-card"><div className="hb-stat-label">已配置平台</div><div className="hb-stat-value">{configuredCount}</div></div>
            <div className="hb-stat-card"><div className="hb-stat-label">Current platform</div><div className="hb-stat-value">{currentSchema.label}</div></div>
            <div className="hb-stat-card"><div className="hb-stat-label">当前模式</div><div className="hb-stat-value">{currentModeSpec.label}</div></div>
          </section>

          <section className="hb-section">
            <div className="hb-section-head">
              <div>
                <h2 className="hb-section-title">平台选择</h2>
                <p className="hb-section-copy">每个平台独立存储，不会互相覆盖。</p>
              </div>
            </div>

            <div className="grid gap-4 xl:grid-cols-3">
              {PLATFORM_ORDER.map((platform) => {
                const schema = PLATFORM_SCHEMAS[platform];
                const configured = configuredPlatforms[platform];
                const active = selectedPlatform === platform;
                const tone = statusTone(configured);
                const modeLabel = schema.mode.label;

                return (
                  <button
                    key={platform}
                    type="button"
                    onClick={() => selectPlatform(platform)}
                    className={`hb-card p-5 text-left transition-transform duration-150 hover:-translate-y-0.5 ${active ? "ring-2 ring-[#4a6cf7]/20" : ""}`}
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <div className="flex items-center gap-2 text-base font-semibold text-[#0a0a0a]">
                          <span className={`hb-squircle h-8 w-8 ${
                            schema.accent === "blue" ? "bg-[#dde9ff] text-[#3d5cff]"
                            : schema.accent === "orange" ? "bg-[#fff0df] text-[#b45309]"
                            : "bg-[#e7f9ee] text-[#15803d]"
                          }`}>{firstCharacter(schema.label)}</span>
                          {schema.label}
                        </div>
                        <p className="mt-2 text-sm leading-relaxed text-[#737373]">{schema.intro}</p>
                      </div>
                      <span className={`hb-pill ${tone}`}>{statusText(configured)}</span>
                    </div>
                    <div className="mt-4 flex flex-wrap gap-2">
                      <span className="hb-pill gray">{modeLabel}</span>
                    </div>
                  </button>
                );
              })}
            </div>
          </section>

          <section className="hb-detail-split">
            <div className="hb-card hb-detail-panel">
              <div className="hb-detail-section-head">
                <div>
                  <h2 className="hb-section-heading !mb-0">{currentSchema.label} config</h2>
                  <p className="mt-2 text-sm text-[#737373]">{currentSchema.intro}</p>
                </div>
                <span className={`hb-pill ${statusTone(isCurrentConfigured)}`}>{statusText(isCurrentConfigured)}</span>
              </div>

              <div className="mt-4 hb-callout info">{currentModeSpec.help}</div>

              <div className="mt-5 grid gap-4 md:grid-cols-2">
                {currentModeSpec.fields.map((field) => (
                  <label key={field.key} className="hb-field md:col-span-1">
                    <span className="hb-field-label">{field.label} {field.required ? "*" : ""}</span>
                    {field.kind === "checkbox" ? (
                      <label className="flex items-center gap-2 rounded-xl border border-[#ececec] bg-white px-3 py-3 text-sm text-[#404040]">
                        <input type="checkbox" checked={String(drafts[selectedPlatform][field.key] ?? "") === "true"}
                          onChange={(e) => updateCheckboxField(field.key, e.target.checked)} disabled={saving}
                          className="h-4 w-4 rounded border-[#d4d4d8] text-[#2563eb]" />
                        <span>{field.placeholder || field.label}</span>
                      </label>
                    ) : (
                      <input type={field.type ?? fieldType(field.key)} value={drafts[selectedPlatform][field.key] ?? ""}
                        onChange={(e) => updateField(field.key, e.target.value)} className="hb-input"
                        placeholder={field.placeholder} disabled={saving} />
                    )}
                    {field.required ? null : <span className="hb-field-help">可选，稍后可从管理控制台填写。</span>}
                  </label>
                ))}
              </div>

              <div className="mt-5 flex flex-wrap justify-end gap-2">
                <button type="button" className="hb-btn-ghost" onClick={() => navigate(instanceBasePath(location.pathname, employee.employeeId))}>取消</button>
                <button type="button" className="hb-btn-ghost" onClick={() => void deleteConfig()} disabled={saving || !isCurrentConfigured}>
                  <Trash2 size={14} /> 解绑
                </button>
                <button type="button" className="hb-btn-primary" onClick={() => void saveConfig()} disabled={saving}>
                  {saving ? <Loader2 size={14} className="animate-spin" /> : <Settings2 size={14} />}
                  {isCurrentConfigured ? "保存并刷新" : "保存配置"}
                </button>
              </div>
            </div>

            <div className="hb-card hb-detail-panel">
              <h2 className="hb-section-heading">集成指南</h2>
              <div className="space-y-3">
                <div className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
                  <div className="text-sm font-semibold text-[#0a0a0a]">1. 选择平台</div>
                  <div className="mt-1 text-sm text-[#737373]">飞书、钉钉和企业微信独立存储，不会互相覆盖。</div>
                </div>
                <div className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
                  <div className="text-sm font-semibold text-[#0a0a0a]">2. 填写凭证</div>
                  <div className="mt-1 text-sm text-[#737373]">完成必填字段，后端将验证并加密它们。</div>
                </div>
              </div>

              <div className="mt-5 hb-callout info">
                <Wifi size={16} />
                <div>
                  <div className="font-semibold text-[#0a0a0a]">应用内聊天和IM是独立的</div>
                  <div className="mt-1 text-sm text-[#404040]">此页面仅处理平台绑定。配置后，您仍然可以从实例详情页进入应用内聊天或直接使用IM频道。</div>
                </div>
              </div>

              <div className="mt-5 rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
                <div className="flex items-center gap-2 text-sm font-semibold text-[#0a0a0a]"><Link2 size={14} />当前实例信息</div>
                <div className="mt-3 grid gap-3 text-sm text-[#404040]">
                  <div className="flex items-center justify-between gap-3"><span>实例名称</span><span className="font-medium text-[#0a0a0a]">{employee.nickname}</span></div>
                  <div className="flex items-center justify-between gap-3"><span>实例ID</span><span className="font-medium text-[#0a0a0a]">{employee.employeeId}</span></div>
                  <div className="flex items-center justify-between gap-3"><span>部门</span><span className="font-medium text-[#0a0a0a]">{employee.departmentId || employee.owningTeam}</span></div>
                  <div className="flex items-center justify-between gap-3"><span>状态</span><span className="font-medium text-[#0a0a0a]">{statusLabel(employeeView.mappedStatus, employeeView.lifecycleStatus)}</span></div>
                </div>
              </div>
            </div>
          </section>
        </>
      )}
    </div>
  );
}
