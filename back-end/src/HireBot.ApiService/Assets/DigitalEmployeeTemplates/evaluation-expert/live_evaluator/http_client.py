"""
http_client.py - HTTP 补充采集

职责：
  - 在目标沙箱执行结束后，补采 Gateway HTTP API 中的运行时信息
  - 采集 runtime-events、sessions、approval-history、dashboard
  - 原样返回，不做评分推断
"""

import json
import re
from datetime import datetime, timezone
from typing import Any

import urllib.request
import urllib.error


class HttpCollector:
    """
    调用 Gateway HTTP API 补充采集数据。

    用法：
        collector = HttpCollector(endpoint, token)
        data = collector.collect_all()
    """

    def __init__(
        self,
        endpoint: str,
        token: str,
        *,
        base_url: str | None = None,
        headers: dict[str, str] | None = None,
    ):
        """
        Args:
            endpoint: Gateway 地址（HOST:PORT 或完整 URL）
            token:    JWT Bearer Token
        """
        # 从 endpoint 推导 http base URL；如上下文显式提供则优先使用
        e = (base_url or endpoint).strip()
        if re.match(r'^wss?://', e, re.IGNORECASE):
            e = re.sub(r'^ws', 'http', e, flags=re.IGNORECASE)
        if not re.match(r'^https?://', e, re.IGNORECASE):
            e = f"http://{e}"
        # 去掉 /ws 路径和 token 参数
        e = re.sub(r'/ws($|[?#].*)', '', e)
        e = re.sub(r'[?&]token=[^&]*', '', e).rstrip('?&')
        self.base_url = e
        self.token = token
        self.headers = headers or {"Authorization": f"Bearer {self.token}"}

    def _get(self, path: str, params: dict[str, str] | None = None) -> Any:
        """发送 GET 请求，返回解析后的 JSON 或错误信息。"""
        url = f"{self.base_url}{path}"
        if params:
            query = "&".join(f"{k}={v}" for k, v in params.items())
            url = f"{url}?{query}"

        req = urllib.request.Request(
            url,
            headers=self.headers,
        )
        try:
            with urllib.request.urlopen(req, timeout=10) as resp:
                return json.loads(resp.read().decode())
        except urllib.error.HTTPError as e:
            return {"_error": f"HTTP {e.code}", "url": url}
        except Exception as e:
            return {"_error": str(e), "url": url}

    def collect_runtime_events(self) -> Any:
        """GET /api/integration/runtime-events"""
        return self._get("/api/integration/runtime-events")

    def collect_sessions(self) -> Any:
        """GET /api/integration/sessions"""
        return self._get("/api/integration/sessions", {"include_state": "true"})

    def collect_approval_history(self) -> Any:
        """GET /api/integration/approval-history"""
        return self._get("/api/integration/approval-history")

    def collect_dashboard(self) -> Any:
        """GET /api/integration/dashboard"""
        return self._get("/api/integration/dashboard")

    def collect_all(self) -> dict[str, Any]:
        """
        采集所有补充数据，返回合并结果。
        任何单项失败不影响其他项。
        """
        return {
            "collected_at": datetime.now(timezone.utc).isoformat(),
            "runtime_events": self.collect_runtime_events(),
            "sessions": self.collect_sessions(),
            "approval_history": self.collect_approval_history(),
            "dashboard": self.collect_dashboard(),
        }


if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser(description="HTTP API 补充采集")
    parser.add_argument("--endpoint", required=True, help="Gateway HOST:PORT")
    parser.add_argument("--token", required=True, help="JWT Token")
    parser.add_argument("--base-url", help="显式指定 HTTP Base URL")
    parser.add_argument("--output", default="http_supplement.json", help="输出文件路径")
    args = parser.parse_args()

    collector = HttpCollector(args.endpoint, args.token, base_url=args.base_url)
    data = collector.collect_all()

    with open(args.output, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

    print(f"[http_client] 补充数据已写入 {args.output}")
