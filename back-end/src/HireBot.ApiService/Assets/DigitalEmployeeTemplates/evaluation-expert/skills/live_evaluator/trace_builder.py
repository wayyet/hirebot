"""
trace_builder.py - 格式化 execution_trace

职责：
  - 把 WsCollector 采集的原始消息列表格式化为 execution_trace
  - 组装评估所需的 trace_result.json
  - 保留原始消息、思考块、题卡与本体摘要

不做任何评分逻辑。
"""

import re
from datetime import datetime, timezone
from typing import Any


# WS 消息 type → execution_trace log type 映射
# 未知类型原样保留 type 字段
_TYPE_MAP = {
    "assistant_message": "assistant_message",
    "text_delta": "text_delta",
    "assistant_chunk": "text_delta",
    "tool_call": "tool_call",
    "thought": "thought",
    "typing_start": "state_change",
    "typing_stop": "state_change",
    "assistant_done": "state_change",
    "approval_required": "approval_required",
    "error": "error",
}


def extract_think_blocks(text: str) -> tuple[list[str], str]:
    """
    从文本中提取 <think>...</think> 块，返回 (思考内容列表, 去除思考后的文本)。

    示例：
        "<think>分析问题</think>你好<think>继续思考</think>回复"
        → (["分析问题", "继续思考"], "你好回复")
    """
    thinks = re.findall(r'<think>(.*?)</think>', text, re.DOTALL)
    cleaned = re.sub(r'<think>.*?</think>', '', text, flags=re.DOTALL)
    return thinks, cleaned


