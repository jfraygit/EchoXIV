namespace EchoRoleplay;

/// What kind of build this is.
internal static class Build
{
    /// True in the builds Dalamud dev-loads from bin\Debug, false in the packaged release.
    public const bool Diagnostics =
#if DEBUG
        true;
#else
        false;
#endif
}
