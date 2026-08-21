using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using EchoNav.Game;

namespace EchoNav.UI;

/// The icon the game itself draws for an encounter.
public sealed class EncounterArt(ITextureProvider textures)
{
    /// The encounter's own map icon, or null if the game hasn't given it one.
    public IDalamudTextureWrap? Icon(NavTarget target) =>
        target.MapIconId == 0
            ? null
            : textures.GetFromGameIcon(new GameIconLookup(target.MapIconId)).GetWrapOrDefault();
}
