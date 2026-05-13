#!/usr/bin/env python3
"""
Multi-pass data validator for the canonical draft classes and rosters.

Each pass attacks the data from a different angle - structural / count /
known-player / value-range / cross-year / field-completeness. Issues are
collected and printed as a categorized report at the end.

Exit code = number of issues found (0 = clean).

Run:
    python tools/validate_data.py
"""
from __future__ import annotations

import collections
import json
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
CANONICAL = REPO_ROOT / "data" / "canonical"
MADDEN_DIR = REPO_ROOT / "data" / "raw" / "madden-ratings"
MAPPINGS = REPO_ROOT / "data" / "mappings"

DRAFT_YEARS = list(range(2008, 2027))
ROSTER_YEARS = list(range(2008, 2026))


class Report:
    def __init__(self) -> None:
        self.passes: list[tuple[str, list[str]]] = []
        self._current: list[str] = []
        self._current_name = ""

    def start(self, name: str) -> None:
        if self._current_name:
            self.passes.append((self._current_name, self._current))
        self._current_name = name
        self._current = []
        print(f"\n=== Pass: {name} ===", file=sys.stderr)

    def issue(self, msg: str) -> None:
        self._current.append(msg)
        print(f"  ISSUE: {msg}", file=sys.stderr)

    def info(self, msg: str) -> None:
        print(f"  {msg}", file=sys.stderr)

    def finish(self) -> None:
        if self._current_name:
            self.passes.append((self._current_name, self._current))

    def summary(self) -> int:
        total = 0
        print("\n" + "=" * 70)
        print("VALIDATION SUMMARY")
        print("=" * 70)
        for name, issues in self.passes:
            n = len(issues)
            total += n
            status = "CLEAN" if n == 0 else f"{n} issue(s)"
            print(f"  {name:50} {status}")
        print(f"\nTotal: {total} issue(s)")
        return total


def load_json(path: Path) -> dict | None:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError:
        return None
    except json.JSONDecodeError as e:
        return {"_parse_error": str(e)}


# Known #1 picks per draft year. Used in pass 3.
KNOWN_NUMBER_ONE_PICKS = {
    2008: ("Jake", "Long"),
    2009: ("Matthew", "Stafford"),
    2010: ("Sam", "Bradford"),
    2011: ("Cam", "Newton"),
    2012: ("Andrew", "Luck"),
    2013: ("Eric", "Fisher"),
    2014: ("Jadeveon", "Clowney"),
    2015: ("Jameis", "Winston"),
    2016: ("Jared", "Goff"),
    2017: ("Myles", "Garrett"),
    2018: ("Baker", "Mayfield"),
    2019: ("Kyler", "Murray"),
    2020: ("Joe", "Burrow"),
    2021: ("Trevor", "Lawrence"),
    2022: ("Travon", "Walker"),
    2023: ("Bryce", "Young"),
    2024: ("Caleb", "Williams"),
    2025: ("Cam", "Ward"),
    2026: ("Fernando", "Mendoza"),
}

# Real-life identifiers for spot-check players. nflverse uses the legal
# name forms (no suffixes on lastName; periods in CJ -> "C.J.") so the
# expected first/last here must match canonical convention.

# Known notable picks (not necessarily #1) for additional spot-checks
KNOWN_TOP_PICKS = {
    2009: [("Mark", "Sanchez", 5)],
    2010: [("Ndamukong", "Suh", 2), ("Gerald", "McCoy", 3)],
    2011: [("Von", "Miller", 2)],
    2012: [("Robert", "Griffin", 2)],
    2013: [("Luke", "Joeckel", 2)],
    2014: [("Greg", "Robinson", 2), ("Blake", "Bortles", 3)],
    2015: [("Marcus", "Mariota", 2)],
    2016: [("Carson", "Wentz", 2), ("Ezekiel", "Elliott", 4)],
    2017: [("Mitchell", "Trubisky", 2), ("Patrick", "Mahomes", 10)],
    2018: [("Saquon", "Barkley", 2), ("Lamar", "Jackson", 32)],
    2019: [("Nick", "Bosa", 2), ("Quinnen", "Williams", 3)],
    2020: [("Chase", "Young", 2), ("Tua", "Tagovailoa", 5)],
    2021: [("Zach", "Wilson", 2), ("Trey", "Lance", 3), ("Justin", "Fields", 11)],
    2022: [("Aidan", "Hutchinson", 2)],
    2023: [("C.J.", "Stroud", 2), ("Anthony", "Richardson", 4)],
    2024: [("Jayden", "Daniels", 2), ("Drake", "Maye", 3)],
    2025: [("Travis", "Hunter", 2), ("Abdul", "Carter", 3)],
}

