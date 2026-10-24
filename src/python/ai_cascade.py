"""Spanda AI cascade: customer keys -> keyless pool -> local Ollama -> template.

Every call goes straight from this machine to the provider, never through a
server of ours, so each customer gets their own rate limits. The endpoint list
lives in ai_endpoints.json so a dead provider is fixed by editing data, not by
shipping a release. See docs/SPEC-ai-cascade.md.
"""

from __future__ import annotations

import base64
import hashlib
import json
import os
import random
import secrets
import shutil
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import webbrowser
from dataclasses import dataclass, field
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parent
BUNDLED_CONFIG = ROOT / "ai_endpoints.json"
VERSION = "0.3.0"
USER_AGENT = f"Spanda/{VERSION}"

MAX_TOKENS = 1500  # the keyless models reason first; a small cap returns no content
TIMEOUT = 30
BACKOFF_BASE = 30
BACKOFF_MAX = 3600
DEV_NOTE_EVERY = 3600


def home() -> Path:
    base = os.environ.get("SPANDA_HOME") or os.path.join(
        os.environ.get("LOCALAPPDATA") or str(Path.home()), "Spanda"
    )
    path = Path(base)
    path.mkdir(parents=True, exist_ok=True)
    return path


# ---------------------------------------------------------------- config


def load_config() -> dict:
    """The refreshed copy if it is newer than the bundled one, else the bundled one."""
    bundled = json.loads(BUNDLED_CONFIG.read_text(encoding="utf-8"))
    cached_path = home() / "ai_endpoints.json"
    try:
        cached = json.loads(cached_path.read_text(encoding="utf-8"))
        if _valid_config(cached) and cached.get("version", 0) > bundled.get("version", 0):
            return cached
    except (OSError, ValueError):
        pass
    return bundled


def refresh_config(timeout: float = 5) -> bool:
    """Fetch the endpoint list from refresh_url and cache it. Failures keep the old copy."""
    url = load_config().get("refresh_url", "")
    if not url:
        return False
    try:
        req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
        with urllib.request.urlopen(req, timeout=timeout) as res:
            data = json.loads(res.read().decode("utf-8"))
    except (OSError, ValueError):
        return False
    if not _valid_config(data):
        return False
    (home() / "ai_endpoints.json").write_text(json.dumps(data, indent=2), encoding="utf-8")
    return True


def _valid_config(data: object) -> bool:
    return (
        isinstance(data, dict)
        and isinstance(data.get("keyless"), list)
        and all(isinstance(e, dict) and e.get("id") and e.get("url") and e.get("model") for e in data["keyless"])
    )


# ---------------------------------------------------------------- state


def _state_path() -> Path:
    return home() / "ai_state.json"


def load_state() -> dict:
    try:
        state = json.loads(_state_path().read_text(encoding="utf-8"))
    except (OSError, ValueError):
        state = {}
    state.setdefault("health", {})
    state.setdefault("dev_notes", True)
    today = time.strftime("%Y-%m-%d")
    if state.get("day") != today:
        state["day"] = today
        state["keyless_used"] = 0
    return state


def save_state(state: dict) -> None:
    tmp = _state_path().with_suffix(".tmp")
    tmp.write_text(json.dumps(state, indent=2), encoding="utf-8")
    tmp.replace(_state_path())


def keyless_remaining(state: dict | None = None, config: dict | None = None) -> int:
    state = state or load_state()
    config = config or load_config()
    return max(0, int(config.get("keyless_daily_cap", 40)) - int(state.get("keyless_used", 0)))


def set_dev_notes(enabled: bool) -> None:
    state = load_state()
    state["dev_notes"] = bool(enabled)
    save_state(state)


# ---------------------------------------------------------------- keys (DPAPI)


