using System;
using System.Numerics;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;

namespace EchoNav.Nav;

/// Feeds a movement direction straight into the game's own input handling, by hooking the function that turns
/// held keys into a direction and writing the intended direction instead.
public sealed unsafe class MovementOverride : IDisposable
{
    /// The game's "read movement input" function.
    private delegate void RMIWalkDelegate(
        void* self, float* sumLeft, float* sumForward, float* sumTurnLeft,
        byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk);

    /// Guards the game uses to decide whether movement input counts at all.
    private delegate bool RMIWalkIsInputEnabledDelegate(void* self);

    private const string RmiWalkSignature = "E8 ?? ?? ?? ?? 80 7B 3E 00 48 8D 3D";
    private const string InputEnabled1Signature = "E8 ?? ?? ?? ?? 84 C0 75 10 38 43 3C";
    private const string InputEnabled2Signature = "E8 ?? ?? ?? ?? 84 C0 75 03 88 47 3F";

    private readonly Hook<RMIWalkDelegate>? rmiWalkHook;
    private readonly Hook<RMIWalkIsInputEnabledDelegate>? inputEnabled1Hook;
    private readonly Hook<RMIWalkIsInputEnabledDelegate>? inputEnabled2Hook;

    /// False when the signatures didn't resolve against this build of the game.
    public bool Available { get; }

    /// Why it isn't available, for the diagnostics readout.
    public string UnavailableReason { get; } = string.Empty;

    /// Whether to override at all.
    public bool Enabled { get; set; }

    /// When the player was last seen steering for themselves, while the driver was steering.
    public long LastPlayerInputTick { get; private set; }

    /// Camera-relative direction: X is left, Y is forward, unit length.
    public Vector2 Direction { get; set; }

    public MovementOverride(IGameInteropProvider interop, ISigScanner sigScanner)
    {
        try
        {
            var rmiWalk = sigScanner.ScanText(RmiWalkSignature);
            var inputEnabled1 = sigScanner.ScanText(InputEnabled1Signature);
            var inputEnabled2 = sigScanner.ScanText(InputEnabled2Signature);

            rmiWalkHook = interop.HookFromAddress<RMIWalkDelegate>(rmiWalk, RMIWalkDetour);
            inputEnabled1Hook = interop.HookFromAddress<RMIWalkIsInputEnabledDelegate>(inputEnabled1, InputEnabled1Detour);
            inputEnabled2Hook = interop.HookFromAddress<RMIWalkIsInputEnabledDelegate>(inputEnabled2, InputEnabled2Detour);

            rmiWalkHook.Enable();
            inputEnabled1Hook.Enable();
            inputEnabled2Hook.Enable();

            Available = true;
        }
        catch (Exception ex)
        {
            Available = false;
            UnavailableReason = ex.Message;
            Plugin.Log.Warning(ex, "[EchoNav] Movement hook unavailable - falling back to key input");
        }
    }

    private void RMIWalkDetour(
        void* self, float* sumLeft, float* sumForward, float* sumTurnLeft,
        byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk)
    {
        rmiWalkHook!.Original(self, sumLeft, sumForward, sumTurnLeft, haveBackwardOrStrafe, a6, bAdditiveUnk);

        if (!Enabled)
            return;

        if (*sumLeft != 0f || *sumForward != 0f)
        {
            LastPlayerInputTick = Environment.TickCount64;
            return;
        }

        *sumLeft = Direction.X;
        *sumForward = Direction.Y;
    }

    private bool InputEnabled1Detour(void* self) => Enabled || inputEnabled1Hook!.Original(self);

    private bool InputEnabled2Detour(void* self) => Enabled || inputEnabled2Hook!.Original(self);

    public void Dispose()
    {
        Enabled = false;

        rmiWalkHook?.Dispose();
        inputEnabled1Hook?.Dispose();
        inputEnabled2Hook?.Dispose();
    }
}
