import { useEffect, useMemo, useState } from "react";
import {
  AlertCircle,
  ArrowLeft,
  Bot,
  CheckCircle2,
  Copy,
  Loader2,
  RefreshCw,
  Settings2,
  ShieldCheck,
  Trash2,
  Wifi,
  Link2,
} from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import { useUxOverlay } from "@/app/context/UxOverlayContext";
import { Breadcrumb } from "@/shared/components/Breadcrumb";
import {
  api,
  type DingTalkChannelConfigRequest,
  type DingTalkChannelEffectiveConfig,
  type EmployeeDetail,
  type FeishuChannelEffectiveConfig,
  type ImConfigItem,
  type ImConfigRequest,
  type ImConnectionMode,
  type ImPlatformId,
} from "@/infra/api";
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

type PlatformSchema = {
  label: string;
  accent: "blue" | "orange" | "green";
  intro: string;
  defaultMode: ImConnectionMode;
  modes: Record<
    ImConnectionMode,
    {
      label: string;
      help: string;
      allowed: boolean;
      fields: Array<{
        key: DraftValueKey;
        label: string;
        placeholder: string;
        required: boolean;
        type?: "text" | "password" | "number";
        kind?: "input" | "checkbox";
      }>;
    }
  >;
};

const PLATFORM_ORDER: ImPlatformId[] = ["feishu", "dingtalk", "wecom"];

const PLATFORM_SCHEMAS: Record<ImPlatformId, PlatformSchema> = {
  feishu: {
    label: "飞书",
    accent: "blue",
    intro: "飞书支持WebSocket和URL回调模式。",
    defaultMode: "websocket",
    modes: {
      websocket: {
        label: "WebSocket",
        help: "使用WebSocket模式进行飞书事件流传输。",
        allowed: true,
        fields: [
          {
            key: "appId",
            label: "应用ID",
            placeholder: "飞书 app_id",
            required: true,
          },
          {
            key: "appSecret",
            label: "应用密钥",
            placeholder: "飞书 app_secret",
            required: true,
            type: "password",
          },
        ],
      },
      url_callback: {
        label: "URL回调",
        help: "URL回调需要Encrypt Key；Verification Token是可选的。",
        allowed: true,
        fields: [
          {
            key: "appId",
            label: "应用ID",
            placeholder: "飞书 app_id",
            required: true,
          },
          {
            key: "appSecret",
            label: "应用密钥",
            placeholder: "飞书 app_secret",
            required: true,
            type: "password",
          },
          {
            key: "encryptKey",
            label: "加密密钥",
            placeholder: "Encrypt Key",
            required: true,
            type: "password",
          },
          {
            key: "verificationToken",
            label: "验证令牌",
            placeholder: "可选的验证令牌",
            required: false,
            type: "password",
          },
        ],
      },
    },
  },
  dingtalk: {
    label: "钉钉",
    accent: "orange",
    intro: "钉钉只需要App ID、App Key和App Secret。",
    defaultMode: "websocket",
    modes: {
      websocket: {
        label: "Stream",
        help: "钉钉Stream模式只需要三个凭证。",
        allowed: true,
        fields: [
          {
            key: "appId",
            label: "应用ID",
            placeholder: "钉钉 App ID",
            required: true,
          },
          {
            key: "appKey",
            label: "应用密钥",
            placeholder: "钉钉 App Key / Client ID",
            required: true,
          },
          {
            key: "appSecret",
            label: "应用密钥",
            placeholder: "钉钉 App Secret",
            required: true,
            type: "password",
          },
        ],
      },
      url_callback: {
        label: "URL回调",
        help: "本项目中钉钉使用Stream模式。",
        allowed: false,
        fields: [],
      },
    },
  },
  wecom: {
    label: "企业微信",
    accent: "green",
    intro: "企业微信目前仅支持URL回调模式。",
    defaultMode: "url_callback",
    modes: {
      websocket: {
        label: "WebSocket",
        help: "企业微信在此不支持WebSocket模式。",
        allowed: false,
        fields: [],
      },
      url_callback: {
        label: "URL回调",
        help: "企业微信需要corpId、agentId、agentSecret、token和AES密钥。",
        allowed: true,
        fields: [
          {
            key: "corpId",
            label: "企业ID",
            placeholder: "企业微信 CorpID",
            required: true,
          },
          {
            key: "agentId",
            label: "应用ID",
            placeholder: "企业微信 AgentID",
            required: true,
          },
          {
            key: "agentSecret",
            label: "应用密钥",
            placeholder: "企业微信 Secret",
            required: true,
            type: "password",
          },
          {
            key: "token",
            label: "令牌",
            placeholder: "回调令牌",
            required: true,
            type: "password",
          },
          {
            key: "aesKey",
            label: "AES密钥",
            placeholder: "EncodingAESKey",
            required: true,
            type: "password",
          },
        ],
      },
    },
  },
};

