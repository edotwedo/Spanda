using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Spanda;

sealed class SpandaForm : Form
{
    const string Tagline = "SPANDA // SADNAP v0.3  //  FIND PEOPLE ASKING FOR HELP -> SEND HELPFUL REPLIES  //  ISOLATED PROFILES READY  //  ";
    const int TitleBarHeight = 26;

    static readonly string[] Campaigns = ["dualtooth", "robinthood", "local_biz", "megazword", "interference"];
    static readonly string[] Tones = ["HELPFUL NERD", "CHILL NEIGHBOR", "SAVAGE POACHER", "UNAPOLOGETIC AD", "MIDNIGHT HACKER"];
    static readonly (string Code, string Name)[] Languages =
    [
        ("AUTO", "AUTO (MATCH POST)"),
        ("English", "EN · ENGLISH"),
        ("Spanish", "ES · ESPAÑOL"),
        ("German", "DE · DEUTSCH"),
        ("Portuguese", "PT · PORTUGUÊS"),
        ("French", "FR · FRANÇAIS"),
        ("Japanese", "JA · 日本語"),
        ("Chinese", "ZH · 中文"),
    ];

    readonly List<(string Name, string Path)> cartridges = [];
    int cartridgeIdx;
    int campaignIdx;
    int langIdx;
    int toneIdx;

    readonly SpandaLcdDisplay lcd = new();
    readonly SkinButton campaignBtn = new() { Text = "1. PRODUCT: DUALTOOTH ▸", Accent = true, LedOn = true };
    readonly SkinButton scanBtn = new() { Text = "📡 FIND NEW POSTS" };
    readonly SkinButton remixBtn = new() { Text = "🧠 AI REWRITE ($0)" };
    readonly SkinButton langBtn = new() { Text = "🌐 LANG: AUTO ▸", LedOn = true };
    readonly SkinButton toneBtn = new() { Text = "🌶️ VOICE: NERD ▸", LedOn = true };
    readonly SkinButton markDoneBtn = new() { Text = "✓ SKIP/DONE" };

    // Bottom-Left: Isolated Profile + Storage Deck (Step 4)
    readonly LcdReadout pipelineReadout = new();
    readonly SkinButton cartridgeBtn = new() { Text = "🎭 PROFILE: SLOT 1 ▸", Accent = true, LedOn = true };
    readonly SkinButton loginBurnerBtn = new() { Text = "🔑 LOG IN PROFILE" };
    readonly SkinButton usbFolderBtn = new() { Text = "🔌 USB / FOLDER" };

    // Bottom-Right: Firing Deck
    readonly SkinButton copyJumpBtn = new() { Text = "📋 COPY + OPEN TAB" };
    readonly SkinButton fireBtn = new() { Text = "🔥 SEND REPLY NOW", Accent = true, LedOn = true, Font = Skin.ButtonLarge };
    readonly SkinButton blitzBtn = new() { Text = "🚀 AUTO-SEND TOP 3", Accent = true, LedOn = true, Font = Skin.ButtonLarge };

    readonly SkinButton aiKeysBtn = new() { Text = "⚡ AI KEYS", Accent = true, LedOn = true };
    readonly SkinButton skinBtn = new() { Text = "SKIN" };
    readonly SkinButton supportBtn = new() { Text = "♥ PRO $29", Accent = true };
    readonly SkinButton minimizeBtn = new() { Text = "–" };
    readonly SkinButton closeBtn = new() { Text = "×" };
    readonly ToolTip tips = new() { InitialDelay = 200 };

