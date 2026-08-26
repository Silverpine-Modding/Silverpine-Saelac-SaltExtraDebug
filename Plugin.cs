using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaltExtraDebug;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "salt.silverpine.extradebug";
    public const string PluginName = "Salt Extra Debug";
    public const string PluginVersion = "0.22.0";

    internal static ConfigEntry<bool> FreeBuildEnabled;
    internal static ConfigEntry<bool> GodModeEnabled;
    internal static ConfigEntry<bool> StatChangesLocked;
    internal static ConfigEntry<bool> NoGoldDecreaseEnabled;
    internal static ConfigEntry<bool> NoDurabilityDecreaseEnabled;
    internal static ConfigEntry<bool> InfiniteEnergyEnabled;
    internal static ConfigEntry<bool> BlockNegativeStatusEffects;
    internal static ConfigEntry<bool> AllowNpcTalkInCombat;
    private Harmony _harmony;

    private void Awake()
    {
        FreeBuildEnabled = Config.Bind(
            "Free Build",
            "Enabled",
            false,
            "Enables the Free Build gameplay patches."
        );
        GodModeEnabled = Config.Bind(
            "Cheats",
            "GodMode",
            false,
            "Prevents positive damage to the player while allowing healing."
        );
        StatChangesLocked = Config.Bind(
            "Cheats",
            "LockNegativeStatChanges",
            false,
            "Prevents hunger, thirst, sanity, and rest from getting worse."
        );
        NoGoldDecreaseEnabled = Config.Bind(
            "Cheats",
            "NoGoldDecrease",
            false,
            "Prevents the player's gold from decreasing while allowing gains."
        );
        NoDurabilityDecreaseEnabled = Config.Bind(
            "Cheats",
            "NoDurabilityDecrease",
            false,
            "Prevents item durability from decreasing while allowing repairs."
        );
        InfiniteEnergyEnabled = Config.Bind(
            "Cheats",
            "InfiniteEnergy",
            false,
            "Prevents the player's energy from decreasing while allowing recovery."
        );
        BlockNegativeStatusEffects = Config.Bind(
            "Cheats",
            "BlockNegativeStatusEffects",
            false,
            "Prevents harmful status effects from being applied to the player."
        );
        AllowNpcTalkInCombat = Config.Bind(
            "Cheats",
            "AllowNpcTalkInCombat",
            false,
            "Allows the player to start NPC dialogue while in combat."
        );

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();
    }
}

[HarmonyPatch]
internal static class DebugSpawnPrefabDeconstructablePatch
{
    private static MethodBase TargetMethod()
    {
        Type spawnAction = AccessTools.Inner(
            typeof(DebugUI),
            "DebugAction_SpawnPrefab"
        );

        MethodInfo target = spawnAction
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Prepend(spawnAction)
            .SelectMany(type => type.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic
            ))
            .FirstOrDefault(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return method.ReturnType == typeof(void)
                    && parameters.Length == 2
                    && parameters[0].ParameterType == typeof(int)
                    && parameters[1].ParameterType == typeof(Vector2);
            });

        return target ?? throw new MissingMethodException(
            "Could not find the Debug Spawn Prefab placement callback."
        );
    }

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo markSpawned = AccessTools.Method(
            typeof(DebugSpawnPrefabDeconstructablePatch),
            nameof(MarkSpawned)
        );

        foreach (CodeInstruction instruction in instructions)
        {
            yield return instruction;

            if (instruction.opcode == OpCodes.Call
                && instruction.operand is MethodInfo method
                && method.Name == nameof(UnityEngine.Object.Instantiate)
                && method.ReturnType == typeof(GameObject))
            {
                yield return new CodeInstruction(
                    OpCodes.Call,
                    markSpawned
                );
            }
        }
    }

    private static GameObject MarkSpawned(GameObject spawned)
    {
        if (spawned == null)
        {
            return spawned;
        }

        Deconstructable deconstructable =
            spawned.GetComponent<Deconstructable>();

        if (deconstructable == null)
        {
            deconstructable = spawned.AddComponent<Deconstructable>();
            Traverse.Create(deconstructable)
                .Field("interactable")
                .SetValue(true);
            Traverse.Create(deconstructable)
                .Field("toDrop")
                .SetValue(new List<ItemNameAmountPair>());
        }

        deconstructable.playerPropertyOnly = false;
        return spawned;
    }
}

