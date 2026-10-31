// SPDX-License-Identifier: MIT
// Copyright (c) 2026 unicbm
using System;
using System.Collections.Generic;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;

namespace BigHead;

/// <summary>
/// Main-thread-only player scaling for a CSSharp plugin. Create once in Load,
/// share that instance within the owner plugin, and Dispose in Unload.
/// Supports humans and bots; dead, invalid, HLTV and takeover players cannot be set.
/// </summary>
public sealed class PlayerScaleService : IDisposable
{
    private const string HeadField = "m_flBigHeadScale", MutatorsField = "m_nActiveMutators";
    private readonly BasePlugin owner;
    private readonly uint headOffset, mutatorsOffset, bodyOffset;
    private readonly Dictionary<int, State> heads = new(), bodies = new();
    private CCSGameRulesProxy? proxy;
    private bool ownsMutator, disposed;

    private sealed class State(CCSPlayerController player)
    {
        public readonly CCSPlayerController Player = player;
        public float Scale, OriginalScale;
        public CCSPlayerPawn? AppliedPawn;
        public bool Grow, Shrink;
    }

    public PlayerScaleService(BasePlugin owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        this.owner = owner;
        // Resolve current schema fields; no hard-coded offsets or native hooks.
        headOffset = RequiredOffset("CCSPlayerPawn", HeadField);
        mutatorsOffset = RequiredOffset("CCSGameRules", MutatorsField);
        bodyOffset = RequiredOffset("CBaseEntity", "m_CBodyComponent");
        RequiredOffset("CGameSceneNode", "m_flScale");
        owner.RegisterListener<Listeners.OnServerPostEntityThink>(Update);
        owner.RegisterListener<Listeners.OnClientDisconnect>(Disconnected);
        owner.RegisterListener<Listeners.OnMapEnd>(ClearMap);
        owner.RegisterListener<Listeners.OnMapStart>(MapStarted);
    }

    /// <summary>Sets the head multiplier relative to the body. False means the player/rules are not ready.</summary>
    public bool TrySetHeadScale(CCSPlayerController player, float scale) => TrySet(player, scale, false);

    /// <summary>Sets the whole model scale, including its head. False means the player/model is not ready.</summary>
    public bool TrySetBodyScale(CCSPlayerController player, float scale) => TrySet(player, scale, true);

    /// <summary>Reads the live head field, or null when there is no eligible live pawn.</summary>
    public float? GetHeadScale(CCSPlayerController player)
    {
        CheckAccess(player);
        return Pawn(player) is { } pawn ? ReadScale(pawn, false) : null;
    }

    /// <summary>Reads the live local scene-node scale, or null when there is no eligible live model.</summary>
    public float? GetBodyScale(CCSPlayerController player)
    {
        CheckAccess(player);
        return Pawn(player) is { } pawn ? BodyNode(pawn)?.Scale : null;
    }

    /// <summary>Stops managing the head and restores its captured baseline, including while dead.</summary>
    public bool ResetHeadScale(CCSPlayerController player) => Reset(player, false);

    /// <summary>Stops managing the body and restores its captured baseline, including while dead.</summary>
    public bool ResetBodyScale(CCSPlayerController player) => Reset(player, true);

    /// <summary>Restores both independently managed multipliers for this player.</summary>
    public void ResetPlayer(CCSPlayerController player)
    {
        ResetHeadScale(player);
        ResetBodyScale(player);
    }