    readonly LeadPlaylist playlist = new();
    readonly LcdReadout telemetryReadout = new();
    readonly TextBox pitchEditor = new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None,
        BackColor = Color.FromArgb(0x06, 0x08, 0x13),
        ForeColor = Color.FromArgb(0x00, 0xF5, 0xD4),
        Font = new Font("Consolas", 9.5f, FontStyle.Bold),
    };

    readonly float scale;
    int sentCount;

    int S(float value) => (int)Math.Round(value * scale);

    public SpandaForm()
    {
        scale = DeviceDpi / 96f;
        Text = "Spanda // SadNap";
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Skin.Body;
        DoubleBuffered = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(S(840), S(700));

        InitCartridges();

        // Top Bar Controls
        Place(aiKeysBtn, 584, 5, 80, 16);
        Place(skinBtn, 668, 5, 52, 16);
        Place(supportBtn, 724, 5, 58, 16);
        Place(minimizeBtn, 786, 5, 20, 16);
        Place(closeBtn, 810, 5, 20, 16);

        // Main Top LCD Oscilloscope + 3 Sleepy/Weepy Pandas
        Place(lcd, 14, 32, 812, 88);

        // Mode & Radar Action Strip (With plain-English labels)
        Place(campaignBtn, 14, 130, 184, 26);
        Place(scanBtn, 204, 130, 124, 26);
        Place(remixBtn, 334, 130, 124, 26);
        Place(langBtn, 464, 130, 126, 26);
        Place(toneBtn, 596, 130, 142, 26);
        Place(markDoneBtn, 744, 130, 82, 26);

        // Left Top Pane: Step 1 - Live Radar Playlist
        Place(playlist, 14, 198, 394, 290);

        // Left Bottom Pane: Step 4 - Data Pipeline & Isolated Profile Vault
        Place(pipelineReadout, 14, 530, 394, 96);
        Place(cartridgeBtn, 14, 632, 138, 32);
        Place(loginBurnerBtn, 158, 632, 132, 32);
        Place(usbFolderBtn, 296, 632, 112, 32);

        // Right Top Pane: Step 2 - Target Telemetry (What they asked & URL)
        Place(telemetryReadout, 424, 198, 402, 150);

        // Right Bottom Pane: Step 3 - Editable Reply Message
        Place(pitchEditor, 426, 390, 398, 234);

        // Bottom-Right Firing Deck
        Place(copyJumpBtn, 424, 632, 126, 38);
        Place(fireBtn, 556, 632, 136, 38);
        Place(blitzBtn, 698, 632, 128, 38);

        SetupTooltips();

        aiKeysBtn.Click += (_, _) =>
        {
            using var diag = new AiOnboardingForm();
            diag.ShowDialog(this);
        };
        skinBtn.Click += (_, _) => CycleSkin();
        supportBtn.Click += (_, _) => Process.Start(new ProcessStartInfo("https://buy.stripe.com/7sYcN72KS2CT36D75f9AA07") { UseShellExecute = true });
        minimizeBtn.Click += (_, _) => WindowState = FormWindowState.Minimized;
        closeBtn.Click += (_, _) => Close();

        campaignBtn.Click += async (_, _) =>
        {
            campaignIdx = (campaignIdx + 1) % Campaigns.Length;
            campaignBtn.Text = $"1. PRODUCT: {Campaigns[campaignIdx].ToUpperInvariant()} ▸";
            lcd.Party();
            await RefreshLeadsAsync();
        };

        langBtn.Click += (_, _) =>
        {
            langIdx = (langIdx + 1) % Languages.Length;
            var (code, label) = Languages[langIdx];
            langBtn.Text = $"🌐 LANG: {(code == "AUTO" ? "AUTO" : label[..2])} ▸";
            lcd.Party();
            lcd.Line2Color = Skin.TearCyan;
            lcd.Line2 = $"REPLY LANGUAGE -> {label}";
        };

        toneBtn.Click += (_, _) =>
        {
            toneIdx = (toneIdx + 1) % Tones.Length;
            var tone = Tones[toneIdx];
            var shortTone = tone.Split(' ').Last();
            toneBtn.Text = $"🌶️ VOICE: {shortTone} ▸";
            lcd.ToneName = tone;
            lcd.Party();
            lcd.Line2Color = Skin.Accent;
            lcd.Line2 = $"AI VOICE PERSONALITY -> [{tone}]";
        };

        cartridgeBtn.Click += (_, _) =>
        {
            cartridgeIdx = (cartridgeIdx + 1) % cartridges.Count;
            var (cName, cPath) = cartridges[cartridgeIdx];
            cartridgeBtn.Text = $"🎭 {cName} ▸";
            Directory.CreateDirectory(cPath);
            lcd.Party();
            lcd.Line2Color = Skin.TearCyan;
            lcd.Line2 = $"ISOLATED PROFILE LOADED -> {cName} ({cPath})";
            UpdatePipelineReadout();
        };

        loginBurnerBtn.Click += async (_, _) =>
        {
            var (cName, cPath) = cartridges[cartridgeIdx];
            Directory.CreateDirectory(cPath);
            lcd.Party();
            lcd.Line1 = $"OPENING LOGIN WINDOW FOR {cName}...";
            lcd.Line2 = "LOG IN ONCE -> SESSION SAVES IN PROFILE FOLDER (0 PASSWORDS STORED)";
            var py = $$"""
import sys
sys.path.insert(0, {{JsonSerializer.Serialize(ResolveInterferenceDir())}})
import blaster
blaster.open_cartridge_login_window({{JsonSerializer.Serialize(cPath)}})
""";
            await RunPythonSilentAsync(py);
        };

        usbFolderBtn.Click += (_, _) => ManageUsbOrFolder();

        scanBtn.Click += async (_, _) => await RunScanAsync();
        remixBtn.Click += async (_, _) => await RunRemixAsync();
        copyJumpBtn.Click += async (_, _) => await RunCopyJumpAsync();
        fireBtn.Click += async (_, _) => await RunFireAsync();
        blitzBtn.Click += async (_, _) => await RunBlitzWaveAsync();
        markDoneBtn.Click += async (_, _) => await MarkDoneAsync();

        playlist.SelectionChanged += (_, _) => ShowSelectedLead();
        Load += async (_, _) => await RefreshLeadsAsync();

        lcd.Marquee = Tagline;
        lcd.Line1 = "READY // PICK A QUESTION ON THE LEFT -> SEND YOUR REPLY ON THE RIGHT";
        lcd.Line2 = "ISOLATED PROFILE #1 LOADED // ZERO PASSWORDS STORED IN APP";
        UpdatePipelineReadout();
    }

    void InitCartridges()
    {
        cartridges.Clear();
        var baseDir = ResolveInterferenceDir();
        cartridges.Add(("SLOT 1 (MAIN)", Path.Combine(baseDir, "browser-profile")));
        cartridges.Add(("SLOT 2 (DEV)", Path.Combine(baseDir, "profiles", "slot-2")));
        cartridges.Add(("SLOT 3 (EXTRA)", Path.Combine(baseDir, "profiles", "slot-3")));

        // Auto-detect any removable USB drive plugged into the PC!
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady && drive.DriveType == DriveType.Removable)
            {
                cartridges.Add(($"USB ({drive.Name.TrimEnd('\\')})", Path.Combine(drive.RootDirectory.FullName, "SpandaProfiles", "profile-usb-1")));
            }
        }
        cartridges.Add(("PORTABLE", Path.Combine(baseDir, "profiles", "portable-pack")));
    }

    void SetupTooltips()
    {
        tips.SetToolTip(supportBtn, "Get Spanda Pro Lifetime License ($29) — priority updates, BYOK unlocked, and multi-campaign support.");
        tips.SetToolTip(aiKeysBtn, "Setup free AI keys (Groq, Gemini, OpenRouter) or view keyless pool & local Ollama status.");
        tips.SetToolTip(campaignBtn, "Choose which business/product you are promoting (Dualtooth, Local Business, MegaZword, etc.).");
        tips.SetToolTip(scanBtn, "Searches public Reddit, StackExchange, Hacker News & GitHub for people asking for help right now.");
        tips.SetToolTip(remixBtn, "Uses free local AI ($0 cost) to rewrite your reply so it directly answers their specific post.");
        tips.SetToolTip(langBtn, "Choose which language your reply is written in (AUTO matches the language of their post).");
        tips.SetToolTip(toneBtn, "Choose the personality/vibe of the AI reply (Helpful Nerd, Chill Neighbor, Savage Poacher, etc.).");
        tips.SetToolTip(markDoneBtn, "Hide the selected post so you don't reply to it twice.");
        tips.SetToolTip(cartridgeBtn, "Switch between isolated browser profiles (each slot has its own browser cookies on PC or USB stick).");
        tips.SetToolTip(loginBurnerBtn, "Opens Chrome for the active isolated profile so you can log into Reddit, Hacker News, or AskUbuntu. No passwords are saved in Spanda!");
        tips.SetToolTip(usbFolderBtn, "Move your isolated profile onto a USB thumb drive or open the local database folder in File Explorer.");
        tips.SetToolTip(copyJumpBtn, "Copies your reply to the clipboard and opens their post in your browser profile so you can paste & review manually.");
        tips.SetToolTip(fireBtn, "Automatically types and submits your reply into the post using your active isolated profile.");
        tips.SetToolTip(blitzBtn, "Automatically rewrites and sends replies to the Top 3 highest-scoring posts in a row.");
    }

    void ManageUsbOrFolder()
    {
        var baseDir = ResolveInterferenceDir();
        using var dialog = new FolderBrowserDialog
        {
            Description = "Pick a USB Thumb Drive (or any folder) to store an Isolated Profile — or click Cancel to open your current Data Folder in Explorer.",
            UseDescriptionForTitle = true,
            InitialDirectory = baseDir
        };
        if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            var customName = $"USB ({Path.GetFileName(dialog.SelectedPath)})";
            cartridges.Add((customName, dialog.SelectedPath));
            cartridgeIdx = cartridges.Count - 1;
            cartridgeBtn.Text = $"🎭 {customName} ▸";
            UpdatePipelineReadout();
            lcd.Party();
            lcd.Line2Color = Skin.Accent;
            lcd.Line2 = $"MOUNTED USB PROFILE -> {dialog.SelectedPath}";
        }
        else
        {
            Process.Start("explorer.exe", baseDir);
        }
    }

    void UpdatePipelineReadout()
    {
        var (cName, cPath) = cartridges[cartridgeIdx];
        var lead = playlist.SelectedLead;
        var dest = lead is null ? "(Click a post above to lock destination)" : $"{lead.Platform} -> {lead.Url}";
        pipelineReadout.ValueBadge = "0 PASSWORDS STORED";
        pipelineReadout.Text =
            $"📥 READS FROM: Reddit · 6x StackExchange · HN · GitHub · Lemmy · Web Forums (1,000-Lead Buffer)\n" +
            $"💾 SAVED AT:   {ResolveDbPath()}\n" +
            $"🎭 ACCOUNT:    {cName} -> {cPath}\n" +
            $"📤 SENDS TO:   {dest}";
    }

    void Place(Control control, int x, int y, int width, int height)
    {
        control.Bounds = new Rectangle(S(x), S(y), S(width), S(height));
        Controls.Add(control);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsMinimizeBox = 0x20000, WsSysMenu = 0x80000;
            var cp = base.CreateParams;
            cp.Style |= WsMinimizeBox | WsSysMenu;
            return cp;
        }
    }

    void CycleSkin()
    {
        var name = Skin.CyclePalette();
        BackColor = Skin.Body;
        lcd.BackColor = Skin.Lcd;
        playlist.BackColor = Skin.Lcd;
        telemetryReadout.BackColor = Skin.Lcd;
        pipelineReadout.BackColor = Skin.Lcd;
        pitchEditor.BackColor = Skin.Lcd;
        pitchEditor.ForeColor = Skin.Glow;
        lcd.Party();
        lcd.Line2Color = Skin.Accent;
        lcd.Line2 = $"HARDWARE SKIN ENGAGED: {name}";
        Invalidate(true);
    }

    void ShowSelectedLead()
    {
        var lead = playlist.SelectedLead;
        UpdatePipelineReadout();
        if (lead is null)
        {
            telemetryReadout.Text = "No post selected yet. Click [📡 FIND NEW POSTS] at the top to search Reddit, AskUbuntu, Hacker News & GitHub.";
            pitchEditor.Text = "";
            return;
        }

        lcd.LockScore = lead.Score;
        lcd.FiredStreak = sentCount;
        lcd.PulseLock();
        lcd.Line1 = $"LOCKED [{lead.Score}% MATCH] // FROM: {lead.Platform.ToUpperInvariant()} // POSTED: {lead.CreatedAt}";
        lcd.Line2Color = Skin.Glow;
        lcd.Line2 = $"{playlist.Count} POSTS READY TO ANSWER  ·  {sentCount} REPLIES SENT";

        telemetryReadout.ValueBadge = lead.Campaign switch
        {
            "local_biz" => lead.Score >= 95 ? "EST. JOB: $1,200–$3,500" : "EST. JOB: $450–$1,200",
            "dualtooth" => lead.Score >= 95 ? "HOT BUYER INTENT" : "ACTIVE PAIN POINT",
            _ => $"MATCH {lead.Score}% // HIGH INTENT"
        };
        telemetryReadout.Text =
            $"WHERE THIS CAME FROM: {lead.Platform} (by user @{lead.Author})\n" +
            $"WEB LINK: {lead.Url}\n" +
            $"QUESTION: {lead.Title}\n\n" +
            (string.IsNullOrWhiteSpace(lead.Body) ? "(Title-only post)" : lead.Body);

        pitchEditor.Text = lead.Pitch.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
    }

    static string ResolveDbPath()
    {
        var localDev = Path.Combine(ResolveInterferenceDir(), "interference.db");
        if (File.Exists(localDev)) return localDev;
        var appdata = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spanda");
        return Path.Combine(appdata, "interference.db");
    }

    static string ResolveInterferenceDir()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        if (File.Exists(Path.Combine(baseDir, "radar.py"))) return baseDir;
        var subDir = Path.Combine(baseDir, "interference");
        if (File.Exists(Path.Combine(subDir, "radar.py"))) return subDir;
        var userPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "interference");
        if (File.Exists(Path.Combine(userPath, "radar.py"))) return userPath;
        return baseDir;
    }

    static string ResolvePythonExe()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var p = Path.Combine(baseDir, "python", "python.exe");
        if (File.Exists(p)) return p;
        p = Path.Combine(baseDir, "python.exe");
        if (File.Exists(p)) return p;
        var userVenv = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "mega-zword", ".venv", "Scripts", "python.exe");
        if (File.Exists(userVenv)) return userVenv;
        
        return "python.exe";
    }

    static async Task<string> RunPythonSilentAsync(string code)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResolvePythonExe(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        psi.ArgumentList.Add("-X");
        psi.ArgumentList.Add("utf8");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(code);

        using var proc = Process.Start(psi)!;
        var output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return output.Trim();
    }

    async Task RefreshLeadsAsync()
    {
        var slug = Campaigns[campaignIdx];
        var py = $$"""
import sys, json
sys.path.insert(0, {{JsonSerializer.Serialize(ResolveInterferenceDir())}})
import radar
conn = radar.connect()
rows = [dict(r) for r in conn.execute("SELECT id, campaign, platform, title, body, url, author, created_at, score, pitch, status FROM leads WHERE campaign=? AND status='ready' ORDER BY score DESC, created_at DESC LIMIT 1000", ({{JsonSerializer.Serialize(slug)}},)).fetchall()]
sent = conn.execute("SELECT COUNT(*) FROM leads WHERE status='sent'").fetchone()[0]
print(json.dumps({"sent": sent, "rows": rows}))
""";
        try
        {
            var json = await RunPythonSilentAsync(py);
            using var doc = JsonDocument.Parse(json);
            sentCount = doc.RootElement.GetProperty("sent").GetInt32();
            var list = new List<LeadItem>();
            foreach (var el in doc.RootElement.GetProperty("rows").EnumerateArray())
            {
                list.Add(new LeadItem(
                    el.GetProperty("id").GetInt32(),
                    el.GetProperty("campaign").GetString() ?? slug,
                    el.GetProperty("platform").GetString() ?? "",
                    el.GetProperty("title").GetString() ?? "",
                    el.GetProperty("body").GetString() ?? "",
                    el.GetProperty("url").GetString() ?? "",
                    el.GetProperty("author").GetString() ?? "",
                    el.GetProperty("created_at").GetString() ?? "",
                    el.GetProperty("score").GetInt32(),
                    el.GetProperty("pitch").GetString() ?? "",
                    el.GetProperty("status").GetString() ?? "ready"
                ));
            }
            playlist.SetLeads(list);
            UpdatePipelineReadout();
            if (list.Count == 0)
            {
                lcd.Line1 = $"PRODUCT: {slug.ToUpperInvariant()} (0 POSTS CACHED)";
                lcd.Line2 = "CLICK [📡 FIND NEW POSTS] TO SEARCH LIVE QUESTIONS";
            }
        }
        catch (Exception ex)
        {
            lcd.Line2Color = Skin.Red;
            lcd.Line2 = $"DB READ ERR: {ex.Message}";
        }
    }

    async Task RunScanAsync()
    {
        var slug = Campaigns[campaignIdx];
        lcd.Party();
        lcd.Line1 = $"SEARCHING PUBLIC POSTS FOR {slug.ToUpperInvariant()}...";
        lcd.Line2Color = Skin.Accent;
        lcd.Line2 = "READING REDDIT + STACKEXCHANGE + HACKER NEWS + GITHUB -> SAVING TO leads.db";
        var py = $$"""
import sys, json
sys.path.insert(0, {{JsonSerializer.Serialize(ResolveInterferenceDir())}})
import radar
c = radar.load_campaign({{JsonSerializer.Serialize(slug)}})
added, total = radar.scan_all(c)
print(json.dumps({"added": added, "total": total}))
""";
        var outJson = await RunPythonSilentAsync(py);
        await RefreshLeadsAsync();
        lcd.Party();
        lcd.Line2Color = Skin.Glow;
        lcd.Line2 = $"SEARCH COMPLETE -> SAVED TO leads.db: {outJson}";
    }

    async Task RunRemixAsync()
    {
        var lead = playlist.SelectedLead;
        if (lead is null) return;
        var (langCode, langLabel) = Languages[langIdx];
        var tone = Tones[toneIdx];
        lcd.Party();
        lcd.Line1 = $"AI REWRITING [{tone}] IN {langLabel} ($0 COST)...";
        var py = $$"""
import sys, json
sys.path.insert(0, {{JsonSerializer.Serialize(ResolveInterferenceDir())}})
import radar
conn = radar.connect()
c = radar.load_campaign({{JsonSerializer.Serialize(lead.Campaign)}})
row = conn.execute("SELECT * FROM leads WHERE id=?", ({{lead.Id}},)).fetchone()
remixed = radar.remix_pitch_with_megazword(row, c, backend="groq-120b", target_lang={{JsonSerializer.Serialize(langCode)}}, tone={{JsonSerializer.Serialize(tone)}})
conn.execute("UPDATE leads SET pitch=? WHERE id=?", (remixed, {{lead.Id}}))
conn.commit()
print(json.dumps({"pitch": remixed}))
""";
        var res = await RunPythonSilentAsync(py);
        try
        {
            using var doc = JsonDocument.Parse(res);
            var pitch = doc.RootElement.GetProperty("pitch").GetString() ?? "";
            pitchEditor.Text = pitch.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            lcd.Line2Color = Skin.Accent;
            lcd.Line2 = "✨ AI REWRITE READY IN BOX #3 (REVIEW BEFORE SENDING)";
        }
        catch { }
    }

    async Task RunCopyJumpAsync()
    {
        var lead = playlist.SelectedLead;
        if (lead is null) return;
        var pitch = pitchEditor.Text;
        Clipboard.SetText(pitch);
        var py = $$"""
import sys
sys.path.insert(0, {{JsonSerializer.Serialize(ResolveInterferenceDir())}})
import blaster
blaster.copy_and_jump({{JsonSerializer.Serialize(lead.Url)}}, {{JsonSerializer.Serialize(pitch)}})
""";
        await RunPythonSilentAsync(py);
        lcd.Party();
        lcd.Line2Color = Skin.TearCyan;
        lcd.Line2 = $"COPIED TO CLIPBOARD & OPENED {lead.Platform} IN BROWSER!";
    }

    async Task RunFireAsync()
    {
        var lead = playlist.SelectedLead;
        if (lead is null) return;
        var pitch = pitchEditor.Text;
        var (cName, _) = cartridges[cartridgeIdx];
        lcd.Party();
        lcd.Line1 = $"SENDING REPLY TO {lead.Platform} VIA {cName}...";
        var py = $$"""
import sys, json
sys.path.insert(0, {{JsonSerializer.Serialize(ResolveInterferenceDir())}})
import blaster, radar
ok, msg = blaster.playwright_fire({{JsonSerializer.Serialize(lead.Url)}}, {{JsonSerializer.Serialize(pitch)}}, auto_submit=True)
if ok and "SUBMITTED" in msg:
    conn = radar.connect()
    conn.execute("UPDATE leads SET status='sent', pitch=? WHERE id=?", ({{JsonSerializer.Serialize(pitch)}}, {{lead.Id}}))
    conn.commit()
print(json.dumps({"ok": ok, "msg": msg}))
""";
        var res = await RunPythonSilentAsync(py);
        await RefreshLeadsAsync();
        lcd.Party();
        lcd.Line2Color = Skin.Accent;
        lcd.Line2 = $"🔥 {res}";
    }

    async Task RunBlitzWaveAsync()
    {
        var top3 = playlist.AllLeads.Take(3).ToList();
        if (top3.Count == 0) return;
        for (int i = 0; i < top3.Count; i++)
        {
            var t = top3[i];
            lcd.Party();
            lcd.Line1 = $"AUTO-SEND [{i + 1}/3] -> {t.Platform}...";
            var py = $$"""
import sys, json
sys.path.insert(0, {{JsonSerializer.Serialize(ResolveInterferenceDir())}})
import blaster, radar
conn = radar.connect()
c = radar.load_campaign({{JsonSerializer.Serialize(t.Campaign)}})
row = conn.execute("SELECT * FROM leads WHERE id=?", ({{t.Id}},)).fetchone()
remixed = radar.remix_pitch_with_megazword(row, c, backend="groq-120b")
ok, msg = blaster.playwright_fire({{JsonSerializer.Serialize(t.Url)}}, remixed, auto_submit=True)
conn.execute("UPDATE leads SET status='sent', pitch=? WHERE id=?", (remixed, {{t.Id}}))
conn.commit()
print(msg)
""";
            var msg = await RunPythonSilentAsync(py);
            lcd.Line2 = $"SENT [{i + 1}/3]: {msg}";
        }
        await RefreshLeadsAsync();
    }

    async Task MarkDoneAsync()
    {
        var lead = playlist.SelectedLead;
        if (lead is null) return;
        var py = $$"""
import sys
sys.path.insert(0, {{JsonSerializer.Serialize(ResolveInterferenceDir())}})
import radar
conn = radar.connect()
conn.execute("UPDATE leads SET status='sent' WHERE id=?", ({{lead.Id}},))
conn.commit()
""";
        await RunPythonSilentAsync(py);
        await RefreshLeadsAsync();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var outer = ClientRectangle;

        using (var body = new LinearGradientBrush(outer, ControlPaint.Light(Skin.Body, 0.16f), Skin.Body, LinearGradientMode.Vertical))
            g.FillRectangle(body, outer);
        using (var edge = new Pen(Skin.BodyLight))
            g.DrawRectangle(edge, 0, 0, outer.Width - 1, outer.Height - 1);

        DrawTitleBar(g);

        foreach (var screen in new Control[] { lcd, playlist, pipelineReadout, telemetryReadout, pitchEditor })
            Skin.Well(g, screen.Bounds, scale);

        DrawStep(g, 1, "PICK A QUESTION TO ANSWER", "Public posts found on Reddit, AskUbuntu, HN & GitHub. Click one:", 14, 166);
        DrawStep(g, 2, "WHAT THEY ASKED & WHERE IT GOES", "Shows the exact web link & question you're replying to:", 424, 166);
        DrawStep(g, 3, "YOUR REPLY MESSAGE (EDIT BEFORE SENDING)", "Type here or click [AI REWRITE] at the top. Nothing sends until you click Send:", 424, 358);
        DrawStep(g, 4, "WHERE INFO IS SAVED & ISOLATED PROFILES (PC / USB)", "Zero passwords stored! Each Profile is an isolated browser folder:", 14, 498);

        TextRenderer.DrawText(g, "spanda // sadnap v0.3  ·  hover any button for plain-english help  ·  isolated browser profiles enabled",
            Skin.Label, new Point(S(14), S(676)), Skin.GlowDim);
    }

    void DrawStep(Graphics g, int number, string title, string subtitle, int x, int y)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var circle = new RectangleF(S(x), S(y), S(16), S(16));
        using (var halo = new SolidBrush(Color.FromArgb(45, Skin.Glow)))
            g.FillEllipse(halo, RectangleF.Inflate(circle, S(2), S(2)));
        using (var ring = new Pen(Skin.Glow, Math.Max(1, 1.5f * scale)))
            g.DrawEllipse(ring, circle);
        g.SmoothingMode = SmoothingMode.None;

        TextRenderer.DrawText(g, number.ToString(), Skin.Label, Rectangle.Round(circle), Skin.Glow,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, title, Skin.Label, new Point(S(x + 22), S(y - 1)), Skin.BrightText);
        TextRenderer.DrawText(g, subtitle, Skin.LcdTiny, new Point(S(x + 22), S(y + 13)), Skin.GlowDim);
    }

    void DrawTitleBar(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float ring = S(10), y = (S(TitleBarHeight) - ring) / 2f + S(2);
        using (var c1 = new Pen(Skin.Glow, Math.Max(1.5f, 1.8f * scale)))
        using (var c2 = new Pen(Skin.Accent, Math.Max(1.5f, 1.8f * scale)))
        using (var c3 = new Pen(Skin.TearCyan, Math.Max(1.5f, 1.8f * scale)))
        {
            g.DrawEllipse(c1, S(14), y, ring, ring);
            g.DrawEllipse(c2, S(14) + ring * 0.55f, y, ring, ring);
            g.DrawEllipse(c3, S(14) + ring * 1.1f, y, ring, ring);
        }
        g.SmoothingMode = SmoothingMode.None;

        var titleX = S(14) + (int)(ring * 2.2f) + S(8);
        TextRenderer.DrawText(g, "spanda // sadnap", Skin.Title, new Point(titleX, S(4)), Skin.BrightText);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var round = 2;
        DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, 2, 0);
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
}
