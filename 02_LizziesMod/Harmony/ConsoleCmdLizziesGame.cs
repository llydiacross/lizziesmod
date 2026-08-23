using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace LizziesMod
{
    public class ConsoleCmdLizziesGame : ConsoleCmdAbstract
    {
        private const int MaximumListEntries = 50;

        public override string[] getCommands()
        {
            return new[] { "lizziesgame", "lizziesqa" };
        }

        public override string getDescription()
        {
            return "Inspects and exercises player, world, inventory, spawn, and LizziesMod gameplay systems.";
        }

        public override string getHelp()
        {
            return "Usage:\n" +
                "  lizziesgame player | world | time\n" +
                "  lizziesgame block [x y z]\n" +
                "  lizziesgame entities [radius]\n" +
                "  lizziesgame give|take|count <item-or-block> [count]\n" +
                "  lizziesgame teleport <x> <y> <z>\n" +
                "  lizziesgame spawnworld | spawnstatus\n" +
                "  lizziesgame buff add|remove|has <buff-id>\n" +
                "  lizziesgame spawn list [props|entities|ragdolls] [filter]\n" +
                "  lizziesgame spawn <entry-id> | grant <prop-id> | undo | clear\n" +
                "  lizziesgame inputs [filter] | textures [filter]\n" +
                "  lizziesgame xml [items|blocks|recipes] [filter]\n" +
                "  lizziesgame portal | ui <window-name>\n\n" +
                "lizziesqa is an alias. List output is capped at " + MaximumListEntries + " entries.";
        }

        public override void Execute(List<string> parameters, CommandSenderInfo senderInfo)
        {
            if (parameters == null || parameters.Count == 0)
            {
                Output(getHelp(), senderInfo);
                return;
            }

            switch (parameters[0].ToLowerInvariant())
            {
                case "help":
                    Output(getHelp(), senderInfo);
                    return;
                case "player":
                    if (!HasParameterCount(parameters, 1, senderInfo)) return;
                    OutputPlayer(senderInfo);
                    return;
                case "world":
                    if (!HasParameterCount(parameters, 1, senderInfo)) return;
                    OutputWorld(senderInfo);
                    return;
                case "time":
                    if (!HasParameterCount(parameters, 1, senderInfo)) return;
                    OutputTime(senderInfo);
                    return;
                case "block":
                    OutputBlock(parameters, senderInfo);
                    return;
                case "entities":
                    OutputEntities(parameters, senderInfo);
                    return;
                case "give":
                case "take":
                case "count":
                    HandleInventory(parameters, senderInfo);
                    return;
                case "teleport":
                    TeleportPlayer(parameters, senderInfo);
                    return;
                case "spawnworld":
                    if (!HasParameterCount(parameters, 1, senderInfo)) return;
                    RequestSpawnIntoWorld(senderInfo);
                    return;
                case "spawnstatus":
                    if (!HasParameterCount(parameters, 1, senderInfo)) return;
                    OutputSpawnStatus(senderInfo);
                    return;
                case "buff":
                    HandleBuff(parameters, senderInfo);
                    return;
                case "spawn":
                    HandleSpawn(parameters, senderInfo);
                    return;
                case "inputs":
                    if (parameters.Count > 2)
                    {
                        Output(getHelp(), senderInfo);
                        return;
                    }

                    OutputInputs(parameters.Count == 2 ? parameters[1] : "", senderInfo);
                    return;
                case "textures":
                    if (parameters.Count > 2)
                    {
                        Output(getHelp(), senderInfo);
                        return;
                    }

                    OutputTextures(parameters.Count == 2 ? parameters[1] : "", senderInfo);
                    return;
                case "xml":
                    OutputUserXml(parameters, senderInfo);
                    return;
                case "portal":
                    if (!HasParameterCount(parameters, 1, senderInfo)) return;
                    OutputPortal(senderInfo);
                    return;
                case "ui":
                    if (!HasParameterCount(parameters, 2, senderInfo)) return;
                    OpenWindow(parameters[1], senderInfo);
                    return;
                default:
                    Output("Unknown LizziesMod game operation '" + parameters[0] + "'.\n\n" + getHelp(), senderInfo);
                    return;
            }
        }

        private static bool HasParameterCount(List<string> parameters, int expectedCount, CommandSenderInfo senderInfo)
        {
            if (parameters.Count == expectedCount) return true;

            Output("This operation expects " + (expectedCount - 1) + " argument(s). Run lizziesgame help for usage.", senderInfo);
            return false;
        }

        private static void OutputPlayer(CommandSenderInfo senderInfo)
        {
            EntityPlayerLocal player;
            World world;
            if (!TryGetPlayerAndWorld(out player, out world, senderInfo)) return;

            Vector3 position = player.position;
            Vector3i blockPosition = new Vector3i(
                Mathf.FloorToInt(position.x),
                Mathf.FloorToInt(position.y),
                Mathf.FloorToInt(position.z));
            string heldItem = player.inventory != null && player.inventory.holdingItemItemValue != null
                ? player.inventory.holdingItemItemValue.ItemClass.Name
                : "none";
            Output("Player " + player.entityId + " | admin=" + player.IsAdmin +
                " | dimension=" + DimensionManager.ActiveDimensionId +
                " | stamina=" + player.Stats.Stamina.Value.ToString("F1", CultureInfo.InvariantCulture) +
                " | held=" + heldItem +
                " | block=" + blockPosition.x + ", " + blockPosition.y + ", " + blockPosition.z, senderInfo);
        }

        private static void OutputWorld(CommandSenderInfo senderInfo)
        {
            EntityPlayerLocal player;
            World world;
            if (!TryGetPlayerAndWorld(out player, out world, senderInfo)) return;

            int entityCount = world.Entities != null && world.Entities.list != null ? world.Entities.list.Count : 0;
            Output("World | dimension=" + DimensionManager.ActiveDimensionId +
                " | game time=" + world.worldTime +
                " | day=" + GameUtils.WorldTimeToDays(world.worldTime) +
                " | year=" + TimeManager.GetGameYear() +
                " | active entities=" + entityCount +
                " | chunk cache=" + (world.ChunkCache != null), senderInfo);
        }

        private static void OutputTime(CommandSenderInfo senderInfo)
        {
            EntityPlayerLocal player;
            World world;
            if (!TryGetPlayerAndWorld(out player, out world, senderInfo)) return;

            Output("Time | world time=" + world.worldTime +
                " | day=" + GameUtils.WorldTimeToDays(world.worldTime) +
                " | starting year=" + TimeManager.GetStartingYear() +
                " | game year=" + TimeManager.GetGameYear(), senderInfo);
        }

        private static void OutputBlock(List<string> parameters, CommandSenderInfo senderInfo)
        {
            if (parameters.Count != 1 && parameters.Count != 4)
            {
                Output("Usage: lizziesgame block [x y z]", senderInfo);
                return;
            }

            EntityPlayerLocal player;
            World world;
            if (!TryGetPlayerAndWorld(out player, out world, senderInfo)) return;

            Vector3i position;
            if (parameters.Count == 4)
            {
                int x;
                int y;
                int z;
                if (!int.TryParse(parameters[1], out x) || !int.TryParse(parameters[2], out y) || !int.TryParse(parameters[3], out z))
                {
                    Output("Block coordinates must be integers.", senderInfo);
                    return;
                }

                position = new Vector3i(x, y, z);
            }
            else
            {
                position = new Vector3i(
                    Mathf.FloorToInt(player.position.x),
                    Mathf.FloorToInt(player.position.y),
                    Mathf.FloorToInt(player.position.z));
            }

            BlockValue blockValue = world.GetBlock(position);
            string blockName = blockValue.Block != null ? blockValue.Block.GetBlockName() : "air";
            object tileEntity = world.GetTileEntity(position);
            Output("Block " + position.x + ", " + position.y + ", " + position.z +
                " | name=" + blockName +
                " | tile entity=" + (tileEntity == null ? "none" : tileEntity.GetType().Name), senderInfo);
        }

        private static void OutputEntities(List<string> parameters, CommandSenderInfo senderInfo)
        {
            if (parameters.Count > 2)
            {
                Output("Usage: lizziesgame entities [radius]", senderInfo);
                return;
            }

            EntityPlayerLocal player;
            World world;
            if (!TryGetPlayerAndWorld(out player, out world, senderInfo)) return;

            float radius = 24f;
            if (parameters.Count == 2 && (!TryParseFloat(parameters[1], out radius) || radius <= 0f || radius > 250f))
            {
                Output("Entity radius must be a number from 0 to 250.", senderInfo);
                return;
            }

            List<Entity> entities = world.Entities != null && world.Entities.list != null
                ? new List<Entity>(world.Entities.list)
                : new List<Entity>();
            entities.Sort((left, right) =>
                Vector3.SqrMagnitude(left.position - player.position).CompareTo(Vector3.SqrMagnitude(right.position - player.position)));

            StringBuilder report = new StringBuilder();
            int matchedCount = 0;
            int displayedCount = 0;
            float radiusSquared = radius * radius;
            foreach (Entity entity in entities)
            {
                if (entity == null || Vector3.SqrMagnitude(entity.position - player.position) > radiusSquared) continue;

                matchedCount++;
                if (displayedCount >= MaximumListEntries) continue;

                displayedCount++;
                report.AppendLine("- " + entity.entityId + " | " + entity.GetType().Name +
                    " | distance=" + Vector3.Distance(entity.position, player.position).ToString("F1", CultureInfo.InvariantCulture) +
                    " | position=" + entity.position.x.ToString("F1", CultureInfo.InvariantCulture) + ", " +
                    entity.position.y.ToString("F1", CultureInfo.InvariantCulture) + ", " +
                    entity.position.z.ToString("F1", CultureInfo.InvariantCulture));
            }

            string suffix = matchedCount > displayedCount ? " (showing first " + displayedCount + ")" : "";
            Output("Entities within " + radius.ToString("F1", CultureInfo.InvariantCulture) + ": " + matchedCount + suffix +
                (displayedCount == 0 ? "" : "\n" + report.ToString().TrimEnd()), senderInfo);
        }

        private static void HandleInventory(List<string> parameters, CommandSenderInfo senderInfo)
        {
            if (parameters.Count < 2 || parameters.Count > 3)
            {
                Output("Usage: lizziesgame give|take|count <item-or-block> [count]", senderInfo);
                return;
            }

            EntityPlayerLocal player;
            World world;
            if (!TryGetPlayerAndWorld(out player, out world, senderInfo)) return;
            if (player.inventory == null)
            {
                Output("The local player's inventory is unavailable.", senderInfo);
                return;
            }

            ItemValue itemValue;
            string resolvedName;
            if (!TryResolveItem(parameters[1], out itemValue, out resolvedName))
            {
                Output("Could not resolve item or block '" + parameters[1] + "'.", senderInfo);
                return;
            }

            int requestedCount = 1;
            if (parameters.Count == 3 && (!int.TryParse(parameters[2], out requestedCount) || requestedCount < 1 || requestedCount > 10000))
            {
                Output("Item count must be an integer from 1 to 10000.", senderInfo);
                return;
            }

            string operation = parameters[0].ToLowerInvariant();
            if (operation == "count")
            {
                Output("Inventory count for '" + resolvedName + "': " + player.inventory.GetItemCount(itemValue, true), senderInfo);
                return;
            }

            if (operation == "give")
            {
                if (!player.inventory.AddItem(new ItemStack(itemValue, requestedCount)))
                {
                    Output("Inventory is full; could not add '" + resolvedName + "'.", senderInfo);
                    return;
                }

                player.inventory.onInventoryChanged();
                Output("Added " + requestedCount + " x '" + resolvedName + "'.", senderInfo);
                return;
            }

            int removedCount = player.inventory.DecItem(itemValue, requestedCount, false, null);
            player.inventory.onInventoryChanged();
            Output("Removed " + removedCount + " of " + requestedCount + " requested '" + resolvedName + "'.", senderInfo);
        }

        private static bool TryResolveItem(string identifier, out ItemValue itemValue, out string resolvedName)
        {
            itemValue = default(ItemValue);
            resolvedName = "";
            if (string.IsNullOrEmpty(identifier)) return false;

            ItemClass itemClass = ItemClass.GetItemClass(identifier, false);
            if (itemClass != null)
            {
                itemValue = new ItemValue(itemClass.Id);
                resolvedName = itemClass.Name;
                return true;
            }

            BlockValue blockValue = Block.GetBlockValue(identifier, false);
            if (blockValue.Block == null) return false;

            itemValue = blockValue.ToItemValue();
            resolvedName = blockValue.Block.GetBlockName();
            return true;
        }

        private static void TeleportPlayer(List<string> parameters, CommandSenderInfo senderInfo)
        {
            if (!HasParameterCount(parameters, 4, senderInfo)) return;

            float x;
            float y;
            float z;
            if (!TryParseFloat(parameters[1], out x) || !TryParseFloat(parameters[2], out y) || !TryParseFloat(parameters[3], out z))
            {
                Output("Teleport coordinates must be numbers.", senderInfo);
                return;
            }

            EntityPlayerLocal player;
            World world;
            if (!TryGetPlayerAndWorld(out player, out world, senderInfo)) return;

            Vector3 destination = new Vector3(x, y, z);
            player.SetPosition(destination, true);
            Output("Teleported to " + x.ToString("F2", CultureInfo.InvariantCulture) + ", " +
                y.ToString("F2", CultureInfo.InvariantCulture) + ", " + z.ToString("F2", CultureInfo.InvariantCulture) + ".", senderInfo);
        }

        private static void RequestSpawnIntoWorld(CommandSenderInfo senderInfo)
        {
            if (!ModSettingsManager.IsDeveloperMode)
            {
                Output("World spawn automation is available only in developer mode.", senderInfo);
                return;
            }

            if (GameManager.Instance == null)
            {
                Output("The game manager is unavailable.", senderInfo);
                return;
            }

            GameSpawnAutomation.RequestSpawn();
            Output("Queued the native Spawn action. It will run as soon as the loading screen is ready.", senderInfo);
        }

        private static void OutputSpawnStatus(CommandSenderInfo senderInfo)
        {
            Output("World spawn | queued=" + GameSpawnAutomation.IsSpawnQueued +
                " | ready=" + GameSpawnAutomation.IsSpawnReady +
                " | game starting=" + (GameManager.Instance != null && GameManager.Instance.IsStartingGame), senderInfo);
        }

        private static void HandleBuff(List<string> parameters, CommandSenderInfo senderInfo)
        {
            if (!HasParameterCount(parameters, 3, senderInfo)) return;

            EntityPlayerLocal player;
            World world;
            if (!TryGetPlayerAndWorld(out player, out world, senderInfo)) return;

            string operation = parameters[1].ToLowerInvariant();
            string buffId = parameters[2];
            if (operation == "add")
            {
                player.Buffs.AddBuff(buffId);
                Output("Buff '" + buffId + "' present=" + player.Buffs.HasBuff(buffId) + ".", senderInfo);
                return;
            }

            if (operation == "remove")
            {
                player.Buffs.RemoveBuff(buffId);
                Output("Buff '" + buffId + "' present=" + player.Buffs.HasBuff(buffId) + ".", senderInfo);
                return;
            }

            if (operation == "has")
            {
                Output("Buff '" + buffId + "' present=" + player.Buffs.HasBuff(buffId) + ".", senderInfo);
                return;
            }

            Output("Usage: lizziesgame buff add|remove|has <buff-id>", senderInfo);
        }

        private static void HandleSpawn(List<string> parameters, CommandSenderInfo senderInfo)
        {
            if (parameters.Count < 2)
            {
                Output("Usage: lizziesgame spawn list [props|entities|ragdolls] [filter] | <entry-id> | grant <prop-id> | undo | clear", senderInfo);
                return;
            }

            EntityPlayerLocal player;
            World world;
            if (!TryGetPlayerAndWorld(out player, out world, senderInfo)) return;

            string operation = parameters[1].ToLowerInvariant();
            if (operation == "list")
            {
                if (parameters.Count > 4)
                {
                    Output(getSpawnHelp(), senderInfo);
                    return;
                }

                SpawnMenuTab tab;
                if (!TryGetSpawnTab(parameters.Count >= 3 ? parameters[2] : "props", out tab))
                {
                    Output("Spawn type must be props, entities, or ragdolls.", senderInfo);
                    return;
                }

                OutputSpawnEntries(tab, parameters.Count == 4 ? parameters[3] : "", senderInfo);
                return;
            }

            if (!SpawnMenuManager.CanUse(player))
            {
                Output("The Prop Spawner is disabled or the local player is not allowed to use it.", senderInfo);
                return;
            }

            if (operation == "undo")
            {
                if (!HasParameterCount(parameters, 2, senderInfo)) return;
                SpawnMenuManager.RequestUndo(player);
                Output("Requested undo of the latest owned prop-spawner entry.", senderInfo);
                return;
            }

            if (operation == "clear")
            {
                if (!HasParameterCount(parameters, 2, senderInfo)) return;
                SpawnMenuManager.RequestClearOwned(player);
                Output("Requested cleanup of all owned prop-spawner entries.", senderInfo);
                return;
            }

            if (operation == "grant")
            {
                if (!HasParameterCount(parameters, 3, senderInfo)) return;

                SpawnablePropDefinition prop;
                if (!SpawnCatalog.TryGetProp(parameters[2], out prop))
                {
                    Output("No spawnable prop named '" + parameters[2] + "'.", senderInfo);
                    return;
                }

                SpawnMenuManager.RequestGrantItem(player, prop.Id);
                Output("Requested prop item '" + prop.Id + "'.", senderInfo);
                return;
            }

            if (!HasParameterCount(parameters, 2, senderInfo)) return;

            SpawnMenuEntryDefinition entry;
            if (!SpawnCatalog.TryGetEntry(parameters[1], out entry))
            {
                Output("No spawnable entry named '" + parameters[1] + "'. Use lizziesgame spawn list to inspect the catalog.", senderInfo);
                return;
            }

            SpawnMenuManager.RequestSpawn(player, entry.Id);
            Output("Requested " + entry.EntryType + " '" + entry.Id + "'.", senderInfo);
        }

        private static string getSpawnHelp()
        {
            return "Usage: lizziesgame spawn list [props|entities|ragdolls] [filter]";
        }

        private static bool TryGetSpawnTab(string value, out SpawnMenuTab tab)
        {
            switch ((value ?? "").ToLowerInvariant())
            {
                case "prop":
                case "props":
                    tab = SpawnMenuTab.Props;
                    return true;
                case "entity":
                case "entities":
                    tab = SpawnMenuTab.Entities;
                    return true;
                case "ragdoll":
                case "ragdolls":
                    tab = SpawnMenuTab.Ragdolls;
                    return true;
                default:
                    tab = SpawnMenuTab.Props;
                    return false;
            }
        }

        private static void OutputSpawnEntries(SpawnMenuTab tab, string filter, CommandSenderInfo senderInfo)
        {
            List<SpawnMenuEntryDefinition> entries = SpawnCatalog.GetEntries(tab, "all", filter);
            StringBuilder report = new StringBuilder();
            int displayedCount = Math.Min(entries.Count, MaximumListEntries);
            for (int index = 0; index < displayedCount; index++)
            {
                SpawnMenuEntryDefinition entry = entries[index];
                report.AppendLine("- " + entry.Id + " | " + entry.DisplayName +
                    " | category=" + entry.CategoryId + " | type=" + entry.EntryType);
            }

            string suffix = entries.Count > displayedCount ? " (showing first " + displayedCount + ")" : "";
            Output(tab + " spawn entries: " + entries.Count + suffix +
                (displayedCount == 0 ? "" : "\n" + report.ToString().TrimEnd()), senderInfo);
        }

        private static void OutputInputs(string filter, CommandSenderInfo senderInfo)
        {
            List<CustomInputDefinition> inputs = CustomInputManager.GetInputs();
            StringBuilder report = new StringBuilder();
            int matchedCount = 0;
            int displayedCount = 0;
            foreach (CustomInputDefinition input in inputs)
            {
                if (!MatchesFilter(filter, input.Id, input.ModName, input.Name, input.Category, input.Description)) continue;

                matchedCount++;
                if (displayedCount >= MaximumListEntries) continue;

                displayedCount++;
                report.AppendLine("- " + input.Id + " | " + input.Name +
                    " | mod=" + input.ModName + " | chord=" + input.Chord +
                    " | default=" + input.DefaultChord);
            }

            string suffix = matchedCount > displayedCount ? " (showing first " + displayedCount + ")" : "";
            Output("Custom inputs: " + matchedCount + suffix +
                (displayedCount == 0 ? "" : "\n" + report.ToString().TrimEnd()), senderInfo);
        }

        private static void OutputTextures(string filter, CommandSenderInfo senderInfo)
        {
            StringBuilder report = new StringBuilder();
            int matchedCount = 0;
            int displayedCount = 0;
            foreach (CustomTexture texture in CustomTextureManager.CustomTextures)
            {
                if (!MatchesFilter(filter, texture.Name, texture.DisplayName, texture.Group)) continue;

                matchedCount++;
                if (displayedCount >= MaximumListEntries) continue;

                displayedCount++;
                report.AppendLine("- " + texture.Name + " | " + texture.DisplayName +
                    " | paint=" + texture.PaintID + " | texture=" + texture.TextureID +
                    " | group=" + texture.Group + " | hidden=" + texture.Hidden);
            }

            string suffix = matchedCount > displayedCount ? " (showing first " + displayedCount + ")" : "";
            Output("Custom textures: " + matchedCount + suffix +
                (displayedCount == 0 ? "" : "\n" + report.ToString().TrimEnd()), senderInfo);
        }

        private static void OutputUserXml(List<string> parameters, CommandSenderInfo senderInfo)
        {
            if (parameters.Count > 3)
            {
                Output("Usage: lizziesgame xml [items|blocks|recipes] [filter]", senderInfo);
                return;
            }

            string typeParameter = parameters.Count >= 2 ? parameters[1] : "";
            string filter = parameters.Count == 3 ? parameters[2] : "";
            if (string.IsNullOrEmpty(typeParameter))
            {
                OutputUserXmlType(UserXmlDefinitionType.Item, filter, senderInfo);
                OutputUserXmlType(UserXmlDefinitionType.Block, filter, senderInfo);
                OutputUserXmlType(UserXmlDefinitionType.Recipe, filter, senderInfo);
                return;
            }

            UserXmlDefinitionType type;
            if (!TryGetUserXmlType(typeParameter, out type))
            {
                Output("XML definition type must be items, blocks, or recipes.", senderInfo);
                return;
            }

            OutputUserXmlType(type, filter, senderInfo);
        }

        private static bool TryGetUserXmlType(string value, out UserXmlDefinitionType type)
        {
            switch ((value ?? "").ToLowerInvariant())
            {
                case "item":
                case "items":
                    type = UserXmlDefinitionType.Item;
                    return true;
                case "block":
                case "blocks":
                    type = UserXmlDefinitionType.Block;
                    return true;
                case "recipe":
                case "recipes":
                    type = UserXmlDefinitionType.Recipe;
                    return true;
                default:
                    type = UserXmlDefinitionType.Item;
                    return false;
            }
        }

        private static void OutputUserXmlType(UserXmlDefinitionType type, string filter, CommandSenderInfo senderInfo)
        {
            List<UserXmlDefinitionSummary> definitions = UserXmlContentManager.GetDefinitions(type);
            StringBuilder report = new StringBuilder();
            int matchedCount = 0;
            int displayedCount = 0;
            foreach (UserXmlDefinitionSummary definition in definitions)
            {
                if (!definition.IsUserDefinition || !MatchesFilter(filter, definition.Name)) continue;

                matchedCount++;
                if (displayedCount >= MaximumListEntries) continue;

                displayedCount++;
                report.AppendLine("- " + definition.Name);
            }

            string suffix = matchedCount > displayedCount ? " (showing first " + displayedCount + ")" : "";
            Output("User XML " + type + " definitions: " + matchedCount + suffix +
                (displayedCount == 0 ? "" : "\n" + report.ToString().TrimEnd()), senderInfo);
        }

        private static void OutputPortal(CommandSenderInfo senderInfo)
        {
            Output("Mod Portal | status=" + ModPortalManager.Status +
                " | cached packages=" + ModPortalManager.GetPackages().Count, senderInfo);
        }

        private static void OpenWindow(string windowName, CommandSenderInfo senderInfo)
        {
            EntityPlayerLocal player;
            World world;
            if (!TryGetPlayerAndWorld(out player, out world, senderInfo)) return;
            if (player.playerUI == null || player.playerUI.windowManager == null)
            {
                Output("The local player UI is unavailable.", senderInfo);
                return;
            }

            player.playerUI.windowManager.Open(windowName, true);
            Output("Requested UI window '" + windowName + "'.", senderInfo);
        }

        private static bool TryGetPlayerAndWorld(out EntityPlayerLocal player, out World world, CommandSenderInfo senderInfo)
        {
            world = GameManager.Instance != null ? GameManager.Instance.World : null;
            player = world != null ? world.GetPrimaryPlayer() : null;
            if (player != null) return true;

            Output("Load a local save before running this operation.", senderInfo);
            return false;
        }

        private static bool TryParseFloat(string value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
        }

        private static bool MatchesFilter(string filter, params string[] values)
        {
            if (string.IsNullOrEmpty(filter)) return true;

            foreach (string value in values)
            {
                if (!string.IsNullOrEmpty(value) && value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        private static void Output(string message, CommandSenderInfo senderInfo)
        {
            Logger.Info("[LizziesGame] " + message);
            if (SdtdConsole.Instance != null)
            {
                SdtdConsole.Instance.Output(message, senderInfo);
            }
        }
    }

    internal static class GameSpawnAutomation
    {
        private static bool spawnQueued;
        private static bool spawnDialogReady;
        private static XUiC_SpawnSelectionWindow spawnSelectionWindow;

        internal static bool IsSpawnQueued
        {
            get { return spawnQueued; }
        }

        internal static bool IsSpawnReady
        {
            get { return spawnDialogReady; }
        }

        internal static void RequestSpawn()
        {
            spawnQueued = true;
            ProcessQueuedSpawnAtDialog();
        }

        internal static void ProcessQueuedSpawnAtDialog()
        {
            if (!spawnQueued || !spawnDialogReady || spawnSelectionWindow == null) return;

            spawnQueued = false;
            spawnDialogReady = false;
            XUiC_SpawnSelectionWindow window = spawnSelectionWindow;
            spawnSelectionWindow = null;
            window.SpawnButtonPressed(SpawnMethod.Invalid, -1);
            Logger.Info("[GameSpawn] Invoked the native Spawn button from a queued developer request.");
        }

        internal static void NotifySpawnDialogShown(XUiC_SpawnSelectionWindow window)
        {
            if (window == null) return;

            spawnSelectionWindow = window;
            spawnDialogReady = true;
            ProcessQueuedSpawnAtDialog();
        }
    }

    [HarmonyPatch(typeof(XUiC_SpawnSelectionWindow), "showSpawningComponents")]
    public class XUiC_SpawnSelectionWindow_QueuedSpawnPatch
    {
        public static void Postfix(
            XUiC_SpawnSelectionWindow __instance,
            [HarmonyArgument(0)] XUiC_SpawnSelectionWindow.ESpawnWindowMode windowMode)
        {
            if (windowMode == XUiC_SpawnSelectionWindow.ESpawnWindowMode.SpawnSelection)
            {
                GameSpawnAutomation.NotifySpawnDialogShown(__instance);
            }
        }
    }
}