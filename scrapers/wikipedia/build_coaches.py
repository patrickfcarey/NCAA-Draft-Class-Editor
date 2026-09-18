#!/usr/bin/env python3
"""
Build a canonical NFL coaching-staff file per season from Wikipedia team-season
infoboxes.

Madden 08's franchise save carries a COCH table with exactly 4 coaches per team
(head coach + offensive / defensive / special-teams coordinators). The template
ships with the 2007 staff (Lovie Smith, Rick Dennison, ...). This scraper pulls
the real staff for each season so the franchise compiler can rewrite the names.

Source: en.wikipedia.org "{year} {City Team} season" articles. The infobox
carries `coach` (head coach), `off_coach` (offensive coordinator) and
`def_coach` (defensive coordinator). There is NO special-teams field, so ST is
left to the compiler (keeps the template's ST coach). Wikipedia is not
Cloudflare-protected (unlike Pro-Football-Reference), and pages are fetched in
batches of up to 50 via the MediaWiki query API.

Team names track relocations (St. Louis -> Los Angeles Rams, San Diego -> LA
Chargers, Oakland -> Las Vegas Raiders, Washington Redskins -> Football Team ->
Commanders) so each year points at the era-correct article. The emitted "team"
key is the era mascot; the franchise compiler resolves it to a TGID by mascot
(with Redskins/Commanders aliasing).

Usage:
    python build_coaches.py --year 2018
    python build_coaches.py --all
"""
import argparse
import json
import os
import re
import sys
import time
import urllib.parse
import urllib.request

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(REPO_ROOT, "data", "canonical")
API = "https://en.wikipedia.org/w/api.php"
UA = "NCAA-Draft-Class-Editor coach scraper (contact: patrickfcarey@gmail.com)"

YEARS = range(2008, 2027)

# 28 franchises whose city+mascot are stable across 2008-2026.
STABLE = [
    ("Cardinals", "Arizona"), ("Falcons", "Atlanta"), ("Ravens", "Baltimore"),
    ("Bills", "Buffalo"), ("Panthers", "Carolina"), ("Bears", "Chicago"),
    ("Bengals", "Cincinnati"), ("Browns", "Cleveland"), ("Cowboys", "Dallas"),
    ("Broncos", "Denver"), ("Lions", "Detroit"), ("Packers", "Green Bay"),
    ("Texans", "Houston"), ("Colts", "Indianapolis"), ("Jaguars", "Jacksonville"),
    ("Chiefs", "Kansas City"), ("Dolphins", "Miami"), ("Vikings", "Minnesota"),
    ("Patriots", "New England"), ("Saints", "New Orleans"), ("Giants", "New York"),
    ("Jets", "New York"), ("Eagles", "Philadelphia"), ("Steelers", "Pittsburgh"),
    ("49ers", "San Francisco"), ("Seahawks", "Seattle"), ("Buccaneers", "Tampa Bay"),
    ("Titans", "Tennessee"),
]


def teams_for_year(year):
    """Return [{key: era-mascot, wiki: 'City Mascot'}] for all 32 teams."""
    teams = [{"key": m, "wiki": f"{c} {m}"} for m, c in STABLE]
    teams.append({"key": "Rams",
                  "wiki": "St. Louis Rams" if year <= 2015 else "Los Angeles Rams"})
    teams.append({"key": "Chargers",
                  "wiki": "San Diego Chargers" if year <= 2016 else "Los Angeles Chargers"})
    teams.append({"key": "Raiders",
                  "wiki": "Oakland Raiders" if year <= 2019 else "Las Vegas Raiders"})
    if year <= 2019:
        teams.append({"key": "Redskins", "wiki": "Washington Redskins"})
    elif year <= 2021:
        teams.append({"key": "Football Team", "wiki": "Washington Football Team"})
    else:
        teams.append({"key": "Commanders", "wiki": "Washington Commanders"})
    return teams


def api_get(params):
    url = API + "?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)


def fetch_wikitext(titles):
    """title -> wikitext for up to 50 titles, following redirects."""
    out = {}
    for i in range(0, len(titles), 50):
        chunk = titles[i:i + 50]
        data = api_get({
            "action": "query", "prop": "revisions", "rvprop": "content",
            "rvslots": "main", "format": "json", "redirects": 1,
            "titles": "|".join(chunk),
        })
        q = data.get("query", {})
        # Map any title normalization / redirects back to the requested title.
        alias = {}
        for n in q.get("normalized", []):
            alias[n["to"]] = n["from"]
        for rd in q.get("redirects", []):
            alias[rd["to"]] = alias.get(rd["from"], rd["from"])
        for page in q.get("pages", {}).values():
            title = page.get("title", "")
            requested = alias.get(title, title)
            revs = page.get("revisions")
            if not revs:
                continue
            out[requested] = revs[0]["slots"]["main"]["*"]
        time.sleep(0.2)
    return out


# Pull a named infobox parameter's value up to the next "\n| key =" line.
def infobox_field(wikitext, key):
    m = re.search(r"\n\|\s*" + re.escape(key) + r"\s*=\s*(.*?)(?=\n\|\s*[\w_]+\s*=|\n\}\})",
                  wikitext, re.DOTALL)
    return m.group(1).strip() if m else ""