const DEFAULT_DRAFT = (platform: ImPlatformId): ImConfigRequest => ({
  connectionMode: PLATFORM_SCHEMAS[platform].defaultMode,
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

function draftFromConfig(
  platform: ImPlatformId,
  config: ImConfigItem | null,
): ImConfigRequest {
  const draft = DEFAULT_DRAFT(platform);
  if (!config) return draft;

  return {
    ...draft,
    connectionMode:
      config.connectionMode === "websocket" ||
      config.connectionMode === "url_callback"
        ? config.connectionMode
        : draft.connectionMode,
    appId: config.appId ?? "",
    appSecret: config.appSecret ?? "",
    appKey: config.appKey ?? "",
    appIdRef: config.appIdRef ?? "",
    appKeyRef: config.appKeyRef ?? "",
    appSecretRef: config.appSecretRef ?? "",
    encryptKey: config.encryptKey ?? "",
    token: config.token ?? "",
    aesKey: config.aesKey ?? "",
    verificationToken: config.verificationToken ?? "",
    corpId: config.corpId ?? "",
    agentId: config.agentId ?? "",
    agentSecret: config.agentSecret ?? "",
    robotCode: config.robotCode ?? "",
    robotCodeRef: config.robotCodeRef ?? "",
    groupPolicy: config.groupPolicy ?? "",
    allowedFromUserIds: config.allowedFromUserIds
      ? config.allowedFromUserIds.join(",")
      : "",
    allowedGroupIds: config.allowedGroupIds
      ? config.allowedGroupIds.join(",")
      : "",
    maxInboundChars:
      config.maxInboundChars != null ? String(config.maxInboundChars) : "",
    requireMentionInGroup:
      config.requireMentionInGroup != null
        ? String(config.requireMentionInGroup)
        : "",
    exposeInboundMediaUrls:
      config.exposeInboundMediaUrls != null
        ? String(config.exposeInboundMediaUrls)
        : "",
    streamPollIntervalMs:
      config.streamPollIntervalMs != null
        ? String(config.streamPollIntervalMs)
        : "",
  };
}

function draftFromFeishuEffectiveConfig(
  config: FeishuChannelEffectiveConfig | null,
): ImConfigRequest {
  const draft = DEFAULT_DRAFT("feishu");
  if (!config) return draft;

  return {
    ...draft,
    appId: config.appId ?? "",
    appSecret: config.appSecret ?? "",
    appIdRef: config.appIdRef ?? "",
    appSecretRef: config.appSecretRef ?? "",
    connectionMode: config.connectionMode ?? draft.connectionMode,
  };
}

function draftFromDingTalkEffectiveConfig(
  config: DingTalkChannelEffectiveConfig | null,
): ImConfigRequest {
  const draft = DEFAULT_DRAFT("dingtalk");
  if (!config) return draft;

  return {
    ...draft,
    appId: config.appId ?? "",
    appKey: config.appKey ?? "",
    appSecret: config.appSecret ?? "",
  };
}

function buildDingTalkConfigRequest(
  draft: ImConfigRequest,
): DingTalkChannelConfigRequest {
  return {
    enabled: true,
    appId: draft.appId?.trim() || null,
    appKey: draft.appKey?.trim() || null,
    appSecret: draft.appSecret?.trim() || null,
  };
}

function toConfigMap(items: ImConfigItem[]) {
  return PLATFORM_ORDER.reduce<Record<ImPlatformId, ImConfigItem | null>>(
    (acc, platform) => {
      acc[platform] = items.find((item) => item.platform === platform) ?? null;
      return acc;
    },
    { feishu: null, dingtalk: null, wecom: null },
  );
}

function statusTone(item: ImConfigItem | null): "green" | "orange" | "gray" {
  if (!item) return "gray";
  if (item.lastError) return "orange";
  if (item.status === "active") return "green";
  return "gray";
}

function statusText(item: ImConfigItem | null) {
  if (!item) return "未配置";
  if (item.lastError) return "配置错误";
  if (item.status === "active") return "已连接";
  return item.status;
}

function fieldType(key: DraftValueKey): "text" | "password" {
  if (
    key === "appSecret" ||
    key === "encryptKey" ||
    key === "token" ||
    key === "aesKey" ||
    key === "verificationToken" ||
    key === "agentSecret"
  ) {
    return "password";
  }
  return "text";
}

function isPersonalAssetOwnership(ownership?: string | null) {
  return ownership === "personal_clone" || ownership === "private_branch";
}

export default function InstanceImConfigPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { showToast } = useUxOverlay();

  const [employee, setEmployee] = useState<EmployeeDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [selectedPlatform, setSelectedPlatform] =
    useState<ImPlatformId>("feishu");
  const [configMap, setConfigMap] = useState<
    Record<ImPlatformId, ImConfigItem | null>
  >({
    feishu: null,
    dingtalk: null,
    wecom: null,
  });
  const [drafts, setDrafts] = useState<Record<ImPlatformId, ImConfigRequest>>({
    feishu: DEFAULT_DRAFT("feishu"),
    dingtalk: DEFAULT_DRAFT("dingtalk"),
    wecom: DEFAULT_DRAFT("wecom"),
  });
  const [webhookUrl, setWebhookUrl] = useState("");
  const [webhookLoading, setWebhookLoading] = useState(false);

  const employeeView = useMemo(() => {
    if (!employee) return null;
    return withEmployeeView(toEmployeeDetailSummary(employee));
  }, [employee]);

  const isPersonalAsset = employeeView
    ? isPersonalAssetOwnership(employeeView.ownership)
    : false;
  const currentSchema = PLATFORM_SCHEMAS[selectedPlatform];
  const currentMode = drafts[selectedPlatform].connectionMode;
  const currentModeSchema = currentSchema.modes[currentMode];
  const currentConfig = configMap[selectedPlatform];
  const configuredCount = PLATFORM_ORDER.filter(
    (platform) => configMap[platform]?.status === "active",
  ).length;

  async function loadPage() {
    if (!id) return;

    setLoading(true);
    setError("");
    setNotice("");
    try {
      const [detail, configs, feishuEffective, dingTalkEffective] =
        await Promise.all([
          api.employeeRuntime.getEmployee(id),
          api.employeeRuntime.getImConfigs(id),
          api.employeeRuntime.getFeishuEffectiveImConfig(id).catch(() => null),
          api.employeeRuntime
            .getDingTalkEffectiveImConfig(id)
            .catch(() => null),
        ]);

      const nextConfigMap = toConfigMap(configs.configs);
      setEmployee(detail);
      setConfigMap(nextConfigMap);
      setDrafts((previous) => ({
        ...previous,
        feishu: draftFromFeishuEffectiveConfig(feishuEffective),
        dingtalk: draftFromDingTalkEffectiveConfig(dingTalkEffective),
        wecom: nextConfigMap.wecom
          ? draftFromConfig("wecom", nextConfigMap.wecom)
          : previous.wecom,
      }));

      const firstActive = PLATFORM_ORDER.find(
        (platform) => nextConfigMap[platform]?.status === "active",
      );
      if (firstActive) {
        setSelectedPlatform(firstActive);
      }
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error ? requestError.message : "加载IM配置失败",
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void loadPage();
  }, [id]);

  useEffect(() => {
    if (!id || !employeeView || !isPersonalAsset) {
      setWebhookUrl("");
      setWebhookLoading(false);
      return;
    }

    let cancelled = false;
    setWebhookLoading(true);
    api.employeeRuntime
      .getImWebhookUrl(id, selectedPlatform)
      .then((result) => {
        if (!cancelled) setWebhookUrl(result.webhookUrl);
      })
      .catch(() => {
        if (!cancelled) setWebhookUrl("");
      })
      .finally(() => {
        if (!cancelled) setWebhookLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [id, employeeView, isPersonalAsset, selectedPlatform]);

  useEffect(() => {
    if (!currentModeSchema.allowed) {
      setDrafts((previous) => ({
        ...previous,
        [selectedPlatform]: {
          ...previous[selectedPlatform],
          connectionMode: currentSchema.defaultMode,
        },
      }));
    }
  }, [currentModeSchema.allowed, currentSchema.defaultMode, selectedPlatform]);

  function updateField(key: DraftValueKey, value: string) {
    setDrafts((previous) => ({
      ...previous,
      [selectedPlatform]: {
        ...previous[selectedPlatform],
        [key]: value,
      },
    }));
  }

  function updateCheckboxField(key: DraftValueKey, checked: boolean) {
    updateField(key, checked ? "true" : "false");
  }

  function changeMode(mode: ImConnectionMode) {
    if (!currentSchema.modes[mode].allowed) return;

    setDrafts((previous) => ({
      ...previous,
      [selectedPlatform]: {
        ...previous[selectedPlatform],
        connectionMode: mode,
      },
    }));
  }

  function selectPlatform(platform: ImPlatformId) {
    setSelectedPlatform(platform);
  }

  async function refreshConfigs() {
    if (!id) return;

    const [configs, feishuEffective, dingTalkEffective] = await Promise.all([
      api.employeeRuntime.getImConfigs(id),
      api.employeeRuntime.getFeishuEffectiveImConfig(id).catch(() => null),
      api.employeeRuntime.getDingTalkEffectiveImConfig(id).catch(() => null),
    ]);

    const nextConfigMap = toConfigMap(configs.configs);
    setConfigMap(nextConfigMap);
    setDrafts((previous) => ({
      ...previous,
      feishu: draftFromFeishuEffectiveConfig(feishuEffective),
      dingtalk: draftFromDingTalkEffectiveConfig(dingTalkEffective),
      wecom: nextConfigMap.wecom
        ? draftFromConfig("wecom", nextConfigMap.wecom)
        : previous.wecom,
    }));
  }

  async function saveConfig() {
    if (!id) return;
    if (!isPersonalAsset) {
      showToast("部门成员无法配置IM。请先创建个人分身。", "error");
      return;
    }

    const draft = drafts[selectedPlatform];
    const requiredFields = currentModeSchema.fields.filter(
      (field) => field.required,
    );
    const missing = requiredFields.find(
      (field) => !(draft[field.key] ?? "").trim(),
    );
    if (missing) {
      showToast(`${missing.label} 是必填项`, "error");
      return;
    }

    setSaving(true);
    setError("");
    setNotice("");
    try {
      const result =
        selectedPlatform === "dingtalk"
          ? await api.employeeRuntime.updateDingTalkImConfig(
              id,
              buildDingTalkConfigRequest(draft),
            )
          : await api.employeeRuntime.upsertImConfig(
              id,
              selectedPlatform,
              draft,
            );

      setNotice(`${currentSchema.label} - ${result.message}`);
      showToast(`${currentSchema.label} 已保存`, "success");
      await refreshConfigs();
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error ? requestError.message : "保存IM配置失败",
      );
    } finally {
      setSaving(false);
    }
  }

  async function deleteConfig() {
    if (!id) return;
    if (!isPersonalAsset) {
      showToast("部门成员无法配置IM。请先创建个人分身。", "error");
      return;
    }

    if (!window.confirm(`确定要移除 ${currentSchema.label} 配置吗？`)) {
      return;
    }

    setSaving(true);
    setError("");
    try {
      if (selectedPlatform === "dingtalk") {
        await api.employeeRuntime.deleteDingTalkImConfig(id);
      } else {
        await api.employeeRuntime.deleteImConfig(id, selectedPlatform);
      }
      setNotice(`${currentSchema.label} 配置已移除`);
      showToast(`${currentSchema.label} 已解绑`, "success");
      setDrafts((previous) => ({
        ...previous,
        [selectedPlatform]: DEFAULT_DRAFT(selectedPlatform),
      }));
      await refreshConfigs();
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error ? requestError.message : "删除IM配置失败",
      );
    } finally {
      setSaving(false);
    }
  }

  function copyWebhookUrl() {
    if (!webhookUrl) return;
    void navigator.clipboard.writeText(webhookUrl);
    showToast("Webhook URL已复制", "success");
  }

  if (loading) {
    return (
      <div className="hb-page">
        <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[#737373]">
          <Loader2 size={16} className="animate-spin" />
          正在加载IM配置...
        </div>
      </div>
    );
  }

  if (!employee || !employeeView) {
    return (
      <div className="hb-page space-y-4">
        <button
          type="button"
          onClick={() => navigate("/department-employees")}
          className="hb-btn-ghost"
        >
          <ArrowLeft size={14} />
          返回列表
        </button>
        <div className="hb-card p-8 text-sm text-[#737373]">实例不存在</div>
      </div>
    );
  }

  return (
    <div className="hb-page space-y-5">
      <Breadcrumb items={[{ label: '员工详情', to: `/instances/${employee.employeeId}` }, { label: 'IM 配置' }]} />

      {error ? (
        <div className="hb-alert hb-alert-error">
          <AlertCircle size={14} />
          <span>{error}</span>
        </div>
      ) : null}

      {notice ? (
        <div className="hb-alert hb-alert-success">
          <CheckCircle2 size={14} />
          <span>{notice}</span>
        </div>
      ) : null}

      <section className="hb-card p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <span className="hb-kicker">多平台IM</span>
            <h1 className="hb-page-title">IM配置 - {employee.nickname}</h1>
            <p className="hb-page-copy">
              配置各平台IM设置，生成webhook URL，并绑定飞书/钉钉/企业微信。
            </p>
          </div>
          <div className="flex flex-col items-end gap-2">
            <span
              className={`hb-pill ${statusClass(employeeView.mappedStatus, employeeView.lifecycleStatus)}`}
            >
              {statusLabel(
                employeeView.mappedStatus,
                employeeView.lifecycleStatus,
              )}
            </span>
            <span
              className={`hb-pill ${ownershipClass(employeeView.ownership)}`}
            >
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
                <Bot size={16} />
                部门成员不支持IM配置
              </div>
              <p className="mt-2 max-w-2xl text-sm leading-relaxed text-[#737373]">
                IM设置仅适用于个人分身和私有分支。请先创建一个，然后在此绑定频道。
              </p>
            </div>
            <button
              type="button"
              className="hb-btn-primary"
              onClick={() => navigate(`/clone/${employee.employeeId}`)}
            >
              Create clone
            </button>
          </div>
        </section>
      ) : (
        <>
          <section className="hb-stat-grid">
            <div className="hb-stat-card">
              <div className="hb-stat-label">已配置平台</div>
              <div className="hb-stat-value">{configuredCount}</div>
            </div>
            <div className="hb-stat-card">
              <div className="hb-stat-label">Current platform</div>
              <div className="hb-stat-value">
                {PLATFORM_SCHEMAS[selectedPlatform].label}
              </div>
            </div>
            <div className="hb-stat-card">
              <div className="hb-stat-label">当前模式</div>
              <div className="hb-stat-value">{currentModeSchema.label}</div>
            </div>
            <div className="hb-stat-card">
              <div className="hb-stat-label">Webhook</div>
              <div className="hb-stat-value">
                {webhookUrl ? "就绪" : "待配置"}
              </div>
            </div>
          </section>

          <section className="hb-section">
            <div className="hb-section-head">
              <div>
                <h2 className="hb-section-title">平台选择</h2>
                <p className="hb-section-copy">
                  每个平台独立存储，不会互相覆盖。
                </p>
              </div>
            </div>

            <div className="grid gap-4 xl:grid-cols-3">
              {PLATFORM_ORDER.map((platform) => {
                const schema = PLATFORM_SCHEMAS[platform];
                const item = configMap[platform];
                const active = selectedPlatform === platform;
                const tone = statusTone(item);
                const modeLabel = item?.connectionMode
                  ? schema.modes[item.connectionMode].label
                  : "未选择模式";
                const configuredAt = item?.configuredAt
                  ? new Date(item.configuredAt).toLocaleString("zh-CN", {
                      hour12: false,
                    })
                  : "—";

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
                          <span
                            className={`hb-squircle h-8 w-8 ${
                              schema.accent === "blue"
                                ? "bg-[#dde9ff] text-[#3d5cff]"
                                : schema.accent === "orange"
                                  ? "bg-[#fff0df] text-[#b45309]"
                                  : "bg-[#e7f9ee] text-[#15803d]"
                            }`}
                          >
                            {firstCharacter(schema.label)}
                          </span>
                          {schema.label}
                        </div>
                        <p className="mt-2 text-sm leading-relaxed text-[#737373]">
                          {schema.intro}
                        </p>
                      </div>
                      <span className={`hb-pill ${tone}`}>
                        {statusText(item)}
                      </span>
                    </div>

                    <div className="mt-4 flex flex-wrap gap-2">
                      <span className="hb-pill gray">{modeLabel}</span>
                      <span className="hb-pill blue">
                        {item?.webhookPath ? "Webhook就绪" : "Webhook待配置"}
                      </span>
                    </div>

                    <div className="mt-4 grid grid-cols-2 gap-3 text-xs text-[#737373]">
                      <div>
                        <div>绑定时间</div>
                        <div className="mt-1 text-sm font-semibold text-[#0a0a0a]">
                          {configuredAt}
                        </div>
                      </div>
                      <div>
                        <div>Webhook路径</div>
                        <div className="mt-1 truncate text-sm font-semibold text-[#0a0a0a]">
                          {item?.webhookPath || "待配置"}
                        </div>
                      </div>
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
                  <h2 className="hb-section-heading !mb-0">
                    {currentSchema.label} config
                  </h2>
                  <p className="mt-2 text-sm text-[#737373]">
                    {currentSchema.intro}
                  </p>
                </div>
                <span className={`hb-pill ${statusTone(currentConfig)}`}>
                  {statusText(currentConfig)}
                </span>
              </div>

              <div className="mt-5 hb-chip-row">
                {Object.entries(currentSchema.modes).map(([mode, spec]) => (
                  <button
                    key={mode}
                    type="button"
                    className={`hb-chip ${currentMode === mode ? "is-active" : ""}`}
                    onClick={() => changeMode(mode as ImConnectionMode)}
                    disabled={!spec.allowed}
                  >
                    {spec.label}
                    {!spec.allowed ? <span>不可用</span> : null}
                  </button>
                ))}
              </div>

              <div className="mt-4 hb-callout info">
                {currentModeSchema.help}
              </div>

              {!currentModeSchema.allowed ? (
                <div className="mt-4 hb-alert hb-alert-warn">
                  <ShieldCheck size={14} />
                  <span>此平台模式不可用。请切换到支持的模式。</span>
                </div>
              ) : null}

              <div className="mt-5 grid gap-4 md:grid-cols-2">
                {currentModeSchema.fields.map((field) => (
                  <label key={field.key} className="hb-field md:col-span-1">
                    <span className="hb-field-label">
                      {field.label} {field.required ? "*" : ""}
                    </span>
                    {field.kind === "checkbox" ? (
                      <label className="flex items-center gap-2 rounded-xl border border-[#ececec] bg-white px-3 py-3 text-sm text-[#404040]">
                        <input
                          type="checkbox"
                          checked={
                            String(
                              drafts[selectedPlatform][field.key] ?? "",
                            ) === "true"
                          }
                          onChange={(event) =>
                            updateCheckboxField(field.key, event.target.checked)
                          }
                          disabled={saving}
                          className="h-4 w-4 rounded border-[#d4d4d8] text-[#2563eb]"
                        />
                        <span>{field.placeholder || field.label}</span>
                      </label>
                    ) : (
                      <input
                        type={field.type ?? fieldType(field.key)}
                        value={drafts[selectedPlatform][field.key] ?? ""}
                        onChange={(event) =>
                          updateField(field.key, event.target.value)
                        }
                        className="hb-input"
                        placeholder={field.placeholder}
                        disabled={saving}
                      />
                    )}
                    {field.required ? null : (
                      <span className="hb-field-help">
                        可选，稍后可从管理控制台填写。
                      </span>
                    )}
                  </label>
                ))}
              </div>

              <div className="mt-5 rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <div className="text-sm font-semibold text-[#0a0a0a]">
                      Webhook URL
                    </div>
                    <div className="mt-1 text-xs text-[#737373]">
                      {webhookLoading
                        ? "获取当前平台webhook..."
                        : "保存后可将此URL复制到平台控制台。"}
                    </div>
                  </div>
                  <button
                    type="button"
                    className="hb-btn-ghost !px-3 !py-1.5 !text-xs"
                    onClick={() => void refreshConfigs()}
                    disabled={saving || webhookLoading}
                  >
                    <RefreshCw size={12} />
                    刷新
                  </button>
                </div>
                <div className="mt-3 flex flex-wrap items-center gap-2">
                  <div className="min-w-0 flex-1 break-all rounded-xl bg-white px-3 py-2 text-xs text-[#404040]">
                    {webhookUrl || "Webhook URL尚未加载"}
                  </div>
                  <button
                    type="button"
                    className="hb-btn-ghost !px-3 !py-1.5 !text-xs"
                    onClick={copyWebhookUrl}
                    disabled={!webhookUrl}
                  >
                    <Copy size={12} />
                    复制URL
                  </button>
                </div>
              </div>

              <div className="mt-5 flex flex-wrap justify-end gap-2">
                <button
                  type="button"
                  className="hb-btn-ghost"
                  onClick={() => navigate(`/instances/${employee.employeeId}`)}
                >
                  取消
                </button>
                <button
                  type="button"
                  className="hb-btn-ghost"
                  onClick={() => void deleteConfig()}
                  disabled={saving || !currentConfig}
                >
                  <Trash2 size={14} />
                  解绑
                </button>
                <button
                  type="button"
                  className="hb-btn-primary"
                  onClick={() => void saveConfig()}
                  disabled={saving}
                >
                  {saving ? (
                    <Loader2 size={14} className="animate-spin" />
                  ) : (
                    <Settings2 size={14} />
                  )}
                  {currentConfig ? "保存并刷新" : "保存配置"}
                </button>
              </div>
            </div>

            <div className="hb-card hb-detail-panel">
              <h2 className="hb-section-heading">集成指南</h2>
              <div className="space-y-3">
                <div className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
                  <div className="text-sm font-semibold text-[#0a0a0a]">
                    1. 选择平台
                  </div>
                  <div className="mt-1 text-sm text-[#737373]">
                    飞书、钉钉和企业微信独立存储，不会互相覆盖。
                  </div>
                </div>
                <div className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
                  <div className="text-sm font-semibold text-[#0a0a0a]">
                    2. 填写凭证
                  </div>
                  <div className="mt-1 text-sm text-[#737373]">
                    完成所选模式的必填字段，后端将验证并加密它们。
                  </div>
                </div>
                <div className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
                  <div className="text-sm font-semibold text-[#0a0a0a]">
                    3. 复制webhook
                  </div>
                  <div className="mt-1 text-sm text-[#737373]">
                    将生成的回调URL粘贴到相应的IM平台控制台。
                  </div>
                </div>
              </div>

              <div className="mt-5 hb-callout info">
                <Wifi size={16} />
                <div>
                  <div className="font-semibold text-[#0a0a0a]">
                    应用内聊天和IM是独立的
                  </div>
                  <div className="mt-1 text-sm text-[#404040]">
                    此页面仅处理平台绑定。配置后，您仍然可以从实例详情页进入应用内聊天或直接使用IM频道。
                  </div>
                </div>
              </div>

              <div className="mt-5 rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
                <div className="flex items-center gap-2 text-sm font-semibold text-[#0a0a0a]">
                  <Link2 size={14} />
                  当前实例信息
                </div>
                <div className="mt-3 grid gap-3 text-sm text-[#404040]">
                  <div className="flex items-center justify-between gap-3">
                    <span>实例名称</span>
                    <span className="font-medium text-[#0a0a0a]">
                      {employee.nickname}
                    </span>
                  </div>
                  <div className="flex items-center justify-between gap-3">
                    <span>实例ID</span>
                    <span className="font-medium text-[#0a0a0a]">
                      {employee.employeeId}
                    </span>
                  </div>
                  <div className="flex items-center justify-between gap-3">
                    <span>部门</span>
                    <span className="font-medium text-[#0a0a0a]">
                      {employee.departmentId || employee.owningTeam}
                    </span>
                  </div>
                  <div className="flex items-center justify-between gap-3">
                    <span>状态</span>
                    <span className="font-medium text-[#0a0a0a]">
                      {statusLabel(
                        employeeView.mappedStatus,
                        employeeView.lifecycleStatus,
                      )}
                    </span>
                  </div>
                </div>
              </div>
            </div>
          </section>
        </>
      )}
    </div>
  );
}
