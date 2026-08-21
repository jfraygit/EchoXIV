using Dalamud.Configuration;

namespace EchoNav;

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

        WindowWidth = DefaultWindowWidth;
        WindowHeight = DefaultWindowHeight;
        Version = CurrentVersion;
        Save();
    }

    public bool IsMainWindowOpen { get; set; }

    /// Window size, set from the sliders in Settings rather than by dragging the frame.
    public const float DefaultWindowWidth = 440f;
    /// Sized for what actually turns up: four live encounters at once is the most anyone has seen, so four
    /// cards, the chrome above them and the journey strip below is the whole window.
    public const float DefaultWindowHeight = 41f + (4f * 67f) + 78f + 16f;

    public float WindowWidth { get; set; } = DefaultWindowWidth;
    public float WindowHeight { get; set; } = DefaultWindowHeight;

    /// Pins the window in place, so working in it can't nudge it around.
    public bool WindowLocked { get; set; }

    /// Where each mode was last left.
    public float FullWindowX { get; set; } = UnsetPosition;
    public float FullWindowY { get; set; } = UnsetPosition;
    public float MinimisedX { get; set; } = UnsetPosition;
    public float MinimisedY { get; set; } = UnsetPosition;

    public const float UnsetPosition = -99999f;

    /// Whether the window was collapsed when the session ended.
    public bool StartMinimised { get; set; }

    /// The one colour every other colour in the plugin derives from.
    public System.Numerics.Vector4? AccentColour { get; set; }

    /// The diagnostics window, remembered separately and off by default.
    public bool IsDebugWindowOpen { get; set; }

    /// Chime when a trip finishes.
    public bool ArrivalSound { get; set; } = true;

    /// Mount before setting off, and again after anything that dismounts.
    public bool AutoMount { get; set; } = true;

    /// The pot cycle's memory - see PotTracker.
    public System.Numerics.Vector3? PotNorthPosition { get; set; }
    public System.Numerics.Vector3? PotSouthPosition { get; set; }

    public long PotLastSpawnEpoch { get; set; }

    /// "North" or "South" - which pot was last seen, since they alternate.
    public string PotLastSpawnSide { get; set; } = string.Empty;

    /// How many times the zone had been entered when that spawn was recorded - see PotInstance.
    public int PotLastSpawnVisit { get; set; } = -1;

    /// Entries into the supported zone, counted for the above.
    public int ZoneVisit { get; set; }

    /// Steer around monsters that lie on the route.
    public bool AvoidMonsters { get; set; }

    /// The newest changelog entry the player has actually had open.
    public string? LastSeenChangelogVersion { get; set; }

    /// Monsters below this level are not worth steering around.
    public int IgnoreMonstersBelowLevel { get; set; }

    /// Step aside for coffers that lie on the route - see CofferCollector.
    public bool CollectCoffers { get; set; }

    /// Keep the knowledge-crystal buffs up automatically - see PhantomBuffer.
    public bool PhantomBuffAtCrystals { get; set; }

    /// Which mount to summon, as a Mount sheet row id.
    public uint MountId { get; set; }

    /// Whether moving along the driver's positive lateral axis produces a bearing a quarter turn clockwise,
    /// in the bearing convention MovementDriver uses.
    public bool? LateralProbePositive { get; set; }

    /// How the game's camera angle lines up with the driver's bearing convention - the constant offset
    /// between them, and whether they count the same way round.
    public float? CameraOffset { get; set; }
    public bool? CameraOppositeSense { get; set; }

    /// Everything needed to start steering immediately, with no measuring walk first.
    public bool HasFullCalibration =>
        LateralProbePositive.HasValue && CameraOffset.HasValue && CameraOppositeSense.HasValue;

    public void ClearCalibration()
    {
        LateralProbePositive = null;
        CameraOffset = null;
        CameraOppositeSense = null;
        Save();
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
