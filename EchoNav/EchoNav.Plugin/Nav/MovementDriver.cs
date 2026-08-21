using System;
using System.Numerics;
using Dalamud.Game.Config;
using Dalamud.Plugin.Services;

namespace EchoNav.Nav;

public enum MovementStatus
{
    Idle,
    Probing,
    Running,
    Arrived,
    Blocked,
    WaitingForFocus,

    /// The player took the controls, so the run ended.
    Cancelled,
}

/// Walks the character toward a point, steering continuously from its own position.
public sealed class MovementDriver
{
    private enum Phase
    {
        Idle,
        ProbeForward,
        ProbeLateral,
        Steering,
        Recovering,
    }

    /// How long to push sideways when trying to get out of a snag, and how many times to try before admitting
    /// defeat.
    private const long RecoveryMs = 1200;
    private const int MaxRecoveries = 3;

    /// One of the eight directions WASD can express, as an offset from camera-forward.
    private readonly record struct MoveCombo(string Label, float Offset, ushort[] Keys);

    private static readonly MoveCombo[] Combos =
    [
        new("W",   0f,                 [KeyboardInput.ScanW]),
        new("WD",  MathF.PI / 4f,      [KeyboardInput.ScanW, KeyboardInput.ScanD]),
        new("D",   MathF.PI / 2f,      [KeyboardInput.ScanD]),
        new("SD",  3f * MathF.PI / 4f, [KeyboardInput.ScanS, KeyboardInput.ScanD]),
        new("S",   MathF.PI,           [KeyboardInput.ScanS]),
        new("SA", -3f * MathF.PI / 4f, [KeyboardInput.ScanS, KeyboardInput.ScanA]),
        new("A",  -MathF.PI / 2f,      [KeyboardInput.ScanA]),
        new("WA", -MathF.PI / 4f,      [KeyboardInput.ScanW, KeyboardInput.ScanA]),
    ];

    /// How much better an alternative combination must be before switching, plus a minimum time on the
    /// current one.
    private const float ComboSwitchMargin = 0.20f;
    private const long MinComboHoldMs = 250;

    private const long ProbeForwardMs = 500;
    private const long ProbeLateralMs = 400;
    private const float ProbeMinDistance = 0.5f;
    private const int MaxProbeAttempts = 4;

    /// Minimum travel between camera-heading samples.
    private const float HeadingSampleDistance = 2.5f;

    /// How strongly each new sample pulls the camera-heading estimate.
    private const float HeadingBlend = 0.2f;

    private const float StuckDistance = 0.7f;
    private const long StuckWindowMs = 3000;

    private readonly IGameConfig gameConfig;
    private readonly Configuration configuration;
    private readonly MovementOverride movementOverride;
    private readonly MountController mountController;

    private bool running;
    private Phase phase = Phase.Idle;
    private float arriveWithin;

    /// Points to walk through, last one being the actual target.
    private IReadOnlyList<Vector3> route = [];
    private int routeIndex;

    /// Where the driver is steering right now - the current waypoint, or the target on the last leg.
    private Vector3 destination => routeIndex < route.Count ? route[routeIndex] : Vector3.Zero;

    /// How close counts as reaching a route corner.
    private const float WaypointTolerance = 1f;

    private MoveCombo? heldCombo;
    private long comboEngagedTick;

    private long phaseStartTick;
    private int probeAttempts;
    private Vector3 probeStartPosition;
    private float probeForwardBearing;

    private float cameraHeading;

    private Vector3 headingSampleAnchor;
    private Vector3 stuckAnchor;
    private long stuckAnchorTick;

    /// Re-plans a route to the final destination from wherever the character now is.
    private Func<Vector3, IReadOnlyList<Vector3>>? replan;
    private Vector3 finalDestination;

    private long runStartedTick;

    /// When a run was last ended by the player taking the controls.
    public long LastCancelledTick { get; private set; }

    private int recoveryAttempts;
    private long recoveryUntilTick;
    private bool recoverLeft;

    public MovementStatus Status { get; private set; } = MovementStatus.Idle;
    public string StatusDetail { get; private set; } = string.Empty;
    public bool IsRunning => running;
    public Vector3 Destination => destination;

