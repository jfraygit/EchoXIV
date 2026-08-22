using System;
using System.Threading;
using System.Threading.Tasks;
using EchoGlam.Shared;

namespace EchoGlam.Game;

/// The bits of gallery state more than one tab needs.
public sealed class GalleryState
{
    private readonly Plugin plugin;

    public GalleryState(Plugin plugin) => this.plugin = plugin;

    /// The relay's limits and vocabularies, or this build's defaults until it answers.
    public GallerySettings Settings { get; private set; } = new();

    /// This installation's own profile, once it has been asked for.
    public ProfileDetail? MyProfile { get; private set; }

    public string? ProfileError { get; private set; }

    public bool LoadingProfile { get; private set; }

    private bool settingsAsked;

    /// Whether the relay actually answered, as opposed to these being this build's defaults.
    public bool SettingsLoaded { get; private set; }

    public string OwnerKey => plugin.Configuration.EnsureOwnerKey();

    /// Fetches the relay's settings once.
    public void EnsureSettings()
    {
        if (settingsAsked)
            return;

        settingsAsked = true;

        _ = Task.Run(async () =>
        {
            try
            {
                Settings = await GalleryClient.SettingsAsync(CancellationToken.None).ConfigureAwait(false);
                SettingsLoaded = true;

                Plugin.Log.Information(
                    $"[EchoGlam] Gallery settings: submissions {(Settings.SubmissionsOpen ? "open" : "closed")}, "
                    + $"{Settings.Tags.Length} tag(s), {Settings.MaximumImages} image(s) per glamour.");
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"[EchoGlam] Gallery settings unavailable: {ex.Message}");
            }
        });
    }

    /// Fetches the player's own profile, creating it on the relay if this installation has never had one.
    public void RefreshProfile(bool force = false)
    {
        if (LoadingProfile || (MyProfile is not null && !force))
            return;

        LoadingProfile = true;
        ProfileError = null;

        _ = Task.Run(async () =>
        {
            try
            {
                MyProfile = await GalleryClient.MyProfileAsync(OwnerKey, CancellationToken.None).ConfigureAwait(false);
                await SyncCharacterAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ProfileError = ex.Message;
                Plugin.Log.Warning($"[EchoGlam] Profile unavailable: {ex.Message}");
            }
            finally
            {
                LoadingProfile = false;
            }
        });
    }

    /// Replaces the held profile after a write that returned a fresh one, so the page redraws from the
    /// relay's answer rather than from what the client hoped it did.
    public void Adopt(ProfileDetail profile) => MyProfile = profile;

    /// The character this installation is currently playing.
    public static (string Name, string World) Character
    {
        get
        {
            var player = Plugin.ObjectTable.LocalPlayer;

            return (
                player?.Name.TextValue ?? string.Empty,
                player?.HomeWorld.Value.Name.ExtractText() ?? string.Empty);
        }
    }

    /// The same, hopped onto the framework thread for callers that are not already on it.
    public static Task<(string Name, string World)> CharacterAsync() =>
        Plugin.Framework.RunOnTick(() => Character);

    /// Keeps the profile's name and world equal to the character's.
    private async Task SyncCharacterAsync()
    {
        if (MyProfile is not { } profile)
            return;

        var (name, world) = await CharacterAsync().ConfigureAwait(false);

        if (string.IsNullOrEmpty(name))
            return;

        if (profile.DisplayName == name && profile.HomeWorld == world)
            return;

        try
        {
            MyProfile = await GalleryClient.UpdateProfileAsync(
                new ProfileSubmission
                {
                    OwnerKey = OwnerKey,
                    DisplayName = name,
                    HomeWorld = world,
                    Bio = profile.Bio,
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[EchoGlam] Couldn't sync the profile name: {ex.Message}");
        }
    }

    /// Whether the board is currently accepting submissions from anybody.
    public bool CanPublish => !SettingsLoaded || Settings.SubmissionsOpen;
}
