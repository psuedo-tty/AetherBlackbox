using AetherBlackbox.Core;
using AetherBlackbox.Game;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Network;
using System.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherBlackbox.Events
{
    public unsafe class CombatEventCapture : IDisposable
    {
        private readonly Plugin plugin;
        private readonly CombatHistory history;
        private readonly MetadataRecorder metadataRecorder;
        private readonly DeathRecorder deathRecorder;

        private delegate void ProcessPacketEffectResultDelegate(uint targetId, IntPtr actionIntegrityData, byte isReplay);

        private Hook<ActionEffectHandler.Delegates.Receive> processPacketActionEffectHook;
        private Hook<PacketDispatcher.Delegates.HandleActorControlPacket> processPacketActorControlHook;

        [Signature("48 8B C4 44 88 40 18 89 48 08", DetourName = nameof(ProcessPacketEffectResultDetour))]
        private Hook<ProcessPacketEffectResultDelegate> processPacketEffectResultHook = null!;

        public CombatEventCapture(Plugin plugin)
        {
            this.plugin = plugin;
            this.history = new CombatHistory(plugin);
            this.metadataRecorder = new MetadataRecorder(plugin, history);
            this.deathRecorder = new DeathRecorder(plugin, history);

            InitializeHooks();
        }

        private void InitializeHooks()
        {
            try
            {
                Service.GameInteropProvider.InitializeFromAttributes(this);
                processPacketEffectResultHook?.Enable();
            }
            catch (Exception e)
            {
                Service.PluginLog.Error(e, "Failed to hook EffectResult signatures.");
                Service.ChatGui.PrintError("[AetherBlackbox] Buffs/debuffs are currently not being tracked due to patch updates, but positions and actions should work.");
            }

            processPacketActionEffectHook = Service.GameInteropProvider.HookFromAddress(
                (nint)ActionEffectHandler.MemberFunctionPointers.Receive,
                new ActionEffectHandler.Delegates.Receive(ProcessPacketActionEffectDetour));
            processPacketActionEffectHook.Enable();

            processPacketActorControlHook = Service.GameInteropProvider.HookFromAddress(
                (nint)PacketDispatcher.MemberFunctionPointers.HandleActorControlPacket,
                new PacketDispatcher.Delegates.HandleActorControlPacket(ProcessPacketActorControlDetour));
            processPacketActorControlHook.Enable();
        }

        public void CleanCombatEvents()
        {
            history.CleanCombatEvents();
        }

        private unsafe void ProcessPacketActionEffectDetour(
            uint casterEntityId, Character* casterPtr, Vector3* targetPos, ActionEffectHandler.Header* effectHeader, ActionEffectHandler.TargetEffects* effectArray,
            GameObjectId* targetEntityIds)
        {
            processPacketActionEffectHook.Original(casterEntityId, casterPtr, targetPos, effectHeader, effectArray, targetEntityIds);

            try
            {
                if (effectHeader->NumTargets == 0)
                    return;

                var actionId = (ActionType)effectHeader->ActionType switch
                {
                    ActionType.Mount => 0xD000000 + effectHeader->ActionId,
                    ActionType.Item => 0x2000000 + effectHeader->ActionId,
                    _ => effectHeader->SpellId
                };

                Vector3? sourcePos = (targetPos != null) ? *targetPos : (casterPtr != null ? casterPtr->GameObject.Position : null);
                float? sourceRot = casterPtr != null ? casterPtr->GameObject.Rotation : null;

                plugin.PositionRecorder.OnActionUsed(casterEntityId, actionId, sourcePos, sourceRot);

                if (casterPtr != null)
                {
                    if (Service.ObjectTable.SearchById(casterEntityId) is IGameObject casterObj)
                        metadataRecorder.RegisterEntity(casterEntityId, casterObj);
                }

                List<uint>? additionalStatus = null;

                for (var i = 0; i < effectHeader->NumTargets; i++)
                {
                    var actionTargetId = (uint)(targetEntityIds[i] & uint.MaxValue);
                    var targetObj = Service.ObjectTable.SearchById(actionTargetId);
                    var targetEffects = effectArray[i];

                    if (plugin.PullManager.IsInSession && targetObj is IBattleNpc npc)
                    {
                        for (var k = 0; k < 8; k++)
                        {
                            ref var eff = ref targetEffects.Effects[k];
                            var effectType = (ActionEffectType)eff.Type;

                            if (effectType is not (ActionEffectType.Damage or ActionEffectType.BlockedDamage or ActionEffectType.ParriedDamage))
                                continue;

                            uint dmg = eff.Value;
                            if ((eff.Param4 & 0x40) == 0x40)
                                dmg += (uint)eff.Param3 << 16;

                            if (dmg == 0)
                                continue;

                            plugin.PullManager.CurrentSession!.DamageByTarget.TryGetValue(npc.Name.TextValue, out var existing);
                            plugin.PullManager.CurrentSession.DamageByTarget[npc.Name.TextValue] = existing + dmg;

                            var snapshot = SnapshotFactory.Create(npc);
                            plugin.PullManager.CurrentSession.DetailedDamageEvents.Add(
                                CombatEventFactory.CreateDamage(snapshot, actionTargetId, casterEntityId, effectHeader->ActionId, dmg, effectType, eff.Param0, eff.Param1, (ActionType)effectHeader->ActionType)
                            );
                        }
                    }

                    if (!plugin.ConditionEvaluator.ShouldCapture(actionTargetId))
                        continue;

                    if (targetObj == null)
                        continue;

                    metadataRecorder.RegisterEntity(actionTargetId, targetObj);

                    for (var j = 0; j < 8; j++)
                    {
                        ref var actionEffect = ref targetEffects.Effects[j];
                        if (actionEffect.Type == 0)
                            continue;

                        uint amount = actionEffect.Value;
                        if ((actionEffect.Param4 & 0x40) == 0x40)
                            amount += (uint)actionEffect.Param3 << 16;

                        switch ((ActionEffectType)actionEffect.Type)
                        {
                            case ActionEffectType.Miss:
                            case ActionEffectType.Damage:
                            case ActionEffectType.BlockedDamage:
                            case ActionEffectType.ParriedDamage:
                                if (amount == 0 && (ActionEffectType)actionEffect.Type != ActionEffectType.Miss)
                                    continue;

                                if (additionalStatus == null)
                                {
                                    additionalStatus = new List<uint>();
                                    if (casterPtr != null)
                                    {
                                        var statusManager = casterPtr->GetStatusManager();
                                        if (statusManager != null)
                                        {
                                            foreach (ref var status in statusManager->Status)
                                            {
                                                if (status.StatusId is 1203 or 1195 or 1193 or 860 or 1715 or 2115 or 3642)
                                                    additionalStatus.Add(status.StatusId);
                                            }
                                        }
                                    }
                                }

                                var dmgSnapshot = SnapshotFactory.Create(targetObj, true, additionalStatus);
                                history.AddEvent(actionTargetId, CombatEventFactory.CreateDamage(
                                    dmgSnapshot, actionTargetId, casterEntityId, effectHeader->ActionId, amount,
                                    (ActionEffectType)actionEffect.Type, actionEffect.Param0, actionEffect.Param1, (ActionType)effectHeader->ActionType));
                                break;

                            case ActionEffectType.Heal:
                                if (amount == 0) continue;

                                var healSnapshot = SnapshotFactory.Create(targetObj, true);
                                history.AddEvent(actionTargetId, CombatEventFactory.CreateHealed(
                                    healSnapshot, actionTargetId, casterEntityId, actionId, amount, (actionEffect.Param1 & 0x20) == 0x20));
                                break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Service.PluginLog.Error(e, "Caught unexpected exception in ActionEffectDetour");
            }
        }

        private void ProcessPacketActorControlDetour(
            uint entityId, uint category, uint param1, uint param2, uint param3, uint param4, uint param5, uint param6, uint param7, uint param8, GameObjectId targetId,
            bool param9)
        {
            processPacketActorControlHook.Original(entityId, category, param1, param2, param3, param4, param5, param6, param7, param8, targetId, param9);

            try
            {
                if (!plugin.ConditionEvaluator.ShouldCapture(entityId) && (ActorControlCategory)category != ActorControlCategory.DoT)
                    return;

                var targetObj = Service.ObjectTable.SearchById(entityId);
                var p = targetObj as IPlayerCharacter;
                var npc = targetObj as IBattleNpc;

                if (p == null && npc == null)
                    return;

                if (p != null)
                    metadataRecorder.RegisterEntity(entityId, p);

                var snapshot = SnapshotFactory.Create(targetObj);

                switch ((ActorControlCategory)category)
                {
                    case ActorControlCategory.DoT:
                        uint sourceId = 0;
                        if (targetObj is IBattleNpc battleNpc)
                        {
                            var status = battleNpc.StatusListSafe().FirstOrDefault(s => s.StatusId == param1);
                            if (status != null) sourceId = status.SourceId;
                        }

                        var dotEvent = CombatEventFactory.CreateDoT(snapshot, entityId, sourceId, param2, param1);
                        history.AddEvent(entityId, dotEvent);

                        if (plugin.PullManager.IsInSession && plugin.PullManager.CurrentSession != null)
                        {
                            plugin.PullManager.CurrentSession.DetailedDamageEvents.Add(dotEvent);
                        }
                        break;

                    case ActorControlCategory.HoT:
                        if (param1 != 0)
                        {
                            history.AddEvent(entityId, CombatEventFactory.CreateHoT(snapshot, entityId, entityId, param2, param1));
                        }
                        else
                        {
                            history.AddEvent(entityId, new CombatEvent.HoT { Snapshot = snapshot, TargetActorId = entityId, Amount = param2 });
                        }
                        break;

                    case ActorControlCategory.Death:
                        deathRecorder.ProcessDeath(entityId);
                        break;
                }
            }
            catch (Exception e)
            {
                Service.PluginLog.Error(e, "Caught unexpected exception in ActorControlDetour");
            }
        }

        private unsafe void ProcessPacketEffectResultDetour(uint targetId, IntPtr actionIntegrityData, byte isReplay)
        {
            processPacketEffectResultHook.Original(targetId, actionIntegrityData, isReplay);

            try
            {
                var message = (AddStatusEffect*)actionIntegrityData;
                var targetObj = Service.ObjectTable.SearchById(targetId);
                var p = targetObj as IPlayerCharacter;
                var npc = targetObj as IBattleNpc;

                if (p == null && npc == null)
                    return;

                if (p != null)
                {
                    if (!plugin.ConditionEvaluator.ShouldCapture(targetId))
                        return;

                    metadataRecorder.RegisterEntity(targetId, p);
                }

                var snapshot = SnapshotFactory.Create(targetObj);

                var effects = (StatusEffectAddEntry*)message->Effects;
                var effectCount = Math.Min(message->EffectCount, 4u);

                for (uint j = 0; j < effectCount; j++)
                {
                    var effect = effects[j];
                    var effectId = effect.EffectId;

                    if (effectId <= 0 || effect.Duration < 0)
                        continue;

                    if (Service.ObjectTable.SearchById(effect.SourceActorId) is IPlayerCharacter sourcePc)
                    {
                        metadataRecorder.RegisterEntity(effect.SourceActorId, sourcePc);
                    }

                    history.AddEvent(targetId, CombatEventFactory.CreateStatusEffect(
                        snapshot, targetId, effect.SourceActorId, effectId, effect.StackCount, effect.Duration));
                }
            }
            catch (Exception e)
            {
                Service.PluginLog.Error(e, "Caught unexpected exception in EffectResultDetour");
            }
        }

        public void Dispose()
        {
            processPacketActionEffectHook.Dispose();
            processPacketEffectResultHook.Dispose();
            processPacketActorControlHook.Dispose();
        }
    }
}