using Dalamud.Game.Config;
using Dalamud.Plugin.Services;

namespace EchoMix.Plugin.Audio;

/// Mutes the game's own BGM and ambient/environment sound while a deck is playing, so EchoMix's track isn't
/// competing with them.
public sealed class GameSoundMuteController
{
    private static readonly SystemConfigOption[] Categories =
    {
        SystemConfigOption.IsSndBgm,
        SystemConfigOption.IsSndEnv,
    };

    private readonly IGameConfig gameConfig;
    private readonly bool[] originalMuted = new bool[Categories.Length];
    private bool isControlling;

    public GameSoundMuteController(IGameConfig gameConfig)
    {
        this.gameConfig = gameConfig;
    }

    public void SetShouldMute(bool shouldMute)
    {
        if (shouldMute && !isControlling)
        {
            for (var i = 0; i < Categories.Length; i++)
            {
                gameConfig.TryGet(Categories[i], out originalMuted[i]);
                gameConfig.Set(Categories[i], true);
            }

            isControlling = true;
        }
        else if (!shouldMute && isControlling)
        {
            for (var i = 0; i < Categories.Length; i++)
                gameConfig.Set(Categories[i], originalMuted[i]);

            isControlling = false;
        }
    }
}