    /// Where this run actually ends, as opposed to the corner being walked to now.
    public Vector3 FinalDestination => finalDestination;
    public string TargetLabel { get; private set; } = string.Empty;

    public float LastDistance { get; private set; }
    public float CameraHeading => cameraHeading;

    /// Whether steering is using the game's camera angle rather than the inferred one.
    public bool UsingLiveCamera { get; private set; }
    public float LastRelativeBearing { get; private set; }

    /// Which leg of the route is in progress, for the readout.
    public string RouteProgress => route.Count == 0 ? "-" : $"{routeIndex + 1}/{route.Count}";

    /// True when steering through the game's own input handling; false when falling back to synthesised keys,
    /// which is worth knowing since only the fallback needs window focus.
    public bool UsingHook => movementOverride.Available;
    public string HookUnavailableReason => movementOverride.UnavailableReason;

    public string CurrentCombo => UsingHook
        ? $"{movementOverride.Direction.X:F2},{movementOverride.Direction.Y:F2}"
        : heldCombo?.Label ?? "-";

    public bool? LateralProbePositive => configuration.LateralProbePositive;
    public bool IsCalibrated => configuration.LateralProbePositive.HasValue;
    public uint? ObservedMoveMode { get; private set; }

    public MovementDriver(
        IGameConfig gameConfig, Configuration configuration,
        MovementOverride movementOverride, MountController mountController)
    {
        this.gameConfig = gameConfig;
        this.configuration = configuration;
        this.movementOverride = movementOverride;
        this.mountController = mountController;
    }

    /// Begins a run along plannedRoute, whose last element is the actual target.
    public void Start(
        IReadOnlyList<Vector3> plannedRoute, float arriveWithinYalms, string label = "",
        Func<Vector3, IReadOnlyList<Vector3>>? replanner = null)
    {
        Stop();

        if (plannedRoute.Count == 0)
            return;

        TargetLabel = label;
        route = plannedRoute;
        routeIndex = 0;
        replan = replanner;
        finalDestination = plannedRoute[^1];
        recoveryAttempts = 0;
        arriveWithin = MathF.Max(arriveWithinYalms, 2f);
        running = true;
        StatusDetail = string.Empty;

        phaseStartTick = 0;
        probeAttempts = 0;
        stuckAnchorTick = 0;

        runStartedTick = Environment.TickCount64;

        ReadMoveMode();
        mountController.Reset();

        if (configuration.HasFullCalibration && CameraReader.Heading.HasValue)
        {
            RestoreCameraAlignment();
            BeginSteering();
            return;
        }

        BeginProbing();
    }

    /// Gets off the mount.
    public void Dismount() => mountController.TryDismount();

    public void Stop()
    {
        ReleaseAll();

        if (running)
            Status = MovementStatus.Idle;

        running = false;
        phase = Phase.Idle;
        StatusDetail = string.Empty;
    }

    /// Call once per framework tick while a run is active.
    public void Tick(Vector3 playerPosition)
    {
        if (!running)
            return;

        if (movementOverride.LastPlayerInputTick > runStartedTick)
        {
            ReleaseAll();
            running = false;
            phase = Phase.Idle;
            Status = MovementStatus.Cancelled;
            StatusDetail = "Cancelled - you took the controls.";
            LastCancelledTick = Environment.TickCount64;
            return;
        }

        if (!UsingHook && !KeyboardInput.IsGameFocused)
        {
            ReleaseAll();
            Status = MovementStatus.WaitingForFocus;
            StatusDetail = "Click back into the game to resume.";
            return;
        }

        KeepMounted(playerPosition);

        switch (phase)
        {
            case Phase.ProbeForward:
                TickProbe(playerPosition, relative: 0f, ProbeForwardMs, OnForwardProbed);
                break;
            case Phase.ProbeLateral:
                TickProbe(playerPosition, relative: MathF.PI / 2f, ProbeLateralMs, OnLateralProbed);
                break;
            case Phase.Steering:
                TickSteering(playerPosition);
                break;
            case Phase.Recovering:
                TickRecovering(playerPosition);
                break;
        }
    }