[HarmonyPatch(
    typeof(NeuralNPC),
    nameof(NeuralNPC.OnInteractInnerInvoke))]
internal static class AllowNpcTalkInCombatPatch
{
    [HarmonyPriority(Priority.First + 100)]
    private static void Prefix(out bool __state)
    {
        __state = false;
        if (Plugin.AllowNpcTalkInCombat.Value
            && Player.Instance != null
            && Player.Instance.inCombat)
        {
            __state = true;
            Player.Instance.inCombat = false;
        }
    }

    private static void Postfix(bool __state)
    {
        RestoreCombatState(__state);
    }

    private static Exception Finalizer(
        Exception __exception,
        bool __state)
    {
        RestoreCombatState(__state);
        return __exception;
    }

    private static void RestoreCombatState(bool wasInCombat)
    {
        if (wasInCombat && Player.Instance != null)
        {
            Player.Instance.inCombat = true;
        }
    }
}

[HarmonyPatch(typeof(PlayerAbility_Deconstruct), "OnUse")]
internal static class FreeBuildDeconstructPatch
{
    private static bool Prefix()
    {
        if (!Plugin.FreeBuildEnabled.Value)
        {
            return true;
        }

        PauseMenuManager.Instance.Close();

        Predicate<GameObject> canDeconstruct =
            gameObject => gameObject.TryGetComponent<Deconstructable>(out _);

        Targetable.StartTargetingMode(
            gameObject =>
                gameObject.transform.GetVector2IntPosition().IsAdjacent3x3(
                    Player.Instance.transform.GetVector2IntPosition()
                )
                && canDeconstruct(gameObject),
            delegate(Targetable target)
            {
                if (target == null)
                {
                    UpperNotificationUI.Instance.OneOff(
                        "There's nothing to deconstruct here."
                    );
                    return;
                }

                Turfs.TryGetTurfGameObjects(
                    target.transform.GetVector2IntPosition(),
                    out var objects
                );
                List<GameObject> choices = objects
                    .Where(gameObject => canDeconstruct(gameObject))
                    .ToList();

                if (choices.Count == 0)
                {
                    UpperNotificationUI.Instance.OneOff(
                        "There's nothing to deconstruct here."
                    );
                }
                else if (choices.Count > 1)
                {
                    RadialMenuUI.Instance.OpenAndDraw(
                        choices.Select(gameObject =>
                            new ListUIItem_Generic(
                                gameObject
                                    .GetComponent<NPCVisibleObject>()
                                    .GetSimplified()
                                    .ToUpperWords(),
                                ListUIItem_Generic.GameObjectToIcon(gameObject),
                                "",
                                delegate
                                {
                                    gameObject
                                        .GetComponent<Deconstructable>()
                                        .Deconstruct(true);
                                }
                            )
                        ).ToList()
                    );
                }
                else
                {
                    choices[0]
                        .GetComponent<Deconstructable>()
                        .Deconstruct(true);
                }
            },
            autoSelectSolitary: true
        );

        return false;
    }
}

internal static class FreeBuildHarvesting
{
    private static readonly HashSet<Type> HarvestingCallers = new()
    {
        typeof(Tree),
        typeof(Grass),
        typeof(ResourceNode),
        typeof(QuarryResourceNode),
        typeof(ResinWall)
    };

    private static readonly HashSet<Type> InstantCallers = new()
    {
        typeof(Tree),
        typeof(ResourceNode),
        typeof(QuarryResourceNode),
        typeof(ResinWall),
        typeof(Deconstructable),
        typeof(ConstructionSite)
    };

    internal static bool IsHarvestingCall()
    {
        return HasCaller(HarvestingCallers);
    }

    internal static bool IsInstantInteractionCall()
    {
        return HasCaller(InstantCallers);
    }

    private static bool HasCaller(HashSet<Type> allowedTypes)
    {
        var trace = new System.Diagnostics.StackTrace();
        for (int i = 0; i < trace.FrameCount; i++)
        {
            Type declaringType = trace.GetFrame(i)
                ?.GetMethod()
                ?.DeclaringType;

            while (declaringType != null)
            {
                if (allowedTypes.Contains(declaringType))
                {
                    return true;
                }

                declaringType = declaringType.DeclaringType;
            }
        }

        return false;
    }

