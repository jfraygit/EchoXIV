using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using EchoGlam.Game;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI.Controls;

/// One piece of gear's own icon, out of the game's texture pack.
public static class ItemIcon
{
    /// AN EMPTY SLOT IS A FAINT OUTLINE RATHER THAN NOTHING, so a strip of them keeps its shape and a
    /// half-filled look does not read as a half-loaded one.
    public static void Draw(ImDrawListPtr drawList, GlamItem item, Vector2 min, float size, float alpha = 1f)
    {
        var max = min + new Vector2(size, size);

        if (item.IsNone || item.IconId == 0)
        {
            drawList.AddRect(
                min, max, Col.U32(new Vector4(1f, 1f, 1f, 0.10f * alpha)),
                Rad.Chip, ImDrawFlags.None, Stroke.Hair);

            return;
        }

        var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(item.IconId)).GetWrapOrDefault();

        if (texture == null)
            return;

        drawList.AddImage(
            texture.Handle, min, max, Vector2.Zero, Vector2.One,
            Col.U32(new Vector4(1f, 1f, 1f, alpha)));
    }
}