    /// Moves in one fixed direction for a fixed time and reports the bearing actually travelled.
    private void BeginProbing()
    {
        phase = Phase.ProbeForward;
        Status = MovementStatus.Probing;
        StatusDetail = "Getting my bearings...";
        phaseStartTick = 0;
        probeAttempts = 0;
    }

    /// Summons a mount if the character is on foot, without interrupting anything.
    public bool SuppressMount { get; set; }

    /// How much walking makes a mount worth summoning.
    private const float MountWorthwhileDistance = 60f;

    /// What is left of the route from here: the leg being walked plus every leg after it.
    private float RemainingDistance(Vector3 playerPosition)
    {
        if (routeIndex >= route.Count)
            return 0f;

        var total = Vector3.Distance(playerPosition, route[routeIndex]);
        for (var i = routeIndex; i < route.Count - 1; i++)
            total += Vector3.Distance(route[i], route[i + 1]);

        return total;
    }

    private void KeepMounted(Vector3 playerPosition)
    {
        if (SuppressMount || !configuration.AutoMount || !mountController.CanTryMount)
            return;

        if (RemainingDistance(playerPosition) < MountWorthwhileDistance)
            return;

        mountController.TryMount();
    }

    private void TickProbe(Vector3 playerPosition, float relative, long durationMs, Action<float> onMeasured)
    {
        var now = Environment.TickCount64;

        if (phaseStartTick == 0)
        {
            probeStartPosition = playerPosition;
            phaseStartTick = now;
            ApplyRawDirection(relative);
            return;
        }

        if (now - phaseStartTick < durationMs)
            return;

        ReleaseAll();

        var travelled = playerPosition - probeStartPosition;
        var flat = new Vector2(travelled.X, travelled.Z);

        if (flat.Length() < ProbeMinDistance)
        {
            if (++probeAttempts >= MaxProbeAttempts)
            {
                Fail("Couldn't get moving - try again somewhere with room to walk.");
                return;
            }

            phaseStartTick = 0;
            return;
        }

        onMeasured(MathF.Atan2(flat.X, flat.Y));
    }

    private void OnForwardProbed(float bearing)
    {
        probeForwardBearing = bearing;
        cameraHeading = bearing;

        phaseStartTick = 0;
        probeAttempts = 0;

        if (IsCalibrated)
        {
            BeginSteering();
        }
        else
        {
            phase = Phase.ProbeLateral;
            StatusDetail = "Checking which way is which...";
        }
    }

    /// The lateral axis produces a bearing a quarter turn from forward.
    private void OnLateralProbed(float bearing)
    {
        configuration.LateralProbePositive = NormalizeAngle(bearing - probeForwardBearing) > 0;
        configuration.Save();

        phaseStartTick = 0;
        probeAttempts = 0;
        BeginSteering();
    }

    private void BeginSteering()
    {
        phase = Phase.Steering;
        Status = MovementStatus.Running;
        StatusDetail = string.Empty;
        stuckAnchorTick = 0;
        headingSampleAnchor = Vector3.Zero;
    }

    private void TickSteering(Vector3 playerPosition)
    {
        var toTarget = destination - playerPosition;

        var flatDistance = new Vector2(toTarget.X, toTarget.Z).Length();
        LastDistance = flatDistance;

        var onFinalLeg = routeIndex >= route.Count - 1;

        var reached = flatDistance <= (onFinalLeg ? arriveWithin : WaypointTolerance)
                      || (!onFinalLeg && HasPassed(playerPosition, toTarget));

        if (reached)
        {
            if (!onFinalLeg)
            {
                routeIndex++;
                stuckAnchorTick = 0;
                return;
            }

            ReleaseAll();
            running = false;
            phase = Phase.Idle;
            Status = MovementStatus.Arrived;
            StatusDetail = $"Arrived (within {arriveWithin:F0}y).";
            return;
        }

        RefineCameraHeading(playerPosition);

        UpdateStuckDetection(playerPosition);
        if (Status == MovementStatus.Blocked)
            return;

        var heading = AlignedCameraHeading ?? cameraHeading;
        UsingLiveCamera = AlignedCameraHeading.HasValue;

        var course = Deflect(playerPosition, new Vector2(toTarget.X, toTarget.Z));

        var desiredBearing = MathF.Atan2(course.X, course.Y);
        var relative = NormalizeAngle(desiredBearing - heading);
        LastRelativeBearing = relative;

        ApplyRawDirection(ToRawAxis(relative));
        Status = MovementStatus.Running;
    }

