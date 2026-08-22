using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using EchoSim.Sim;
using EchoSim.Shared;
using EchoSim.Sim.Analysis;
using EchoSim.Sim.Engine;
using EchoSim.Sim.Jobs;

namespace EchoSim.UI;

/// A live execution score, shown while the fight is still going.
public sealed class LiveWindow : Window
{
    private readonly Plugin plugin;

    private Execution.CeilingCurve? ceiling;
    private IJobSim? ceilingJob;
    private uint ceilingFor;

    /// How many closed downtime windows the current ceiling was built with.
    private int ceilingWindows = -1;

    /// Whether a rebuild is already in flight, so a slow simulation cannot stack up.
    private volatile bool ceilingRebuilding;

    /// The number actually on screen, which chases the real one rather than snapping to it.
    private float shown;

    /// False until the first frame of a pull, so every fight counts up from zero.
    private bool rolling;

    /// The last figure the panel had something real to show, kept for after the boss dies.
    private ExecutionScore? held;

    /// Combat state as of the previous frame, for spotting the start of a pull.
    private bool wasInCombat;

    /// Whether Settings is currently showing the panel for positioning.
    public bool Preview { get; set; }

    public LiveWindow(Plugin plugin)
        : base("EchoSim Live###EchoSimLive", ImGuiWindowFlags.NoTitleBar)
    {
        this.plugin = plugin;
    }

    /// Whether the panel is on screen at all, and the only hook called on the frames where it is NOT - which
    /// makes it the only place that can tell one fight from the next.
    public override bool DrawConditions()
    {
        var inCombat = plugin.Combat.Available && plugin.Combat.InCombat;

        if (inCombat && !wasInCombat)
        {
            rolling = false;
            held = null;
            Preview = false;
        }

        wasInCombat = inCombat;

        if (Preview)
            return true;

        if (!plugin.Configuration.ShowLiveScore || !plugin.Combat.Available)
            return false;

        return inCombat || plugin.Configuration.LiveScoreAlwaysOn;
    }

    /// The frame, the padding and the click-through, all reasserted every frame.
    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.NoTitleBar
                | ImGuiWindowFlags.AlwaysAutoResize
                | ImGuiWindowFlags.NoFocusOnAppearing
                | ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoSavedSettings;

        if (!Preview)
            Flags |= ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoMove;

        ApplySavedPosition();

        themeColors = Theme.Push();

