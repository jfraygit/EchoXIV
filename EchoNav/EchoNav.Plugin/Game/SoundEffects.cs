using System;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace EchoNav.Game;

/// Plays one of the game's own sound effects - the same ones &lt;se.1&gt; through &lt;se.16&gt; produce in
/// chat.
public static class SoundEffects
{
    /// The one that plays when a trip finishes.
    public const uint Arrival = 5;

    public static void Play(uint soundEffectId)
    {
        try
        {
            UIGlobals.PlayChatSoundEffect(soundEffectId);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, $"[EchoNav] Could not play sound effect {soundEffectId}");
        }
    }
}
