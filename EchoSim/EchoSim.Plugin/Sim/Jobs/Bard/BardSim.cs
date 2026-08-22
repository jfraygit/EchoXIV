using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Bard;

/// Bard's song state, gauges and proc accumulator on top of the generic sim state.
public sealed class BardState : SimState
{
    public Song CurrentSong { get; set; } = Song.None;

    /// When the running song started, for deciding when to swap to the next one.
    public double SongStartedAt { get; set; }

    /// When the next Repertoire lands.
    public double NextRepertoireAt { get; set; } = double.PositiveInfinity;

    public int SoulVoice { get; set; }

    /// When Caustic Bite and Stormbite run out.
    public double CausticExpiresAt { get; set; }

    public double StormExpiresAt { get; set; }

    public int PitchPerfectStacks { get; set; }

    /// Army's Paeon Repertoire, worth 4% haste apiece.
    public int PaeonStacks { get; set; }

    /// What Army's Muse is currently worth, held separately because it is fixed at the moment Paeon ends
    /// rather than recomputed - the Repertoire that set it is gone by the time it applies.
    public int MuseHaste { get; set; }

    /// Distinct Coda banked.
    public HashSet<Song> Coda { get; } = [];

    /// How many Coda the last Radiant Finale consumed - which is what prices Radiant Encore.
    public int EncoreCoda { get; set; }

    /// Accumulated Hawk's Eye probability.
    public double HawksEyeCharge { get; set; }

    public void GainSoulVoice()
    {
        if (SoulVoice >= BardData.MaxSoulVoice)
        {
            Count("soulvoice.overcapped", BardData.SoulVoicePerRepertoire);
            return;
        }

        SoulVoice = System.Math.Min(BardData.MaxSoulVoice, SoulVoice + BardData.SoulVoicePerRepertoire);
    }

    public override SimState Clone()
    {
        var copy = new BardState
        {
            CurrentSong = CurrentSong,
            SongStartedAt = SongStartedAt,
            NextRepertoireAt = NextRepertoireAt,
            SoulVoice = SoulVoice,
            CausticExpiresAt = CausticExpiresAt,
            StormExpiresAt = StormExpiresAt,
            PitchPerfectStacks = PitchPerfectStacks,
            PaeonStacks = PaeonStacks,
            MuseHaste = MuseHaste,
            EncoreCoda = EncoreCoda,
            HawksEyeCharge = HawksEyeCharge,
        };

        foreach (var coda in Coda)
            copy.Coda.Add(coda);

        CopyInto(copy);
        return copy;
    }
}

/// Bard's rules: what's legal, what it's worth, and what it does to the songs and gauges.
public sealed class BardSim : IJobSim
{
    public string JobName => "Bard";

    public MainAttribute MainAttribute => MainAttribute.Dexterity;

    public CombatRole Role => CombatRole.PhysicalRanged;

    public int MainStatModifier { get; init; } = 115;

    /// None permanently.
    public int HastePercent => 0;

    /// 80.
    public int AutoAttackPotency => 80;

    /// Increased Action Damage II, the physical ranged trait.
    public double TraitMultiplier => 1.20;

    public IReadOnlyDictionary<string, ActionDef> Actions => BardData.Actions;

    /// Army's Paeon's Repertoire at 4% a stack while it plays, and Army's Muse for ten seconds after.
    public int TransientHastePercent(SimState state)
    {
        var brd = (BardState)state;
        var paeon = brd.PaeonStacks * BardData.PaeonHastePerStack;
        var muse = brd.HasStatus(Brd.ArmysMuse) ? brd.MuseHaste : 0;

        return paeon + muse;
    }

    /// Auto-attacks are hastened by Army's Paeon exactly as weaponskills are, which the reference parse's
    /// swing gaps confirm directly: a 3.075s base mode against a 2.583s fast one, and 3.075 x 0.84 is 2.583.
    public double AutoAttackInterval(SimState state, PlayerStats stats)
        => stats.AutoAttackIntervalWith(TransientHastePercent(state));