    /// Where the monsters are, refreshed from outside so the driver never reads the object table itself.
    public IReadOnlyList<Vector3> Threats { get; set; } = [];

    /// Whether a point has room to stand and turn.
    public Func<Vector3, bool>? IsOpenGround { get; set; }

    /// How close a monster has to be before the course bends around it.
    private const float ThreatRadius = 18f;

    /// The most the course may bend.
    private const float MaxDeflection = 70f * MathF.PI / 180f;

    /// How far ahead to check there is somewhere to bend into.
    private const float DeflectionProbe = 5f;

    /// Room a sidestep needs at that point before it is worth taking.
    private const float DeflectionClearance = 2f;

    /// Bends the course away from anything nearby, while still going where it was sent.
    public int ThreatsPushing { get; private set; }

    public float DeflectionDegrees { get; private set; }

    public bool DeflectionBlocked { get; private set; }

    /// How hard the push counts against the course it is bending.
    private const float PushStrength = 3f;

    private Vector2 Deflect(Vector3 playerPosition, Vector2 toTarget)
    {
        ThreatsPushing = 0;
        DeflectionDegrees = 0f;
        DeflectionBlocked = false;

        if (!configuration.AvoidMonsters || Threats.Count == 0 || toTarget.LengthSquared() < 0.01f)
            return toTarget;

        var course = Vector2.Normalize(toTarget);
        var push = Vector2.Zero;

        foreach (var threat in Threats)
        {
            var away = new Vector2(playerPosition.X - threat.X, playerPosition.Z - threat.Z);
            var distance = away.Length();

            if (distance >= ThreatRadius || distance < 0.01f)
                continue;

            var toThreat = -away / distance;
            if (Vector2.Dot(course, toThreat) <= 0f)
                continue;

            ThreatsPushing++;
            var closeness = MathF.Sqrt(1f - (distance / ThreatRadius));
            push += (away / distance) * closeness * PushStrength;
        }

        if (push.LengthSquared() < 0.0001f)
            return toTarget;

        var bent = Vector2.Normalize(course + push);

        var turn = MathF.Atan2(bent.X, bent.Y) - MathF.Atan2(course.X, course.Y);
        turn = NormalizeAngle(turn);

        if (MathF.Abs(turn) > MaxDeflection)
            turn = MathF.Sign(turn) * MaxDeflection;

        var original = MathF.Atan2(course.X, course.Y);
        var steered = new Vector2(MathF.Sin(original + turn), MathF.Cos(original + turn));

        var ahead = playerPosition + new Vector3(steered.X, 0f, steered.Y) * DeflectionProbe;
        if (IsOpenGround?.Invoke(ahead) != true)
        {
            DeflectionBlocked = true;
            return toTarget;
        }

        DeflectionDegrees = turn * 180f / MathF.PI;
        return steered * toTarget.Length();
    }

    /// Whether the current corner is behind the character, measured along the leg being walked.
    private bool HasPassed(Vector3 playerPosition, Vector3 toTarget)
    {
        if (routeIndex <= 0 || routeIndex >= route.Count)
            return false;

        var from = route[routeIndex - 1];
        var corner = route[routeIndex];

        var leg = new Vector2(corner.X - from.X, corner.Z - from.Z);
        if (leg.LengthSquared() < 0.01f)
            return false;

        leg = Vector2.Normalize(leg);
        var remaining = new Vector2(toTarget.X, toTarget.Z);

        return Vector2.Dot(remaining, leg) < -0.5f;
    }


    /// Converts a bearing relative to camera-forward into the driver's own axis convention, flipping it when
    /// the lateral probe found the axis runs the other way.
    private float ToRawAxis(float relative) =>
        (configuration.LateralProbePositive ?? true) ? relative : -relative;

    /// Applies a direction expressed in the raw axis convention: 0 is forward, a positive quarter turn is
    /// whatever the lateral probe moved the character toward.
    private void ApplyRawDirection(float rawRelative)
    {
        if (UsingHook)
        {
            movementOverride.Direction = new Vector2(MathF.Sin(rawRelative), MathF.Cos(rawRelative));
            movementOverride.Enabled = true;
            return;
        }

        SelectCombo(rawRelative);
    }

