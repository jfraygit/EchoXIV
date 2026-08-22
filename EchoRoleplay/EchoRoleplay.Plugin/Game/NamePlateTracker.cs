using System;
using System.Collections.Generic;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Plugin.Services;

namespace EchoRoleplay.Game;

/// Which nameplate belongs to which player.
public sealed class NamePlateTracker : IDisposable
{
    private readonly INamePlateGui namePlateGui;

    /// Object id to plate index.
    private Dictionary<ulong, int> plates = [];
    private Dictionary<ulong, int> building = [];

    /// Diagnostics for the Development panel.
    public int LastHandlerCount { get; private set; }
    public int LastPlayerPlateCount { get; private set; }
    public int UpdatesSeen { get; private set; }

    public NamePlateTracker(INamePlateGui namePlateGui)
    {
        this.namePlateGui = namePlateGui;
        this.namePlateGui.OnPostDataUpdate += OnPostDataUpdate;
    }

    public void Dispose() => namePlateGui.OnPostDataUpdate -= OnPostDataUpdate;

    private void OnPostDataUpdate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        building.Clear();

        LastHandlerCount = handlers.Count;

        foreach (var handler in handlers)
        {
            if (handler.NamePlateKind != NamePlateKind.PlayerCharacter)
                continue;

            if (handler.GameObjectId == 0)
                continue;

            building[handler.GameObjectId] = handler.NamePlateIndex;
        }

        LastPlayerPlateCount = building.Count;
        UpdatesSeen++;

        (plates, building) = (building, plates);
    }

    /// Which plate belongs to this player, if the game is drawing one for them.
    public bool TryGetPlateIndex(ulong gameObjectId, out int namePlateIndex) =>
        plates.TryGetValue(gameObjectId, out namePlateIndex);
}