    public SimState CreateState()
    {
        var state = new BardState();

        foreach (var (name, action) in BardData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var brd = (BardState)state;

        return action.Name switch
        {
            Brd.RefulgentArrow => brd.HasStatus(Brd.HawksEye) || brd.HasStatus(Brd.BarrageStatus),
            Brd.BlastArrow => brd.HasStatus(Brd.BlastArrowReady),
            Brd.ResonantArrow => brd.HasStatus(Brd.ResonantArrowReady),
            Brd.RadiantEncore => brd.HasStatus(Brd.RadiantEncoreReady),
            Brd.ApexArrow => brd.SoulVoice >= BardData.ApexMinimumGauge,

            Brd.PitchPerfect => brd.CurrentSong == Song.Minuet && brd.PitchPerfectStacks > 0,

            Brd.RadiantFinaleAction => brd.Coda.Count > 0,

            _ => true,
        };
    }

    public double EffectivePotency(ActionDef action, SimState state)
    {
        var brd = (BardState)state;

        return action.Name switch
        {
            Brd.ApexArrow => BardData.ApexArrowPotency(brd.SoulVoice),
            Brd.RadiantEncore => BardData.RadiantEncorePotency(brd.EncoreCoda),

            Brd.PitchPerfect => brd.PitchPerfectStacks switch
            {
                >= 3 => 360,
                2 => 220,
                _ => 100,
            },

            Brd.RefulgentArrow when brd.HasStatus(Brd.BarrageStatus) => action.Potency * BardData.BarrageHits,

            _ => action.Potency,
        };
    }

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var brd = (BardState)state;

        AdvanceSong(brd);

        switch (action.Name)
        {
            case Brd.BurstShot:
            case Brd.Ladonsbite:
                RollHawksEye(brd);
                return;

            case Brd.RefulgentArrow:
            case Brd.Shadowbite:
                if (brd.HasStatus(Brd.BarrageStatus))
                    brd.RemoveStatus(Brd.BarrageStatus);
                else
                    brd.RemoveStatus(Brd.HawksEye);

                return;

            case Brd.CausticBite:
                ApplyDot(brd, recordHit, caustic: true);
                RollHawksEye(brd);
                return;

            case Brd.Stormbite:
                ApplyDot(brd, recordHit, caustic: false);
                RollHawksEye(brd);
                return;

            case Brd.IronJaws:
                ApplyDot(brd, recordHit, caustic: true);
                ApplyDot(brd, recordHit, caustic: false);
                RollHawksEye(brd);
                return;

            case Brd.ApexArrow:
                if (brd.SoulVoice >= BardData.BlastArrowThreshold)
                    brd.ApplyStatus(Brd.BlastArrowReady, BardData.BlastArrowReadyDuration);

                brd.SoulVoice = 0;
                return;

            case Brd.BlastArrow:
                brd.RemoveStatus(Brd.BlastArrowReady);
                return;

            case Brd.ResonantArrow:
                brd.RemoveStatus(Brd.ResonantArrowReady);
                return;

            case Brd.RadiantEncore:
                brd.RemoveStatus(Brd.RadiantEncoreReady);
                return;

            case Brd.PitchPerfect:
                brd.PitchPerfectStacks = 0;
                return;

            case Brd.EmpyrealArrow:
                if (brd.CurrentSong != Song.None)
                    GrantRepertoire(brd);

                return;

            case Brd.BarrageAction:
                brd.ApplyStatus(Brd.BarrageStatus, BardData.BarrageDuration);
                brd.ApplyStatus(Brd.ResonantArrowReady, BardData.ReadyDuration);
                return;

            case Brd.RagingStrikesAction:
                brd.ApplyStatus(Brd.RagingStrikes, BardData.RagingStrikesDuration,
                    damageMulti: BardData.RagingStrikesMulti);
                return;

            case Brd.BattleVoiceAction:
                brd.ApplyStatus(Brd.BattleVoice, BardData.BattleVoiceDuration,
                    directHitBonus: BardData.BattleVoiceDirectHitBonus);
                return;

            case Brd.RadiantFinaleAction:
                brd.EncoreCoda = brd.Coda.Count;
                brd.Coda.Clear();
                brd.ApplyStatus(Brd.RadiantFinale, BardData.RadiantFinaleDuration,
                    damageMulti: BardData.RadiantFinaleMulti(brd.EncoreCoda));
                brd.ApplyStatus(Brd.RadiantEncoreReady, BardData.ReadyDuration);
                return;

            case Brd.WanderersMinuet:
                StartSong(brd, Song.Minuet);
                return;

            case Brd.MagesBallad:
                StartSong(brd, Song.Ballad);
                return;

            case Brd.ArmysPaeon:
                StartSong(brd, Song.Paeon);
                return;
        }
    }