    internal static Item MakeVirtualTool(Item template)
    {
        Item tool = ItemLibrary.GetItemByName(template.name, logInvalid: false);

        if (tool.TryGetItemComponent<ItemComponent_HarvestingTool>(
            out var harvestingTool))
        {
            harvestingTool.Quality = Quality.Legendary;
        }

        if (tool.TryGetItemComponent<ItemComponent_Durability>(
            out var durability))
        {
            durability.maxDurability = 1000000f;
            durability.Durability = durability.maxDurability;
        }

        return tool;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.GetItemWithPredicate))]
internal static class FreeBuildSingleToolPatch
{
    private static void Postfix(
        Inventory __instance,
        Predicate<Item> predicate,
        ref Item __result)
    {
        if (__result != null
            || !Plugin.FreeBuildEnabled.Value
            || Player.Instance == null
            || __instance != Player.Instance.worldInventory.inventory
            || !FreeBuildHarvesting.IsHarvestingCall())
        {
            return;
        }

        Item template = ItemLibrary.Items.FirstOrDefault(
            item => predicate(item)
        );
        if (template != null)
        {
            __result = FreeBuildHarvesting.MakeVirtualTool(template);
        }
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.GetItemsWithPredicate))]
internal static class FreeBuildToolListPatch
{
    private static void Postfix(
        Inventory __instance,
        Predicate<Item> predicate,
        ref List<Item> __result)
    {
        if (__result.Count != 0
            || !Plugin.FreeBuildEnabled.Value
            || Player.Instance == null
            || __instance != Player.Instance.worldInventory.inventory
            || !FreeBuildHarvesting.IsHarvestingCall())
        {
            return;
        }

        Item template = ItemLibrary.Items.FirstOrDefault(
            item => predicate(item)
        );
        if (template != null)
        {
            __result.Add(FreeBuildHarvesting.MakeVirtualTool(template));
        }
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.StartLongInteraction))]
internal static class FreeBuildInstantInteractionPatch
{
    private static bool Prefix(
        Action successCallback,
        Action finallyCallback)
    {
        if (!Plugin.FreeBuildEnabled.Value
            || !FreeBuildHarvesting.IsInstantInteractionCall())
        {
            return true;
        }

        successCallback?.Invoke();
        finallyCallback?.Invoke();
        return false;
    }
}

[HarmonyPatch(
    typeof(StatusEffectsTarget),
    nameof(StatusEffectsTarget.AddStatusEffect))]
internal static class BlockNegativeStatusEffectsPatch
{
    private static readonly HashSet<Type> HarmfulEffects = new()
    {
        typeof(StatusEffect_Burning),
        typeof(StatusEffect_Cold),
        typeof(StatusEffect_Confused),
        typeof(StatusEffect_Encumbered),
        typeof(StatusEffect_FrostPoison),
        typeof(StatusEffect_Hungry),
        typeof(StatusEffect_Insane),
        typeof(StatusEffect_Poison),
        typeof(StatusEffect_Slowed),
        typeof(StatusEffect_Soaked),
        typeof(StatusEffect_Stunned),
        typeof(StatusEffect_Thirsty),
        typeof(StatusEffect_Tired)
    };

    private static bool Prefix(
        StatusEffectsTarget __instance,
        StatusEffect newStatusEffect)
    {
        if (!Plugin.BlockNegativeStatusEffects.Value
            || Player.Instance == null
            || __instance != Player.Instance.statusEffectsTarget
            || newStatusEffect == null)
        {
            return true;
        }

        return !HarmfulEffects.Contains(newStatusEffect.GetType());
    }
}

[HarmonyPatch(
    typeof(Player.PlayerResource),
    nameof(Player.PlayerResource.Value),
    MethodType.Setter)]
internal static class InfiniteEnergyPatch
{
    private static void Prefix(Player.PlayerResource __instance, ref int value)
    {
        if (Plugin.InfiniteEnergyEnabled.Value
            && Player.Instance != null
            && __instance == Player.Instance.energy
            && value < __instance.Value)
        {
            value = __instance.Value;
        }
    }
}

[HarmonyPatch(
    typeof(ItemComponent_Durability),
    nameof(ItemComponent_Durability.Durability),
    MethodType.Setter)]
internal static class NoDurabilityDecreasePatch
{
    private static void Prefix(
        ItemComponent_Durability __instance,
        ref float value)
    {
        if (Plugin.NoDurabilityDecreaseEnabled.Value
            && value < __instance.Durability)
        {
            value = __instance.Durability;
        }
    }
}

