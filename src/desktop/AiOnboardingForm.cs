using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Spanda;

sealed class AiOnboardingForm : Form
{
    const int TitleBarHeight = 28;

    record ProviderInfo(string Id, string Name, string Url, string KeyPage, string KeyPrefix, string[] Steps, bool OneClick = false);

    static readonly ProviderInfo[] Providers =
    [
        new(
            "openrouter", "OpenRouter (1-Click PKCE)", "https://openrouter.ai/api/v1/chat/completions",
            "https://openrouter.ai/keys", "sk-or-",
            [
                "1. Click [🔑 1-CLICK SIGN IN] below.",
                "2. Your browser opens to OpenRouter. Sign in with Google, GitHub or email.",
                "3. Click Authorize to grant access.",
                "4. Spanda automatically receives and saves your free key with Windows DPAPI!"
            ],
            true
        ),
        new(
            "groq", "Groq (Fastest 70B)", "https://api.groq.com/openai/v1/chat/completions",
            "https://console.groq.com/keys", "gsk_",
            [
                "1. Click [GET FREE KEY] to open Groq Console.",
                "2. Sign in with Google or GitHub (100% free).",
                "3. Click 'Create API Key', name it 'Spanda', copy the 'gsk_...' key.",
                "4. Paste it below and click [TEST & SAVE KEY]."
            ]
        ),
        new(
            "gemini", "Google Gemini (Flash)", "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
            "https://aistudio.google.com/apikey", "AIza",
            [
                "1. Click [GET FREE KEY] to open Google AI Studio.",
                "2. Sign in with your Google account.",
                "3. Click 'Create API key' in Google AI Studio.",
                "4. Copy the key starting with 'AIza...', paste below and save."
            ]
        ),
        new(
            "cerebras", "Cerebras (Ultra-Fast)", "https://api.cerebras.ai/v1/chat/completions",
            "https://cloud.cerebras.ai/platform", "csk-",
            [
                "1. Click [GET FREE KEY] to open Cerebras Cloud.",
                "2. Sign in, go to API Keys, generate a free key.",
                "3. Copy the 'csk_...' key, paste below and save."
            ]
        )
    ];

    int selectedProviderIdx;
    int keylessRemaining = 40;
    readonly List<string> localModels = [];
    readonly List<(string Provider, string MaskedKey)> activeKeys = [];

    readonly SkinButton closeBtn = new() { Text = "×" };
    readonly SkinButton returnBtn = new() { Text = "✔ RETURN TO RADAR", Accent = true, Font = Skin.ButtonLarge };

    // Tier 1 Readout
    readonly LcdReadout tier1Readout = new();

    // Tier 2 Controls
    readonly SkinButton[] providerTabs;
    readonly LcdReadout stepsReadout = new();
    readonly SkinButton getKeyBtn = new() { Text = "🔑 1-CLICK SIGN IN", Accent = true };
    readonly TextBox keyInput = new();
    readonly SkinButton testSaveBtn = new() { Text = "⚡ TEST & SAVE KEY", Accent = true };
    readonly LcdReadout statusReadout = new();
    readonly SkinButton removeKeyBtn = new() { Text = "🗑️ REMOVE KEY" };

    // Tier 3 Readout
    readonly LcdReadout tier3Readout = new();

    float scale = 1f;
    int S(float v) => (int)Math.Round(v * scale);

    public AiOnboardingForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        Width = 720;
        Height = 650;
        BackColor = Skin.Body;
        DoubleBuffered = true;

        providerTabs = new SkinButton[Providers.Length];
        for (int i = 0; i < Providers.Length; i++)
        {
            int idx = i;
            providerTabs[i] = new SkinButton
            {
                Text = Providers[i].Name,
                LedOn = (i == 0),
                Accent = (i == 0)
            };
            providerTabs[i].Click += (_, _) => SelectProvider(idx);
        }

