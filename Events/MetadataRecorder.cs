using AetherBlackbox.Core;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System.Collections.Generic;

namespace AetherBlackbox.Events
{
    public class MetadataRecorder
    {
        private readonly Plugin plugin;
        private readonly CombatHistory history;

        public MetadataRecorder(Plugin plugin, CombatHistory history)
        {
            this.plugin = plugin;
            this.history = history;
        }

        public void RegisterEntity(uint entityId, IGameObject obj)
        {
            if (!plugin.PullManager.IsInSession || plugin.PullManager.CurrentSession == null) return;
            var session = plugin.PullManager.CurrentSession;
            if (session.Metadata.ContainsKey(entityId)) return;

            string nameToStore = obj.Name.TextValue;
            uint classJobId = obj is ICharacter c ? c.ClassJob.RowId : 0;

            if (plugin.Configuration.AnonymizeNames && obj is IPlayerCharacter p)
            {
                var jobAbbr = p.ClassJob.Value.Abbreviation.ExtractText();
                if (string.IsNullOrEmpty(jobAbbr)) jobAbbr = "UNK";
                int count = 0;
                foreach (var meta in session.Metadata.Values)
                {
                    if (meta.ClassJobId == classJobId) count++;
                }
                nameToStore = count == 0 ? jobAbbr : $"{jobAbbr} {count + 1}";
            }

            session.Metadata[entityId] = new ReplayMetadata
            {
                EntityId = entityId,
                ClassJobId = classJobId,
                Name = nameToStore,
                OwnerId = obj.OwnerId,
                Type = (EntityType)obj.ObjectKind
            };

            if (obj is IPlayerCharacter pc && (Service.ClientState.TerritoryType == 1197 || Service.ClientState.TerritoryType == 1252 || Service.ClientState.TerritoryType == 1346))
            {
                foreach (var status in pc.StatusListSafe())
                {
                    if ((status.StatusId >= 4358 && status.StatusId <= 4369) || (status.StatusId >= 4803 && status.StatusId <= 4805) || (status.StatusId >= 5328 && status.StatusId <= 5335))
                    {
                        var statusData = GameDataCache.GetStatus(status.StatusId);
                        history.AddEvent(entityId, new CombatEvent.StatusEffect
                        {
                            Snapshot = SnapshotFactory.Create(pc),
                            TargetActorId = entityId,
                            Id = status.StatusId,
                            StackCount = 0,
                            SourceActorId = entityId,
                            Icon = statusData?.Icon,
                            Duration = 9999f,
                            Status = statusData?.Name.ExtractText(),
                            Description = statusData?.Description.ExtractText(),
                            Category = (StatusCategory)(statusData?.StatusCategory ?? 0)
                        });
                    }
                }
            }
        }
    }
}