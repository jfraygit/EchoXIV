using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using EchoRoleplay.Shared;

namespace EchoRoleplay.Game;

/// What a profile puts on the game's own nameplate: the roleplay name, in place of the character's own.
public sealed class NamePlateNames : IDisposable
{
    /// How long a name may be on a plate.
    public const int MaximumPlateName = 32;

    private readonly INamePlateGui namePlateGui;
    private readonly ProfileDirectory directory;
    private readonly Configuration configuration;
    private readonly Func<string> localCharacterKey;

    /// How many plates got a name on the last update that carried any.
    public int LastNamesWritten { get; private set; }

    public int UpdatesSeen { get; private set; }

    public NamePlateNames(
        INamePlateGui namePlateGui, ProfileDirectory directory, Configuration configuration,
        Func<string> localCharacterKey)
    {
        this.namePlateGui = namePlateGui;
        this.directory = directory;
        this.configuration = configuration;
        this.localCharacterKey = localCharacterKey;

        this.namePlateGui.OnNamePlateUpdate += OnNamePlateUpdate;
    }

    public void Dispose()
    {
        namePlateGui.OnNamePlateUpdate -= OnNamePlateUpdate;

        Refresh();
    }

    /// Rebuilds every plate on the next frame.
    public void Refresh() => namePlateGui.RequestRedraw();

    private void OnNamePlateUpdate(
        INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        UpdatesSeen++;
        LastNamesWritten = 0;

        if (!configuration.ReplaceNamePlateNames)
            return;

        var localKey = localCharacterKey();

        foreach (var handler in handlers)
        {
            if (handler.NamePlateKind != NamePlateKind.PlayerCharacter || handler.GameObjectId == 0)
                continue;

            var player = handler.PlayerCharacter;
            if (player is null)
                continue;

            var profile = directory.Lookup(ProfileDirectory.KeyFor(player), localKey);
            if (profile is null)
                continue;

            if (PlateName(profile.Name) is not { Length: > 0 } name)
                continue;

            handler.NameParts.Text = new SeString(new TextPayload(name));
            LastNamesWritten++;
        }
    }

    /// A profile's roleplay name, reduced to something that can safely go on a plate.
    public static string PlateName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var text = new StringBuilder(raw.Length);
        var lastWasSpace = true;

        foreach (var character in raw)
        {
            var c = character is '\n' or '\r' or '\t' ? ' ' : character;

            if (char.IsControl(c))
                continue;

            if (c == ' ')
            {
                if (lastWasSpace)
                    continue;

                lastWasSpace = true;
            }
            else
            {
                lastWasSpace = false;
            }

            text.Append(c);

            if (text.Length >= MaximumPlateName)
                break;
        }

        return text.ToString().TrimEnd();
    }
}