def _dpapi(data: bytes, protect: bool) -> bytes:
    if sys.platform != "win32":
        raise OSError("key storage needs Windows DPAPI")
    import ctypes
    from ctypes import wintypes

    class Blob(ctypes.Structure):
        _fields_ = [("cbData", wintypes.DWORD), ("pbData", ctypes.POINTER(ctypes.c_char))]

    buf = ctypes.create_string_buffer(data, len(data))
    src = Blob(len(data), ctypes.cast(buf, ctypes.POINTER(ctypes.c_char)))
    out = Blob()
    crypt32 = ctypes.windll.crypt32
    fn = crypt32.CryptProtectData if protect else crypt32.CryptUnprotectData
    if protect:
        ok = fn(ctypes.byref(src), "Spanda", None, None, None, 0x01, ctypes.byref(out))
    else:
        ok = fn(ctypes.byref(src), None, None, None, None, 0x01, ctypes.byref(out))
    if not ok:
        raise OSError("DPAPI call failed")
    try:
        return ctypes.string_at(out.pbData, out.cbData)
    finally:
        ctypes.windll.kernel32.LocalFree(out.pbData)


def _keys_path() -> Path:
    return home() / "keys.json"


def _load_key_file() -> dict:
    try:
        return json.loads(_keys_path().read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}


def saved_keys() -> list[tuple[str, str]]:
    """(provider_id, key) pairs, decrypted. Unreadable entries are skipped."""
    pairs = []
    for provider, blobs in _load_key_file().items():
        for blob in blobs:
            try:
                pairs.append((provider, _dpapi(base64.b64decode(blob), protect=False).decode("utf-8")))
            except (OSError, ValueError):
                continue
    return pairs


def add_key(provider: str, key: str) -> None:
    key = key.strip()
    data = _load_key_file()
    blobs = data.setdefault(provider, [])
    if any(k == key for p, k in saved_keys() if p == provider):
        return
    blobs.append(base64.b64encode(_dpapi(key.encode("utf-8"), protect=True)).decode("ascii"))
    _keys_path().write_text(json.dumps(data, indent=2), encoding="utf-8")


def remove_keys(provider: str) -> None:
    data = _load_key_file()
    data.pop(provider, None)
    _keys_path().write_text(json.dumps(data, indent=2), encoding="utf-8")


def test_key(provider: str, key: str, config: dict | None = None) -> tuple[bool, str]:
    """Tiny live request. Returns (ok, plain-English message) for the onboarding screen."""
    config = config or load_config()
    spec = _provider(config, provider)
    if spec is None:
        return False, "Unknown provider."
    try:
        _chat(spec["url"], spec["model"], "Reply with the word OK.", key=key.strip(), max_tokens=200)
    except CallError as exc:
        if exc.status in (401, 403):
            return False, "That key was refused. Copy it again, the whole line, and paste it here."
        if exc.status == 429:
            return True, "The key works. The provider is busy right now, so Spanda will retry shortly."
        return False, f"Could not reach {spec['name']} ({exc.code}). Check your internet and try again."
    return True, f"Connected. Spanda now uses your {spec['name']} key, with no daily limit."


# ---------------------------------------------------------------- OpenRouter one-click key (OAuth PKCE)

OPENROUTER_AUTH = "https://openrouter.ai/auth"
OPENROUTER_EXCHANGE = "https://openrouter.ai/api/v1/auth/keys"
SIGN_IN_TIMEOUT = 300

_DONE_PAGE = (
    "<html><body style='font-family:sans-serif;text-align:center;margin-top:15vh'>"
    "<h2>{title}</h2><p>You can close this tab and go back to Spanda.</p></body></html>"
)


def _pkce_pair() -> tuple[str, str]:
    verifier = secrets.token_urlsafe(64)
    challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode("ascii")).digest()).rstrip(b"=").decode()
    return verifier, challenge