def build_turn_trace(
    turn_index: int,
    test_case_id: str,
    user_input: str,
    raw_messages: list[dict[str, Any]],
) -> dict[str, Any]:
    """
    将单轮对话的原始 WS 消息列表格式化为 execution_trace。

    Args:
        turn_index:   轮次序号（0-based）
        test_case_id: 对应的测试用例 ID
        user_input:   本轮发送的用户消息
        raw_messages: WsCollector.send_and_collect() 返回的原始消息列表

    Returns:
        单轮 turn 结构，包含 execution_trace
    """
    logs: list[dict[str, Any]] = []
    tool_calls: list[dict[str, Any]] = []
    assistant_text_parts: list[str] = []
    think_blocks: list[str] = []  # 收集所有思考块（暂不提取）
    has_thought = False
    start_ts = None
    end_ts = None
    
    # 工具调用配对：tool_start → tool_result
    pending_tool: dict[str, Any] | None = None

    for msg in raw_messages:
        msg_type = msg.get("type", "unknown")
        ts = msg.get("_received_at")

        if start_ts is None:
            start_ts = ts
        end_ts = ts

        log_type = _TYPE_MAP.get(msg_type, msg_type)

        # 工具调用开始
        if msg_type == "tool_start":
            tool_name = msg.get("text") or msg.get("tool_name") or "unknown"
            pending_tool = {
                "type": "tool_call",
                "timestamp": ts,
                "tool_name": tool_name,
                "parameters": None,  # tool_start 里没有入参
                "result": None,
                "start_message": msg,
            }
            logs.append({
                "type": "tool_start",
                "timestamp": ts,
                "tool_name": tool_name,
            })

        # 工具调用结果
        elif msg_type == "tool_result":
            result_text = msg.get("text") or ""
            if pending_tool:
                pending_tool["result"] = result_text
                pending_tool["result_message"] = msg
                tool_calls.append(pending_tool)
                pending_tool = None
            logs.append({
                "type": "tool_result",
                "timestamp": ts,
                "result": result_text,
            })

        # 独立 thought 消息（如果存在）
        elif msg_type == "thought":
            has_thought = True
            logs.append({
                "type": "thought",
                "timestamp": ts,
                "content": msg.get("content") or msg.get("text") or "",
            })

        # 独立 tool_call 消息（如果存在，与 tool_start/result 不同）
        elif msg_type == "tool_call":
            entry = {
                "type": "tool_call",
                "timestamp": ts,
                "tool_name": msg.get("toolName") or msg.get("tool_name") or msg.get("name") or "unknown",
                "parameters": msg.get("parameters") or msg.get("args") or {},
                "result": msg.get("result"),
                "call_id": msg.get("callId") or msg.get("id"),
            }
            logs.append(entry)
            tool_calls.append(entry)

        # 完整助手消息
        elif msg_type == "assistant_message":
            content = msg.get("content") or msg.get("text") or ""
            # 提取 <think> 块
            extracted_thinks, cleaned_content = extract_think_blocks(content)
            think_blocks.extend(extracted_thinks)
            has_thought = has_thought or len(extracted_thinks) > 0
            logs.append({
                "type": "assistant_message",
                "timestamp": ts,
                "content": content,
                "cleaned_content": cleaned_content,
            })

        # 流式 token（拼接备用，暂不提取 <think>）
        elif msg_type in ("text_delta", "assistant_chunk"):
            delta = msg.get("delta") or msg.get("text") or ""
            assistant_text_parts.append(delta)
            logs.append({
                "type": "text_delta",
                "timestamp": ts,
                "delta": delta,
            })

        # 状态变更
        elif msg_type in ("typing_start", "typing_stop", "assistant_done"):
            logs.append({
                "type": "state_change",
                "timestamp": ts,
                "state": msg_type,
            })

        # 审批请求
        elif msg_type == "approval_required":
            logs.append({
                "type": "approval_required",
                "timestamp": ts,
                "call_id": msg.get("callId"),
                "tool_name": msg.get("toolName"),
                "parameters": msg.get("parameters") or {},
            })

        # 错误
        elif msg_type == "error":
            logs.append({
                "type": "error",
                "timestamp": ts,
                "message": msg.get("message") or msg.get("error") or str(msg),
            })

        # 未知类型，原样保留
        else:
            logs.append({
                "type": log_type,
                "timestamp": ts,
                "_raw": msg,
            })

    # 如果没有 assistant_message 但有 text_delta，拼接成完整文本并提取 <think>
    assembled_text = None
    if assistant_text_parts:
        full_text = "".join(assistant_text_parts)
        extracted_thinks, cleaned = extract_think_blocks(full_text)
        think_blocks.extend(extracted_thinks)
        has_thought = has_thought or len(extracted_thinks) > 0
        assembled_text = cleaned.strip()

    # 计算执行时长
    execution_time = None
    if start_ts and end_ts:
        try:
            t0 = datetime.fromisoformat(start_ts)
            t1 = datetime.fromisoformat(end_ts)
            execution_time = round((t1 - t0).total_seconds(), 2)
        except Exception:
            pass

    return {
        "turn_index": turn_index,
        "test_case_id": test_case_id,
        "user_input": user_input,
        "execution_trace": {
            "logs": logs,
            "raw_messages": raw_messages,   # 完整原始消息，不丢弃
            "assembled_assistant_text": assembled_text,
            "think_blocks": think_blocks,   # 所有提取的思考内容
            "summary": {
                "total_messages": len(raw_messages),
                "total_tool_calls": len(tool_calls),
                "has_thought": has_thought,
                "think_count": len(think_blocks),
                "execution_time_seconds": execution_time,
                "tool_calls_list": [t["tool_name"] for t in tool_calls],
            },
        },
    }


def build_trace_result(
    target_endpoint: str,
    session_summary: dict[str, Any],
    materials: dict[str, Any],
    turns: list[dict[str, Any]],
    status: str = "completed",
    http_supplement: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """
    组装最终的 trace_result.json 结构。

    Args:
        target_endpoint: 目标沙箱 Gateway / WS 地址
        session_summary: 脱敏后的会话元信息
        materials:      inspect_materials() 返回的材料摘要
        turns:          每轮 build_turn_trace() 的结果列表
        status:         执行状态
        http_supplement:http_client 采集的补充数据（可选）

    Returns:
        完整的 trace_result 结构
    """
    return {
        "meta": {
            "target_endpoint": target_endpoint,
            "collected_at": datetime.now(timezone.utc).isoformat(),
            "total_turns": len(turns),
            **session_summary,
        },
        "status": status,
        "materials": materials,
        "test_cases": materials.get("testcases", {}).get("items", []),
        "question_cards": materials.get("testcases", {}).get("question_cards", []),
        "ontology": materials.get("ontology", {}),
        "turns": turns,
        "http_supplement": http_supplement,  # None 时 evaluator 忽略即可
    }
