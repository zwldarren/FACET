#!/usr/bin/env python3
"""
Matched-round rate by wave - the visualization half of FACET's analytics pipeline.

The game sends one row per finished wave to a Google Form (Assets/Scripts/Unity/AnalyticsReporter.cs);
the form's responses land in a Google Sheet. This script reads that sheet as CSV and draws the share of
turret hits whose round matched the wave's weakness, per map and wave.

Usage:
    python3 Tools/analytics/plot_match_rate.py responses.csv
    python3 Tools/analytics/plot_match_rate.py "https://docs.google.com/spreadsheets/d/e/.../pub?output=csv"
    python3 Tools/analytics/plot_match_rate.py responses.csv --build alpha --out match_rate.png
    python3 Tools/analytics/plot_match_rate.py responses.csv --build alpha --finished-only

Each row is one wave. A player's rows share a Session; their rows on one map, in the order they were
sent, form attempts: an attempt ends at a row whose Outcome is won, lost or abandoned. A finished attempt
is one that ended won or lost - the player played the map to its end. --finished-only keeps only those,
dropping players who walked away part-way through a map.

The CSV is the sheet's File > Download > CSV, or its File > Share > Publish to web > CSV link.
Columns are found by name (Session, Map, Wave, Matched, Resisted, Outcome, Build), so extra columns such
as Google's Timestamp are ignored. Needs matplotlib (pip install matplotlib).
"""
import argparse
import csv
import io
import sys
import urllib.request
from collections import OrderedDict

MAP_ORDER = ["Quarry", "Brickyard"]


def read_rows(source):
    if source.startswith("http://") or source.startswith("https://"):
        with urllib.request.urlopen(source) as response:
            text = response.read().decode("utf-8")
    else:
        with open(source, newline="", encoding="utf-8") as handle:
            text = handle.read()

    reader = csv.DictReader(io.StringIO(text))
    columns = {name.strip().lower(): name for name in (reader.fieldnames or [])}
    needed = ["map", "wave", "matched", "resisted"]
    missing = [c for c in needed if c not in columns]
    if missing:
        sys.exit("missing column(s) " + ", ".join(missing) + " - the form's questions must be titled "
                 "Session, Map, Wave, Matched, Resisted, Outcome, Build")

    for raw in reader:
        try:
            yield {
                "session": raw.get(columns.get("session", ""), ""),
                "map": raw[columns["map"]].strip(),
                "wave": int(raw[columns["wave"]]),
                "matched": int(raw[columns["matched"]]),
                "resisted": int(raw[columns["resisted"]]),
                "outcome": raw.get(columns.get("outcome", ""), "").strip(),
                "build": raw.get(columns.get("build", ""), "").strip(),
            }
        except (ValueError, KeyError):
            continue  # a half-filled or hand-edited row


FINISHED = ("won", "lost")
ENDS_ATTEMPT = ("won", "lost", "abandoned")


def mark_attempts(rows):
    """Number each row's attempt within its (session, map), in sheet order, and say whether that attempt
    was played to its end. Rows are dicts; adds 'attempt' and 'finished' keys in place."""
    counters, current = {}, {}
    for row in rows:
        key = (row["session"], row["map"])
        counters.setdefault(key, 1)
        row["attempt"] = counters[key]
        current.setdefault((key, counters[key]), []).append(row)
        if row["outcome"] in ENDS_ATTEMPT:
            counters[key] += 1

    for group in current.values():
        finished = group[-1]["outcome"] in FINISHED
        for row in group:
            row["finished"] = finished
    return rows


def aggregate(rows, build=None):
    """(map, wave) -> [matched, hits, sessions], maps in campaign order, waves ascending."""
    totals = {}
    for row in rows:
        if build and row["build"] != build:
            continue
        key = (row["map"], row["wave"])
        entry = totals.setdefault(key, [0, 0, set()])
        entry[0] += row["matched"]
        entry[1] += row["matched"] + row["resisted"]
        entry[2].add(row["session"])

    def order(key):
        name, wave = key
        rank = MAP_ORDER.index(name) if name in MAP_ORDER else len(MAP_ORDER)
        return (rank, name, wave)

    return OrderedDict((key, totals[key]) for key in sorted(totals, key=order))


