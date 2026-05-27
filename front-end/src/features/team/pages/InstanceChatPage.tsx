import { useEffect, useMemo, useRef, useState, useCallback } from "react";
import {
  AlertCircle,
  FileText,
  Loader2,
  Maximize2,
  MessageCircle,
  Paperclip,
  Send,
  Square,
  Trash2,
  Minimize2,
} from "lucide-react";
import { useLocation, useNavigate, useParams } from "react-router-dom";
import { Breadcrumb } from "@/shared/components/Breadcrumb";
import { instanceBasePath } from "@/shared/utils/instancePath";
import "@/features/team/styles/instance-chat-page.css";

import {
  api,
  type EmployeeDetail,
  type InstanceChatMessage,
} from "@/infra/api";
import { tokenService } from "@/infra/auth/token-service";
import { GatewayWs } from "@/infra/sandbox/gateway-ws";
import { resolveGatewayEndpoint } from "@/infra/sandbox/sandbox-config";
import {
  fetchLatestGatewaySession,
  fetchSandboxSessionMessages,
  type SandboxToolCall,
  uploadMediaToGateway,
} from "@/infra/sandbox/sandbox-api";
import SessionListPanel from "@/features/team/components/SessionListPanel";
import { InstanceChatMessageBody } from "@/features/team/components/InstanceChatMessageBody";
import { HiringToolStepsBlock } from "@/features/hiring/pages/components/HiringToolStepsBlock";
import type { ToolStep } from "@/features/hiring/pages/hiringPageTypes";
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
  status: "上传中" | "已上传" | "上传失败";
  mimeType?: string;
  marker?: string;
  url?: string;
  uploadError?: string;
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function mkId(): string {
  return Math.random().toString(36).substring(2, 15);
}

type ChatDraft = {
  content: string;
};

type DisplayChatMessage = InstanceChatMessage & {
  toolSteps?: ToolStep[];
};

type SlashCommand = {
  cmd: string;
  args: string;
  desc: string;
};

const SLASH_COMMANDS: SlashCommand[] = [
  { cmd: "/new", args: "", desc: "开始一个新会话" },
  { cmd: "/clear", args: "", desc: "清空当前聊天记录" },
  { cmd: "/stop", args: "", desc: "终止当前生成" },
  { cmd: "/help", args: "", desc: "查看可用聊天命令" },
  { cmd: "/think", args: "off | low | medium | high", desc: "调整思考强度并发送到分身" },
];

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

function mergeHistoryMessages(
  previousMessages: DisplayChatMessage[],
  historyMessages: DisplayChatMessage[],
) {
  return historyMessages.map((message) => {
    const matched = [...previousMessages]
      .reverse()
      .find(
        (item) =>
          item.role === message.role &&
          item.content === message.content &&
          (item.toolSteps?.length ?? 0) > 0,
      );

    if (!matched?.toolSteps) {
      return message;
    }

    return {
      ...message,
      toolSteps: matched.toolSteps,
    };
  });
}

