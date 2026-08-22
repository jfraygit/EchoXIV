using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;

namespace EchoRoleplay;

/// Where a profile's statuses are drawn.
public enum StatusPlacement
{
    /// The icon row hanging under the character, out in the world.
    UnderCharacter = 0,

    /// Only in the hover card, with the words that go with them.
    InTooltip = 1,
}

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// Bumped when a stored value needs correcting rather than merely defaulting.
    public const int CurrentVersion = 2;

    /// Brings a saved configuration up to date.
    public void Migrate()
    {
        if (Version >= CurrentVersion)
            return;

        if (Version < 2)
            StatusRowYOffset = DefaultRowDrop;

        Version = CurrentVersion;
        Save();
    }

    public bool IsMainWindowOpen { get; set; }

    /// Which tab was open when the session ended.
    public int LastTab { get; set; }

    /// Which section of the character sheet was last open - Identity, Details or Story.
    public int LastProfileSection { get; set; }

    /// How large the window is, as a multiple of its design size.
    public float WindowScale { get; set; } = 1f;

    public const float MinimumWindowScale = 0.75f;
    public const float MaximumWindowScale = 1.6f;

    /// The window's design size, before either scale.
    public const float DefaultWindowWidth = 900f;

    public const float DefaultWindowHeight = 700f;

    /// Pins the window in place, so working in it can't nudge it around.
    public bool WindowLocked { get; set; }

    /// Whether the window was collapsed to its box when the session ended.
    public bool StartMinimised { get; set; }

    /// Where each mode was last left.
    public float FullWindowX { get; set; } = UnsetPosition;
    public float FullWindowY { get; set; } = UnsetPosition;
    public float MinimisedX { get; set; } = UnsetPosition;
    public float MinimisedY { get; set; } = UnsetPosition;

    public const float UnsetPosition = -99999f;

    /// The one colour every other colour in the plugin derives from.
    public Vector4? AccentColour { get; set; }

    /// Whether a profile's roleplay name replaces the one on the nameplate.
    public bool ReplaceNamePlateNames { get; set; } = true;

    /// Whether resting the cursor on somebody shows their profile.
    public bool ShowHoverTooltip { get; set; } = true;

    /// Whether statuses and hover profiles stand down while the player is in a duty.
    public bool HideInDuty { get; set; }

    /// Where somebody's statuses are shown.
    public StatusPlacement Statuses { get; set; } = StatusPlacement.UnderCharacter;

    /// Whether opening somebody's profile plays their theme.
    public bool PlayThemeSongs { get; set; } = true;


    /// Whether the phase 2 status row spike draws.
    public bool ShowStatusRowSpike { get; set; } = true;

    /// Extra distance between the bottom of a nameplate and the status row, in design pixels.
    public float StatusRowYOffset { get; set; } = DefaultRowDrop;

    /// Thirty design pixels under the feet.
    public const float DefaultRowDrop = 30f;

    /// Whether the spike draws a cross on the exact projected anchor point.
    public bool MarkStatusRowAnchor { get; set; }

    /// How hard the row's screen position is eased, nought to one.
    public float StatusRowSmoothing { get; set; } = 1f;

    /// How far away, in world units, a status row is still drawn.
    public float StatusRowMaxDistance { get; set; } = 40f;

    /// Side of one status icon's box at full size, in design pixels.
    public float StatusRowIconSize { get; set; } = DefaultIconSize;

    /// Settled by eye in a crowd.
    public const float DefaultIconSize = 32f;

    /// Whether a status row is withheld when it would land on the game's own interface.
    public bool AvoidGameUi { get; set; } = true;

    /// Whether the row's position is rounded to whole pixels.
    public bool SnapStatusRowToPixels { get; set; } = true;

    /// The newest changelog entry the player has actually had open.
    public string? LastSeenChangelogVersion { get; set; }

    /// This installation's identity on the relay.
    public string OwnerKey { get; set; } = string.Empty;

    /// Returns the owner key, minting and saving one the first time it is asked for.
    public string EnsureOwnerKey()
    {
        if (!string.IsNullOrEmpty(OwnerKey))
            return OwnerKey;

        OwnerKey = Guid.NewGuid().ToString("N");
        Save();
        return OwnerKey;
    }

    /// The relay's own id for each profile this installation has published, keyed on the profile's id.
    public Dictionary<string, string> PublishedIds { get; set; } = [];

    /// Characters this installation has proved through the Lodestone.
    public HashSet<string> VerifiedCharacters { get; set; } = [];

    /// Which relay the note above belongs to.
    public string VerifiedOnRelay { get; set; } = string.Empty;

    /// Which build's rules this installation has accepted, or null for none.
    public string? AcceptedRulesVersion { get; set; }

    /// Show profiles and statuses for friends, and for nobody else.
    public bool OnlyShowFriends { get; set; }

    /// Writes the configuration out.
    public void Save()
    {
        try
        {
            Plugin.PluginInterface.SavePluginConfig(this);
        }
        catch (Exception ex)
        {
            try
            {
                Plugin.Log.Error(ex, "[EchoRoleplay] The configuration could not be saved");
            }
            catch (Exception)
            {
            }
        }
    }
}