# Known star players per team per year, for roster spot-checks
KNOWN_ROSTER_PLAYERS = {
    # year -> [(team_abbr_pre_2016, first, last), ...]
    2008: [("NE", "Tom", "Brady"), ("IND", "Peyton", "Manning"), ("CHI", "Brian", "Urlacher")],
    2009: [("NE", "Tom", "Brady"), ("NO", "Drew", "Brees"), ("DAL", "Tony", "Romo")],
    2010: [("NE", "Tom", "Brady"), ("GB", "Aaron", "Rodgers")],
    2011: [("GB", "Aaron", "Rodgers"), ("NO", "Drew", "Brees")],
    2012: [("NE", "Tom", "Brady"), ("DEN", "Peyton", "Manning")],
    2013: [("DEN", "Peyton", "Manning"), ("SEA", "Russell", "Wilson")],
    2014: [("NE", "Tom", "Brady"), ("SEA", "Russell", "Wilson")],
    2015: [("CAR", "Cam", "Newton"), ("NE", "Tom", "Brady")],
    2016: [("ATL", "Matt", "Ryan"), ("NE", "Tom", "Brady")],
    2017: [("PHI", "Carson", "Wentz"), ("NE", "Tom", "Brady")],
    2018: [("KC", "Patrick", "Mahomes"), ("NO", "Drew", "Brees")],
    2019: [("BAL", "Lamar", "Jackson"), ("KC", "Patrick", "Mahomes")],
    2020: [("KC", "Patrick", "Mahomes"), ("GB", "Aaron", "Rodgers")],
    2021: [("TB", "Tom", "Brady"), ("BUF", "Josh", "Allen")],
    2022: [("KC", "Patrick", "Mahomes"), ("BUF", "Josh", "Allen")],
    2023: [("KC", "Patrick", "Mahomes"), ("BUF", "Josh", "Allen")],
    2024: [("BAL", "Lamar", "Jackson"), ("KC", "Patrick", "Mahomes")],
    2025: [("KC", "Patrick", "Mahomes"), ("BUF", "Josh", "Allen")],
}


def pass1_structural(rep: Report) -> dict:
    """Pass 1: Files exist, parse, have expected top-level shape."""
    rep.start("1. structural (files exist + parse + shape)")
    data: dict[str, dict] = {"drafts": {}, "rosters": {}}

    for y in DRAFT_YEARS:
        path = CANONICAL / f"draft-class-{y}.json"
        d = load_json(path)
        if d is None:
            rep.issue(f"missing draft-class-{y}.json")
            continue
        if d.get("_parse_error"):
            rep.issue(f"draft-class-{y}.json JSON parse error: {d['_parse_error']}")
            continue
        if d.get("schema") != "ncaa-draft-class/v1":
            rep.issue(f"draft-class-{y}.json wrong schema: {d.get('schema')!r}")
        if d.get("year") != y:
            rep.issue(f"draft-class-{y}.json year mismatch: {d.get('year')} (filename says {y})")
        if not isinstance(d.get("players"), list):
            rep.issue(f"draft-class-{y}.json missing or non-list 'players'")
        data["drafts"][y] = d

    for y in ROSTER_YEARS:
        path = CANONICAL / f"roster-{y}.json"
        d = load_json(path)
        if d is None:
            rep.issue(f"missing roster-{y}.json")
            continue
        if d.get("_parse_error"):
            rep.issue(f"roster-{y}.json JSON parse error: {d['_parse_error']}")
            continue
        if d.get("schema") != "ncaa-madden-roster/v1":
            rep.issue(f"roster-{y}.json wrong schema: {d.get('schema')!r}")
        if d.get("nflSeason") != y:
            rep.issue(f"roster-{y}.json season mismatch: {d.get('nflSeason')} (filename says {y})")
        teams = d.get("teams")
        if not isinstance(teams, list) or len(teams) != 32:
            rep.issue(f"roster-{y}.json team count != 32 (got {len(teams) if isinstance(teams, list) else 'non-list'})")
        data["rosters"][y] = d

    rep.info(f"loaded {len(data['drafts'])}/{len(DRAFT_YEARS)} drafts, "
             f"{len(data['rosters'])}/{len(ROSTER_YEARS)} rosters")
    return data


