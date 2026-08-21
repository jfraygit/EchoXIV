using System;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using EchoNav.Game;
using EchoNav.Nav;

namespace EchoNav.UI;

/// The first build's whole purpose: show what the two target sources actually contain in a live zone, so
/// three questions got settled by observation instead of assumption.
public sealed class DebugWindow(Plugin plugin) : Window("EchoNav diagnostics###EchoNavDebug")
{
    private string statusMessage = string.Empty;
    private Vector4 statusColor = new(0.7f, 0.7f, 0.7f, 1f);

    /// Aethernet row labels, read on demand.
    private IReadOnlyList<string> rowLabels = [];

    private int callbackIndex = 1;

    private static readonly Vector4 Good = new(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 Bad = new(0.95f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 Warn = new(0.95f, 0.78f, 0.40f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.65f, 1f);

    public override void Draw()
    {
        var snapshot = plugin.Snapshot;

        DrawEnvironment(snapshot);
        ImGui.Separator();
        DrawDriverStatus(snapshot);
        ImGui.Separator();
        DrawCriticalEncounters(snapshot);
        ImGui.Separator();
        DrawFates(snapshot);
        ImGui.Separator();
        DrawFooter(snapshot);
    }

    private static void DrawEnvironment(NavSnapshot snapshot)
    {
        ImGui.TextUnformatted($"Territory: {snapshot.TerritoryType}");
        ImGui.TextUnformatted(snapshot.HasPlayer
            ? $"Player: {Format(snapshot.PlayerPosition)}"
            : "Player: (not loaded)");
    }

    private void DrawDriverStatus(NavSnapshot snapshot)
    {
        var driver = plugin.MovementDriver;

        ImGui.TextUnformatted("Movement:");
        ImGui.SameLine();
        var color = driver.Status switch
        {
            MovementStatus.Running or MovementStatus.Probing or MovementStatus.Arrived => Good,
            MovementStatus.Blocked => Bad,
            MovementStatus.WaitingForFocus => Warn,
            _ => Dim,
        };
        ImGui.TextColored(color, driver.Status.ToString());

        if (driver.IsRunning && !string.IsNullOrEmpty(driver.TargetLabel))
        {
            ImGui.SameLine();
            ImGui.TextUnformatted($"-> {driver.TargetLabel} {Format(driver.Destination)}");
        }

        if (!string.IsNullOrEmpty(driver.StatusDetail))
        {
            ImGui.SameLine();
            ImGui.TextColored(Dim, $"- {driver.StatusDetail}");
        }

        ImGui.TextColored(Dim, driver.IsCalibrated
            ? $"steering: {(driver.UsingHook ? "game hook" : "WASD fallback")}  lateral {(driver.LateralProbePositive == true ? "+" : "-")}ve  " +
              $"moveMode={driver.ObservedMoveMode?.ToString() ?? "?"}  camera={(driver.UsingLiveCamera ? "live" : "inferred")}"
            : "not calibrated yet - the first run measures the controls (takes about a second)");

        var willProbe = !plugin.Configuration.HasFullCalibration;
        ImGui.TextColored(willProbe ? Warn : Good,
            willProbe
                ? "start-up: will measure the controls first (one short walk, then never again)"
                : "start-up: goes straight down the route, no measuring walk");

        var mount = plugin.MountController;
        var autoMount = plugin.Configuration.AutoMount;
        if (ImGui.Checkbox("auto-mount##automount", ref autoMount))
        {
            plugin.Configuration.AutoMount = autoMount;
            plugin.Configuration.Save();
        }

        ImGui.SameLine();
        ImGui.TextColored(mount.IsMounted ? Good : Dim,
            mount.IsMounted ? "mounted" : $"on foot (last mount status {mount.LastStatus})");

        if (driver.IsRunning)
        {
            ImGui.TextColored(Dim,
                $"camera={driver.CameraHeading:F2}  relBearing={driver.LastRelativeBearing:F2}  " +
                $"dir={driver.CurrentCombo}  dist={driver.LastDistance:F1}  leg={driver.RouteProgress}");

            if (plugin.Configuration.AvoidMonsters)
            {
                ImGui.TextColored(driver.DeflectionBlocked ? Warn : driver.DeflectionDegrees != 0f ? Good : Dim,
                    $"avoid: nearby={plugin.BattleNpcsInRange} seen={driver.Threats.Count} pushing={driver.ThreatsPushing} " +
                    $"kl={plugin.PhantomState.KnowledgeLevel} " +
                    $"bend={driver.DeflectionDegrees:F0}deg" +
                    (driver.DeflectionBlocked ? "  (no room to step aside)" : string.Empty));
            }
        }

        DrawPotStatus(snapshot);
        DrawPhantomStatus();
        DrawMeshStatus(snapshot);

        var journey = plugin.Journey;
        if (journey.Stage != JourneyStage.Idle)
        {
            ImGui.TextUnformatted("Journey:");
            ImGui.SameLine();
            ImGui.TextColored(
                journey.Stage == JourneyStage.Failed ? Bad : journey.Stage == JourneyStage.Done ? Good : Warn,
                $"{journey.Stage} - {journey.Detail}");
        }

        if (ImGui.Button("Stop##navstop"))
        {
            plugin.Journey.Stop();
            SetStatus("Stopped.", Dim);
        }

        ImGui.SameLine();
        var recorder = plugin.TrailRecorder;
        if (ImGui.Button(recorder.IsRecording ? "Stop recording##trail" : "Record trail##trail"))
        {
            if (recorder.IsRecording)
                recorder.Stop(snapshot.TerritoryType);
            else
                recorder.Start(snapshot.TerritoryType);
        }

        if (!string.IsNullOrEmpty(recorder.Detail))
        {
            ImGui.SameLine();
            ImGui.TextColored(recorder.IsRecording ? Good : Dim, recorder.Detail);
        }

        ImGui.SameLine();
        if (ImGui.Button("Recalibrate##navcal"))
        {
            plugin.MovementDriver.Stop();
            plugin.Configuration.ClearCalibration();
            SetStatus("Calibration cleared - the next Go will measure again.", Dim);
        }

        if (!driver.UsingHook && !KeyboardInput.IsGameFocused)
        {
            ImGui.SameLine();
            ImGui.TextColored(Warn, "(game not focused - keys can't be sent)");
        }
    }

    /// Navmesh status.
    private void DrawPotStatus(NavSnapshot snapshot)
    {
        var config = plugin.Configuration;
        var pots = snapshot.Pots;

        var reader = plugin.InstanceReader;
        var directorStart = reader.DirectorStart;
        var age = directorStart > 0 ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() - directorStart : 0;

        ImGui.TextColored(directorStart > 0 ? Dim : Warn,
            $"instance: director={directorStart} via {reader.Source} ({reader.DirectorCount} in list)" +
            (directorStart > 0 ? $" age {age / 60}:{age % 60:D2}" : string.Empty) +
            $"  publicInstance={reader.PublicInstanceId}  zoneVisit={config.ZoneVisit}");

        ImGui.TextColored(Dim, $"   director changes this session: {reader.Changes} (entry time, not instance age)");

        if (config.PotLastSpawnEpoch <= 0)
        {
            ImGui.TextColored(Dim, "pots: nothing recorded yet");
        }
        else
        {
            var stale = config.PotLastSpawnVisit != config.ZoneVisit;
            var sinceEntry = directorStart > 0 ? config.PotLastSpawnEpoch - directorStart : 0;
            var recorded = DateTimeOffset.FromUnixTimeSeconds(config.PotLastSpawnEpoch).ToLocalTime();

            ImGui.TextColored(stale ? Warn : pots.IsUp ? Good : Dim,
                $"pots: last {config.PotLastSpawnSide} at {recorded:HH:mm:ss} " +
                $"({(stale ? "stale, other instance" : $"{sinceEntry / 60}:{sinceEntry % 60:D2} after entry")})" +
                "  next " +
                $"{(string.IsNullOrEmpty(pots.NextSide) ? "?" : pots.NextSide)} in " +
                $"{pots.SecondsUntilNext / 60}:{pots.SecondsUntilNext % 60:D2}");
        }

        ImGui.TextColored(Dim,
            $"   north {(config.PotNorthPosition is { } n ? Format(n) : "not seen")}   " +
            $"south {(config.PotSouthPosition is { } s ? Format(s) : "not seen")}");
    }

    /// Where the buff routine is, and what it thinks the character can produce.
    private void DrawPhantomStatus()
    {
        var buffer = plugin.PhantomBuffer;
        var state = plugin.PhantomState;

        if (!state.Available)
        {
            ImGui.TextColored(Dim, "phantom: no Occult Crescent state");
            return;
        }

        var colour = buffer.Stage switch
        {
            PhantomBuffStage.Failed => Warn,
            PhantomBuffStage.Idle => Dim,
            _ => Good,
        };

        ImGui.TextColored(colour,
            $"phantom: {buffer.Stage}  on Ph. {EchoNav.Game.PhantomJobs.NameOf(state.CurrentJob)}" +
            $"  knowledge={state.KnowledgeLevel} ({state.KnowledgePoints}/{state.KnowledgeNeeded})" +
            $"  gaps=[{string.Join(" ", state.UnknownBytes)}]" +
            $"  auto={plugin.Configuration.PhantomBuffAtCrystals}" +
            (string.IsNullOrEmpty(buffer.Detail) ? string.Empty : $"  \"{buffer.Detail}\""));

        ImGui.TextColored(Dim, "   " + string.Join("  ", EchoNav.Game.PhantomJobs.Buffs.Select(b =>
            $"{b.JobName} {state.LevelOf(b.JobId)}/{b.JobLevel}")) +
            $"  Freelancer {state.LevelOf(EchoNav.Game.PhantomJobs.Freelancer)}/{EchoNav.Game.PhantomJobs.InquiringMindLevel}");
    }

    private void DrawMeshStatus(NavSnapshot snapshot)
    {
        var (label, colour) = snapshot.MeshState switch
        {
            NavMeshState.Ready => ("ready", Good),
            NavMeshState.Building => ("building", Warn),
            NavMeshState.Loading => ("loading", Warn),
            NavMeshState.Failed => ("failed", Bad),
            _ => ("none", Dim),
        };

        ImGui.TextUnformatted("navmesh:");
        ImGui.SameLine();
        ImGui.TextColored(colour, label);

        if (!string.IsNullOrEmpty(snapshot.MeshDetail))
        {
            ImGui.SameLine();
            ImGui.TextColored(Dim, $"({snapshot.MeshDetail})");
        }

        if (plugin.Router.LastPlanMs > 0)
        {
            var router = plugin.Router;
            ImGui.SameLine();

            ImGui.TextColored(
                !router.LastPlanReachedGoal ? Bad : router.LastPlanMs > 30 ? Warn : Dim,
                $"last route: {router.LastPlanMs:F0}ms ({router.LastSearchMs:F0}ms searching), " +
                $"{router.LastPlanPoints} pts" +
                (router.LastPlanReachedGoal ? string.Empty : "  UNREACHABLE - ends in a straight line"));
        }

        if (snapshot.MeshState == NavMeshState.Building)
            ImGui.TextColored(Dim, "first visit to a zone without a prebuilt mesh - this takes about a minute, once");

        DrawAetherytes(snapshot);
    }

    /// Shards discovered so far, and whether the graph can route from each of them.
    private void DrawAetherytes(NavSnapshot snapshot)
    {
        var shards = plugin.AetheryteRegistry.Current;

        var routable = 0;
        foreach (var shard in shards)
        {
            if (plugin.Router.IsReachable(shard.Position))
                routable++;
        }

        var ret = plugin.ReturnAction;
        ImGui.TextColored(Dim, "Occult Return:");
        ImGui.SameLine();
        if (ret.ActionId is { } id)
        {
            var usable = ret.IsAvailable;
            ImGui.TextColored(usable ? Good : Warn, $"action {id}{(usable ? " (usable)" : $" (status {ret.LastStatus})")}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Use##testreturn"))
            {
                var ok = ret.Use();
                SetStatus(ok ? "Returning to base camp." : "Return refused.", ok ? Good : Bad);
            }
        }
        else
        {
            ImGui.TextColored(Bad, "not found in the Action sheet");
        }

        ImGui.SameLine();
        var baseCamp = plugin.AetheryteRegistry.MainAetheryte;
        ImGui.TextColored(baseCamp == null ? Warn : Dim,
            baseCamp == null ? "- base camp unidentified" : $"- returns to {baseCamp.DisplayName}");

        ImGui.TextColored(Dim, $"shards: {shards.Count} known");
        if (shards.Count > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(routable == shards.Count ? Good : Warn, $"({routable} reachable by graph)");
        }

        if (shards.Count > 0 && ImGui.CollapsingHeader("shard list##shards"))
        {
            foreach (var shard in shards)
            {
                var distance = snapshot.HasPlayer ? Vector3.Distance(snapshot.PlayerPosition, shard.Position) : 0f;

                var named = !string.IsNullOrEmpty(shard.AethernetName);

                ImGui.TextColored(named ? Good : Warn,
                    $"  {(named ? shard.AethernetName : "unnamed - visit it and open the shard menu")}");
                ImGui.SameLine();
                ImGui.TextColored(Dim, $"{Format(shard.Position)}  {distance:F0}y");
            }
        }

        if (ImGui.CollapsingHeader("aethernet destinations (sheet)##aethernet"))
        {
            var destinations = plugin.AethernetDirectory.For(snapshot.TerritoryType);
            if (destinations.Count == 0)
                ImGui.TextColored(Bad, "  none listed for this territory (either column)");

            foreach (var destination in destinations)
            {
                ImGui.TextColored(Dim,
                    $"  #{destination.RowId} \"{destination.Name}\" {Format(destination.Position)}" +
                    $"  aetheryteTerritory={destination.AetheryteTerritory} levelTerritory={destination.LevelTerritory}" +
                    $"{(destination.IsMainAetheryte ? "  (main)" : string.Empty)}");
            }

            if (snapshot.HasPlayer)
            {
                ImGui.TextColored(Dim, "  nearest sheet entries to you, any zone:");
                foreach (var (destination, distance) in plugin.AethernetDirectory.NearestAnywhere(snapshot.PlayerPosition, 5))
                {
                    ImGui.TextColored(Dim,
                        $"    {distance,8:N0}y  #{destination.RowId} \"{destination.Name}\"" +
                        $"  aTerr={destination.AetheryteTerritory} lTerr={destination.LevelTerritory}");
                }
            }
        }

        if (ImGui.CollapsingHeader("aethernet window (diagnostic)##menu"))
        {
            var menu = plugin.AethernetMenu;

            ImGui.TextColored(string.IsNullOrEmpty(menu.OpenAddonName) ? Dim : Good,
                $"  window: {(string.IsNullOrEmpty(menu.OpenAddonName) ? "(none of the expected names are open)" : menu.OpenAddonName)}");

            if (!string.IsNullOrEmpty(menu.CurrentLocation))
                ImGui.TextColored(Good, $"  standing at: {menu.CurrentLocation}");

            if (ImGui.SmallButton("Read rows##readrows"))
            {
                rowLabels = menu.ReadRowLabels();
                SetStatus($"Read {rowLabels.Count} row label(s).", rowLabels.Count > 0 ? Good : Warn);
            }

            if (!string.IsNullOrEmpty(menu.LastSelectResult))
            {
                ImGui.SameLine();
                ImGui.TextColored(Warn, menu.LastSelectResult);
            }

            for (var i = 0; i < rowLabels.Count; i++)
                ImGui.TextColored(Dim, $"  [{i}] {rowLabels[i]}");

            ImGui.TextColored(Dim, "  --- teleport (highlight then confirm, automatically) ---");
            for (var i = 0; i < menu.Destinations.Count; i++)
            {
                ImGui.TextColored(Dim, $"  [{i}] {menu.Destinations[i]}");
                ImGui.SameLine();
                if (ImGui.SmallButton($"Teleport##dest{i}"))
                {
                    var ok = menu.Select(i);
                    SetStatus(ok ? $"Teleporting to {menu.Destinations[i]}." : "Selection failed.", ok ? Good : Bad);
                }
            }

            ImGui.TextColored(Dim, "  --- selection attempts (index, then each callback shape) ---");
            ImGui.SetNextItemWidth(120f);
            ImGui.InputInt("index##cbindex", ref callbackIndex);

            for (var shape = 0; shape < 4; shape++)
            {
                if (shape > 0)
                    ImGui.SameLine();

                if (ImGui.SmallButton($"shape {shape}##cb{shape}"))
                {
                    menu.TryCallback(shape, callbackIndex);
                    SetStatus($"Sent callback shape {shape} with index {callbackIndex}.", Dim);
                }
            }

            foreach (var line in menu.NodeDump)
                ImGui.TextColored(Dim, $"  {line}");

            if (menu.ValueDump.Count > 0)
            {
                ImGui.TextColored(Dim, "  --- data values ---");
                foreach (var line in menu.ValueDump)
                    ImGui.TextColored(Dim, $"  {line}");
            }

            if (ImGui.SmallButton("Copy window##copymenu"))
            {
                ImGui.SetClipboardText(
                    $"addon: {menu.OpenAddonName}\ncurrentLocation: {menu.CurrentLocation}\n" +
                    string.Join("\n", menu.NodeDump) +
                    "\n\nvalues:\n" + string.Join("\n", menu.ValueDump));
                SetStatus("Aethernet window contents copied.", Good);
            }
        }

        if (!ImGui.CollapsingHeader("nearby objects (diagnostic)##nearby"))
            return;

        var nearby = plugin.AetheryteRegistry.NearbyDiagnostic;
        if (nearby.Count == 0)
        {
            ImGui.TextColored(Dim, "  nothing within 60y");
            return;
        }

        foreach (var line in nearby)
            ImGui.TextColored(Dim, $"  {line}");

        if (ImGui.SmallButton("Copy nearby##copynearby"))
        {
            ImGui.SetClipboardText(string.Join("\n", nearby));
            SetStatus("Nearby objects copied.", Good);
        }
    }

    private void DrawCriticalEncounters(NavSnapshot snapshot)
    {
        var result = snapshot.DynamicEvents;

        ImGui.TextUnformatted("Critical Encounters (DynamicEventContainer)");

        if (!result.ContainerFound)
        {
            ImGui.TextColored(Bad, "GetInstance() returned null - no container in this zone.");
            return;
        }

        ImGui.TextColored(Good, $"Container found. CurrentEventId={result.CurrentEventId}, CurrentEventIndex={result.CurrentEventIndex}");

        if (!ImGui.BeginTable("##ceslots", 11, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("#");
        ImGui.TableSetupColumn("Id");
        ImGui.TableSetupColumn("State");
        ImGui.TableSetupColumn("Name");
        ImGui.TableSetupColumn("Marker position");
        ImGui.TableSetupColumn("Dist");
        ImGui.TableSetupColumn("Left");

        ImGui.TableSetupColumn("Staging");

        ImGui.TableSetupColumn("Prog");
        ImGui.TableSetupColumn("Route");
        ImGui.TableSetupColumn("Go");
        ImGui.TableHeadersRow();

        foreach (var slot in result.RawSlots)
        {
            var inactive = slot.State == "Inactive";

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(inactive ? Dim : Good, slot.Slot.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(slot.DynamicEventId.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(slot.State);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(string.IsNullOrEmpty(slot.Name) ? "-" : slot.Name);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Format(slot.MarkerPosition));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(snapshot.HasPlayer
                ? Vector3.Distance(snapshot.PlayerPosition, slot.MarkerPosition).ToString("F1")
                : "-");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(slot.SecondsLeft.ToString());
            ImGui.TableNextColumn();

            ImGui.TextColored(slot.StagingSecondsLeft > 0 ? Good : Dim, slot.StagingSecondsLeft.ToString());
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    $"start={slot.StartTimestamp} (now={DateTimeOffset.UtcNow.ToUnixTimeSeconds()})\n" +
                    $"registration={slot.SecondsRegistrationTime}s warmup={slot.SecondsWarmupTime}s " +
                    $"duration={slot.SecondsDuration}s");
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{slot.Progress}%");
            ImGui.TableNextColumn();

            if (plugin.Router.IsReachable(slot.MarkerPosition))
                ImGui.TextColored(Good, "ok");
            else
                ImGui.TextColored(Warn, "off-mesh");

            ImGui.TableNextColumn();
            if (slot.MarkerPosition != Vector3.Zero && ImGui.Button($"Go##ce{slot.Slot}"))
                Go(slot.MarkerPosition, slot.MarkerRadius > 1f ? slot.MarkerRadius : 15f, slot.Name);
        }

        ImGui.EndTable();
    }

    private void DrawFates(NavSnapshot snapshot)
    {
        ImGui.TextUnformatted($"FATEs (IFateTable): {snapshot.Fates.Count}");

        if (snapshot.Fates.Count == 0)
        {
            ImGui.TextColored(Dim, "None active.");
            return;
        }

        if (!ImGui.BeginTable("##fates", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("Id");
        ImGui.TableSetupColumn("Name");
        ImGui.TableSetupColumn("State");
        ImGui.TableSetupColumn("Position");
        ImGui.TableSetupColumn("Dist");
        ImGui.TableSetupColumn("Prog");
        ImGui.TableSetupColumn("Go");
        ImGui.TableHeadersRow();

        foreach (var fate in snapshot.Fates)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(fate.Id.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(fate.Name);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(fate.StateLabel);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Format(fate.Position));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(snapshot.HasPlayer
                ? Vector3.Distance(snapshot.PlayerPosition, fate.Position).ToString("F1")
                : "-");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{fate.Progress}%");
            ImGui.TableNextColumn();
            if (ImGui.Button($"Go##fate{fate.Id}"))
                Go(fate.Position, MathF.Max(fate.Radius, 5f), fate.Name);
        }

        ImGui.EndTable();
    }

    private void DrawFooter(NavSnapshot snapshot)
    {
        if (ImGui.Button("Copy full dump"))
        {
            ImGui.SetClipboardText(BuildDump(snapshot));
            SetStatus("Dump copied to clipboard.", Good);
        }

        if (!string.IsNullOrEmpty(statusMessage))
            ImGui.TextColored(statusColor, statusMessage);
    }

    private void Go(Vector3 destination, float range, string label)
    {
        if (!plugin.MovementDriver.UsingHook && !KeyboardInput.IsGameFocused)
        {
            SetStatus("The game window needs focus before EchoNav can send movement keys.", Warn);
            return;
        }

        var plan = plugin.PlanTravel(destination);

        plugin.Journey.Start(plan, range, label, plugin.PlanRoute);

        SetStatus(plan.UsesReturn
            ? $"Going to {label} via Occult Return{(plan.ToShard != null ? $" -> {plan.ToShard.AethernetName}" : string.Empty)}."
            : plan.UsesTeleport
            ? $"Going to {label} via {plan.FromShard!.AethernetName} -> {plan.ToShard!.AethernetName}."
            : plan.Route.Count > 1
                ? $"Walking to {label} via {plan.Route.Count - 1} corner(s)."
                : $"Walking straight to {label} at {Format(destination)}.", Good);
    }

    private void SetStatus(string message, Vector4 color)
    {
        statusMessage = message;
        statusColor = color;
    }

    private static string Format(Vector3 v) => $"{v.X:F1}, {v.Y:F1}, {v.Z:F1}";

    /// A plain-text version of everything on screen, for pasting somewhere it can be read properly.
    private string BuildDump(NavSnapshot snapshot)
    {
        var driver = plugin.MovementDriver;
        var sb = new StringBuilder();
        sb.AppendLine("=== EchoNav debug dump ===");
        sb.AppendLine($"Territory: {snapshot.TerritoryType}");
        sb.AppendLine(
            $"Driver: {driver.Status} camera={driver.CameraHeading:F3} relBearing={driver.LastRelativeBearing:F3} " +
            $"combo={driver.CurrentCombo} dist={driver.LastDistance:F1} " +
            $"calibrated={driver.IsCalibrated} usingHook={driver.UsingHook} lateralPositive={driver.LateralProbePositive} " +
            $"moveMode={driver.ObservedMoveMode} " +
            $"focused={KeyboardInput.IsGameFocused}");
        sb.AppendLine($"Player: {(snapshot.HasPlayer ? Format(snapshot.PlayerPosition) : "(not loaded)")}");
        sb.AppendLine();

        var result = snapshot.DynamicEvents;
        sb.AppendLine($"--- DynamicEventContainer: {(result.ContainerFound ? "FOUND" : "NULL")} ---");
        if (result.ContainerFound)
        {
            sb.AppendLine($"CurrentEventId={result.CurrentEventId} CurrentEventIndex={result.CurrentEventIndex}");
            foreach (var slot in result.RawSlots)
            {
                sb.AppendLine(
                    $"[{slot.Slot,2}] id={slot.DynamicEventId} state={slot.State} type={slot.EventType} " +
                    $"name=\"{slot.Name}\" pos=({Format(slot.MarkerPosition)}) icon={slot.MarkerIconId} " +
                    $"radius={slot.MarkerRadius:F1} markerTerritory={slot.MarkerTerritoryTypeId} markerMap={slot.MarkerMapId} " +
                    $"secondsLeft={slot.SecondsLeft} staging={slot.StagingSecondsLeft} " +
                    $"start={slot.StartTimestamp} registration={slot.SecondsRegistrationTime} " +
                    $"warmup={slot.SecondsWarmupTime} duration={slot.SecondsDuration} " +
                    $"progress={slot.Progress} " +
                    $"participants={slot.Participants}/{slot.MaxParticipants}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"--- FATE table: {snapshot.Fates.Count} ---");
        foreach (var fate in snapshot.Fates)
        {
            sb.AppendLine(
                $"id={fate.Id} name=\"{fate.Name}\" state={fate.StateLabel} pos=({Format(fate.Position)}) " +
                $"radius={fate.Radius:F1} progress={fate.Progress} level={fate.Level} mapIcon={fate.MapIconId} " +
                $"secondsLeft={fate.SecondsRemaining}");
        }

        return sb.ToString();
    }
}
