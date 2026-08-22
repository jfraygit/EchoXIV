using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.RedMage;

/// Red Mage's standard single-target rotation.
public sealed class RedMageStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int openerWeaveIndex;
    private int potionIndex;

    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// Veraero III before the pull, which is the only way to open with a Dualcast already banked.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-5.0s", Rdm.Veraero3,
            "The only hard-cast Verspell in the rotation. Lands as the fight starts and leaves "
            + "Dualcast up, so the opener's first global cooldown is already free."),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Rdm.Veraero3,

        Rdm.Verthunder3, Rdm.SwiftcastAction, Buffs.PotionAction,
        Rdm.Verthunder3, Rdm.Fleche, Rdm.AccelerationAction,
        Rdm.Verthunder3, Rdm.EmboldenAction, Rdm.ManaficationAction,

        Rdm.EnchantedRiposte, Rdm.ContreSixte,
        Rdm.EnchantedZwerchhau, Rdm.Engagement,
        Rdm.EnchantedRedoublement, Rdm.CorpsACorps,
        Rdm.Verholy, Rdm.ViceOfThorns,
        Rdm.Scorch, Rdm.Engagement, Rdm.CorpsACorps,
        Rdm.Resolution, Rdm.Prefulgence,

        Rdm.GrandImpact, Rdm.AccelerationAction,
        Rdm.Verfire,
        Rdm.GrandImpact,
    ];

    /// The opener's global cooldowns, in order.
    private static readonly string[] Opener =
    [
        Rdm.Verthunder3,
        Rdm.Verthunder3,
        Rdm.Verthunder3,
        Rdm.EnchantedRiposte,
        Rdm.EnchantedZwerchhau,
        Rdm.EnchantedRedoublement,
        Rdm.Verholy,
        Rdm.Scorch,
        Rdm.Resolution,
        Rdm.GrandImpact,
        Rdm.Verfire,
        Rdm.GrandImpact,
    ];

    /// The opener's weaves, in order, each after the global it was recorded against.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(0, Rdm.SwiftcastAction),
        new(1, Rdm.Fleche),
        new(1, Rdm.AccelerationAction),
        new(2, Rdm.EmboldenAction),
        new(2, Rdm.ManaficationAction),
        new(3, Rdm.ContreSixte),
        new(4, Rdm.Engagement),
        new(5, Rdm.CorpsACorps),
        new(6, Rdm.ViceOfThorns),
        new(7, Rdm.Engagement),
        new(7, Rdm.CorpsACorps),
        new(8, Rdm.Prefulgence),
        new(9, Rdm.AccelerationAction),
    ];

    /// Index into OpenerWeaves of Swiftcast, which the opener's potion follows.
    private const int OpenerPotionAfterWeave = 0;

    /// Seconds of Prefulgence Ready kept in hand, so a banked one is never lost to expiry.
    private const double PrefulgenceHoldFloor = 6.0;

    /// How far before the pull the Veraero III goes, so it lands as the fight starts.
    private const double PrePullLead = 5.0;

    /// Whether to run the area CASTING half.
    private bool areaChain;

    /// Whether the Moulinet chain beats the enchanted single-target one at this target count.
    private bool areaMelee;

    public void Reset(SimState state, PlayerStats stats)
    {
        areaChain = state.UseAreaRotation;

        areaMelee = TargetChoice.CyclePotencyPerGcd(
                        RedMageData.Actions, state.Targets, state.PositionalsLostFrom,
                        Rdm.EnchantedMoulinet, Rdm.EnchantedMoulinetDeux, Rdm.EnchantedMoulinetTrois)
                    > TargetChoice.CyclePotencyPerGcd(
                        RedMageData.Actions, state.Targets, state.PositionalsLostFrom,
                        Rdm.EnchantedRiposte, Rdm.EnchantedZwerchhau, Rdm.EnchantedRedoublement);

        openerIndex = 0;
        openerWeaveIndex = 0;
        potionIndex = 0;

        var rdm = (RedMageState)state;

        rdm.GainWhite(RedMageData.ManaPerVerspell);
        rdm.BankProc(Rdm.VerstoneReady);
        rdm.ApplyStatus(Rdm.Dualcast, RedMageData.DualcastDuration - PrePullLead);
    }

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var rdm = (RedMageState)state;

        if (Weave(rdm, job, stats) is { } ability)
            return ability;

        if (rdm.Time + 1e-9 < rdm.NextGcdAt)
            return null;

        if (openerIndex < Opener.Length)
        {
            var planned = RedMageData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, rdm))
                return planned;

            rdm.Count("opener.skipped", 1);
            rdm.Count($"opener.skipped:{planned.Name}", 1);
        }

        return NextGcd(rdm, job);
    }

    /// The global cooldown priority: finish the combo, start one if the gauge allows, otherwise fill.
    private ActionDef NextGcd(RedMageState rdm, IJobSim job)
    {
        if (rdm.ResolutionReady)
            return RedMageData.Get(Rdm.Resolution);

        if (rdm.ScorchReady)
            return RedMageData.Get(Rdm.Scorch);

        if (rdm.ComboFinisherReady)
            return RedMageData.Get(rdm.BlackMana <= rdm.WhiteMana ? Rdm.Verflare : Rdm.Verholy);

        var chain = areaMelee
            ? new[] { Rdm.EnchantedMoulinetTrois, Rdm.EnchantedMoulinetDeux, Rdm.EnchantedMoulinet }
            : new[] { Rdm.EnchantedRedoublement, Rdm.EnchantedZwerchhau, Rdm.EnchantedRiposte };

        foreach (var step in chain)
        {
            var action = RedMageData.Get(step);
            if (job.CanUse(action, rdm))
                return action;
        }

        var grandImpact = RedMageData.Get(Rdm.GrandImpact);
        if (job.CanUse(grandImpact, rdm))
            return grandImpact;

        if (rdm.HasStatus(Rdm.Dualcast) || rdm.HasStatus(Rdm.AccelerationStatus)
            || rdm.HasStatus(Rdm.SwiftcastStatus))
        {
            var wantsBlack = rdm.BlackMana <= rdm.WhiteMana;

            if (Accelerating(rdm))
            {
                var blackHeld = rdm.HasStatus(Rdm.VerfireReady);
                var whiteHeld = rdm.HasStatus(Rdm.VerstoneReady);

                if (wantsBlack && blackHeld && !whiteHeld)
                    wantsBlack = false;
                else if (!wantsBlack && whiteHeld && !blackHeld)
                    wantsBlack = true;
            }

            return areaChain
                ? RedMageData.Get(Rdm.Impact)
                : RedMageData.Get(wantsBlack ? Rdm.Verthunder3 : Rdm.Veraero3);
        }

        var wantBlack = rdm.BlackMana <= rdm.WhiteMana;

        foreach (var name in wantBlack
                     ? new[] { Rdm.Verfire, Rdm.Verstone }
                     : new[] { Rdm.Verstone, Rdm.Verfire })
        {
            var action = RedMageData.Get(name);
            if (job.CanUse(action, rdm))
                return action;
        }

        return areaChain
            ? RedMageData.Get(wantBlack ? Rdm.Verthunder2 : Rdm.Veraero2)
            : RedMageData.Get(Rdm.Jolt3);
    }

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(RedMageState rdm, IJobSim job, PlayerStats stats)
    {
        if (!WeavePlanner.CanWeave(rdm, stats))
            return null;

        if (openerWeaveIndex > OpenerPotionAfterWeave
            && potionIndex < potionTimes.Count && rdm.Time + 1e-9 >= potionTimes[potionIndex]
            && rdm.Cooldown(Buffs.PotionAction).ChargesAt(rdm.Time) > 0)
        {
            potionIndex++;
            return RedMageData.Get(Buffs.PotionAction);
        }

        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd + 1)
            {
                openerWeaveIndex++;
            }
            else if (openerIndex > planned.AfterGcd
                     && Ready(rdm, planned.Action)
                     && job.CanUse(RedMageData.Get(planned.Action), rdm))
            {
                openerWeaveIndex++;
                return RedMageData.Get(planned.Action);
            }
            else if (openerIndex <= planned.AfterGcd)
            {
                return null;
            }
        }

        foreach (var name in new[] { Rdm.Prefulgence, Rdm.ViceOfThorns })
        {
            var action = RedMageData.Get(name);
            if (job.CanUse(action, rdm) && !HoldForEmbolden(rdm, name))
                return action;
        }

        if (Ready(rdm, Rdm.EmboldenAction))
            return RedMageData.Get(Rdm.EmboldenAction);

        if (Ready(rdm, Rdm.ManaficationAction) && !rdm.HasStatus(Rdm.MagickedSwordplay)
            && rdm.BlackMana < RedMageData.MeleeComboCost)
        {
            return RedMageData.Get(Rdm.ManaficationAction);
        }

        foreach (var name in new[] { Rdm.Fleche, Rdm.ContreSixte })
        {
            if (Ready(rdm, name))
                return RedMageData.Get(name);
        }

        var comboImminent = rdm.HasStatus(Rdm.MagickedSwordplay)
                            || (rdm.BlackMana >= RedMageData.MeleeComboCost
                                && rdm.WhiteMana >= RedMageData.MeleeComboCost);

        if (Ready(rdm, Rdm.AccelerationAction) && !rdm.HasStatus(Rdm.Dualcast)
            && !rdm.HasStatus(Rdm.GrandImpactReady) && !comboImminent)
        {
            return RedMageData.Get(Rdm.AccelerationAction);
        }

        if (!WeavePlanner.FitsWithoutClipping(rdm))
            return null;

        if (Ready(rdm, Rdm.SwiftcastAction) && !rdm.HasStatus(Rdm.Dualcast)
            && !rdm.HasStatus(Rdm.SwiftcastStatus) && !rdm.HasStatus(Rdm.AccelerationStatus))
        {
            return RedMageData.Get(Rdm.SwiftcastAction);
        }

        foreach (var name in new[] { Rdm.CorpsACorps, Rdm.Engagement })
        {
            if (Ready(rdm, name))
                return RedMageData.Get(name);
        }

        return null;
    }

    /// Whether an ability is off recast, asked at the moment it could actually be pressed.
    private static bool HoldForEmbolden(RedMageState rdm, string name)
    {
        if (name != Rdm.Prefulgence || rdm.HasStatus(Rdm.EmboldenStatus))
            return false;

        var remaining = rdm.StatusRemaining(Rdm.PrefulgenceReady);
        if (remaining <= PrefulgenceHoldFloor)
            return false;

        var untilEmbolden = rdm.Cooldown(Rdm.EmboldenAction).ReadyAt(rdm.Time) - rdm.Time;

        return untilEmbolden < remaining - PrefulgenceHoldFloor;
    }

    /// Whether an instant Verspell cast now would spend the ACCELERATION charge specifically.
    private static bool Accelerating(RedMageState rdm)
        => rdm.HasStatus(Rdm.AccelerationStatus)
           && !rdm.HasStatus(Rdm.Dualcast)
           && !rdm.HasStatus(Rdm.SwiftcastStatus);

    /// The HasCooldown guard is not decoration: the plan above names Vice of Thorns and Prefulgence, which
    /// are gated by a status and have no recast registered at all, and Cooldown() throws on a key it does not
    /// hold.
    private static bool Ready(RedMageState rdm, string name)
        => !rdm.HasCooldown(name)
           || rdm.Cooldown(name).ChargesAt(Math.Max(rdm.Time, rdm.AnimationLockUntil)) > 0;
}
