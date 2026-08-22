using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using EchoGlam.Game;

namespace EchoGlam.UI.Controls;

/// Drag to reposition, scroll to zoom, then confirm - the crop step every image upload goes through.
public sealed class ImageCropDialog
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

        if (!ImGui.BeginPopup(PopupId))
        {
            IsOpen = false;
            return;
        }

        IsOpen = true;
        var scale = UiHelpers.Scale;

        Theme.SectionHeader(title, ruleWidth: MaximumViewportWidth * scale);
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
                "##cropconfirm", "Use this", new Vector2(0f, height),
                enabled: sourceTexture != null && error is null && !working))
        {
            working = true;

            try
            {
                onConfirmed?.Invoke(ImageProcessor.ToJpeg(sourcePath, targetWidth, targetHeight, panX, panY, zoom));
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

    private void DrawCanvas(float scale)
    {
        var targetAspect = targetWidth / (float)targetHeight;

        var viewportWidth = MaximumViewportWidth * scale;
        var viewportHeight = viewportWidth / targetAspect;

        if (viewportHeight > MaximumViewportHeight * scale)
        {
            viewportHeight = MaximumViewportHeight * scale;
            viewportWidth = viewportHeight * targetAspect;
        }

        var coverScale = MathF.Max(viewportWidth / sourceWidth, viewportHeight / sourceHeight);
        var effective = coverScale * zoom;

        var scaledWidth = sourceWidth * effective;
        var scaledHeight = sourceHeight * effective;
        var maxOffsetX = MathF.Max(0f, scaledWidth - viewportWidth);
        var maxOffsetY = MathF.Max(0f, scaledHeight - viewportHeight);

        var origin = ImGui.GetCursorScreenPos();
        var size = new Vector2(viewportWidth, viewportHeight);
        var drawList = ImGui.GetWindowDrawList();

        var imageMin = origin - new Vector2(panX * maxOffsetX, panY * maxOffsetY);

        drawList.PushClipRect(origin, origin + size, true);
        drawList.AddImage(sourceTexture!.Handle, imageMin, imageMin + new Vector2(scaledWidth, scaledHeight));
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
