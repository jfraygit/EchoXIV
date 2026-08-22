using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace EchoRoleplay.Game;

/// Plays one of the game's own tracks over whatever the zone was playing, and puts it back afterwards.
public sealed class GameMusic : IDisposable
{
    /// Which BGM scene is overridden.
    private const uint Scene = 0;

    /// Fade lengths, in milliseconds.
    private const uint FadeOutMs = 800;
    private const uint FadeInMs = 800;

    private readonly IFramework framework;
    private readonly MusicCatalogue catalogue;

    /// The orchestrion row currently playing, or zero.
    public uint NowPlaying { get; private set; }

    public GameMusic(IFramework framework, MusicCatalogue catalogue)
    {
        this.framework = framework;
        this.catalogue = catalogue;
    }

    /// Starts a theme.
    public void Play(uint trackId)
    {
        if (!catalogue.CanPlay(trackId))
            return;

        NowPlaying = trackId;

        framework.RunOnFrameworkThread(() =>
        {
            try
            {
                WriteSong((ushort)trackId);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "[EchoRoleplay] Could not start a theme song");
                NowPlaying = 0;
            }
        });
    }

    /// Keeps the theme in the scene the game keeps taking back.
    public unsafe void Tick()
    {
        if (NowPlaying == 0)
            return;

        try
        {
            var scene = SceneAt(Scene);

            if (scene is not null && scene->PlayingBgmId != NowPlaying)
                WriteSong((ushort)NowPlaying);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoRoleplay] Could not hold a theme song");
            NowPlaying = 0;
        }
    }

    /// Hands the zone its music back.
    public void Stop()
    {
        if (NowPlaying == 0)
            return;

        NowPlaying = 0;

        framework.RunOnFrameworkThread(() =>
        {
            try
            {
                WriteSilence();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "[EchoRoleplay] Could not stop a theme song");
            }
        });
    }

    /// Stops without going through the framework, for shutdown.
    public void Dispose()
    {
        if (NowPlaying == 0)
            return;

        NowPlaying = 0;

        try
        {
            WriteSilence();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoRoleplay] Could not restore the zone's music");
        }
    }

    /// Puts a track into the scene by writing the scene, rather than by asking the game to.
    private static unsafe void WriteSong(ushort bgmId)
    {
        var scene = SceneAt(Scene);

        if (scene is null)
            return;

        scene->EnableCustomFade = true;
        scene->FadeOutTime = FadeOutMs;
        scene->FadeInTime = FadeInMs;
        scene->FadeInStartTime = 0;

        scene->BgmId = bgmId;
        scene->PlayingBgmId = bgmId;
        scene->PreviousBgmId = bgmId;
    }

    /// Hands the zone its music back.
    private static unsafe void WriteSilence()
    {
        var scene = SceneAt(Scene);

        if (scene is null)
            return;

        scene->BgmId = 0;
        scene->PlayingBgmId = 0;
        scene->PreviousBgmId = 0;
    }

    /// A scene, or null.
    private static unsafe BGMSystem.Scene* SceneAt(uint index)
    {
        var system = BGMSystem.Instance();

        if (system is null || index >= system->Scenes.Count)
            return null;

        return system->Scenes.First + index;
    }
}