def short(name):
    return name[:1].upper() if name else "?"


def plot(table, out, title_suffix):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    surface, ink, muted, grid, blue = "#fcfcfb", "#0b0b0b", "#898781", "#e1e0d9", "#2a78d6"
    keys = [k for k in table if table[k][1] > 0]
    if not keys:
        sys.exit("no hits to plot yet")

    rates = [100.0 * table[k][0] / table[k][1] for k in keys]
    fig, ax = plt.subplots(figsize=(max(6, 1.0 * len(keys) + 2), 4.4), dpi=200)
    fig.patch.set_facecolor(surface)
    ax.set_facecolor(surface)

    xs = range(len(keys))
    ax.bar(xs, rates, width=0.5, color=blue, zorder=2)
    for x, key, rate in zip(xs, keys, rates):
        matched, hits, sessions = table[key]
        ax.text(x, rate + 2, f"{rate:.0f}%", ha="center", va="bottom", fontsize=9, color=ink)
        ax.text(x, -6, f"{short(key[0])}{key[1]}", ha="center", va="top", fontsize=9, color=ink)
        ax.text(x, -14, f"{hits} hits", ha="center", va="top", fontsize=7.5, color=muted)

    ax.set_ylim(-20, 108)
    ax.set_xlim(-0.6, len(keys) - 0.4)
    ax.set_yticks([0, 25, 50, 75, 100])
    ax.set_yticklabels(["0%", "25%", "50%", "75%", "100%"], color=muted, fontsize=9)
    ax.set_xticks([])
    ax.grid(axis="y", color=grid, lw=0.8, zorder=0)
    for side in ("top", "right", "left"):
        ax.spines[side].set_visible(False)
    ax.spines["bottom"].set_position(("data", 0))
    ax.spines["bottom"].set_color(muted)
    ax.tick_params(axis="y", length=0)

    sessions = set()
    for key in keys:
        sessions |= table[key][2]
    fig.text(0.07, 0.95, "Matched-round rate by wave", fontsize=13, fontweight="bold", color=ink)
    fig.text(0.07, 0.905, f"Share of turret hits whose round matched the wave's weakness  ·  "
             f"{len(sessions)} sessions{title_suffix}", fontsize=9, color=muted)
    fig.subplots_adjust(left=0.08, right=0.98, top=0.86, bottom=0.08)
    fig.savefig(out, facecolor=surface)
    print("wrote " + out)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("source", help="the responses sheet as a CSV file or a published CSV link")
    parser.add_argument("--build", help="only rows from this build label (AnalyticsConfig.BuildLabel)")
    parser.add_argument("--out", default="match_rate.png", help="where to write the chart")
    parser.add_argument("--finished-only", action="store_true",
                        help="only attempts played to their end (the map won or lost)")
    args = parser.parse_args()

    rows = mark_attempts(list(read_rows(args.source)))
    if args.build:
        rows = [r for r in rows if r["build"] == args.build]

    attempts = {(r["session"], r["map"], r["attempt"]): r["finished"] for r in rows}
    print(f"{len({r['session'] for r in rows})} sessions, {len(attempts)} attempts at a map, "
          f"{sum(attempts.values())} finished (won or lost)\n")

    if args.finished_only:
        rows = [r for r in rows if r["finished"]]

    table = aggregate(rows)

    print(f"{'map':<12}{'wave':>5}{'matched':>9}{'hits':>6}{'rate':>7}{'sessions':>10}")
    for (name, wave), (matched, hits, sessions) in table.items():
        rate = f"{100.0 * matched / hits:.0f}%" if hits else "-"
        print(f"{name:<12}{wave:>5}{matched:>9}{hits:>6}{rate:>7}{len(sessions):>10}")

    suffix = (f"  ·  build {args.build}" if args.build else "") + \
             ("  ·  finished attempts only" if args.finished_only else "")
    plot(table, args.out, suffix)


if __name__ == "__main__":
    main()
