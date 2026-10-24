"""Multi-platform Signal Radar & Lead Scorer for Interference Pattern."""

from __future__ import annotations

import gzip
import html
import json
import os
import re
import shutil
import sqlite3
import subprocess
import time
import tomllib
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path

import ai_cascade

ROOT = Path(__file__).resolve().parent

def _resolve_db_path() -> Path:
    env_db = os.environ.get("SPANDA_DB")
    if env_db:
        return Path(env_db)
    local_dev = ROOT / "interference.db"
    # If local dev DB exists in ROOT (e.g. Phil's PC) and is writeable, use it
    if local_dev.exists() and os.access(str(ROOT), os.W_OK):
        return local_dev
    # Otherwise use %LOCALAPPDATA%\Spanda\interference.db (safe for Program Files / clean installs)
    appdata = Path(os.environ.get("LOCALAPPDATA") or str(Path.home())) / "Spanda"
    appdata.mkdir(parents=True, exist_ok=True)
    return appdata / "interference.db"

DB_PATH = _resolve_db_path()
CAMPAIGNS_DIR = ROOT / "campaigns"

# Which tier answered the last remix, for the UI ("AI unavailable, plain template").
LAST_REMIX: dict = {}


@dataclass
class Campaign:
    slug: str
    name: str
    tagline: str
    price: str
    stripe_url: str
    download_url: str
    repo_url: str
    banner_path: str
    logo_path: str
    queries: list[str]
    signal_words: list[str]
    subreddits: list[str]
    stackexchange_sites: list[str]
    blast_template: str


def load_campaign(slug_or_path: str = "dualtooth") -> Campaign:
    path = Path(slug_or_path)
    if not path.is_file():
        path = CAMPAIGNS_DIR / f"{slug_or_path.removesuffix('.toml')}.toml"
    data = tomllib.loads(path.read_text(encoding="utf-8"))
    return Campaign(
        slug=path.stem,
        name=data.get("name", path.stem),
        tagline=data.get("tagline", ""),
        price=data.get("price", ""),
        stripe_url=data.get("stripe_url", ""),
        download_url=data.get("download_url", ""),
        repo_url=data.get("repo_url", ""),
        banner_path=data.get("banner_path", ""),
        logo_path=data.get("logo_path", ""),
        queries=list(data.get("queries", [])),
        signal_words=[w.lower() for w in data.get("signal_words", [])],
        subreddits=list(data.get("subreddits", [])),
        stackexchange_sites=list(data.get("stackexchange_sites", ["askubuntu", "superuser"])),
        blast_template=data.get("blast_template", "").strip(),
    )


def list_campaigns() -> list[str]:
    if not CAMPAIGNS_DIR.exists():
        return []
    return sorted(p.stem for p in CAMPAIGNS_DIR.glob("*.toml"))


def connect(db_path: Path = DB_PATH) -> sqlite3.Connection:
    conn = sqlite3.connect(str(db_path))
    conn.row_factory = sqlite3.Row
    conn.execute(
        """
        CREATE TABLE IF NOT EXISTS leads (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            campaign TEXT NOT NULL,
            platform TEXT NOT NULL,
            title TEXT NOT NULL,
            body TEXT NOT NULL,
            url TEXT NOT NULL UNIQUE,
            author TEXT DEFAULT '',
            created_at TEXT DEFAULT '',
            score INTEGER NOT NULL DEFAULT 0,
            pitch TEXT NOT NULL DEFAULT '',
            status TEXT NOT NULL DEFAULT 'ready',
            discovered_at REAL NOT NULL
        )
        """
    )
    conn.commit()
    return conn


def clean_html(raw: str) -> str:
    if not raw:
        return ""
    text = html.unescape(raw)
    text = re.sub(r"<[^>]+>", " ", text)
    return re.sub(r"\s+", " ", text).strip()


