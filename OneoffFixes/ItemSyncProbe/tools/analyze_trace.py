#!/usr/bin/env python3
"""Summarize ItemSyncProbe TRACE records from Vintage Story logs."""

from __future__ import annotations

import argparse
from collections import Counter
from pathlib import Path
import re


def base_sig(value: str | None) -> str:
    if not value:
        return ""
    if value in {"empty", "slot?", "stack?"}:
        return value
    parts = value.split(":")
    if len(parts) >= 4:
        return ":".join(parts[:4])
    return value


def identity(value: str | None) -> tuple[str, str] | None:
    sig = base_sig(value)
    if sig in {"", "empty", "slot?", "stack?"}:
        return None

    parts = sig.split(":")
    if len(parts) < 2:
        return None

    return parts[0], parts[1]


def parse_trace(line: str) -> dict[str, str] | None:
    marker = "TRACE|"
    index = line.find(marker)
    if index < 0:
        return None

    fields: dict[str, str] = {}
    for part in line[index + len(marker) :].strip().split("|"):
        if "=" not in part:
            continue
        key, value = part.split("=", 1)
        fields[key] = value
    return fields


def parse_detail(value: str | None) -> dict[str, str]:
    if not value:
        return {}

    fields: dict[str, str] = {}
    for part in value.replace("%7C", "|").split("|"):
        if "=" not in part:
            continue
        key, field_value = part.split("=", 1)
        fields[key] = field_value
    return fields


def inv_prefix(value: str | None) -> str:
    if not value:
        return "?"
    return value.split("-", 1)[0]


