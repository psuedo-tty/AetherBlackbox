using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentWKSMission;

namespace AetherBlackbox.Core
{
    public class PositionRecorder : IDisposable
    {
        private readonly Plugin plugin;
        private List<WaymarkSnapshot> initialWaymarks = new();
        private readonly Dictionary<uint, ReplayEntityState> lastRecordedStates = new();

        private static readonly ulong[] lastLoggedEntityIds = new ulong[20];

        private readonly List<(DateTime Time, List<EntityPositionSnapshot> Data)> sessionData = new();
        private readonly List<ReplayAoeEvent> sessionAoeEvents = new();
        private const int MaxRecordingSeconds = 1200;

        private readonly Dictionary<uint, uint> _actionBuffer = new();
        private readonly object _bufferLock = new();

        private readonly HashSet<(uint EntityId, uint StatusId)> _loggedPhantomStatuses = new();

        public bool IsRecording { get; private set; } = false;
        private DateTime sessionStartTime = DateTime.MinValue;
        private DateTime lastCaptureTime = DateTime.MinValue;
        private const double CaptureIntervalMs = 66.0; // ~15 FPS

        public PositionRecorder(Plugin plugin)
        {
            this.plugin = plugin;
            Service.Framework.Update += OnUpdate;
        }

        public void OnActionUsed(uint sourceId, uint actionId, Vector3? pos = null, float? rot = null)
        {
            if (!IsRecording) return;

            float timeOffset = (float)(DateTime.Now - sessionStartTime).TotalSeconds;
            var obj = Service.ObjectTable.SearchById(sourceId);

            string sourceName = obj != null ? obj.Name.TextValue : "Invisible Helper";
            Vector3 origin = pos ?? (obj != null ? obj.Position : Vector3.Zero);
            float rotation = rot ?? (obj != null ? obj.Rotation : 0f);

            if (pos != null || obj != null)
            {
                var aoeEvent = new ReplayAoeEvent
                {
                    TimeOffset = timeOffset,
                    ActionId = actionId,
                    SourceId = sourceId,
                    Origin = origin,
                    Rotation = rotation
                };

                lock (sessionAoeEvents)
                {
                    sessionAoeEvents.Add(aoeEvent);
                }
            }

            lock (_bufferLock)
            {
                _actionBuffer[sourceId] = actionId;
            }
        }

        public unsafe void StartRecording()
        {
            lock (sessionData) sessionData.Clear();
            lock (sessionAoeEvents) sessionAoeEvents.Clear();
            lock (_bufferLock) _actionBuffer.Clear();
            _loggedPhantomStatuses.Clear();
            sessionStartTime = DateTime.Now;
            lastCaptureTime = DateTime.MinValue;
            IsRecording = true;
            initialWaymarks.Clear();
            lastRecordedStates.Clear();

            var controller = MarkingController.Instance();
            if (controller != null)
            {
                var markers = controller->FieldMarkers;

                for (int i = 0; i < 8; i++)
                {
                    var marker = markers[i];

                    if (marker.Active)
                    {
                        initialWaymarks.Add(new WaymarkSnapshot
                        {
                            ID = i,
                            X = marker.Position.X,
                            Z = marker.Position.Z,
                            Active = true
                        });
                    }
                }
            }

            Service.PluginLog.Debug("[PositionRecorder] Recording started.");
        }

        public void StopRecording()
        {
            if (!IsRecording) return;
            IsRecording = false;
            Service.PluginLog.Debug($"[PositionRecorder] Recording stopped. Captured {sessionData.Count} frames.");
        }

