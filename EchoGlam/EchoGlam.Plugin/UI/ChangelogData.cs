namespace EchoGlam.UI;

/// One group of lines inside a release - Added, Changed, Fixed, Removed, Notes.
public sealed record ChangelogGroup(string Heading, string[] Lines);

/// One release's worth of user-facing notes, shown in Settings > Changelog.
public sealed record ChangelogEntry(string Version, ChangelogGroup[] Groups);

public static class ChangelogData
{
    /// Nothing goes in here for a development build: the unread dot means "there is something here you have
    /// not read", and lighting it up for a build that was never published teaches people to ignore it.
    public static readonly ChangelogEntry[] Entries =
    [
        new("2.0.0.1",
        [
            new ChangelogGroup("Added",
            [
                "Right-click a dye to make it a favourite. Your favourites sit together at the top "
                + "of the dye list instead of among the rest, on both channels.",
            ]),
            new ChangelogGroup("Fixed",
            [
                "The cross at the end of a search box did not clear what you had typed.",
                "Headings and labels set in spaced capitals had uneven gaps between their letters, "
                + "which made some letters look a different size from their neighbours.",
                "The EchoGlam wordmark was slightly blurred.",
                "The camera mark on your profile picture was too large for its circle and sat "
                + "left of the middle.",
            ]),
        ]),

        new("2.0.0.0",
        [
            new ChangelogGroup("Added",
            [
                "EchoGlam has been redesigned from the ground up. The tabs along the top are gone - "
                + "every destination is listed down the left, and the one you are on is lit.",
                "The Dressing Room is a character sheet. Your twelve slots are tiles showing what is "
                + "in each one and the dyes on it, and the whole column folds away when you want the "
                + "room for the item list.",
                "A banner above the item list names the piece in the slot you are working on, its "
                + "market price and whether you own it. Point at anything in the list for the same.",
                "Appearance tells you what you have changed: a count at the top, a dot on each part "
                + "holding a change, and a mark that puts one setting back.",
                "This month's top ten travel across the top of the Gallery. Point at the strip to "
                + "stop it, or drag it along.",
                "Filters are a column of their own, with every job as an icon.",
                "96 tags in six groups, picked from a list rather than a wall of chips. A glamour "
                + "can carry six.",
                "Your profile opens with a shelf of your best glamours.",
                "Friends whose glamour is on screen move to the top of your friends list while they "
                + "are there.",
                "29 colour themes under Settings > Appearance, in seven sets including seasonal ones.",
                "You can turn a screenshot to any angle while you are framing it.",
            ]),
            new ChangelogGroup("Changed",
            [
                "Appearance is six parts - Face, Eyes, Hair, Skin, Build and Race - one at a time "
                + "across the full width, so most of them fit with nothing to scroll.",
                "Race, clan and gender are picked from named tiles and switches instead of dropdowns.",
                "Outfits is one page. What is dressing you and why sits at the top, there is one box "
                + "to name and keep a look, and every row in your collection leads with the gear "
                + "that look is made of.",
                "Automatic outfits is a single line while it is switched off, and becomes a full "
                + "banner while a rule is actually dressing you.",
                "Saving a look and pasting a Glamourer design are one control now, and the line "
                + "above the box says which of the two Save is about to use.",
                "Before you save, EchoGlam says what is going in - how many slots, whether your "
                + "appearance is coming with it, and how many empty slots the outfit will strip.",
                "The Gallery leads with the photographs. Eight fit on a screen where three did.",
                "A glamour's page is one spread with nothing to scroll, and it folds the picture "
                + "away while you are trying the look on so you can see your character.",
                "Friends is one grid, and a card says which of four things is true about somebody "
                + "rather than only whether their glamour is on your screen.",
                "Settings is a list of categories instead of a row of tabs. Report a Bug is in there.",
                "EchoGlam no longer replaces the game's mouse cursor while its window is open.",
                "Changing page in the Gallery travels back to the top instead of jumping, and stops "
                + "if you touch the wheel.",
            ]),
            new ChangelogGroup("Fixed",
            [
                "The sort switches in Outfits and on your profile snapped back to their first "
                + "option the moment you pressed them.",
                "The Gallery went blank and stayed blank if the server restarted while you were "
                + "browsing, until you reloaded the plugin.",
                "Buttons that only appear when you point at a friend could not be clicked.",
                "The last column of the friends grid sat underneath the scrollbar.",
                "Parts of a glamour's page could be cut off and unclickable.",
                "Changing your profile picture left the old one on your published glamours.",
            ]),
            new ChangelogGroup("Notes",
            [
                "Your outfits, friends, published glamours and settings are all carried over. "
                + "Nothing needs setting up again.",
            ]),
        ]),

        new("1.1.0.4",
        [
            new ChangelogGroup("Fixed",
            [
                "Add EchoGlam Friend no longer appears when you right-click a fashion accessory, or "
                + "anything else that is not a person.",
            ]),
        ]),

        new("1.1.0.3",
        [
            new ChangelogGroup("Added",
            [
                "Bring your Glamourer designs across. Copy one in Glamourer, then press Paste Design "
                + "in the Outfits pane.",
                "Copy Design puts what you are wearing on the clipboard, ready to paste into "
                + "Glamourer.",
            ]),
            new ChangelogGroup("Changed",
            [
                "An imported outfit dresses you from bare, so the slots the design leaves empty are "
                + "emptied rather than left as they were.",
                "A design that hides your headgear or your weapon is imported hidden too.",
                "Glasses, advanced dyes and Penumbra settings cannot come across. EchoGlam tells you "
                + "what a design was carrying before you save it.",
                "Hide now works both ways. Press it again to put back whatever was in the slot.",
            ]),
        ]),

        new("1.1.0.2",
        [
            new ChangelogGroup("Added",
            [
                "The hairstyles you unlock in game now appear in the appearance editor. There are "
                + "about twice as many to choose from as before.",
            ]),
            new ChangelogGroup("Fixed",
            [
                "Nose, jaw, mouth, eyebrows and eye shape were showing pictures of another "
                + "character's hair, and setting the wrong value when you picked one. Both fixed.",
            ]),
            new ChangelogGroup("Notes",
            [
                "An appearance saved before this update may look different when you next wear it, "
                + "if it set one of those five.",
            ]),
        ]),

        new("1.1.0.0",
        [
            new ChangelogGroup("Added",
            [
                "Added Friends. Turn on Glamour Sharing, then right-click somebody in game and "
                + "choose Add EchoGlam Friend.",
                "Friends see the glamours you wear, and you see theirs. Nobody else does, and "
                + "nothing about you is shared until you turn it on.",
                "Outfit changes show on your friends' screens as you make them, along with the "
                + "appearance you are wearing.",
                "An outfit's swap animation plays on their screen too, at the same moment it plays "
                + "on yours.",
                "Hide a friend's glamours without unfriending them, from their row in the Friends "
                + "tab.",
                "/eg opens EchoGlam, the same as /echoglam.",
            ]),
            new ChangelogGroup("Changed",
            [
                "Turning sharing off stops anyone looking you up, and keeps your friends.",
            ]),
        ]),


        new("1.0.1.2",
        [
            new ChangelogGroup("Added",
            [
                "Outfits can now dress you on their own. Right-click one in the Dressing Room to set "
                + "when it goes on: a map, a particular duty, any duty, or in combat.",
                "A map condition can name one house rather than every cottage of that kind, so your "
                + "own place is not the same rule as everyone else's.",
                "An outfit can play an animation when it goes on and when it comes off - any emote, "
                + "or any combat or spell animation in the game. Only you see it.",
            ]),
            new ChangelogGroup("Changed",
            [
                "When nothing matches you go back to whatever you had on before, or to an outfit you "
                + "pick.",
                "Change a piece by hand while a rule is dressing you and it lets go until the "
                + "conditions change.",
                "Redesigned the outfit list. Each one is a card saying what it holds, and the search "
                + "and sort match the Gallery's.",
            ]),
        ]),

        new("1.0.1.0",
        [
            new ChangelogGroup("Added",
            [
                "Editing a glamour now shows the screenshots it already has. Remove one, add "
                + "another, and the rest stay exactly as they were.",
            ]),
            new ChangelogGroup("Fixed",
            [
                "The screenshots on the publish page now fit their panel instead of pushing the rest "
                + "of the page off the window.",
                "A long description no longer runs out of the box when you open a glamour to edit "
                + "it.",
            ]),
        ]),

        new("1.0.0.9",
        [
            new ChangelogGroup("Fixed",
            [
                "Trying on a glamour built for another job no longer puts that job's weapon in your "
                + "hands.",
                "Revert now puts a paired off-hand weapon back straight away.",
            ]),
        ]),

        new("1.0.0.8",
        [
            new ChangelogGroup("Changed",
            [
                "Redesigned the Gallery. The sorts are one switch, the search box has a search "
                + "field, and Publish Outfit sits beside them.",
                "Cards lift as you point at them, and anything published in the last three days is "
                + "marked New.",
                "Redesigned the publish page. It uses two columns where there is room, and tags you "
                + "haven't picked no longer look picked.",
            ]),
            new ChangelogGroup("Fixed",
            [
                "Changing a filter no longer moves the rest of the page around.",
                "The title and author on a glamour page now sit level with the profile picture "
                + "instead of riding up against the top of the panel.",
            ]),
        ]),

        new("1.0.0.7",
        [
            new ChangelogGroup("Added",
            [
                "The glamour page now shows the author's profile picture.",
                "Items in a glamour now show whether they are on the market board, from the store, "
                + "seasonal or exclusive.",
                "Hover the Market label to see the cheapest listing on your world.",
                "Added a Sources filter to the item picker, to hide store, seasonal or exclusive "
                + "gear.",
            ]),
            new ChangelogGroup("Changed",
            [
                "Redesigned the glamour page. The details sit in cards, Try On leads, and the item "
                + "list is a two-column grid.",
                "EchoGlam's window no longer opens at the title screen or character select.",
            ]),
            new ChangelogGroup("Fixed",
            [
                "A published glamour lost the character name and race on it.",
            ]),
        ]),

        new("1.0.0.5",
        [
            new ChangelogGroup("Fixed",
            [
                "Text boxes wrap as you type instead of running off the edge, and Enter still starts "
                + "a new line.",
                "Stepping through a glamour's screenshots slides between them instead of cutting.",
            ]),
        ]),

        new("1.0.0.2",
        [
            new ChangelogGroup("Added",
            [
                "Right-click an item in the picker to wear it and open its dyes.",
                "Added a search box to the dye picker.",
            ]),
            new ChangelogGroup("Changed",
            [
                "Dyes are grouped by colour family, so similar shades sit together.",
                "The dye you pick carries onto the next item you put on, including past pieces that "
                + "cannot be dyed.",
                "Leaving the dye view puts you back where you were in the item list.",
                "The slot name, its buttons, the filters and the search now stay put as you scroll "
                + "the list.",
            ]),
            new ChangelogGroup("Fixed",
            [
                "Paired weapons like a Ninja's daggers now change and hide as a pair, instead of "
                + "only the main hand.",
            ]),
        ]),

        new("1.0.0.1",
        [
            new ChangelogGroup("Added",
            [
                "Added an Owned filter to the item picker, for gear in your inventory, armoury, "
                + "saddlebag, armoire, glamour dresser and retainers.",
            ]),
            new ChangelogGroup("Fixed",
            [
                "Right-clicking a piece in the Dressing Room did not take it off after Load Current.",
            ]),
        ]),
    ];

    /// The newest version with notes.
    public static string LatestVersion => Entries.Length > 0 ? Entries[0].Version : string.Empty;
}