def pass2_counts(rep: Report, data: dict) -> None:
    """Pass 2: Per-file counts within expected ranges."""
    rep.start("2. counts (player + team counts vs. NFL realities)")
    for y, d in data["drafts"].items():
        n = len(d.get("players", []))
        if not (220 <= n <= 280):
            rep.issue(f"draft {y}: {n} picks (expected 220-280; NFL drafts are ~250-260)")
    for y, d in data["rosters"].items():
        teams = d.get("teams", [])
        if len(teams) != 32:
            continue
        per_team = [len(t.get("players", [])) for t in teams]
        if min(per_team) < 30:
            rep.issue(f"roster {y}: team with only {min(per_team)} players (Bears/etc. should be 50+)")
        if max(per_team) > 130:
            rep.issue(f"roster {y}: team with {max(per_team)} players (suspiciously high)")
        empty = sum(1 for n in per_team if n == 0)
        if empty:
            rep.issue(f"roster {y}: {empty} team(s) have 0 players")
    # Madden rating coverage
    for y in range(2008, 2024):
        madden_files = list(MADDEN_DIR.glob(f"madden*-{y}.json"))
        if not madden_files:
            rep.issue(f"missing Madden ratings file for {y} season")


def pass3_known_players(rep: Report, data: dict) -> None:
    """Pass 3: Spot-check known #1 picks + roster stars."""
    rep.start("3. known players (canonical 'must-have' spot checks)")
    for y, (first, last) in KNOWN_NUMBER_ONE_PICKS.items():
        d = data["drafts"].get(y)
        if not d:
            continue
        picks = d.get("players", [])
        first_pick_round1 = next(
            (p for p in picks
             if (p.get("draft") or {}).get("round") == 1 and (p.get("draft") or {}).get("pick") == 1),
            None,
        )
        if not first_pick_round1:
            rep.issue(f"draft {y}: no pick at round 1, pick 1")
            continue
        got_first = (first_pick_round1.get("name") or {}).get("first", "")
        got_last = (first_pick_round1.get("name") or {}).get("last", "")
        if got_last != last:
            rep.issue(f"draft {y} #1 pick: expected {first} {last}, got {got_first} {got_last}")

    for y, picks in KNOWN_TOP_PICKS.items():
        d = data["drafts"].get(y)
        if not d:
            continue
        names = {(p.get("name", {}).get("first", ""), p.get("name", {}).get("last", ""))
                 for p in d.get("players", [])}
        for first, last, pick in picks:
            # Be permissive about first name (Joshua vs Josh, Robert Griffin III)
            matched = any(last == ln and (first == fn or first.startswith(fn[:3]) or fn.startswith(first[:3]))
                          for fn, ln in names)
            if not matched:
                rep.issue(f"draft {y}: expected {first} {last} (pick ~{pick}); not in canonical")

    for y, roster_checks in KNOWN_ROSTER_PLAYERS.items():
        d = data["rosters"].get(y)
        if not d:
            continue
        by_team = {t.get("abbreviation"): t for t in d.get("teams", [])}
        for abbr, first, last in roster_checks:
            team = by_team.get(abbr)
            if not team:
                rep.issue(f"roster {y}: team abbrev {abbr} not in roster (rebrand issue?)")
                continue
            players = team.get("players", [])
            matched = any((p.get("name") or {}).get("last") == last
                          and (first in (p.get("name") or {}).get("first", "")
                               or (p.get("name") or {}).get("first", "").startswith(first[:3]))
                          for p in players)
            if not matched:
                rep.issue(f"roster {y}: expected {first} {last} on {abbr}; not found "
                          f"(team has {len(players)} players)")


