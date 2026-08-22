using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using EchoGlam.Game;
using EchoGlam.UI.Controls;

namespace EchoGlam.UI;

/// The wardrobe: a slot down the left, everything that fits it on the right.
public sealed class DressingRoomTab
{
    private readonly Plugin plugin;

    private GlamSlot selectedSlot = GlamSlot.Body;
    private string search = string.Empty;

    /// ClassJob row id the picker is filtered to, or null for everything.
    private uint? selectedJob;

    /// Whether the picker is narrowed to gear this character can get at.
    private bool ownedOnly;

    /// Which kinds of hard-to-get gear the picker is hiding.
    private bool hideStore;

    private bool hideSeasonal;
    private bool hideExclusive;

    /// This frame's item list, and how many the source filters took out of it.
    private List<GlamItem> matches = [];

    private int hiddenBySource;

    /// The last dye picked in each channel, carried onto whatever goes on next.
    private byte stickyStain0;

    private byte stickyStain1;

    /// What dyes to put a piece on in, given what it can actually take.
    private (byte Stain0, byte Stain1) StainsFor(GlamItem item) =>
        (item.DyeChannels >= 1 ? stickyStain0 : (byte)0,
         item.DyeChannels >= 2 ? stickyStain1 : (byte)0);

    /// Which slot's dye row is open.
    private GlamSlot? dyeOpenFor;

    /// What the right-hand pane is showing.
    private enum RightPane
    {
        Items,
        Outfits,
        Appearance,
    }

    private RightPane pane = RightPane.Items;

    private readonly AppearanceEditor appearance;

    /// Name typed into the save box.
    private string outfitName = string.Empty;

    /// Whether a saved outfit carries the appearance as well as the gear.
    private bool saveAppearance;

    /// A Glamourer design that has been pasted and read, waiting to be named and kept.
    private GlamourerLook? pendingImport;

    /// The name the imported outfit will be saved under.
    private string importName = string.Empty;

    /// Why the last paste did not work, for the person who pasted it.
    private string importError = string.Empty;

    /// Confirmation that a design went to the clipboard.
    private string exportNotice = string.Empty;

    /// Filter over the saved outfit list.
    private string outfitSearch = string.Empty;

    /// Newest first rather than alphabetical.
    private bool outfitsByDate;

    /// Which outfit's delete button has been armed, if any.
    private string? pendingDelete;

    /// Which outfit's conditions are open, or null for the saved list.
    private string? rulesFor;

    /// Whether the priority list is open.
    private bool priorityOpen;

    /// State of the map picker, while adding or changing a place rule.
    private OutfitRule? pickingPlaceFor;
    private string zoneSearch = string.Empty;

    /// Null for everything, true for duties only, false for open world only.
    private bool? zoneDutyFilter;

    /// Filter over the fallback-outfit popup.
    private string originalSearch = string.Empty;

    /// Which outfit's animation is being chosen, or null.
    private string? pickingAnimationFor;
    private string animationSearch = string.Empty;

    /// Null for both, or one source of animations.
    private AnimationSource? animationFilter;

    public DressingRoomTab(Plugin plugin)
    {
        this.plugin = plugin;
        appearance = new AppearanceEditor(plugin);
    }

    private ItemCatalogue Items => plugin.Items;
    private DyeCatalogue Dyes => plugin.Dyes;
    private Wardrobe Wardrobe => plugin.Wardrobe;

    /// Width of the slot column, in design units.
    private const float SlotColumnDesign = 210f;

    /// An item cell: the icon box, and the row height including the name under it.
    private const float CellIconDesign = 46f;
    private const float CellDesign = 54f;
    private const float CellGapDesign = 6f;

    public void Draw()
    {
        var scale = UiHelpers.Scale;

        plugin.EnsureCatalogues();

        if (!Items.Ready || !Dyes.Ready)
        {
            DrawBuilding();
            return;
        }

        DrawToolbar();
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        var available = ImGui.GetContentRegionAvail();
        var slotWidth = SlotColumnDesign * scale;

        if (ImGui.BeginChild("##slots", new Vector2(slotWidth, available.Y), false))
            DrawSlotList();

        ImGui.EndChild();
        ImGui.SameLine(0f, 10f * scale);

        if (ImGui.BeginChild("##picker", new Vector2(0f, available.Y), false))
        {
            switch (pane)
            {
                case RightPane.Outfits:
                    DrawOutfits();
                    break;

                case RightPane.Appearance:
                    appearance.Draw();
                    break;

                default:
                    DrawPicker();
                    break;
            }
        }

        ImGui.EndChild();
    }

    /// The outfits pane: save what you are wearing, and search what you have saved.
    private void DrawOutfits()
    {
        if (pickingPlaceFor is not null)
        {
            DrawZonePicker();
            return;
        }

        if (pickingAnimationFor is { } animating)
        {
            DrawAnimationPicker(animating);
            return;
        }

        if (rulesFor is { } editing)
        {
            DrawOutfitConditions(editing);
            return;
        }

        if (priorityOpen)
        {
            DrawPriority();
            return;
        }

        DrawOutfitList();
    }

    private void DrawOutfitList()
    {
        var scale = UiHelpers.Scale;
        var store = plugin.Outfits;
        var width = ImGui.GetContentRegionAvail().X;
        var gap = 8f * scale;

        DrawSavePanel(width);
        ImGui.Dummy(new Vector2(0f, 10f * scale));

        DrawImportPanel(width);
        ImGui.Dummy(new Vector2(0f, 10f * scale));

        DrawAutomationPanel(width);
        ImGui.Dummy(new Vector2(0f, 12f * scale));

        Theme.SectionHeader($"Saved ({store.Count})");
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        if (store.Count == 0)
        {
            DrawEmptyState();
            return;
        }

        var sortLabels = new[] { "A-Z", "Newest" };
        var sortWidth = EchoSegment.Width(sortLabels);
        var rowHeight = 28f * scale;

        UiHelpers.SearchField(
            "##outfitsearch", "Search outfits...", ref outfitSearch,
            MathF.Max(120f * scale, width - sortWidth - gap), rowHeight, plugin.Fonts.Icon);

        ImGui.SameLine(0f, gap);

        var sort = EchoSegment.Draw(
            "##outfitsort", sortLabels, outfitsByDate ? 1 : 0, rowHeight,
            ["Sorted by name.", "Sorted by when they were saved."]);

        outfitsByDate = sort == 1;

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        var visible = store.All.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(outfitSearch))
            visible = visible.Where(o => o.Name.Contains(outfitSearch, StringComparison.OrdinalIgnoreCase));

        var list = outfitsByDate
            ? visible.OrderByDescending(o => o.SavedAtUtc).ToList()
            : visible.ToList();

        if (list.Count == 0)
        {
            ImGui.TextColored(Theme.TextDim, "No outfit matches that.");
            return;
        }

        if (ImGui.BeginChild(
                "##outfitlist", ImGui.GetContentRegionAvail(), false,
                ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            foreach (var outfit in list)
                DrawOutfitRow(outfit);
        }

        ImGui.EndChild();
    }