    private void CheckAccess(CCSPlayerController player)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(player);
    }

    private static uint RequiredOffset(string type, string field)
    {
        int offset = Schema.GetSchemaOffset(type, field);
        return offset > 0 ? (uint)offset
            : throw new InvalidOperationException($"PlayerScaleService requires schema field {type}.{field}.");
    }

    private static CCSPlayerPawn? Pawn(CCSPlayerController player) =>
        player is { IsValid: true, IsHLTV: false, ControllingBot: false, PawnIsAlive: true }
        && player.PlayerPawn.Value is { IsValid: true, LifeState: 0 } pawn ? pawn : null;

    private static CGameSceneNode? BodyNode(CCSPlayerPawn pawn) =>
        pawn.CBodyComponent?.SceneNode is { Handle: not 0 } node ? node : null;

    private bool Ready(CCSPlayerPawn pawn, bool body)
    {
        if (body) return BodyNode(pawn) != null;
        if (proxy is not { IsValid: true })
            proxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        return proxy?.GameRules is { Handle: not 0 };
    }

    private Dictionary<int, State> States(bool body) => body ? bodies : heads;

    private State GetState(CCSPlayerController player, CCSPlayerPawn pawn, bool body)
    {
        var states = States(body);
        if (!states.TryGetValue(player.Slot, out var state))
            states.Add(player.Slot, state = new State(player) { Scale = ReadScale(pawn, body) });
        return state;
    }

    private bool TrySet(CCSPlayerController player, float scale, bool body)
    {
        CheckAccess(player);
        if (!float.IsFinite(scale) || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale), "Scale must be a finite positive number.");
        if (Pawn(player) is not { } pawn || !Ready(pawn, body)) return false;
        var state = GetState(player, pawn, body);
        state.Scale = scale;
        state.Grow = state.Shrink = false;
        Apply(state, pawn, body);
        return true;
    }

    private bool Reset(CCSPlayerController player, bool body)
    {
        CheckAccess(player);
        if (!player.IsValid || !States(body).Remove(player.Slot, out var state)) return false;
        RestorePawn(state, body);
        if (!body && heads.Count == 0) ReleaseMutator();
        return true;
    }

    // Compatibility helpers used only by the standalone host's local test commands.
    internal bool IsManaged(CCSPlayerController player, bool body) => States(body).ContainsKey(player.Slot);
    internal float ManagedScale(CCSPlayerController player, bool body) => States(body)[player.Slot].Scale;
    internal bool TryHold(CCSPlayerController player, bool grow, bool pressed, bool body)
    {
        CheckAccess(player);
        State state;
        if (pressed)
        {
            if (Pawn(player) is not { } pawn || !Ready(pawn, body)) return false;
            state = GetState(player, pawn, body);
        }
        else if (!States(body).TryGetValue(player.Slot, out state!)) return false;
        if (grow) state.Grow = pressed; else state.Shrink = pressed;
        return true;
    }

    private void Update()
    {
        UpdateStates(heads, false);
        UpdateStates(bodies, true);
    }

    private void UpdateStates(Dictionary<int, State> states, bool body)
    {
        foreach (var state in states.Values)
        {
            var pawn = Pawn(state.Player);
            if (pawn == null)
            {
                state.Grow = state.Shrink = false;
                RestorePawn(state, body);
                continue;
            }
            int direction = (state.Grow ? 1 : 0) - (state.Shrink ? 1 : 0);
            if (direction != 0)
            {
                float next = state.Scale * MathF.Pow(2, direction * Server.TickInterval);
                if (float.IsFinite(next) && next > 0) state.Scale = next;
                else state.Grow = state.Shrink = false;
            }
            Apply(state, pawn, body);
        }
    }

    private void Apply(State state, CCSPlayerPawn pawn, bool body)
    {
        if (body)
        {
            if (BodyNode(pawn) == null) return;
        }
        else
        {
            if (proxy is not { IsValid: true } || proxy.GameRules is not { Handle: not 0 } rules) return;
            ref int flags = ref Schema.GetRef<int>(rules.Handle, "CCSGameRules", MutatorsField);
            if ((flags & 1) == 0)
            {
                ownsMutator = true;
                flags |= 1;
                NativeAPI.SchemaNetworkStateChanged(rules.__m_pChainEntity.Handle, mutatorsOffset, uint.MaxValue, uint.MaxValue);
            }
        }
        if (state.AppliedPawn?.EntityHandle.Raw != pawn.EntityHandle.Raw)
        {
            RestorePawn(state, body);
            state.OriginalScale = ReadScale(pawn, body);
            state.AppliedPawn = pawn;
        }
        SetScale(pawn, state.Scale, body);
    }

    private static float ReadScale(CCSPlayerPawn pawn, bool body) => body
        ? BodyNode(pawn)!.Scale
        : Schema.GetRef<float>(pawn.Handle, "CCSPlayerPawn", HeadField);

    private void SetScale(CCSPlayerPawn pawn, float scale, bool body)
    {
        if (body)
        {
            if (BodyNode(pawn) is not { } node || node.Scale == scale) return;
            node.Scale = scale;
        }
        else
        {
            ref float current = ref Schema.GetRef<float>(pawn.Handle, "CCSPlayerPawn", HeadField);
            if (current == scale) return;
            current = scale;
        }
        // 1.41.9.3 serializes these fields without MNetworkEnable field metadata.
        // Dirty the owning body component for scene-node scale, not pawn + node offset.
        NativeAPI.SchemaSetStateChanged(pawn.Handle, body ? bodyOffset : headOffset, uint.MaxValue, uint.MaxValue);
        pawn.LastNetworkChange = Server.CurrentTime;
        pawn.IsSteadyState.Clear();
    }

    private void RestorePawn(State state, bool body)
    {
        if (state.AppliedPawn is { IsValid: true } pawn) SetScale(pawn, state.OriginalScale, body);
        state.AppliedPawn = null;
    }

    private void ReleaseMutator()
    {
        if (ownsMutator && proxy is { IsValid: true } && proxy.GameRules is { Handle: not 0 } rules)
        {
            ref int flags = ref Schema.GetRef<int>(rules.Handle, "CCSGameRules", MutatorsField);
            flags &= ~1;
            NativeAPI.SchemaNetworkStateChanged(rules.__m_pChainEntity.Handle, mutatorsOffset, uint.MaxValue, uint.MaxValue);
        }
        ownsMutator = false;
    }

    private void Disconnected(int slot)
    {
        if (heads.Remove(slot, out var head)) RestorePawn(head, false);
        if (bodies.Remove(slot, out var body)) RestorePawn(body, true);
        if (heads.Count == 0) ReleaseMutator();
    }

    private void MapStarted(string map) => ClearMap();

    private void ClearMap()
    {
        // Map teardown invalidates entity pointers; do not restore through them.
        heads.Clear();
        bodies.Clear();
        proxy = null;
        ownsMutator = false;
    }

    /// <summary>Unsubscribes listeners and restores owned changes. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        owner.RemoveListener<Listeners.OnServerPostEntityThink>(Update);
        owner.RemoveListener<Listeners.OnClientDisconnect>(Disconnected);
        owner.RemoveListener<Listeners.OnMapEnd>(ClearMap);
        owner.RemoveListener<Listeners.OnMapStart>(MapStarted);
        foreach (var state in heads.Values) RestorePawn(state, false);
        foreach (var state in bodies.Values) RestorePawn(state, true);
        ReleaseMutator();
        ClearMap();
    }
}