def _wait_for_code(server: HTTPServer, timeout: float) -> str:
    """Serve the localhost callback until OpenRouter redirects back with ?code=, or time runs out."""
    got: dict = {}

    class Callback(BaseHTTPRequestHandler):
        def do_GET(self):
            query = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query)
            code = (query.get("code") or [""])[0]
            if code:
                got["code"] = code
            title = "Spanda is connected to OpenRouter." if code else "Sign-in was cancelled."
            body = _DONE_PAGE.format(title=title).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            if code or "error" in query:
                got.setdefault("code", "")
                threading.Thread(target=server.shutdown, daemon=True).start()

        def log_message(self, *args):
            pass

    server.RequestHandlerClass = Callback
    timer = threading.Timer(timeout, server.shutdown)
    timer.start()
    try:
        server.serve_forever(poll_interval=0.2)
    finally:
        timer.cancel()
        server.server_close()
    return got.get("code", "")


def _exchange_code(code: str, verifier: str) -> str:
    body = json.dumps({"code": code, "code_verifier": verifier, "code_challenge_method": "S256"}).encode("utf-8")
    req = urllib.request.Request(
        OPENROUTER_EXCHANGE,
        data=body,
        headers={"Content-Type": "application/json", "User-Agent": USER_AGENT},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=TIMEOUT) as res:
            key = json.loads(res.read().decode("utf-8")).get("key", "")
    except urllib.error.HTTPError as exc:
        raise CallError(f"http{exc.code}", exc.code) from None
    except (urllib.error.URLError, OSError):
        raise CallError("network") from None
    except ValueError:
        raise CallError("badjson") from None
    if not key:
        raise CallError("nokey")
    return key


def openrouter_sign_in(open_browser=webbrowser.open, timeout: float = SIGN_IN_TIMEOUT) -> tuple[bool, str]:
    """One click: the customer signs in to OpenRouter in their browser and Spanda receives their own key.

    Blocks until the browser comes back or `timeout` seconds pass. Returns (ok, plain-English message).
    """
    verifier, challenge = _pkce_pair()
    server = HTTPServer(("127.0.0.1", 0), BaseHTTPRequestHandler)
    callback = f"http://127.0.0.1:{server.server_address[1]}/callback"
    url = OPENROUTER_AUTH + "?" + urllib.parse.urlencode(
        {"callback_url": callback, "code_challenge": challenge, "code_challenge_method": "S256"}
    )
    open_browser(url)
    code = _wait_for_code(server, timeout)
    if not code:
        return False, "Sign-in did not finish. Click 'Sign in with OpenRouter' to try again."
    try:
        key = _exchange_code(code, verifier)
    except CallError as exc:
        return False, f"OpenRouter did not hand over a key ({exc.code}). Click 'Sign in with OpenRouter' to try again."
    add_key("openrouter", key)
    return True, "Connected. Spanda now uses your own OpenRouter key."


def _provider(config: dict, provider: str) -> dict | None:
    return next((p for p in config.get("key_providers", []) if p["id"] == provider), None)


# ---------------------------------------------------------------- HTTP


class CallError(Exception):
    def __init__(self, code: str, status: int = 0, retry_after: float = 0):
        super().__init__(code)
        self.code = code
        self.status = status
        self.retry_after = retry_after


