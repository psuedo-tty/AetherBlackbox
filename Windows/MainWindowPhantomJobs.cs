using System.Collections.Generic;
using System.Linq;
using AetherBlackbox.Core;
using Lumina.Excel.Sheets;

namespace AetherBlackbox.Windows
{
    public partial class MainWindow
    {
        private static readonly Dictionary<uint, uint> PhantomIconMap = new()
        {
            { 4358, 216872 }, { 4359, 216873 }, { 4360, 216874 }, { 4361, 216875 },
            { 4362, 216876 }, { 4363, 216877 }, { 4364, 216878 }, { 4365, 216879 },
            { 4366, 216880 }, { 4367, 216881 }, { 4368, 216882 }, { 4369, 216883 },
            { 4803, 216884 }, { 4804, 216885 }, { 4805, 216886 },
            { 5328, 216887 }, { 5329, 216888 }, { 5330, 216889 }, { 5331, 216890 },
            { 5332, 216891 }, { 5333, 216892 }, { 5334, 216893 }, { 5335, 216894 }
        };

        public uint GetActivePhantomJobIconId(ulong entityId, float timeOffset)
        {
            // Use the replay's TerritoryTypeId instead of the current client state
            if (ActiveDeathReplay == null || (ActiveDeathReplay.TerritoryTypeId != 1197 && ActiveDeathReplay.TerritoryTypeId != 1252 && ActiveDeathReplay.TerritoryTypeId != 1346))
                return 0;

            var statuses = GetActiveStatuses(ActiveDeathReplay.ReplayData, (uint)entityId, timeOffset);
            /*
            if (timeOffset >= 10.0f && timeOffset <= 13.0f)
            {
                foreach (var status in statuses)
                {
                    Service.PluginLog.Debug($"[Diagnostic] Entity {entityId} at {timeOffset:F2}s has Status ID: {status.Id}");
                }
            }*/

            foreach (var status in statuses)
            {
                if (PhantomIconMap.TryGetValue(status.Id, out var iconId))
                    return iconId;
            }
            return 0;
        }
    }
}