def pass4_value_ranges(rep: Report, data: dict) -> None:
    """Pass 4: Field values within plausible NFL ranges."""
    rep.start("4. value ranges (heights, weights, ages, ratings, jerseys)")

    def bad_range(label: str, value, lo: int, hi: int) -> bool:
        return value is not None and not (lo <= value <= hi)

    for y, d in data["drafts"].items():
        for i, p in enumerate(d.get("players", [])):
            m = p.get("measurables") or {}
            h = m.get("heightIn")
            w = m.get("weightLb")
            if bad_range("height", h, 60, 90):
                rep.issue(f"draft {y}#{i}: height {h} in (expected 60-90)")
            if bad_range("weight", w, 140, 400):
                rep.issue(f"draft {y}#{i}: weight {w} lb (expected 140-400)")
            draft = p.get("draft") or {}
            rnd = draft.get("round")
            pick = draft.get("pick")
            if bad_range("round", rnd, 1, 8):
                rep.issue(f"draft {y}#{i}: round {rnd} out of range")
            if bad_range("pick", pick, 1, 280):
                rep.issue(f"draft {y}#{i}: pick {pick} out of range")
    for y, d in data["rosters"].items():
        for t in d.get("teams", []):
            for j, p in enumerate(t.get("players", [])):
                if bad_range("age", p.get("age"), 18, 50):
                    rep.issue(f"roster {y} {t.get('abbreviation')}#{j}: age {p.get('age')} weird")
                if bad_range("jersey", p.get("jerseyNumber"), 0, 99):
                    rep.issue(f"roster {y} {t.get('abbreviation')}#{j}: jersey {p.get('jerseyNumber')}")
                if bad_range("years_pro", p.get("yearsPro"), 0, 25):
                    rep.issue(f"roster {y} {t.get('abbreviation')}#{j}: yearsPro {p.get('yearsPro')}")
                m = p.get("measurables") or {}
                if bad_range("height", m.get("heightIn"), 60, 90):
                    rep.issue(f"roster {y} {t.get('abbreviation')}#{j}: height {m.get('heightIn')}")
                if bad_range("weight", m.get("weightLb"), 140, 400):
                    rep.issue(f"roster {y} {t.get('abbreviation')}#{j}: weight {m.get('weightLb')}")
                r = p.get("ratings") or {}
                for attr, val in r.items():
                    if attr == "source":
                        continue
                    if isinstance(val, (int, float)) and not (0 <= val <= 99):
                        rep.issue(f"roster {y} {t.get('abbreviation')}#{j}: rating {attr}={val} out of 0-99")


def pass5_cross_year(rep: Report, data: dict) -> None:
    """Pass 5: A drafted player should appear in following years' rosters."""
    rep.start("5. cross-year consistency (drafted players show up on rosters)")
    # Sample 20 random picks per draft year and check they're on a roster the year after
    import random
    rng = random.Random(0)
    for y in range(2008, 2025):
        d = data["drafts"].get(y)
        next_roster = data["rosters"].get(y)   # year-N draft happens BEFORE the year-N season
        if not d or not next_roster:
            continue
        picks = d.get("players", [])
        if not picks:
            continue
        sample = rng.sample(picks, min(10, len(picks)))

        all_roster_names = set()
        for t in next_roster.get("teams", []):
            for p in t.get("players", []):
                n = p.get("name") or {}
                all_roster_names.add((n.get("first", "").lower(), n.get("last", "").lower()))

        missing = []
        for p in sample:
            n = p.get("name") or {}
            key = (n.get("first", "").lower(), n.get("last", "").lower())
            if key not in all_roster_names and key != ("", ""):
                missing.append(f"{n.get('first')} {n.get('last')}")
        # Some draftees don't make a roster (UDFA fates etc.) - allow some misses
        if len(missing) > 5:   # >50% missing is suspicious
            rep.issue(f"draft {y} -> roster {y}: {len(missing)}/10 sampled picks missing "
                      f"(e.g. {missing[:3]}); join broken?")


