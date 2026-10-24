"""Hybrid Delivery Engine for Interference Pattern: Live CDP Port 9222 + Isolated Burner Browser."""

from __future__ import annotations

import os
import shutil
import subprocess
from pathlib import Path

try:
    import pyperclip
except ImportError:
    pyperclip = None

ROOT = Path(__file__).resolve().parent
BURNER_PROFILE_DIR = ROOT / "burner-browser-profile"
CDP_URL = "http://127.0.0.1:9222"


def copy_to_clipboard(text: str) -> bool:
    if pyperclip is not None:
        try:
            pyperclip.copy(text)
            return True
        except Exception:
            pass
    try:
        proc = subprocess.Popen(
            ["clip.exe"],
            stdin=subprocess.PIPE,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0x08000000),
        )
        proc.communicate(input=text.encode("utf-16le"))
        return proc.returncode == 0
    except Exception:
        return False


def _find_browser_exe() -> str | None:
    candidates = [
        r"C:\Program Files\Google\Chrome\Application\chrome.exe",
        r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    ]
    for c in candidates:
        if os.path.exists(c):
            return c
    return shutil.which("chrome") or shutil.which("msedge")


def open_cartridge_login_window(profile_dir: str | None = None) -> tuple[bool, str]:
    """Open a visible browser window for the chosen Burner Cartridge (PC or USB stick) so the user can log into Reddit/HN/StackExchange."""
    target_dir = Path(profile_dir) if profile_dir else BURNER_PROFILE_DIR
    target_dir.mkdir(parents=True, exist_ok=True)
    exe = _find_browser_exe()
    if not exe:
        return False, "Chrome/Edge not found"

    ps_cmd = (
        f'$a = New-ScheduledTaskAction -Execute "{exe}" -Argument \'--user-data-dir="{target_dir}" --remote-debugging-port=9222 --disable-blink-features=AutomationControlled --no-first-run --no-default-browser-check --new-window "https://old.reddit.com/login" "https://news.ycombinator.com/login" "https://askubuntu.com/users/login"\'; '
        f'$p = New-ScheduledTaskPrincipal -UserId "$env:USERNAME" -LogonType Interactive; '
        f'Register-ScheduledTask -TaskName "IFP_Burner_Login" -Action $a -Principal $p -Force | Out-Null; '
        f'Start-ScheduledTask -TaskName "IFP_Burner_Login"'
    )
    subprocess.run(
        ["powershell", "-NoProfile", "-WindowStyle", "Hidden", "-Command", ps_cmd],
        capture_output=True,
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0x08000000),
    )
    return True, f"Opened Burner Cartridge Login Window -> {target_dir}"


def ensure_live_browser(url: str = "https://www.reddit.com") -> bool:
    """Ensure the interactive Chrome burner window is open on the user's desktop with CDP port 9222."""
    import urllib.request

    try:
        urllib.request.urlopen(f"{CDP_URL}/json/version", timeout=1.5)
        return True
    except Exception:
        pass

    BURNER_PROFILE_DIR.mkdir(parents=True, exist_ok=True)
    exe = _find_browser_exe()
    if not exe:
        return False

    ps_cmd = (
        f'$a = New-ScheduledTaskAction -Execute "{exe}" -Argument \'--user-data-dir="{BURNER_PROFILE_DIR}" --remote-debugging-port=9222 --disable-blink-features=AutomationControlled --no-first-run --no-default-browser-check --window-position=-32000,-32000 --new-window "{url}"\'; '
        f'$p = New-ScheduledTaskPrincipal -UserId "$env:USERNAME" -LogonType Interactive; '
        f'Register-ScheduledTask -TaskName "IFP_Burner_Browser" -Action $a -Principal $p -Force | Out-Null; '
        f'Start-ScheduledTask -TaskName "IFP_Burner_Browser"'
    )
    subprocess.run(
        ["powershell", "-NoProfile", "-WindowStyle", "Hidden", "-Command", ps_cmd],
        capture_output=True,
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0x08000000),
    )
    return True


def copy_and_jump(url: str, pitch: str) -> tuple[bool, str]:
    """Copy armed payload to clipboard and open URL in our live CDP burner browser on the user's desktop."""
    copied = copy_to_clipboard(pitch)
    ensure_live_browser(url)
    try:
        from playwright.sync_api import sync_playwright

        with sync_playwright() as pw:
            browser = pw.chromium.connect_over_cdp(CDP_URL)
            ctx = browser.contexts[0]
            page = ctx.pages[0] if ctx.pages else ctx.new_page()
            page.goto(url, wait_until="domcontentloaded", timeout=25000)
            return True, f"Opened {url[:48]} in Live Burner Deck + copied payload ({len(pitch)} chars)!"
    except Exception as exc:
        return copied, f"Payload copied ({len(pitch)} chars). Live deck status: {exc}"


