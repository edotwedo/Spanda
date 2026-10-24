using System.Drawing.Drawing2D;

namespace Spanda;

public sealed record LeadItem(
    int Id,
    string Campaign,
    string Platform,
    string Title,
    string Body,
    string Url,
    string Author,
    string CreatedAt,
    int Score,
    string Pitch,
    string Status
);

public enum PandaMood
{
    SadNap,       // Droopy weeping sleepy pandas (︶ ︶) + zZz + falling cyan tears
    LaserLock,    // Visors snap on (◈ ◈) when locking >90% target or AI remixing
    Overdrive     // Bouncing hyped pandas (^ ▽ ^) + harmonic spike when firing!
}

abstract class SkinControl : Control
{
    protected SkinControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected int S(float value) => (int)Math.Round(value * Skin.Scale(this));
}

/// <summary>Tactile hardware button with LED indicator dot, bevelled chassis edge, and hover lift.</summary>
sealed class SkinButton : SkinControl
{
    bool hover, pressed;
    public bool Accent { get; set; }
    public bool LedOn { get; set; }

    public SkinButton()
    {
        Font = Skin.Label;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var radius = Math.Min(S(5), Height / 2f);

        var lift = !Enabled ? 0.05f : pressed ? 0f : hover ? 0.34f : 0.17f;
        var top = ControlPaint.Light(Skin.Body, lift);
        var bottom = pressed ? Skin.Body : Skin.BodyDark;
        using (var path = Skin.RoundRect(r, radius))
        using (var brush = new LinearGradientBrush(r, top, bottom, LinearGradientMode.Vertical))
        using (var edge = new Pen(Accent && Enabled ? Color.FromArgb(hover ? 235 : 155, Skin.Accent) : Skin.BodyLight))
        {
            g.FillPath(brush, path);
            g.DrawPath(edge, path);
        }
        if (!pressed)
            using (var shine = new Pen(Color.FromArgb(50, Color.White)))
                g.DrawLine(shine, r.Left + radius, r.Top + 1, r.Right - radius, r.Top + 1);

        if (LedOn)
        {
            using var ledBrush = new SolidBrush(Skin.Glow);
            g.FillEllipse(ledBrush, r.Left + S(6), r.Top + S(5), S(5), S(5));
        }

        var textColor = !Enabled ? Skin.BodyLight : Accent ? Skin.Accent : Skin.BrightText;
        var textRect = Rectangle.Round(r);
        if (pressed) textRect.Offset(0, 1);
        TextRenderer.DrawText(g, Text, Font, textRect, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

/// <summary>
/// Flipper-Zero x Teenage-Engineering OP-1 OLED Display:
/// Features reactive 3-state Cyber-Pandas (SadNap -> LaserLock -> Overdrive), 12-bar dB VU Signal Meter,
/// Streak/XP Rank Badge, and real-time Constructive Wave-Interference Oscilloscope.
/// </summary>
sealed class SpandaLcdDisplay : SkinControl
{
    readonly System.Windows.Forms.Timer timer = new() { Interval = 33 };
    readonly List<(float X, float Y, float Vy, int PandaIdx)> tears = [];
    readonly List<(float X, float Radius, float Alpha)> ripples = [];
    readonly Random rng = new();
    float scroll, phase;
    int burstFrames;
    int laserFrames;

    public string Marquee { get; set; } = "";
    public string Line1 { get; set; } = "";
    public string Line2 { get; set; } = "";
    public int LockScore { get; set; } = 98;
    public int FiredStreak { get; set; } = 12;
    public string ToneName { get; set; } = "HELPFUL NERD";
    public Color Line2Color { get; set; } = Skin.Glow;

    public PandaMood CurrentMood =>
        burstFrames > 0 ? PandaMood.Overdrive :
        (laserFrames > 0 || LockScore >= 95) ? PandaMood.LaserLock :
        PandaMood.SadNap;

    public SpandaLcdDisplay()
    {
        BackColor = Skin.Lcd;
        timer.Tick += (_, _) => Animate();
        timer.Start();
    }

    public void Party() => burstFrames = 95;
    public void PulseLock() => laserFrames = 55;

    void Animate()
    {
        scroll += 1.35f * Skin.Scale(this);
        phase += burstFrames > 0 ? 0.25f : 0.095f;

        if (rng.NextDouble() < (burstFrames > 0 ? 0.35 : 0.09))
        {
            var pIdx = rng.Next(3);
            tears.Add((0f, 0f, 0.85f + (float)rng.NextDouble() * 0.6f, pIdx));
        }

        for (var i = tears.Count - 1; i >= 0; i--)
        {
            var t = tears[i];
            t.Y += t.Vy;
            if (t.Y > 18f)
            {
                ripples.Add((t.PandaIdx, 2f, 1f));
                tears.RemoveAt(i);
            }
            else
            {
                tears[i] = t;
            }
        }

        for (var i = ripples.Count - 1; i >= 0; i--)
        {
            var rp = ripples[i];
            rp.Radius += 1.25f;
            rp.Alpha -= 0.05f;
            if (rp.Alpha <= 0) ripples.RemoveAt(i);
            else ripples[i] = rp;
        }

        if (burstFrames > 0) burstFrames--;
        if (laserFrames > 0) laserFrames--;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Skin.Lcd);
        int pad = S(8), rightPanelWidth = S(260), vuWidth = S(96);

        // Left side: Marquee + 2 Status lines
        var leftWidth = Width - rightPanelWidth - vuWidth - 4 * pad;
        var marqueeRect = new Rectangle(pad, pad, leftWidth, Skin.LcdLarge.Height + S(2));
        if (Marquee.Length > 0)
        {
            var textWidth = Math.Max(10, TextRenderer.MeasureText(g, Marquee, Skin.LcdLarge, Size.Empty, TextFormatFlags.NoPadding).Width);
            var x = marqueeRect.X - (int)(scroll % textWidth);
            g.SetClip(marqueeRect);
            for (var copy = x; copy < marqueeRect.Right; copy += textWidth)
            {
                TextRenderer.DrawText(g, Marquee, Skin.LcdLarge, new Point(copy + 1, marqueeRect.Y + 1), Skin.GlowShadow, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, Marquee, Skin.LcdLarge, new Point(copy, marqueeRect.Y), Skin.Glow, TextFormatFlags.NoPadding);
            }
            g.ResetClip();
        }

        var lineY = marqueeRect.Bottom + S(8);
        const TextFormatFlags lineFlags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(g, Line1, Skin.LcdSmall, new Rectangle(pad, lineY, leftWidth, Skin.LcdSmall.Height), Skin.Glow, lineFlags);
        TextRenderer.DrawText(g, Line2, Skin.LcdSmall, new Rectangle(pad, lineY + Skin.LcdSmall.Height + S(4), leftWidth, Skin.LcdSmall.Height), Line2Color, lineFlags);

        // Center Telemetry Box: 12-Bar Audio/RF VU Spectrum Meter + Dopamine Streak Rank
        var vuArea = new Rectangle(leftWidth + 2 * pad, pad, vuWidth, Height - 2 * pad);
        DrawVuAndStreak(g, vuArea);

        // Right side: Constructive Wave-Interference Oscilloscope + 3 Reactive Cyber-Pandas
        var rightArea = new Rectangle(Width - rightPanelWidth - pad, pad, rightPanelWidth, Height - 2 * pad);
        DrawWaveAndSleepyPandas(g, rightArea);
        Skin.Scanlines(g, ClientRectangle);
    }

    void DrawVuAndStreak(Graphics g, Rectangle area)
    {
        var moodLabel = CurrentMood switch
        {
            PandaMood.Overdrive => "OVRDRV!!",
            PandaMood.LaserLock => "SAD-LOCK",
            _ => "SAD-NAP",
        };
        TextRenderer.DrawText(g, $"MOOD:{moodLabel}", Skin.LcdTiny, new Point(area.X, area.Y), Skin.Accent, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, $"HIT STREAK:{FiredStreak}", Skin.LcdTiny, new Point(area.X, area.Y + S(14)), Skin.TearCyan, TextFormatFlags.NoPadding);

        // 10-bar vertical RF spectrum analyzer
        int bars = 10;
        float barW = (area.Width - S(12)) / (float)bars;
        float baseLine = area.Bottom - S(4);
        float maxH = area.Height - S(34);
        float gain = Math.Clamp(LockScore / 100f, 0.2f, 1f);

        for (int b = 0; b < bars; b++)
        {
            float wave = MathF.Abs(MathF.Sin(phase * 1.3f + b * 0.55f)) * gain;
            float h = Math.Max(S(3), wave * maxH);
            var barColor = b >= 7 ? Skin.Accent : b >= 4 ? Skin.TearCyan : Skin.Glow;
            using var br = new SolidBrush(barColor);
            g.FillRectangle(br, area.X + b * barW, baseLine - h, Math.Max(2f, barW - S(2)), h);
        }
    }

    void DrawWaveAndSleepyPandas(Graphics g, Rectangle area)
    {
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var mood = CurrentMood;

        string[] pandaRoles = ["SCAN", "SCORE", "SEND"];
        float pandaSpacing = area.Width / 3f;
        for (int i = 0; i < 3; i++)
        {
            float bounce = mood == PandaMood.Overdrive ? MathF.Abs(MathF.Sin(phase * 2f + i)) * S(4) : 0f;
            float cx = area.X + pandaSpacing * (i + 0.5f);
            float cy = area.Y + S(17) - bounce;
            float headR = S(11);

            // Role tag above panda
            TextRenderer.DrawText(g, pandaRoles[i], Skin.LcdTiny, new Point((int)(cx - S(14)), area.Y - S(2)), Skin.GlowDim, TextFormatFlags.NoPadding);

            // Ears
            using var earBrush = new SolidBrush(Skin.GlowDim);
            g.FillEllipse(earBrush, cx - headR * 0.95f, cy - headR * 0.95f, S(7), S(7));
            g.FillEllipse(earBrush, cx + headR * 0.95f - S(7), cy - headR * 0.95f, S(7), S(7));

            // Panda Head outline
            using var headFill = new SolidBrush(Skin.BodyDark);
            using var headPen = new Pen(mood == PandaMood.Overdrive ? Skin.Accent : Skin.Glow, Math.Max(1f, S(1.4f)));
            g.FillEllipse(headFill, cx - headR, cy - headR * 0.8f, headR * 2, headR * 1.65f);
            g.DrawEllipse(headPen, cx - headR, cy - headR * 0.8f, headR * 2, headR * 1.65f);

            if (mood == PandaMood.LaserLock)
            {
                // Glowing Cyber-Visor strip across eyes + sleepy tear!
                using var visorBrush = new SolidBrush(Skin.Accent);
                g.FillRectangle(visorBrush, cx - S(8), cy - S(3), S(16), S(3.5f));
            }
            else
            {
                // Sleepy droopy closed eyes (sad/sleepy curved arcs: ︶ ︶)
                using var eyePen = new Pen(Skin.Accent, Math.Max(1.2f, S(1.5f)));
                g.DrawArc(eyePen, cx - S(7), cy - S(3), S(5), S(4), 15, 150);
                g.DrawArc(eyePen, cx + S(2), cy - S(3), S(5), S(4), 15, 150);
            }

            // Floating "zZz" above sleepy pandas
            if (i == 1 && mood != PandaMood.Overdrive)
            {
                float zOffset = MathF.Sin(phase * 0.5f) * S(2);
                TextRenderer.DrawText(g, "zZz", Skin.LcdTiny, new Point((int)(cx + S(10)), (int)(cy - S(12) + zOffset)), Skin.TearCyan, TextFormatFlags.NoPadding);
            }

            // Glowing weeping cyan tear droplets falling from each panda's eyes
            using var tearBrush = new SolidBrush(Skin.TearCyan);
            foreach (var t in tears)
            {
                if (t.PandaIdx == i)
                {
                    float tx = cx - S(5) + (t.PandaIdx % 2 == 0 ? 0 : S(9));
                    float ty = cy + S(2) + S(t.Y * 0.9f);
                    g.FillEllipse(tearBrush, tx - S(1.5f), ty - S(2f), S(3f), S(4.5f));
                }
            }

            // Expanding tear ripples at the wave baseline
            foreach (var rp in ripples)
            {
                if ((int)rp.X == i)
                {
                    using var rpPen = new Pen(Color.FromArgb((int)(rp.Alpha * 190), Skin.TearCyan), 1.2f);
                    float rx = cx - S(rp.Radius);
                    float ry = area.Bottom - S(14) - S(rp.Radius * 0.28f);
                    g.DrawEllipse(rpPen, rx, ry, S(rp.Radius * 2), S(rp.Radius * 0.55f));
                }
            }
        }

        // 2. Constructive Wave-Interference Oscilloscope across the bottom half
        float waveTop = area.Y + S(38);
        float waveMid = waveTop + (area.Bottom - waveTop) / 2f;
        float amp = Math.Max(0.25f, LockScore / 100f) * S(11);
        float x0 = area.X + S(4), x1 = area.Right - S(4);

        using var wavePen1 = new Pen(Skin.Glow, Math.Max(1.2f, S(1.6f)));
        using var wavePen2 = new Pen(Color.FromArgb(150, Skin.Accent), Math.Max(1f, S(1.2f)));

        PointF prev1 = PointF.Empty, prev2 = PointF.Empty;
        for (float x = x0; x <= x1; x += 3f)
        {
            float t = (x - x0) / Math.Max(1f, x1 - x0);
            float envelope = MathF.Sin(t * MathF.PI);
            float w1 = MathF.Sin(t * MathF.PI * 5f - phase) * envelope;
            float w2 = MathF.Sin(t * MathF.PI * 5f + phase * 0.7f + (1f - LockScore / 100f) * 2f) * envelope;

            var pt1 = new PointF(x, waveMid + (w1 + w2) * 0.5f * amp);
            var pt2 = new PointF(x, waveMid + w1 * amp * 0.65f);
            if (prev1 != PointF.Empty)
            {
                g.DrawLine(wavePen2, prev2, pt2);
                g.DrawLine(wavePen1, prev1, pt1);
            }
            prev1 = pt1;
            prev2 = pt2;
        }

        g.SmoothingMode = mode;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Recessed LCD screen for target details and estimated value readouts.</summary>
sealed class LcdReadout : SkinControl
{
    public Color TextColor { get; set; } = Skin.Glow;
    public string ValueBadge { get; set; } = "";

    public LcdReadout()
    {
        BackColor = Skin.Lcd;
        Font = Skin.LcdSmall;
    }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Skin.Lcd);
        var r = Rectangle.Inflate(ClientRectangle, -S(8), -S(6));
        if (!string.IsNullOrEmpty(ValueBadge))
        {
            var badgeW = TextRenderer.MeasureText(g, ValueBadge, Skin.LcdTiny).Width + S(12);
            var badgeRect = new Rectangle(ClientRectangle.Right - badgeW - S(8), S(6), badgeW, S(16));
            using var br = new SolidBrush(Skin.Selection);
            using var pen = new Pen(Skin.Accent);
            g.FillRectangle(br, badgeRect);
            g.DrawRectangle(pen, badgeRect);
            TextRenderer.DrawText(g, ValueBadge, Skin.LcdTiny, badgeRect, Skin.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        TextRenderer.DrawText(g, Text, Font, r, TextColor, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        Skin.Scanlines(g, ClientRectangle);
    }
}

/// <summary>Hardware LED Radar Playlist: Shows live high-intent targets with glowing lock LEDs.</summary>
sealed class LeadPlaylist : SkinControl
{
    List<LeadItem> items = [];
    int current = -1, top;

    public string EmptyText { get; set; } = "Scanning radar frequencies...";
    public event EventHandler? SelectionChanged;

    public int Count => items.Count;
    public LeadItem? SelectedLead => current >= 0 && current < items.Count ? items[current] : null;
    public IReadOnlyList<LeadItem> AllLeads => items;

    public LeadPlaylist()
    {
        BackColor = Skin.Lcd;
        Font = Skin.LcdSmall;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    int RowHeight => Font.Height + S(8);
    int VisibleRows => Math.Max(1, Height / RowHeight);

    public void SetLeads(IEnumerable<LeadItem> newLeads)
    {
        items = newLeads.ToList();
        current = items.Count > 0 ? Math.Clamp(current, 0, items.Count - 1) : -1;
        if (current < 0 && items.Count > 0) current = 0;
        top = 0;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Left)
        {
            int idx = top + e.Y / RowHeight;
            if (idx >= 0 && idx < items.Count)
            {
                current = idx;
                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        top = Math.Clamp(top - Math.Sign(e.Delta) * 3, 0, Math.Max(0, items.Count - VisibleRows));
        Invalidate();
        base.OnMouseWheel(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (items.Count == 0) return;
        if (e.KeyCode is Keys.Up or Keys.Down)
        {
            current = Math.Clamp(current + (e.KeyCode == Keys.Up ? -1 : 1), 0, items.Count - 1);
            if (current < top) top = current;
            if (current >= top + VisibleRows) top = current - VisibleRows + 1;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Skin.Lcd);

        if (items.Count == 0)
        {
            TextRenderer.DrawText(g, EmptyText, Font, ClientRectangle, Skin.GlowDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            Skin.Scanlines(g, ClientRectangle);
            return;
        }

        int pad = S(8), led = S(8), rowHeight = RowHeight;
        var scrollbar = items.Count > VisibleRows ? S(5) : 0;
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;

        for (var i = top; i < Math.Min(items.Count, top + VisibleRows + 1); i++)
        {
            var item = items[i];
            var rect = new Rectangle(0, (i - top) * rowHeight, Width - scrollbar, rowHeight);
            var selected = i == current;
            if (selected)
                using (var sel = new SolidBrush(Skin.Selection)) g.FillRectangle(sel, rect);

            // Glowing Lock LED
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var ledRect = new RectangleF(pad, rect.Y + (rowHeight - led) / 2f, led, led);
            var ledColor = item.Score >= 95 ? Skin.Glow : Skin.Accent;
            using (var halo = new SolidBrush(Color.FromArgb(selected ? 90 : 45, ledColor)))
            using (var lit = new SolidBrush(ledColor))
            {
                g.FillEllipse(halo, RectangleF.Inflate(ledRect, S(3), S(3)));
                g.FillEllipse(lit, ledRect);
            }
            g.SmoothingMode = SmoothingMode.None;

            var scoreText = $"{item.Score}%";
            var scoreWidth = S(36);
            var platText = item.Platform.Length > 13 ? item.Platform[..13] : item.Platform;
            var platWidth = S(98);

            var scoreLeft = (int)ledRect.Right + S(6);
            TextRenderer.DrawText(g, scoreText, Font, new Rectangle(scoreLeft, rect.Y, scoreWidth, rowHeight), Skin.Accent, flags);
            TextRenderer.DrawText(g, platText, Font, new Rectangle(scoreLeft + scoreWidth, rect.Y, platWidth, rowHeight), Skin.TearCyan, flags);

            var titleLeft = scoreLeft + scoreWidth + platWidth + S(4);
            var titleRect = new Rectangle(titleLeft, rect.Y, rect.Right - titleLeft - pad, rowHeight);
            TextRenderer.DrawText(g, item.Title, Font, titleRect, selected ? Color.White : Skin.Glow, flags | TextFormatFlags.EndEllipsis);
        }

        if (scrollbar > 0)
        {
            var track = new Rectangle(Width - scrollbar, 0, scrollbar, Height);
            using (var trackBrush = new SolidBrush(Skin.GlowShadow)) g.FillRectangle(trackBrush, track);
            var thumbHeight = Math.Max(S(12), Height * VisibleRows / items.Count);
            var thumbY = (Height - thumbHeight) * top / Math.Max(1, items.Count - VisibleRows);
            using var thumb = new SolidBrush(Skin.GlowDim);
            g.FillRectangle(thumb, track.X + 1, thumbY, track.Width - 2, thumbHeight);
        }
        Skin.Scanlines(g, ClientRectangle);
    }
}