[HarmonyPatch]
internal static class NoGoldDecreasePatch
{
    private static readonly FieldInfo GoldField = AccessTools.Field(
        typeof(Inventory),
        nameof(Inventory.gold)
    );

    private static IEnumerable<MethodBase> TargetMethods()
    {
        int goldToken = GoldField.MetadataToken;

        foreach (Type type in typeof(Inventory).Assembly.GetTypes())
        {
            MethodInfo[] methods;
            try
            {
                methods = type.GetMethods(
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly
                );
            }
            catch
            {
                continue;
            }

            foreach (MethodInfo method in methods)
            {
                if (ContainsGoldStore(method, goldToken))
                {
                    yield return method;
                }
            }
        }
    }

    private static bool ContainsGoldStore(MethodInfo method, int goldToken)
    {
        try
        {
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null || il.Length < 5)
            {
                return false;
            }

            byte token0 = (byte)goldToken;
            byte token1 = (byte)(goldToken >> 8);
            byte token2 = (byte)(goldToken >> 16);
            byte token3 = (byte)(goldToken >> 24);

            for (int i = 0; i <= il.Length - 5; i++)
            {
                // 0x7D is the single-byte IL opcode for stfld.
                if (il[i] == 0x7D
                    && il[i + 1] == token0
                    && il[i + 2] == token1
                    && il[i + 3] == token2
                    && il[i + 4] == token3)
                {
                    return true;
                }
            }
        }
        catch
        {
            // Some runtime-generated methods do not expose a readable body.
        }

        return false;
    }

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo guardedStore = AccessTools.Method(
            typeof(NoGoldDecreasePatch),
            nameof(SetGold)
        );

        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Stfld
                && instruction.operand is FieldInfo field
                && field == GoldField)
            {
                var replacement = new CodeInstruction(
                    OpCodes.Call,
                    guardedStore
                );
                replacement.labels.AddRange(instruction.labels);
                replacement.blocks.AddRange(instruction.blocks);
                yield return replacement;
            }
            else
            {
                yield return instruction;
            }
        }
    }

    private static void SetGold(Inventory inventory, int newValue)
    {
        bool isPlayerInventory =
            Player.Instance != null
            && Player.Instance.worldInventory != null
            && Player.Instance.worldInventory.inventory == inventory;

        if (Plugin.NoGoldDecreaseEnabled.Value
            && isPlayerInventory
            && newValue < inventory.gold)
        {
            return;
        }

        inventory.gold = newValue;
    }
}

[HarmonyPatch]
internal static class GodModePatch
{
    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(Damagable), "AnimateDamage");
    }

    private static bool Prefix(
        Damagable __instance,
        Damage damage,
        ref System.Collections.IEnumerator __result)
    {
        if (!Plugin.GodModeEnabled.Value
            || Player.Instance == null
            || __instance != Player.Instance.damagable
            || damage.amount <= 0)
        {
            return true;
        }

        __result = NoDamage();
        return false;
    }

    private static System.Collections.IEnumerator NoDamage()
    {
        yield break;
    }
}

[HarmonyPatch(
    typeof(Player.PlayerResource),
    nameof(Player.PlayerResource.Value),
    MethodType.Setter)]
internal static class StatChangesSetterPatch
{
    private static void Prefix(
        Player.PlayerResource __instance,
        ref int value)
    {
        if (!Plugin.StatChangesLocked.Value || Player.Instance == null)
        {
            return;
        }

        int current = __instance.Value;

        if ((__instance == Player.Instance.hunger
                || __instance == Player.Instance.thirst)
            && value > current)
        {
            value = current;
        }

        if ((__instance == Player.Instance.sanity
                || __instance == Player.Instance.rest)
            && value < current)
        {
            value = current;
        }
    }
}

[HarmonyPatch(
    typeof(Player.PlayerResource),
    nameof(Player.PlayerResource.ChangeQuiet))]
internal static class StatChangesQuietPatch
{
    private static void Prefix(
        Player.PlayerResource __instance,
        ref int change)
    {
        if (!Plugin.StatChangesLocked.Value || Player.Instance == null)
        {
            return;
        }

        if ((__instance == Player.Instance.hunger
                || __instance == Player.Instance.thirst)
            && change > 0)
        {
            change = 0;
        }

        if ((__instance == Player.Instance.sanity
                || __instance == Player.Instance.rest)
            && change < 0)
        {
            change = 0;
        }
    }
}