    /// Applies or refreshes one damage-over-time, paying out the ticks it will actually deliver.
    private static void ApplyDot(BardState brd, HitRecorder recordHit, bool caustic)
    {
        var expiresAt = caustic ? brd.CausticExpiresAt : brd.StormExpiresAt;
        var remaining = System.Math.Max(0, expiresAt - brd.Time);
        var alreadyPaid = (int)System.Math.Round(remaining / BardData.TickInterval);
        var ticks = System.Math.Max(0, BardData.DotTicks - alreadyPaid);

        if (caustic)
        {
            brd.CausticExpiresAt = brd.Time + BardData.DotDuration;
            recordHit(Brd.CausticBiteDot, ticks * BardData.CausticBiteTickPotency);
        }
        else
        {
            brd.StormExpiresAt = brd.Time + BardData.DotDuration;
            recordHit(Brd.StormbiteDot, ticks * BardData.StormbiteTickPotency);
        }

        if (remaining <= 1e-9)
            brd.Count("dot.applied", 1);
    }

    /// Banks 35% of a proc, and spends it once a whole one has accumulated.
    private static void RollHawksEye(BardState brd)
    {
        brd.HawksEyeCharge += BardData.HawksEyeChance;
        if (brd.HawksEyeCharge < 1.0)
            return;

        brd.HawksEyeCharge -= 1.0;

        if (brd.HasStatus(Brd.HawksEye))
            brd.Count("hawkseye.overwritten", 1);

        brd.ApplyStatus(Brd.HawksEye, BardData.HawksEyeDuration);
    }

    /// Starts a song, banking its Coda and resetting what the previous one was granting.
    private static void StartSong(BardState brd, Song song)
    {
        var previous = brd.CurrentSong;

        brd.CurrentSong = song;
        brd.SongStartedAt = brd.Time;
        brd.NextRepertoireAt = brd.Time + BardData.RepertoireInterval;
        brd.Coda.Add(song);

        if (previous == Song.Paeon && brd.PaeonStacks > 0)
        {
            brd.MuseHaste = BardData.MuseHasteFor(brd.PaeonStacks);
            brd.ApplyStatus(Brd.ArmysMuse, BardData.ArmysMuseDuration);
        }

        brd.PitchPerfectStacks = song == Song.Minuet ? brd.PitchPerfectStacks : 0;
        brd.PaeonStacks = 0;
    }

    /// Catches the song up to the current time, granting whatever Repertoire is due.
    private static void AdvanceSong(BardState brd)
    {
        if (brd.CurrentSong == Song.None)
            return;

        if (brd.Time >= brd.SongStartedAt + BardData.SongDuration)
        {
            brd.CurrentSong = Song.None;
            brd.NextRepertoireAt = double.PositiveInfinity;
            brd.PaeonStacks = 0;
            brd.PitchPerfectStacks = 0;
            return;
        }

        while (brd.Time + 1e-9 >= brd.NextRepertoireAt)
        {
            GrantRepertoire(brd);
            brd.NextRepertoireAt += BardData.RepertoireInterval;
        }
    }

    /// One Repertoire: Soul Voice always, and whatever the running song makes of it.
    private static void GrantRepertoire(BardState brd)
    {
        brd.GainSoulVoice();
        brd.Count("repertoire", 1);

        switch (brd.CurrentSong)
        {
            case Song.Minuet:
                if (brd.PitchPerfectStacks >= BardData.MaxPitchPerfect)
                    brd.Count("pitchperfect.overcapped", 1);
                else
                    brd.PitchPerfectStacks++;

                return;

            case Song.Ballad:
                brd.Cooldown(Brd.HeartbreakShot).Reduce(BardData.BalladRecastReduction, brd.Time);
                return;

            case Song.Paeon:
                if (brd.PaeonStacks < BardData.MaxPaeonStacks)
                    brd.PaeonStacks++;

                return;
        }
    }
}
