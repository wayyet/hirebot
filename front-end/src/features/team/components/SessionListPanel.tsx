import { useEffect, useState, useCallback } from "react";
import {
  List,
  MessageCircle,
  Plus,
  Search,
  Loader2,
  ChevronLeft,
  ChevronRight,
} from "lucide-react";
import {
  fetchAdminSessions,
  fetchSandboxSessionMessages,
  type SessionSummary,
  type SandboxMessage,
} from "@/infra/sandbox/sandbox-api";

export interface SessionListPanelProps {
  gatewayEndpoint: string;
  currentSessionId: string | null;
  onSelectSession: (sessionId: string) => void;
  onNewChat: () => void;
  refreshTrigger?: number;
}

function formatLastActive(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  const now = Date.now();
  const diffMs = now - date.getTime();
  const diffMin = Math.floor(diffMs / 60000);
  if (diffMin < 1) return "刚刚";
  if (diffMin < 60) return `${diffMin} 分钟前`;
  const diffHour = Math.floor(diffMin / 60);
  if (diffHour < 24) return `${diffHour} 小时前`;
  const diffDay = Math.floor(diffHour / 24);
  if (diffDay < 7) return `${diffDay} 天前`;
  return date.toLocaleString("zh-CN", {
    month: "numeric",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function getMessageText(message?: SandboxMessage): string {
  const value =
    typeof message?.text === "string" ? message.text : message?.content;
  if (typeof value !== "string") return "";
  return value.replace(/<[^>]*>/g, "").replace(/\s+/g, " ").trim();
}

function buildSessionPreview(messages: SandboxMessage[]): string {
  const firstUserMessage = messages.find(
    (message) => message.type === "user_message",
  );
  const fallbackMessage = messages.find(
    (message) => message.type === "assistant_message",
  );
  return getMessageText(firstUserMessage ?? fallbackMessage);
}

export default function SessionListPanel({
  gatewayEndpoint,
  currentSessionId,
  onSelectSession,
  onNewChat,
  refreshTrigger = 0,
}: SessionListPanelProps) {
  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [hasMore, setHasMore] = useState(false);
  const [collapsed, setCollapsed] = useState(false);
  const [loadMoreLoading, setLoadMoreLoading] = useState(false);
  const [sessionPreviews, setSessionPreviews] = useState<
    Record<string, string>
  >({});

  const loadSessions = useCallback(
    async (pageNum: number, searchTerm: string, append: boolean) => {
      if (pageNum === 1 && !append) setLoading(true);
      else setLoadMoreLoading(true);

      try {
        const resp = await fetchAdminSessions(gatewayEndpoint, {
          page: pageNum,
          pageSize: 25,
          search: searchTerm || undefined,
        });
        const items = resp.persisted.items;
        setSessions((prev) => (append ? [...prev, ...items] : items));
        setHasMore(resp.persisted.hasMore);
        setPage(pageNum);
      } catch {
        if (!append) setSessions([]);
      } finally {
        setLoading(false);
        setLoadMoreLoading(false);
      }
    },
    [gatewayEndpoint],
  );

  useEffect(() => {
    void loadSessions(1, search, false);
  }, [loadSessions, search, refreshTrigger]);

  useEffect(() => {
    if (sessions.length === 0) {
      setSessionPreviews({});
      return;
    }

    const missingSessions = sessions.filter(
      (session) => sessionPreviews[session.id] === undefined,
    );
    if (missingSessions.length === 0) return;

    let cancelled = false;
    void Promise.allSettled(
      missingSessions.map(async (session) => {
        const messages = await fetchSandboxSessionMessages(
          gatewayEndpoint,
          session.id,
        );
        return [session.id, buildSessionPreview(messages)] as const;
      }),
    ).then((results) => {
      if (cancelled) return;
      setSessionPreviews((prev) => {
        const next = { ...prev };
        for (const session of missingSessions) {
          next[session.id] = "";
        }
        for (const result of results) {
          if (result.status === "fulfilled") {
            const [sessionId, preview] = result.value;
            next[sessionId] = preview;
          }
        }
        return next;
      });
    });

    return () => {
      cancelled = true;
    };
  }, [gatewayEndpoint, sessions, sessionPreviews]);

  const handleSearchChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setSearch(e.target.value);
    },
    [],
  );

  const handleLoadMore = useCallback(() => {
    void loadSessions(page + 1, search, true);
  }, [loadSessions, page, search]);

  const panelWidth = collapsed ? 40 : 280;

  return (
    <div
      className="flex-shrink-0 rounded-2xl border border-[#ececec] bg-white shadow-sm transition-all duration-200"
      style={{ width: panelWidth }}
    >
      {/* 折叠按钮 */}
      <div className="flex items-center justify-between px-3 py-2">
        {!collapsed && (
          <span className="text-xs font-medium text-[#737373]">会话列表</span>
        )}
        <button
          type="button"
          onClick={() => setCollapsed(!collapsed)}
          className="rounded p-0.5 text-[#9ca3af] hover:bg-[#ececec] hover:text-[#525252]"
        >
          {collapsed ? <ChevronRight size={14} /> : <ChevronLeft size={14} />}
        </button>
      </div>

      {/* 新对话按钮 */}
      {!collapsed && (
        <div className="px-3 pb-3">
          <button
            type="button"
            onClick={onNewChat}
            className="flex w-full items-center justify-center gap-1.5 rounded-md border border-[#d2e3fc] bg-[#e8f0fe] px-2 py-1.5 text-xs font-medium text-[#1967d2] hover:bg-[#d2e3fc] transition-colors"
          >
            <Plus size={13} />
            新对话
          </button>
        </div>
      )}

      {collapsed ? (
        <div className="flex justify-center py-2">
          <List size={14} className="text-[#9ca3af]" />
        </div>
      ) : (
        <>
          {/* 搜索框 */}
          <div className="px-3 pb-3">
            <div className="flex items-center gap-1.5 rounded-md border border-[#ececec] bg-white px-2 py-1">
              <Search size={12} className="text-[#9ca3af]" />
              <input
                type="text"
                value={search}
                onChange={handleSearchChange}
                placeholder="搜索 senderId..."
                className="flex-1 border-none bg-transparent py-0.5 text-xs text-[#404040] outline-none placeholder:text-[#c4c4c4]"
              />
            </div>
          </div>

          {/* 会话列表 */}
          <div
            className="overflow-y-auto px-2 pb-3"
            style={{ maxHeight: "calc(100vh - 200px)" }}
          >
            {loading ? (
              <div className="flex items-center justify-center gap-1.5 py-6 text-xs text-[#9ca3af]">
                <Loader2 size={12} className="animate-spin" />
                加载中...
              </div>
            ) : sessions.length === 0 ? (
              <div className="py-6 text-center text-xs text-[#9ca3af]">
                暂无会话记录
              </div>
            ) : (
              <>
                {sessions.map((session) => (
                  <button
                    key={session.id}
                    type="button"
                    onClick={() => onSelectSession(session.id)}
                    className={`mb-1.5 w-full rounded-xl px-2.5 py-2 text-left transition-colors ${
                      session.id === currentSessionId
                        ? "bg-[#e8f0fe] text-[#1967d2]"
                        : "hover:bg-[#ececec] text-[#404040]"
                    }`}
                  >
                    <div className="flex items-center gap-1.5">
                      <MessageCircle size={11} className="flex-shrink-0" />
                      <span className="truncate text-xs font-medium">
                        {sessionPreviews[session.id] ||
                          session.senderId ||
                          session.id}
                      </span>
                    </div>
                    <div className="mt-0.5 flex items-center gap-2 text-[10px] text-[#9ca3af]">
                      <span>{formatLastActive(session.lastActiveAt)}</span>
                      <span>{session.historyTurns} 轮</span>
                      {session.isActive && (
                        <span className="text-[#34a853]">● 活跃</span>
                      )}
                    </div>
                  </button>
                ))}

                {hasMore && (
                  <button
                    type="button"
                    onClick={handleLoadMore}
                    disabled={loadMoreLoading}
                    className="mt-1 w-full rounded-lg px-2.5 py-1.5 text-center text-xs text-[#1967d2] hover:bg-[#ececec] disabled:text-[#9ca3af]"
                  >
                    {loadMoreLoading ? "加载中..." : "加载更多"}
                  </button>
                )}
              </>
            )}
          </div>
        </>
      )}
    </div>
  );
}
