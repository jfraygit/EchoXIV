using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using EchoGlam.Game;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI.Controls;

/// Drag to reposition, scroll to zoom, then confirm - the crop step every image upload goes through.
public sealed class ImageCropDialog : IDisposable
{
    private const string PopupId = "##echoglamcrop";

    /// The largest the preview may get, in design units.
    private const float MaximumViewportWidth = 380f;
    private const float MaximumViewportHeight = 440f;

    private const float MinimumZoom = 1f;
    private const float MaximumZoom = 4f;

    /// Zoom per wheel notch.
    private const float ZoomPerTick = 1.1f;

    private bool openRequested;
    private string sourcePath = string.Empty;
    private int targetWidth;
    private int targetHeight;
    private int sourceWidth;
    private int sourceHeight;
    private string title = "Position your screenshot";
    private Action<byte[]>? onConfirmed;

    private IDalamudTextureWrap? sourceTexture;
    private float panX = 0.5f;
    private float panY = 0.5f;
    private float zoom = MinimumZoom;

    /// How far the picture is turned clockwise, in degrees.
    private float angle;

    private string? error;
    private bool working;

    public bool IsOpen { get; private set; }

    /// Loads the image and arms the popup to open on the next Draw.
    public async Task OpenAsync(string path, int width, int height, string heading, Action<byte[]> confirmed)
    {
        sourcePath = path;
        targetWidth = width;
        targetHeight = height;
        title = heading;
        onConfirmed = confirmed;
        panX = 0.5f;
        panY = 0.5f;
        zoom = MinimumZoom;
        angle = 0f;
        error = null;
        working = false;

        try
        {
            if (ImageProcessor.Measure(path) is not { } size)
                throw new InvalidOperationException("that file isn't an image");

            sourceWidth = size.Width;
            sourceHeight = size.Height;

            var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            var wrap = await Plugin.TextureProvider.CreateFromImageAsync(bytes).ConfigureAwait(false);

            sourceTexture?.Dispose();
            sourceTexture = wrap;
        }
        catch (Exception ex)
        {
            error = $"Couldn't open that image: {ex.Message}";
        }

        openRequested = true;
    }

    /// Must be called every frame, open or not.
    public void Draw()
    {
        if (openRequested)
        {
            ImGui.OpenPopup(PopupId);
            openRequested = false;
        }

        Modal.Centre();

        if (!ImGui.BeginPopup(PopupId))
        {
            IsOpen = false;
            return;
        }

        IsOpen = true;
        var scale = UiHelpers.Scale;
        var viewport = Viewport(scale);

        Theme.SectionHeader(title, ruleWidth: viewport.X);
        ImGui.Dummy(new Vector2(0f, 4f * scale));
        ImGui.TextColored(Theme.TextDim, "Drag to reposition, scroll to zoom.");
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        if (error is { } message)
            ImGui.TextColored(Theme.Bad, message);
        else if (sourceTexture != null)
            DrawCanvas(scale, viewport);

        if (sourceTexture != null && error is null)
            DrawAngle(scale, viewport.X);

        ImGui.Dummy(new Vector2(0f, 10f * scale));

        var height = 26f * scale;

        if (EchoButton.Draw("##cropcancel", "Cancel", new Vector2(0f, height)))
        {
            Close();
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine(0f, 6f * scale);

        if (EchoButton.Draw(
                "##cropreset", "Reset", new Vector2(0f, height),
                enabled: panX != 0.5f || panY != 0.5f || zoom != MinimumZoom || angle != 0f,
                tooltip: "Back to the whole picture, upright and centred."))
        {
            panX = 0.5f;
            panY = 0.5f;
            zoom = MinimumZoom;
            angle = 0f;
        }

        ImGui.SameLine(0f, 6f * scale);

        if (EchoButton.Draw(
                "##cropconfirm", "Use this", new Vector2(0f, height),
                enabled: sourceTexture != null && error is null && !working))
        {
            working = true;

            try
            {
                onConfirmed?.Invoke(
                    ImageProcessor.ToJpeg(
                        sourcePath, targetWidth, targetHeight, panX, panY, zoom, angle));

                Close();
                ImGui.CloseCurrentPopup();
            }
            catch (Exception ex)
            {
                error = $"Couldn't process that image: {ex.Message}";
            }
            finally
            {
                working = false;
            }
        }

        ImGui.EndPopup();
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
                         * ImageProcessor.CoverMargin;

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

        ImGui.InvisibleButton("##cropcanvas", size, ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        if (hovered || active)
        {
            var guide = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.18f));

            for (var i = 1; i < 3; i++)
            {
                var x = origin.X + (size.X * i / 3f);
                var y = origin.Y + (size.Y * i / 3f);

                drawList.AddLine(new Vector2(x, origin.Y), new Vector2(x, origin.Y + size.Y), guide, 1f * scale);
                drawList.AddLine(new Vector2(origin.X, y), new Vector2(origin.X + size.X, y), guide, 1f * scale);
            }
        }

        drawList.AddRect(
            origin, origin + size,
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, hovered || active ? 1f : 0.7f)),
            6f * scale, ImDrawFlags.None, 1.5f * scale);

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

    /// How far either way the angle may be taken, and how near a quarter turn counts as being at one.
    private const float AngleLimit = 180f;

    /// Inside this of a quarter turn, the slider sits on the quarter turn.
    private const float SnapDegrees = 2.5f;

    /// The angle, as a slider under the picture.
    private void DrawAngle(float scale, float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var line = Text.CaptionSize * 1.4f;

        var reading = $"{angle:0.#}°";

        using (Text.PushCaption())
        {
            Text.Tracked(drawList, origin, "ROTATION", Col.U32(Col.TextMuted), 1.8f, width);

            drawList.AddText(
                new Vector2(origin.X + width - Text.Width(reading), origin.Y),
                Col.U32(angle == 0f ? Col.TextMuted : Col.Text), reading);
        }

        ImGui.Dummy(new Vector2(width, line));

        var wanted = angle;

        if (EchoSlider.Draw(
                "##cropangle", ref wanted, -AngleLimit, AngleLimit, width,
                format: "{0:0.#}°", resetTo: 0f, ticks: [-90f, 0f, 90f]))
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
