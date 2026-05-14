import ReactMarkdown from "react-markdown";
import rehypeHighlight from "rehype-highlight";
import remarkGfm from "remark-gfm";

function normalizeThinkMarkdown(content: string): string {
  const withThinkBlocks = content.replace(
    /<think>([\s\S]*?)<\/think>/gi,
    (_, rawInner: string) => {
      const inner = String(rawInner).trim();
      if (!inner) {
        return "\n\n";
      }

      const quoted = inner
        .split(/\r?\n/)
        .map((line) => `> ${line}`)
        .join("\n");

      return `\n\n> 思考\n${quoted}\n\n`;
    },
  );

  return withThinkBlocks.replace(/<\/?think>/gi, "");
}

export function InstanceChatMessageBody({
  content,
  role,
  streaming = false,
  onMediaLinkClick,
}: {
  content: string;
  role: "user" | "assistant";
  streaming?: boolean;
  onMediaLinkClick?: (url: string, fileName: string) => void;
}) {
  const normalized = normalizeThinkMarkdown(content).trim();

  if (!normalized) {
    return null;
  }

  if (streaming) {
    const splitIdx = normalized.lastIndexOf("\n\n");
    const completedMd =
      splitIdx >= 0 ? normalized.slice(0, splitIdx + 1).trim() : "";
    const tail = splitIdx >= 0 ? normalized.slice(splitIdx + 2) : normalized;

    return (
      <div className="hb-chat-markdown">
        {completedMd ? (
          <ReactMarkdown remarkPlugins={[remarkGfm]}>
            {completedMd}
          </ReactMarkdown>
        ) : null}
        <p className="hb-chat-streaming-tail">
          {tail}
          <span className="hb-chat-streaming-cursor" aria-hidden="true">
            ▋
          </span>
        </p>
      </div>
    );
  }

  return (
    <div className="hb-chat-markdown">
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        rehypePlugins={role === "assistant" ? [rehypeHighlight] : undefined}
        components={{
          a: ({ href, children, ...props }) => {
            const url = href ?? "";
            if (
              onMediaLinkClick &&
              (url.includes("/media/") || url.includes("/memory/media-cache/"))
            ) {
              const fallbackName = url.split("/").pop() || "file";
              const fileName =
                children?.toString().trim().replace(/^[⬇\s]+/, "") ||
                fallbackName;

              return (
                <a
                  {...props}
                  href={url}
                  onClick={(event) => {
                    event.preventDefault();
                    onMediaLinkClick(url, fileName);
                  }}
                >
                  {children}
                </a>
              );
            }

            return (
              <a {...props} href={url} target="_blank" rel="noreferrer">
                {children}
              </a>
            );
          },
        }}
      >
        {normalized}
      </ReactMarkdown>
    </div>
  );
}