    /// Saving the current look.
    private void DrawSavePanel(float outerWidth)
    {
        var scale = UiHelpers.Scale;
        var store = plugin.Outfits;

        Theme.BeginPanel(outerWidth);

        var width = outerWidth - (28f * scale);

        Theme.SectionHeader("Save what you are wearing", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        if (store.ReadOnly)
        {
            ImGui.TextColored(Theme.Bad, "outfits.json could not be read, so nothing will be saved.");
            ImGui.TextColored(Theme.TextDim, "Move or delete the file and reload the plugin.");
            ImGui.Dummy(new Vector2(0f, 6f * scale));
        }

        var trimmed = outfitName.Trim();
        var replacing = trimmed.Length > 0 && store.Exists(trimmed);
        var ready = trimmed.Length > 0 && Wardrobe.Active && !store.ReadOnly;
        var label = replacing ? "Replace" : "Save";
        var saveIcon = replacing ? FontAwesomeIcon.Sync : FontAwesomeIcon.Save;

        var saveWidth = EchoButton.ContentSize(label, plugin.Fonts.Icon, saveIcon).X;
        var gap = 8f * scale;
        var height = 28f * scale;

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, height / 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12f * scale, 6f * scale));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Tinted(0.04f));

        ImGui.SetNextItemWidth(MathF.Max(120f * scale, width - saveWidth - gap));
        ImGui.InputTextWithHint("##outfitname", "Name this outfit...", ref outfitName, 60);

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);

        ImGui.SameLine(0f, gap);

        if (EchoButton.Draw(
                "##outfitsave", label, new Vector2(saveWidth, height),
                iconFont: plugin.Fonts.Icon,
                icon: saveIcon,
                enabled: ready,
                tooltip: !Wardrobe.Active
                    ? "There is nothing on to save yet."
                    : replacing
                        ? "An outfit with this name already exists and will be overwritten."
                        : null,
                primary: ready))
        {
            store.Save(trimmed, Wardrobe.Current, saveAppearance ? appearance.Draft : null);
            outfitName = string.Empty;
        }

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        EchoToggle.Draw("##saveappearance", "Include Appearance", ref saveAppearance,
            "Saves your face, hair and colours with the gear. Off means the outfit only changes what " +
            "you are wearing and leaves your character's own look alone.");

        Theme.EndPanel();
    }

    /// Bringing a design over from Glamourer.
    private void DrawImportPanel(float outerWidth)
    {
        var scale = UiHelpers.Scale;
        var store = plugin.Outfits;

        Theme.BeginPanel(outerWidth);

        var width = outerWidth - (28f * scale);
        var height = 28f * scale;
        var gap = 8f * scale;

        Theme.SectionHeader("Glamourer", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        if (pendingImport is null)
        {
            ImGui.TextColored(Theme.TextDim, "Move looks between EchoGlam and Glamourer.");
            ImGui.Dummy(new Vector2(0f, 8f * scale));

            var pasteWidth = EchoButton.ContentSize("Paste Design", plugin.Fonts.Icon, FontAwesomeIcon.Paste).X;
            var copyWidth = EchoButton.ContentSize("Copy Design", plugin.Fonts.Icon, FontAwesomeIcon.Copy).X;

            if (EchoButton.Draw(
                    "##glampaste", "Paste Design", new Vector2(pasteWidth, height),
                    iconFont: plugin.Fonts.Icon,
                    icon: FontAwesomeIcon.Paste,
                    enabled: plugin.Items.Ready && !store.ReadOnly,
                    tooltip: !plugin.Items.Ready
                        ? "The item list is still loading."
                        : "Reads the design on your clipboard."))
            {
                PasteGlamourerDesign();
            }

            ImGui.SameLine(0f, gap);

            if (EchoButton.Draw(
                    "##glamcopy", "Copy Design", new Vector2(copyWidth, height),
                    iconFont: plugin.Fonts.Icon,
                    icon: FontAwesomeIcon.Copy,
                    enabled: Wardrobe.Active,
                    tooltip: Wardrobe.Active
                        ? "Copies what you are wearing, for Glamourer."
                        : "There is nothing on to copy yet."))
            {
                CopyAsGlamourerDesign();
            }

            if (importError.Length > 0)
            {
                ImGui.Dummy(new Vector2(0f, 6f * scale));
                ImGui.TextColored(Theme.Bad, importError);
            }
            else if (exportNotice.Length > 0)
            {
                ImGui.Dummy(new Vector2(0f, 6f * scale));
                ImGui.TextColored(Theme.TextDim, exportNotice);
            }

            Theme.EndPanel();
            return;
        }

        var pieces = pendingImport.Pieces;
        var emptied = pendingImport.Look.Count - pieces;

        ImGui.TextColored(
            pieces == 0 ? Theme.Bad : Theme.TextDim,
            pieces == 0
                ? "No gear in this design could be matched."
                : pendingImport.Appearance is not null
                    ? $"{pieces} pieces, with appearance."
                    : $"{pieces} pieces.");

        if (emptied > 0)
            ImGui.TextColored(Theme.TextDim, $"{emptied} empty slots. Wearing it takes those off.");

        foreach (var note in pendingImport.Notes)
            ImGui.TextColored(Theme.TextDim, note);

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        var trimmed = importName.Trim();
        var replacing = trimmed.Length > 0 && store.Exists(trimmed);
        var label = replacing ? "Replace" : "Add Outfit";
        var addIcon = replacing ? FontAwesomeIcon.Sync : FontAwesomeIcon.Plus;

        var addWidth = EchoButton.ContentSize(label, plugin.Fonts.Icon, addIcon).X;
        var discardWidth = EchoButton.ContentSize("Discard", plugin.Fonts.Icon, FontAwesomeIcon.Times).X;
        var ready = trimmed.Length > 0 && pieces > 0 && !store.ReadOnly;

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, height / 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12f * scale, 6f * scale));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Tinted(0.04f));

        ImGui.SetNextItemWidth(MathF.Max(
            60f * scale, width - addWidth - discardWidth - (gap * 2f)));
        ImGui.InputTextWithHint("##glamimportname", "Name this outfit...", ref importName, 60);

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);

        ImGui.SameLine(0f, gap);

        if (EchoButton.Draw(
                "##glamimportsave", label, new Vector2(addWidth, height),
                iconFont: plugin.Fonts.Icon,
                icon: addIcon,
                enabled: ready,
                tooltip: replacing
                    ? "An outfit with this name already exists and will be overwritten."
                    : null,
                primary: ready))
        {
            store.Save(trimmed, pendingImport.Look, pendingImport.Appearance);
            pendingImport = null;
            importName = string.Empty;
        }

        ImGui.SameLine(0f, gap);

        if (EchoButton.Draw(
                "##glamimportdiscard", "Discard", new Vector2(discardWidth, height),
                iconFont: plugin.Fonts.Icon,
                icon: FontAwesomeIcon.Times,
                enabled: true))
        {
            pendingImport = null;
            importName = string.Empty;
        }

        Theme.EndPanel();
    }

    /// Reads whatever is on the clipboard as a Glamourer design.
    private void PasteGlamourerDesign()
    {
        importError = string.Empty;
        exportNotice = string.Empty;

        string clipboard;

        try
        {
            clipboard = ImGui.GetClipboardText();
        }
        catch (Exception)
        {
            importError = "The clipboard could not be read. Try again.";
            return;
        }

        var look = GlamourerDesign.Read(
            clipboard,
            plugin.Items,
            appearance.Draft ?? Wardrobe.CurrentAppearance ?? Wardrobe.BaseAppearance,
            out var error);

        if (look is null)
        {
            importError = error;
            return;
        }

        pendingImport = look;
        importName = look.Name;
    }

    /// Puts what the character is wearing on the clipboard as a Glamourer design.
    private void CopyAsGlamourerDesign()
    {
        importError = string.Empty;
        exportNotice = string.Empty;

        var set = appearance.Draft ?? Wardrobe.CurrentAppearance;

        try
        {
            ImGui.SetClipboardText(GlamourerDesign.Write("EchoGlam Look", Wardrobe.Current, set));
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoGlam] Could not write a Glamourer design to the clipboard");
            importError = "The clipboard could not be written to. Try again.";
            return;
        }

        exportNotice = set is not null
            ? "Copied, with appearance. Paste it into Glamourer."
            : "Copied. Paste it into Glamourer.";
    }

    /// The automation panel: the switch, what is happening right now, and the two things that are not rules -
    /// the fallback and the priority order.
    private void DrawAutomationPanel(float outerWidth)
    {
        var scale = UiHelpers.Scale;
        var rules = plugin.Rules;
        var driving = rules.Enabled && plugin.Automation.Claimed is not null && !plugin.Automation.Surrendered;

        Theme.BeginPanel(outerWidth, driving ? Theme.Accent : null);

        var width = outerWidth - (28f * scale);

        Theme.SectionHeader("Automatic outfits", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        if (rules.ReadOnly)
        {
            ImGui.TextColored(Theme.Bad, "outfit-rules.json could not be read, so rules will not be saved.");
            ImGui.Dummy(new Vector2(0f, 6f * scale));
        }

        var enabled = rules.Enabled;

        if (EchoToggle.Draw("##automation", "Dress Me Automatically", ref enabled,
                "Wears an outfit when its conditions are met. Right-click an outfit below to set them."))
        {
            rules.Enabled = enabled;
        }

        if (!enabled)
        {
            ImGui.Dummy(new Vector2(0f, 6f * scale));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextColored(Theme.TextDisabled,
                "Off. Outfits only go on when you put them on.");
            ImGui.PopTextWrapPos();

            Theme.EndPanel();
            return;
        }

        ImGui.Dummy(new Vector2(0f, 10f * scale));
        DrawAutomationStatus(width, driving);
        ImGui.Dummy(new Vector2(0f, 12f * scale));

        var original = rules.Original;
        var height = 26f * scale;

        ImGui.TextColored(Theme.TextDim, "Otherwise wear");
        ImGui.Dummy(new Vector2(0f, 5f * scale));

        if (EchoButton.Draw(
                "##originaloutfit", original ?? "What I Have On", new Vector2(0f, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Undo,
                selected: original is not null,
                tooltip: original is not null
                    ? $"Goes back to \"{original}\" when no rule matches. Right-click to clear it."
                    : "Goes back to the look you had on before a rule dressed you. Click to pick an outfit instead."))
        {
            originalSearch = string.Empty;
            ImGui.OpenPopup(OriginalPopupId);
        }

        if (EchoButton.RightClicked())
            rules.Original = null;

        DrawOriginalPopup();

        if (plugin.Rules.Rules.Count > 1)
        {
            ImGui.SameLine(0f, 8f * scale);

            if (EchoButton.Draw(
                    "##priority", "Priority", new Vector2(0f, height),
                    iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.SortAmountDown,
                    tooltip: "Which rule wins when more than one of them matches at once."))
            {
                priorityOpen = true;
            }
        }

        Theme.EndPanel();
    }

    /// What the rules are doing right now: a lamp, a headline, and where you are.
    private void DrawAutomationStatus(float width, bool driving)
    {
        var scale = UiHelpers.Scale;
        var drawList = ImGui.GetWindowDrawList();

        var here = plugin.Zones.NameFor(Plugin.ClientState.TerritoryType);

        if (plugin.Housing.Current is { } house)
            here = $"{here}, {house.Address}";

        var worn = plugin.Automation.Claimed;
        var surrendered = plugin.Automation.Surrendered;

        string headline;
        string detail;
        Vector4 lamp;

        if (worn is not null && !surrendered)
        {
            headline = worn;
            detail = $"Worn by a rule · {here}";
            lamp = Theme.Accent;
        }
        else if (worn is not null)
        {
            headline = worn;
            detail = $"You changed it by hand, so the rule let go · {here}";
            lamp = Theme.Warning;
        }
        else if (plugin.Rules.Rules.Count == 0)
        {
            headline = "No conditions set";
            detail = "Right-click an outfit below to say when it should go on";
            lamp = Theme.TextDisabled;
        }
        else
        {
            headline = "Nothing matches here";
            detail = here;
            lamp = Theme.TextDisabled;
        }

        var origin = ImGui.GetCursorScreenPos();
        var lampRadius = 4f * scale;
        var textLeft = origin.X + (18f * scale);
        var lineGap = 4f * scale;
        var detailSize = ImGui.GetFontSize() * 0.88f;
        var height = ImGui.GetTextLineHeight() + lineGap + detailSize;

        ImGui.Dummy(new Vector2(width, height));

        var centre = new Vector2(origin.X + (5f * scale), origin.Y + (ImGui.GetTextLineHeight() / 2f));

        if (driving)
            drawList.AddCircleFilled(centre, lampRadius * 2.2f, ImGui.GetColorU32(lamp with { W = 0.18f }), 20);

        drawList.AddCircleFilled(centre, lampRadius, ImGui.GetColorU32(lamp), 20);

        using (plugin.Fonts.Header.PushSafe())
        {
            drawList.AddText(
                new Vector2(textLeft, origin.Y - (2f * scale)),
                ImGui.GetColorU32(worn is not null ? Theme.Text : Theme.TextDim),
                UiHelpers.Truncate(headline, width - (textLeft - origin.X)));
        }

        drawList.AddText(
            ImGui.GetFont(), detailSize,
            new Vector2(textLeft, origin.Y + ImGui.GetTextLineHeight() + lineGap),
            ImGui.GetColorU32(Theme.TextDim),
            UiHelpers.Truncate(detail, width - (textLeft - origin.X)));
    }

    /// The saved list with nothing in it.
    private void DrawEmptyState()
    {
        var scale = UiHelpers.Scale;
        var drawList = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;
        var height = 92f * scale;
        var origin = ImGui.GetCursorScreenPos();

        ImGui.Dummy(new Vector2(width, height));

        var min = origin;
        var max = origin + new Vector2(width, height);

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Tinted(0.04f)), Theme.CardRounding);
        drawList.AddRect(
            min, max, ImGui.GetColorU32(Theme.Border), Theme.CardRounding, ImDrawFlags.None, 1.2f * scale);

        using (plugin.Fonts.Icon.PushSafe())
        {
            UiHelpers.DrawScaledIcon(
                drawList, FontAwesomeIcon.Bookmark,
                new Vector2(min.X + (width / 2f), min.Y + (height / 2f) - (14f * scale)),
                ImGui.GetColorU32(Theme.TextDisabled));
        }

        var label = Wardrobe.Active
            ? "Name what you are wearing above to save it"
            : "Put a look together, then save it here";

        var measured = ImGui.CalcTextSize(label);

        drawList.AddText(
            new Vector2(min.X + ((width - measured.X) / 2f), min.Y + (height / 2f) + (10f * scale)),
            ImGui.GetColorU32(Theme.TextDim),
            label);
    }

    private const string OriginalPopupId = "##echoglamoriginal";

    /// How many outfits before the fallback popup grows a search box.
    private const int SearchOutfitsAbove = 8;

    /// Rows of outfits the popup shows before it scrolls.
    private const int OriginalPopupRows = 8;

    private void DrawOriginalPopup()
    {
        if (!ImGui.BeginPopup(OriginalPopupId))
            return;

        var scale = UiHelpers.Scale;
        var rowHeight = 24f * scale;
        var width = 200f * scale;
        var all = plugin.Outfits.All;

        if (EchoButton.Draw(
                "##originalnone", "What I Have On", new Vector2(width, rowHeight),
                selected: plugin.Rules.Original is null,
                tooltip: "Goes back to the look you had on before a rule dressed you."))
        {
            plugin.Rules.Original = null;
            ImGui.CloseCurrentPopup();
        }

        if (all.Count == 0)
        {
            ImGui.EndPopup();
            return;
        }

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        var searching = all.Count > SearchOutfitsAbove;

        if (searching)
        {
            ImGui.SetNextItemWidth(width);
            ImGui.InputTextWithHint("##originalsearch", "Search outfits...", ref originalSearch, 60);
            ImGui.Dummy(new Vector2(0f, 4f * scale));
        }

        var matches = string.IsNullOrWhiteSpace(originalSearch)
            ? all
            : [.. all.Where(o => o.Name.Contains(originalSearch, StringComparison.OrdinalIgnoreCase))];

        if (matches.Count == 0)
        {
            ImGui.TextColored(Theme.TextDim, "Nothing matches that.");
            ImGui.EndPopup();
            return;
        }

        var rows = Math.Min(matches.Count, OriginalPopupRows);
        var listHeight = (rows * (rowHeight + ImGui.GetStyle().ItemSpacing.Y)) + (2f * scale);

        var scrolls = matches.Count > OriginalPopupRows;
        var childWidth = width + (scrolls ? ImGui.GetStyle().ScrollbarSize : 0f);

        if (ImGui.BeginChild("##originallist", new Vector2(childWidth, listHeight), false))
        {
            foreach (var outfit in matches)
            {
                if (EchoButton.Draw(
                        $"##original{outfit.Name}", outfit.Name, new Vector2(width, rowHeight),
                        selected: string.Equals(plugin.Rules.Original, outfit.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    plugin.Rules.Original = outfit.Name;
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.EndChild();
        ImGui.EndPopup();
    }

    /// One saved outfit.
    private void DrawOutfitRow(Outfit outfit)
    {
        var scale = UiHelpers.Scale;
        var rowHeight = 50f * scale;
        var gap = 6f * scale;
        var iconWidth = 26f * scale;
        var padX = 12f * scale;

        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var drawList = ImGui.GetWindowDrawList();

        var armed = pendingDelete == outfit.Name;
        var ruleCount = plugin.Rules.CountFor(outfit.Name);

        var auto = plugin.Rules.Enabled
                   && string.Equals(plugin.Automation.Claimed, outfit.Name, StringComparison.OrdinalIgnoreCase);

        ImGui.InvisibleButton($"##row{outfit.Name}", new Vector2(width, rowHeight), ImGuiButtonFlags.MouseButtonLeft);

        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        ImGui.SetItemAllowOverlap();

        if (!armed && ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            rulesFor = outfit.Name;
            pendingDelete = null;
        }

        var min = origin;
        var max = origin + new Vector2(width, rowHeight);
        var rounding = 8f * scale;

        var fill = armed
            ? Theme.Bad with { W = 0.14f }
            : hovered
                ? Theme.Tinted(0.13f)
                : auto
                    ? Theme.Tinted(0.07f)
                    : Theme.Tinted(0.035f);

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(fill), rounding);

        if (hovered || armed || auto)
        {
            var edge = armed ? Theme.Bad with { W = 0.5f } : auto ? Theme.Accent with { W = 0.5f } : Theme.Border;
            drawList.AddRect(min, max, ImGui.GetColorU32(edge), rounding, ImDrawFlags.None, 1.2f * scale);
        }

        if (auto && !armed)
        {
            drawList.AddRectFilled(
                min, new Vector2(min.X + (3f * scale), max.Y),
                ImGui.GetColorU32(Theme.Accent), rounding, ImDrawFlags.RoundCornersLeft);
        }

        var buttonY = origin.Y + ((rowHeight - (26f * scale)) / 2f);

        var overButton = false;

        var x = origin.X + width - iconWidth - padX;

        if (armed)
        {
            ImGui.SetCursorScreenPos(new Vector2(x, buttonY + (1f * scale)));
            if (EchoButton.BareIcon(
                    $"##cancel{outfit.Name}", plugin.Fonts.Icon, FontAwesomeIcon.Times, iconWidth,
                    "Keep it."))
                pendingDelete = null;

            overButton |= ImGui.IsItemHovered();
            x -= iconWidth + gap;

            ImGui.SetCursorScreenPos(new Vector2(x, buttonY + (1f * scale)));
            if (EchoButton.BareIcon(
                    $"##confirm{outfit.Name}", plugin.Fonts.Icon, FontAwesomeIcon.Check, iconWidth,
                    "Delete it for good.", colourOverride: Theme.Bad))
            {
                plugin.Outfits.Delete(outfit.Name);

                plugin.Rules.Forget(outfit.Name);
                pendingDelete = null;
            }

            overButton |= ImGui.IsItemHovered();
        }
        else
        {
            if (hovered)
            {
                ImGui.SetCursorScreenPos(new Vector2(x, buttonY + (1f * scale)));
                if (EchoButton.BareIcon(
                        $"##del{outfit.Name}", plugin.Fonts.Icon, FontAwesomeIcon.TrashAlt, iconWidth,
                        "Delete this outfit.", colourOverride: Theme.Bad))
                    pendingDelete = outfit.Name;

                overButton |= ImGui.IsItemHovered();
            }

            x -= gap;

            var wearWidth = EchoButton.ContentSize("Wear").X + (10f * scale);
            x -= wearWidth;

            ImGui.SetCursorScreenPos(new Vector2(x, buttonY));

            if (EchoButton.Draw(
                    $"##wear{outfit.Name}", "Wear", new Vector2(wearWidth, 26f * scale),
                    tooltip: "Put this outfit on.",
                    primary: hovered))
                WearOutfit(outfit);

            overButton |= ImGui.IsItemHovered();
        }

        var labelWidth = MathF.Max(30f * scale, x - origin.X - gap - padX);
        var lineGap = 3f * scale;
        var detailSize = ImGui.GetFontSize() * 0.85f;
        var blockHeight = ImGui.GetTextLineHeight() + lineGap + detailSize;
        var textTop = origin.Y + ((rowHeight - blockHeight) / 2f);

        drawList.AddText(
            new Vector2(origin.X + padX, textTop),
            ImGui.GetColorU32(armed ? Theme.Bad : Theme.Text),
            UiHelpers.Truncate(armed ? $"Delete \"{outfit.Name}\"?" : outfit.Name, labelWidth));

        var detail = armed
            ? "This cannot be undone"
            : Summarise(outfit, ruleCount);

        drawList.AddText(
            ImGui.GetFont(), detailSize,
            new Vector2(origin.X + padX, textTop + ImGui.GetTextLineHeight() + lineGap),
            ImGui.GetColorU32(armed ? Theme.Bad with { W = 0.75f } : Theme.TextDim),
            UiHelpers.Truncate(detail, labelWidth));

        if (hovered && !overButton && !armed)
        {
            UiHelpers.WrappedTooltip(
                $"Saved {outfit.SavedAtUtc.ToLocalTime():d MMM yyyy, HH:mm}"
                + "\n\nRight-click to set when this wears itself.");
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + rowHeight + (6f * scale)));
    }

    /// The second line of an outfit's row: what it is, in words.
    private static string Summarise(Outfit outfit, int ruleCount)
    {
        var parts = new List<string>
        {
            $"{outfit.Slots.Count} slot{(outfit.Slots.Count == 1 ? "" : "s")}",
        };

        if (outfit.HasAppearance)
            parts.Add("with appearance");

        if (ruleCount > 0)
            parts.Add($"{ruleCount} condition{(ruleCount == 1 ? "" : "s")}");

        if (outfit.HasAnimation)
            parts.Add(outfit.AnimationName ?? "plays an animation");

        return string.Join("  ·  ", parts);
    }


    /// One outfit's conditions: when it puts itself on.
    private void DrawOutfitConditions(string name)
    {
        var scale = UiHelpers.Scale;

        if (SubViewHeader($"When to wear \"{name}\""))
        {
            rulesFor = null;
            return;
        }

        if (!plugin.Outfits.Exists(name))
        {
            rulesFor = null;
            return;
        }

        var width = ImGui.GetContentRegionAvail().X;

        DrawConditionsPanel(name, width);
        ImGui.Dummy(new Vector2(0f, 10f * scale));

        DrawAnimationPanel(name, width);
    }

    /// The rules on one outfit, and the three ways to add another.
    private void DrawConditionsPanel(string name, float outerWidth)
    {
        var scale = UiHelpers.Scale;
        var rules = plugin.Rules;

        Theme.BeginPanel(outerWidth);

        var width = outerWidth - (28f * scale);

        Theme.SectionHeader("Conditions", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        if (!rules.Enabled)
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextColored(Theme.Warning, "Automatic outfits are switched off, so none of these fire yet.");
            ImGui.PopTextWrapPos();
            ImGui.Dummy(new Vector2(0f, 8f * scale));
        }

        var mine = rules.For(name).ToList();

        if (mine.Count == 0)
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextColored(Theme.TextDim, "None yet. This outfit only goes on when you put it on.");
            ImGui.PopTextWrapPos();
        }
        else
        {
            foreach (var rule in mine)
            {
                if (DrawRuleRow(rule, rules.IndexOf(rule), width, showOutfit: false, showOrder: false))
                    break;
            }

            if (mine.Count > 1)
            {
                ImGui.Dummy(new Vector2(0f, 4f * scale));
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
                ImGui.TextColored(Theme.TextDim, "When more than one matches, the highest priority wins.");
                ImGui.PopTextWrapPos();
            }
        }

        ImGui.Dummy(new Vector2(0f, 12f * scale));

        var gap = 6f * scale;
        var height = 30f * scale;
        var each = MathF.Max(80f * scale, (width - (gap * 2f)) / 3f);
        var blocked = rules.ReadOnly;

        if (EchoButton.Draw(
                "##addzone", "Map Or Duty", new Vector2(each, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.MapMarkerAlt,
                enabled: plugin.Zones.Ready && !blocked,
                tooltip: plugin.Zones.Ready
                    ? "One place you pick - a city, a zone, a particular dungeon, or your own house."
                    : "The game's map list could not be read."))
        {
            var rule = new OutfitRule { Outfit = name, Trigger = OutfitTrigger.Zone };
            rules.Add(rule);
            pickingPlaceFor = rule;
            zoneSearch = string.Empty;
        }

        ImGui.SameLine(0f, gap);

        if (EchoButton.Draw(
                "##addduty", "Any Duty", new Vector2(each, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Dungeon,
                enabled: !blocked,
                tooltip: "Any instanced content at all, rather than one you name."))
        {
            rules.Add(new OutfitRule { Outfit = name, Trigger = OutfitTrigger.Duty });
        }

        ImGui.SameLine(0f, gap);

        if (EchoButton.Draw(
                "##addcombat", "In Combat", new Vector2(each, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.FistRaised,
                enabled: !blocked,
                tooltip: "While you are fighting, anywhere. It waits a few seconds before changing "
                    + "back, so it cannot swap between two pulls."))
        {
            rules.Add(new OutfitRule { Outfit = name, Trigger = OutfitTrigger.Combat });
        }

        Theme.EndPanel();
    }

    /// Every rule from every outfit, in the order they are tried.
    private void DrawPriority()
    {
        var scale = UiHelpers.Scale;

        if (SubViewHeader("Priority"))
        {
            priorityOpen = false;
            return;
        }

        var outerWidth = ImGui.GetContentRegionAvail().X;

        Theme.BeginPanel(outerWidth);

        var width = outerWidth - (28f * scale);

        Theme.SectionHeader("Tried from the top", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextColored(Theme.TextDim,
            "The first one that matches is what you wear, so being in a duty and in combat at once is "
            + "decided here.");
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0f, 10f * scale));

        var rules = plugin.Rules.Rules.ToList();

        if (rules.Count == 0)
        {
            ImGui.TextColored(Theme.TextDim, "No rules yet.");
            Theme.EndPanel();
            return;
        }

        for (var i = 0; i < rules.Count; i++)
        {
            if (DrawRuleRow(rules[i], i, width, showOutfit: true, showOrder: true))
                break;
        }

        Theme.EndPanel();
    }

    /// A Back button and a title, shared by the sub-views.
    private bool SubViewHeader(string title)
    {
        var scale = UiHelpers.Scale;

        var back = EchoButton.Draw(
            "##rulesback", "Back", new Vector2(0f, 26f * scale),
            iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.ChevronLeft);

        ImGui.SameLine(0f, 10f * scale);
        ImGui.AlignTextToFramePadding();

        using (plugin.Fonts.Header.PushSafe())
            ImGui.TextUnformatted(UiHelpers.Truncate(title, ImGui.GetContentRegionAvail().X));

        ImGui.Dummy(new Vector2(0f, 12f * scale));

        return back;
    }

    /// One condition, as a card.
    private bool DrawRuleRow(OutfitRule rule, int id, float width, bool showOutfit, bool showOrder)
    {
        var scale = UiHelpers.Scale;
        var rowHeight = 46f * scale;
        var gap = 6f * scale;
        var iconWidth = 24f * scale;
        var padX = 10f * scale;
        var badge = 26f * scale;

        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var active = ReferenceEquals(plugin.Automation.ActiveRule, rule) && plugin.Rules.Enabled;

        ImGui.InvisibleButton($"##rulerow{id}", new Vector2(width, rowHeight), ImGuiButtonFlags.MouseButtonLeft);

        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        ImGui.SetItemAllowOverlap();

        var min = origin;
        var max = origin + new Vector2(width, rowHeight);
        var rounding = 8f * scale;

        var fill = hovered
            ? Theme.Tinted(0.13f)
            : active
                ? Theme.Tinted(0.07f)
                : Theme.Tinted(0.035f);

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(fill), rounding);

        if (hovered || active)
        {
            drawList.AddRect(
                min, max, ImGui.GetColorU32(active ? Theme.Accent with { W = 0.5f } : Theme.Border),
                rounding, ImDrawFlags.None, 1.2f * scale);
        }

        if (active)
        {
            drawList.AddRectFilled(
                min, new Vector2(min.X + (3f * scale), max.Y),
                ImGui.GetColorU32(Theme.Accent), rounding, ImDrawFlags.RoundCornersLeft);
        }

        var buttonY = origin.Y + ((rowHeight - iconWidth) / 2f);
        var x = origin.X + width - iconWidth - padX;

        if (hovered)
        {
            ImGui.SetCursorScreenPos(new Vector2(x, buttonY));

            if (EchoButton.BareIcon(
                    $"##ruledel{id}", plugin.Fonts.Icon, FontAwesomeIcon.TrashAlt, iconWidth,
                    "Remove this condition.", colourOverride: Theme.Bad))
            {
                plugin.Rules.Remove(rule);
                return true;
            }
        }

        x -= iconWidth + gap;

        ImGui.SetCursorScreenPos(new Vector2(x, buttonY));

        if (EchoButton.BareIcon(
                $"##ruleon{id}", plugin.Fonts.Icon,
                rule.Enabled ? FontAwesomeIcon.ToggleOn : FontAwesomeIcon.ToggleOff, iconWidth,
                rule.Enabled ? "On. Click to stop it firing." : "Off. Click to switch it back on.",
                colourOverride: rule.Enabled ? Theme.Accent : Theme.TextDisabled))
        {
            plugin.Rules.Toggle(rule, !rule.Enabled);
        }

        x -= iconWidth + gap;

        if (showOrder)
        {
            ImGui.SetCursorScreenPos(new Vector2(x, buttonY));
            if (EchoButton.BareIcon(
                    $"##ruledown{id}", plugin.Fonts.Icon, FontAwesomeIcon.ChevronDown,
                    iconWidth, "Lower priority."))
            {
                plugin.Rules.Move(rule, 1);
                return true;
            }

            x -= iconWidth + gap;

            ImGui.SetCursorScreenPos(new Vector2(x, buttonY));
            if (EchoButton.BareIcon(
                    $"##ruleup{id}", plugin.Fonts.Icon, FontAwesomeIcon.ChevronUp,
                    iconWidth, "Higher priority."))
            {
                plugin.Rules.Move(rule, -1);
                return true;
            }

            x -= iconWidth + gap;
        }

        if (rule.Trigger == OutfitTrigger.Zone)
        {
            var changeWidth = EchoButton.ContentSize("Change").X + (10f * scale);
            x -= changeWidth - iconWidth;

            ImGui.SetCursorScreenPos(new Vector2(x, origin.Y + ((rowHeight - (24f * scale)) / 2f)));

            if (EchoButton.Draw(
                    $"##rulezone{id}", "Change", new Vector2(changeWidth, 24f * scale),
                    tooltip: "Pick a different map or duty."))
            {
                pickingPlaceFor = rule;
                zoneSearch = string.Empty;
                return true;
            }

            x -= gap;
        }

        var plateMin = new Vector2(origin.X + padX, origin.Y + ((rowHeight - badge) / 2f));
        var plateMax = plateMin + new Vector2(badge, badge);
        var tint = rule.Enabled ? Theme.Accent : Theme.TextDisabled;

        drawList.AddRectFilled(plateMin, plateMax, ImGui.GetColorU32(tint with { W = 0.14f }), 7f * scale);

        using (plugin.Fonts.Icon.PushSafe())
        {
            UiHelpers.DrawScaledIcon(
                drawList,
                rule.Trigger switch
                {
                    OutfitTrigger.Zone => rule.HouseId != 0 ? FontAwesomeIcon.Home : FontAwesomeIcon.MapMarkerAlt,
                    OutfitTrigger.Duty => FontAwesomeIcon.Dungeon,
                    _ => FontAwesomeIcon.FistRaised,
                },
                plateMin + new Vector2(badge / 2f, badge / 2f),
                ImGui.GetColorU32(rule.Enabled ? tint : Theme.TextDisabled));
        }

        var textLeft = plateMax.X + (10f * scale);
        var labelWidth = MathF.Max(30f * scale, x - textLeft - gap);
        var (headline, detail) = Describe(rule);

        if (showOutfit)
            detail = $"{detail}  ·  {rule.Outfit}";

        var lineGap = 3f * scale;
        var detailSize = ImGui.GetFontSize() * 0.85f;
        var blockHeight = ImGui.GetTextLineHeight() + lineGap + detailSize;
        var textTop = origin.Y + ((rowHeight - blockHeight) / 2f);

        drawList.AddText(
            new Vector2(textLeft, textTop),
            ImGui.GetColorU32(rule.Enabled ? Theme.Text : Theme.TextDisabled),
            UiHelpers.Truncate(headline, labelWidth));

        drawList.AddText(
            ImGui.GetFont(), detailSize,
            new Vector2(textLeft, textTop + ImGui.GetTextLineHeight() + lineGap),
            ImGui.GetColorU32(rule.Enabled ? Theme.TextDim : Theme.TextDisabled),
            UiHelpers.Truncate(rule.Enabled ? detail : $"{detail}  ·  off", labelWidth));

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + rowHeight + (6f * scale)));
        return false;
    }

    /// A rule as two lines: where, and what kind of where.
    private (string Headline, string Detail) Describe(OutfitRule rule)
    {
        switch (rule.Trigger)
        {
            case OutfitTrigger.Zone when rule.TerritoryId == 0:
                return ("Nowhere picked yet", "Choose a map or duty");

            case OutfitTrigger.Zone:
                var place = plugin.Zones.NameFor(rule.TerritoryId);

                if (rule.HouseId != 0)
                    return (place, $"This house  ·  {rule.HouseAddress ?? "one address"}");

                return (place, plugin.Zones.IsDuty(rule.TerritoryId) ? "A duty" : "Anywhere on this map");

            case OutfitTrigger.Duty:
                return ("Any duty", "Dungeons, trials and raids");

            case OutfitTrigger.Combat:
                return ("In combat", "Anywhere, while you are fighting");

            default:
                return ("Unknown", string.Empty);
        }
    }
    /// The animation played when this outfit goes on and comes back off.
    private void DrawAnimationPanel(string name, float outerWidth)
    {
        var scale = UiHelpers.Scale;

        var outfit = plugin.Outfits.All.FirstOrDefault(o =>
            string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

        if (outfit is null)
            return;

        Theme.BeginPanel(outerWidth);

        var width = outerWidth - (28f * scale);

        Theme.SectionHeader("Animation", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        var label = outfit.HasAnimation
            ? outfit.AnimationName ?? $"Animation {outfit.AnimationId}"
            : "Nothing";

        if (EchoButton.Draw(
                "##pickanim", label, new Vector2(0f, 26f * scale),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Running,
                selected: outfit.HasAnimation,
                enabled: plugin.Animations.Ready && !plugin.Outfits.ReadOnly,
                tooltip: plugin.Animations.Ready
                    ? "Plays once, every time this outfit goes on. Right-click to clear it."
                    : "The game's animation list could not be read."))
        {
            pickingAnimationFor = name;
            animationSearch = string.Empty;
        }

        if (EchoButton.RightClicked())
            plugin.Outfits.SetAnimation(name, 0, null);

        if (outfit.HasAnimation)
        {
            ImGui.SameLine(0f, 6f * scale);

            if (EchoButton.Draw(
                    "##testanim", "Test", new Vector2(0f, 26f * scale),
                    iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Play,
                    tooltip: "Plays it now, without changing what you are wearing."))
            {
                plugin.Animations.Request(outfit.AnimationId);
            }
        }

        if (outfit.HasAnimation)
        {
            ImGui.Dummy(new Vector2(0f, 8f * scale));

            var first = outfit.ChangeAfterAnimation;

            if (EchoToggle.Draw(
                    "##animatefirst", "Change After The Animation", ref first,
                    "On, the animation plays and you are already wearing the outfit when it ends - a "
                    + "transformation. Off, the outfit changes straight away and the animation follows "
                    + "it. On delays the change by however long the animation runs."
                    + "\n\nEither way it plays again when a rule takes the outfit back off."))
            {
                plugin.Outfits.SetAnimateFirst(name, first);
            }

            if (outfit.ChangeAfterAnimation)
            {
                ImGui.Dummy(new Vector2(0f, 6f * scale));

                var hold = outfit.HoldSeconds;
                var sliderHeight = 22f * scale;

                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, sliderHeight / 2f);
                ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, sliderHeight / 2f);
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f * scale, 3f * scale));
                ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Tinted(0.05f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.Tinted(0.09f));
                ImGui.PushStyleColor(ImGuiCol.SliderGrab, Theme.Accent);
                ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Theme.AccentHover);

                ImGui.SetNextItemWidth(180f * scale);

                if (ImGui.SliderFloat("##animhold", ref hold, 0.2f, 5f, "Wait %.1fs"))
                    plugin.Outfits.SetAnimationHold(name, hold);

                ImGui.PopStyleColor(4);
                ImGui.PopStyleVar(3);

                if (ImGui.IsItemHovered())
                {
                    UiHelpers.WrappedTooltip(
                        "How long the animation gets before the clothes change. It waits longer if the "
                        + "animation is still running, never less than this.");
                }
            }
        }

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextColored(Theme.TextDim,
            "Only you see this, the same as the glamour itself. The game cuts it short if you are "
            + "moving or fighting.");
        ImGui.PopTextWrapPos();

        Theme.EndPanel();
    }

    /// Choosing the animation, from every emote and player action in the game.
    private void DrawAnimationPicker(string name)
    {
        var scale = UiHelpers.Scale;

        if (SubViewHeader($"Animation for \"{name}\""))
        {
            pickingAnimationFor = null;
            return;
        }

        var outfit = plugin.Outfits.All.FirstOrDefault(o =>
            string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

        if (outfit is null)
        {
            pickingAnimationFor = null;
            return;
        }

        var filterWidth = EchoButton.ContentSize("Emotes").X;
        var gap = 8f * scale;

        ImGui.SetNextItemWidth(MathF.Max(120f * scale, ImGui.GetContentRegionAvail().X - filterWidth - gap));
        ImGui.InputTextWithHint("##animsearch", "Search emotes and actions...", ref animationSearch, 60);

        ImGui.SameLine(0f, gap);

        var filterLabel = animationFilter switch
        {
            AnimationSource.Emote => "Emotes",
            AnimationSource.Action => "Actions",
            _ => "Both",
        };

        if (EchoButton.Draw(
                "##animfilter", filterLabel, new Vector2(filterWidth, 26f * scale),
                selected: animationFilter is not null,
                tooltip: "Cycles between both, emotes only, and combat and spell animations only."))
        {
            animationFilter = animationFilter switch
            {
                null => AnimationSource.Emote,
                AnimationSource.Emote => AnimationSource.Action,
                _ => null,
            };
        }

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        if (EchoButton.Draw(
                "##animnone", "Nothing", new Vector2(ImGui.GetContentRegionAvail().X, 26f * scale),
                selected: !outfit.HasAnimation,
                tooltip: "The outfit goes on without an animation."))
        {
            plugin.Outfits.SetAnimation(name, 0, null);
            pickingAnimationFor = null;
            return;
        }

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        if (!ImGui.BeginChild("##animlist", ImGui.GetContentRegionAvail(), false))
        {
            ImGui.EndChild();
            return;
        }

        var rowHeight = 26f * scale;

        const int Cap = 200;
        var matches = plugin.Animations.Search(animationSearch, animationFilter).ToList();

        foreach (var entry in matches.Take(Cap))
        {
            var picked = outfit.AnimationId == entry.TimelineId;

            if (EchoButton.Draw(
                    $"##anim{entry.TimelineId}", entry.Name,
                    new Vector2(ImGui.GetContentRegionAvail().X, rowHeight),
                    selected: picked,
                    tooltip: entry.Source == AnimationSource.Emote
                        ? "An emote. Click to use it, and it plays once so you can see it."
                        : "A combat or spell animation. Click to use it, and it plays once so you can see it."))
            {
                plugin.Outfits.SetAnimation(name, entry.TimelineId, entry.Name);

                plugin.Animations.Request(entry.TimelineId);
                break;
            }
        }

        if (matches.Count > Cap)
        {
            ImGui.Dummy(new Vector2(0f, 4f * scale));
            ImGui.TextColored(Theme.TextDim, $"{matches.Count - Cap} more. Search to narrow it down.");
        }
        else if (matches.Count == 0)
        {
            ImGui.TextColored(Theme.TextDim, "Nothing matches that.");
        }

        ImGui.EndChild();
    }

    /// Picking the one place a zone rule fires in.
    private void DrawZonePicker()
    {
        var scale = UiHelpers.Scale;
        var rule = pickingPlaceFor!;

        if (SubViewHeader("Pick a map or duty"))
        {
            if (rule.TerritoryId == 0)
                plugin.Rules.Remove(rule);

            pickingPlaceFor = null;
            return;
        }

        var filterWidth = EchoButton.ContentSize("Open World").X;
        var gap = 8f * scale;

        ImGui.SetNextItemWidth(MathF.Max(120f * scale, ImGui.GetContentRegionAvail().X - filterWidth - gap));
        ImGui.InputTextWithHint("##zonesearch", "Search maps and duties...", ref zoneSearch, 60);

        ImGui.SameLine(0f, gap);

        var filterLabel = zoneDutyFilter switch
        {
            true => "Duties",
            false => "Open World",
            _ => "Everywhere",
        };

        if (EchoButton.Draw(
                "##zonefilter", filterLabel, new Vector2(filterWidth, 26f * scale),
                selected: zoneDutyFilter is not null,
                tooltip: "Cycles between everywhere, duties only, and open world only."))
        {
            zoneDutyFilter = zoneDutyFilter switch
            {
                null => true,
                true => false,
                false => null,
            };
        }

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        var here = Plugin.ClientState.TerritoryType;

        if (here != 0)
        {
            if (plugin.Housing.Current is { } house)
            {
                if (EchoButton.Draw(
                        "##zonehouse", $"This House — {house.Address}",
                        new Vector2(ImGui.GetContentRegionAvail().X, 26f * scale),
                        iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Home,
                        primary: true,
                        tooltip: "Only this address. Other houses of the same kind will not match."))
                {
                    rule.TerritoryId = here;
                    rule.HouseId = house.Id;
                    rule.HouseAddress = house.Address;
                    plugin.Rules.Save();
                    pickingPlaceFor = null;
                    return;
                }

                ImGui.Dummy(new Vector2(0f, 4f * scale));
            }

            var label = plugin.Housing.Current is null
                ? $"Here — {plugin.Zones.NameFor(here)}"
                : $"Any {plugin.Zones.NameFor(here)}";

            if (EchoButton.Draw(
                    "##zonehere", label,
                    new Vector2(ImGui.GetContentRegionAvail().X, 26f * scale),
                    iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.MapPin,
                    tooltip: plugin.Housing.Current is null
                        ? "The map you are standing in."
                        : "Every house of this kind, anyone's - not just this address."))
            {
                rule.TerritoryId = here;
                rule.HouseId = 0;
                rule.HouseAddress = null;
                plugin.Rules.Save();
                pickingPlaceFor = null;
                return;
            }

            ImGui.Dummy(new Vector2(0f, 6f * scale));
        }

        if (!ImGui.BeginChild("##zonelist", ImGui.GetContentRegionAvail(), false))
        {
            ImGui.EndChild();
            return;
        }

        var rowHeight = 24f * scale;

        const int Cap = 200;
        var matches = plugin.Zones.Search(zoneSearch, zoneDutyFilter).ToList();

        foreach (var zone in matches.Take(Cap))
        {
            if (EchoButton.Draw(
                    $"##zone{zone.TerritoryId}", zone.Label,
                    new Vector2(ImGui.GetContentRegionAvail().X, rowHeight),
                    selected: rule.TerritoryId == zone.TerritoryId,
                    tooltip: zone.IsDuty ? "A duty." : "Open world."))
            {
                rule.TerritoryId = zone.TerritoryId;

                rule.HouseId = 0;
                rule.HouseAddress = null;

                plugin.Rules.Save();
                pickingPlaceFor = null;
                break;
            }
        }

        if (matches.Count > Cap)
        {
            ImGui.Dummy(new Vector2(0f, 4f * scale));
            ImGui.TextColored(Theme.TextDim, $"{matches.Count - Cap} more. Search to narrow it down.");
        }
        else if (matches.Count == 0)
        {
            ImGui.TextColored(Theme.TextDim, "Nothing matches that.");
        }

        ImGui.EndChild();
    }

    private void WearOutfit(Outfit outfit)
    {
        if (Build.Diagnostics)
        {
            Plugin.Log.Information(
                $"[EchoGlam] Wear \"{outfit.Name}\": animation={outfit.AnimationId} "
                + $"first={outfit.ChangeAfterAnimation} hold={outfit.HoldSeconds:0.0}s");
        }

        if (outfit.HasAnimation && outfit.ChangeAfterAnimation)
        {
            plugin.Animations.PlayThen(outfit.AnimationId, outfit.HoldSeconds, () => PutOn(outfit));
            return;
        }

        PutOn(outfit);
        plugin.Animations.Request(outfit.AnimationId);
    }

    private void PutOn(Outfit outfit)
    {
        Wardrobe.Wear(outfit.ToLook());

        if (outfit.ToAppearance() is { } set)
        {
            Wardrobe.SetAppearance(set);
            appearance.Invalidate();
        }
    }

    private void DrawBuilding()
    {
        var scale = UiHelpers.Scale;
        ImGui.Dummy(new Vector2(0f, 40f * scale));
        Theme.SectionHeader("Reading the game's item list");
        ImGui.Dummy(new Vector2(0f, 6f * scale));
        ImGui.TextColored(Theme.TextDim,
            "Every wearable item in the game, straight from its own data. This happens once.");
    }

    private void DrawToolbar()
    {
        var scale = UiHelpers.Scale;
        var buttonHeight = 26f * scale;

        if (EchoButton.Draw(
                "##revertall", "Revert All", new Vector2(0f, buttonHeight),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.UndoAlt,
                enabled: Wardrobe.Active,
                tooltip: "Puts your real gear and appearance back."))
        {
            Wardrobe.RevertAll();
            appearance.Invalidate();
        }

        ImGui.SameLine(0f, 8f * scale);

        if (EchoButton.Draw(
                "##copyworn", "Load Current", new Vector2(0f, buttonHeight),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.ArrowDown,
                tooltip: "Fills the slots with what you are wearing right now, glamour plates and " +
                         "dyes included, so you can edit from there."))
        {
            var current = Wardrobe.CaptureWorn();

            if (current.Count > 0)
                Wardrobe.Wear(current);
        }

        ImGui.SameLine(0f, 10f * scale);

        var worn = Wardrobe.Current.Count;
        ImGui.AlignTextToFramePadding();

        if (Wardrobe.Active)
        {
            ImGui.TextColored(
                Theme.Accent,
                worn == 1 ? "1 slot glamoured" : $"{worn} slots glamoured");
        }
        else
        {
            ImGui.TextColored(Theme.TextDim, "Wearing your own gear.");
        }

        ImGui.SameLine(0f, 0f);
        var note = "Only you see this.";
        var noteWidth = ImGui.CalcTextSize(note).X;
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - noteWidth + ImGui.GetCursorPosX() - ImGui.GetCursorPosX());
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - noteWidth));
        ImGui.TextColored(Theme.TextDisabled, note);

        if (ImGui.IsItemHovered())
            UiHelpers.WrappedTooltip(
                "EchoGlam changes what your own client draws. Other players still see the gear you " +
                "actually have equipped.");
    }

    private void DrawSlotList()
    {
        var scale = UiHelpers.Scale;
        Theme.SectionHeader("Slots");
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        foreach (var slot in GlamSlots.All)
            DrawSlotRow(slot);

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        DrawPaneRow(
            RightPane.Outfits, FontAwesomeIcon.Bookmark, "Outfits",
            plugin.Outfits.Count == 0 ? "None saved" : $"{plugin.Outfits.Count} saved",
            highlight: false);

        DrawPaneRow(
            RightPane.Appearance, FontAwesomeIcon.User, "Appearance",
            Wardrobe.HasAppearanceOverride ? "Changed" : "—",
            highlight: Wardrobe.HasAppearanceOverride);
    }

    /// A row under the slots that switches the right pane.
    private void DrawPaneRow(RightPane target, FontAwesomeIcon icon, string label, string detail, bool highlight)
    {
        var scale = UiHelpers.Scale;
        var iconSize = 30f * scale;
        var rowHeight = iconSize + (8f * scale);

        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var drawList = ImGui.GetWindowDrawList();

        var selected = pane == target;

        if (ImGui.InvisibleButton($"##pane{target}", new Vector2(width, rowHeight), ImGuiButtonFlags.MouseButtonLeft))
        {
            pane = target;

            pendingDelete = null;
        }

        var hovered = ImGui.IsItemHovered();

        if (selected || hovered)
        {
            drawList.AddRectFilled(
                origin, origin + new Vector2(width, rowHeight),
                ImGui.GetColorU32(Theme.Tinted(selected ? 0.24f : 0.12f)), 6f * scale);
        }

        if (selected)
        {
            drawList.AddRectFilled(
                origin, origin + new Vector2(3f * scale, rowHeight),
                ImGui.GetColorU32(Theme.Accent), 1.5f * scale);
        }

        var iconCentre = origin + new Vector2((10f * scale) + (iconSize / 2f), rowHeight / 2f);
        using (plugin.Fonts.Icon.PushSafe())
        {
            UiHelpers.DrawScaledIcon(
                drawList, icon, iconCentre, ImGui.GetColorU32(highlight ? Theme.Accent : Theme.TextDim));
        }

        var textX = origin.X + (10f * scale) + iconSize + (8f * scale);

        drawList.AddText(
            new Vector2(textX, origin.Y + (4f * scale)),
            ImGui.GetColorU32(highlight ? Theme.Accent : Theme.TextDim), label);

        drawList.AddText(
            new Vector2(textX, origin.Y + (4f * scale) + ImGui.GetTextLineHeight()),
            ImGui.GetColorU32(highlight ? Theme.Text : Theme.TextDisabled), detail);
    }

    private void DrawSlotRow(GlamSlot slot)
    {
        var scale = UiHelpers.Scale;
        var iconSize = 30f * scale;
        var rowHeight = iconSize + (8f * scale);

        var entry = Wardrobe.EntryFor(slot);
        var item = entry is { } e ? Items.Resolve(e.ItemId) : GlamItem.None;
        var overridden = entry is not null;

        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var drawList = ImGui.GetWindowDrawList();

        var clicked = ImGui.InvisibleButton($"##slot{slot}", new Vector2(width, rowHeight), ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();
        var selected = slot == selectedSlot;

        if (clicked)
        {
            selectedSlot = slot;
            search = string.Empty;
            pane = RightPane.Items;
            pendingDelete = null;
        }

        if (hovered && overridden && !item.IsNone && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            Wardrobe.Set(slot, 0);

        if (hovered && overridden)
        {
            UiHelpers.WrappedTooltip(item.IsNone
                ? "Hidden\n\nSelect the slot and press Restore to show your real gear again."
                : $"{item.Name}\n\nRight-click to take it off.");
        }

        if (selected || hovered)
        {
            drawList.AddRectFilled(
                origin, origin + new Vector2(width, rowHeight),
                ImGui.GetColorU32(Theme.Tinted(selected ? 0.24f : 0.12f)), 6f * scale);
        }

        if (selected)
        {
            drawList.AddRectFilled(
                origin, origin + new Vector2(3f * scale, rowHeight),
                ImGui.GetColorU32(Theme.Accent), 1.5f * scale);
        }

        var iconMin = origin + new Vector2(10f * scale, 4f * scale);
        DrawItemIcon(drawList, item, iconMin, iconSize);

        var textX = iconMin.X + iconSize + (8f * scale);
        var labelColour = overridden ? Theme.Accent : Theme.TextDim;

        drawList.AddText(
            new Vector2(textX, origin.Y + (4f * scale)), ImGui.GetColorU32(labelColour), slot.Label());

        var name = overridden ? (item.IsNone ? "Hidden" : item.Name) : "—";
        drawList.AddText(
            new Vector2(textX, origin.Y + (4f * scale) + ImGui.GetTextLineHeight()),
            ImGui.GetColorU32(overridden ? Theme.Text : Theme.TextDisabled),
            UiHelpers.Truncate(name, width - (textX - origin.X) - (8f * scale)));
    }

    private void DrawPicker()
    {
        var scale = UiHelpers.Scale;

        Theme.SectionHeader(selectedSlot.Label());
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        var dyed = Wardrobe.EntryFor(selectedSlot);
        if (dyeOpenFor == selectedSlot && (dyed is null || !Items.Resolve(dyed.Value.ItemId).IsDyeable))
            CloseDye();

        var showingDye = dyeOpenFor == selectedSlot;

        matches = Matches(out hiddenBySource);

        DrawSlotActions(showingDye);
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        if (showingDye)
        {
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputTextWithHint("##dyesearch", "Search dyes...", ref dyeSearch, 40);
            ImGui.Dummy(new Vector2(0f, 6f * scale));
        }
        else
        {
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputTextWithHint("##search", "Search this slot...", ref search, 100);

            if (selectedSlot.IsWeapon())
            {
                ImGui.Dummy(new Vector2(0f, 4f * scale));
                ImGui.TextColored(Theme.TextDim, "Your current job's weapons only. Changing job clears this slot.");
            }

            ImGui.Dummy(new Vector2(0f, 6f * scale));
        }

        if (ImGui.BeginChild("##pickerscroll", ImGui.GetContentRegionAvail(), false))
        {
            if (showingDye)
            {
                DrawDyeGrid();
            }
            else
            {
                if (restoreItemScroll is { } scroll)
                {
                    ImGui.SetScrollY(scroll);
                    restoreItemScroll = null;
                }

                DrawItemGrid();

                itemScroll = ImGui.GetScrollY();
            }
        }

        ImGui.EndChild();
    }

    /// Where the item list was left, and the position to put it back to on the frame after the dye grid
    /// closes.
    private float itemScroll;

    private float? restoreItemScroll;

    /// Opens the dye grid for a slot, keeping the item list's place.
    private void OpenDye(GlamSlot slot)
    {
        restoreItemScroll = null;
        dyeOpenFor = slot;
    }

    /// Closes the dye grid and asks for the item list's place back.
    private void CloseDye()
    {
        dyeOpenFor = null;
        restoreItemScroll = itemScroll;
    }

    /// showingDye - Whether the dye grid is what is below this row.
    private void DrawSlotActions(bool showingDye)
    {
        var scale = UiHelpers.Scale;
        var height = 24f * scale;

        var entry = Wardrobe.EntryFor(selectedSlot);
        var item = entry is { } e ? Items.Resolve(e.ItemId) : GlamItem.None;

        var hidden = entry is { ItemId: 0 };
        var restores = Wardrobe.HiddenBefore(selectedSlot);
        var restoresName = restores is { } r ? Items.Resolve(r.ItemId).Name : null;

        if (EchoButton.Draw("##hide", "Hide", new Vector2(0f, height),
                selected: hidden,
                tooltip: !hidden
                    ? "Show nothing in this slot."
                    : restoresName is not null
                        ? $"Put {restoresName} back."
                        : "Show your real gear again."))
        {
            if (!hidden)
            {
                Wardrobe.Set(selectedSlot, 0);
            }
            else if (!Wardrobe.Unhide(selectedSlot))
            {
                Wardrobe.Clear(selectedSlot);
            }
        }

        ImGui.SameLine(0f, 6f * scale);

        if (EchoButton.Draw("##restore", "Restore", new Vector2(0f, height),
                enabled: entry is not null,
                tooltip: "Hand this slot back to the game and show your real gear."))
            Wardrobe.Clear(selectedSlot);

        if (entry is not null && item.IsDyeable)
        {
            ImGui.SameLine(0f, 6f * scale);
            var open = dyeOpenFor == selectedSlot;

            if (EchoButton.Draw("##dye", open ? "Back to items" : "Dye", new Vector2(0f, height), selected: open))
            {
                if (open)
                    CloseDye();
                else
                    OpenDye(selectedSlot);
            }
        }

        if (showingDye)
            return;

        if (!selectedSlot.IsWeapon())
            DrawJobFilter(height);

        DrawAvailabilityFilter(height);
        DrawOwnedFilter(height);
    }

    private const string AvailabilityPopupId = "##echoglamavailability";

    /// Hides gear that cannot simply be gone and got.
    private void DrawAvailabilityFilter(float rowHeight)
    {
        var scale = UiHelpers.Scale;
        ImGui.SameLine(0f, 8f * scale);

        var on = hideStore || hideSeasonal || hideExclusive;

        var label = on ? $"Sources ({hiddenBySource})" : "Sources";

        if (EchoButton.Draw(
                "##availabilityfilter", label,
                new Vector2(0f, rowHeight),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Tags,
                selected: on,
                tooltip: !on
                    ? "Hide store, seasonal or exclusive items."
                    : $"Hiding {hiddenBySource} "
                      + (hiddenBySource == 1 ? "item" : "items")
                      + " in this slot: "
                      + string.Join(", ",
                        new[]
                        {
                            hideStore ? "store" : null,
                            hideSeasonal ? "seasonal" : null,
                            hideExclusive ? "exclusive" : null,
                        }.Where(part => part is not null))
                      + ". Click to change, right-click to clear."))
            ImGui.OpenPopup(AvailabilityPopupId);

        if (EchoButton.RightClicked())
            hideStore = hideSeasonal = hideExclusive = false;

        DrawAvailabilityPopup();
    }

    private void DrawAvailabilityPopup()
    {
        if (!ImGui.BeginPopup(AvailabilityPopupId))
            return;

        var scale = UiHelpers.Scale;
        var width = 260f * scale;

        Theme.SectionHeader("Hide by source", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        EchoToggle.Draw(
            "##hidestore", "Hide store items", ref hideStore,
            "Gear sold on the online store.");

        EchoToggle.Draw(
            "##hideseasonal", "Hide seasonal items", ref hideSeasonal,
            "Gear from seasonal events, which is only obtainable while the event is running.");

        EchoToggle.Draw(
            "##hideexclusive", "Hide exclusive items", ref hideExclusive,
            "Promotional, collaboration and pre-order gear.");

        ImGui.Dummy(new Vector2(0f, 6f * scale));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);

        ImGui.TextColored(
            Theme.TextDisabled,
            "Read from the game's own store and armoire listings, so the odd piece may not be marked.");

        ImGui.PopTextWrapPos();
        ImGui.EndPopup();
    }

    /// Narrows the picker to gear this character can actually get at.
    private void DrawOwnedFilter(float rowHeight)
    {
        var scale = UiHelpers.Scale;
        ImGui.SameLine(0f, 8f * scale);

        var owned = plugin.Owned;

        if (EchoButton.Draw(
                "##ownedfilter", "Owned", new Vector2(0f, rowHeight),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.BoxOpen,
                selected: ownedOnly,
                tooltip: OwnedTooltip(owned)))
            ownedOnly = !ownedOnly;

        if (EchoButton.RightClicked())
            ownedOnly = false;
    }

    private static string OwnedTooltip(OwnedItems owned)
    {
        var sources = owned.Sources;

        if (sources.Count == 0)
        {
            return "Show only gear you can get at.\n\nNothing scanned yet - it fills in as you play, "
                 + "and reads your retainers, saddlebag, armoire and glamour dresser as you open them.";
        }

        var lines = new List<string>
        {
            $"Showing only gear you can get at - {owned.Count} items.",
            string.Empty,
            "Read from:",
        };

        foreach (var source in sources)
            lines.Add($"  {source.Label} - {Ago(source.SeenUtc)}");

        lines.Add(string.Empty);
        lines.Add("Retainers, the saddlebag, the armoire and the dresser are only readable while open, "
                + "so anything missing is a container EchoGlam has not seen yet. Open it once and it is "
                + "remembered.");
        lines.Add(string.Empty);
        lines.Add("Right-click to clear the filter.");

        return string.Join("\n", lines);
    }

    /// How long ago, in the roughest terms that are still useful.
    private static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;

        if (span < TimeSpan.FromMinutes(2)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} minutes ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} hours ago";

        return span < TimeSpan.FromDays(2) ? "yesterday" : $"{(int)span.TotalDays} days ago";
    }

    private const string JobPopupId = "##echoglamjobs";

    /// The job filter, as one button that opens a picker.
    private void DrawJobFilter(float rowHeight)
    {
        var scale = UiHelpers.Scale;
        ImGui.SameLine(0f, 12f * scale);

        var job = selectedJob is { } id ? JobList.Find(id) : null;
        var label = job is { } j ? j.Abbreviation : "All jobs";

        if (EchoButton.Draw(
                "##jobfilter", label, new Vector2(0f, rowHeight),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Filter,
                selected: selectedJob is not null,
                tooltip: job is { } sel
                    ? $"Showing only gear {sel.Name} can wear. Click to change, right-click to clear."
                    : "Show only gear a particular job can wear."))
            ImGui.OpenPopup(JobPopupId);

        if (EchoButton.RightClicked())
            selectedJob = null;

        DrawJobPopup();
    }

    private void DrawJobPopup()
    {
        if (!ImGui.BeginPopup(JobPopupId))
            return;

        var scale = UiHelpers.Scale;
        var icon = 30f * scale;
        var gap = 4f * scale;

        var width = (icon * JobList.LargestGroupSize) + (gap * (JobList.LargestGroupSize - 1));

        Theme.SectionHeader("Filter by job", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        if (EchoButton.Draw(
                "##joball", "All jobs", new Vector2(width, 24f * scale),
                selected: selectedJob is null))
        {
            selectedJob = null;
            ImGui.CloseCurrentPopup();
        }

        var drawList = ImGui.GetWindowDrawList();

        foreach (var group in JobList.Groups)
        {
            ImGui.Dummy(new Vector2(0f, 8f * scale));
            ImGui.TextColored(Theme.TextDim, JobList.GroupLabel(group));
            ImGui.Dummy(new Vector2(0f, 2f * scale));

            var first = true;

            foreach (var job in JobList.InGroup(group))
            {
                if (!first)
                    ImGui.SameLine(0f, gap);

                first = false;
                DrawJobButton(drawList, job, icon);
            }
        }

        ImGui.Dummy(new Vector2(0f, 4f * scale));
        ImGui.EndPopup();
    }

    private void DrawJobButton(ImDrawListPtr drawList, JobInfo job, float size)
    {
        var scale = UiHelpers.Scale;
        var origin = ImGui.GetCursorScreenPos();
        var selected = selectedJob == job.Id;

        var clicked = ImGui.InvisibleButton($"##job{job.Id}", new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();

        if (clicked)
        {
            selectedJob = job.Id;
            ImGui.CloseCurrentPopup();
        }

        if (hovered)
            UiHelpers.WrappedTooltip($"{job.Name} ({job.Abbreviation})");

        if (selected || hovered)
        {
            drawList.AddRectFilled(
                origin, origin + new Vector2(size, size),
                ImGui.GetColorU32(Theme.Tinted(selected ? 0.32f : 0.16f)), 5f * scale);
        }

        var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(job.IconId)).GetWrapOrDefault();
        if (texture != null)
        {
            var inset = 2f * scale;
            drawList.AddImage(
                texture.Handle, origin + new Vector2(inset, inset),
                origin + new Vector2(size - inset, size - inset), Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, selected ? 1f : hovered ? 0.9f : 0.6f)));
        }

        if (selected)
        {
            drawList.AddRect(
                origin, origin + new Vector2(size, size), ImGui.GetColorU32(Theme.Accent),
                5f * scale, ImDrawFlags.None, 2f * scale);
        }
    }

    /// The item grid, clipped to what is on screen.
    private void DrawItemGrid()
    {
        var scale = UiHelpers.Scale;
        var cell = CellDesign * scale;
        var gap = CellGapDesign * scale;
        var iconSize = CellIconDesign * scale;

        if (matches.Count == 0)
        {
            var reason = search.Length > 0
                ? "Nothing here matches that search."
                : ownedOnly && !plugin.Owned.Ready
                    ? "Nothing scanned yet. Your inventory and armoury are read automatically; open a "
                      + "retainer, your saddlebag, the armoire or the glamour dresser once and their "
                      + "contents are remembered."
                    : ownedOnly
                        ? "Nothing you can get at fits this slot. Anything in a retainer or container "
                          + "EchoGlam has not seen yet is missing until you open it once."
                        : selectedJob is not null
                            ? "Nothing in this slot is equippable by that job."
                            : hideStore || hideSeasonal || hideExclusive
                                ? "Everything in this slot is hidden by the source filter."
                                : "Nothing your character can wear fits this slot.";

            ImGui.TextColored(Theme.TextDim, reason);
            return;
        }

        var available = ImGui.GetContentRegionAvail().X;
        var columns = Math.Max(1, (int)((available + gap) / (cell + gap)));
        var rows = (matches.Count + columns - 1) / columns;

        var entry = Wardrobe.EntryFor(selectedSlot);
        var currentId = entry?.ItemId ?? uint.MaxValue;

        var drawList = ImGui.GetWindowDrawList();

        var rowHeight = cell + ImGui.GetTextLineHeight() + gap;

        var clipper = new ImGuiListClipper();
        clipper.Begin(rows, rowHeight);

        while (clipper.Step())
        {
            for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var index = (row * columns) + column;
                    if (index >= matches.Count)
                        break;

                    if (column > 0)
                        ImGui.SameLine(0f, gap);

                    DrawItemCell(drawList, matches[index], cell, iconSize, currentId);
                }
            }
        }

        clipper.End();
    }

    private void DrawItemCell(ImDrawListPtr drawList, GlamItem item, float cell, float iconSize, uint currentId)
    {
        var scale = UiHelpers.Scale;
        var origin = ImGui.GetCursorScreenPos();

        var selected = item.ItemId == currentId;

        ImGui.BeginGroup();

        var clicked = ImGui.InvisibleButton($"##item{item.ItemId}", new Vector2(cell, cell), ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();

        if (clicked)
        {
            var (stain0, stain1) = StainsFor(item);
            Wardrobe.Set(selectedSlot, item.ItemId, stain0, stain1);
        }

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            var (stain0, stain1) = StainsFor(item);
            Wardrobe.Set(selectedSlot, item.ItemId, stain0, stain1);

            if (item.IsDyeable)
                OpenDye(selectedSlot);
        }

        if (hovered)
        {
            UiHelpers.WrappedTooltip(item.IsDyeable
                ? $"{item.Name}\n\nRight-click to wear it and pick its dyes."
                : item.Name);
        }

        var body = selected ? Theme.Tinted(0.30f) : hovered ? Theme.Tinted(0.16f) : Theme.Panel;
        drawList.AddRectFilled(origin, origin + new Vector2(cell, cell), ImGui.GetColorU32(body), 6f * scale);

        var inset = (cell - iconSize) / 2f;
        DrawItemIcon(drawList, item, origin + new Vector2(inset, inset), iconSize);

        if (selected)
        {
            drawList.AddRect(
                origin, origin + new Vector2(cell, cell), ImGui.GetColorU32(Theme.Accent),
                6f * scale, ImDrawFlags.None, 2f * scale);
        }

        var label = UiHelpers.Truncate(item.Name, cell);
        drawList.AddText(
            new Vector2(origin.X, origin.Y + cell + (2f * scale)),
            ImGui.GetColorU32(selected ? Theme.Accent : Theme.TextDim),
            label);

        ImGui.Dummy(new Vector2(cell, ImGui.GetTextLineHeight()));
        ImGui.EndGroup();
    }

    private void DrawDyeGrid()
    {
        var scale = UiHelpers.Scale;
        var swatch = 28f * scale;
        var gap = 5f * scale;

        var entry = Wardrobe.EntryFor(selectedSlot);
        if (entry is not { } current)
            return;

        var item = Items.Resolve(current.ItemId);
        var drawList = ImGui.GetWindowDrawList();

        var filter = dyeSearch.Trim();
        var filtered = filter.Length > 0;

        for (var channel = 0; channel < Math.Max(1, (int)item.DyeChannels); channel++)
        {
            Theme.SectionHeader(item.DyeChannels > 1 ? $"Dye channel {channel + 1}" : "Dye");
            ImGui.Dummy(new Vector2(0f, 4f * scale));

            var active = channel == 0 ? current.Stain0 : current.Stain1;
            var available = ImGui.GetContentRegionAvail().X;
            var perRow = Math.Max(1, (int)((available + gap) / (swatch + gap)));
            var column = 0;

            var shown = 0;

            foreach (var dye in Dyes.Palette)
            {
                if (filtered
                    && dye.Id != active
                    && !dye.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (column > 0 && column % perRow != 0)
                    ImGui.SameLine(0f, gap);

                var origin = ImGui.GetCursorScreenPos();
                var clicked = ImGui.InvisibleButton($"##dye{channel}_{dye.Id}", new Vector2(swatch, swatch), ImGuiButtonFlags.MouseButtonLeft);
                var hovered = ImGui.IsItemHovered();

                if (clicked)
                {
                    if (channel == 0)
                        Wardrobe.SetDye(selectedSlot, dye.Id, current.Stain1);
                    else
                        Wardrobe.SetDye(selectedSlot, current.Stain0, dye.Id);

                    if (channel == 0)
                        stickyStain0 = dye.Id;
                    else
                        stickyStain1 = dye.Id;
                }

                if (hovered)
                    UiHelpers.WrappedTooltip(dye.Name);

                drawList.AddRectFilled(
                    origin, origin + new Vector2(swatch, swatch), ImGui.GetColorU32(dye.Colour), 4f * scale);

                var ring = dye.Id == active
                    ? Theme.Accent
                    : new Vector4(1f, 1f, 1f, hovered ? 0.55f : 0.15f);

                drawList.AddRect(
                    origin, origin + new Vector2(swatch, swatch), ImGui.GetColorU32(ring),
                    4f * scale, ImDrawFlags.None, (dye.Id == active ? 2.5f : 1f) * scale);

                column++;
                shown++;
            }

            if (shown == 0)
                ImGui.TextColored(Theme.TextDim, $"No dye matches \"{filter}\".");

            ImGui.Dummy(new Vector2(0f, 10f * scale));
        }
    }

    /// Kept across opens rather than cleared each time.
    private string dyeSearch = string.Empty;

    private void DrawItemIcon(ImDrawListPtr drawList, GlamItem item, Vector2 min, float size, float alpha = 1f)
    {
        var max = min + new Vector2(size, size);

        if (item.IsNone || item.IconId == 0)
        {
            drawList.AddRect(
                min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f * alpha)),
                4f * UiHelpers.Scale, ImDrawFlags.None, 1f * UiHelpers.Scale);
            return;
        }

        var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(item.IconId)).GetWrapOrDefault();
        if (texture == null)
            return;

        drawList.AddImage(
            texture.Handle, min, max, Vector2.Zero, Vector2.One,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha)));
    }

    /// Items in the selected slot that this character could actually wear, narrowed by the job filter and the
    /// search box.
    private List<GlamItem> Matches(out int hidden)
    {
        IEnumerable<GlamItem> items = Items.ForSlot(selectedSlot);

        items = items.Where(plugin.CanWear);

        if (selectedSlot.IsWeapon())
        {
            var current = Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0;

            if (current != 0)
                items = items.Where(i => i.UsableBy(current));
        }
        else if (selectedJob is { } job)
        {
            items = items.Where(i => i.UsableBy(job));
        }

        if (ownedOnly)
            items = items.Where(i => i.IsNone || plugin.Owned.Contains(i.ItemId));

        if (!string.IsNullOrWhiteSpace(search))
            items = items.Where(i => i.Name.Contains(search, StringComparison.OrdinalIgnoreCase));

        var everything = items.ToList();

        if (!hideStore && !hideSeasonal && !hideExclusive)
        {
            hidden = 0;
            return everything;
        }

        var kept = everything
            .Where(i => i.IsNone
                || ((!hideStore || !i.IsStore)
                    && (!hideSeasonal || !i.IsSeasonal)
                    && (!hideExclusive || !i.IsExclusive)))
            .ToList();

        hidden = everything.Count - kept.Count;

        return kept;
    }
}
