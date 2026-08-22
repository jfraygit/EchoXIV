using System;
using Dalamud.Plugin.Ipc;

namespace EchoMix.Plugin.Integrations;




using AddressBookEntryTuple = (string Name, int World, int City, int Ward, int PropertyType, int Plot, int Apartment, bool ApartmentSubdivision, bool AliasEnabled, string Alias);

/// Thin wrapper around Lifestream's EzIPC surface - Lifestream is an optional third-party plugin (not
/// referenced at compile time), so every call here degrades to a user-facing error string instead of throwing
/// if it isn't installed, isn't ready yet, or the request fails for any other reason.
public static class LifestreamIntegration
{
    private static ICallGateSubscriber<bool>? isBusySubscriber;
    private static ICallGateSubscriber<string, string, string, string, bool, bool, AddressBookEntryTuple>? buildEntrySubscriber;
    private static ICallGateSubscriber<AddressBookEntryTuple, object>? goToHousingAddressSubscriber;

    private static void EnsureSubscribers()
    {
        isBusySubscriber ??= Plugin.PluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        buildEntrySubscriber ??= Plugin.PluginInterface.GetIpcSubscriber<string, string, string, string, bool, bool, AddressBookEntryTuple>("Lifestream.BuildAddressBookEntry");
        goToHousingAddressSubscriber ??= Plugin.PluginInterface.GetIpcSubscriber<AddressBookEntryTuple, object>("Lifestream.GoToHousingAddress");
    }

    /// A venue needs World + Housing Area + Ward + Plot/Apartment# to be travelable - Data Center alone isn't
    /// required since world names are globally unique and Lifestream looks the world up by name regardless of
    /// which DC it's in.
    public static bool HasVisitableLocation(string? world, string? housingArea, string? ward, string? plot) =>
        !string.IsNullOrWhiteSpace(world) && !string.IsNullOrWhiteSpace(housingArea)
            && !string.IsNullOrWhiteSpace(ward) && !string.IsNullOrWhiteSpace(plot);

    /// Attempts to send the local player to a venue's saved housing address via Lifestream.
    public static string? TryVisit(string world, string housingArea, string ward, string plot, bool isApartment = false, bool isSubdivision = false)
    {
        try
        {
            EnsureSubscribers();

            if (isBusySubscriber!.InvokeFunc())
                return "Lifestream is busy with another travel request - try again in a moment.";

            var entry = buildEntrySubscriber!.InvokeFunc(world, housingArea, ward, plot, isApartment, isSubdivision);
            goToHousingAddressSubscriber!.InvokeAction(entry);
            return null;
        }
        catch (Exception)
        {
            return "Couldn't reach Lifestream - install it from the NightmareXIV plugin repo to enable one-click Visit.";
        }
    }
}