def playwright_fire(url: str, pitch: str, dry_run: bool = False, auto_submit: bool = True) -> tuple[bool, str]:
    """Connect to live CDP burner browser (port 9222), navigate to target, fill reply box, and submit!"""
    copy_to_clipboard(pitch)
    if dry_run or os.environ.get("MZ_DRY_RUN") == "1":
        return True, f"[DRY RUN] Would fire {len(pitch)}-char payload onto {url}."

    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        return copy_and_jump(url, pitch)

    ensure_live_browser(url)
    target_url = url.replace("https://www.reddit.com", "https://old.reddit.com")

    try:
        with sync_playwright() as pw:
            browser = pw.chromium.connect_over_cdp(CDP_URL)
            ctx = browser.contexts[0]
            page = ctx.pages[0] if ctx.pages else ctx.new_page()
            page.goto(target_url, wait_until="domcontentloaded", timeout=30000)
            page.wait_for_timeout(2000)

            # 1. Reddit (old.reddit.com bypass + anti-spam-filter link sanitization + live profile verification)
            if "old.reddit.com" in target_url:
                # Strip clickable https:// URLs on 1-karma Reddit accounts so Reddit's delayed 5-minute link-spam bot never triggers [Removed by Reddit]
                clean_lines = [
                    ln for ln in pitch.splitlines()
                    if "buy.stripe.com" not in ln and ".exe" not in ln
                ]
                clean_pitch = "\n".join(clean_lines).strip()
                import re as _re
                clean_pitch = _re.sub(r"https?://github\.com/edotwedo/Dualtooth\S*", "`edotwedo/Dualtooth` on GitHub", clean_pitch)
                clean_pitch = _re.sub(r"https?://\S+", "", clean_pitch).strip()
                if "edotwedo/Dualtooth" not in clean_pitch and "Dualtooth" in clean_pitch:
                    clean_pitch += " (search `edotwedo/Dualtooth` on GitHub)"

                ta = page.locator("form.usertext textarea[name='text']").first
                if ta.count() > 0 and ta.is_visible():
                    ta.fill(clean_pitch)
                    if auto_submit:
                        page.wait_for_timeout(800)
                        btn = page.locator("form.usertext button.save").first
                        if btn.count() > 0:
                            btn.click()
                            page.wait_for_timeout(3500)
                            errs = [e.strip() for e in page.locator("form.usertext .error").all_inner_texts() if e.strip()]
                            if errs:
                                return False, f"REDDIT RATE-LIMIT/ERR: {errs[0][:80]}"
                            # Verify on user profile that the comment is actually live and NOT [Removed by Reddit]
                            page.goto("https://old.reddit.com/user/timelesstreepeople/comments/", wait_until="domcontentloaded", timeout=15000)
                            top_comment = page.locator("div.thing.comment").first.inner_text() if page.locator("div.thing.comment").count() else ""
                            if "[ Removed by Reddit ]" in top_comment:
                                return False, f"FLAGGED BY REDDIT FILTER on {url[:40]}"
                            return True, f"VERIFIED LIVE & SURVIVED (NOT REMOVED) on {url[:45]}!"
                    return True, f"Filled Reddit reply box on {url[:45]}!"
                return False, f"ARCHIVED/LOCKED thread (no comment box) on {url[:45]}"

            # 2. StackExchange / AskUbuntu / SuperUser
            if page.locator("textarea#wmd-input").count() > 0:
                loc = page.locator("textarea#wmd-input").first
                loc.scroll_into_view_if_needed()
                loc.fill(pitch)
                if auto_submit and page.locator("button#submit-button").count() > 0:
                    page.wait_for_timeout(600)
                    page.locator("button#submit-button").first.click()
                    page.wait_for_timeout(2500)
                    return True, f"FIRED & SUBMITTED StackExchange answer on {url[:45]}!"
                return True, f"Filled StackExchange answer box on {url[:45]}!"

            # 3. Hacker News
            if page.locator("textarea[name='text']").count() > 0:
                page.locator("textarea[name='text']").first.fill(pitch)
                if auto_submit and page.locator("input[type='submit']").count() > 0:
                    page.wait_for_timeout(600)
                    page.locator("input[type='submit']").first.click()
                    page.wait_for_timeout(2000)
                    return True, f"FIRED & SUBMITTED Hacker News comment on {url[:45]}!"
                return True, f"Filled Hacker News box on {url[:45]}!"

            return True, f"Opened {url[:45]} in Live Deck (payload on clipboard — hit Ctrl+V)."
    except Exception:
        return copy_and_jump(url, pitch)
