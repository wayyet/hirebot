import { useEffect, useMemo, useRef, useState, useCallback } from "react";
import {
  AlertCircle,
  Loader2,
  MessageCircle,
  Send,
  Trash2,
  Upload,
} from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import { Breadcrumb } from "@/shared/components/Breadcrumb";

import {
  api,
  type EmployeeDetail,
  type InstanceChatMessage,
} from "@/infra/api";
import { tokenService } from "@/infra/auth/token-service";
import { GatewayWs } from "@/infra/sandbox/gateway-ws";
import {
  fetchSandboxSessionMessages,
  uploadMediaToGateway,
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

export interface ChatFile {
  id: string;
  name: string;
  size: number;
  status: "解析中" | "已解析";
  type?: "file" | "skill";
  mimeType?: string;
  content?: string;
  metadata?: Record<string, string>;
  rawFile?: File;
}

const MAX_MATERIAL_CHARS = 100000;

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function mkId(): string {
  return Math.random().toString(36).substring(2, 15);
}

async function readFileText(file: File): Promise<string | undefined> {
  if (file.size > MAX_MATERIAL_CHARS * 4) {
    return `[文件过大，仅作为资料登记：${file.name}，${file.size} bytes]`;
  }
  return new Promise((resolve) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () =>
      resolve(`[文件内容读取失败，仅作为资料登记：${file.name}]`);
    reader.readAsText(file);
  });
}

async function fileToChatFile(
  file: File,
  type: "file" | "skill" = "file",
): Promise<ChatFile> {
  const content = type === "file" ? await readFileText(file) : undefined;
  return {
    id: mkId(),
    name: file.name,
    size: file.size,
    status: "已解析",
    type,
    mimeType: file.type || undefined,
    content,
    rawFile: file,
  };
}

type ChatDraft = {
  content: string;
};

