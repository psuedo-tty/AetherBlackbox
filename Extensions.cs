using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Statuses;
using AetherBlackbox.Core;
using AetherBlackbox.Events;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace AetherBlackbox;

public static class Extensions {
    public static unsafe byte Barrier(this IPlayerCharacter player) {
        return ((Character*)player.Address)->CharacterData.ShieldValue;
    }

    // Dalamud's IsCasting derefs GetCastInfo() unguarded, and that's null on characters with no
    // cast info (companions, NPCs still spawning). ClientStructs' own check handles it.
    public static unsafe bool IsCastingSafe(this IBattleChara chara) {
        if (chara.Address == IntPtr.Zero) return false;
        return ((Character*)chara.Address)->IsCasting;
    }

    public static unsafe ReplayCast GetCastSafe(this IBattleChara chara) {
        if (chara.Address == IntPtr.Zero) return default;
        var character = (Character*)chara.Address;
        if (!character->IsCasting) return default;
        var info = character->GetCastInfo();
        if (info == null) return default;
        return new ReplayCast { ActionId = info->ActionId, Current = info->CurrentCastTime, Total = info->TotalCastTime };
    }

    // Same footgun as IsCasting: enumerating StatusList derefs GetStatusManager(), null on
    // characters without one. Empty list instead of NRE.
    public static unsafe IEnumerable<IStatus> StatusListSafe(this IBattleChara chara) {
        if (chara.Address == IntPtr.Zero) return Array.Empty<IStatus>();
        if (((Character*)chara.Address)->GetStatusManager() == null) return Array.Empty<IStatus>();
        return chara.StatusList;
    }

    public static CombatEvent.EventSnapshot Snapshot(
        this IPlayerCharacter player, bool snapEffects = false,
        IReadOnlyCollection<uint>? additionalStatus = null) {
        var statusEffects = snapEffects
            ? player.StatusList.Select(s => new CombatEvent.StatusEffectSnapshot { Id = s.StatusId, StackCount = s.Param })
                .ToList()
            : null;
        if (additionalStatus != null)
            statusEffects?.AddRange(additionalStatus.Select(s => new CombatEvent.StatusEffectSnapshot { Id = s, StackCount = 0 }));
        var snapshot = new CombatEvent.EventSnapshot {
            Time = DateTime.Now,
            CurrentHp = player.CurrentHp,
            MaxHp = player.MaxHp,
            StatusEffects = statusEffects,
            BarrierPercent = player.Barrier()
        };
        return snapshot;
    }

    public static void AddEntry<TKey, TValue>(this Dictionary<TKey, List<TValue>> dict, TKey key, TValue val) where TKey : notnull {
        if (dict.TryGetValue(key, out var list)) {
            list.Add(val);
        } else {
            var objList = new List<TValue>();
            dict.Add(key, objList);
            objList.Add(val);
        }
    }
}
