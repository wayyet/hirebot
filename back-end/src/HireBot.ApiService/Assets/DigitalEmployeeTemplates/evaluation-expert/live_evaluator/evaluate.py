"""
evaluate.py - 评估沙箱执行入口

职责：
  - inspect 模式：检查评估沙箱本地材料是否齐备，并生成题卡
  - execute 模式：驱动目标沙箱逐题执行测试用例，并采集 trace

输入通过 --runtime-context 注入运行时上下文。
"""

from __future__ import annotations

import argparse
import asyncio
import json
from pathlib import Path
from typing import Any

from auth_client import resolve_auth
from material_loader import inspect_materials, load_runtime_context
from trace_builder import build_trace_result, build_turn_trace


def build_target_execution_prompt(testcase: dict[str, Any]) -> str:
    """把测试用例包装成发给目标沙箱的一次执行指令。"""
    testcase_id = str(testcase.get("test_case_id") or testcase.get("testcase_id") or "").strip()
    scenario_name = str(testcase.get("scenario_name") or testcase.get("title") or "").strip()
    input_block = testcase.get("input") or {}
    user_request = str(input_block.get("user_request") or "").strip()
    context_json = json.dumps(input_block.get("context") or {}, ensure_ascii=False)

    return (
        "[EvaluationExecution]\n"
        f"testcase_id: {testcase_id}\n"
        f"scenario_name: {scenario_name}\n"
        f"user_request: {user_request}\n"
        f"context_json: {context_json}\n"
        "请你作为目标数字员工执行上述场景，并返回可用于评估的真实业务响应。"
    )


def summarize_session(runtime_context: dict[str, Any], auth_source: str | None = None) -> dict[str, Any]:
    """输出可写入结果文件的脱敏会话摘要。"""
    session = runtime_context.get("session") or {}
    target = runtime_context.get("target_sandbox") or {}

    return {
        "session_id": session.get("session_id"),
        "employee_id": session.get("employee_id"),
        "employee_name": session.get("employee_name"),
        "iteration": session.get("iteration"),
        "target_sandbox_id": target.get("sandbox_id"),
        "auth_source": auth_source,
    }


def write_json(output_path: str, payload: dict[str, Any]) -> None:
    """写出 JSON 文件。"""
    path = Path(output_path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")


def run_inspect(runtime_context: dict[str, Any], output: str) -> int:
    """执行材料检查。"""
    materials = inspect_materials(runtime_context)
    result = {
        "mode": "inspect",
        "status": materials["status"],
        "session": summarize_session(runtime_context),
        "materials": materials,
    }
    write_json(output, result)

    print(f"[检查] 材料状态: {materials['status']}")
    print(f"[检查] 题卡数量: {materials['testcases']['count']}")
    print(f"[检查] 本体文件数: {len(materials['ontology']['files'])}")
    print(f"[输出] {output}")
    return 0 if materials["status"] == "ready" else 2


async def run_execute(
    runtime_context: dict[str, Any],
    output: str,
    timeout_override: int | None,
    disable_http_supplement: bool,
) -> int:
    """驱动目标沙箱逐题执行，并采集完整 trace。"""
    from http_client import HttpCollector
    from ws_client import WsCollector

    materials = inspect_materials(runtime_context)
    if materials["status"] != "ready":
        result = {
            "mode": "execute",
            "status": "materials_incomplete",
            "meta": summarize_session(runtime_context),
            "materials": materials,
            "turns": [],
        }
        write_json(output, result)
        print("[执行] 材料未就绪，已输出缺失信息")
        return 2

    target = runtime_context.get("target_sandbox") or {}
    execution = runtime_context.get("execution") or {}
    endpoint = str(
        target.get("ws_url")
        or target.get("ws_endpoint")
        or target.get("gateway_endpoint")
        or ""
    ).strip()
    if not endpoint:
        raise ValueError("runtime context is missing target_sandbox.ws_endpoint / gateway_endpoint")

    auth = resolve_auth(target.get("auth"))
    timeout = timeout_override or int(execution.get("timeout_seconds") or 60)
    auto_http_supplement = bool(execution.get("http_supplement", True))
    http_supplement_enabled = auto_http_supplement and not disable_http_supplement

    print(f"[执行] 题卡数量: {materials['testcases']['count']}")
    print(f"[执行] 目标端点: {endpoint}")
    print(f"[执行] 鉴权方式: {auth.source}")

    turns: list[dict[str, Any]] = []
    additional_headers = auth.build_http_headers() if auth.ws_transport == "header" else {}
    async with WsCollector(
        endpoint,
        auth.access_token,
        timeout=timeout,
        ws_transport=auth.ws_transport,
        ws_query_param=auth.ws_query_param,
        additional_headers=additional_headers,
    ) as collector:
        for index, testcase in enumerate(materials["testcases"]["items"], start=1):
            testcase_id = str(testcase.get("test_case_id") or testcase.get("testcase_id") or f"TC-{index:03d}")
            prompt = build_target_execution_prompt(testcase)

            print(f"[{index}/{materials['testcases']['count']}] 驱动目标沙箱执行: {testcase_id}")
            raw_messages = await collector.send_and_collect(prompt, auto_approve=True)
            print(f"  ← 收到 {len(raw_messages)} 条消息")

            turn = build_turn_trace(
                turn_index=index - 1,
                test_case_id=testcase_id,
                user_input=str((testcase.get("input") or {}).get("user_request") or ""),
                raw_messages=raw_messages,
            )
            turns.append(turn)

            summary = turn["execution_trace"]["summary"]
            print(f"  工具调用: {summary['tool_calls_list']}")
            print(f"  思考块数量: {summary['think_count']}")
            print(f"  执行时长: {summary['execution_time_seconds']}s")
            print()

    supplement = None
    if http_supplement_enabled:
        print("[HTTP] 补充采集运行时数据...")
        supplement = HttpCollector(
            endpoint,
            auth.access_token,
            base_url=str(target.get("http_base_url") or "").strip() or None,
            headers=auth.build_http_headers(),
        ).collect_all()
        print("[HTTP] 完成")

    result = build_trace_result(
        target_endpoint=endpoint,
        session_summary=summarize_session(runtime_context, auth.source),
        materials=materials,
        turns=turns,
        status="completed",
        http_supplement=supplement,
    )
    write_json(output, result)

    print(f"[完成] trace_result 已写入: {output}")
    print(f"[完成] 总轮次: {len(turns)}")
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="评估沙箱执行入口 - inspect 检查本地材料，execute 驱动目标沙箱并采集 trace"
    )
    parser.add_argument(
        "--runtime-context",
        required=True,
        help="运行时上下文 JSON 路径"
    )
    parser.add_argument(
        "--mode",
        choices=["inspect", "execute"],
        required=True,
        help="运行模式：inspect=材料检查，execute=执行采集"
    )
    parser.add_argument(
        "--output",
        required=True,
        help="输出 JSON 文件路径"
    )
    parser.add_argument(
        "--timeout",
        type=int,
        help="覆盖运行时上下文中的 timeout_seconds"
    )
    parser.add_argument(
        "--no-http-supplement",
        action="store_true",
        help="跳过 HTTP API 补充采集"
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    runtime_context = load_runtime_context(args.runtime_context)

    if args.mode == "inspect":
        return run_inspect(runtime_context, args.output)

    return asyncio.run(
        run_execute(
            runtime_context,
            args.output,
            args.timeout,
            args.no_http_supplement,
        )
    )


if __name__ == "__main__":
    raise SystemExit(main())