function mapSandboxMessages(
  messages: {
    type: string;
    content?: string;
    text?: string;
    createdAt?: string;
    toolCalls?: SandboxToolCall[];
  }[],
) {
  return messages
    .filter(
      (message) =>
        message.type === "user_message" || message.type === "assistant_message",
    )
    .map<DisplayChatMessage>((message, index) => ({
      messageId: `sandbox-${index}-${Date.now()}`,
      role: message.type === "user_message" ? "user" : "assistant",
      content: normalizeMessageContent(
        String(message.content ?? message.text ?? ""),
      ),
      createdAt: message.createdAt ?? new Date().toISOString(),
      toolSteps:
        message.type === "assistant_message" &&
        Array.isArray(message.toolCalls) &&
        message.toolCalls.length > 0
          ? message.toolCalls.map<ToolStep>((toolCall, toolIndex) => ({
              id: `history-tool-${index}-${toolIndex}`,
              name: toolCall.toolName.startsWith("streaming.")
                ? toolCall.toolName.slice("streaming.".length)
                : toolCall.toolName,
              args: toolCall.arguments,
              result: toolCall.result,
              status: toolCall.result ? "done" : "running",
            }))
          : undefined,
    }))
    .filter(
      (message) =>
        message.content.trim().length > 0 ||
        (message.toolSteps?.length ?? 0) > 0,
    );
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
  const [messages, setMessages] = useState<DisplayChatMessage[]>([]);
  const [draft, setDraft] = useState<ChatDraft>({ content: "" });
  const [loading, setLoading] = useState(true);
  const [sending, setSending] = useState(false);
  const [typing, setTyping] = useState(false);
  const [error, setError] = useState("");
  const [clearing, setClearing] = useState(false);
  const [streamingContent, setStreamingContent] = useState<string | null>(null);
  const [streamingToolSteps, setStreamingToolSteps] = useState<ToolStep[]>([]);
  const [pendingFiles, setPendingFiles] = useState<ChatFile[]>([]);
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(
    null,
  );
  const [sessionListVisible] = useState(true);
  const [sessionListRefreshKey, setSessionListRefreshKey] = useState(0);
  const [sandboxConnected, setSandboxConnected] = useState(false);
  const [sessionSwitching, setSessionSwitching] = useState(false);
  const [expandOpen, setExpandOpen] = useState(false);
  const [slashMenuIdx, setSlashMenuIdx] = useState(0);

  const bottomRef = useRef<HTMLDivElement | null>(null);
  const wsRef = useRef<GatewayWs | null>(null);
  const gatewayEndpointRef = useRef<string | null>(null);
  const sessionIdRef = useRef<string | null>(null);
  // 保存 WS 流式回复的原始内容（normalizeMessageContent 之前）
  const rawStreamingContentRef = useRef<string>("");
  const pendingToolStepsRef = useRef<ToolStep[]>([]);
  // 文件选择 input 的 ref
  const fileRef = useRef<HTMLInputElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement | null>(null);

  const employeeView = useMemo(() => {
    if (!employee) return null;
    return withEmployeeView(toEmployeeDetailSummary(employee));
  }, [employee]);

  const canChat =
    employeeView?.ownership === "personal_clone" ||
    employeeView?.ownership === "private_branch";
  const isLive = employeeView?.mappedStatus === "live";
  const isAiWorking =
    typing || streamingContent !== null || streamingToolSteps.length > 0;
  const slashCandidates = useMemo(() => {
    if (!draft.content.startsWith("/")) {
      return [];
    }

    const prefix = draft.content.toLowerCase();
    return SLASH_COMMANDS.filter(({ cmd }) => cmd.startsWith(prefix));
  }, [draft.content]);
  const slashMenuOpen = slashCandidates.length > 0;

  const addPendingFiles = useCallback((fl: FileList | File[]) => {
    const files = Array.from(fl);
    const endpoint = gatewayEndpointRef.current;

    files.forEach((file, index) => {
      const fileId = `${file.name}-${file.lastModified}-${Date.now()}-${index}`;
      const placeholder: ChatFile = {
        id: fileId,
        name: file.name,
        size: file.size,
        status: "上传中",
        mimeType: file.type || undefined,
      };

      setPendingFiles((prev) => [...prev, placeholder]);

      if (!endpoint) {
        setPendingFiles((prev) =>
          prev.map((item) =>
            item.id === fileId
              ? {
                  ...item,
                  status: "上传失败",
                  uploadError: "沙箱端点尚未就绪，无法上传附件",
                }
              : item,
          ),
        );
        return;
      }

      void (async () => {
        try {
          const token = await tokenService.ensureFresh();
          if (!token) {
            throw new Error("Token not available for file upload");
          }

          const result = await uploadMediaToGateway(endpoint, token, file);
          setPendingFiles((prev) =>
            prev.map((item) =>
              item.id === fileId
                ? {
                    ...item,
                    status: "已上传",
                    marker: result.marker,
                    url: result.url,
                    size: result.sizeBytes,
                  }
                : item,
            ),
          );
        } catch (requestError: unknown) {
          setPendingFiles((prev) =>
            prev.map((item) =>
              item.id === fileId
                ? {
                    ...item,
                    status: "上传失败",
                    uploadError: normalizeErrorMessage(requestError),
                  }
                : item,
            ),
          );
        }
      })();
    });
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
  }, []);

  const triggerFileUpload = useCallback(() => {
    fileRef.current?.click();
  }, []);

  async function resolveLatestSandboxEndpoint(instanceId: string) {
    const gatewayEndpointResult =
      await api.employeeRuntime.getSandboxGatewayEndpoint(instanceId);
    const gatewayEndpoint = resolveGatewayEndpoint(gatewayEndpointResult);
    if (!gatewayEndpoint) {
      throw new Error("沙箱网关地址未就绪，请稍后重试");
    }

    gatewayEndpointRef.current = gatewayEndpoint;
    return gatewayEndpoint;
  }

  async function connectSandboxWsWithRecovery(preferredEndpoint?: string | null) {
    if (!id) {
      throw new Error("实例 ID 缺失，无法连接沙箱");
    }

    let endpoint = preferredEndpoint?.trim() || gatewayEndpointRef.current;
    let lastError: unknown = null;

    for (let attempt = 0; attempt < 3; attempt += 1) {
      try {
        if (!endpoint) {
          endpoint = await resolveLatestSandboxEndpoint(id);
        }

        await connectSandboxWs(endpoint);
        gatewayEndpointRef.current = endpoint;
        return endpoint;
      } catch (connectionError: unknown) {
        lastError = connectionError;
        wsRef.current?.disconnect();
        wsRef.current = null;
        setSandboxConnected(false);

        if (attempt >= 2) {
          break;
        }

        await new Promise((resolve) => {
          window.setTimeout(resolve, 1200 * (attempt + 1));
        });

        endpoint = await resolveLatestSandboxEndpoint(id);
      }
    }

    throw lastError instanceof Error
      ? lastError
      : new Error("沙箱连接未建立，无法发送消息");
  }

  async function syncSandboxHistory(endpoint: string, sessionId: string) {
    const sandboxMessages = await fetchSandboxSessionMessages(
      endpoint,
      sessionId,
    );
    const mapped = mapSandboxMessages(sandboxMessages);
    setMessages((prev) =>
      mapped.length >= prev.length ? mergeHistoryMessages(prev, mapped) : prev,
    );
  }

  async function connectSandboxWs(endpoint: string) {
    wsRef.current?.disconnect();
    setSandboxConnected(false);

    const token = await tokenService.ensureFresh();
    if (!token) {
      throw new Error("Token not available for sandbox connection");
    }

    const ws = new GatewayWs(endpoint, token);
    let resolveOpen: (() => void) | null = null;
    let rejectOpen: ((error: Error) => void) | null = null;
    let settled = false;
    let timeoutId: ReturnType<typeof window.setTimeout> | null = null;

    const waitForOpen = new Promise<void>((resolve, reject) => {
      resolveOpen = resolve;
      rejectOpen = reject;
      timeoutId = window.setTimeout(() => {
        if (settled) return;
        settled = true;
        reject(new Error("沙箱连接超时，请稍后重试"));
      }, 8000);
    });

    const settleOpen = (error?: Error) => {
      if (settled) return;
      settled = true;
      if (timeoutId !== null) {
        window.clearTimeout(timeoutId);
        timeoutId = null;
      }
      if (error) {
        rejectOpen?.(error);
      } else {
        resolveOpen?.();
      }
    };

    ws.onMessage = (msg) => {
      const type = String(msg.type ?? "");
      if (type === "typing_start") {
        // AI 开始思考，初始化流式内容
        rawStreamingContentRef.current = "";
        pendingToolStepsRef.current = [];
        setStreamingToolSteps([]);
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
          return nextRaw;
        });
        return;
      }

      if (type === "tool_call" || type === "tool_start") {
        const rawMsg = msg as Record<string, unknown>;
        const rawName = String(
          rawMsg.tool_name ?? rawMsg.name ?? rawMsg.text ?? "tool",
        );
        const toolName = rawName.startsWith("streaming.")
          ? rawName.slice("streaming.".length)
          : rawName;
        const args =
          rawMsg.arguments != null
            ? typeof rawMsg.arguments === "string"
              ? rawMsg.arguments
              : JSON.stringify(rawMsg.arguments)
            : undefined;
        const step: ToolStep = {
          id: mkId(),
          name: toolName || "tool",
          status: "running",
          args,
        };
        pendingToolStepsRef.current = [...pendingToolStepsRef.current, step];
        setStreamingToolSteps([...pendingToolStepsRef.current]);
        return;
      }

      if (type === "tool_result") {
        const rawMsg = msg as Record<string, unknown>;
        const rawName = String(rawMsg.tool_name ?? rawMsg.name ?? "");
        const toolName = rawName.startsWith("streaming.")
          ? rawName.slice("streaming.".length)
          : rawName;
        const resultText = String(rawMsg.text ?? rawMsg.result ?? "");
        const next = pendingToolStepsRef.current.slice();
        let targetIndex = -1;

        if (toolName) {
          for (let index = next.length - 1; index >= 0; index -= 1) {
            if (
              next[index].status === "running" &&
              next[index].name === toolName
            ) {
              targetIndex = index;
              break;
            }
          }
        }

        if (targetIndex < 0) {
          for (let index = next.length - 1; index >= 0; index -= 1) {
            if (next[index].status === "running") {
              targetIndex = index;
              break;
            }
          }
        }

        if (targetIndex >= 0) {
          next[targetIndex] = {
            ...next[targetIndex],
            status: (rawMsg.is_error ?? rawMsg.isError) ? "error" : "done",
            result: resultText || next[targetIndex].result,
          };
          pendingToolStepsRef.current = next;
          setStreamingToolSteps([...next]);
        }
        return;
      }

      if (type === "typing_stop" || type === "assistant_done") {
        // AI 回复完毕，保存原始内容，然后将清理后的内容提交为正式气泡
        const rawReply =
          rawStreamingContentRef.current ||
          String(msg.content ?? msg.text ?? "");
        const toolSteps =
          pendingToolStepsRef.current.length > 0
            ? [...pendingToolStepsRef.current]
            : undefined;
        rawStreamingContentRef.current = "";

        // 直接从 ref 取流式内容提交为正式消息（不放在 setStreamingContent 回调里，
        // 避免 React StrictMode 双重调用导致同一条 bot 消息被 add 两遍）
        if (rawReply && rawReply.trim().length > 0) {
          const cleaned = normalizeMessageContent(rawReply);
          if (cleaned.length > 0 || toolSteps) {
            setMessages((current) => [
              ...current,
              {
                messageId: `local-${Date.now()}`,
                role: "assistant",
                content: cleaned,
                createdAt: new Date().toISOString(),
                toolSteps,
              },
            ]);
          }
        }
        pendingToolStepsRef.current = [];
        setStreamingToolSteps([]);
        setStreamingContent(null);
        setTyping(false);

        const sandboxSessionId = sessionIdRef.current;
        const sandboxGatewayEndpoint = gatewayEndpointRef.current;
        if (sandboxSessionId && sandboxGatewayEndpoint) {
          void syncSandboxHistory(sandboxGatewayEndpoint, sandboxSessionId)
            .catch(() => {
              // 历史同步失败时保留当前已渲染内容
            })
            .finally(() => {
              setSessionListRefreshKey((k) => k + 1);
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
      setSandboxConnected(state === "open");
      if (state === "open") {
        settleOpen();
      }
      if (state === "closed" || state === "error") {
        settleOpen(new Error("沙箱连接未建立，无法发送消息"));
        setTyping(false);
        setStreamingContent(null);
        pendingToolStepsRef.current = [];
        setStreamingToolSteps([]);
        rawStreamingContentRef.current = "";
      }
    };

    ws.connect();
    wsRef.current = ws;
    await waitForOpen;
  }

  async function loadChat(instanceId: string) {
    setLoading(true);
    setError("");

    try {
      const [detail, gatewayEndpointResult] = await Promise.all([
        api.employeeRuntime.getEmployee(instanceId),
        api.employeeRuntime.getSandboxGatewayEndpoint(instanceId),
      ]);

      setEmployee(detail);
      sessionIdRef.current = `instance:${instanceId}:inapp`;
      setSelectedSessionId(sessionIdRef.current);

      // 调用新 API 获取 gateway endpoint（与 HiringPage 一致）
      // VITE_SANDBOX_URL 有值时固定使用本地端点，便于本地联调
      const gatewayEndpoint = resolveGatewayEndpoint(gatewayEndpointResult);
      console.log(
        "[InstanceChatPage] gatewayEndpoint from API:",
        gatewayEndpoint,
      );

      if (gatewayEndpoint) {
        gatewayEndpointRef.current = gatewayEndpoint;
        try {
          const latestSessionId = await fetchLatestGatewaySession(gatewayEndpoint);
          if (latestSessionId) {
            sessionIdRef.current = latestSessionId;
            setSelectedSessionId(latestSessionId);
            await syncSandboxHistory(gatewayEndpoint, latestSessionId);
          }
          await connectSandboxWsWithRecovery(gatewayEndpoint);
        } catch (sandboxError: unknown) {
          setError(normalizeErrorMessage(sandboxError));
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

  async function handleSelectSession(sessionId: string) {
    if (sessionSwitching || sessionId === selectedSessionId) return;
    const previousSelectedSessionId = selectedSessionId;
    const previousActiveSessionId = sessionIdRef.current;
    setSelectedSessionId(sessionId);
    setSessionSwitching(true);
    setStreamingContent(null);
    pendingToolStepsRef.current = [];
    setStreamingToolSteps([]);
    setTyping(false);
    setError("");

    const endpoint = gatewayEndpointRef.current;
    if (!endpoint) {
      setSelectedSessionId(previousSelectedSessionId);
      setSessionSwitching(false);
      return;
    }

    // Treat the selected history session as the active writable chat.
    try {
      const sandboxMessages = await fetchSandboxSessionMessages(
        endpoint,
        sessionId,
      );
      const mapped = mapSandboxMessages(sandboxMessages);
      setMessages(mapped);
      sessionIdRef.current = sessionId;
      await connectSandboxWsWithRecovery(endpoint);
    } catch (sessionError: unknown) {
      setError(normalizeErrorMessage(sessionError));
      setSelectedSessionId(previousSelectedSessionId);
      sessionIdRef.current = previousActiveSessionId;
      if (previousActiveSessionId) {
        void connectSandboxWsWithRecovery(endpoint);
      }
    } finally {
      setSessionSwitching(false);
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
  }, [messages, sending, typing, streamingContent, streamingToolSteps]);

  useEffect(() => {
    const element = textareaRef.current;
    if (!element) {
      return;
    }

    element.style.height = "auto";
    const minHeight = expandOpen ? 180 : 96;
    const maxHeight = expandOpen ? 420 : 220;
    element.style.height = `${Math.min(
      Math.max(element.scrollHeight, minHeight),
      maxHeight,
    )}px`;
  }, [draft.content, expandOpen]);

  useEffect(() => {
    setSlashMenuIdx((current) =>
      Math.min(current, Math.max(slashCandidates.length - 1, 0)),
    );
  }, [slashCandidates.length]);

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
    const readyFiles = pendingFiles.filter(
      (file) => file.status === "已上传" && Boolean(file.marker),
    );
    if (!content && readyFiles.length === 0) {
      return;
    }

    if (await handleLocalCommand(content)) {
      return;
    }

    if (sending) {
      return;
    }

    if (pendingFiles.some((file) => file.status === "上传中")) {
      setError("仍有附件在上传中，请稍候再发送");
      return;
    }

    const erroredFiles = pendingFiles.filter((file) => file.status === "上传失败");
    if (erroredFiles.length > 0) {
      setError(`附件上传失败：${erroredFiles.map((file) => file.name).join("、")}`);
      return;
    }

    setSending(true);
    setError("");

    // 收集待发送的文件
    const incoming = readyFiles.length > 0 ? [...readyFiles] : [];

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
    if (
      !sandboxGatewayEndpoint ||
      !sandboxSessionId ||
      !sandboxWs ||
      !sandboxWs.isOpen()
    ) {
      setError("沙箱连接未建立，无法发送消息");
      setMessages((prev) =>
        prev.filter((message) => message.messageId !== optimistic.messageId),
      );
      setDraft({ content });
      setPendingFiles(incoming);
      setSending(false);
      void connectSandboxWsWithRecovery(sandboxGatewayEndpoint);
      return;
    }

    try {
      if (incoming.length > 0) {
        const markers = incoming
          .filter((file) => Boolean(file.marker))
          .map(
            (file) =>
              `${file.marker}\nAttached file: ${file.name} (${formatFileSize(file.size)})`,
          );
        if (markers.length > 0) {
          messageText = `${markers.join("\n")}\n\n${messageText}`;
        }
      }

      const sent = sandboxWs.send({
        type: "user_message",
        text: messageText,
        sessionId: sandboxSessionId,
      });
      if (!sent) {
        throw new Error("沙箱连接尚未就绪，请稍后重试");
      }
      setTyping(true);
      setTimeout(() => setSessionListRefreshKey((k) => k + 1), 1500);
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

  async function handleNewChat() {
    if (!id) return;

    const newSessionId = `instance:${id}:inapp-${Date.now()}`;
    sessionIdRef.current = newSessionId;
    setSelectedSessionId(newSessionId);
    setMessages([]);
    setStreamingContent(null);
    pendingToolStepsRef.current = [];
    setStreamingToolSteps([]);
    setTyping(false);
    setSessionListRefreshKey((current) => current + 1);
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
      pendingToolStepsRef.current = [];
      setStreamingToolSteps([]);
      setTyping(false);

      await api.employeeRuntime.clearInstanceChatMessages(id);
      setMessages([]);

      const endpoint = gatewayEndpointRef.current;
      if (endpoint && sessionIdRef.current) {
        void connectSandboxWsWithRecovery(endpoint);
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

  const location = useLocation();
  const pageProtocol = window.location.protocol === "https:" ? "https" : "http";

  const handleMediaLinkClick = useCallback(
    async (url: string, fileName: string) => {
      const endpoint = gatewayEndpointRef.current;
      const fullUrl = /^https?:\/\//i.test(url)
        ? url
        : endpoint
          ? `${pageProtocol}://${endpoint.replace(/^\/+/, "").replace(/\/$/, "")}/${url.replace(/^\/+/, "")}`
          : url;

      try {
        const token = await tokenService.ensureFresh();
        const headers: HeadersInit = token
          ? { Authorization: `Bearer ${token}` }
          : {};
        const response = await fetch(fullUrl, { headers });
        if (!response.ok) {
          throw new Error(`下载文件失败: ${response.status}`);
        }

        const blob = await response.blob();
        const blobUrl = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = blobUrl;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        window.setTimeout(() => URL.revokeObjectURL(blobUrl), 10000);
      } catch (requestError: unknown) {
        setError(normalizeErrorMessage(requestError));
      }
    },
    [pageProtocol],
  );

  function selectSlashCommand(command: SlashCommand) {
    setDraft({ content: command.args ? `${command.cmd} ` : command.cmd });
    setSlashMenuIdx(0);
  }

  function pushAssistantNotice(content: string) {
    setMessages((current) => [
      ...current,
      {
        messageId: `local-assistant-${Date.now()}`,
        role: "assistant",
        content,
        createdAt: new Date().toISOString(),
      },
    ]);
  }

  async function handleLocalCommand(command: string) {
    const normalized = command.trim().toLowerCase();
    if (normalized === "/new") {
      setDraft({ content: "" });
      setPendingFiles([]);
      await handleNewChat();
      return true;
    }

    if (normalized === "/clear") {
      setDraft({ content: "" });
      setPendingFiles([]);
      await handleClear();
      return true;
    }

    if (normalized === "/stop") {
      setDraft({ content: "" });
      handleStop();
      return true;
    }

    if (normalized === "/help") {
      setDraft({ content: "" });
      pushAssistantNotice([
        "### 聊天命令",
        "",
        "- `/new` 新建会话",
        "- `/clear` 清空当前对话",
        "- `/stop` 终止当前生成",
        "- `/think low|medium|high` 调整思考强度",
        "- `Shift+Enter` 换行，`Enter` 发送",
      ].join("\n"));
      return true;
    }

    return false;
  }

  function handleStop() {
    const currentSessionId = sessionIdRef.current;
    if (!currentSessionId) {
      setError("当前会话尚未建立，暂时无法终止生成");
      return;
    }

    if (!wsRef.current?.isOpen()) {
      setError("沙箱连接未建立，无法终止当前生成");
      return;
    }

    setError("");
    wsRef.current.send({
      type: "user_message",
      text: "/stop",
      sessionId: currentSessionId,
    });
  }

  return (
    <div className="hb-page">
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
        <div className="flex h-[calc(100vh-116px)] min-h-[640px] gap-3">
          {/* 左侧：会话历史列表 */}
          {gatewayEndpointRef.current && sessionListVisible && (
            <SessionListPanel
              gatewayEndpoint={gatewayEndpointRef.current}
              currentSessionId={selectedSessionId}
              onSelectSession={(sessionId) =>
                void handleSelectSession(sessionId)
              }
              onNewChat={() => void handleNewChat()}
              refreshTrigger={sessionListRefreshKey}
            />
          )}

          {/* 右侧：主聊天卡片 */}
          <div className="hb-card flex min-w-0 flex-1 flex-col overflow-hidden">

            {/* 紧凑头部 */}
            <div className="flex shrink-0 items-center justify-between gap-3 border-b ic-chat-divider px-5 py-3.5">
              <div className="flex min-w-0 items-center gap-3">
                <div
                  className={`hb-user-avatar shrink-0 !h-9 !w-9 !rounded-xl !text-sm ${ownershipClass(employeeView.ownership)}`}
                >
                  {firstCharacter(employee.nickname)}
                </div>
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <span className="truncate text-[15px] font-semibold ic-text-title">
                      {employee.nickname}
                    </span>
                    <span
                      className={`hb-pill shrink-0 ${statusClass(employeeView.mappedStatus, employee.lifecycleStatus)}`}
                    >
                      {statusLabel(
                        employeeView.mappedStatus,
                        employee.lifecycleStatus,
                      )}
                    </span>
                    <span
                      className={`hb-pill shrink-0 ${ownershipClass(employeeView.ownership)}`}
                    >
                      {ownershipLabel(employeeView.ownership)}
                    </span>
                  </div>
                  <p className="mt-0.5 text-[11px] ic-text-secondary">
                    实时对话 · 消息直达分身沙箱
                  </p>
                </div>
              </div>

              <div className="flex shrink-0 items-center gap-2">
                <span
                  className={`rounded-full border px-2.5 py-1 text-[11px] ${sandboxConnected ? "ic-badge-connected" : "ic-badge-disconnected"}`}
                >
                  {sandboxConnected ? "已连接" : "未连接"}
                </span>
                <button
                  type="button"
                  className="hb-btn-ghost !px-3 !py-1.5 !text-[12px]"
                  onClick={handleClear}
                  disabled={clearing || messages.length === 0}
                >
                  <Trash2 size={13} />
                  {clearing ? "清空中" : "清空"}
                </button>
                <button
                  type="button"
                  className="hb-btn-primary !px-3 !py-1.5 !text-[12px]"
                  onClick={() =>
                    navigate(
                      instanceBasePath(location.pathname, employee.employeeId),
                    )
                  }
                >
                  <MessageCircle size={13} />
                  查看详情
                </button>
              </div>
            </div>

            {/* 消息列表 */}
            <div className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-5 py-5">
              {messages.length === 0 && !typing ? (
                <div className="m-auto flex flex-col items-center gap-3 text-center">
                  <div className="rounded-2xl border ic-empty-icon p-4">
                    <MessageCircle size={22} className="ic-text-secondary" />
                  </div>
                  <div>
                    <p className="text-[14px] font-medium ic-text-title">
                      和 {employee.nickname} 开始对话
                    </p>
                    <p className="mt-1 text-[12px] ic-text-secondary">
                      发送消息，分身会实时响应
                    </p>
                  </div>
                </div>
              ) : (
                messages.map((message) => {
                  const isUser = message.role === "user";
                  return (
                    <div
                      key={message.messageId}
                      className={`flex ${isUser ? "justify-end" : "justify-start"}`}
                    >
                      {!isUser && (
                        <div
                          className={`hb-hiring-avatar mr-2.5 mt-0.5 shrink-0 rounded-[8px] text-[12px] ${ownershipClass(employeeView.ownership)}`}
                        >
                          {firstCharacter(employee.nickname)}
                        </div>
                      )}
                      <div
                        className={`flex min-w-0 max-w-[80%] flex-col ${isUser ? "items-end" : "items-start"}`}
                      >
                        {!isUser &&
                          message.toolSteps &&
                          message.toolSteps.length > 0 && (
                            <div className="mb-2 w-full">
                              <HiringToolStepsBlock steps={message.toolSteps} />
                            </div>
                          )}
                        <div className={isUser ? "ic-bubble-user" : "ic-bubble-assistant"}>
                          <InstanceChatMessageBody
                            content={message.content}
                            role={
                              message.role === "assistant" ? "assistant" : "user"
                            }
                            onMediaLinkClick={handleMediaLinkClick}
                          />
                        </div>
                        <div className="mt-1 ic-meta-secondary">
                          {isUser ? "我" : employee.nickname} ·{" "}
                          {formatTime(message.createdAt)}
                        </div>
                      </div>
                      {isUser && (
                        <div className="ic-avatar-user ml-2.5 mt-0.5">我</div>
                      )}
                    </div>
                  );
                })
              )}

              {/* 流式响应 / 思考动画 */}
              {typing && (
                <div className="flex justify-start">
                  <div
                    className={`hb-hiring-avatar mr-2.5 mt-0.5 shrink-0 rounded-[8px] text-[12px] ${ownershipClass(employeeView.ownership)}`}
                  >
                    {firstCharacter(employee.nickname)}
                  </div>
                  <div className="flex min-w-0 max-w-[80%] flex-col items-start">
                    {streamingToolSteps.length > 0 && (
                      <div className="mb-2 w-full">
                        <HiringToolStepsBlock steps={streamingToolSteps} />
                      </div>
                    )}
                    <div className="ic-bubble-assistant">
                      {streamingContent !== null &&
                      streamingContent.length > 0 ? (
                        <InstanceChatMessageBody
                          content={streamingContent}
                          role="assistant"
                          streaming
                          onMediaLinkClick={handleMediaLinkClick}
                        />
                      ) : (
                        <div className="flex items-center gap-1.5 py-0.5">
                          {[0, 1, 2].map((i) => (
                            <span
                              key={i}
                              className="ic-typing-dot"
                              style={{ animationDelay: `${i * 0.2}s` }}
                            />
                          ))}
                        </div>
                      )}
                    </div>
                    <div className="mt-1 ic-meta-secondary">
                      {employee.nickname} · 正在回复
                    </div>
                  </div>
                </div>
              )}

              {sessionSwitching && (
                <div className="rounded-xl border px-4 py-2 text-center text-[12px] ic-switching-bar">
                  正在切换会话...
                </div>
              )}

              <div ref={bottomRef} />
            </div>

            {/* 编辑区 */}
            <div className="hb-chat-compose">
              {/* 待上传文件列表 */}
              {pendingFiles.length > 0 && (
                <div className="mb-3 flex flex-wrap gap-2">
                  {pendingFiles.map((file) => (
                    <div
                      key={file.id}
                      className={`hb-chat-file-chip is-${
                        file.status === "上传失败"
                          ? "error"
                          : file.status === "上传中"
                            ? "loading"
                            : "ready"
                      }`}
                    >
                      {file.status === "上传中" ? (
                        <Loader2 size={12} className="hb-chat-file-chip-spin" />
                      ) : file.status === "上传失败" ? (
                        <AlertCircle size={12} className="text-[#dc2626]" />
                      ) : (
                        <FileText size={12} className="text-[#9ca3af]" />
                      )}
                      <span className="max-w-[200px] truncate">{file.name}</span>
                      <span className="hb-chat-file-chip-meta">
                        {file.status === "上传失败"
                          ? file.uploadError || file.status
                          : file.status}
                      </span>
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
              <div className="hb-chat-compose-main">
                <input
                  ref={fileRef}
                  type="file"
                  multiple
                  onChange={handleFileInputChange}
                  className="hidden"
                  disabled={sending || !isLive}
                />
                <button
                  type="button"
                  className="hb-chat-attach-btn"
                  onClick={triggerFileUpload}
                  disabled={sending || !isLive}
                  title="上传文件"
                >
                  <Paperclip size={16} />
                </button>
                <div
                  className={`hb-chat-input-wrap ${expandOpen ? "is-expanded" : ""}`}
                >
                  {slashMenuOpen ? (
                    <div className="hb-chat-slash-menu">
                      {slashCandidates.map((command, index) => (
                        <button
                          key={command.cmd}
                          type="button"
                          onMouseDown={(event) => {
                            event.preventDefault();
                            selectSlashCommand(command);
                          }}
                          onMouseEnter={() => setSlashMenuIdx(index)}
                          className={`hb-chat-slash-item ${index === slashMenuIdx ? "is-active" : ""}`}
                        >
                          <span className="hb-chat-slash-cmd">{command.cmd}</span>
                          {command.args ? (
                            <span className="hb-chat-slash-args">
                              {command.args}
                            </span>
                          ) : null}
                          <span className="hb-chat-slash-desc">
                            {command.desc}
                          </span>
                        </button>
                      ))}
                    </div>
                  ) : null}
                  <textarea
                    ref={textareaRef}
                    rows={1}
                    value={draft.content}
                    onChange={(event) => {
                      setDraft({ content: event.target.value });
                      setSlashMenuIdx(0);
                    }}
                    onKeyDown={(event) => {
                      if (slashMenuOpen) {
                        if (event.key === "ArrowDown") {
                          event.preventDefault();
                          setSlashMenuIdx((current) =>
                            Math.min(current + 1, slashCandidates.length - 1),
                          );
                          return;
                        }

                        if (event.key === "ArrowUp") {
                          event.preventDefault();
                          setSlashMenuIdx((current) =>
                            Math.max(current - 1, 0),
                          );
                          return;
                        }

                        if (
                          event.key === "Tab" ||
                          (event.key === "Enter" && !event.shiftKey)
                        ) {
                          event.preventDefault();
                          selectSlashCommand(slashCandidates[slashMenuIdx]);
                          return;
                        }

                        if (event.key === "Escape") {
                          event.preventDefault();
                          setDraft({ content: "" });
                          return;
                        }
                      }

                      if (event.key === "Enter" && !event.shiftKey) {
                        event.preventDefault();
                        void handleSend();
                      }
                    }}
                    placeholder={
                      isLive
                        ? "输入消息，Enter 发送，Shift+Enter 换行，/stop 终止当前生成"
                        : "当前实例未上岗，不能对话"
                    }
                    disabled={sending || !isLive}
                  />
                  <button
                    type="button"
                    className="hb-chat-expand-btn"
                    onClick={() => setExpandOpen((current) => !current)}
                    title={expandOpen ? "收起输入框" : "放大输入框"}
                  >
                    {expandOpen ? (
                      <Minimize2 size={14} />
                    ) : (
                      <Maximize2 size={14} />
                    )}
                  </button>
                </div>

                {isAiWorking ? (
                  <button
                    type="button"
                    className="hb-btn-primary hb-chat-stop-btn"
                    onClick={handleStop}
                    disabled={!isLive || sessionSwitching || !selectedSessionId}
                  >
                    <Square size={14} />
                    终止
                  </button>
                ) : (
                  <button
                    type="button"
                    className="hb-btn-primary hb-chat-send-btn"
                    onClick={() => void handleSend()}
                    disabled={
                      sending ||
                      sessionSwitching ||
                      !isLive ||
                      !sandboxConnected ||
                      (draft.content.trim().length === 0 &&
                        pendingFiles.length === 0)
                    }
                  >
                    <Send size={14} />
                  </button>
                )}
              </div>
              {!isLive && (
                <p className="mt-3 text-xs text-[#9ca3af]">
                  只有 live 状态的分身和私有分支才能进入站内对话。
                </p>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
