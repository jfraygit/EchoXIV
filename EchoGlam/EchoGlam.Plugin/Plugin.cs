using System;
using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EchoGlam.Game;
using EchoGlam.UI;

namespace EchoGlam;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/echoglam";

    /// The short one, for typing mid-game.
    private const string ShortCommandName = "/eg";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    /// Who the player is.
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;

    /// Only for LocalPlayer - the race, clan and gender a glamour is being previewed against, which is what
    /// the "your character cannot wear this" check compares to.
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;

    /// The Item, Stain, EquipSlotCategory and EquipRaceCategory sheets.
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;

    /// Item icons for the picker and the gear list, and the decode path for gallery screenshots.
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    /// Anything that reads client memory happens here rather than while drawing.
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    /// What is actually in the character's equipment slots.
    [PluginService] internal static IGameInventory GameInventory { get; private set; } = null!;

    /// In combat, bound by duty, between areas.
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    /// The game's own right-click menu, where a friend is added from.
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;

    public Configuration Configuration { get; }

    public string Version { get; } =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    public readonly WindowSystem WindowSystem = new("EchoGlam");
    private readonly MainWindow mainWindow;

    /// Header and icon fonts, shared with the rest of the suite - see UI/Fonts.
    public Fonts Fonts { get; }

    /// Every wearable item in the game.
    public ItemCatalogue Items { get; } = new();

    public DyeCatalogue Dyes { get; } = new();

    /// Cheapest market board listing per item, asked for only when somebody hovers a price.
    public MarketPrices Prices { get; } = new();

    /// The thing that actually dresses the character.
    public Wardrobe Wardrobe { get; }

    /// Saved looks, in their own file beside the configuration.
    public OutfitStore Outfits { get; }

    /// The conditions under which those looks put themselves on.
    public OutfitRuleStore Rules { get; }

    /// Every map and duty in the game, for a rule to name one.
    public Zones Zones { get; }

    /// Which house you are standing in, so a rule can name one rather than every cottage of its kind in the
    /// game.
    public Housing Housing { get; } = new();

    /// Every emote and action animation, and the deferred player that survives a redraw.
    public Animations Animations { get; }

    /// Watches the conditions and wears what they call for.
    public OutfitAutomation Automation { get; }

    /// What gear this character can actually get at, remembered across sessions because most of it is only
    /// readable while the container is open.
    public OwnedItems Owned { get; }

    /// The relay's current limits and this installation's own profile, shared by every tab that needs them so
    /// they cannot end up holding three different answers.
    public GalleryState Gallery { get; }

    /// Who has agreed to see whose glamours.
    public Friendships Friendships { get; }

    /// Sends this character's look to those friends and draws theirs.
    public LookSharing Looks { get; }

    private readonly FriendContextMenu friendMenu;

    /// Takes screenshots, and hides the plugin's windows while it does.
    public Screenshots Screenshots { get; } = new();

    /// Dalamud's file browser, for picking an avatar, a banner or a screenshot.
    public FileDialogManager FileDialogs { get; } = new();

    private bool catalogueStarted;

    /// Starts the one-off catalogue build, off the game's thread.
    public void EnsureCatalogues()
    {
        if (catalogueStarted)
            return;

        catalogueStarted = true;

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                Dyes.Build();
                ColourPalette.Build();
                Items.Build();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[EchoGlam] Catalogue build failed");
            }
        });
    }

    /// Race categories this character is allowed to wear, or null before it has been worked out.
    private HashSet<ushort>? allowedRaceCategories;
    private byte cachedRace = byte.MaxValue;
    private byte cachedSex = byte.MaxValue;

    /// Whether the local player's race and gender can wear this item.
    public bool CanWear(GlamItem item)
    {
        if (item.RaceCategory == 0)
            return true;

        var player = ObjectTable.LocalPlayer;
        if (player == null)
            return true;

        var race = player.Customize[(int)CustomizeIndex.Race];
        var sex = player.Customize[(int)CustomizeIndex.Sex];

        if (allowedRaceCategories == null || race != cachedRace || sex != cachedSex)
        {
            cachedRace = race;
            cachedSex = sex;
            allowedRaceCategories = ItemCatalogue.AllowedRaceCategories(race, sex);
        }

        return allowedRaceCategories.Contains(item.RaceCategory);
    }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Migrate();

        Wardrobe = new Wardrobe(Items);
        Outfits = new OutfitStore(PluginInterface.GetPluginConfigDirectory());
        Rules = new OutfitRuleStore(PluginInterface.GetPluginConfigDirectory());
        Zones = new Zones();
        Animations = new Animations();
        Automation = new OutfitAutomation(this);
        Owned = new OwnedItems(PluginInterface.GetPluginConfigDirectory());
        Gallery = new GalleryState(this);
        Friendships = new Friendships(this);
        Looks = new LookSharing(this);
        friendMenu = new FriendContextMenu(ContextMenu, this);
        Fonts = new Fonts(PluginInterface);

        if (Configuration.AccentColour is { } accent)
            Theme.ApplyAccent(accent);

        mainWindow = new MainWindow(this);
        WindowSystem.AddWindow(mainWindow);
        mainWindow.IsOpen = Configuration.IsMainWindowOpen;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens EchoGlam.",
        });

        CommandManager.AddHandler(ShortCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens EchoGlam.",
            ShowInHelp = false,
        });

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainWindow;
        Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Wardrobe.RevertAll();

        Looks.ReleaseAll();

        Configuration.IsMainWindowOpen = mainWindow.IsOpen;
        Configuration.Save();

        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainWindow;
        Framework.Update -= OnFrameworkUpdate;

        friendMenu.Dispose();
        Friendships.Dispose();
        Looks.Dispose();

        WindowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(ShortCommandName);
    }

    /// Keeps the look on the character.
    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            Housing.Tick();
            Animations.Tick();
            Automation.Tick();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[EchoGlam] Outfit rule evaluation failed");
        }


        try
        {
            Wardrobe.Tick();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[EchoGlam] Wardrobe tick failed");
        }


        try
        {
            Owned.Tick();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[EchoGlam] Owned-item scan failed");
        }

        try
        {
            Friendships.Tick();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[EchoGlam] Friend sync failed");
        }

        try
        {
            Looks.Tick();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[EchoGlam] Look sharing failed");
        }
    }

    private void OnCommand(string command, string args) => ToggleMainWindow();

    /// Read the UI scale before anything draws, then draw.
    private void DrawUi()
    {
        if (!ClientState.IsLoggedIn)
            return;

        UiHelpers.SampleScale();
        WindowSystem.Draw();

        FileDialogs.Draw();
    }

    private void ToggleMainWindow() => mainWindow.IsOpen = !mainWindow.IsOpen;
}