def score_lead(title: str, body: str, campaign: Campaign, created_at: str = "", platform: str = "") -> int:
    """Compute a 0-100 Constructive Interference score based on pain intent, keyword overlap, and recency."""
    combined = f"{title} {body}".lower()
    title_low = title.lower()
    if not campaign.signal_words:
        return 50

    # Reject "Show & Tell / Competitor" posts where OP already built their own tool/script
    competitor_phrases = (
        "i just made",
        "i made a",
        "i built a",
        "i created a",
        "i wrote a script",
        "i wrote a tool",
        "i developed",
        "my open source",
        "my little tool",
        "check out my",
        "here is a script i",
        "sharing a tool i",
        "dualboot-bt-link-keys",
        "controller-driven dual boot setup",
    )
    if any(p in combined for p in competitor_phrases) or platform.lower() in ("r/rust", "r/linustechtips"):
        return 0

    hits = 0
    title_hits = 0
    for word in campaign.signal_words:
        if word in combined:
            hits += 1
        if word in title_low:
            title_hits += 1

    # Core requirement: for Dualtooth, must mention bluetooth + dual-boot or pairing/connection issue across OSes (Multilingual: EN/ES/DE/PT/FR/JA/ZH)
    if campaign.slug == "dualtooth":
        has_bt = any(w in combined for w in ("bluetooth", "bluez", "airpods", "headphones", "earbuds", "mouse", "keyboard", "controller", "xbox", "auriculares", "kopfhörer", "écouteurs", "fones", "蓝牙", "ブルートゥース"))
        has_dual = any(w in combined for w in ("dual boot", "dual-boot", "dualboot", "both windows and linux", "switch os", "switching between windows", "windows and ubuntu", "windows and linux", "windows 11 and linux", "windows 10 and linux", "booting into windows", "debian and windows", "arranque dual", "doble arranque", "双系统", "デュアルブート"))
        has_pairing_pain = any(w in combined for w in ("pair", "re-pair", "unpair", "repair", "link key", "linkkey", "reconnect", "sync", "share", "only 1 profile", "break in arch after switching", "stop working in ubuntu after", "emparejar", "vincular", "conectar", "koppeln", "verbinden", "parear", "appairer", "配对", "ペアリング"))
        has_linux = any(w in combined for w in ("linux", "ubuntu", "arch", "fedora", "mint", "debian", "pop!_os", "manjaro", "bluez", "steamos", "bazzite", "nobara", "cachyos"))
        if not (has_bt and has_dual and (has_pairing_pain or has_linux)):
            return 0
        # Reject laptop-for-sale / spec sheet spam or macOS-only threads or unrelated soundcard/USB crashes
        if any(spam in combined for spam in ("for sale", "price:", "ksh", "whatsapp", "call/whatsapp", "warranty", "ubuntu-macos", "imac ubuntu 12", "soundcard issue", "usb controller crash")):
            return 0
    elif campaign.slug == "megazword":
        allowed_subs = {"r/claudeai", "r/cursor", "r/localllama", "r/chatgptcoding", "r/coding", "r/commandline", "r/openai", "r/artificial", "r/singularity", "r/llmdevs", "r/aidungeon", "hackernews"}
        if platform.startswith("r/") and platform.lower() not in allowed_subs:
            return 0
        if not any(w in combined for w in ("claude", "cursor", "codex", "llm", "openrouter", "rate limit", "quota", "api key", "cli")):
            return 0
    elif campaign.slug == "interference":
        allowed_subs = {"r/saas", "r/indiehackers", "r/sideproject", "r/microsaas", "r/entrepreneur", "r/entrepreneurridealong", "r/startups", "r/marketing", "r/growthhacking", "hackernews", "se:softwarerecs"}
        if platform.startswith("r/") and platform.lower() not in allowed_subs:
            return 0
        if not any(w in combined for w in ("gummysearch", "f5bot", "syften", "social listening", "find customers", "lead generation", "reddit leads", "first customers")):
            return 0

    bullseye_title = 20 if ("bluetooth" in title_low and any(d in title_low for d in ("dual boot", "dual-boot", "dualboot", "windows"))) else 0
    pain_bonus = 10 if any(q in combined for q in ("?", "how do i", "how to", "any way to", "annoying", "every time i", "tired of", "help")) else 0
    platform_bonus = 12 if platform.startswith("r/") else 0

    # Recency adjustment (boost 2024-2026, penalize pre-2021)
    recency_adj = 0
    if created_at >= "2025":
        recency_adj = 15
    elif created_at >= "2024":
        recency_adj = 8
    elif created_at and created_at < "2021":
        recency_adj = -25

    raw = 35 + bullseye_title + (title_hits * 7) + (hits * 4) + pain_bonus + platform_bonus + recency_adj
    return max(0, min(100, raw))


