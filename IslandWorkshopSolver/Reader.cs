using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Plugin;
using System.Reflection;
using Dalamud.Utility.Signatures;
using Lumina.Excel.GeneratedSheets;
using Lumina.Excel;
using Dalamud.Logging;
using IslandWorkshopSolver.Solver;
using Lumina;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace IslandWorkshopSolver
{
    // Lifted entirely from Rietty's AutoMammet, who apparently took it from Otter. Bless you both. <3
    public class Reader
    {
        private readonly IReadOnlyList<string> items;
        private readonly IReadOnlyList<string> popularities;
        private readonly IReadOnlyList<string> supplies;
        private readonly IReadOnlyList<string> shifts;
        private readonly ExcelSheet<MJICraftworksPopularity> sheet;
        private int lastValidDay = -1;
        private int lastHash = -1;

        public Reader(DalamudPluginInterface pluginInterface)
        {
            SignatureHelper.Initialise(this);
            items = DalamudPlugins.GameData.GetExcelSheet<MJICraftworksObject>()!.Select(o => o.Item.Value?.Name.ToString() ?? string.Empty)
               .Where(s => s.Length > 0).Prepend(string.Empty).ToArray();
            var itemSheet = DalamudPlugins.GameData.GetExcelSheet<Lumina.Excel.GeneratedSheets.Item>()!;
            //This will probably need to be changed if we get new mats/crafts
            string[] materialNames = Enumerable.Range(37551, 61).Select(i => itemSheet.GetRow((uint)i)!.Name.ToString()).ToArray();
            var addon = DalamudPlugins.GameData.GetExcelSheet<Addon>()!;
            shifts = Enumerable.Range(15186, 5).Select(i => addon.GetRow((uint)i)!.Text.ToString()).ToArray();
            supplies = Enumerable.Range(15181, 5).Reverse().Select(i => addon.GetRow((uint)i)!.Text.ToString()).ToArray();
            popularities = Enumerable.Range(15177, 4).Select(i => addon.GetRow((uint)i)!.Text.ToString()).Prepend(string.Empty).ToArray();
            
            var craftIDs = DalamudPlugins.GameData.GetExcelSheet<MJICraftworksObject>()!.Select(o => o.Item.Value?.RowId ?? 0)
               .Where(r => r > 0).ToArray();
            List<string> craftNames = new List<string>();
            foreach (var craft in craftIDs)
            {
                string name = itemSheet.GetRow((uint)craft)!.Name.ToString();
                craftNames.Add(name);
            }

            ItemHelper.InitFromGameData(craftNames);
            //Maps material ID to value
            var rareMats = DalamudPlugins.GameData.GetExcelSheet<MJIDisposalShopItem>()!.Where(i => i.Unknown1 == 0).ToDictionary(i => i.Unknown0, i=> i.Unknown2);
            RareMaterialHelper.InitFromGameData(rareMats, materialNames);

            var supplyMods = DalamudPlugins.GameData.GetExcelSheet<MJICraftworksSupplyDefine>()!.ToDictionary(i => i.RowId, i => i.Unknown1);
            SupplyHelper.InitFromGameData(supplyMods);

            var popMods = DalamudPlugins.GameData.GetExcelSheet<MJICraftworksPopularityType>()!.ToDictionary(i => i.RowId, i => i.Unknown0);
            PopularityHelper.InitFromGameData(popMods);

            var validItemRows = DalamudPlugins.GameData.GetExcelSheet<MJICraftworksObject>()!.Where(s => (s.Item.Value?.Name.ToString() ?? string.Empty).Length > 0);

            //"Isleworks Pumpkin Pudding", 37662, firstItem.Item.Value!.Name, firstItem.Item.Value.RowId
            //4, 0, 0,  cat1, cat2, ? firstItem.Unknown1, firstItem.Unknown2, firstItem.Unknown3
            //45, 3, 56, 1, 60, 1, mat1 id, mat1 q, etc. firstItem.Unknown4, firstItem.Unknown5, firstItem.Unknown6, firstItem.Unknown7, firstItem.Unknown8, firstItem.Unknown9
            //0, 0, 8, 6, 78 ?? rank, hours, basevalue firstItem.Unknown10, firstItem.Unknown11, firstItem.Unknown12, firstItem.Unknown13, firstItem.Unknown14
            //Items.Add(new ItemInfo(PumpkinPudding, Confections, Invalid, 78, 6, 8, new Dictionary<RareMaterial, int>() { { Pumpkin, 3 }, { Egg, 1 }, { Milk, 1 } }));

            List<ItemInfo> itemInfos = new List<ItemInfo>();
            int itemIndex = 0;
            foreach (var item in validItemRows)
            {
                Dictionary<Material, int> mats = new Dictionary<Material, int>();
                if(item.Unknown5 > 0)
                    mats.Add((Material)item.Unknown4, item.Unknown5);
                if (item.Unknown7 > 0)
                    mats.Add((Material)item.Unknown6, item.Unknown7);
                if (item.Unknown9 > 0)
                    mats.Add((Material)item.Unknown8, item.Unknown9);

                if(Enum.IsDefined((Solver.Item)itemIndex))
                {
                    itemInfos.Add(new ItemInfo((Solver.Item)itemIndex, (ItemCategory)item.Unknown1, (ItemCategory)item.Unknown2, item.Unknown14, item.Unknown13, item.Unknown12, mats));
                    PluginLog.Verbose("Adding item {0} with material count {1}", (Solver.Item)itemIndex, mats.Count);

                    itemIndex++;
                }
                
            }

            Solver.Solver.InitItemsFromGameData(itemInfos);

            sheet = DalamudPlugins.GameData.GetExcelSheet<MJICraftworksPopularity>()!;
        }

        //This is in one method because island rank has a default invalid value whereas landmarks might just not be built
        public unsafe (int rank, int maxGroove) GetIslandRankAndMaxGroove()
        {
            if (MJIManager.Instance() == null)
                return (-1,-1);

            var currentRank = MJIManager.Instance()->CurrentRank;

            int completedLandmarks = 0;
            for (int i=0;  i< MJILandmarkPlacements.Slots; i++)
            {
                PluginLog.Verbose("Landmark {0} ID {1}, placement {4}, under construction {2}, hours to complete {3}", i,
                     MJIManager.Instance()->LandmarkIds[i], MJIManager.Instance()->LandmarkUnderConstruction[i], MJIManager.Instance()->LandmarkHoursToCompletion[i], MJIManager.Instance()->LandmarkPlacements[i]->LandmarkId);
                if (MJIManager.Instance()->LandmarkIds[i] != 0)
                {
                    if (MJIManager.Instance()->LandmarkUnderConstruction[i] == 0)
                        completedLandmarks++;
                }
                    
            }
            var tension = DalamudPlugins.GameData.GetExcelSheet<MJICraftworksTension>()!;
            var maxGroove = tension.GetRow((uint)completedLandmarks)!.Unknown0;

            PluginLog.Debug("Found {0} completed landmarks, setting max groove to {1}", completedLandmarks, maxGroove);

            PluginLog.Debug("Island rank {0}", currentRank);
            return (currentRank, maxGroove);
        }

        public unsafe (int bonus, bool error) GetWorkshopBonus()
        {
            if (MJIManager.Instance() == null)
                return (-1, false);

            int minLevel = 999;
            bool showError = false;
            int numWorkshops = 0;
            for (int i = 0; i < /*MJIWorkshops.MaxWorkshops*/ 3; i++)
            {
                if (MJIManager.Instance()->Workshops.PlaceId[i] != 0)
                {
                    PluginLog.Verbose("Workshop {0} level {1}, under construction {2}, hours to complete {3}, placeID {4}", i,
                    MJIManager.Instance()->Workshops.BuildingLevel[i] + 1, MJIManager.Instance()->Workshops.UnderConstruction[i],
                    MJIManager.Instance()->Workshops.HoursToCompletion[i], MJIManager.Instance()->Workshops.PlaceId[i]);
                    numWorkshops++;
                    if (MJIManager.Instance()->Workshops.UnderConstruction[i] == 0)
                        minLevel = Math.Min(minLevel, MJIManager.Instance()->Workshops.BuildingLevel[i]);
                    else if (MJIManager.Instance()->Workshops.HoursToCompletion[i] == 0)
                        showError = true;  
                }
                else
                {
                    PluginLog.Verbose("Workshop {0} not built", i);
                }
            }
            
            int bonus = -1;

            if(minLevel < 999)
            {
                minLevel++; //Level appears to be 0-indexed but data is 1-indexed, so
                var workshopBonusSheet = DalamudPlugins.GameData.GetExcelSheet<MJICraftworksRankRatio>()!;
                bonus = workshopBonusSheet.GetRow((uint)minLevel)!.Unknown0;
            }

            PluginLog.Debug("Found min workshop rank of {0} with {2} workshops, setting bonus to {1}", minLevel, bonus, numWorkshops);

            return (bonus, showError);
        }

        public unsafe bool GetInventory(out Dictionary<int, int> inventory)
        {
            inventory = new Dictionary<int, int>();
            var uiModulePtr = DalamudPlugins.GameGui.GetUIModule();
            if (uiModulePtr == IntPtr.Zero)
                return false;

            var agentModule = ((UIModule*)uiModulePtr)->GetAgentModule();
            if (agentModule == null)
                return false;

            var mjiPouch = agentModule->GetAgentMJIPouch();
            if (mjiPouch == null)
                return false;

            if (mjiPouch->InventoryData == null)
                return false;

            int totalItems = 0;
            for (ulong i = 0; i < mjiPouch->InventoryData->Inventory.Size(); i++)
            {
                var invItem = mjiPouch->InventoryData->Inventory.Get(i);
                PluginLog.Verbose("MJI Pouch inventory item: name {2}, slotIndex {0}, stack {1}", invItem.SlotIndex, invItem.StackSize, invItem.Name);
                totalItems += invItem.StackSize;
                if(i < (int)Material.NumMats)
                    inventory.Add(invItem.SlotIndex, invItem.StackSize);
            }

            return totalItems > 0;
        }

        public unsafe (int,string) ExportIsleData()
        {
            if (MJIManager.Instance() == null)
                return (-1, ""); 


            var currentPopularity = sheet.GetRow(MJIManager.Instance()->CurrentPopularity)!; 
            var nextPopularity = sheet.GetRow(MJIManager.Instance()->NextPopularity)!; 
            PluginLog.Information("Current pop index {0}, next pop index {1}", currentPopularity.RowId, nextPopularity.RowId);

            var sb = new StringBuilder(64 * 128);
            int numNE = 0;
            for (var i = 1; i < items.Count; ++i)
            {
                sb.Append(items[i]);
                sb.Append('\t');
                sb.Append(GetPopularity(currentPopularity, i));
                sb.Append('\t');
                var supply = (int)MJIManager.Instance()->GetSupplyForCraftwork((uint)i);
                var shift = (int)MJIManager.Instance()->GetDemandShiftForCraftwork((uint)i);
                if (supply == (int)Supply.Nonexistent)
                    numNE++;
                sb.Append(supply);
                sb.Append('\t');
                sb.Append(shift);
                sb.Append('\t');
                sb.Append(GetPopularity(nextPopularity, i));
                sb.Append('\n');
            }

            

            string returnStr = sb.ToString();
            int newHash = returnStr.GetHashCode();
            if (numNE == items.Count - 1)
                PluginLog.Warning("Reading invalid supply data (all Nonexistent). Need to talk to the mammet");
            else if (lastHash == -1 || lastHash != newHash)
            {
                int currentDay = Solver.Solver.GetCurrentDay();
                PluginLog.Debug("New valid supply data detected! Previous hash: {0}, day {1}, Current hash: {2}, day {3})", lastHash, lastValidDay, newHash, currentDay) ; 
                PluginLog.Verbose("{0}", returnStr);

                lastHash = newHash;
                lastValidDay = currentDay;
            }
            else
                PluginLog.LogDebug("Reading same valid supply data as before, not updating hash or day");

            return (lastValidDay, sb.ToString());
        }

        private int GetPopularity(MJICraftworksPopularity pop, int idx)
        {
            var val = (byte?)pop.GetType().GetProperty($"Unknown{idx}", BindingFlags.Instance | BindingFlags.Public | BindingFlags.GetProperty)?.GetValue(pop, null);
            return val == null ? 0 : val.Value; // string.Empty : popularities[val.Value];
        }
    }
}