[HarmonyPatch(typeof(ConstructionSite), nameof(ConstructionSite.OnInteract))]
internal static class ConstructionSiteHammerPatch
{
    private static bool Prefix(ConstructionSite __instance)
    {
        if (!Plugin.FreeBuildEnabled.Value)
        {
            return true;
        }

        List<ItemNameAmountPair> requiredItems = __instance.requiredItems;
        Inventory siteInventory = Traverse.Create(__instance)
            .Field("inventory")
            .GetValue<Inventory>();

        if (requiredItems.Count > 0)
        {
            DialogBox.Instance.DisplayTextNoDialog(
                "Missing materials: " +
                string.Join(", ", requiredItems.Select(
                    i => $"<b>{i.itemName}</b> x <color=yellow>{i.amount}</color>"
                )),
                new DialogOption("Deposit Materials", delegate
                {
                    foreach (ItemNameAmountPair requiredItem in requiredItems)
                    {
                        int amount = requiredItem.amount;
                        for (int i = 0; i < amount; i++)
                        {
                            Item item = Player.Instance.worldInventory.inventory
                                .GetItemByName(requiredItem.itemName);
                            if (item == null)
                            {
                                break;
                            }

                            requiredItem.amount--;
                            item.RemoveItemFromInventory();
                            siteInventory.TryAddItem(item);
                        }
                    }

                    foreach (ItemNameAmountPair requiredItem in requiredItems.ToArray())
                    {
                        if (requiredItem.amount == 0)
                        {
                            requiredItems.Remove(requiredItem);
                        }
                    }
                }),
                new DialogOption("Leave", null)
            );
        }
        else
        {
            MethodInfo onPostProcessTurnMethod = AccessTools.Method(
                typeof(ConstructionSite),
                "OnPostProcessTurn"
            );
            Action onPostProcessTurn =
                (Action)Delegate.CreateDelegate(
                    typeof(Action),
                    __instance,
                    onPostProcessTurnMethod
                );

            ActionQueue.Instance.OnPostProcessTurn += onPostProcessTurn;
            AudioPlayer.Instance.PlayLoopAtPosition(
                "sound_loop_hammering",
                0.5f,
                Player.Instance.transform.GetVector2IntPosition()
            );
            Player.Instance.StartLongInteraction(
                delegate
                {
                    __instance.FinishConstruction(null);
                },
                __instance.workTurns,
                null,
                delegate
                {
                    AudioPlayer.Instance.StopLoop(
                        Player.Instance.transform.GetVector2IntPosition()
                    );
                    ActionQueue.Instance.OnPostProcessTurn -= onPostProcessTurn;
                }
            );
        }

        // The Free Build path above is the original method minus its hammer
        // lookup and rejection. OFF returns true and runs the untouched game method.
        return false;
    }
}

[HarmonyPatch]
internal static class StartPlacementModePatch
{
    private static MethodBase TargetMethod()
    {
        var constructionItemType = AccessTools.Inner(
            typeof(PlayerAbility_Construct),
            "ConstructionItem"
        );

        return AccessTools.Method(
            constructionItemType,
            "StartPlacementMode"
        );
    }

    private static void Prefix(object __instance)
    {
        if (!Plugin.FreeBuildEnabled.Value)
        {
            return;
        }

        Traverse.Create(__instance)
            .Field("playerPropertyOnly")
            .SetValue(false);
        Traverse.Create(__instance)
            .Field("requiresHammer")
            .SetValue(false);
        Traverse.Create(__instance)
            .Field("requiredItems")
            .SetValue(new List<ItemNameAmountPair>());
        Traverse.Create(__instance)
            .Field("workMinutes")
            .SetValue(0);
        Traverse.Create(__instance)
            .Field("positionZoneName")
            .SetValue("Soup");
    }

}