def read_lines(paths: list[Path]) -> list[str]:
    lines: list[str] = []
    for path in paths:
        if path.exists():
            lines.extend(path.read_text(errors="replace").splitlines())
    return lines


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "logs",
        nargs="*",
        type=Path,
        default=[
            Path.home() / ".config/VintagestoryData/Logs/client-main.log",
            Path.home() / ".config/VintagestoryData/Logs/server-main.log",
            Path.home() / ".config/VintagestoryData/Logs/server-debug.log",
        ],
    )
    args = parser.parse_args()

    lines = read_lines(args.logs)
    traces = [trace for line in lines if (trace := parse_trace(line))]

    events = Counter(trace.get("event", "?") for trace in traces)
    client_updates = [trace for trace in traces if trace.get("event") == "CLIENT_APPLY_UPDATE_END"]
    client_changed = [trace for trace in client_updates if trace.get("changed") == "1"]
    client_different_before = [
        trace for trace in traces
        if trace.get("event") == "CLIENT_APPLY_UPDATE_BEGIN"
        and base_sig(trace.get("before")) != base_sig(trace.get("packet"))
    ]
    client_item_changed = [
        trace for trace in client_updates
        if identity(trace.get("before")) != identity(trace.get("after"))
    ]
    client_nonempty_identity_swaps = [
        trace for trace in client_updates
        if identity(trace.get("before")) is not None
        and identity(trace.get("after")) is not None
        and identity(trace.get("before")) != identity(trace.get("after"))
    ]
    inert_lastchanged = [
        trace for trace in traces
        if trace.get("event") == "SLOT_MUT" and trace.get("stackChanged") == "1" and trace.get("lastMoved") == "0"
    ]
    client_sends = [
        trace for trace in traces
        if trace.get("event") in {"CLIENT_SEND_ACTIVATE", "CLIENT_SEND_MOVE", "CLIENT_SEND_FLIP"}
    ]
    creative_sends = [
        trace for trace in client_sends
        if inv_prefix(trace.get("inv") or trace.get("srcInv") or trace.get("dstInv")) == "creative"
    ]
    ui_mismatches = []
    for trace in client_sends:
        ui = parse_detail(trace.get("ui"))
        ui_inv = ui.get("inv")
        if not ui_inv:
            continue

        packet_invs = [
            inv for inv in [trace.get("inv"), trace.get("srcInv"), trace.get("dstInv")]
            if inv
        ]
        if packet_invs and ui_inv not in packet_invs:
            ui_mismatches.append((trace, ui))

    suppressed = sum("[ItemSyncEchoFix] Suppressed self echo" in line for line in lines)
    allowed = sum("[ItemSyncEchoFix] Allowed inventory update" in line for line in lines)
    allowed_mismatch = sum("Allowed inventory update" in line and "fingerprint mismatch" in line for line in lines)
    allowed_no_expected = sum("Allowed inventory update" in line and "no expected self state" in line for line in lines)

    coalesce_events = []
    coalesce_pattern = re.compile(r"\[ItemSyncCoalesce\] Coalesced paused updates for (.*?): queued=(\d+), applied=(\d+), dropped=(\d+)")
    combined_coalesce_pattern = re.compile(r"\[ItemSyncFixes\] Coalesced paused updates for (.*?): queued=(\d+), applied=(\d+), dropped=(\d+)")
    mouse_immediate_pattern = re.compile(r"\[(ItemSyncCoalesce|ItemSyncFixes)\] Applied paused mouse update immediately for (.*?)\[(\d+)\], dropped queued=(\d+)")
    mouse_immediate_events = []
    for line in lines:
        match = coalesce_pattern.search(line) or combined_coalesce_pattern.search(line)
        if not match:
            mouse_match = mouse_immediate_pattern.search(line)
            if mouse_match:
                mouse_immediate_events.append(
                    {
                        "mod": mouse_match.group(1),
                        "inv": mouse_match.group(2),
                        "slot": int(mouse_match.group(3)),
                        "dropped": int(mouse_match.group(4)),
                    }
                )
            continue
        coalesce_events.append({
            "inv": match.group(1),
            "queued": int(match.group(2)),
            "applied": int(match.group(3)),
            "dropped": int(match.group(4)),
        })

    print("Trace events")
    for event, count in events.most_common():
        print(f"  {event}: {count}")

    print("\nClient update applications")
    print(f"  begins differing from local slot: {len(client_different_before)}")
    print(f"  applied and changed local slot: {len(client_changed)}")
    print(f"  applied and changed item identity, including empty transitions: {len(client_item_changed)}")
    print(f"  applied non-empty item-to-item identity swaps: {len(client_nonempty_identity_swaps)}")

    print("\nLastChanged")
    print(f"  stack mutations with LastChanged unchanged: {len(inert_lastchanged)}")

    print("\nClient sends")
    print(f"  outgoing inventory actions: {len(client_sends)}")
    print(f"  outgoing creative actions: {len(creative_sends)}")
    print(f"  sends whose UI inventory differs from packet inventory: {len(ui_mismatches)}")

    by_send_inv = Counter()
    for trace in client_sends:
        by_send_inv[inv_prefix(trace.get("inv") or trace.get("srcInv") or trace.get("dstInv"))] += 1
    if by_send_inv:
        print("  by first packet inventory prefix:")
        for inv, count in by_send_inv.most_common():
            print(f"    {inv}: {count}")

    print("\nEcho fix")
    print(f"  suppressed exact self echoes: {suppressed}")
    print(f"  allowed updates: {allowed}")
    print(f"  allowed fingerprint mismatches: {allowed_mismatch}")
    print(f"  allowed no expected state: {allowed_no_expected}")

    print("\nCoalesce fix")
    print(f"  queue flushes coalesced: {len(coalesce_events)}")
    print(f"  queued updates observed: {sum(event['queued'] for event in coalesce_events)}")
    print(f"  stale queued updates dropped: {sum(event['dropped'] for event in coalesce_events)}")
    print(f"  paused mouse updates applied immediately: {len(mouse_immediate_events)}")
    print(f"  queued mouse updates dropped before immediate apply: {sum(event['dropped'] for event in mouse_immediate_events)}")

    if coalesce_events:
        by_coalesce_inv = Counter()
        for event in coalesce_events:
            by_coalesce_inv[inv_prefix(event["inv"])] += event["dropped"]
        print("  dropped updates by inventory prefix:")
        for inv, count in by_coalesce_inv.most_common():
            print(f"    {inv}: {count}")

    by_inv = Counter()
    for trace in client_changed:
        inv = trace.get("packetInv") or trace.get("utilInv") or "?"
        by_inv[inv.split("-", 1)[0]] += 1
    if by_inv:
        print("\nClient changed slots by inventory prefix")
        for inv, count in by_inv.most_common():
            print(f"  {inv}: {count}")

    if client_nonempty_identity_swaps:
        print("\nNon-empty identity swap examples")
        for trace in client_nonempty_identity_swaps[:5]:
            print(
                "  "
                f"{trace.get('packetInv') or trace.get('utilInv') or '?'}[{trace.get('slot', '?')}] "
                f"before={trace.get('before', '?')} packet={trace.get('packet', '?')} after={trace.get('after', '?')}"
            )

    if creative_sends:
        print("\nCreative send examples")
        for trace in creative_sends[:5]:
            ui = parse_detail(trace.get("ui"))
            print(
                "  "
                f"{trace.get('event')} packet={trace.get('inv') or trace.get('srcInv') or '?'}"
                f"[{trace.get('slot') or trace.get('srcSlot') or '?'}]"
                f" packetStack={trace.get('slotNow') or trace.get('srcNow') or '?'}"
                f" ui={ui.get('inv', '?')}[{ui.get('slot', '?')}]"
                f" uiStack={ui.get('slotNow', '?')}"
            )

    if ui_mismatches:
        print("\nUI/packet inventory mismatch examples")
        for trace, ui in ui_mismatches[:5]:
            print(
                "  "
                f"{trace.get('event')} packetInvs="
                f"{','.join(inv for inv in [trace.get('inv'), trace.get('srcInv'), trace.get('dstInv')] if inv)}"
                f" ui={ui.get('inv', '?')}[{ui.get('slot', '?')}]"
            )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
