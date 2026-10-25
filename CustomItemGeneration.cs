// Migrated from the Manual Custom Item Generator. Keep this namespace and
// both component type names unchanged: existing saves store their FullName.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SilverpineMods.CustomItemFreezeFix
{
    [HarmonyPatch]
    public static class DebugGenerateCustomItemPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type actionBaseType = AccessTools.Inner(typeof(DebugUI), "DebugAction");
            yield return AccessTools.Method(actionBaseType, "Call");
            // Also cover direct callers of the method formerly rewritten by
            // the preloader. The outer Call prefix skips its body, so a normal
            // click still opens exactly one wizard.
            Type generatorType = AccessTools.Inner(typeof(DebugUI), "DebugAction_GenerateCustomItem");
            yield return AccessTools.DeclaredMethod(generatorType, "OnCalled");
        }

        private static bool Prefix(object __instance)
        {
            if (__instance == null ||
                __instance.GetType().Name != "DebugAction_GenerateCustomItem")
            {
                return true;
            }

            OpenPrompt();
            return false;
        }

        // The native debug action is intercepted at runtime; no preloader is required.
        public static void OpenPrompt()
        {
            GenericListUI.Instance.Close();
            ManualItemWizard.Start();
        }
    }

    internal static class ManualItemWizard
    {
        private static Item _selectedAppearanceItem;

        private static Sprite ListIcon =>
            Resources.Load<Sprite>("Sprites/StatusEffect/sprite_statuseffect_interacting");

        public static void Start()
        {
            _selectedAppearanceItem = null;
            OpenTextInput(
                "Enter the custom item's name",
                customName => OpenCategoryPicker(customName.Trim()));
        }

        private static void OpenCategoryPicker(string customName)
        {
            var entries = Enum.GetValues(typeof(ItemCategory))
                .Cast<ItemCategory>()
                .Select(category => new ListUIItem_Generic(
                    category.ToString(),
                    ListIcon,
                    "",
                    () =>
                    {
                        GenericListUI.Instance.Close();
                        RunNextFrame(() => OpenBaseItemPicker(customName, category));
                    }))
                .ToList();

            SaltExtraDebug.DebugMenuScreens.Draw(GenericListUI.Instance, entries, "custom-item:category");
            GenericListUI.Instance.Open();
        }

        private static void OpenBaseItemPicker(string customName, ItemCategory category)
        {
            var candidates = ItemLibrary.Items
                .Where(item => item.itemCategory == category)
                .GroupBy(item => item.name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.name)
                .ToList();

            if (candidates.Count == 0)
            {
                ShowError($"No base items exist in the {category} category.");
                return;
            }

            var entries = candidates
                .Select(baseItem => new ListUIItem_Generic(
                    baseItem.name,
                    baseItem.Sprite ?? ListIcon,
                    "Clone",
                    () =>
                    {
                        GenericListUI.Instance.Close();
                        RunNextFrame(
                            () => OpenAppearancePicker(customName, category, baseItem));
                    }))
                .ToList();

            SaltExtraDebug.DebugMenuScreens.Draw(GenericListUI.Instance, entries, "custom-item:base:" + category);
            GenericListUI.Instance.Open();
        }

        private static void OpenAppearancePicker(
            string customName,
            ItemCategory category,
            Item baseItem)
        {
            var entries = ItemLibrary.Items
                .GroupBy(item => item.itemCategory)
                .OrderBy(group => group.Key.ToString())
                .Select(group => new ListUIItem_Generic(
                    group.Key.ToString(),
                    ListIcon,
                    $"{group.Select(item => item.name).Distinct().Count()} models",
                    () =>
                    {
                        GenericListUI.Instance.Close();
                        RunNextFrame(
                            () => OpenAppearanceItemPicker(
                                customName,
                                category,
                                baseItem,
                                group.Key));
                    }))
                .ToList();

            entries.Insert(
                0,
                new ListUIItem_Generic(
                    "MODEL SELECTION",
                    ListIcon,
                    "Choose an inventory model category",
                    null));

            SaltExtraDebug.DebugMenuScreens.Draw(GenericListUI.Instance, entries, "custom-item:appearance-category");
            GenericListUI.Instance.Open();
        }

        private static void OpenAppearanceItemPicker(
            string customName,
            ItemCategory category,
            Item baseItem,
            ItemCategory appearanceCategory)
        {
            var entries = ItemLibrary.Items
                .Where(item => item.itemCategory == appearanceCategory)
                .GroupBy(item => item.name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.name)
                .Select(appearanceItem => new ListUIItem_Generic(
                    appearanceItem.name,
                    appearanceItem.Sprite ?? ListIcon,
                    appearanceItem.name.Equals(
                        baseItem.name,
                        StringComparison.OrdinalIgnoreCase)
                        ? "Current appearance"
                        : "Use appearance",
                    () =>
                    {
                        _selectedAppearanceItem = appearanceItem;
                        GenericListUI.Instance.Close();
                        RunNextFrame(
                            () => OpenDescriptionInput(
                                customName,
                                category,
                                baseItem));
                    }))
                .ToList();

            entries.Insert(
                0,
                new ListUIItem_Generic(
                    "MODEL SELECTION",
                    ListIcon,
                    $"{appearanceCategory} inventory models",
                    null));

            SaltExtraDebug.DebugMenuScreens.Draw(GenericListUI.Instance, entries, "custom-item:appearance:" + appearanceCategory);
            GenericListUI.Instance.Open();
        }

        private static void OpenDescriptionInput(
            string customName,
            ItemCategory category,
            Item baseItem)
        {
            OpenTextInput(
                "Enter the custom item's description",
                description => OpenQualityPicker(
                    customName,
                    category,
                    baseItem,
                    description.Trim()));
        }

        private static void OpenQualityPicker(
            string customName,
            ItemCategory category,
            Item baseItem,
            string description)
        {
            var entries = Enum.GetValues(typeof(Quality))
                .Cast<Quality>()
                .Select(quality => new ListUIItem_Generic(
                    quality.ToString(),
                    ListIcon,
                    $"{(int)quality} star{((int)quality == 1 ? "" : "s")}",
                    () =>
                    {
                        GenericListUI.Instance.Close();
                        RunNextFrame(
                            () => ContinueAfterQuality(
                                customName,
                                category,
                                baseItem,
                                description,
                                quality));
                    }))
                .ToList();

            SaltExtraDebug.DebugMenuScreens.Draw(GenericListUI.Instance, entries, "custom-item:quality");
            GenericListUI.Instance.Open();
        }

        private static void ContinueAfterQuality(
            string customName,
            ItemCategory category,
            Item baseItem,
            string description,
            Quality quality)
        {
            if (ItemComponent_Enchantment.IsEnchantable(baseItem))
            {
                OpenEnchantmentPicker(
                    customName,
                    category,
                    baseItem,
                    description,
                    quality);
                return;
            }

            ContinueAfterEnchantment(
                customName,
                category,
                baseItem,
                description,
                quality,
                null);
        }

        private static void OpenEnchantmentPicker(
            string customName,
            ItemCategory category,
            Item baseItem,
            string description,
            Quality quality)
        {
            var entries = Enum.GetValues(typeof(ItemComponent_Enchantment.EnchantmentType))
                .Cast<ItemComponent_Enchantment.EnchantmentType>()
                .Select(enchantment => new ListUIItem_Generic(
                    enchantment.ToString(),
                    ListIcon,
                    "Enchantment",
                    () =>
                    {
                        GenericListUI.Instance.Close();
                        RunNextFrame(
                            () => ContinueAfterEnchantment(
                                customName,
                                category,
                                baseItem,
                                description,
                                quality,
                                enchantment));
                    }))
                .ToList();

            entries.Insert(
                0,
                new ListUIItem_Generic(
                    "None",
                    ListIcon,
                    "Unenchanted",
                    () =>
                    {
                        GenericListUI.Instance.Close();
                        RunNextFrame(
                            () => ContinueAfterEnchantment(
                                customName,
                                category,
                                baseItem,
                                description,
                                quality,
                                null));
                    }));

            SaltExtraDebug.DebugMenuScreens.Draw(GenericListUI.Instance, entries, "custom-item:enchantment");
            GenericListUI.Instance.Open();
        }

        private static void ContinueAfterEnchantment(
            string customName,
            ItemCategory category,
            Item baseItem,
            string description,
            Quality quality,
            ItemComponent_Enchantment.EnchantmentType? enchantment)
        {
            if (baseItem.GetItemComponent<ItemComponent_Letter>() != null)
            {
                OpenLetterSenderInput(
                    customName,
                    category,
                    baseItem,
                    description,
                    quality,
                    enchantment);
                return;
            }

            ContinueAfterLetter(
                customName,
                category,
                baseItem,
                description,
                quality,
                enchantment,
                null,
                null);
        }

        private static void OpenLetterSenderInput(
            string customName,
            ItemCategory category,
            Item baseItem,
            string description,
            Quality quality,
            ItemComponent_Enchantment.EnchantmentType? enchantment)
        {
            OpenTextInput(
                "Enter the note or letter's sender",
                sender => OpenLetterContentInput(
                    customName,
                    category,
                    baseItem,
                    description,
                    quality,
                    enchantment,
                    sender.Trim()));
        }

        private static void OpenLetterContentInput(
            string customName,
            ItemCategory category,
            Item baseItem,
            string description,
            Quality quality,
            ItemComponent_Enchantment.EnchantmentType? enchantment,
            string sender)
        {
            OpenTextInput(
                "Enter the readable note or letter contents",
                content => ContinueAfterLetter(
                    customName,
                    category,
                    baseItem,
                    description,
                    quality,
                    enchantment,
                    sender,
                    content.Trim()));
        }

        private static void ContinueAfterLetter(
            string customName,
            ItemCategory category,
            Item baseItem,
            string description,
            Quality quality,
            ItemComponent_Enchantment.EnchantmentType? enchantment,
            string letterSender,
            string letterContent)
        {
            if (category == ItemCategory.Food)
            {
                OpenFoodExpirationInput(
                    customName,
                    category,
                    baseItem,
                    description,
                    quality,
                    enchantment,
                    letterSender,
                    letterContent);
                return;
            }

            CreateAndAdd(
                customName,
                category,
                baseItem,
                description,
                quality,
                enchantment,
                letterSender,
                letterContent,
                null);
        }

        private static void OpenFoodExpirationInput(
            string customName,
            ItemCategory category,
            Item baseItem,
            string description,
            Quality quality,
            ItemComponent_Enchantment.EnchantmentType? enchantment,
            string letterSender,
            string letterContent)
        {
            TextInputUI.Instance.Open(
                "How many in-game days should this food last? (1-10000)",
                value =>
                    int.TryParse(value, out int days) &&
                    days >= 1 &&
                    days <= 10000,
                value => RunNextFrame(
                    () => CreateAndAdd(
                        customName,
                        category,
                        baseItem,
                        description,
                        quality,
                        enchantment,
                        letterSender,
                        letterContent,
                        int.Parse(value))));
        }

        private static void CreateAndAdd(
            string customName,
            ItemCategory category,
            Item baseItem,
            string description,
            Quality quality,
            ItemComponent_Enchantment.EnchantmentType? enchantment,
            string letterSender,
            string letterContent,
            int? expirationDays)
        {
            try
            {
                Item result = baseItem.DeepClone();
                Item appearanceItem = _selectedAppearanceItem ?? baseItem;
                result.name = customName;
                result.description = description;
                result.itemCategory = category;
                result.spriteName = appearanceItem.spriteName;
                result.RemoveItemComponentsOfType<ItemComponent_VendorItem>();
                result.RemoveItemComponentsOfType<ManualAppearanceComponent>();
                result.AddItemComponent(
                    new ManualAppearanceComponent(appearanceItem.name));
                if (ItemComponent_Enchantment.IsEnchantable(baseItem))
                {
                    result.RemoveItemComponentsOfType<ItemComponent_Enchantment>();
                    if (enchantment.HasValue)
                    {
                        result.AddItemComponent(
                            new ItemComponent_Enchantment(enchantment.Value));
                    }
                }

                ItemComponent_Letter letter = result.GetItemComponent<ItemComponent_Letter>();
                if (letter != null)
                {
                    letter.SetMessage(
                        letterSender ?? string.Empty,
                        letterContent ?? string.Empty);
                }

                if (category == ItemCategory.Food && expirationDays.HasValue)
                {
                    result.RemoveItemComponentsOfType<ItemComponent_Expirable>();
                    result.AddItemComponent(
                        new ItemComponent_Expirable(expirationDays.Value * 1440));
                }

                if (result.TryGetItemComponent<IQualityItemComponent>(out _))
                {
                    result.SetQuality(quality);
                }
                else
                {
                    result.AddItemComponent(new ManualQualityComponent(quality));
                }

                if (!string.IsNullOrWhiteSpace(description) &&
                    result.GetItemComponent<ItemComponent_CustomLore>() == null)
                {
                    result.AddItemComponent(new ItemComponent_CustomLore());
                }

                Inventory inventory = Player.Instance.worldInventory.inventory;
                if (!inventory.TryAddItem(result))
                {
                    ShowError(
                        "The item could not be added. The inventory may be full or over its bulk limit.");
                    return;
                }

                AnimationHelper.Instance.SpawnItemFloatingText(result);
            }
            catch (Exception exception)
            {
                SaltExtraDebug.Plugin.Log?.LogError(exception);
                ShowError("Custom item creation failed:\n" + exception.Message);
            }
        }

        private static void OpenTextInput(string label, Action<string> onSubmitted)
        {
            TextInputUI.Instance.Open(
                label,
                value => !string.IsNullOrWhiteSpace(value),
                value => RunNextFrame(() => onSubmitted(value)));
        }

        private static void RunNextFrame(Action action)
        {
            ActionQueue.Instance.DoAfterXFrames(1, action);
        }

        private static void ShowError(string message)
        {
            DialogBox.Instance.DisplayTextNoDialog(message);
        }
    }

    [HarmonyPatch(
        typeof(ItemComponent_EnchantScroll),
        nameof(ItemComponent_EnchantScroll.GetDescriptionSuffix))]
    internal static class BrokenEnchantScrollCompatibilityPatch
    {
        private static void Prefix(ItemComponent_EnchantScroll __instance)
        {
            if (__instance?.item == null ||
                __instance.item.GetItemComponent<ItemComponent_Enchantment>() != null)
            {
                return;
            }

            // Version 3.0.0 could strip this required companion component from
            // cloned scrolls. Its original type cannot be recovered, so make
            // the existing item usable and non-crashing with a safe default.
            __instance.item.AddItemComponent(
                new ItemComponent_Enchantment(
                    ItemComponent_Enchantment.EnchantmentType.Vitality));
            SaltExtraDebug.Plugin.Log?.LogWarning(
                $"Repaired missing enchantment on scroll '{__instance.item.name}' with Vitality.");
        }
    }

    // Item.Sprite gives ISpriteProvider components priority over spriteName.
    // Keeping this override as an item component makes the selected appearance
    // survive saves even when the cloned mechanics include a pickupable model.
    public sealed class ManualAppearanceComponent :
        ItemComponent,
        ISpriteProvider
    {
        private string _appearanceItemName = "";

        public Sprite Sprite
        {
            get
            {
                Item appearance = ItemLibrary.GetItemByName(
                    _appearanceItemName,
                    logInvalid: false);
                return appearance?.Sprite;
            }
        }

        public ManualAppearanceComponent()
        {
        }

        public ManualAppearanceComponent(string appearanceItemName)
        {
            _appearanceItemName = appearanceItemName ?? "";
        }

        public override void Serialize(BinaryWriter binaryWriter)
        {
            var storage = new VersionSafeStorage();
            storage.AddToStorage("appearanceItemName", _appearanceItemName);
            storage.Serialize(binaryWriter);
        }

        public override void Deserialize(BinaryReader binaryReader)
        {
            var storage = new VersionSafeStorage();
            storage.Deserialize(binaryReader);
            _appearanceItemName = storage.GetString(
                "appearanceItemName",
                _appearanceItemName);
        }
    }

    // Some library items have no native quality-bearing component. This small,
    // serializable component makes the complete Quality enum available to them
    // without adding weapon, clothing, harvesting, or ingredient behavior.
    public sealed class ManualQualityComponent :
        ItemComponent,
        IQualityItemComponent,
        IItemValueMultiplier
    {
        public Quality Quality { get; set; }

        public ManualQualityComponent()
        {
        }

        public ManualQualityComponent(Quality quality)
        {
            Quality = quality;
        }

        public float GetMultiplier()
        {
            return (float)(Quality + 1);
        }

        public override void Serialize(BinaryWriter binaryWriter)
        {
            var storage = new VersionSafeStorage();
            storage.AddToStorage("Quality", (int)Quality);
            storage.Serialize(binaryWriter);
        }

        public override void Deserialize(BinaryReader binaryReader)
        {
            var storage = new VersionSafeStorage();
            storage.Deserialize(binaryReader);
            Quality = (Quality)storage.GetInt("Quality", (int)Quality);
        }
    }
}
