using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;

namespace EchoGlam.UI.Foundation;

/// Reserved z-layers within one ImGui draw list, so that a surface can paint its own background AFTER
/// measuring the content that decides its height - at any nesting depth.
internal enum Layer
{
    Decor0 = 0,
    Decor1 = 1,
    Decor2 = 2,
    Decor3 = 3,

    /// Everything that is not a surface's own background.
    Content = 4,
}

/// Holds one open layer-stack scope.
public readonly struct LayerScope : IDisposable
{
    /// False for a scope that was refused - see LayerStack.Begin.
    private readonly bool owns;

    internal LayerScope(bool owns) => this.owns = owns;

    public void Dispose()
    {
        if (owns)
            LayerStack.End();
    }
}

public static class LayerStack
{
    /// Decor0..Decor3 plus Content.
    public const int Count = 5;

    /// How many surfaces may nest before their backgrounds start sharing a layer.
    public const int MaxDepth = 4;

    /// THE DRAW LIST IS STORED, NOT RE-ASKED FOR.
    private record struct Scope(ImDrawListPtr DrawList, nint Identity, int Depth, bool Explicit);

    private static readonly List<Scope> Scopes = [];

    /// Whether anything is currently splitting a draw list.
    public static bool Active => Scopes.Count > 0;

    /// How many surfaces are open on the draw list being drawn into.
    public static int Depth
    {
        get
        {
            var index = Find(Identity(ImGui.GetWindowDrawList()));
            return index < 0 ? 0 : Scopes[index].Depth;
        }
    }

    /// Opens the layer stack for the draw list of the window or child being drawn.
    public static LayerScope Begin()
    {
        var drawList = ImGui.GetWindowDrawList();
        var identity = Identity(drawList);

        for (var i = 0; i < Scopes.Count; i++)
        {
            if (Scopes[i].Identity != identity)
                continue;

            if (Build.Diagnostics)
            {
                Plugin.Log.Error(
                    "[EchoGlam] LayerStack.Begin called twice for one draw list (depth {0}). " +
                    "A scope is opened per window and per BeginChild; this one is already open.",
                    Scopes.Count);
            }

            return new LayerScope(false);
        }

        Open(drawList, identity, explicitly: true);
        return new LayerScope(true);
    }

    private static int Open(ImDrawListPtr drawList, nint identity, bool explicitly)
    {
        drawList.ChannelsSplit(Count);

        drawList.ChannelsSetCurrent((int)Layer.Content);

        Scopes.Add(new Scope(drawList, identity, 0, explicitly));
        return Scopes.Count - 1;
    }

    internal static void End()
    {
        if (Scopes.Count == 0)
            return;

        var scope = Scopes[^1];
        Scopes.RemoveAt(Scopes.Count - 1);

        if (Build.Diagnostics && scope.Depth != 0)
        {
            Plugin.Log.Error(
                "[EchoGlam] LayerStack scope closed with {0} surface(s) still open.", scope.Depth);
        }

        scope.DrawList.ChannelsMerge();
    }

    /// Finds the scope for the draw list being drawn into, or -1.
    private static int Find(nint identity)
    {
        for (var i = Scopes.Count - 1; i >= 0; i--)
        {
            if (Scopes[i].Identity == identity)
                return i;
        }

        return -1;
    }

    /// Claims the next nesting depth for a surface about to open, opening a scope for this draw list if there
    /// is not one yet.
    internal static int EnterSurface()
    {
        var drawList = ImGui.GetWindowDrawList();
        var identity = Identity(drawList);

        var index = Find(identity);
        if (index < 0)
            index = Open(drawList, identity, explicitly: false);

        var scope = Scopes[index];
        scope.Depth++;
        Scopes[index] = scope;

        UseContent();
        return scope.Depth - 1;
    }

    /// Closes the surface opened by the matching EnterSurface, merging an implicit scope once the last
    /// surface on its draw list has gone.
    internal static void ExitSurface()
    {
        var identity = Identity(ImGui.GetWindowDrawList());
        var index = Find(identity);
        if (index < 0)
            return;

        var scope = Scopes[index];
        if (scope.Depth > 0)
            scope.Depth--;

        Scopes[index] = scope;

        if (scope.Depth != 0 || scope.Explicit)
        {
            UseContent();
            return;
        }

        Scopes.RemoveAt(index);
        scope.DrawList.ChannelsMerge();
    }

    /// Switches to the decor layer for a surface at this depth, for the few calls that paint its background.
    internal static void UseDecor(int depth)
    {
        var index = Find(Identity(ImGui.GetWindowDrawList()));
        if (index < 0)
            return;

        Scopes[index].DrawList.ChannelsSetCurrent(Math.Clamp(depth, 0, MaxDepth - 1));
    }

    /// Switches back to the content layer.
    internal static void UseContent()
    {
        var index = Find(Identity(ImGui.GetWindowDrawList()));
        if (index < 0)
            return;

        Scopes[index].DrawList.ChannelsSetCurrent((int)Layer.Content);
    }

    /// Drops any scope left open by a draw that threw, called at the top of each frame.
    internal static void Reset() => Scopes.Clear();

    private static unsafe nint Identity(ImDrawListPtr drawList) => (nint)drawList.Handle;
}