def _chat(url: str, model: str, prompt: str, key: str = "", max_tokens: int = MAX_TOKENS) -> str:
    body = json.dumps(
        {
            "model": model,
            "stream": False,
            "max_tokens": max_tokens,
            "messages": [{"role": "user", "content": prompt}],
        }
    ).encode("utf-8")
    headers = {"Content-Type": "application/json", "User-Agent": USER_AGENT}
    if key:
        headers["Authorization"] = f"Bearer {key}"
    req = urllib.request.Request(url, data=body, headers=headers, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=TIMEOUT) as res:
            payload = json.loads(res.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        retry = exc.headers.get("Retry-After", "") if exc.headers else ""
        raise CallError(f"http{exc.code}", exc.code, float(retry) if retry.isdigit() else 0) from None
    except (urllib.error.URLError, OSError):
        raise CallError("network") from None
    except ValueError:
        raise CallError("badjson") from None
    return _content(payload)


def _content(payload: dict) -> str:
    """Only the final answer. The reasoning field is the model thinking, never the reply."""
    try:
        choice = payload["choices"][0]
        text = choice["message"].get("content") or ""
    except (KeyError, IndexError, TypeError, AttributeError):
        raise CallError("badshape") from None
    text = text.strip()
    if choice.get("finish_reason") == "length":
        text = _last_sentence(text)
    if not text:
        raise CallError("empty")
    return text


def _last_sentence(text: str) -> str:
    """A reply cut off by the token limit ends mid-sentence; keep what ends cleanly."""
    end = max(text.rfind(mark) for mark in (".", "!", "?", "\n"))
    return text[: end + 1].strip() if end > len(text) // 3 else text


# ---------------------------------------------------------------- health and choice


def _record(state: dict, endpoint_id: str, ok: bool, exc: CallError | None = None) -> None:
    h = state["health"].setdefault(endpoint_id, {"ok": 0, "fail": 0, "strikes": 0, "backoff_until": 0})
    if ok:
        h["ok"] += 1
        h["strikes"] = 0
        h["backoff_until"] = 0
        return
    h["fail"] += 1
    h["strikes"] += 1
    wait = min(BACKOFF_MAX, BACKOFF_BASE * 2 ** (h["strikes"] - 1))
    if exc is not None and exc.retry_after:
        wait = max(wait, min(exc.retry_after, BACKOFF_MAX))
    h["backoff_until"] = time.time() + wait


def _ordered(endpoints: list[dict], state: dict, rng: random.Random) -> list[dict]:
    """Healthy endpoints, shuffled with a bias toward a good track record."""
    now = time.time()
    healthy = [e for e in endpoints if state["health"].get(e["id"], {}).get("backoff_until", 0) <= now]

    def weight(e: dict) -> float:
        h = state["health"].get(e["id"], {})
        return (h.get("ok", 0) + 1) / (h.get("ok", 0) + h.get("fail", 0) + 2)

    # Weighted shuffle: sort by u ** (1 / weight), the Efraimidis-Spirakis trick.
    return sorted(healthy, key=lambda e: rng.random() ** (1 / weight(e)), reverse=True)


# ---------------------------------------------------------------- tiers


@dataclass
class Result:
    text: str = ""
    tier: str = "template"  # key | megazword | keyless | ollama | template
    source: str = ""
    failures: list[str] = field(default_factory=list)
    keyless_left: int = 0

    @property
    def ok(self) -> bool:
        return bool(self.text)


def _try_keys(prompt: str, config: dict, state: dict, rng: random.Random, result: Result) -> bool:
    pairs = saved_keys()
    rng.shuffle(pairs)
    for provider, key in pairs:
        spec = _provider(config, provider)
        if spec is None:
            continue
        eid = f"key:{provider}:{key[-4:]}"
        if state["health"].get(eid, {}).get("backoff_until", 0) > time.time():
            result.failures.append(f"{eid}=backoff")
            continue
        try:
            result.text = _chat(spec["url"], spec["model"], prompt, key=key)
        except CallError as exc:
            _record(state, eid, False, exc)
            result.failures.append(f"key:{provider}={exc.code}")
            continue
        _record(state, eid, True)
        result.tier, result.source = "key", provider
        return True
    return False


def _try_megazword(prompt: str, result: Result) -> bool:
    """Phil's PC only: used when megazword is on PATH. Customers never have it."""
    if os.environ.get("SPANDA_USE_MEGAZWORD", "1") == "0":
        return False
    exe = shutil.which("megazword")
    if not exe:
        return False
    try:
        res = subprocess.run(
            [exe, "run", "--free-only", prompt],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=40,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
    except (OSError, subprocess.TimeoutExpired):
        result.failures.append("megazword=error")
        return False
    text = "\n".join(ln for ln in res.stdout.strip().splitlines() if not ln.startswith("[megazword]")).strip()
    if res.returncode != 0 or not text:
        result.failures.append(f"megazword=rc{res.returncode}")
        return False
    result.text, result.tier, result.source = text, "megazword", "megazword"
    return True


def _try_keyless(prompt: str, config: dict, state: dict, rng: random.Random, result: Result, capped: bool) -> bool:
    if capped and keyless_remaining(state, config) <= 0:
        result.failures.append("keyless=daily-cap")
        return False
    order = _ordered(config.get("keyless", []), state, rng)
    if not order:
        result.failures.append("keyless=all-backoff")
    for endpoint in order:
        try:
            result.text = _chat(endpoint["url"], endpoint["model"], prompt)
        except CallError as exc:
            _record(state, endpoint["id"], False, exc)
            result.failures.append(f"{endpoint['id']}={exc.code}")
            continue
        _record(state, endpoint["id"], True)
        if capped:
            state["keyless_used"] = int(state.get("keyless_used", 0)) + 1
        result.tier, result.source = "keyless", endpoint["id"]
        return True
    return False


def ollama_models(config: dict | None = None, timeout: float = 2) -> list[str]:
    base = (config or load_config()).get("ollama", {}).get("url", "http://127.0.0.1:11434")
    try:
        with urllib.request.urlopen(f"{base}/api/tags", timeout=timeout) as res:
            data = json.loads(res.read().decode("utf-8"))
    except (OSError, ValueError):
        return []
    return [m["name"] for m in data.get("models", []) if m.get("name")]


def _try_ollama(prompt: str, config: dict, state: dict, result: Result) -> bool:
    models = ollama_models(config)
    if not models:
        result.failures.append("ollama=absent")
        return False
    model = state.get("ollama_model") if state.get("ollama_model") in models else models[0]
    base = config.get("ollama", {}).get("url", "http://127.0.0.1:11434")
    body = json.dumps({"model": model, "stream": False, "messages": [{"role": "user", "content": prompt}]})
    req = urllib.request.Request(
        f"{base}/api/chat", data=body.encode("utf-8"), headers={"Content-Type": "application/json"}, method="POST"
    )
    try:
        with urllib.request.urlopen(req, timeout=120) as res:
            text = (json.loads(res.read().decode("utf-8")).get("message") or {}).get("content", "").strip()
    except (OSError, ValueError):
        result.failures.append("ollama=error")
        return False
    if not text:
        result.failures.append("ollama=empty")
        return False
    result.text, result.tier, result.source = text, "ollama", model
    return True


def complete(prompt: str, rng: random.Random | None = None) -> Result:
    """Run the cascade. An empty Result means every tier failed; use the template."""
    rng = rng or random.Random()
    config = load_config()
    state = load_state()
    result = Result()
    has_keys = bool(saved_keys())
    try:
        if _try_keys(prompt, config, state, rng, result):
            return result
        if _try_megazword(prompt, result):
            return result
        # The keyless pool is capped per day only while the customer has no key of their own.
        if _try_keyless(prompt, config, state, rng, result, capped=not has_keys):
            return result
        if _try_ollama(prompt, config, state, result):
            return result
        _dev_note(config, state, result.failures)
        return result
    finally:
        result.keyless_left = keyless_remaining(state, config)
        save_state(state)


# ---------------------------------------------------------------- developer note


def _dev_note(config: dict, state: dict, failures: list[str]) -> bool:
    """One anonymous note when every tier failed: version and error codes only, at most hourly."""
    url = config.get("dev_note_url", "")
    if not url or not state.get("dev_notes", True):
        return False
    if time.time() - float(state.get("last_dev_note", 0)) < DEV_NOTE_EVERY:
        return False
    state["last_dev_note"] = time.time()
    text = f"Spanda {VERSION}: every AI tier failed, so a customer got the plain template. " + ", ".join(failures)
    req = urllib.request.Request(
        url,
        data=text[:1000].encode("utf-8"),
        headers={"Title": "Spanda AI cascade down", "Tags": "warning", "User-Agent": USER_AGENT},
        method="POST",
    )
    try:
        urllib.request.urlopen(req, timeout=5).close()
    except OSError:
        return False
    return True