        ImGui.PushStyleColor(ImGuiCol.WindowBg, Theme.Background with { W = BackgroundAlpha });
        ImGui.PushStyleColor(ImGuiCol.Border,
            new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, BorderAlpha));

        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, BorderThickness);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, FrameRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(FramePadX, FramePadY));
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
        Theme.Pop(themeColors);
    }

    private int themeColors;

    public override void Draw()
    {
        if (Preview)
        {
            DrawScore(PreviewScore());
            CapturePosition();
            return;
        }

        var job = ResolveJob();
        if (job is null)
        {
            ImGui.TextColored(Theme.TextDim, "No simulation for this job yet.");
            return;
        }

        var tracker = plugin.Combat;

        var at = tracker.DowntimeOpenedAt ?? tracker.Elapsed;

        var live = ceiling is null
            ? ExecutionScore.None
            : Execution.Score(job, tracker.Casts, at, ceiling);

        if (live.HasValue)
            held = live;

        if (!live.HasValue && held is null)
        {
            DrawPlaceholder();
            return;
        }

        var score = live.HasValue ? live : held!.Value;

        DrawScore(score);

        if (live.HasValue && !tracker.TargetAvailable)
            Caption(Theme.TextDisabled, "Paused");

    }

    /// The number, and by design the only thing on the panel.
    private void DrawScore(ExecutionScore score)
    {
        var target = (float)score.Displayed;

        if (!rolling)
        {
            shown = 0f;
            rolling = true;
        }

        shown = UiHelpers.Lerp(shown, target, RollSpeed, ImGui.GetIO().DeltaTime);

        if (MathF.Abs(target - shown) < SettleThreshold)
            shown = target;

        var normalised = Math.Clamp(shown / 100f, 0f, 1f);
        var intensity = normalised * normalised * normalised;

        var rounded = MathF.Round(shown, Decimals);
        var band = ParseBands.For(rounded);
        var baseColour = new Vector4(band.R / 255f, band.G / 255f, band.B / 255f, 1f);

        var time = (float)ImGui.GetTime();
        var pulse = (MathF.Sin(time * (RestingSpeed + (SpeedRange * intensity))) + 1f) * 0.5f;

        var lift = pulse * ColourLift * intensity;
        var colour = new Vector4(
            MathF.Min(1f, baseColour.X + lift),
            MathF.Min(1f, baseColour.Y + lift),
            MathF.Min(1f, baseColour.Z + lift),
            1f);

        const float fontSize = RestingSize;

        var whole = ((int)rounded).ToString();
        var fraction = $"{rounded - (int)rounded:F2}"[1..];

        var fractionSize = fontSize * FractionScale;

        Vector2 wholeSize, fractionExtent, boxSize;
        float ascent, fractionAscent, inkTop;
        var font = ImGui.GetFont();

        using (plugin.Fonts.Display.PushSafe())
        {
            font = ImGui.GetFont();

            wholeSize = ImGui.CalcTextSize(whole) * (fontSize / Fonts.DisplayBakedSize);

            fractionExtent = ImGui.CalcTextSize(WidestFraction) * (fractionSize / Fonts.DisplayBakedSize);

            var restingWhole = ImGui.CalcTextSize(WidestWhole) * (RestingSize / Fonts.DisplayBakedSize);
            var restingFraction =
                ImGui.CalcTextSize(WidestFraction) * (RestingSize * FractionScale / Fonts.DisplayBakedSize);

            var scaleToBaked = font.FontSize > 0f ? 1f / font.FontSize : 0f;

            ascent = font.Ascent * scaleToBaked * fontSize;
            fractionAscent = font.Ascent * scaleToBaked * fractionSize;

            var digit = ImGui.FindGlyph(font, '0');
            var inkHeight = restingWhole.Y;
            inkTop = 0f;

            if (!digit.IsNull && font.FontSize > 0f)
            {
                inkTop = digit.Y0 * scaleToBaked * fontSize;
                inkHeight = (digit.Y1 - digit.Y0) * scaleToBaked * fontSize;
            }

            boxSize = new Vector2(restingWhole.X + restingFraction.X, inkHeight);
        }

        var drawnWidth = wholeSize.X + fractionExtent.X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();

        var pos = new Vector2(
            origin.X + ((boxSize.X - drawnWidth) * 0.5f),
            origin.Y - inkTop);

        var fractionPos = new Vector2(pos.X + wholeSize.X, pos.Y + ascent - fractionAscent);

        void Paint(Vector2 offset, uint tint)
        {
            draw.AddText(font, fontSize, pos + offset, tint, whole);
            draw.AddText(font, fractionSize, fractionPos + offset, tint, fraction);
        }

        if (intensity > 0.01f)
        {
            var radius = GlowRadius * intensity;
            var alpha = (GlowFloor + (GlowSwell * pulse)) * intensity;

            var outer = ImGui.ColorConvertFloat4ToU32(colour with { W = alpha * 0.5f });
            var inner = ImGui.ColorConvertFloat4ToU32(colour with { W = alpha });

            foreach (var offset in GlowRing)
                Paint(offset * radius * 2f, outer);

            foreach (var offset in GlowRing)
                Paint(offset * radius, inner);
        }

        Paint(Vector2.Zero, ImGui.ColorConvertFloat4ToU32(colour));

        ImGui.Dummy(boxSize);
    }

    /// A dash where the number will be, holding the panel's exact size and position.
    private void DrawPlaceholder()
    {
        rolling = false;

        Vector2 boxSize;
        float inkTop;
        var font = ImGui.GetFont();

        using (plugin.Fonts.Display.PushSafe())
        {
            font = ImGui.GetFont();

            var restingWhole = ImGui.CalcTextSize(WidestWhole) * (RestingSize / Fonts.DisplayBakedSize);
            var restingFraction =
                ImGui.CalcTextSize(WidestFraction) * (RestingSize * FractionScale / Fonts.DisplayBakedSize);

            var scaleToBaked = font.FontSize > 0f ? 1f / font.FontSize : 0f;
            var digit = ImGui.FindGlyph(font, '0');

            var inkHeight = restingWhole.Y;
            inkTop = 0f;

            if (!digit.IsNull && font.FontSize > 0f)
            {
                inkTop = digit.Y0 * scaleToBaked * RestingSize;
                inkHeight = (digit.Y1 - digit.Y0) * scaleToBaked * RestingSize;
            }

            boxSize = new Vector2(restingWhole.X + restingFraction.X, inkHeight);
        }

        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();

        float dashWidth;
        using (plugin.Fonts.Display.PushSafe())
            dashWidth = ImGui.CalcTextSize("-").X * (RestingSize / Fonts.DisplayBakedSize);

        draw.AddText(
            font,
            RestingSize,
            new Vector2(origin.X + ((boxSize.X - dashWidth) * 0.5f), origin.Y - inkTop),
            ImGui.ColorConvertFloat4ToU32(Theme.TextDisabled),
            "-");

        ImGui.Dummy(boxSize);
    }

    /// A caveat line, centred under the number and deliberately small.
    private static void Caption(Vector4 colour, string text)
    {
        UiHelpers.CenterCursorX(ImGui.CalcTextSize(text).X);
        ImGui.TextColored(colour, text);
    }

    /// A stand-in figure while the panel is being placed, sweeping rather than sitting still.
    private static ExecutionScore PreviewScore()
    {
        var cycle = (float)ImGui.GetTime() * PreviewSweepSpeed % 2f;
        var phase = cycle < 1f ? cycle : 2f - cycle;

        return new ExecutionScore(PreviewFloor + ((100f - PreviewFloor) * phase), 0, 0);
    }

    /// Pins the panel where it was left, except while it is being placed.
    private void ApplySavedPosition()
    {
        var x = plugin.Configuration.LiveScoreX;
        var y = plugin.Configuration.LiveScoreY;

        if (Preview || x <= Configuration.UnsetPosition || y <= Configuration.UnsetPosition)
        {
            Position = null;
            return;
        }

        Position = new Vector2(x, y);
        PositionCondition = ImGuiCond.Always;
    }

    /// Records where the panel was dragged to, once the drag has finished.
    private void CapturePosition()
    {
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            return;

        var pos = ImGui.GetWindowPos();
        var config = plugin.Configuration;

        if (MathF.Abs(pos.X - config.LiveScoreX) < 0.5f && MathF.Abs(pos.Y - config.LiveScoreY) < 0.5f)
            return;

        config.LiveScoreX = pos.X;
        config.LiveScoreY = pos.Y;
        plugin.SaveConfig();
    }

    /// The number's size, fixed.
    private const float RestingSize = 44f;

    /// Widest whole and fractional parts the score can produce - what the box is measured from.
    private const string WidestWhole = "100";
    private const string WidestFraction = ".00";

    /// Decimal places shown.
    private const int Decimals = 2;

    /// The fraction's size relative to the whole part.
    private const float FractionScale = 0.52f;

    /// How close the roll has to get before it is snapped onto the real figure.
    private const float SettleThreshold = 0.005f;

    /// How fast the displayed figure chases the real one.
    private const float RollSpeed = 7f;

    private const float BackgroundAlpha = 0.88f;
    private const float BorderAlpha = 0.75f;
    private const float BorderThickness = 2f;
    private const float FrameRounding = 10f;

    private const float FramePadX = 15f;
    private const float FramePadY = 11f;

    /// Where the placement sweep starts, and how fast it runs the band.
    private const float PreviewFloor = 35f;
    private const float PreviewSweepSpeed = 0.22f;


    /// Pulse rate in radians a second at zero and at a hundred - a slow breath either way.
    private const float RestingSpeed = 1.1f;
    private const float SpeedRange = 1.3f;

    /// How much the colour brightens at the top of a full-intensity breath.
    private const float ColourLift = 0.16f;

    /// Bloom radius in pixels at full intensity, and its alpha at the trough and the peak.
    private const float GlowRadius = 2.5f;
    private const float GlowFloor = 0.05f;
    private const float GlowSwell = 0.07f;

    /// Eight directions, so the bloom is a halo rather than a cross.
    private static readonly Vector2[] GlowRing = BuildGlowRing();

    private static Vector2[] BuildGlowRing()
    {
        var ring = new Vector2[8];
        for (var i = 0; i < ring.Length; i++)
        {
            var angle = MathF.Tau * i / ring.Length;
            ring[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        return ring;
    }

    /// Gives the ceiling the same gaps the fight has had, once each one closes.
    private void RebuildForDowntime(uint classJob)
    {
        var windows = plugin.Combat.Downtime;

        if (ceilingRebuilding || windows.Count == ceilingWindows)
            return;

        var definition = JobRegistry.For(classJob);
        if (definition is null)
            return;

        ceilingRebuilding = true;
        var wanted = windows.Count;

        _ = Task.Run(() =>
        {
            try
            {
                var job = definition.CreateSim();
                var stats = definition.ReferenceGear().ToPlayerStats(
                    job, partyBonus: false, food: FoodDef.None, potion: PotionDef.Grade4);

                var reference = new Simulator().Run(
                    job, definition.CreateRotation(PotionPlan.ZeroFiveTen.Times), stats,
                    Execution.ReferenceDuration, downtime: windows);

                ceiling = Execution.CeilingCurve.From(job, reference);
                ceilingWindows = wanted;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "EchoSim: could not rebuild the live ceiling for downtime");
            }
            finally
            {
                ceilingRebuilding = false;
            }
        });
    }

    /// The job actually being played, with its ceiling simulated once and kept.
    private IJobSim? ResolveJob()
    {
        var classJob = Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0;
        if (classJob == 0)
            return null;

        if (ceilingJob is not null && ceilingFor == classJob)
        {
            RebuildForDowntime(classJob);
            return ceilingJob;
        }

        var definition = JobRegistry.For(classJob);
        if (definition is null)
        {
            ceilingJob = null;
            ceilingFor = classJob;
            return null;
        }

        StartCeilingBuild(definition, classJob);

        return null;
    }

    /// Builds a job's ceiling in the background, once.
    private void StartCeilingBuild(JobDefinition definition, uint classJob)
    {
        if (ceilingBuilding == classJob)
            return;

        ceilingBuilding = classJob;

        _ = Task.Run(() =>
        {
            try
            {
                var job = definition.CreateSim();
                var stats = definition.ReferenceGear().ToPlayerStats(
                    job, partyBonus: false, food: FoodDef.None, potion: PotionDef.Grade4);

                var reference = new Simulator().Run(
                    job, definition.CreateRotation(PotionPlan.ZeroFiveTen.Times), stats,
                    Execution.ReferenceDuration);

                var curve = Execution.CeilingCurve.From(job, reference);

                if (ceilingBuilding != classJob)
                    return;

                ceiling = curve;
                ceilingJob = job;
                ceilingFor = classJob;
                ceilingWindows = 0;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "EchoSim: could not build the live ceiling");
            }
            finally
            {
                if (ceilingBuilding == classJob)
                    ceilingBuilding = 0;
            }
        });
    }

    /// Which job has a build in flight, so the next frame does not start another.
    private uint ceilingBuilding;
}
