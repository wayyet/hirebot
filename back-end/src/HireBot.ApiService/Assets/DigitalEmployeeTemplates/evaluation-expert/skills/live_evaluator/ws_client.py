"""
ws_client.py - WebSocket 连接与消息采集

职责：
  - 建立评估沙箱到目标沙箱的 WebSocket 连接
  - 发送包装后的评估执行指令
  - 原样采集目标沙箱返回的所有消息
  - 等待 assistant_done 后结束单题采集

支持 query token 和 Authorization header 两种鉴权传输方式。
"""

import asyncio
import json
import re
import time
from datetime import datetime, timezone
from typing import Any

import websockets
from websockets.exceptions import ConnectionClosed


def build_ws_url(endpoint: str, token: str | None = None, query_param_name: str = "token") -> str:
    """
    对齐前端 gateway-ws.ts 的 buildGatewayWsUrl 逻辑。

    支持：
      - 裸 HOST:PORT          → ws://HOST:PORT/ws
      - http(s)://...         → ws(s)://...[/ws]
      - ws(s)://...           → 直接使用
    """
    base = endpoint.strip()

    # http/https → ws/wss
    if re.match(r'^https?://', base, re.IGNORECASE):
        base = re.sub(r'^http', 'ws', base, flags=re.IGNORECASE)
    elif not re.match(r'^wss?://', base, re.IGNORECASE):
        # 裸 HOST:PORT，补协议
        base = f"ws://{base.lstrip('/')}"

    # 去掉已有的 token 参数（避免重复）
    base = re.sub(rf'([?&]){re.escape(query_param_name)}=[^&]*(&)?', r'\1', base)
    base = base.rstrip('?&')

    # 如果路径里没有 /ws，追加
    if not re.search(r'/ws($|[?#])', base, re.IGNORECASE):
        base = base.rstrip('/') + '/ws'

    if not token:
        return base

    sep = '&' if '?' in base else '?'
    return f"{base}{sep}{query_param_name}={token}"


class WsCollector:
    """
    连接单个 Gateway WebSocket，采集消息。

    用法：
        async with WsCollector(endpoint, token) as collector:
            messages = await collector.send_and_collect("用户消息")
    """

    def __init__(
        self,
        endpoint: str,
        token: str,
        timeout: int = 60,
        ws_transport: str = "query",
        ws_query_param: str = "token",
        additional_headers: dict[str, str] | None = None,
    ):
        """
        Args:
            endpoint: Gateway 地址（支持多种格式，见模块说明）
            token:    已解析的 access token
            timeout:  等待 assistant_done 的超时秒数
        """
        self.endpoint = endpoint
        self.token = token
        self.timeout = timeout
        self.ws_transport = ws_transport
        self.ws_query_param = ws_query_param
        self.additional_headers = additional_headers or {}
        self._ws = None

    @property
    def ws_url(self) -> str:
        query_token = self.token if self.ws_transport == "query" else None
        return build_ws_url(self.endpoint, query_token, self.ws_query_param)

    async def __aenter__(self):
        url = self.ws_url
        print(f"[WS] 连接: {url}")
        self._ws = await websockets.connect(
            url,
            ping_interval=20,
            ping_timeout=10,
            open_timeout=15,
            additional_headers=self.additional_headers,
        )
        print(f"[WS] 已连接")
        return self

    async def __aexit__(self, *args):
        if self._ws:
            await self._ws.close()

    async def send_and_collect(self, user_text: str, auto_approve: bool = False) -> list[dict[str, Any]]:
        """
        发送一条用户消息，收集直到 assistant_done 的所有服务端消息。

        Returns:
            原始消息列表，每条附加 _received_at 时间戳字段
        """
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

            if auto_approve and msg.get("type") == "approval_required" and msg.get("callId"):
                await self.approve_tool(msg["callId"], approved=True)

            if msg.get("type") == "assistant_done":
                break

        return collected

    async def approve_tool(self, call_id: str, approved: bool = True) -> None:
        """审批工具调用（如果 approval_required 出现时使用）。"""
        payload = json.dumps({
            "type": "approve_tool",
            "callId": call_id,
            "approved": approved,
        })
        await self._ws.send(payload)
