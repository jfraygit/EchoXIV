using System;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace EchoGlam.Game;

/// Which house you are standing in, if any.
public readonly record struct HouseKey(ulong Id, int Ward, int Plot, int Room, bool IsApartment)
{
    /// The address, written the way the game writes it on a placard.
    public string Address
    {
        get
        {
            if (IsApartment)
                return Room > 0 ? $"Ward {Ward}, Apartment {Room}" : $"Ward {Ward}, Apartments";

            if (Room > 0)
                return $"Ward {Ward}, Plot {Plot}, Room {Room}";

            return $"Ward {Ward}, Plot {Plot}";
        }
    }
}

/// Where you live, read once a frame.
public sealed class Housing
{
    /// The house you are in, or null for anywhere that is not one.
    public HouseKey? Current { get; private set; }

    public void Tick()
    {
        Current = Read();
    }

    private static unsafe HouseKey? Read()
    {
        try
        {
            var manager = HousingManager.Instance();
            if (manager == null)
                return null;

            var id = manager->GetCurrentHouseId();

            if (id.Id == 0)
                return null;

            return new HouseKey(
                id.Id,
                id.WardIndex + 1,
                id.PlotIndex + 1,
                id.RoomNumber,
                id.IsApartment);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoGlam] Could not read the current house");
            return null;
        }
    }
}
