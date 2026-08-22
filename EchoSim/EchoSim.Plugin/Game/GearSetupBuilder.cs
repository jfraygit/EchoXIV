using EchoSim.Sim;
using EchoSim.Sim.Validation;

namespace EchoSim.Game;

/// Turns the plugin's saved gear and materia selection into the abstract shape the gear validator works on.
public static class GearSetupBuilder
{
    /// The level 100 substat floor.
    private const int SpeedFloor = 420;


    public static GearSetup Build(
        string jobName,
        IReadOnlyDictionary<string, uint> selection,
        IReadOnlyDictionary<string, List<int>> melds,
        RelicAllocation? relic = null,
        IReadOnlyDictionary<string, int>? inferredRelicStats = null)
    {
        var slots = new List<GearSlotSetup>();

        var relicTotalSlot = Enum.GetValues<GearSlot>()
            .Select(GearCatalog.Label)
            .Where(l => GearCatalog.ById(selection.GetValueOrDefault(l)) is { IsCustomisableRelic: true })
            .OrderBy(l => GearCatalog.ById(selection.GetValueOrDefault(l))!.RelicScale == RelicSlot.OffHand ? 1 : 0)
            .FirstOrDefault();

        foreach (var slot in Enum.GetValues<GearSlot>())
        {
            if (slot == GearSlot.OffHand && GearCatalog.For(slot).Count == 0)
                continue;

            var label = GearCatalog.Label(slot);
            var itemId = selection.GetValueOrDefault(label);
            var piece = GearCatalog.ById(itemId);

            if (piece is null)
            {
                slots.Add(new GearSlotSetup { SlotName = label });
                continue;
            }

            var meldInfos = new List<MeldInfo>();
            if (melds.TryGetValue(label, out var encoded))
            {
                foreach (var e in encoded)
                {
                    if (MateriaCatalog.Decode(e) is { } option)
                        meldInfos.Add(new MeldInfo(option.Stat, option.Value, option.Name));
                    else if (MateriaCatalog.FromEquipped((ushort)MateriaOption.Decode(e).RowId, (byte)MateriaOption.Decode(e).Grade) is { } equipped)
                        meldInfos.Add(new MeldInfo(equipped.Stat, equipped.Value, equipped.Name));
                }
            }

            slots.Add(new GearSlotSetup
            {
                SlotName = label,
                ItemId = piece.ItemId,
                ItemName = piece.Name,
                ItemLevel = piece.ItemLevel,
                IsUnique = piece.IsUnique,
                MateriaSlots = piece.MateriaSlots,
                MaxMelds = piece.MaxMelds,
                WeaponDamage = piece.WeaponDamage,
                Melds = meldInfos,
                IsRelic = piece.IsCustomisableRelic,

                RelicStats = !piece.IsCustomisableRelic
                    ? []
                    : inferredRelicStats is { Count: > 0 }
                        ? label == relicTotalSlot
                            ? [.. SubStats.Parse(inferredRelicStats).Select(kv => new MeldInfo(kv.Key, kv.Value, "Relic"))]
                            : []
                        : meldInfos.Count > 0
                            ? meldInfos
                            : [.. (relic?.Entries(piece.RelicScale ?? RelicSlot.TwoHand) ?? [])
                                .Select(e => new MeldInfo(e.Stat, e.Value, "Relic"))],
                BaseStats = new Dictionary<SubStat, int>
                {
                    [SubStat.Crit] = piece.Crit,
                    [SubStat.Determination] = piece.Determination,
                    [SubStat.DirectHit] = piece.DirectHit,
                    [SubStat.Speed] = piece.SkillSpeed,
                    [SubStat.Tenacity] = piece.Tenacity,
                },
                Caps = piece.Caps,
            });
        }

        var definition = Sim.Jobs.JobRegistry.ForOrDefault(GearCatalog.ActiveJob);
        var reference = definition.ReferenceGear();
        var wantsSpeed = reference.SkillSpeed > SpeedFloor;

        var usesTenacity = Sim.Engine.CombatRoles.UsesTenacity(definition.CreateSim().Role);

        var usesPiety = Sim.Engine.CombatRoles.UsesPiety(definition.CreateSim().Role);

        return new GearSetup(jobName, slots)
        {
            UselessStats =
            [
                .. usesTenacity ? Array.Empty<SubStat>() : [SubStat.Tenacity],
                .. usesPiety ? Array.Empty<SubStat>() : [SubStat.Piety],
            ],

            UnavailableStats =
            [
                .. Enum.GetValues<SubStat>()
                    .Where(stat => !Sim.Engine.CombatRoles.GearCarries(definition.CreateSim().Role, stat)),
            ],

            AvoidedStats = wantsSpeed ? [] : [SubStat.Speed],

            SpeedStatLabel = GearCatalog.SpeedStatLabel,
        };
    }
}
