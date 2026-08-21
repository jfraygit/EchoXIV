namespace EchoNav.Nav;

/// Where EchoNav is allowed to do anything at all.
public static class SupportedZones
{
    /// Occult Crescent: North Horn.
    public const uint NorthHorn = 1346;

    /// What to call it when explaining why nothing is happening.
    public const string SupportedName = "Occult Crescent: North Horn";

    public static bool Supports(uint territory) => territory == NorthHorn;
}