function formatTime(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleString("zh-CN", {
    month: "numeric",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function normalizeMessageContent(content: string) {
  // 最终保存时去掉 <think> 标签内的内容
  return content.replace(/<think>[\s\S]*?<\/think>/gi, "").trim();
}

function mapMessages(messages: InstanceChatMessage[]) {
  return messages
    .filter((message) => message.content.trim().length > 0)
    .map((message) => ({
      ...message,
      content: normalizeMessageContent(message.content),
    }));
}

function mapSandboxMessages(
  messages: { type: string; content?: string; text?: string }[],
) {
  return messages
    .filter(
      (message) =>
        message.type === "user_message" || message.type === "assistant_message",
    )
    .map<InstanceChatMessage>((message, index) => ({
      messageId: `sandbox-${index}-${Date.now()}`,
      role: message.type === "user_message" ? "user" : "assistant",
      content: normalizeMessageContent(
        String(message.content ?? message.text ?? ""),
      ),
      createdAt: new Date().toISOString(),
    }))
    .filter((message) => message.content.trim().length > 0);
}

function normalizeErrorMessage(error: unknown) {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message;
  }

  return "请求失败，请稍后重试";
}

export default function InstanceChatPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [employee, setEmployee] = useState<EmployeeDetail | null>(null);
  const [messages, setMessages] = useState<InstanceChatMessage[]>([]);
  const [draft, setDraft] = useState<ChatDraft>({ content: "" });
  const [loading, setLoading] = useState(true);
  const [sending, setSending] = useState(false);
  const [typing, setTyping] = useState(false);
  const [error, setError] = useState("");
  const [clearing, setClearing] = useState(false);
  const [streamingContent, setStreamingContent] = useState<string | null>(null);
  const [pendingFiles, setPendingFiles] = useState<ChatFile[]>([]);

  const bottomRef = useRef<HTMLDivElement | null>(null);
  const wsRef = useRef<GatewayWs | null>(null);
  const gatewayEndpointRef = useRef<string | null>(null);
  const sessionIdRef = useRef<string | null>(null);
  // 保存 WS 流式回复的原始内容（normalizeMessageContent 之前）
  const rawStreamingContentRef = useRef<string>("");
  // 存储原始 File 对象，供 WS 路径上传到 Gateway 使用
  const rawFileMapRef = useRef<Map<string, File>>(new Map());
  // 文件选择 input 的 ref
  const fileRef = useRef<HTMLInputElement>(null);

  const employeeView = useMemo(() => {
    if (!employee) return null;
    return withEmployeeView(toEmployeeDetailSummary(employee));
  }, [employee]);

  const canChat =
    employeeView?.ownership === "personal_clone" ||
    employeeView?.ownership === "private_branch";
  const isLive = employeeView?.mappedStatus === "live";

  const addPendingFiles = useCallback((fl: FileList | File[]) => {
    const files = Array.from(fl);
    const placeholders: ChatFile[] = files.map((file) => {
      const id = mkId();
      rawFileMapRef.current.set(id, file);
      return {
        id,
        name: file.name,
        size: file.size,
        status: "解析中" as const,
        type: "file" as const,
        mimeType: file.type || undefined,
      };
    });
    setPendingFiles((prev) => [...prev, ...placeholders]);

    void Promise.all(files.map((file) => fileToChatFile(file, "file"))).then(
      (parsedFiles) => {
        setPendingFiles((prev) =>
          prev.map((item) => {
            const parsed = parsedFiles.find(
              (file) => file.name === item.name && file.size === item.size,
            );
            return parsed ?? item;
          }),
        );
      },
    );
  }, []);

  const handleFileInputChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const files = e.target.files;
      if (files && files.length > 0) {
        addPendingFiles(files);
      }
      e.target.value = "";
    },
    [addPendingFiles],
  );

  const handleRemovePendingFile = useCallback((fileId: string) => {
    setPendingFiles((prev) => prev.filter((file) => file.id !== fileId));
    rawFileMapRef.current.delete(fileId);
  }, []);

  const triggerFileUpload = useCallback(() => {
    fileRef.current?.click();
  }, []);

  async function syncSandboxHistory(endpoint: string, sessionId: string) {
    const sandboxMessages = await fetchSandboxSessionMessages(
      endpoint,
      sessionId,
    );
    const mapped = mapSandboxMessages(sandboxMessages);
    setMessages((prev) => (mapped.length >= prev.length ? mapped : prev));
  }

  async function connectSandboxWs(endpoint: string) {
    wsRef.current?.disconnect();

    const token = await tokenService.ensureFresh();
    if (!token) return;

    const ws = new GatewayWs(endpoint, token);

    ws.onMessage = (msg) => {
      const type = String(msg.type ?? "");
      if (type === "typing_start") {
        // AI 开始思考，初始化流式内容
        rawStreamingContentRef.current = "";
        setStreamingContent("");
        setTyping(true);
        return;
      }

      if (type === "text_delta" || type === "assistant_chunk") {
        // 逐字追加流式内容，同时处理 <think> 标签
        const chunk = String(
          msg.delta ?? msg.chunk ?? msg.content ?? msg.text ?? "",
        );
        setStreamingContent((prev) => {
          const nextRaw = prev === null ? chunk : prev + chunk;
          rawStreamingContentRef.current = nextRaw;
          // 对流式内容也处理 <think> 标签
          return nextRaw
            .replace(
              /<think>([\s\S]*?)<\/think>/gi,
              '<span style="color: #9ca3af; font-style: italic;">$1</span>',
            )
            .trim();
        });
        return;
      }

      if (type === "typing_stop" || type === "assistant_done") {
        // AI 回复完毕，保存原始内容，然后将清理后的内容提交为正式气泡
        const rawReply =
          rawStreamingContentRef.current ||
          String(msg.content ?? msg.text ?? "");
        rawStreamingContentRef.current = "";

        // 直接从 ref 取流式内容提交为正式消息（不放在 setStreamingContent 回调里，
        // 避免 React StrictMode 双重调用导致同一条 bot 消息被 add 两遍）
        if (rawReply && rawReply.trim().length > 0) {
          const cleaned = normalizeMessageContent(rawReply);
          if (cleaned.length > 0) {
            setMessages((current) => [
              ...current,
              {
                messageId: `local-${Date.now()}`,
                role: "assistant",
                content: cleaned,
                createdAt: new Date().toISOString(),
              },
            ]);
          }
        }
        setStreamingContent(null);
        setTyping(false);

        const sandboxSessionId = sessionIdRef.current;
        const sandboxGatewayEndpoint = gatewayEndpointRef.current;
        if (sandboxSessionId && sandboxGatewayEndpoint) {
          void syncSandboxHistory(
            sandboxGatewayEndpoint,
            sandboxSessionId,
          ).catch(() => {
            // 历史同步失败时保留当前已渲染内容
          });
        }
      }
    };

    // 重连后拉取断线期间的会话历史
    ws.onReconnected = () => {
      const sid = sessionIdRef.current;
      const ep = gatewayEndpointRef.current;
      if (ep && sid) {
        void fetchSandboxSessionMessages(ep, sid)
          .then((sandboxMessages) => {
            const mapped = mapSandboxMessages(sandboxMessages);
            setMessages((prev) =>
              mapped.length >= prev.length ? mapped : prev,
            );
          })
          .catch(() => {
            /* 忽略拉取失败 */
          });
      }
    };

    ws.onStateChange = (state) => {
      if (state === "closed" || state === "error") {
        setTyping(false);
        setStreamingContent(null);
        rawStreamingContentRef.current = "";
      }
    };

    ws.connect();
    wsRef.current = ws;
  }

  async function loadChat(instanceId: string) {
    setLoading(true);
    setError("");

    try {
      const [detail, timeline, gatewayEndpointResult] = await Promise.all([
        api.employeeRuntime.getEmployee(instanceId),
        api.employeeRuntime.getInstanceChatMessages(instanceId),
        api.employeeRuntime.getSandboxGatewayEndpoint(instanceId),
      ]);

      setEmployee(detail);
      setMessages(mapMessages(timeline.messages));
      sessionIdRef.current = timeline.conversationId;

      // 调用新 API 获取 gateway endpoint（与 HiringPage 一致）
      const gatewayEndpoint = gatewayEndpointResult ?? null;
      console.log(
        "[InstanceChatPage] gatewayEndpoint from API:",
        gatewayEndpoint,
      );

      if (gatewayEndpoint) {
        gatewayEndpointRef.current = gatewayEndpoint;
        try {
          await syncSandboxHistory(gatewayEndpoint, timeline.conversationId);
          await connectSandboxWs(gatewayEndpoint);
        } catch {
          // 直连沙箱不可用时，保持后端兜底路径
          gatewayEndpointRef.current = null;
          wsRef.current?.disconnect();
          wsRef.current = null;
        }
      } else {
        console.log(
          "[InstanceChatPage] gatewayEndpoint 为空，将使用 HTTP API 兜底",
        );
      }
    } catch (requestError: unknown) {
      setError(normalizeErrorMessage(requestError));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (!id) {
      setError("实例 ID 缺失");
      setLoading(false);
      return;
    }

    void loadChat(id);
  }, [id]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, sending, typing]);

  useEffect(() => {
    return () => {
      wsRef.current?.disconnect();
      wsRef.current = null;
    };
  }, []);

  async function handleSend() {
    if (!id || !canChat || !isLive) {
      return;
    }

    const content = draft.content.trim();
    if (!content && pendingFiles.length === 0) {
      return;
    }

    if (sending) {
      return;
    }

    setSending(true);
    setError("");

    // 收集待发送的文件
    const incoming = pendingFiles.length > 0 ? [...pendingFiles] : [];

    // 构建乐观消息内容
    let messageText = content || "补充信息";
    if (incoming.length > 0) {
      messageText = `${messageText}\n\n上传文件：${incoming.map((f) => f.name).join("、")}`;
    }

    const optimistic: InstanceChatMessage = {
      messageId: `local-${Date.now()}`,
      role: "user",
      content: messageText,
      createdAt: new Date().toISOString(),
    };
    setMessages((prev) => [...prev, optimistic]);
    setDraft({ content: "" });
    setPendingFiles([]);

    const sandboxGatewayEndpoint = gatewayEndpointRef.current;
    const sandboxSessionId = sessionIdRef.current;
    const sandboxWs = wsRef.current;

    // 必须通过 WebSocket 发送消息，沙箱实时流式回复
    if (!sandboxGatewayEndpoint || !sandboxSessionId || !sandboxWs) {
      setError("沙箱连接未建立，无法发送消息");
      setSending(false);
      return;
    }

    try {
      // 如果有附件，先上传到 Gateway 获取 [FILE_URL:...] 标记
      if (incoming.length > 0) {
        const token = await tokenService.ensureFresh();
        if (!token) {
          throw new Error("Token not available for file upload");
        }
        const markers: string[] = [];
        for (const file of incoming) {
          const rawFile = rawFileMapRef.current.get(file.id) ?? file.rawFile;
          if (!rawFile) {
            throw new Error(`无法获取文件原始数据：${file.name}`);
          }
          const result = await uploadMediaToGateway(
            sandboxGatewayEndpoint,
            token,
            rawFile,
          );
          markers.push(
            `${result.marker}\nAttached file: ${result.fileName} (${formatFileSize(result.sizeBytes)})`,
          );
          // 清理已上传的文件引用
          rawFileMapRef.current.delete(file.id);
        }
        if (markers.length > 0) {
          messageText = `${markers.join("\n")}\n\n${messageText}`;
        }
      }

      sandboxWs.send({
        type: "user_message",
        text: messageText,
        sessionId: sandboxSessionId,
      });
      setTyping(true);
    } catch (requestError: unknown) {
      setError(normalizeErrorMessage(requestError));
      setMessages((prev) =>
        prev.filter((message) => message.messageId !== optimistic.messageId),
      );
      setDraft({ content });
    } finally {
      setSending(false);
    }
  }

  async function handleClear() {
    if (!id || clearing) {
      return;
    }

    setClearing(true);
    setError("");
    try {
      wsRef.current?.disconnect();
      wsRef.current = null;
      setStreamingContent(null);
      setTyping(false);

      await api.employeeRuntime.clearInstanceChatMessages(id);
      setMessages([]);

      const endpoint = gatewayEndpointRef.current;
      if (endpoint && sessionIdRef.current) {
        void connectSandboxWs(endpoint);
      }
    } catch (requestError: unknown) {
      setError(normalizeErrorMessage(requestError));
    } finally {
      setClearing(false);
    }
  }

  const backTarget =
    employeeView?.ownership === "department"
      ? "/department-employees"
      : "/my-employees";

  return (
    <div className="hb-page hb-page-wide">
      <Breadcrumb
        items={[
          {
            label: employeeView?.ownership === "department" ? '部门数字员工' : '我的数字员工',
            to: backTarget,
          },
          { label: '实例对话' },
        ]}
      />

      {error && (
        <div className="hb-alert hb-alert-error">
          <AlertCircle size={14} />
          <span>{error}</span>
        </div>
      )}

      {loading ? (
        <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[#737373]">
          <Loader2 size={16} className="animate-spin" />
          正在加载分身对话...
        </div>
      ) : !employee || !employeeView ? (
        <div className="hb-card p-8 text-sm text-[#737373]">实例不存在</div>
      ) : !canChat ? (
        <div className="hb-card p-8 text-sm text-[#737373]">
          当前实例不是分身类型，不能进入站内对话。
        </div>
      ) : (
        <div className="hb-chat-shell hb-card">
          <div className="hb-chat-head">
            <div className="flex items-start gap-4">
              <div
                className={`hb-user-avatar hb-chat-avatar ${ownershipClass(employeeView.ownership)}`}
              >
                {firstCharacter(employee.nickname)}
              </div>
              <div className="space-y-2">
                <div className="flex flex-wrap items-center gap-2">
                  <h1 className="text-[22px] font-semibold tracking-[-0.02em] text-[#0a0a0a]">
                    {employee.nickname}
                  </h1>
                  <span
                    className={`hb-pill ${statusClass(employeeView.mappedStatus, employee.lifecycleStatus)}`}
                  >
                    {statusLabel(
                      employeeView.mappedStatus,
                      employee.lifecycleStatus,
                    )}
                  </span>
                  <span
                    className={`hb-pill ${ownershipClass(employeeView.ownership)}`}
                  >
                    {ownershipLabel(employeeView.ownership)}
                  </span>
                </div>
                <p className="max-w-[720px] text-sm leading-6 text-[#737373]">
                  这里是你的实例站内对话。消息会直接发送到当前分身，不会进入雇佣流程。
                </p>
                <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-[#9ca3af]">
                  <span>实例 ID {employee.employeeId}</span>
                  <span>Owner {employee.ownerUserId}</span>
                  <span>
                    部门 {employee.departmentId || employee.owningTeam}
                  </span>
                </div>
              </div>
            </div>

            <div className="flex items-center gap-2">
              <button
                type="button"
                className="hb-btn-ghost"
                onClick={handleClear}
                disabled={clearing || messages.length === 0}
              >
                <Trash2 size={14} />
                {clearing ? "清空中" : "清空对话"}
              </button>
              <button
                type="button"
                className="hb-btn-primary"
                onClick={() => navigate(`/instances/${employee.employeeId}`)}
              >
                <MessageCircle size={14} />
                查看详情
              </button>
            </div>
          </div>

          <div className="hb-chat-history">
            {messages.length === 0 ? (
              <div className="hb-chat-empty">
                <MessageCircle size={16} />
                还没有消息，先给分身发一句话吧
              </div>
            ) : (
              messages.map((message) => (
                <div
                  key={message.messageId}
                  className={`hb-chat-message ${message.role === "assistant" ? "is-assistant" : "is-user"}`}
                >
                  <div className="hb-chat-meta">
                    {message.role === "assistant" ? employee.nickname : "我"} ·{" "}
                    {formatTime(message.createdAt)}
                  </div>
                  <div
                    className={`hb-chat-bubble ${message.role === "assistant" ? "is-assistant" : "is-user"}`}
                    dangerouslySetInnerHTML={{ __html: message.content }}
                  />
                </div>
              ))
            )}

            {typing && streamingContent !== null && (
              <div className="hb-chat-message is-assistant">
                <div className="hb-chat-meta">
                  {employee.nickname} · 正在回复
                </div>
                <div
                  className="hb-chat-bubble is-assistant"
                  dangerouslySetInnerHTML={{
                    __html:
                      streamingContent.length > 0 ? streamingContent : "...",
                  }}
                />
              </div>
            )}

            {typing && streamingContent === null && (
              <div className="hb-chat-message is-assistant">
                <div className="hb-chat-meta">
                  {employee.nickname} · 正在回复
                </div>
                <div className="hb-chat-bubble is-assistant hb-chat-typing">
                  正在思考中...
                </div>
              </div>
            )}

            <div ref={bottomRef} />
          </div>

          <div className="hb-chat-compose">
            {/* 待上传文件列表 */}
            {pendingFiles.length > 0 && (
              <div className="mb-3 flex flex-wrap gap-2">
                {pendingFiles.map((file) => (
                  <div
                    key={file.id}
                    className="flex items-center gap-2 rounded-full border border-[#ececec] bg-[#fafafa] px-3 py-1.5 text-sm text-[#404040]"
                  >
                    <Upload size={12} className="text-[#9ca3af]" />
                    <span className="max-w-[200px] truncate">{file.name}</span>
                    <button
                      type="button"
                      onClick={() => handleRemovePendingFile(file.id)}
                      className="ml-1 text-[#9ca3af] hover:text-[#525252]"
                    >
                      ×
                    </button>
                  </div>
                ))}
              </div>
            )}
            <button
              type="button"
              className="hb-btn-ghost mb-3 inline-flex self-start"
              onClick={triggerFileUpload}
              disabled={sending || !isLive}
              title="上传文件"
            >
              <Upload size={14} />
              上传文件
            </button>
            <div className="flex items-end gap-3">
              <textarea
                value={draft.content}
                onChange={(event) => setDraft({ content: event.target.value })}
                onKeyDown={(event) => {
                  if (event.key === "Enter" && !event.shiftKey) {
                    event.preventDefault();
                    void handleSend();
                  }
                }}
                placeholder={
                  isLive
                    ? "输入消息，Enter 发送，Shift+Enter 换行"
                    : "当前实例未上岗，不能对话"
                }
                disabled={sending || !isLive}
              />

              <button
                type="button"
                className="hb-btn-primary"
                onClick={() => void handleSend()}
                disabled={
                  sending ||
                  !isLive ||
                  (draft.content.trim().length === 0 &&
                    pendingFiles.length === 0)
                }
              >
                <Send size={14} />
                发送
              </button>
            </div>
            {!isLive && (
              <p className="mt-3 text-xs text-[#9ca3af]">
                只有 `live` 状态的分身和私有分支才能进入站内对话。
              </p>
            )}
            {/* 隐藏的文件选择 input */}
            <input
              ref={fileRef}
              type="file"
              multiple
              onChange={handleFileInputChange}
              className="hidden"
              disabled={sending || !isLive}
            />
          </div>
        </div>
      )}
    </div>
  );
}