def pass6_field_completeness(rep: Report, data: dict) -> None:
    """Pass 6: Coverage stats per field. Flags any field that mostly missing."""
    rep.start("6. field completeness (% of records with each field populated)")

    # Drafts
    for y, d in data["drafts"].items():
        picks = d.get("players", [])
        if not picks:
            continue
        counts = collections.Counter()
        for p in picks:
            if (p.get("name") or {}).get("first"):
                counts["name"] += 1
            if p.get("position"):
                counts["position"] += 1
            if p.get("college"):
                counts["college"] += 1
            if (p.get("draft") or {}).get("pick"):
                counts["draft.pick"] += 1
            if (p.get("measurables") or {}).get("heightIn"):
                counts["measurables.heightIn"] += 1
            if (p.get("measurables") or {}).get("weightLb"):
                counts["measurables.weightLb"] += 1
            if (p.get("combine") or {}).get("fortyYd"):
                counts["combine.fortyYd"] += 1
        n = len(picks)
        for field, count in counts.items():
            pct = 100 * count / n
            if pct < 80 and field in ("name", "position", "draft.pick"):
                rep.issue(f"draft {y}: {field} present in only {pct:.0f}% of {n} picks")
        if 100 * counts.get("measurables.heightIn", 0) / n < 60 and y < 2026:
            rep.issue(f"draft {y}: heightIn present in only "
                      f"{100*counts.get('measurables.heightIn',0)/n:.0f}% of picks")

    # Rosters
    for y, d in data["rosters"].items():
        all_players = [p for t in d.get("teams", []) for p in t.get("players", [])]
        if not all_players:
            continue
        counts = collections.Counter()
        for p in all_players:
            if (p.get("name") or {}).get("first") and (p.get("name") or {}).get("last"):
                counts["name"] += 1
            if p.get("position"):
                counts["position"] += 1
            if p.get("jerseyNumber") is not None:
                counts["jerseyNumber"] += 1
            if p.get("age") is not None:
                counts["age"] += 1
            if p.get("yearsPro") is not None:
                counts["yearsPro"] += 1
            if p.get("ratings"):
                counts["ratings"] += 1
        n = len(all_players)
        if 100 * counts["name"] / n < 95:
            rep.issue(f"roster {y}: name missing on {100 - 100*counts['name']/n:.0f}% of players")
        # Madden ratings should be present when the Madden file exists for that year
        if y < 2024:
            pct_ratings = 100 * counts["ratings"] / n
            if pct_ratings < 30:
                rep.issue(f"roster {y}: ratings present in only {pct_ratings:.0f}% "
                          f"of {n} players (Madden join failure?)")


def pass7_duplicates(rep: Report, data: dict) -> None:
    """Pass 7: True duplicates only - same (first, last, position) on the same
    team in the same year. Real NFL teams sometimes have two different
    players with the same name (e.g. Roy Williams WR + Roy Williams S on
    DAL 2008); those have different positions so they don't trip this check.
    Draft pick duplicates also have to match by team to flag - sup draft picks
    legitimately share round/pick numbers with the regular draft."""
    rep.start("7. duplicates (same name+position on same team within a year)")
    for y, d in data["rosters"].items():
        for t in d.get("teams", []):
            seen = set()
            for p in t.get("players", []):
                n = p.get("name") or {}
                # Include jersey number: two real players with the same name
                # on the same team in the same position is rare but happens
                # (Darrell Williams x 2 on LA Rams 2018 - both OL, different
                # jerseys). Only flag when even the jersey matches.
                key = (n.get("first", "").lower(),
                       n.get("last", "").lower(),
                       p.get("position", ""),
                       p.get("jerseyNumber"))
                if key in seen and key[0] and key[1]:
                    rep.issue(f"roster {y} {t.get('abbreviation')}: duplicate "
                              f"{key[0]} {key[1]} ({key[2]}, #{key[3]})")
                seen.add(key)
    for y, d in data["drafts"].items():
        # Same (round, pick, team) twice would be a real bug; supplemental
        # draft reuses (round, pick) but with a different team, so include
        # team in the key.
        slots = collections.Counter()
        for p in d.get("players", []):
            draft = p.get("draft") or {}
            slots[(draft.get("round"), draft.get("pick"), draft.get("team"))] += 1
        for slot, count in slots.items():
            if count > 1 and slot[0] is not None:
                rep.issue(f"draft {y}: {count} players at slot {slot} (should be 1)")


def main() -> int:
    rep = Report()
    data = pass1_structural(rep)
    pass2_counts(rep, data)
    pass3_known_players(rep, data)
    pass4_value_ranges(rep, data)
    pass5_cross_year(rep, data)
    pass6_field_completeness(rep, data)
    pass7_duplicates(rep, data)
    rep.finish()
    return rep.summary()


if __name__ == "__main__":
    sys.exit(main())