def fetch_stackexchange(campaign: Campaign) -> list[dict]:
    results = []
    extra_sites = ["unix", "stackoverflow", "serverfault"] if campaign.slug != "local_biz" else ["diy", "lifehacks"]
    sites = list(dict.fromkeys(campaign.stackexchange_sites + extra_sites))[:6]
    for site in sites:
        for q in campaign.queries:
            url = (
                f"https://api.stackexchange.com/2.3/search/advanced"
                f"?order=desc&sort=relevance&q={urllib.parse.quote(q)}"
                f"&site={site}&pagesize=35&filter=withbody"
            )
            try:
                req = urllib.request.Request(
                    url,
                    headers={"User-Agent": "InterferenceRadar/0.3", "Accept-Encoding": "gzip"},
                )
                with urllib.request.urlopen(req, timeout=8) as resp:
                    raw = resp.read()
                    if resp.headers.get("Content-Encoding") == "gzip" or raw[:2] == b"\x1f\x8b":
                        raw = gzip.decompress(raw)
                    data = json.loads(raw.decode("utf-8", "replace"))
                    for item in data.get("items", []):
                        title = clean_html(item.get("title", ""))
                        body = clean_html(item.get("body", ""))[:600]
                        link = item.get("link", "")
                        ts = item.get("creation_date", 0)
                        created = time.strftime("%Y-%m-%d", time.gmtime(ts)) if ts else ""
                        owner = item.get("owner", {}).get("display_name", "user")
                        results.append({
                            "platform": f"SE:{site}",
                            "title": title,
                            "body": body,
                            "url": link,
                            "author": owner,
                            "created_at": created,
                        })
            except Exception:
                continue
    return results


def fetch_github_issues(campaign: Campaign) -> list[dict]:
    results = []
    for q in campaign.queries:
        url = f"https://api.github.com/search/issues?q={urllib.parse.quote(q)}&sort=updated&order=desc&per_page=35"
        try:
            req = urllib.request.Request(
                url,
                headers={
                    "User-Agent": "InterferenceRadar/0.3",
                    "Accept": "application/vnd.github+json",
                },
            )
            with urllib.request.urlopen(req, timeout=8) as resp:
                data = json.loads(resp.read().decode("utf-8", "replace"))
                for item in data.get("items", []):
                    title = clean_html(item.get("title", ""))
                    body = clean_html(item.get("body", "") or "")[:600]
                    link = item.get("html_url", "")
                    created = (item.get("created_at") or "")[:10]
                    author = item.get("user", {}).get("login", "gh-user")
                    results.append({
                        "platform": "GitHub",
                        "title": title,
                        "body": body,
                        "url": link,
                        "author": author,
                        "created_at": created,
                    })
        except Exception:
            continue
    return results