def extract_staff_template(wikitext):
    """Return the text of the first {{... staff ...}} template (balanced
    braces), or None. NFL season articles carry {{NFL final staff}} with a
    full coaching-staff breakdown even when the infobox omits coordinators."""
    m = re.search(r"\{\{\s*[^|}\n]*[Ss]taff", wikitext)
    if not m:
        return None
    i = m.start() + 2
    depth = 1
    while i < len(wikitext) - 1 and depth:
        two = wikitext[i:i + 2]
        if two == "{{":
            depth += 1; i += 2
        elif two == "}}":
            depth -= 1; i += 2
        else:
            i += 1
    return wikitext[m.start():i]


# Bullet lines like "* Head coach – [[Steve Wilks]]" inside the staff template.
# Head coach is anchored to bullet start so "Assistant to the head coach" is
# not mistaken for it.
STAFF_ROLE_RE = {
    "headCoach":             r"(?mi)^\s*\*\s*Head coach\s*[–\-:]\s*(.+)$",
    "offensiveCoordinator":  r"(?mi)^\s*\*\s*Offensive coordinator\s*[–\-:]\s*(.+)$",
    "defensiveCoordinator":  r"(?mi)^\s*\*\s*Defensive coordinator\s*[–\-:]\s*(.+)$",
    "specialTeams":          r"(?mi)^\s*\*\s*Special teams coordinator\s*[–\-:]\s*(.+)$",
}

# Infobox fallback fields (no special-teams equivalent in the infobox).
INFOBOX_ROLE_FIELD = {
    "headCoach": "coach",
    "offensiveCoordinator": "off_coach",
    "defensiveCoordinator": "def_coach",
}


def parse_person(raw):
    """Extract a person's name from an infobox value, taking the first person
    listed (ignores interim replacements / <br> extras) and resolving a
    [[wikilink]] to its display text."""
    if not raw:
        return None
    # First line / first entry only (drop <br>, newlines, mid-season changes).
    raw = re.split(r"<br\s*/?>|\n", raw)[0]
    raw = re.sub(r"<ref.*?</ref>|<ref[^>]*/>", "", raw, flags=re.DOTALL)
    m = re.search(r"\[\[([^\]]+)\]\]", raw)
    if m:
        target = m.group(1)
        name = target.split("|")[-1]          # display text after pipe, else target
    else:
        name = raw
    name = re.sub(r"\([^)]*\)", "", name)      # strip "(American football)" disambig
    name = re.sub(r"'{2,}", "", name)           # wiki bold/italic markup (keep name apostrophes)
    name = name.replace('"', "")
    name = re.sub(r"\s+", " ", name).strip(" .,")
    if not name or " " not in name:
        return None
    first, last = name.split(" ", 1)
    return {"first": first.strip(), "last": last.strip()}


def build_year(year, wikitext_by_title):
    staffs = []
    for t in teams_for_year(year):
        title = f"{year} {t['wiki']} season"
        wt = wikitext_by_title.get(title)
        entry = {"team": t["key"]}
        if wt:
            # Primary: the {{NFL final staff}} template (all 4 roles incl. ST).
            staff = extract_staff_template(wt)
            if staff:
                for role, pat in STAFF_ROLE_RE.items():
                    m = re.search(pat, staff)
                    if m:
                        person = parse_person(m.group(1))
                        if person:
                            entry[role] = person
            # Fallback: infobox fields for any role the template didn't yield.
            for role, field in INFOBOX_ROLE_FIELD.items():
                if role not in entry:
                    person = parse_person(infobox_field(wt, field))
                    if person:
                        entry[role] = person
        staffs.append(entry)
    return {
        "schema": "ncaa-madden-coaches/v1",
        "year": year,
        "source": "en.wikipedia.org team-season infoboxes",
        "teams": staffs,
    }


def main():
    ap = argparse.ArgumentParser(description="Build canonical NFL coaching staffs from Wikipedia.")
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--year", type=int)
    g.add_argument("--all", action="store_true")
    ap.add_argument("--out-dir", default=OUT_DIR)
    args = ap.parse_args()

    os.makedirs(args.out_dir, exist_ok=True)
    years = list(YEARS) if args.all else [args.year]

    for y in years:
        titles = [f"{y} {t['wiki']} season" for t in teams_for_year(y)]
        wt = fetch_wikitext(titles)
        doc = build_year(y, wt)
        hc = sum(1 for s in doc["teams"] if "headCoach" in s)
        oc = sum(1 for s in doc["teams"] if "offensiveCoordinator" in s)
        dc = sum(1 for s in doc["teams"] if "defensiveCoordinator" in s)
        st = sum(1 for s in doc["teams"] if "specialTeams" in s)
        out = os.path.join(args.out_dir, f"coaches-{y}.json")
        with open(out, "w", encoding="utf-8") as f:
            json.dump(doc, f, indent=2)
        print(f"  {y}: HC {hc}/32  OC {oc}/32  DC {dc}/32  ST {st}/32  -> {out}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