        private void OnUpdate(IFramework framework)
        {
            if (!IsRecording || !Service.ClientState.IsLoggedIn) return;

            LogGroupManagerAllianceInspection();

            var snapshotTime = DateTime.Now;

            if ((snapshotTime - sessionStartTime).TotalSeconds > MaxRecordingSeconds)
            {
                Service.PluginLog.Warning("[PositionRecorder] Safety Cap Reached (20m). Force stopping.");
                StopRecording();
                if (plugin.PullManager?.CurrentSession != null) plugin.PullManager.CurrentSession.IsTruncated = true;
                return;
            }

            if ((snapshotTime - lastCaptureTime).TotalMilliseconds < CaptureIntervalMs) return;

            lastCaptureTime = snapshotTime;

            var frameData = new List<EntityPositionSnapshot>();

            if (plugin.PullManager?.CurrentSession != null && plugin.PullManager.CurrentSession.ReplayData.Header == null)
            {
                plugin.PullManager.CurrentSession.ReplayData.Header = new SearchHeader();
            }

            Dictionary<uint, string> allianceTagsById = new();
            Dictionary<string, string> crossRealmTags = new();

            unsafe
            {
                try
                {
                    var groupManager = FFXIVClientStructs.FFXIV.Client.Game.Group.GroupManager.Instance();
                    if (groupManager != null)
                    {
                        var group = groupManager->GetGroup(false);
                        if (group != null)
                        {
                            for (int i = 0; i < 20; i++)
                            {
                                var member = group->GetAllianceMemberByIndex(i);
                                if (member != null && member->EntityId != 0 && member->EntityId != 0xE0000000)
                                {
                                    allianceTagsById[(uint)member->EntityId] = i < 8 ? "Alliance 1" : "Alliance 2";
                                }
                            }
                        }
                    }

                    if (allianceTagsById.Count == 0)
                    {
                        var proxy = FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyCrossRealm.Instance();
                        if (proxy != null && proxy->IsInCrossRealmParty)
                        {
                            for (byte group = 0; group < 6; group++)
                            {
                                var count = FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyCrossRealm.GetGroupMemberCount(group);
                                for (int i = 0; i < count; i++)
                                {
                                    var member = FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyCrossRealm.GetGroupMember(group, i);
                                    if (member != null && member->ContentId != 0)
                                    {
                                        string proxyName = member->NameString.ToString();
                                        if (!string.IsNullOrEmpty(proxyName))
                                        {
                                            char tagLetter = (char)('A' + group);
                                            crossRealmTags[proxyName] = $"Alliance {tagLetter}";
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Service.PluginLog.Warning(e, "Safely caught error while reading cross-realm proxy.");
                }
            }

            Dictionary<uint, uint> actionsThisFrame;
            lock (_bufferLock)
            {
                actionsThisFrame = new Dictionary<uint, uint>(_actionBuffer);
                _actionBuffer.Clear();
            }

            foreach (var obj in Service.ObjectTable)
            {
                uint currentAction = 0;
                bool isNewEntity = !lastRecordedStates.ContainsKey(obj.EntityId);

                bool shouldRecordMovement = true;
                bool shouldRecordAttributes = false;
                if (lastRecordedStates.TryGetValue(obj.EntityId, out var lastState))
                {
                    if (obj is IBattleChara battleObj)
                    {
                        if (battleObj.CurrentHp != lastState.CurrentHp) shouldRecordAttributes = true;
                    }

                    float dist = Vector3.Distance(obj.Position, lastState.Position);
                    float rotDiff = Math.Abs(obj.Rotation - lastState.Rotation);

                    if (dist <= 0.1f && rotDiff < 0.0174f)
                    {
                        shouldRecordMovement = false;
                    }
                }
                uint actionToLog = 0;
                if (actionsThisFrame.TryGetValue(obj.EntityId, out var actId))
                {
                    actionToLog = actId;
                }
                if (obj is IPlayerCharacter player)
                {
                    uint sHash = 0;
                    try
                    {
                        if (player.StatusList != null)
                        {
                            unchecked
                            {
                                foreach (var s in player.StatusList) { if (s != null) sHash = (sHash * 397) ^ s.StatusId ^ s.Param ^ s.SourceId; }
                            }
                        }
                    }
                    catch { }

                    bool statusChanged = lastState.ObjectId == 0 || sHash != lastRecordedStates[player.EntityId].StatusHash;

                    string teamTag = string.Empty;
                    bool inRealParty = false;

                    if (Service.PartyList != null)
                    {
                        for (int i = 0; i < Service.PartyList.Length; i++)
                        {
                            var member = Service.PartyList[i];
                            if (member != null && member.EntityId == player.EntityId)
                            {
                                if (i < 8) inRealParty = true;
                                teamTag = "Party";
                                break;
                            }
                        }
                    }

                    if (allianceTagsById.TryGetValue(player.EntityId, out var gmTag))
                    {
                        teamTag = gmTag;
                    }
                    else if (crossRealmTags.TryGetValue(player.Name.TextValue, out var crTag))
                    {
                        if (inRealParty)
                        {
                            teamTag = $"Party {crTag.Last()}";
                        }
                        else
                        {
                            teamTag = crTag;
                        }
                    }
                    else if (!inRealParty && Service.PartyList != null && Service.PartyList.Any(p => p.EntityId == player.EntityId))
                    {
                        teamTag = "Alliance";
                    }

                    string playerName = string.Empty;
                    if (isNewEntity)
                    {
                        Service.PluginLog.Debug($"[PositionRecorder] Recorded new entity: {player.Name.TextValue} | ID: {player.EntityId} | Tag: {teamTag}");
                        if (plugin.Configuration.AnonymizeNames)
                        {
                            playerName = player.ClassJob.RowId switch
                            {
                                19 => "PLD", 21 => "WAR", 32 => "DRK",37 => "GNB",
                                24 => "WHM", 28 => "SCH", 33 => "AST", 40 => "SGE",
                                20 => "MNK", 22 => "DRG", 30 => "NIN", 34 => "SAM", 39 => "RPR", 41 => "VPR",
                                23 => "BRD", 31 => "MCH", 38 => "DNC",
                                25 => "BLM", 27 => "SMN", 35 => "RDM", 42 => "PCT",
                                _ => "PLAYER"
                            };
                        }
                        else
                        {
                            playerName = player.Name.TextValue;
                        }
                    }

                    var snapshot = new EntityPositionSnapshot
                    {
                        ObjectId = player.EntityId,
                        Name = playerName,
                        TeamTag = teamTag,
                        Position = player.Position,
                        Rotation = player.Rotation,
                        CurrentHp = player.CurrentHp,
                        MaxHp = player.MaxHp,
                        ClassJobId = player.ClassJob.RowId,
                        Timestamp = snapshotTime,
                        Type = EntityType.Player,
                        Statuses = statusChanged ? player.StatusList.Where(s => s != null).Select(s => new ReplayStatus { Id = s.StatusId, Duration = s.RemainingTime, StackCount = s.Param, SourceId = s.SourceId }).ToList() : null,
                        Cast = player.GetCastSafe(),
                        TargetId = player.TargetObjectId,
                        LastLoggedActionId = actionToLog,
                        OwnerId = player.OwnerId
                    };

                    frameData.Add(snapshot);

                    lastRecordedStates[player.EntityId] = new ReplayEntityState
                    {
                        Position = player.Position,
                        Rotation = player.Rotation,
                        CurrentHp = player.CurrentHp,
                        ObjectId = 1,
                        StatusHash = sHash
                    };
                }
                else if (obj is IBattleNpc npc && (npc.MaxHp > 0 || actionToLog != 0 || npc.IsCastingSafe()))
                {
                    bool isDead = npc.IsDead;
                    bool isActive = npc.IsCastingSafe() || actionToLog != 0 || shouldRecordMovement || isNewEntity || npc.IsTargetable || npc.MaxHp == 44;

                    if (lastRecordedStates.TryGetValue(npc.EntityId, out var state))
                    {
                        if (isActive && !isDead)
                        {
                            state.DeathTime = snapshotTime;
                            lastRecordedStates[npc.EntityId] = state;
                        }
                        else
                        {
                            if ((snapshotTime - state.DeathTime).TotalSeconds > 7.0 && npc.MaxHp != 44) continue;
                        }
                    }
                    else
                    {
                        if (isDead) continue;
                        lastRecordedStates[npc.EntityId] = new ReplayEntityState { Position = npc.Position, Rotation = npc.Rotation, DeathTime = snapshotTime };
                    }

                    uint sHash = 0;
                    try
                    {
                        if (npc.StatusList != null)
                        {
                            unchecked
                            {
                                foreach (var s in npc.StatusList)
                                {
                                    if (s != null) sHash = (sHash * 397) ^ s.StatusId ^ s.Param ^ s.SourceId;
                                }
                            }
                        }
                    }
                    catch { }
                    bool statusChanged = lastRecordedStates[npc.EntityId].ObjectId == 0 || sHash != lastRecordedStates[npc.EntityId].StatusHash;

                    var replayStatuses = npc.StatusList?
                        .Where(s => s != null)
                        .Select(s => new ReplayStatus
                        {
                            Id = s.StatusId,
                            Duration = s.RemainingTime,
                            StackCount = s.Param,
                            SourceId = s.SourceId
                        })
                        .ToList() ?? new List<ReplayStatus>();

                    unsafe
                    {
                        var characterPtr = (Character*)npc.Address;
                        if (characterPtr != null)
                        {
                            var statusManager = characterPtr->GetStatusManager();
                            if (statusManager != null)
                            {
                                for (int i = 0; i < statusManager->Status.Length; i++)
                                {
                                    uint stId = statusManager->Status[i].StatusId;
                                    float stDuration = statusManager->Status[i].RemainingTime;
                                    uint stParam = statusManager->Status[i].Param;
                                    uint stSourceId = (uint)statusManager->Status[i].SourceObject;

                                    if ((stId >= 4358 && stId <= 4369) || (stId >= 4803 && stId <= 4805) || (stId >= 5328 && stId <= 5335))
                                    {
                                        if (!replayStatuses.Any(x => x.Id == stId))
                                        {
                                            replayStatuses.Add(new ReplayStatus
                                            {
                                                Id = stId,
                                                Duration = stDuration,
                                                StackCount = stParam,
                                                SourceId = stSourceId
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }

                    var phantomStatuses = replayStatuses
                        .Where(s => (s.Id >= 4358 && s.Id <= 4369) || (s.Id >= 4803 && s.Id <= 4805) || (s.Id >= 5328 && s.Id <= 5335))
                        .ToList();

                    if (phantomStatuses.Count > 0 &&
                        _loggedPhantomStatuses.Add((npc.EntityId, phantomStatuses[0].Id)))
                    {
                        Service.PluginLog.Debug(
                            $"[FrameCapture] Npc {npc.Name.TextValue} captured Phantom statuses: " +
                            string.Join(",", phantomStatuses.Select(s => s.Id))
                        );
                    }

                    var snapshot = new EntityPositionSnapshot
                    {
                        ObjectId = npc.EntityId,
                        Name = isNewEntity ? npc.Name.TextValue : string.Empty,
                        Position = npc.Position,
                        Rotation = npc.Rotation,
                        CurrentHp = npc.CurrentHp,
                        MaxHp = npc.MaxHp,
                        Timestamp = snapshotTime,
                        Type = (npc.OwnerId != 0 && npc.OwnerId != 0xE0000000) ? EntityType.Pet : (npc.IsTargetable ? EntityType.Boss : EntityType.Npc),
                        ModelId = (uint)npc.BaseId,
                        Statuses = statusChanged ? replayStatuses : null,
                        Cast = npc.GetCastSafe(),
                        TargetId = npc.TargetObjectId,
                        LastLoggedActionId = actionToLog,
                        OwnerId = npc.OwnerId
                    };

                    frameData.Add(snapshot);

                    if (lastRecordedStates.TryGetValue(npc.EntityId, out var existingState))
                    {
                        existingState.Position = npc.Position;
                        existingState.Rotation = npc.Rotation;
                        existingState.CurrentHp = npc.CurrentHp;
                        existingState.ObjectId = 1;
                        existingState.StatusHash = sHash;
                        lastRecordedStates[npc.EntityId] = existingState;
                    }
                }
            }

            lock (sessionData)
            {
                sessionData.Add((snapshotTime, frameData));
            }
        }

        public unsafe ReplayRecording GetReplayData()
        {
            lock (sessionData)
            {
                var recording = new ReplayRecording();
                var controller = MarkingController.Instance();
                if (controller != null)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        var marker = controller->FieldMarkers[i];
                        if (marker.Active)
                        {
                            recording.Waymarks.Add(new WaymarkSnapshot
                            {
                                ID = i,
                                X = marker.Position.X,
                                Z = marker.Position.Z,
                                Active = true
                            });
                        }
                    }
                }

                if (sessionData.Count == 0) return recording;
                var startTime = sessionData[0].Time;

                lock (sessionAoeEvents)
                {
                    recording.AoeEvents.AddRange(sessionAoeEvents);
                }

                foreach (var (time, snapshots) in sessionData)
                {
                    var frame = new ReplayFrame
                    {
                        TimeOffset = (float)(time - startTime).TotalSeconds
                    };

                    foreach (var entity in snapshots)
                    {
                        if (!recording.Metadata.ContainsKey(entity.ObjectId) && !string.IsNullOrEmpty(entity.Name))
                        {
                            recording.Metadata[entity.ObjectId] = new ReplayMetadata
                            {
                                Name = entity.Name,
                                TeamTag = entity.TeamTag ?? string.Empty,
                                MaxHp = entity.MaxHp,
                                ClassJobId = entity.ClassJobId,
                                Type = entity.Type,
                                ModelId = entity.ModelId,
                                OwnerId = entity.OwnerId
                            };
                        }

                        if (entity.LastLoggedActionId != 0 && !recording.Header.AbilityManifest.ContainsKey(entity.LastLoggedActionId))
                        {
                            var action = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()?.GetRowOrDefault(entity.LastLoggedActionId);
                            if (action.HasValue)
                            {
                                recording.Header.AbilityManifest.Add(entity.LastLoggedActionId, action.Value.Name.ToString());
                                recording.Header.AbilityIconManifest.Add(entity.LastLoggedActionId, action.Value.Icon);
                            }
                        }
                        if (entity.Cast.ActionId != 0 && !recording.Header.AbilityManifest.ContainsKey(entity.Cast.ActionId))
                        {
                            var castAction = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()?.GetRowOrDefault(entity.Cast.ActionId);
                            if (castAction.HasValue)
                            {
                                recording.Header.AbilityManifest.Add(entity.Cast.ActionId, castAction.Value.Name.ToString());
                                recording.Header.AbilityIconManifest.Add(entity.Cast.ActionId, castAction.Value.Icon);
                            }
                        }
                        if (entity.Statuses != null)
                        {
                            foreach (var s in entity.Statuses)
                            {
                                if (!recording.Header.StatusManifest.ContainsKey(s.Id))
                                {
                                    var status = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>()?.GetRowOrDefault(s.Id);
                                    if (status.HasValue)
                                        recording.Header.StatusManifest.Add(s.Id, status.Value.Name.ToString());
                                }
                            }
                        }

                        frame.Ids.Add(entity.ObjectId);
                        frame.X.Add((float)Math.Round(entity.Position.X, 2));
                        frame.Z.Add((float)Math.Round(entity.Position.Z, 2));
                        frame.Rot.Add((float)Math.Round(entity.Rotation, 2));
                        frame.Hp.Add(entity.CurrentHp);
                        frame.Statuses.Add(entity.Statuses);
                        frame.Casts.Add(entity.Cast);
                        frame.Targets.Add(entity.TargetId);
                        frame.Actions.Add(entity.LastLoggedActionId);
                    }
                    recording.Frames.Add(frame);
                }
                Service.PluginLog.Debug($"[SchemaCheck] Replay Ready. Abilities: {recording.Header.AbilityManifest.Count}, Events: {recording.AoeEvents.Count}, Frames: {recording.Frames.Count}");

                return recording;
            }
        }
        private static unsafe void LogGroupManagerAllianceInspection()
        {
            var groupManager = FFXIVClientStructs.FFXIV.Client.Game.Group.GroupManager.Instance();
            if (groupManager == null) return;

            var group = groupManager->GetGroup(false);
            if (group == null) return;

            for (int i = 0; i < 20; i++)
            {
                var memberPtr = group->GetAllianceMemberByIndex(i);
                if (memberPtr == null)
                {
                    if (lastLoggedEntityIds[i] != 0)
                    {
                        Service.PluginLog.Info($"[GroupManagerInspect] Index {i} | Valid: False | Slot Cleared");
                        lastLoggedEntityIds[i] = 0;
                    }
                    continue;
                }

                ulong entityId = memberPtr->EntityId;
                if (entityId != lastLoggedEntityIds[i])
                {
                    int nullIdx = memberPtr->Name.IndexOf((byte)0);
                    var nameSpan = nullIdx >= 0 ? memberPtr->Name[..nullIdx] : memberPtr->Name;
                    string name = System.Text.Encoding.UTF8.GetString(nameSpan);

                    Service.PluginLog.Info($"[GroupManagerInspect] Index {i} | Valid: True | EntityId: {entityId} | Name: {name}");
                    lastLoggedEntityIds[i] = entityId;
                }
            }
        }
        public void Dispose()
        {
            Service.Framework.Update -= OnUpdate;
            sessionData.Clear();
        }

    }
}