def fetch_hn(campaign: Campaign) -> list[dict]:
    results = []
    for endpoint in ("search", "search_by_date"):
        for q in campaign.queries:
            url = (
                f"https://hn.algolia.com/api/v1/{endpoint}"
                f"?query={urllib.parse.quote(q)}&tags=(story,comment)&hitsPerPage=40"
            )
            try:
                req = urllib.request.Request(url, headers={"User-Agent": "InterferenceRadar/0.3"})
                with urllib.request.urlopen(req, timeout=8) as resp:
                    data = json.loads(resp.read().decode("utf-8", "replace"))
                    for hit in data.get("hits", []):
                        title = clean_html(hit.get("title") or hit.get("story_title") or "")
                        comment = clean_html(hit.get("comment_text") or hit.get("story_text") or "")[:600]
                        if not title and comment:
                            title = comment[:90] + "..."
                        obj_id = hit.get("objectID", "")
                        created = (hit.get("created_at") or "")[:10]
                        author = hit.get("author", "hn-user")
                        if obj_id:
                            results.append({
                                "platform": "HackerNews",
                                "title": title,
                                "body": comment,
                                "url": f"https://news.ycombinator.com/item?id={obj_id}",
                                "author": author,
                                "created_at": created,
                            })
            except Exception:
                continue
    return results


def fetch_reddit_search(campaign: Campaign) -> list[dict]:
    """Query both PullPush Archive (size=100) AND Live Reddit JSON endpoints across all queries & multilingual variants."""
    results = []
    reddit_queries = list(campaign.queries)
    if campaign.slug == "dualtooth":
        reddit_queries.extend([
            "bluetooth dual boot",
            "bluetooth dualboot",
            "bluetooth pair windows linux",
            "bluetooth re-pair dual boot",
            "airpods dual boot linux windows",
            "mx master dual boot bluetooth",
            "keychron dual boot bluetooth",
            "xbox controller bluetooth dual boot",
            "bluetooth windows ubuntu",
            "bluetooth windows arch",
            "bluetooth windows fedora",
            "bluetooth windows bazzite",
            "bluetooth arranque dual windows linux",
            "bluetooth dual boot kopfhörer",
        ])
    reddit_queries = list(dict.fromkeys(reddit_queries))

    for q in reddit_queries:
        # 1. PullPush Reddit search API (100 results per query)
        url = f"https://api.pullpush.io/reddit/search/submission/?q={urllib.parse.quote(q)}&size=100&sort=desc"
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "InterferenceRadar/0.3"})
            with urllib.request.urlopen(req, timeout=8) as resp:
                data = json.loads(resp.read().decode("utf-8", "replace"))
                for post in data.get("data", []):
                    title = clean_html(post.get("title", ""))
                    body = clean_html(post.get("selftext", "") or "")[:600]
                    if body in ("[removed]", "[deleted]"):
                        continue
                    sub = post.get("subreddit", "reddit")
                    permalink = post.get("permalink") or f"/r/{sub}/comments/{post.get('id', '')}/"
                    ts = post.get("created_utc", 0)
                    created = time.strftime("%Y-%m-%d", time.gmtime(int(ts))) if ts else ""
                    author = post.get("author", "redditor")
                    results.append({
                        "platform": f"r/{sub}",
                        "title": title,
                        "body": body,
                        "url": f"https://www.reddit.com{permalink}",
                        "author": author,
                        "created_at": created,
                    })
        except Exception:
            pass

        # 2. Live Old-Reddit JSON search (catches posts from the last 5 minutes!)
        live_url = f"https://old.reddit.com/search.json?q={urllib.parse.quote(q)}&sort=new&limit=50"
        try:
            req = urllib.request.Request(live_url, headers={"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) SpandaRadar/0.3"})
            with urllib.request.urlopen(req, timeout=7) as resp:
                data = json.loads(resp.read().decode("utf-8", "replace"))
                for child in data.get("data", {}).get("children", []):
                    post = child.get("data", {})
                    title = clean_html(post.get("title", ""))
                    body = clean_html(post.get("selftext", "") or "")[:600]
                    sub = post.get("subreddit", "reddit")
                    permalink = post.get("permalink", "")
                    ts = post.get("created_utc", 0)
                    created = time.strftime("%Y-%m-%d", time.gmtime(int(ts))) if ts else ""
                    author = post.get("author", "redditor")
                    if permalink:
                        results.append({
                            "platform": f"r/{sub}",
                            "title": title,
                            "body": body,
                            "url": f"https://www.reddit.com{permalink}",
                            "author": author,
                            "created_at": created,
                        })
        except Exception:
            pass
    return results


