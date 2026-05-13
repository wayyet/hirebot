"""
auth_client.py - 目标沙箱访问鉴权

职责：
  - 根据运行时上下文解析目标沙箱鉴权配置
  - 支持 static_token / password / client_credentials 三种模式
  - 输出统一的访问令牌与 WebSocket / HTTP 传输配置

不负责业务执行，也不记录敏感信息到结果文件。
"""

from __future__ import annotations

import json
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True)
class ResolvedAuth:
    """统一的鉴权结果。"""

    access_token: str
    token_type: str = "Bearer"
    ws_transport: str = "query"
    ws_query_param: str = "token"
    http_header_name: str = "Authorization"
    http_scheme: str = "Bearer"
    source: str = "static_token"

    def build_http_headers(self) -> dict[str, str]:
        """构建 HTTP 调用头。"""
        token_value = self.access_token.strip()
        if not token_value:
            return {}

        scheme = self.http_scheme.strip()
        if scheme:
            return {self.http_header_name: f"{scheme} {token_value}"}
        return {self.http_header_name: token_value}


def resolve_auth(auth_config: dict[str, Any] | None) -> ResolvedAuth:
    """
    解析运行时上下文中的鉴权配置。

    支持：
      - static_token        直接使用 access_token / token
      - password            用户名密码换 token
      - client_credentials  client_id/client_secret 换 token
    """
    config = auth_config or {}
    mode = str(config.get("mode") or "static_token").strip().lower()

    common = {
        "token_type": str(config.get("token_type") or "Bearer").strip() or "Bearer",
        "ws_transport": str(config.get("ws_transport") or "query").strip().lower() or "query",
        "ws_query_param": str(config.get("ws_query_param") or "token").strip() or "token",
        "http_header_name": str(config.get("http_header_name") or "Authorization").strip() or "Authorization",
        "http_scheme": str(config.get("http_scheme") or "Bearer").strip() or "Bearer",
    }

    if mode == "static_token":
        token = _require_non_empty(config.get("access_token") or config.get("token"), "auth.access_token")
        return ResolvedAuth(access_token=token, source="static_token", **common)

    if mode == "password":
        token_url = _require_non_empty(config.get("token_url"), "auth.token_url")
        username = _require_non_empty(config.get("username"), "auth.username")
        password = _require_non_empty(config.get("password"), "auth.password")
        form = {
            "grant_type": "password",
            "username": username,
            "password": password,
        }
        _append_optional(form, "client_id", config.get("client_id"))
        _append_optional(form, "client_secret", config.get("client_secret"))
        _append_optional(form, "scope", config.get("scope"))
        _append_extra_fields(form, config.get("extra_form_fields"))
        payload = _request_token(token_url, form, config.get("request_headers"))
        return ResolvedAuth(
            access_token=_extract_access_token(payload),
            token_type=str(payload.get("token_type") or common["token_type"]).strip() or common["token_type"],
            source="password",
            **common,
        )

    if mode == "client_credentials":
        token_url = _require_non_empty(config.get("token_url"), "auth.token_url")
        client_id = _require_non_empty(config.get("client_id"), "auth.client_id")
        client_secret = _require_non_empty(config.get("client_secret"), "auth.client_secret")
        form = {
            "grant_type": "client_credentials",
            "client_id": client_id,
            "client_secret": client_secret,
        }
        _append_optional(form, "scope", config.get("scope"))
        _append_extra_fields(form, config.get("extra_form_fields"))
        payload = _request_token(token_url, form, config.get("request_headers"))
        return ResolvedAuth(
            access_token=_extract_access_token(payload),
            token_type=str(payload.get("token_type") or common["token_type"]).strip() or common["token_type"],
            source="client_credentials",
            **common,
        )

    raise ValueError(f"unsupported auth mode: {mode}")


def _request_token(
    token_url: str,
    form_fields: dict[str, str],
    request_headers: dict[str, Any] | None,
) -> dict[str, Any]:
    encoded = urllib.parse.urlencode(form_fields).encode("utf-8")
    headers = {"Content-Type": "application/x-www-form-urlencoded"}
    for key, value in (request_headers or {}).items():
        if value is None:
            continue
        headers[str(key)] = str(value)

    request = urllib.request.Request(token_url, data=encoded, headers=headers, method="POST")
    try:
        with urllib.request.urlopen(request, timeout=15) as response:
            body = response.read().decode("utf-8")
    except urllib.error.HTTPError as ex:
        detail = ex.read().decode("utf-8", errors="ignore")
        raise RuntimeError(f"token request failed: HTTP {ex.code} {detail}") from ex
    except urllib.error.URLError as ex:
        raise RuntimeError(f"token request failed: {ex.reason}") from ex

    try:
        return json.loads(body)
    except json.JSONDecodeError as ex:
        raise RuntimeError("token response is not valid json") from ex


def _extract_access_token(payload: dict[str, Any]) -> str:
    token = str(payload.get("access_token") or "").strip()
    if not token:
        raise RuntimeError("token response does not contain access_token")
    return token


def _append_optional(form: dict[str, str], key: str, value: Any) -> None:
    if value is None:
        return
    text = str(value).strip()
    if text:
        form[key] = text


def _append_extra_fields(form: dict[str, str], extra_fields: Any) -> None:
    if not isinstance(extra_fields, dict):
        return
    for key, value in extra_fields.items():
        if value is None:
            continue
        text = str(value).strip()
        if text:
            form[str(key)] = text


def _require_non_empty(value: Any, field_name: str) -> str:
    text = str(value or "").strip()
    if not text:
        raise ValueError(f"missing required field: {field_name}")
    return text
