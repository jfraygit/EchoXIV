using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using EchoRoleplay.Game;

namespace EchoRoleplay.UI.Controls;

/// Drag to reposition, scroll to zoom, then confirm - the step a portrait goes through before it is uploaded.
public sealed class ImageCropDialog
{
    private const string PopupId = "##echorpcrop";

    /// The largest the preview may get, in design units.
    private const float MaximumViewport = 340f;

    private const float MinimumZoom = 1f;
    private const float MaximumZoom = 4f;

    /// Zoom per wheel notch.
    private const float ZoomPerTick = 1.1f;

    private bool openRequested;
    private string sourcePath = string.Empty;
    private int sourceWidth;
    private int sourceHeight;
    private Action<byte[]>? onConfirmed;

    private IDalamudTextureWrap? sourceTexture;
    private float panX = 0.5f;
    private float panY = 0.5f;
    private float zoom = MinimumZoom;
    private string? error;
    private bool working;

    public bool IsOpen { get; private set; }

    /// Loads the image and arms the popup to open on the next Draw.
    public async Task OpenAsync(string path, Action<byte[]> confirmed)
    {
        sourcePath = path;
        onConfirmed = confirmed;
        panX = 0.5f;
        panY = 0.5f;
        zoom = MinimumZoom;
        error = null;
        working = false;

        try
        {
            if (ImageProcessor.Measure(path) is not { } size)
                throw new InvalidOperationException("that file is not an image");

            sourceWidth = size.Width;
            sourceHeight = size.Height;

            var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            var wrap = await Plugin.TextureProvider.CreateFromImageAsync(bytes).ConfigureAwait(false);

            sourceTexture?.Dispose();
            sourceTexture = wrap;
        }
        catch (Exception ex)
        {
            error = $"That image could not be opened: {ex.Message}";
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

        if (!ImGui.BeginPopup(PopupId))
        {
            IsOpen = false;
            return;
        }

        IsOpen = true;
        var scale = UiHelpers.Scale;

        Theme.SectionHeader("Choose Your Portrait", ruleWidth: MaximumViewport * scale);
        ImGui.Dummy(new Vector2(0f, 4f * scale));
        ImGui.TextColored(Theme.TextDim, "Drag to reposition, scroll to zoom.");
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        if (error is { } message)
            ImGui.TextColored(Theme.Bad, message);
        else if (sourceTexture != null)
            DrawCanvas(scale);

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
                enabled: panX != 0.5f || panY != 0.5f || zoom != MinimumZoom,
                tooltip: "Back to the whole picture, centred."))
        {
            panX = 0.5f;
            panY = 0.5f;
            zoom = MinimumZoom;
        }

        ImGui.SameLine(0f, 6f * scale);

        if (EchoButton.Draw(
                "##cropconfirm", "Okay", new Vector2(0f, height),
                enabled: sourceTexture != null && error is null && !working))
        {
            working = true;

            try
            {
                onConfirmed?.Invoke(
                    ImageProcessor.ToJpeg(sourcePath, ImageProcessor.PortraitSize, panX, panY, zoom));

                Close();
                ImGui.CloseCurrentPopup();
            }
            catch (Exception ex)
            {
                error = $"That image could not be processed: {ex.Message}";
            }
            finally
            {
                working = false;
            }
        }

        ImGui.EndPopup();
    }

    private void DrawCanvas(float scale)
    {
        var side = MaximumViewport * scale;

        var coverScale = MathF.Max(side / sourceWidth, side / sourceHeight);
        var effective = coverScale * zoom;

        var scaledWidth = sourceWidth * effective;
        var scaledHeight = sourceHeight * effective;
        var maxOffsetX = MathF.Max(0f, scaledWidth - side);
        var maxOffsetY = MathF.Max(0f, scaledHeight - side);

        var origin = ImGui.GetCursorScreenPos();
        var size = new Vector2(side, side);
        var drawList = ImGui.GetWindowDrawList();
        var centre = origin + (size / 2f);
        var radius = side / 2f;

        var imageMin = origin - new Vector2(panX * maxOffsetX, panY * maxOffsetY);

        drawList.PushClipRect(origin, origin + size, true);
        drawList.AddImage(sourceTexture!.Handle, imageMin, imageMin + new Vector2(scaledWidth, scaledHeight));

        var shade = ImGui.GetColorU32(new Vector4(0.06f, 0.06f, 0.09f, 0.72f));
        var segments = 96;

        drawList.PathArcTo(centre, radius + (radius / 2f), 0f, MathF.Tau, segments);
        drawList.PathStroke(shade, ImDrawFlags.None, radius);

        drawList.PopClipRect();

        ImGui.InvisibleButton("##cropcanvas", size, ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        drawList.AddCircle(
            centre, radius - (1f * scale),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, hovered || active ? 1f : 0.75f)),
            segments, 1.5f * scale);

        if (hovered || active)
        {
            var guide = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.16f));

            drawList.AddLine(
                new Vector2(centre.X, origin.Y), new Vector2(centre.X, origin.Y + side), guide, 1f * scale);

            drawList.AddLine(
                new Vector2(origin.X, centre.Y), new Vector2(origin.X + side, centre.Y), guide, 1f * scale);
        }

        if (active)
        {
            var delta = ImGui.GetIO().MouseDelta;

            if (maxOffsetX > 0f)
                panX = Math.Clamp(panX - (delta.X / maxOffsetX), 0f, 1f);

            if (maxOffsetY > 0f)
                panY = Math.Clamp(panY - (delta.Y / maxOffsetY), 0f, 1f);
        }

        if (!hovered)
            return;

        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var wheel = ImGui.GetIO().MouseWheel;

        if (wheel != 0f)
            zoom = Math.Clamp(zoom * MathF.Pow(ZoomPerTick, wheel), MinimumZoom, MaximumZoom);
    }

    private void Close()
    {
        sourceTexture?.Dispose();
        sourceTexture = null;
        IsOpen = false;
    }
}