def fetch_lemmy_fediverse(campaign: Campaign) -> list[dict]:
    """Search Lemmy / Fediverse communities (c/linux, c/linux_gaming, c/selfhosted, c/technology)."""
    results = []
    for q in campaign.queries[:6]:
        url = f"https://lemmy.world/api/v3/search?q={urllib.parse.quote(q)}&type_=Posts&sort=TopAll&limit=30"
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "InterferenceRadar/0.3"})
            with urllib.request.urlopen(req, timeout=8) as resp:
                data = json.loads(resp.read().decode("utf-8", "replace"))
                for item in data.get("posts", []):
                    post = item.get("post", {})
                    comm = item.get("community", {}).get("name", "lemmy")
                    creator = item.get("creator", {}).get("name", "fediverse")
                    title = clean_html(post.get("name", ""))
                    body = clean_html(post.get("body", "") or "")[:600]
                    ap_id = post.get("ap_id", "")
                    published = (post.get("published") or "")[:10]
                    if ap_id and title:
                        results.append({
                            "platform": f"Lemmy:{comm}",
                            "title": title,
                            "body": body,
                            "url": ap_id,
                            "author": creator,
                            "created_at": published,
                        })
        except Exception:
            continue
    return results


def fetch_open_web_forums(campaign: Campaign) -> list[dict]:
    """Sweep open-web forums (Arch, Fedora, Ubuntu Discourse, Manjaro, LTT, Framework, Nextdoor, Yelp) via DuckDuckGo HTML."""
    results = []
    if campaign.slug == "local_biz":
        forum_sites = ["site:reddit.com", "site:nextdoor.com", "site:yelp.com", "site:houzz.com", "site:thumbtack.com"]
    else:
        forum_sites = [
            "site:bbs.archlinux.org",
            "site:discussion.fedoraproject.org",
            "site:discourse.ubuntu.com",
            "site:forum.manjaro.org",
            "site:community.frame.work",
            "site:forums.linuxmint.com",
        ]
    for site_filter in forum_sites:
        for q in campaign.queries[:3]:
            full_q = f"{site_filter} {q}"
            url = f"https://html.duckduckgo.com/html/?q={urllib.parse.quote(full_q)}"
            try:
                req = urllib.request.Request(
                    url,
                    headers={"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"},
                )
                with urllib.request.urlopen(req, timeout=8) as resp:
                    raw_html = resp.read().decode("utf-8", "replace")
                    for m in re.finditer(
                        r'<a rel="nofollow" class="result__a" href="([^"]+)">(.*?)</a>.*?<a class="result__snippet"[^>]*>(.*?)</a>',
                        raw_html,
                        re.S,
                    ):
                        href, raw_title, raw_snip = m.groups()
                        if "uddg=" in href:
                            parsed = urllib.parse.parse_qs(urllib.parse.urlparse(href).query)
                            href = parsed.get("uddg", [href])[0]
                        domain = site_filter.replace("site:", "").split(".")[0].capitalize()
                        results.append({
                            "platform": f"Web:{domain}",
                            "title": clean_html(raw_title),
                            "body": clean_html(raw_snip),
                            "url": href,
                            "author": "forum-user",
                            "created_at": time.strftime("%Y-%m-%d"),
                        })
            except Exception:
                continue
    return results


