using System;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;

namespace EchoMix.Plugin.UI.Controls;

/// A drag-to-reposition crop popup shared by every image upload point (DJ avatar, DJ banner, Broadcast show
/// image) - every one of those used to auto-center-crop to the target aspect with no way to pick which part
/// of the source photo actually survives.
public sealed class ImageCropDialog
{
    private const string PopupId = "##imageCropDialog";
    private const float MaxViewportWidth = 360f;
    private const float MaxViewportHeight = 320f;

    private bool openRequested;
    private string sourcePath = string.Empty;
    private int targetWidth;
    private int targetHeight;
    private int sourceWidth;
    private int sourceHeight;
    private Action<byte[]>? onConfirmed;

    private const float MinZoom = 1f;
    private const float MaxZoom = 4f;
    private const float ZoomPerWheelTick = 1.1f;

    private IDalamudTextureWrap? sourceTexture;
    private float panX = 0.5f;
    private float panY = 0.5f;
    private float zoom = MinZoom;
    private string? error;

    public bool IsOpen { get; private set; }

    /// Loads the source image for preview and arms the popup to open on the next Draw() call - Draw() must be
    /// called every frame afterward (regardless of IsOpen) for it to actually appear once loading finishes,
    /// same as every other file-triggered popup in this plugin.
    public async Task OpenAsync(string sourcePathToOpen, int newTargetWidth, int newTargetHeight, Action<byte[]> confirmedCallback)
    {
        sourcePath = sourcePathToOpen;
        targetWidth = newTargetWidth;
        targetHeight = newTargetHeight;
        onConfirmed = confirmedCallback;
        panX = 0.5f;
        panY = 0.5f;
        zoom = MinZoom;
        error = null;

        try
        {
            using (var bitmap = new Bitmap(sourcePath))
            {
                sourceWidth = bitmap.Width;
                sourceHeight = bitmap.Height;
            }

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

        if (!ImGui.BeginPopup(PopupId))
        {
            IsOpen = false;
            return;
        }

        IsOpen = true;
        ImGui.SetWindowFontScale(scale);

        ImGui.TextColored(Theme.NeutralAccent, "Position Your Image");
        ImGui.TextDisabled("Drag inside the frame to reposition, scroll to zoom, then confirm.");
        ImGui.Spacing();

        if (error != null)
        {
            ImGui.TextColored(Theme.OrangeAccent, error);
        }
        else if (sourceTexture != null)
        {
            DrawCropCanvas(scale);
        }

        ImGui.Spacing();

        var buttonSize = new Vector2(120, 28) * scale;
        if (PanelButton.Draw("##cropCancel", null, null, "Cancel", buttonSize, Theme.OrangeAccent))
        {
            Close();
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();

        var canConfirm = sourceTexture != null && error == null;
        ImGui.BeginDisabled(!canConfirm);
        if (PanelButton.Draw("##cropConfirm", null, null, "Use This", buttonSize, Theme.NeutralAccent) && canConfirm)
        {
            try
            {
                var processed = ShowImageProcessor.ProcessToJpeg(sourcePath, targetWidth, targetHeight, panX, panY, zoom);
                onConfirmed?.Invoke(processed);
                Close();
                ImGui.CloseCurrentPopup();
            }
            catch (Exception ex)
            {
                error = $"Couldn't process that image: {ex.Message}";
            }
        }
        ImGui.EndDisabled();

        ImGui.EndPopup();
    }

    private void DrawCropCanvas(float scale)
    {
        var targetAspect = targetWidth / (float)targetHeight;
        float viewportWidth, viewportHeight;
        if (targetAspect >= 1f)
        {
            viewportWidth = MaxViewportWidth * scale;
            viewportHeight = viewportWidth / targetAspect;
            if (viewportHeight > MaxViewportHeight * scale)
            {
                viewportHeight = MaxViewportHeight * scale;
                viewportWidth = viewportHeight * targetAspect;
            }
        }
        else
        {
            viewportHeight = MaxViewportHeight * scale;
            viewportWidth = viewportHeight * targetAspect;
        }

        var coverScale = MathF.Max(viewportWidth / sourceWidth, viewportHeight / sourceHeight);
        var effectiveScale = coverScale * zoom;
        var scaledWidth = sourceWidth * effectiveScale;
        var scaledHeight = sourceHeight * effectiveScale;
        var maxOffsetX = MathF.Max(0f, scaledWidth - viewportWidth);
        var maxOffsetY = MathF.Max(0f, scaledHeight - viewportHeight);

        var canvasPos = ImGui.GetCursorScreenPos();
        var canvasSize = new Vector2(viewportWidth, viewportHeight);
        var drawList = ImGui.GetWindowDrawList();

        var imageOffset = new Vector2(panX * maxOffsetX, panY * maxOffsetY);
        var imageMin = canvasPos - imageOffset;

        drawList.PushClipRect(canvasPos, canvasPos + canvasSize, true);
        drawList.AddImage(sourceTexture!.Handle, imageMin, imageMin + new Vector2(scaledWidth, scaledHeight));
        drawList.PopClipRect();
        drawList.AddRect(canvasPos, canvasPos + canvasSize, ImGui.GetColorU32(Theme.NeutralAccent), 4f * scale, ImDrawFlags.None, 1.5f);

        ImGui.InvisibleButton("##cropCanvas", canvasSize);

        if (ImGui.IsItemActive())
        {
            var delta = ImGui.GetIO().MouseDelta;
            if (maxOffsetX > 0f)
                panX = Math.Clamp(panX - (delta.X / maxOffsetX), 0f, 1f);
            if (maxOffsetY > 0f)
                panY = Math.Clamp(panY - (delta.Y / maxOffsetY), 0f, 1f);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            var wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0f)
                zoom = Math.Clamp(zoom * MathF.Pow(ZoomPerWheelTick, wheel), MinZoom, MaxZoom);
        }
    }

    private void Close()
    {
        sourceTexture?.Dispose();
        sourceTexture = null;
        IsOpen = false;
    }
}
