namespace EchoSim.UI;

/// One release's worth of user-facing highlights, shown in Settings &gt; Changelog.
public sealed record ChangelogEntry(string Version, string[] Highlights);

/// The release notes shown inside the plugin.
public static class ChangelogData
{
    public static readonly ChangelogEntry[] Entries =
    [
        new("1.0.2.6",
        [
            "Ninja was losing an ability every cycle. A weave was refused whenever it came off cooldown during an animation lock, so the window went unused - worth about 0.7% of the job's damage. Ninja scores on the leaderboards have been rescored against the corrected ceiling.",
        ]),
        new("1.0.2.5",
        [
            "Fixed a stutter caused by EchoSim doing its heavy reading on the drawing thread - building a job's ceiling, and reading the meld list, now happen in the background.",
            "EchoSim now answers to /es as well as /echosim.",
        ]),
        new("1.0.2.3",
        [
            "Leaderboard scores are now checked against the fight they claim to be, not just its length. Three of the eight ranked scores were sitting on the wrong board and have been re-checked.",
            "A phase-split fight no longer disagrees with its own log about how long it took. Your client keeps timing through the intermission after the first boss dies where FFLogs stops at the kill, which made a genuine M12S phase one clear look ten percent away from the log it was being matched to.",
            "EchoSim no longer draws or tracks anything at the title screen or character select.",
        ]),
        new("1.0.2.2",
        [
            "The healer leaderboards no longer carry a note about 100 not being reachable. It sat above the board on every visit, and the explanation behind it is already in the Live Score settings.",
        ]),
        new("1.0.2.1",
        [
            "Your profile now shows every job you have scores on, a section each, instead of only the job selected at the top of the window. Someone with scores on three jobs was shown one of them, and nothing on the page said the other two were there. Jobs you have no scores on are left out.",
            "Profile scores now scroll on their own, so the name, portrait and season heading stay in place as you go down a long profile.",
        ]),
        new("1.0.1.9",
        [
            "The bug report box now wraps as you type instead of running off the edge.",
        ]),
        new("1.0.1.8",
        [
            "The Timeline tab now shows what happens before the pull. Black Mage hard-casts Fire III four seconds early, so the timeline opened with High Thunder into a Fire IV that no one can actually cast - the simulation was right and the display was missing a line. Every job with a pre-pull step now shows it.",
            "Ninja's opener no longer lists Suiton and Kassatsu as pre-pull actions. Suiton is an attack, so casting it is what starts the fight; only the three mudra presses genuinely happen before the pull, and the notes on that tab are shorter throughout.",
        ]),
        new("1.0.1.7",
        [
            "Black Mage and Dancer were checking whether a cooldown was ready in the middle of an animation lock, so anything coming off cooldown a moment later read as unavailable and the opening was spent on nothing. Every other job had already been corrected; these two were missed.",
            "Warrior now drinks its potion alongside Inner Release, where both guides put it. It had been taking the first opening of the fight instead, spending five seconds of a thirty-second buff on the two weakest attacks in the rotation.",
            "Gunbreaker's potion was landing nine seconds in, six seconds after No Mercy opened, so Gnashing Fang, Jugular Rip and Bow Shock were all spent inside the burst and outside the potion. It now goes up beside No Mercy, where the opener draws it.",
            "Monk now starts a fight holding five Chakra, meditated before the pull as every Monk does. Its first Forbidden Chakra had been arriving on the sixth attack instead of the second, and Perfect Balance was being pushed behind the potion rather than ahead of it.",
            "Black Mage's potion now goes up with Ley Lines after the first Fire IV, and Bard's before Battle Voice rather than after. Both are where the published openers put them, and both had been taking the first free opening of the fight instead.",
            "Scholar was refreshing Biolysis only once it had already run out, so every cycle lost an attack's worth of uptime and a fight finished one application short. It now refreshes just before it lapses, which is what the other three healers already did.",
            "Scholar is compared against a more representative parse. The previous one spent a third of its Aetherflow on healing, which made the distance between it and a damage-only rotation look about twice as large as it really is.",
            "The Opener tab now matches what EchoSim actually simulates for Paladin, Dark Knight, Machinist and Dancer, each of which listed the potion somewhere other than where it is used. Paladin's pre-pull cast is 1.75 seconds rather than 1.5, and Viper's opener now shows the pre-pull Slither.",
        ]),
        new("1.0.1.6",
        [
            "White Mage now starts a fight with three Lilies and three Blood Lilies, a Patch 7.4 change EchoSim had never picked up. Afflatus Misery is free in the opener, and the lily count over a fight matches the reference instead of running three short.",
            "White Mage's opener follows the published order, putting the potion before Dia. A damage-over-time takes its buffs the moment it lands, so that half second decides whether thirty seconds of ticks are medicated. Presence of Mind and Assize are delayed, where both guides put them.",
            "Dia, Biolysis, Combust III and Eukrasian Dosis III were all paid in full on every refresh, including time the version they overwrote had already covered - so refreshing early was free, which is not a choice a real rotation gets. All four now charge for the overlap.",
            "Scholar's opener was missing half its Aetherflow: three Energy Drains where both guides cast six, and no Dissipation at all, so the second set of three had nothing to spend. Baneful Impaction was also pressed five attacks early, ahead of the refills it should follow.",
            "Scholar now refreshes Biolysis early at the end of the opener, which is the point of the opener both guides publish. Clipping it there lines up every later refresh with the end of a buff window rather than the middle of one.",
            "Astrologian now opens holding Lord of Crowns from a card pack drawn before the pull, with Umbral Draw next. Starting from an empty deck delayed it by a full draw cycle and inverted both draw counts against the reference. It now also waits for the two-minute.",
            "Earthly Star now goes down four seconds before the pull, where both guides place it, and is no longer set off the moment it ripens. A star explodes on its own twenty seconds after landing, so pressing the button early only cost it the Divination it belonged in.",
            "Divination and Oracle were spent in the first two seconds, before a single party buff landed. All four uses still came out, so no count looked wrong - the only sign was Oracle dealing a third less damage than it does in the reference.",
            "Sage now presses Eukrasia before the pull, where every guide puts it, so the first cast of the fight is the damage-over-time rather than the button that unlocks it. EchoSim had been spending a combat global on a spell that deals nothing.",
            "Sage now holds both Phlegma III charges for the raid buffs, as all three published openers do - they had been spent on the second and third casts, and Psyche two seconds in. Phlegma was also one use short a fight, because it was only ever cast with both charges banked.",
        ]),
        new("1.0.1.5",
        [
            "Summoner was spending Necrotize the moment it had one. Both guides bank the odd-minute pair for the next two-minute window, landing four under party buffs. Fourteen are cast either way, which is why no count looked wrong.",
            "Energy Drain had been modelled as Scholar's ability of the same name - costing a charge rather than granting two, at half the damage. Summoner's grants two Necrotize and a Ruin IV on a 60-second recast, and that recast is what feeds the burst.",
            "Summoner's opener now follows the published order and opens on Titan, not Ifrit. Searing Flash was pressed third when it belongs last, Energy Drain arrived four attacks late, and one Necrotize went out where both guides cast two.",
            "Ruin IV is now filler between primal phases, where both guides place it. It had been jumping ahead of the gemstone attacks to spend a 520-potency attack in a slot a 620 Ruby Rite wanted. It still leads in multi-target.",
            "Red Mage's opener now casts three long Verspells before the melee combo, as both guides do. It managed two and a Jolt III: three in a row needs Swiftcast to pay for one, and Swiftcast was instead being spent making a 2-second spell instant, which saves nothing.",
            "Acceleration guarantees the Verfire or Verstone it follows, and EchoSim rolled it at the usual 50%. Eight certain procs a fight were treated as four, and each one missed also forces a weaker Jolt III into the slot it would have filled.",
            "Embolden, Manafication, Vice of Thorns and Prefulgence were all spent in the first five seconds. Each still came out at four uses, so nothing looked wrong - but Embolden ran out mid-opener, with the second Grand Impact it exists to buff landing after it.",
            "Prefulgence now waits for Embolden. It comes from Manafication, which returns in 110 seconds against Embolden's 120, so pressing it on sight walks it further out of the buff every cycle.",
            "Pictomancer now uses the 2nd GCD Starry opener both guides publish, putting Starry Muse behind the first attack so its window holds the subtractive spells and Comet in Black. It had been spending those twenty seconds on its three cheapest spells.",
            "Hyperphantasia is spent by any spell cast under Starry Muse, not only the coloured ones. Counting a narrower set, EchoSim had to push those spells ahead of the hammers - which hit twice as hard - to reach the free Rainbow Drip.",
            "Mog of the Ages and Retribution of the Madeen now wait for Starry Muse when it is close, where both guides place them. Madeen was landing outside the burst often enough to deal less over a fight than the smaller Mog.",
        ]),
        new("1.0.1.4",
        [
            "Bard's opener now follows the published order instead of spending every cooldown at the pull. Five abilities were crammed in before the second attack, and Battle Voice and Raging Strikes went up before a single damaging attack had been cast - six seconds of a twenty-second buff, spent on nothing.",
            "Radiant Finale was drifting out of the burst. It returns in 110 seconds against a 120-second song cycle, so pressed on sight it moved earlier each window until it landed before The Wanderer's Minuet - taking Radiant Encore, one of the job's largest attacks, with it. Both now wait for the song.",
            "Empyreal Arrow was losing a use over a fight to a timing fault, and Pitch Perfect was discarding the Repertoire that Empyreal Arrow grants when already at three stacks. Both are fixed, and the second is the rule the guides state outright.",
            "Machinist was losing most of a fight's Reassembles. It decides whether to press one by working out which attack comes next, and it asked too early - so a Drill coming off cooldown a moment later was invisible. One went in at 1.8 seconds and the next not until 107, on a 55-second recast holding two charges.",
            "Machinist's opener now follows the published order as well. Barrel Stabilizer and Reassemble were both being spent at the pull, ahead of the Double Check and Checkmate the opener actually starts with, and Reassemble now always lands on a Drill rather than whatever attack happens to follow it.",
            "Dancer was dancing Standard Step at the pull. Every guide dances it beforehand so the fight opens on the finish, and starting it in combat spent three attacks worth no damage at all - Technical Step went from second in the opener to nearly six seconds in, with the whole burst behind it.",
            "Flourish was going up a second into the fight, handing Fan Dance III and IV to an opener with nothing buffed. It now waits for the Technical Finish window, where the guides put it - after which its 60-second cooldown divides the two-minute burst evenly and stays aligned on its own.",
            "Last Dance now comes before Starfall Dance in Dancer's burst, which is the order both guides print. Between that and the two fixes above, Dancer had been losing a Standard Finish and a Last Dance over the course of a fight.",
        ]),
        new("1.0.1.3",
        [
            "Reaper's two-minute was running one Enshroud where the community runs two back to back. The number of windows over a fight was right, so nothing looked wrong - only their placement. Enshroud is now saved for Arcane Circle, and eight Reapings land inside the buff instead of four.",
            "Shadow of Death can be used during Enshroud and EchoSim would not allow it. It is the one attack Enshroud does not replace, and both guides press it inside the first window to hold Death's Design across the pair. It was also being reapplied every 22 seconds on a 30-second debuff, overwriting time already paid for.",
            "Arcane Circle was going up at the pull instead of behind the opening Soul Slice, where both guides delay it. Twenty seconds later that half-second decides whether Perfectio - Reaper's largest attack - lands inside the buff or just past the end of it. It now lands inside, in the opener and at every two minutes.",
            "Gluttony is no longer pressed alongside the burst. Its two charges have to be spent before Enshroud will open, so one used between the windows pushed the second back two attacks and walked Perfectio out of Arcane Circle on one two-minute in five.",
            "Viper had the same fault, fixed the same way. Its two-minute is a double Reawaken - Serpent's Ire, one combo attack, then two full windows back to back - and EchoSim spent Reawaken as soon as it could afford one, so half the burst landed outside the party's buffs. Serpent Offerings are now saved for Serpent's Ire.",
            "Viper's Coils handed on their follow-ups in the wrong order: Hunter's Coil leads with Twinfang Bite and Swiftskin's Coil with Twinblood Bite, where EchoSim had both starting the same way. Worth no damage by itself, but correcting it uncovered a real fault underneath - the second follow-up behind every Swiftskin's Coil was being dropped.",
            "Viper's opener had the potion in the wrong place and was one Uncoiled Fury short. The potion was taking the first opening after Reaving Fangs, pushing Serpent's Ire back a slot - and on a two-minute cycle, every burst in the fight inherited that delay.",
            "Black Mage's recommended stats reach its 2.45s cast speed more cheaply. The previous set bought that tier with more Spell Speed than it needs; the same recast is reachable with 58 points left over, which now go to critical hit and determination.",
        ]),
        new("1.0.1.2",
        [
            "Samurai was missing an attack outright. Hissatsu: Gyoten is in both community openers and in the reference parse, and it was not in the job at all, so the eleventh weave of the opener sat empty. Ikishoten and Zanshin were also being spent in the first two seconds instead of after the second and sixth attacks, three abilities crammed in behind the opening Gekko. The opener now matches both guides weave for weave.",
            "Hissatsu: Senei was reaching its cooldown with an empty gauge. It costs exactly what Hissatsu: Shinten costs and hits more than three times as hard, but Shinten recasts every second and was taking the Kenki first - so Senei spent two or three attacks waiting for income every minute, and came up a use short over a fight.",
            "Higanbana was slipping a whole cycle. It lasts a minute and was being re-applied every sixty-four seconds, because the rotation only refreshed it at the moment it happened to hold a single Sen - which, after a Setsugekka empties the gauge, can be five attacks away. It now takes the free Sen the burst leaves behind, where both guides put it. That was costing a Shoha as well.",
            "Meikyo Shisui's free attacks no longer overwrite a Sen you already hold. Spending one on a second Gekko banked nothing and still left two attacks owed to reach the missing Sen; it now goes to Yukikaze, which arrives holding it. Higanbana's damage-over-time was also paid in full on every refresh, charging for time the target never spent poisoned.",
            "Healer and tank relics are no longer told to take Direct Hit, which the game does not offer them. Neither role's gear carries a point of it anywhere in the game - they get Tenacity or Piety in its place - so a relic built the way EchoSim described could not be built at all. A relic already saved that way is cleared when you next open the weapon. Melding Direct Hit is unaffected and still worth doing.",
            "The best-in-slot stat lines have been rebuilt from current game data. Tank and healer sets move the most, since a relic that was spending 447 points on Direct Hit now spends them somewhere it can go - tanks gain Determination and Tenacity, healers gain Determination and a Spell Speed tier. Monk, Ninja, Samurai and Viper shift a little as well.",
            "Black Mage now melds toward its 2.45s cast speed. It was the last job without a recast target, so the gear solver - which only sees damage, and a point of Spell Speed buys less of that than anything else - stripped it to the floor and recommended a 2.50s set. For Black Mage that is a different rotation rather than a slower one: the recast decides how many Fire IV fit before Despair.",
            "Fixed the Gear panel occasionally showing another job's weapon - a Ninja dagger on an Astrologian, with your own main stat printed on it. Switching job started rebuilding the gear list in the background, and if the previous job's rebuild was still running it would finish, judge itself current and publish anyway. Long gear-check messages now wrap inside their box as well.",
        ]),
        new("1.0.0.9",
        [
            "Monk, Dragoon and Ninja now open exactly the way both community guides publish, step for step. All three were spending their whole burst in the first few seconds instead of over the first few attacks - Dragoon crammed four abilities in after its opening attack and four more after the second, and Ninja's was disordered badly enough that Gust Slash landed seventh and Phantom Kamaitachi ninth, against second and third in the guides.",
            "Monk's Perfect Balance is now tied to Riddle of Fire, which both guides say to do and which was missing. Pressed on sight, its windows drifted off the buff until every Phantom Rush - the biggest attack the job has - was landing outside it. Nothing in the cast counts showed this; the job used it exactly as often either way.",
            "Dragoon was missing Piercing Talon and Elusive Jump entirely, and its Life Surge was only ever saved for Drakesbane - Heavens' Thrust hits just as hard and is pressed just as often, so half the job's guaranteed critical hits were aimed at an attack the rule could not see. Gunbreaker's Double Down was slipping out of No Mercy a little more each cycle until it landed after the buff had expired.",
            "Guaranteed critical hits now gain from critical hit rate buffs, as they do in the game. They were being handed nothing at all by a party's crit buffs. This affects Monk, Warrior, Samurai, Dragoon, Machinist, Dancer and Pictomancer, and the gear advice now takes it into account as well - on an attack that always crits, the rate is worth nothing while the damage bonus is worth everything.",
            "Melding advice now builds toward the recast each job is actually played at. The gear solver could not see the value of a faster recast and stripped speed to nothing on every job, so Monk, Ninja, Samurai and Viper were all being recommended sets their own guides would not.",
        ]),
        new("1.0.0.8",
        [
            "All four tank openers now match the ones the community publishes, step for step. Warrior, Dark Knight and Gunbreaker were all spending their burst cooldowns at the pull instead of saving them for the buff window. Dark Knight was the worst of it: Darkside, worth 10% of everything the job does, was not going up until six and a half seconds into the fight.",
            "The openers were also missing actions, and one of them listed an attack that cannot be pressed. Warrior had no Tomahawk and put Primal Wrath before the three Fell Cleaves that unlock it; Dark Knight had no Unmend and two Edges of Shadow where the guides use five; Gunbreaker had no Lightning Shot and built a cartridge the long way instead of opening on Bloodfest. Paladin now holds both Intervenes for Fight or Flight, which is where a top parse spends all fourteen of its own.",
            "Paladin, Dark Knight and Gunbreaker were losing combos to their own burst. All three run more than one chain at once - Paladin's Confiteor, Dark Knight's Delirium, Gunbreaker's Gnashing Fang and Reign of Beasts - and using one was cancelling the others, which the game does not do. On Gunbreaker this pushed two attacks out of the burst window entirely.",
            "Fixed a timing fault that cost every tank a cast: an ability coming off cooldown while another was still animating was treated as unavailable, and the moment to use it passed unnoticed. It hits hardest where a cooldown lines up exactly with the global cooldown, which is why Paladin gained the most - Circle of Scorn, Expiacion and Intervene were each losing a use over a fight, and the only sign was a cast count reading one short.",
        ]),
        new("1.0.0.7",
        [
            "Black Mage's opener now puts Amplifier and Ley Lines where the guides put them. Both were going up a global cooldown or two late, which delayed the haste and a Polyglot for nothing at all. High Thunder is also no longer left to fall off before being reapplied - the rotation waited for it to expire first, and waiting for that guarantees a gap every cycle.",
            "Paladin was pressing Fight or Flight four global cooldowns too early, before Royal Authority instead of after it. That spent the two weakest attacks in the job inside a 25% damage buff, seven times a fight. The Opener tab had it in a third place again, matching neither the guides nor the simulation - all three agree now.",
            "Paladin's filler saves what Royal Authority grants for the next Fight or Flight rather than spending it the moment it appears, so the stronger attacks land inside the buff. This is what both guides describe and it was not being done. A separate bug that made the burst throw away your Fast Blade combo is fixed as well: those actions do not break the combo in the game, and they no longer do here.",
        ]),
        new("1.0.0.6",
        [
            "Black Mage's Paradox was wrong in both directions. The opener listed one that cannot be cast - the marker comes from swapping off a full element, and a fight starts with no element - while the rotation quietly threw away others it had earned. The two mistakes were a similar size, so the totals looked correct.",
            "The opener is now the standard \"5+7\": five Fire IV, Manafont, seven more, two Flare Stars, taken cast for cast from a top parse. Black Mage also re-enters Astral Fire by Transposing in for a free Fire III, rather than paying 2,000 MP for it.",
        ]),
        new("1.0.0.5",
        [
            "EchoSim now follows Dalamud's UI scale. The window and everything laid out inside it - column widths, buttons, job icons, the party slots and the summary cards - were fixed pixel sizes, so raising the scale made the text bigger and left the boxes holding it exactly as they were. Labels clipped mid-word, the derived stats printed over their own labels, and the gear buttons overlapped each other. Making the window bigger could not help, because the columns were constants rather than shares of the width. Reported by a player running Dalamud at 200%.",
            "Icons inside buttons are now centred properly. They are drawn slightly smaller than they measure, and the layout was using the measurement, so the icon and its label were both a little off-centre with a gap between them. This was always true - it was only large enough to notice at a raised scale.",
        ]),
        new("1.0.0.4",
        [
            "Gear sets are now saved per job. Switching jobs used to throw away the set you had built - gear, melds and relic allocation together - with no warning and no way back. Each job now keeps its own, and switching between them puts the previous one away and takes the next one out. Whatever you have set up right now is kept, filed under the job it belongs to.",
            "Your meal is part of that set. The food list is every food in the game rather than the ones your job can use, so an Intelligence meal chosen on a caster stayed selected on a melee job and quietly contributed nothing.",
        ]),
        new("1.0.0.3",
        [
            "Fixed the plugin window failing to open after updating to 1.0.0.2. Anyone who had used EchoSim before that release was affected: your saved settings recorded the speed stat under the name an older build gave it, 1.0.0.2 renamed it, and the Gear panel stopped part-way through drawing every frame. Disabling and reinstalling could not fix it, because the settings file is what carried the problem and it survives a reinstall. Old settings are now read correctly and converted on load, so nothing needs to be reset and no gear set is lost.",
            "If any panel does hit an error in future, the window now says so and keeps working instead of going blank. The bug report button stays available, and there is a button to reset the gear setup for the case where the saved set itself is the problem.",
        ]),
        new("1.0.0.2",
        [
            "Healers: Piety is now part of the stat model. It is read off your gear, shown with your other stats, editable, and recognised as materia. It affects MP only, so it does not change your simulated damage - but a healer's fifth substat was previously invisible, and a set that spent points on it was described as though those points had gone nowhere.",
            "Everyone else: melding Piety is now flagged as doing nothing for your job, the same way melding Tenacity already was.",
            "Casters and healers: gear chosen in the Gear panel had its Spell Speed read as zero, because EchoSim was looking at the Skill Speed column that magical gear doesn't use. If you built a set by picking items, its speed - and so its GCD - read low. Reading your equipped gear from the character sheet was never affected, and the best-in-slot presets are unchanged.",
            "Casters and healers: item tooltips called your speed stat \"Skill Speed\" and the gear comparison called your main stat \"Dexterity\", whatever you were actually playing. Both now use the names your job uses, and the comparison shows Piety for healers and Tenacity for tanks instead of leaving them out.",
            "Melding your speed stat is now a note rather than a warning. It is still the lowest-value substat for pure damage, but a speed tier also buys cast-lock and cooldown alignment this simulation does not measure, so it is not a mistake.",
            "Healers are now told, on the leaderboard and in the Live Score settings, that they cannot reach 100 and should not aim to. The ceiling you are scored against never heals - every global cooldown and every resource goes to damage, which no real healer can match - so the gap is the healing your group needed rather than a mistake you made. Boards are per job, so you are ranked against other healers.",
            "The Live Score settings page has been shortened so it fits without scrolling.",
        ]),
        new("1.0.0.1",
        [
            "Fixed the relic substat picker offering Tenacity to jobs that cannot use it. Only tanks gain anything from Tenacity, so allocating it on any other job quietly wasted one of the relic's three choices - worth 447 points of substat - while the DPS number stayed believable. Your simulated damage was never wrong, but a relic built this way was.",
            "If you had already chosen Tenacity on a non-tank relic, it is cleared when you next open the weapon and the plugin tells you so. Pick a replacement to get the stat back.",
            "Casters and healers now see their relic substats labelled Spell Speed rather than Skill Speed. Same stat to the simulator, wrong word on the panel.",
        ]),
    ];

    /// The newest version with notes.
    public static string LatestVersion => Entries.Length > 0 ? Entries[0].Version : string.Empty;
}
