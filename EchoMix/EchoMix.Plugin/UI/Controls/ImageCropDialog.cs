using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Design;

namespace EchoMix.Plugin.UI.Controls;

/// Drag to reposition, scroll to zoom, turn to straighten - the crop step every image upload goes through (DJ
/// avatar, DJ banner, Broadcast show image).
public sealed class ImageCropDialog : IDisposable
{
    private const string PopupId = "##imageCropDialog";

    /// The largest the preview may get, in design units.
    private const float MaximumViewportWidth = 420f;
    private const float MaximumViewportHeight = 400f;

    private const float MinimumZoom = 1f;
    private const float MaximumZoom = 4f;

    /// Zoom per wheel notch.
    private const float ZoomPerTick = 1.1f;

    /// How far either way the angle may be taken.
    private const float AngleLimit = 180f;

    /// Inside this many degrees of a quarter turn, the slider sits on the quarter turn.
    private const float SnapDegrees = 2.5f;

    private bool openRequested;
    private string sourcePath = string.Empty;
    private int targetWidth;
    private int targetHeight;
    private int sourceWidth;
    private int sourceHeight;
    private Action<byte[]>? onConfirmed;

    private IDalamudTextureWrap? sourceTexture;
    private float panX = 0.5f;
    private float panY = 0.5f;
    private float zoom = MinimumZoom;

    /// How far the picture is turned clockwise, in degrees.
    private float angle;

    private string? error;

    public bool IsOpen { get; private set; }

    /// Whether what is being cropped will be displayed as a circle.
    private bool IsCircularTarget => targetWidth == targetHeight;

    /// Loads the source image for preview and arms the popup to open on the next Draw.
    public async Task OpenAsync(string sourcePathToOpen, int newTargetWidth, int newTargetHeight, Action<byte[]> confirmedCallback)
    {
        sourcePath = sourcePathToOpen;
        targetWidth = newTargetWidth;
        targetHeight = newTargetHeight;
        onConfirmed = confirmedCallback;
        panX = 0.5f;
        panY = 0.5f;
        zoom = MinimumZoom;
        angle = 0f;
        error = null;

        try
        {
            if (ShowImageProcessor.Measure(sourcePath) is not { } size)
                throw new InvalidOperationException("that file isn't an image");

            sourceWidth = size.Width;
            sourceHeight = size.Height;

            var bytes = await File.ReadAllBytesAsync(sourcePath);
            var wrap = await Plugin.TextureProvider.CreateFromImageAsync(bytes);
            sourceTexture?.Dispose();
            sourceTexture = wrap;
        }
        catch (Exception ex)
        {
            error = $"Couldn't load that image: {ex.Message}";
        }

        openRequested = true;
    }

    /// Must be called every frame regardless of IsOpen.
    public void Draw(float scale)
    {
        if (openRequested)
        {
            ImGui.OpenPopup(PopupId);
            openRequested = false;
        }

        var windowCentre = ImGui.GetWindowPos() + (ImGui.GetWindowSize() * 0.5f);
        ImGui.SetNextWindowPos(windowCentre, ImGuiCond.Always, new Vector2(0.5f, 0.5f));

        if (!ImGui.BeginPopup(PopupId))
        {
            IsOpen = false;
            return;
        }

        IsOpen = true;

        var previousScale = Metrics.Scale;
        Metrics.Scale = scale * UiHelpers.GlobalFontScale;

        try
        {
            DrawBody(scale);
        }
        finally
        {
            Metrics.Scale = previousScale;
            ImGui.EndPopup();
        }
    }

    private void DrawBody(float scale)
    {
        using var detached = Surfaces.Detach();

        var viewport = Viewport(scale);

        Surfaces.SectionHeader("Position Your Image", width: viewport.X);
        Surfaces.Gap(Metrics.Sm);

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, "Drag to reposition, scroll to zoom.");

        Surfaces.Gap(Metrics.Md);

        if (error is { } message)
        {
            using (TypeScale.Body())
                ImGui.TextColored(Semantic.Danger, message);
        }
        else if (sourceTexture != null)
        {
            DrawCanvas(scale, viewport);
            DrawAngle(viewport.X);
        }

        Surfaces.Gap(Metrics.Lg);

        var height = MathF.Round(Metrics.ControlMd);
        var modified = panX != 0.5f || panY != 0.5f || zoom != MinimumZoom || angle != 0f;

        if (Fields.Button("Cancel", Fields.ButtonStyle.Secondary, height: height, pill: true))
        {
            Close();
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine(0f, Metrics.Md);

        if (Fields.Button("Reset", Fields.ButtonStyle.Secondary, enabled: modified, height: height, pill: true))
        {
            panX = 0.5f;
            panY = 0.5f;
            zoom = MinimumZoom;
            angle = 0f;
        }

        if (ImGui.IsItemHovered())
            Tip.Hovered("Reset", "Back to the whole picture, upright and centred.");

        ImGui.SameLine(0f, Metrics.Md);

        if (Fields.Button("Use This", Fields.ButtonStyle.Primary,
                enabled: sourceTexture != null && error is null, height: height, pill: true))
        {
            try
            {
                onConfirmed?.Invoke(ShowImageProcessor.ProcessToJpeg(
                    sourcePath, targetWidth, targetHeight, panX, panY, zoom, angle));

                Close();
                ImGui.CloseCurrentPopup();
            }
            catch (Exception ex)
            {
                error = $"Couldn't process that image: {ex.Message}";
            }
        }
    }