[HarmonyPatch]
internal static class PlacementZoneNamePatch
{
    private static MethodBase TargetMethod()
    {
        Type constructionItemType = AccessTools.Inner(
            typeof(PlayerAbility_Construct),
            "ConstructionItem"
        );

        IEnumerable<Type> types =
            new[] { constructionItemType }
                .Concat(constructionItemType.GetNestedTypes(
                    BindingFlags.Public | BindingFlags.NonPublic
                ));

        MethodInfo target = types
            .SelectMany(type => type.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic
            ))
            .FirstOrDefault(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();

                return method.Name.Contains("StartPlacementMode")
                    && method.ReturnType == typeof(bool)
                    && parameters.Length == 2
                    && parameters[0].ParameterType == typeof(Vector2)
                    && parameters[1].ParameterType == typeof(bool);
            });

        if (target == null)
        {
            throw new MissingMethodException(
                "Could not find the StartPlacementMode placement-validation function."
            );
        }

        return target;
    }

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo getPositionZoneName = AccessTools.Method(
            typeof(MapZone),
            nameof(MapZone.GetPositionZoneName),
            new[] { typeof(Vector2Int) }
        );

        MethodInfo modifyZoneName = AccessTools.Method(
            typeof(PlacementZoneNamePatch),
            nameof(ModifyZoneName)
        );

        foreach (CodeInstruction instruction in instructions)
        {
            yield return instruction;

            if (instruction.Calls(getPositionZoneName))
            {
                yield return new CodeInstruction(
                    OpCodes.Call,
                    modifyZoneName
                );
            }
        }

    }

    private static string ModifyZoneName(string originalZoneName)
    {
        if (!Plugin.FreeBuildEnabled.Value)
        {
            return originalZoneName;
        }

        string modifiedZoneName = "Outside";

        return modifiedZoneName;
    }
}

[HarmonyPatch(typeof(DebugUI), nameof(DebugUI.ShowDebugOptions))]
internal static class AddCheatButtonsPatch
{
    internal static GameObject CloseButton;

    private static void Postfix()
    {
        if (GenericListUI.Instance == null)
        {
            return;
        }

        GameObject contentRoot = Traverse.Create(GenericListUI.Instance)
            .Field("instanceRoot")
            .GetValue<GameObject>();

        if (contentRoot == null)
        {
            return;
        }

        AddToggle(contentRoot, "Free Build", Plugin.FreeBuildEnabled);
        AddToggle(contentRoot, "God Mode", Plugin.GodModeEnabled);
        AddToggle(contentRoot, "Toggle Stat Changes", Plugin.StatChangesLocked);
        AddToggle(contentRoot, "NoGoldDecrease", Plugin.NoGoldDecreaseEnabled);
        AddToggle(
            contentRoot,
            "NoDurabilityDecrease",
            Plugin.NoDurabilityDecreaseEnabled
        );
        AddToggle(
            contentRoot,
            "Infinite Energy",
            Plugin.InfiniteEnergyEnabled
        );
        AddToggle(
            contentRoot,
            "Block Negative Status Effects",
            Plugin.BlockNegativeStatusEffects
        );
        AddToggle(
            contentRoot,
            "Allow NPC Talk In Combat",
            Plugin.AllowNpcTalkInCombat
        );
        AddAction(
            contentRoot,
            "Force NPC Daily Reset",
            ForceNpcDailyReset
        );
        AddAction(
            contentRoot,
            "Duplicate Inventory Item",
            StartDuplicateInventoryItem
        );
        AddCloseButton(contentRoot);
    }

    private static void AddToggle(
        GameObject contentRoot,
        string name,
        ConfigEntry<bool> setting)
    {
        var item = new ListUIItem_Generic(
            name,
            null,
            setting.Value
                ? "<color=#70e070>ON</color>"
                : "<color=#e07070>OFF</color>",
            delegate
            {
                setting.Value = !setting.Value;
                setting.ConfigFile.Save();
                GenericListUI.Instance.Close();
                DebugUI.Instance.ShowDebugOptions();
            });

        GameObject row = UnityEngine.Object.Instantiate(
            Resources.Load<GameObject>(
                "MiscPrefabs/ListUI/" + item.PrefabName
            ),
            contentRoot.transform,
            false
        );

        item.instantiated = row;
        item.HandleInstantiated(row);
    }

    private static void AddAction(
        GameObject contentRoot,
        string name,
        Action callback)
    {
        var item = new ListUIItem_Generic(
            name,
            null,
            "",
            delegate
            {
                callback();
                GenericListUI.Instance.Close();
            }
        );

        GameObject row = UnityEngine.Object.Instantiate(
            Resources.Load<GameObject>(
                "MiscPrefabs/ListUI/" + item.PrefabName
            ),
            contentRoot.transform,
            false
        );

        item.instantiated = row;
        item.HandleInstantiated(row);
    }