    private void SelectCombo(float rawRelative)
    {
        var now = Environment.TickCount64;

        var best = Combos[0];
        var bestError = MathF.PI * 2f;
        foreach (var combo in Combos)
        {
            var error = MathF.Abs(NormalizeAngle(rawRelative - combo.Offset));
            if (error < bestError)
            {
                bestError = error;
                best = combo;
            }
        }

        if (heldCombo is { } current)
        {
            if (current.Label == best.Label)
                return;

            var currentError = MathF.Abs(NormalizeAngle(rawRelative - current.Offset));
            if (now - comboEngagedTick < MinComboHoldMs || currentError - bestError < ComboSwitchMargin)
                return;
        }

        HoldCombo(best);
    }

    /// Keeps the camera heading current, because every direction the driver chooses is expressed relative to
    /// it and the player can swing it at any moment.
    private void RefineCameraHeading(Vector3 playerPosition)
    {
        var appliedRaw = UsingHook
            ? (movementOverride.Enabled
                ? MathF.Atan2(movementOverride.Direction.X, movementOverride.Direction.Y)
                : (float?)null)
            : heldCombo?.Offset;

        if (appliedRaw is not { } raw)
        {
            headingSampleAnchor = Vector3.Zero;
            return;
        }

        if (headingSampleAnchor == Vector3.Zero)
        {
            headingSampleAnchor = playerPosition;
            return;
        }

        var travelled = playerPosition - headingSampleAnchor;
        var flat = new Vector2(travelled.X, travelled.Z);
        if (flat.Length() < HeadingSampleDistance)
            return;

        headingSampleAnchor = playerPosition;

        var implied = NormalizeAngle(MathF.Atan2(flat.X, flat.Y) - ToRawAxis(raw));

        cameraHeading = NormalizeAngle(cameraHeading + (NormalizeAngle(implied - cameraHeading) * HeadingBlend));

        AlignCameraReading(implied);
    }

    /// How the game's camera angle lines up with the driver's own bearing convention: a constant offset, and
    /// which way round the two run.
    private float offsetSameSense;
    private float offsetOppositeSense;
    private float errorSameSense = float.MaxValue;
    private float errorOppositeSense = float.MaxValue;
    private int cameraSamples;

    /// Samples needed before the game's camera angle is trusted over the inferred one.
    private const int CameraSamplesNeeded = 3;

    private void AlignCameraReading(float implied)
    {
        if (CameraReader.Heading is not { } raw)
            return;

        Fit(raw, ref offsetSameSense, ref errorSameSense);
        Fit(-raw, ref offsetOppositeSense, ref errorOppositeSense);
        cameraSamples++;

        RememberCameraAlignment();

        void Fit(float angle, ref float offset, ref float error)
        {
            var predicted = NormalizeAngle(angle + offset);
            var miss = MathF.Abs(NormalizeAngle(implied - predicted));

            error = error == float.MaxValue ? miss : (error * 0.7f) + (miss * 0.3f);
            var target = NormalizeAngle(implied - angle);
            offset = cameraSamples == 0
                ? target
                : NormalizeAngle(offset + (NormalizeAngle(target - offset) * 0.4f));
        }
    }

    /// Stores the alignment once it has settled, so no future run has to walk to find it again.
    private void RememberCameraAlignment()
    {
        if (cameraSamples < CameraSamplesNeeded)
            return;

        var opposite = errorOppositeSense < errorSameSense;
        var offset = opposite ? offsetOppositeSense : offsetSameSense;

        if (configuration.CameraOppositeSense == opposite
            && configuration.CameraOffset is { } stored
            && MathF.Abs(NormalizeAngle(stored - offset)) < 0.02f)
            return;

        configuration.CameraOppositeSense = opposite;
        configuration.CameraOffset = offset;
        configuration.Save();
    }

