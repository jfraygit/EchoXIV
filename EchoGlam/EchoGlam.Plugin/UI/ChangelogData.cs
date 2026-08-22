namespace EchoGlam.UI;

/// One release's worth of user-facing highlights, shown in Settings > Changelog.
public sealed record ChangelogEntry(string Version, string[] Highlights);

/// The release notes shown inside the plugin.
public static class ChangelogData
{
    /// Nothing goes in here for a development build: the unread dot means "there is something here you have
    /// not read", and lighting it up for a build that was never published teaches people to ignore it.
    public static readonly ChangelogEntry[] Entries =
    [
        new("1.1.0.3",
        [
            "Bring your Glamourer designs across. Copy one in Glamourer, then press Paste Design in "
            + "the Outfits pane.",
            "Copy Design puts what you are wearing on the clipboard, ready to paste into Glamourer.",
            "An imported outfit dresses you from bare, so the slots the design leaves empty are "
            + "emptied rather than left as they were.",
            "A design that hides your headgear or your weapon is imported hidden too.",
            "Glasses, advanced dyes and Penumbra settings cannot come across. EchoGlam tells you "
            + "what a design was carrying before you save it.",
            "Hide now works both ways. Press it again to put back whatever was in the slot.",
        ]),

        new("1.1.0.2",
        [
            "The hairstyles you unlock in game now appear in the appearance editor. There are about "
            + "twice as many to choose from as before.",
            "Nose, jaw, mouth, eyebrows and eye shape were showing pictures of another character's "
            + "hair, and setting the wrong value when you picked one. Both fixed.",
            "An appearance saved before this update may look different when you next wear it, if it "
            + "set one of those five.",
        ]),

        new("1.1.0.0",
        [
            "Added Friends. Turn on Glamour Sharing, then right-click somebody in game and choose "
            + "Add EchoGlam Friend.",
            "Friends see the glamours you wear, and you see theirs. Nobody else does, and nothing "
            + "about you is shared until you turn it on.",
            "Outfit changes show on your friends' screens as you make them, along with the "
            + "appearance you are wearing.",
            "An outfit's swap animation plays on their screen too, at the same moment it plays on "
            + "yours.",
            "Hide a friend's glamours without unfriending them, from their row in the Friends tab.",
            "Turning sharing off stops anyone looking you up, and keeps your friends.",
            "/eg opens EchoGlam, the same as /echoglam.",
        ]),


        new("1.0.1.2",
        [
            "Outfits can now dress you on their own. Right-click one in the Dressing Room to set when "
            + "it goes on: a map, a particular duty, any duty, or in combat.",
            "A map condition can name one house rather than every cottage of that kind, so your own "
            + "place is not the same rule as everyone else's.",
            "When nothing matches you go back to whatever you had on before, or to an outfit you pick.",
            "Change a piece by hand while a rule is dressing you and it lets go until the conditions "
            + "change.",
            "An outfit can play an animation when it goes on and when it comes off - any emote, or any "
            + "combat or spell animation in the game. Only you see it.",
            "Redesigned the outfit list. Each one is a card saying what it holds, and the search and "
            + "sort match the Gallery's.",
        ]),

        new("1.0.1.0",
        [
            "Editing a glamour now shows the screenshots it already has. Remove one, add another, "
            + "and the rest stay exactly as they were.",
            "The screenshots on the publish page now fit their panel instead of pushing the rest of "
            + "the page off the window.",
            "A long description no longer runs out of the box when you open a glamour to edit it.",
        ]),

        new("1.0.0.9",
        [
            "Trying on a glamour built for another job no longer puts that job's weapon in your "
            + "hands.",
            "Revert now puts a paired off-hand weapon back straight away.",
        ]),

        new("1.0.0.8",
        [
            "Redesigned the Gallery. The sorts are one switch, the search box has a search field, "
            + "and Publish Outfit sits beside them.",
            "Cards lift as you point at them, and anything published in the last three days is "
            + "marked New.",
            "Changing a filter no longer moves the rest of the page around.",
            "Redesigned the publish page. It uses two columns where there is room, and tags you "
            + "haven't picked no longer look picked.",
            "The title and author on a glamour page now sit level with the profile picture instead "
            + "of riding up against the top of the panel.",
        ]),

        new("1.0.0.7",
        [
            "Redesigned the glamour page. The details sit in cards, Try On leads, and the item list "
            + "is a two-column grid.",
            "The glamour page now shows the author's profile picture.",
            "Items in a glamour now show whether they are on the market board, from the store, "
            + "seasonal or exclusive.",
            "Hover the Market label to see the cheapest listing on your world.",
            "Added a Sources filter to the item picker, to hide store, seasonal or exclusive gear.",
            "Fixed a published glamour losing the character name and race on it.",
            "EchoGlam's window no longer opens at the title screen or character select.",
        ]),

        new("1.0.0.5",
        [
            "Text boxes wrap as you type instead of running off the edge, and Enter still starts a new line.",
            "Stepping through a glamour's screenshots slides between them instead of cutting.",
        ]),

        new("1.0.0.2",
        [
            "Paired weapons like a Ninja's daggers now change and hide as a pair, instead of only the main hand.",
            "Dyes are grouped by colour family, so similar shades sit together.",
            "Right-click an item in the picker to wear it and open its dyes.",
            "The dye you pick carries onto the next item you put on, including past pieces that cannot be dyed.",
            "Leaving the dye view puts you back where you were in the item list.",
            "Added a search box to the dye picker.",
            "The slot name, its buttons, the filters and the search now stay put as you scroll the list.",
        ]),

        new("1.0.0.1",
        [
            "Added an Owned filter to the item picker, for gear in your inventory, armoury, "
            + "saddlebag, armoire, glamour dresser and retainers.",
            "Fixed right-clicking a piece in the Dressing Room not taking it off after Load Current.",
        ]),
    ];

    /// The newest version with notes.
    public static string LatestVersion => Entries.Length > 0 ? Entries[0].Version : string.Empty;
}
