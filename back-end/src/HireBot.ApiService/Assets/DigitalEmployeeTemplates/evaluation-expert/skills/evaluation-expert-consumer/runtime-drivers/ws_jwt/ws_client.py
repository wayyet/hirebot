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
from typing import Any

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

    def __init__(self, endpoint: str, token: str, timeout: int = 60):
        self.endpoint = endpoint
        self.token = token
        self.timeout = timeout
        self._ws = None

    @property
    def ws_url(self) -> str:
        return build_ws_url(self.endpoint, self.token)

    async def __aenter__(self):
        url = self.ws_url
        import sys as _sys
        print(f"[ws_jwt] connect: {url}", file=_sys.stderr)
        self._ws = await websockets.connect(
            url,
            ping_interval=20,
            ping_timeout=10,
            open_timeout=15,
            additional_headers={},
        )
        print(f"[ws_jwt] connected", file=_sys.stderr)
        return self

    async def __aexit__(self, *args):
        if self._ws:
            await self._ws.close()

    async def send_and_collect(self, user_text: str) -> list[dict[str, Any]]:
        """Send one user message; collect until assistant_done or timeout."""
        payload = json.dumps({"type": "user_message", "text": user_text})
        await self._ws.send(payload)

        collected: list[dict[str, Any]] = []
        deadline = time.monotonic() + self.timeout

        while time.monotonic() < deadline:
            try:
                remaining = deadline - time.monotonic()
                raw = await asyncio.wait_for(self._ws.recv(), timeout=remaining)
            except asyncio.TimeoutError:
                break
            except ConnectionClosed:
                break

            try:
                msg = json.loads(raw)
            except json.JSONDecodeError:
                msg = {"_raw": raw}

            msg["_received_at"] = datetime.now(timezone.utc).isoformat()
            collected.append(msg)

            if msg.get("type") == "assistant_done":
                break

        return collected

    async def approve_tool(self, call_id: str, approved: bool = True) -> None:
        """Approve a pending tool call (for approval_required flows)."""
        payload = json.dumps({
            "type": "approve_tool",
            "callId": call_id,
            "approved": approved,
        })
        await self._ws.send(payload)