    /// How big the preview comes out, which the heading's rule also needs.
    private Vector2 Viewport(float scale)
    {
        var targetAspect = targetWidth / (float)MathF.Max(1, targetHeight);

        var width = MaximumViewportWidth * scale;
        var height = width / MathF.Max(0.01f, targetAspect);

        if (height > MaximumViewportHeight * scale)
        {
            height = MaximumViewportHeight * scale;
            width = height * targetAspect;
        }

        return new Vector2(width, height);
    }

    private void DrawCanvas(float scale, Vector2 size)
    {
        var radians = angle * MathF.PI / 180f;
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);

        var footprintWidth = (size.X * MathF.Abs(cos)) + (size.Y * MathF.Abs(sin));
        var footprintHeight = (size.X * MathF.Abs(sin)) + (size.Y * MathF.Abs(cos));

        var coverScale = MathF.Max(footprintWidth / sourceWidth, footprintHeight / sourceHeight)
                         * ShowImageProcessor.CoverFactor(angle);

        var effective = coverScale * zoom;

        var scaledWidth = sourceWidth * effective;
        var scaledHeight = sourceHeight * effective;

        var maxOffsetX = MathF.Max(0f, scaledWidth - footprintWidth);
        var maxOffsetY = MathF.Max(0f, scaledHeight - footprintHeight);

        var origin = ImGui.GetCursorScreenPos();
        var centre = origin + (size / 2f);
        var drawList = ImGui.GetWindowDrawList();

        var slide = new Vector2((panX - 0.5f) * maxOffsetX, (panY - 0.5f) * maxOffsetY);
        var half = new Vector2(scaledWidth / 2f, scaledHeight / 2f);

        Vector2 Corner(float sx, float sy)
        {
            var p = new Vector2(sx * half.X, sy * half.Y) - slide;
            return centre + new Vector2((p.X * cos) - (p.Y * sin), (p.X * sin) + (p.Y * cos));
        }

        drawList.PushClipRect(origin, origin + size, true);

        drawList.AddImageQuad(
            sourceTexture!.Handle,
            Corner(-1f, -1f), Corner(1f, -1f), Corner(1f, 1f), Corner(-1f, 1f),
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            0xFFFFFFFFu);

        drawList.PopClipRect();

        ImGui.InvisibleButton("##cropCanvas", size, ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        if (hovered || active)
        {
            var guide = ImGui.GetColorU32(Semantic.Alpha(Vector4.One, 0.18f));

            for (var i = 1; i < 3; i++)
            {
                var x = origin.X + (size.X * i / 3f);
                var y = origin.Y + (size.Y * i / 3f);

                drawList.AddLine(new Vector2(x, origin.Y), new Vector2(x, origin.Y + size.Y), guide, Metrics.Hairline);
                drawList.AddLine(new Vector2(origin.X, y), new Vector2(origin.X + size.X, y), guide, Metrics.Hairline);
            }
        }

        var edge = ImGui.GetColorU32(Semantic.Alpha(Semantic.Primary, hovered || active ? 1f : 0.6f));

        if (IsCircularTarget)
        {
            var radius = MathF.Min(size.X, size.Y) * 0.5f;
            var cover = MathF.Max(size.X, size.Y);

            drawList.AddCircle(centre, radius + (cover * 0.5f),
                ImGui.GetColorU32(Semantic.Alpha(Elevation.Base, 0.82f)), 96, cover);

            drawList.AddCircle(centre, radius, edge, 96, Metrics.Hairline * 1.5f);
        }
        else
        {
            drawList.AddRect(origin, origin + size, edge,
                Metrics.RadiusSoft, ImDrawFlags.None, Metrics.Hairline * 1.5f);
        }

        if (active)
        {
            var delta = ImGui.GetIO().MouseDelta;

            var alongX = (delta.X * cos) + (delta.Y * sin);
            var alongY = (delta.Y * cos) - (delta.X * sin);

            if (maxOffsetX > 0f)
                panX = Math.Clamp(panX - (alongX / maxOffsetX), 0f, 1f);

            if (maxOffsetY > 0f)
                panY = Math.Clamp(panY - (alongY / maxOffsetY), 0f, 1f);
        }

        if (!hovered)
            return;

        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var wheel = ImGui.GetIO().MouseWheel;
        if (wheel != 0f)
            zoom = Math.Clamp(zoom * MathF.Pow(ZoomPerTick, wheel), MinimumZoom, MaximumZoom);
    }

    /// The angle, as a slider under the picture.
    private void DrawAngle(float width)
    {
        Surfaces.Gap(Metrics.Md);

        var wanted = angle;

        if (Fields.Slider("##cropAngle", "Rotation", ref wanted, -AngleLimit, AngleLimit,
                "{0:0.#}°", resetTo: 0f, trackWidth: width * 0.55f))
        {
            if (!ImGui.GetIO().KeyCtrl)
            {
                var nearest = MathF.Round(wanted / 90f) * 90f;

                if (MathF.Abs(wanted - nearest) < SnapDegrees)
                    wanted = nearest;
            }

            angle = Math.Clamp(wanted, -AngleLimit, AngleLimit);
        }
    }

    private void Close()
    {
        sourceTexture?.Dispose();
        sourceTexture = null;
        IsOpen = false;
    }

    /// Releases the decoded source picture if the plugin is torn down with the dialog open.
    public void Dispose() => Close();
}