def scan_all(campaign: Campaign, db: sqlite3.Connection | None = None) -> tuple[int, int]:
    """Run all 6 multi-network dragnet radars in parallel (16 threads), score leads, and fill the SQLite buffer."""
    from concurrent.futures import ThreadPoolExecutor, as_completed

    conn = db or connect()
    raw_leads: list[dict] = []
    fetchers = [
        fetch_reddit_search,
        fetch_stackexchange,
        fetch_hn,
        fetch_github_issues,
        fetch_lemmy_fediverse,
        fetch_open_web_forums,
    ]
    with ThreadPoolExecutor(max_workers=6) as pool:
        futures = [pool.submit(fn, campaign) for fn in fetchers]
        for fut in as_completed(futures):
            try:
                raw_leads.extend(fut.result())
            except Exception:
                pass

    added = 0
    total_scanned = len(raw_leads)
    now = time.time()

    for item in raw_leads:
        url = (item.get("url") or "").strip()
        title = (item.get("title") or "").strip()
        body = (item.get("body") or "").strip()
        if not url or not title:
            continue
        created_at = item.get("created_at", "")
        platform = item.get("platform", "web")
        score = score_lead(title, body, campaign, created_at=created_at, platform=platform)
        if score < 55:
            continue
        cur = conn.execute(
            """
            INSERT OR IGNORE INTO leads
            (campaign, platform, title, body, url, author, created_at, score, pitch, status, discovered_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 'ready', ?)
            """,
            (
                campaign.slug,
                item.get("platform", "web"),
                title,
                body,
                url,
                item.get("author", ""),
                item.get("created_at", ""),
                score,
                campaign.blast_template,
                now,
            ),
        )
        if cur.rowcount > 0:
            added += 1

    conn.commit()
    return added, total_scanned


def remix_pitch_with_megazword(
    lead_row: sqlite3.Row | dict,
    campaign: Campaign,
    backend: str = "groq-120b",
    target_lang: str = "AUTO",
    tone: str = "HELPFUL NERD",
) -> str:
    """Use a fast, free Mega Zword HTTP backend (0% paid quota) to tailor the Direct Ad Blast in any language & persona tone."""
    lang_instruction = (
        "Detect the language of the Thread Title/Body (e.g. English, Spanish, German, Portuguese, French, Japanese, Chinese) and write the entire reply fluently in THAT exact language!"
        if target_lang.upper() == "AUTO"
        else f"Write the entire reply fluently in {target_lang} (keep URLs unchanged)!"
    )
    tone_map = {
        "HELPFUL NERD": "Write like a sharp, empathetic systems engineer explaining the exact root cause before dropping the tool.",
        "CHILL NEIGHBOR": "Write like a friendly, trusted local neighbor giving a warm, zero-BS personal recommendation.",
        "SAVAGE POACHER": "Boldly call out why the existing workaround/competitor is broken and position our solution as the immediate upgrade.",
        "UNAPOLOGETIC AD": "Write with deadpan indie-maker confidence: 'I got so tired of this exact problem that I built a standalone fix for it.'",
        "MIDNIGHT HACKER": "Write with ultra-tight, high-signal terminal brevity. Zero fluff, pure technical alpha.",
    }
    tone_instruction = tone_map.get(tone.upper(), tone_map["HELPFUL NERD"])
    link = campaign.repo_url or campaign.download_url
    link_rule = (
        f"include ONLY this clean link {link} — do NOT include raw .exe or buy.stripe.com links so spam filters never flag the comment"
        if link
        else "do NOT include any links"
    )
    prompt = (
        f"Write a short, punchy, helpful Direct Reply (under 130 words) to this user's post.\n"
        f"LANGUAGE RULE: {lang_instruction}\n"
        f"VOICE / TONE: {tone_instruction}\n"
        f"Platform: {lead_row['platform']}\n"
        f"Thread Title: {lead_row['title']}\n"
        f"Thread Body: {lead_row['body'][:350]}\n\n"
        f"Product: {campaign.name} — {campaign.tagline}\n"
        f"Base Blast Template ({link_rule}):\n"
        f"{campaign.blast_template}\n\n"
        f"Output ONLY the ready-to-post reply text, no preamble or quotes."
    )
    # `backend` is kept for callers; the cascade picks the provider (docs/SPEC-ai-cascade.md).
    result = ai_cascade.complete(prompt)
    LAST_REMIX.clear()
    LAST_REMIX.update(tier=result.tier, source=result.source, keyless_left=result.keyless_left, failures=result.failures)
    if not result.ok:
        return campaign.blast_template
    text = result.text
    if link and link not in text:
        text = f"{text}\n\n{link}"
    return text