        keyInput.BackColor = Skin.Lcd;
        keyInput.ForeColor = Skin.BrightText;
        keyInput.BorderStyle = BorderStyle.FixedSingle;
        keyInput.Font = Skin.LcdSmall;
        keyInput.UseSystemPasswordChar = true;

        LayoutControls();

        closeBtn.Click += (_, _) => Close();
        returnBtn.Click += (_, _) => Close();
        getKeyBtn.Click += async (_, _) => await HandleGetKeyClickAsync();
        testSaveBtn.Click += async (_, _) => await TestAndSaveKeyAsync();
        removeKeyBtn.Click += async (_, _) => await RemoveCurrentKeyAsync();

        Load += async (_, _) => await RefreshStateAsync();
    }

    void LayoutControls()
    {
        Controls.Clear();
        scale = Skin.Scale(this);

        // Title bar
        Place(closeBtn, Width - 38, 2, 34, 22);

        // Tier 1 Readout (Y: 34, H: 65)
        Place(tier1Readout, 16, 56, Width - 32, 54);

        // Tier 2: Provider tabs (Y: 148)
        int tabW = (Width - 32 - (Providers.Length - 1) * 6) / Providers.Length;
        for (int i = 0; i < Providers.Length; i++)
            Place(providerTabs[i], 16 + i * (tabW + 6), 142, tabW, 28);

        // Tier 2: Steps readout & Action row (Y: 176)
        Place(stepsReadout, 16, 176, Width - 32, 82);
        Place(getKeyBtn, 16, 264, 200, 30);
        Place(keyInput, 224, 266, 310, 26);
        Place(testSaveBtn, 542, 264, 162, 30);

        // Tier 2: Verification status readout & Remove button
        Place(statusReadout, 16, 300, Width - 170, 48);
        Place(removeKeyBtn, Width - 146, 308, 130, 32);

        // Tier 3: Local Ollama Readout (Y: 386)
        Place(tier3Readout, 16, 384, Width - 32, 60);

        // Bottom action button
        Place(returnBtn, (Width - 260) / 2, Height - 52, 260, 38);
    }

    void Place(Control c, int x, int y, int w, int h)
    {
        c.Bounds = new Rectangle(S(x), S(y), S(w), S(h));
        Controls.Add(c);
    }

    void SelectProvider(int idx)
    {
        selectedProviderIdx = idx;
        for (int i = 0; i < providerTabs.Length; i++)
        {
            providerTabs[i].LedOn = (i == idx);
            providerTabs[i].Accent = (i == idx);
            providerTabs[i].Invalidate();
        }

        var p = Providers[idx];
        if (p.OneClick)
        {
            getKeyBtn.Text = "🔑 1-CLICK SIGN IN";
            getKeyBtn.Accent = true;
            getKeyBtn.Width = S(240);
            keyInput.Visible = false;
            testSaveBtn.Visible = false;
        }
        else
        {
            getKeyBtn.Text = "🌐 1. GET FREE KEY";
            getKeyBtn.Accent = false;
            getKeyBtn.Width = S(150);
            keyInput.Visible = true;
            testSaveBtn.Visible = true;
            keyInput.Clear();
            keyInput.PlaceholderText = $"Paste {p.Name} key here ({p.KeyPrefix}...)";
        }

        stepsReadout.Text = string.Join("\n", p.Steps);
        UpdateStatusReadout();
    }

    async Task HandleGetKeyClickAsync()
    {
        var p = Providers[selectedProviderIdx];
        if (p.OneClick && p.Id == "openrouter")
        {
            await RunOpenRouterSignInAsync();
        }
        else
        {
            try
            {
                Process.Start(new ProcessStartInfo(p.KeyPage) { UseShellExecute = true });
            }
            catch { }
        }
    }

    async Task RunOpenRouterSignInAsync()
    {
        getKeyBtn.Enabled = false;
        getKeyBtn.Text = "⏳ WAITING FOR BROWSER...";
        statusReadout.ValueBadge = "SIGN-IN PENDING";
        statusReadout.TextColor = Skin.Accent;
        statusReadout.Text = "Opened browser to OpenRouter. Sign in and click Authorize — Spanda will capture your key automatically.";

        string py = "import sys, json\n" +
            "sys.path.insert(0, " + JsonSerializer.Serialize(SpandaForm_ResolveInterferenceDir()) + ")\n" +
            "import ai_cascade\n" +
            "ok, msg = ai_cascade.openrouter_sign_in()\n" +
            "print(json.dumps({\"ok\": ok, \"msg\": msg}))\n";

        try
        {
            var res = await SpandaForm_RunPythonAsync(py);
            using var doc = JsonDocument.Parse(res);
            var ok = doc.RootElement.GetProperty("ok").GetBoolean();
            var msg = doc.RootElement.GetProperty("msg").GetString() ?? "";

            if (ok)
            {
                statusReadout.ValueBadge = "VERIFIED & SAVED";
                statusReadout.TextColor = Skin.Glow;
                statusReadout.Text = $"✅ {msg}\nSpanda now uses your own OpenRouter key - no more daily Spanda limit.";
                await RefreshStateAsync();
            }
            else
            {
                statusReadout.ValueBadge = "SIGN-IN CANCELLED";
                statusReadout.TextColor = Skin.Red;
                statusReadout.Text = $"❌ OpenRouter sign-in failed: {msg}";
            }
        }
        catch (Exception ex)
        {
            statusReadout.ValueBadge = "ERROR";
            statusReadout.TextColor = Skin.Red;
            statusReadout.Text = $"Sign-in error: {ex.Message}";
        }
        finally
        {
            getKeyBtn.Enabled = true;
            getKeyBtn.Text = "🔑 1-CLICK SIGN IN";
        }
    }

    async Task RefreshStateAsync()
    {
        string py = "import sys, json\n" +
            "sys.path.insert(0, " + JsonSerializer.Serialize(SpandaForm_ResolveInterferenceDir()) + ")\n" +
            "import ai_cascade\n" +
            "rem = ai_cascade.keyless_remaining()\n" +
            "models = ai_cascade.ollama_models()\n" +
            "keys = ai_cascade.saved_keys()\n" +
            "masked = []\n" +
            "for prov, k in keys:\n" +
            "    visible = k[:4] + '...' + k[-4:] if len(k) > 10 else '***'\n" +
            "    masked.append({'provider': prov, 'masked': visible})\n" +
            "print(json.dumps({'keyless': rem, 'ollama': models, 'keys': masked}))\n";

        try
        {
            var res = await SpandaForm_RunPythonAsync(py);
            using var doc = JsonDocument.Parse(res);
            keylessRemaining = doc.RootElement.GetProperty("keyless").GetInt32();

            localModels.Clear();
            foreach (var m in doc.RootElement.GetProperty("ollama").EnumerateArray())
                localModels.Add(m.GetString() ?? "");

            activeKeys.Clear();
            foreach (var k in doc.RootElement.GetProperty("keys").EnumerateArray())
            {
                var prov = k.GetProperty("provider").GetString() ?? "";
                var m = k.GetProperty("masked").GetString() ?? "";
                activeKeys.Add((prov, m));
            }
        }
        catch
        {
            keylessRemaining = 40;
        }

        UpdateTier1Readout();
        UpdateTier3Readout();
        SelectProvider(selectedProviderIdx);
    }

    void UpdateTier1Readout()
    {
        tier1Readout.ValueBadge = $"{keylessRemaining}/40 LEFT TODAY";
        tier1Readout.TextColor = keylessRemaining > 10 ? Skin.Glow : Skin.Red;
        tier1Readout.Text =
            "PROVIDER: OVHcloud Kepler (Qwen 2.5 72B) + Kilo Gateway (Nemotron 120B / Cohere Code)\n" +
            "SECURITY: 100% direct client-to-endpoint calls from your IP (never through a shared proxy)\n" +
            "CAPACITY: Shared keyless pool resets daily. Add a free API key below for unlimited AI.";
    }

    void UpdateTier3Readout()
    {
        if (localModels.Count > 0)
        {
            tier3Readout.ValueBadge = "AIR-GAPPED ONLINE";
            tier3Readout.TextColor = Skin.TearCyan;
            tier3Readout.Text =
                $"STATUS:  Local Ollama running at http://127.0.0.1:11434 (100% offline, zero internet needed)\n" +
                $"MODELS:  {string.Join(", ", localModels)}\n" +
                $"CASCADE: Spanda automatically routes to local Ollama if network endpoints are blocked.";
        }
        else
        {
            tier3Readout.ValueBadge = "OPTIONAL / OFFLINE";
            tier3Readout.TextColor = Skin.GlowDim;
            tier3Readout.Text =
                "STATUS:  No local Ollama instance detected at http://127.0.0.1:11434 (Not required for Spanda).\n" +
                "UPGRADE: Have a gaming GPU? Install Ollama (https://ollama.com) to run 100% offline air-gapped AI.\n" +
                "NOTE:    Spanda works completely fine using Tier 1 (Keyless) or Tier 2 (Free Keys).";
        }
    }

    void UpdateStatusReadout()
    {
        var p = Providers[selectedProviderIdx];
        var found = activeKeys.Find(k => string.Equals(k.Provider, p.Id, StringComparison.OrdinalIgnoreCase));
        if (found != default)
        {
            statusReadout.ValueBadge = "ACTIVE & SAVED";
            statusReadout.TextColor = Skin.Glow;
            statusReadout.Text = $"ACTIVE KEY: {found.MaskedKey} (Stored with Windows DPAPI encryption)\nUnlimited rewrites unlocked via {p.Name}.";
            removeKeyBtn.Enabled = true;
        }
        else
        {
            statusReadout.ValueBadge = "NO KEY STORED";
            statusReadout.TextColor = Skin.GlowDim;
            statusReadout.Text = p.OneClick
                ? "No OpenRouter key configured. Click [🔑 1-CLICK SIGN IN] above to authorize in 10 seconds."
                : $"No {p.Name} key currently configured. Follow the steps above to get a free key.";
            removeKeyBtn.Enabled = false;
        }
    }

    async Task TestAndSaveKeyAsync()
    {
        var p = Providers[selectedProviderIdx];
        var key = keyInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            statusReadout.ValueBadge = "INPUT REQUIRED";
            statusReadout.TextColor = Skin.Red;
            statusReadout.Text = $"Please paste your {p.Name} API key into the box first.";
            return;
        }

        testSaveBtn.Enabled = false;
        testSaveBtn.Text = "⏳ VERIFYING KEY...";
        statusReadout.ValueBadge = "TESTING...";
        statusReadout.TextColor = Skin.Accent;
        statusReadout.Text = $"Connecting to {p.Name} API endpoint to verify key...";

        string py = "import sys, json\n" +
            "sys.path.insert(0, " + JsonSerializer.Serialize(SpandaForm_ResolveInterferenceDir()) + ")\n" +
            "import ai_cascade\n" +
            "ok, err = ai_cascade.test_key(" + JsonSerializer.Serialize(p.Id) + ", " + JsonSerializer.Serialize(key) + ")\n" +
            "if ok:\n" +
            "    ai_cascade.add_key(" + JsonSerializer.Serialize(p.Id) + ", " + JsonSerializer.Serialize(key) + ")\n" +
            "print(json.dumps({\"ok\": ok, \"err\": err}))\n";

        try
        {
            var res = await SpandaForm_RunPythonAsync(py);
            using var doc = JsonDocument.Parse(res);
            var ok = doc.RootElement.GetProperty("ok").GetBoolean();
            var err = doc.RootElement.GetProperty("err").GetString() ?? "";

            if (ok)
            {
                keyInput.Clear();
                statusReadout.ValueBadge = "VERIFIED & SAVED";
                statusReadout.TextColor = Skin.Glow;
                statusReadout.Text = $"✅ {p.Name} key verified and saved securely via Windows DPAPI!\nUnlimited AI rewrites are now active.";
                await RefreshStateAsync();
            }
            else
            {
                statusReadout.ValueBadge = "VERIFICATION FAILED";
                statusReadout.TextColor = Skin.Red;
                statusReadout.Text = $"❌ {p.Name} rejected this key: {err}\nPlease verify the key on your {p.Name} console.";
            }
        }
        catch (Exception ex)
        {
            statusReadout.ValueBadge = "TEST ERROR";
            statusReadout.TextColor = Skin.Red;
            statusReadout.Text = $"Error testing key: {ex.Message}";
        }
        finally
        {
            testSaveBtn.Enabled = true;
            testSaveBtn.Text = "⚡ TEST & SAVE KEY";
        }
    }

    async Task RemoveCurrentKeyAsync()
    {
        var p = Providers[selectedProviderIdx];
        string py = "import sys\n" +
            "sys.path.insert(0, " + JsonSerializer.Serialize(SpandaForm_ResolveInterferenceDir()) + ")\n" +
            "import ai_cascade\n" +
            "ai_cascade.remove_keys(" + JsonSerializer.Serialize(p.Id) + ")\n" +
            "print('OK')\n";

        await SpandaForm_RunPythonAsync(py);
        await RefreshStateAsync();
    }

    static string SpandaForm_ResolveInterferenceDir()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        if (File.Exists(Path.Combine(baseDir, "radar.py"))) return baseDir;
        var subDir = Path.Combine(baseDir, "interference");
        if (File.Exists(Path.Combine(subDir, "radar.py"))) return subDir;
        var userPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "interference");
        if (File.Exists(Path.Combine(userPath, "radar.py"))) return userPath;
        return baseDir;
    }

    static string SpandaForm_ResolvePythonExe()
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

    static async Task<string> SpandaForm_RunPythonAsync(string code)
    {
        var psi = new ProcessStartInfo
        {
            FileName = SpandaForm_ResolvePythonExe(),
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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var outer = ClientRectangle;

        using (var body = new LinearGradientBrush(outer, ControlPaint.Light(Skin.Body, 0.14f), Skin.Body, LinearGradientMode.Vertical))
            g.FillRectangle(body, outer);
        using (var edge = new Pen(Skin.BodyLight))
            g.DrawRectangle(edge, 0, 0, outer.Width - 1, outer.Height - 1);

        // Title bar
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var c1 = new Pen(Skin.Glow, 2f))
            g.DrawEllipse(c1, S(14), S(8), S(12), S(12));
        g.SmoothingMode = SmoothingMode.None;

        TextRenderer.DrawText(g, "SPANDA // AI CASCADE SETUP · FREE REWRITE ENGINES", Skin.Title, new Point(S(32), S(6)), Skin.BrightText);

        // Section Headers
        DrawSectionHeader(g, "TIER 1: SHARED KEYLESS AI ($0 COST · ZERO SETUP)", 16, 36);
        DrawSectionHeader(g, "TIER 2: YOUR FREE API KEY (UNLIMITED AI · DPAPI ENCRYPTED)", 16, 122);
        DrawSectionHeader(g, "TIER 3: LOCAL AIR-GAPPED OLLAMA (OPTIONAL · 100% OFFLINE)", 16, 364);
        DrawSectionHeader(g, "TIER 4: CAMPAIGN TEMPLATE (SAFETY NET FALLBACK)", 16, 458);

        // Tier 4 Note
        TextRenderer.DrawText(g,
            "Always active: If all network endpoints and local models are offline, Spanda automatically drops back to your\n" +
            "campaign template so you can keep answering posts seamlessly without interruptions.",
            Skin.LcdTiny, new Point(S(18), S(480)), Skin.GlowDim);
    }

    void DrawSectionHeader(Graphics g, string title, int x, int y)
    {
        TextRenderer.DrawText(g, title, Skin.Label, new Point(S(x), S(y)), Skin.Accent);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || e.Y > S(TitleBarHeight)) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, 2, 0);
    }

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
}