    private static void ForceNpcDailyReset()
    {
        foreach (NeuralNPC npc in NeuralNPC.neuralNPCs.Values.ToArray())
        {
            if (npc != null)
            {
                // NeuralNPC.OnNewDay does not use its integer argument.
                npc.OnNewDay(0);
            }
        }
    }

    private static void StartDuplicateInventoryItem()
    {
        if (Player.Instance == null
            || Player.Instance.worldInventory == null
            || InventoryUI.Instance == null)
        {
            return;
        }

        Inventory inventory = Player.Instance.worldInventory.inventory;
        if (inventory == null || inventory.GetItemCount() == 0)
        {
            UpperNotificationUI.Instance.OneOff(
                "There are no inventory items to duplicate."
            );
            return;
        }

        InventoryUI.Instance.StartSelectionMode(
            item => item != null,
            delegate(Item selectedItem)
            {
                if (selectedItem == null)
                {
                    return;
                }

                Item duplicate = selectedItem.DeepClone();
                bool originalAllowEncumbered = inventory.allowEncumbered;
                int originalMaxItems = inventory.maxItems;

                try
                {
                    // A debug duplication action should not silently fail just
                    // because the inventory is already at its normal limit.
                    inventory.allowEncumbered = true;
                    inventory.maxItems = int.MaxValue;

                    if (!inventory.TryAddItem(duplicate))
                    {
                        UpperNotificationUI.Instance.OneOff(
                            "Could not duplicate "
                            + selectedItem.GetFinalName() + "."
                        );
                        return;
                    }
                }
                finally
                {
                    inventory.allowEncumbered = originalAllowEncumbered;
                    inventory.maxItems = originalMaxItems;
                    inventory.UpdateEncumbered();
                }

                AnimationHelper.Instance.SpawnItemFloatingText(
                    duplicate,
                    Player.Instance.transform
                );
                UpperNotificationUI.Instance.OneOff(
                    "Duplicated " + duplicate.GetFinalName() + "."
                );
            }
        );
    }

    private static void AddCloseButton(GameObject contentRoot)
    {
        if (CloseButton != null)
        {
            UnityEngine.Object.DestroyImmediate(CloseButton);
        }

        var closeObject = new GameObject(
            "SaltExtraDebug_CloseButton",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button)
        );
        CloseButton = closeObject;

        // instanceRoot is the scrolling content. Its parent is the fixed-size
        // viewport that forms the visible debug window.
        closeObject.transform.SetParent(contentRoot.transform.parent, false);
        closeObject.transform.SetAsLastSibling();

        RectTransform rect = closeObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-8f, -8f);
        rect.sizeDelta = new Vector2(44f, 44f);

        Image background = closeObject.GetComponent<Image>();
        background.color = new Color(0.12f, 0.12f, 0.12f, 0.9f);

        var textObject = new GameObject(
            "Label",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI)
        );
        textObject.transform.SetParent(closeObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        label.text = "X";
        label.fontSize = 26f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;

        TextMeshProUGUI existingText =
            contentRoot.GetComponentInChildren<TextMeshProUGUI>();
        if (existingText != null)
        {
            label.font = existingText.font;
        }

        closeObject.GetComponent<Button>().onClick.AddListener(delegate
        {
            GenericListUI.Instance.Close();
            if (CloseButton != null)
            {
                UnityEngine.Object.Destroy(CloseButton);
                CloseButton = null;
            }
        });
    }
}

[HarmonyPatch(typeof(DebugUI), "Awake")]
internal static class DebugUIAwakePatch
{
    private static bool Prefix(DebugUI __instance)
    {
        DebugUI.Instance = __instance;

        foreach (Transform child in __instance.transform)
        {
            child.gameObject.SetActive(false);
        }

        // Returning false prevents the original Awake from disabling DebugUI.
        return false;
    }

}

[HarmonyPatch(typeof(DialogBox), "DrawUpperButtons")]
internal static class DialogBoxDrawUpperButtonsPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo debugBuildGetter = AccessTools.PropertyGetter(
            typeof(Debug),
            nameof(Debug.isDebugBuild)
        );

        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(debugBuildGetter))
            {
                // Replace Debug.isDebugBuild with the constant true.
                var replacement = new CodeInstruction(OpCodes.Ldc_I4_1);
                replacement.labels.AddRange(instruction.labels);
                replacement.blocks.AddRange(instruction.blocks);

                yield return replacement;
            }
            else
            {
                yield return instruction;
            }
        }

    }
}
