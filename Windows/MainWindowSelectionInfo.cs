using AetherBlackbox.Core;
using AetherBlackbox.Events;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AetherBlackbox.Windows
{
    public partial class MainWindow
    {
        private void DrawSelectionInfo()
        {
            if (ActiveDeathReplay == null || ActiveDeathReplay.ReplayData.Frames.Count == 0) return;

            var recording = ActiveDeathReplay.ReplayData;
            var deathTimeOffset = selectedPull != null ? (float)(ActiveDeathReplay.TimeOfDeath - selectedPull.StartTime).TotalSeconds : recording.Frames.Last().TimeOffset;
            var targetOffset = deathTimeOffset + replayTimeOffset;
            var closestFrame = GetClosestFrame(recording, targetOffset);
            if (closestFrame == null) return;

            int idx = closestFrame.Ids.IndexOf((uint)selectedEntityId);
            if (idx == -1) return;

            if (!recording.Metadata.TryGetValue((uint)selectedEntityId, out var meta)) return;

            ImGui.Separator();

            if (ImGui.BeginTable("SelectionInfoTable", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Self", ImGuiTableColumnFlags.WidthStretch, 2f);
                ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);

                string displayName = meta.Name;
                if (configuration.AnonymizeNames && meta.ClassJobId != 0)
                {
                    var jobRow = Service.DataManager.GetExcelSheet<ClassJob>().GetRowOrDefault(meta.ClassJobId);
                    displayName = jobRow.HasValue ? jobRow.Value.Abbreviation.ToString() : "Job";
                }

                uint phId = GetActivePhantomJobIconId((uint)selectedEntityId, targetOffset);
                if (phId != 0)
                {
                    var sheetStatus = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>().GetRowOrDefault(phId);
                    if (sheetStatus.HasValue && sheetStatus.Value.Icon != 0)
                    {
                        var icon = Service.TextureProvider.GetFromGameIcon(sheetStatus.Value.Icon).GetWrapOrDefault();
                        if (icon != null)
                        {
                            float phIconSize = ImGui.GetTextLineHeight();
                            ImGui.Image(icon.Handle, new Vector2(phIconSize, phIconSize));
                            ImGui.SameLine(0, 4f * ImGuiHelpers.GlobalScale);
                        }
                    }
                }

                ImGui.Text($"{displayName}");
                ImGui.SameLine();

                uint currentHp = (closestFrame.Hp != null && idx < closestFrame.Hp.Count) ? closestFrame.Hp[idx] : 0;
                float hpPct = meta.MaxHp > 0 ? ((float)currentHp / meta.MaxHp) * 100f : 0f;
                ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), $"(HP: {currentHp} / {meta.MaxHp} {hpPct:F1}%)");

                ImGui.SameLine(350 * ImGuiHelpers.GlobalScale);
                uint upcoming = GetActionInRange(targetOffset, 0.1f, 1.5f);
                ImGui.Text("Next:"); ImGui.SameLine();
                DrawActionIconSmall(upcoming, 0.5f);
                ImGui.SameLine();
                uint current = GetActionInRange(targetOffset, -2.0f, 0.1f);
                ImGui.Text("Used:"); ImGui.SameLine();
                DrawActionIconSmall(current, 1.0f);

                var activeStatuses = GetActiveStatuses(recording, (uint)selectedEntityId, targetOffset);
                if (activeStatuses.Count > 0)
                {
                    int buffCount = 0;
                    foreach (var status in activeStatuses)
                    {
                        var sheetStatus = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>().GetRowOrDefault(status.Id);
                        if (sheetStatus == null) continue;

                        var icon = Service.TextureProvider.GetFromGameIcon(sheetStatus.Value.Icon).GetWrapOrDefault();
                        if (icon != null)
                        {
                            float aspectRatio = (float)icon.Width / icon.Height;
                            ImGui.Image(icon.Handle, new Vector2(24 * aspectRatio, 24) * ImGuiHelpers.GlobalScale);
                            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{sheetStatus.Value.Name}\n{status.RemainingDuration:F1}s");

                            buffCount++;
                            if (buffCount % 10 != 0 && buffCount < activeStatuses.Count)
                                ImGui.SameLine();
                        }
                    }
                    if (buffCount % 10 != 0) ImGui.NewLine();
                }

                bool isCasting = false;
                if (closestFrame.Casts != null && idx < closestFrame.Casts.Count)
                {
                    var cast = closestFrame.Casts[idx];
                    if (cast.ActionId != 0 && cast.Total > 0)
                    {
                        isCasting = true;
                        var action = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRowOrDefault(cast.ActionId);
                        string actionName = action?.Name.ToString() ?? "Unknown";
                        float pct = Math.Clamp(cast.Current / cast.Total, 0f, 1f);

                        ImGui.AlignTextToFramePadding();
                        ImGui.Text($"{actionName}:");
                        ImGui.SameLine();

                        ImGui.ProgressBar(pct, new Vector2(ImGui.GetContentRegionAvail().X, 15 * ImGuiHelpers.GlobalScale), $"{cast.Current:F1}s");
                    }
                }
                if (!isCasting)
                {
                    float height = Math.Max(ImGui.GetTextLineHeight(), 15 * ImGuiHelpers.GlobalScale);
                    ImGui.Dummy(new Vector2(1, height));
                }
                ImGui.TableSetColumnIndex(1);

                if (closestFrame.Targets != null && idx < closestFrame.Targets.Count)
                {
                    ulong targetId = closestFrame.Targets[idx];
                    if (targetId != 0 && targetId != EmptyTargetID)
                    {
                        int targetIdx = closestFrame.Ids.IndexOf((uint)targetId);
                        if (targetIdx != -1 && recording.Metadata.TryGetValue((uint)targetId, out var targetMeta))
                        {
                            ImGui.TextDisabled("Targeting:");

                            string targetDisplayName = targetMeta.Name;
                            if (configuration.AnonymizeNames && targetMeta.ClassJobId != 0)
                            {
                                var jobRow = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>()
                                    .GetRowOrDefault(targetMeta.ClassJobId);
                                targetDisplayName = jobRow.HasValue ? jobRow.Value.Abbreviation.ToString() : "Job";
                            }

                            uint targetPhId = GetActivePhantomJobIconId((uint)targetId, targetOffset);
                            if (targetPhId != 0)
                            {
                                var sheetStatus = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>().GetRowOrDefault(targetPhId);
                                if (sheetStatus.HasValue && sheetStatus.Value.Icon != 0)
                                {
                                    var icon = Service.TextureProvider.GetFromGameIcon(sheetStatus.Value.Icon).GetWrapOrDefault();
                                    if (icon != null)
                                    {
                                        float phIconSize = ImGui.GetTextLineHeight();
                                        ImGui.Image(icon.Handle, new Vector2(phIconSize, phIconSize));
                                        ImGui.SameLine(0, 4f * ImGuiHelpers.GlobalScale);
                                    }
                                }
                            }

                            ImGui.Text($"{targetDisplayName}");

                            float targetHpPct = (closestFrame.Hp != null && targetIdx < closestFrame.Hp.Count) ? (float)closestFrame.Hp[targetIdx] / targetMeta.MaxHp : 0f;
                            ImGui.ProgressBar(targetHpPct, new Vector2(ImGui.GetContentRegionAvail().X, 15 * ImGuiHelpers.GlobalScale), $"{targetHpPct * 100:F1}%");

                            var targetStatuses = GetActiveStatuses(recording, (uint)targetId, targetOffset);
                            if (targetStatuses.Count > 0)
                            {
                                int targetBuffCount = 0;
                                foreach (var status in targetStatuses)
                                {
                                    var sheetStatus = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>().GetRowOrDefault(status.Id);
                                    if (sheetStatus == null) continue;

                                    var icon = Service.TextureProvider.GetFromGameIcon(sheetStatus.Value.Icon).GetWrapOrDefault();
                                    if (icon != null)
                                    {
                                        float aspectRatio = (float)icon.Width / icon.Height;
                                        ImGui.Image(icon.Handle, new Vector2(24 * aspectRatio, 24) * ImGuiHelpers.GlobalScale);
                                        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{sheetStatus.Value.Name}\n{status.RemainingDuration:F1}s");

                                        targetBuffCount++;
                                        if (targetBuffCount % 10 != 0 && targetBuffCount < targetStatuses.Count)
                                            ImGui.SameLine();
                                    }
                                }
                                if (targetBuffCount % 10 != 0) ImGui.NewLine();
                            }
                        }
                        else
                        {
                            ImGui.TextDisabled("Targeting:");
                            ImGui.Text("Unknown Entity");
                        }
                    }
                    else
                    {
                        ImGui.TextDisabled("No Target");
                    }
                }

                ImGui.EndTable();
            }
        }

        private uint GetActionInRange(float currentTime, float startOffset, float endOffset)
        {
            if (ActiveDeathReplay == null) return 0;
            var frames = ActiveDeathReplay.ReplayData?.Frames;
            if (frames == null || frames.Count == 0) return 0;

            float targetStart = currentTime + startOffset;
            float targetEnd = currentTime + endOffset;
            if (targetEnd < targetStart) return 0;

            int left = 0;
            int right = frames.Count - 1;
            int startIndex = frames.Count;

            while (left <= right)
            {
                int mid = (left + right) / 2;
                if (frames[mid].TimeOffset < targetStart) left = mid + 1;
                else
                {
                    startIndex = mid;
                    right = mid - 1;
                }
            }

            if (startIndex >= frames.Count) return 0;

            for (int i = startIndex; i < frames.Count; i++)
            {
                var f = frames[i];
                if (f.TimeOffset > targetEnd) break;
                if (f.Ids == null || f.Actions == null) continue;

                int idx = f.Ids.IndexOf((uint)selectedEntityId);
                if (idx >= 0 && idx < f.Actions.Count)
                {
                    uint action = f.Actions[idx];
                    if (action != 0) return action;
                }
            }
            return 0;
        }

        private List<(uint Id, float RemainingDuration)> GetActiveStatuses(ReplayRecording recording, uint entityId, float currentTime)
        {
            for (int i = recording.Frames.Count - 1; i >= 0; i--)
            {
                var frame = recording.Frames[i];

                if (frame.TimeOffset > currentTime) continue;
                if (currentTime - frame.TimeOffset > 120f) break;

                int idx = frame.Ids.IndexOf(entityId);

                if (idx == -1 || frame.Statuses == null || idx >= frame.Statuses.Count || frame.Statuses[idx] == null)
                    continue;

                return frame.Statuses[idx]
                    .Select(status =>
                    {
                        float timeElapsed = currentTime - frame.TimeOffset;
                        return (Id: status.Id, Remaining: status.Duration - timeElapsed);
                    })
                    .Where(s => s.Remaining > 0f || (s.Id >= 4358 && s.Id <= 4369) || (s.Id >= 4803 && s.Id <= 4805) || (s.Id >= 5328 && s.Id <= 5335))
                    .Select(s => (s.Id, s.Remaining))
                    .ToList();
            }

            return new();
        }

        private void DrawActionIconSmall(uint actionId, float alpha)
        {
            if (actionId == 0) { ImGui.Dummy(new Vector2(24, 24) * ImGuiHelpers.GlobalScale); return; }

            var action = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRowOrDefault(actionId);
            if (!action.HasValue) return;

            var iconWrap = action.Value.Icon != 0 ? AetherBlackbox.DrawingLogic.TextureManager.GetTexture($"luminaicon:{action.Value.Icon}") : null;
            if (iconWrap != null)
            {
                ImGui.Image(iconWrap.Handle, new Vector2(24, 24) * ImGuiHelpers.GlobalScale, Vector2.Zero, Vector2.One, new Vector4(1, 1, 1, alpha));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(action.Value.Name.ToString());
            }
            else
            {
                ImGui.TextColored(new Vector4(1.0f, 1.0f, 1.0f, alpha), action.Value.Name.ToString());
            }
        }
    }
}