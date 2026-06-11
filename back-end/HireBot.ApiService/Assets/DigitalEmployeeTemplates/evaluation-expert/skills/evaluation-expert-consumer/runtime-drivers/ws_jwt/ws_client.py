"""
ws_client.py — WebSocket connect + message collection (atomic module)

Responsibilities:
  - Connect to the evaluatee Gateway WebSocket
  - Send a user message
  - Collect every server-pushed message verbatim
  - Return the full message list of one turn after assistant_done

No evaluation logic. No semantic parsing.

Endpoint formats accepted (any of):
  - HOST:PORT                              -> auto-prefixed ws:// and /ws
  - ws://HOST:PORT/path/to/ws              -> used as-is, token appended
  - http://HOST:PORT/path                  -> rewritten to ws://, /ws appended

WebSocket URL normalization and client helpers for the consumer ws_jwt driver.
"""

import asyncio
import json
import re
import time
from datetime import datetime, timezone
from typing import Any, Callable

import websockets
from websockets.exceptions import ConnectionClosed


def build_ws_url(endpoint: str, token: str) -> str:
    """Mirror of frontend gateway-ws.ts buildGatewayWsUrl."""
    base = endpoint.strip()

    if re.match(r'^https?://', base, re.IGNORECASE):
        base = re.sub(r'^http', 'ws', base, flags=re.IGNORECASE)
    elif not re.match(r'^wss?://', base, re.IGNORECASE):
        base = f"ws://{base.lstrip('/')}"

    base = re.sub(r'([?&])token=[^&]*(&)?', r'\1', base)
    base = base.rstrip('?&')

    if not re.search(r'/ws($|[?#])', base, re.IGNORECASE):
        base = base.rstrip('/') + '/ws'

    sep = '&' if '?' in base else '?'
    return f"{base}{sep}token={token}"


class WsCollector:
    """
    Connect to a single Gateway WebSocket and collect messages.

    Usage:
        async with WsCollector(endpoint, token) as collector:
            messages = await collector.send_and_collect("user message")
    """

    def __init__(
        self,
        endpoint: str,
        token: str,
        timeout: int = 60,
        log_fn: Callable[[str, str], None] | None = None,
    ):
        self.endpoint = endpoint
        self.token = token
        self.timeout = timeout
        self._ws = None
        self._log_fn = log_fn

    def _emit_log(self, level: str, msg: str) -> None:
        """Forward to the provided log_fn if present; otherwise no-op
        (run.py already logs via _log() at call sites for timeout/error).
        """
        if self._log_fn is not None:
            self._log_fn(level, msg)

    @property
    def ws_url(self) -> str:
        return build_ws_url(self.endpoint, self.token)

    @staticmethod
    def _redact_url(url: str) -> str:
        """Remove token value from URL before logging."""
        return re.sub(r'([?&]token=)[^&]*', r'\1<redacted>', url)

    async def __aenter__(self):
        url = self.ws_url
        self._emit_log("WS", f"connecting: {self._redact_url(url)}")
        self._ws = await websockets.connect(
            url,
            ping_interval=20,
            ping_timeout=10,
            open_timeout=15,
        )
        self._emit_log("WS", "connected")
        return self

    async def __aexit__(self, *args):
        if self._ws:
            await self._ws.close()

    async def send_and_collect(self, user_text: str) -> list[dict[str, Any]]:
        """Send one user message; collect until assistant_done or timeout."""
        self._emit_log("WS", f"→ user_message  text={user_text[:80]!r}")
        payload = json.dumps({"type": "user_message", "text": user_text})
        await self._ws.send(payload)

        collected: list[dict[str, Any]] = []
        deadline = time.monotonic() + self.timeout

        while time.monotonic() < deadline:
            try:
                remaining = deadline - time.monotonic()
                raw = await asyncio.wait_for(self._ws.recv(), timeout=remaining)
            except asyncio.TimeoutError:
                self._emit_log("WS", f"← recv timeout after {self.timeout}s  collected={len(collected)}")
                break
            except ConnectionClosed:
                self._emit_log("WS", f"← connection closed  collected={len(collected)}")
                break

            try:
                msg = json.loads(raw)
            except json.JSONDecodeError:
                msg = {"_raw": raw}

            msg["_received_at"] = datetime.now(timezone.utc).isoformat()
            collected.append(msg)

            msg_type = msg.get("type", "?")
            # assistant_chunk 数量多但无额外诊断价值，跳过逐条打印，在 assistant_done 汇总
            if msg_type != "assistant_chunk":
                self._emit_log("WS", f"← {msg_type}  total={len(collected)}")

            if msg_type == "assistant_done":
                chunk_count = sum(1 for m in collected if m.get("type") == "assistant_chunk")
                self._emit_log("WS", f"← assistant_done  chunks={chunk_count}  total={len(collected)}")
                break

        return collected

    async def approve_tool(self, call_id: str, approved: bool = True) -> None:
        """Approve a pending tool call (for approval_required flows)."""
        self._emit_log("WS", f"→ approve_tool  callId={call_id!r}  approved={approved}")
        payload = json.dumps({
            "type": "approve_tool",
            "callId": call_id,
            "approved": approved,
        })
        await self._ws.send(payload)