    /// Loads a previously measured alignment, so steering can start on the first frame.
    private void RestoreCameraAlignment()
    {
        if (configuration.CameraOffset is not { } offset || configuration.CameraOppositeSense is not { } opposite)
            return;

        offsetSameSense = opposite ? 0f : offset;
        offsetOppositeSense = opposite ? offset : 0f;
        errorSameSense = opposite ? 10f : 0f;
        errorOppositeSense = opposite ? 0f : 10f;
        cameraSamples = CameraSamplesNeeded;

        if (CameraReader.Heading is { } raw)
            cameraHeading = NormalizeAngle(opposite ? -raw + offset : raw + offset);
    }

    /// The camera's heading straight from the game, once the alignment is known.
    private float? AlignedCameraHeading
    {
        get
        {
            if (cameraSamples < CameraSamplesNeeded || CameraReader.Heading is not { } raw)
                return null;

            return errorSameSense <= errorOppositeSense
                ? NormalizeAngle(raw + offsetSameSense)
                : NormalizeAngle(-raw + offsetOppositeSense);
        }
    }

    private void UpdateStuckDetection(Vector3 playerPosition)
    {
        var now = Environment.TickCount64;

        if (stuckAnchorTick == 0)
        {
            stuckAnchor = playerPosition;
            stuckAnchorTick = now;
            return;
        }

        if (now - stuckAnchorTick < StuckWindowMs)
            return;

        var moved = Vector3.Distance(playerPosition, stuckAnchor);
        stuckAnchor = playerPosition;
        stuckAnchorTick = now;

        if (moved >= StuckDistance)
            return;

        if (recoveryAttempts >= MaxRecoveries)
        {
            Fail("Stopped - can't find a way through. Try somewhere the graph knows better.");
            return;
        }

        recoveryAttempts++;
        recoverLeft = !recoverLeft;
        recoveryUntilTick = now + RecoveryMs;
        phase = Phase.Recovering;
        Status = MovementStatus.Probing;
        StatusDetail = $"Stuck - working around it ({recoveryAttempts}/{MaxRecoveries})...";
    }

    private void TickRecovering(Vector3 playerPosition)
    {
        if (Environment.TickCount64 < recoveryUntilTick)
        {
            var sidestep = LastRelativeBearing + (recoverLeft ? -MathF.PI / 2f : MathF.PI / 2f);
            ApplyRawDirection(ToRawAxis(NormalizeAngle(sidestep)));
            return;
        }

        ReleaseAll();

        route = replan?.Invoke(finalDestination) ?? [finalDestination];
        routeIndex = 0;
        stuckAnchorTick = 0;
        headingSampleAnchor = Vector3.Zero;

        phase = Phase.Steering;
        Status = MovementStatus.Running;
        StatusDetail = string.Empty;
    }

    private void Fail(string reason)
    {
        ReleaseAll();
        running = false;
        phase = Phase.Idle;
        Status = MovementStatus.Blocked;
        StatusDetail = reason;
    }

    /// Reads the player's movement mode for the diagnostics readout only, and deliberately doesn't change it
    /// - neither path cares which mode is set, so there's nothing to gain from writing to a setting that
    /// belongs to the player.
    private void ReadMoveMode()
    {
        try
        {
            if (gameConfig.TryGet(UiControlOption.MoveMode, out uint current))
                ObservedMoveMode = current;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[EchoNav] Could not read the movement mode");
        }
    }

    private void HoldCombo(MoveCombo combo)
    {
        ReleaseKeys();

        foreach (var key in combo.Keys)
            KeyboardInput.Press(key);

        heldCombo = combo;
        comboEngagedTick = Environment.TickCount64;
        headingSampleAnchor = Vector3.Zero;
    }

    private void ReleaseKeys()
    {
        if (heldCombo is { } combo)
        {
            foreach (var key in combo.Keys)
                KeyboardInput.Release(key);
        }

        heldCombo = null;
    }

    /// Every exit path goes through here.
    public void ReleaseAll()
    {
        movementOverride.Enabled = false;
        movementOverride.Direction = Vector2.Zero;
        ReleaseKeys();
        headingSampleAnchor = Vector3.Zero;
    }

    private static float NormalizeAngle(float radians)
    {
        while (radians > MathF.PI) radians -= MathF.Tau;
        while (radians < -MathF.PI) radians += MathF.Tau;
        return radians;
    }
}
