using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SilverpineMods.CustomItemFreezeFix;

// Execute with Unity's Mono via MonoHost, not the desktop .NET runtime.
// Writer/reader run in separate processes so the reader cannot use legacy types.
internal static class Program
{
    private static int checks, promptCalls;
    private static readonly string FixtureRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures");
    private static readonly FieldInfo Components = typeof(Item).GetField("itemComponents", BindingFlags.Instance | BindingFlags.NonPublic);

    private static int Main()
    {
        try
        {
            string legacyDll = Environment.GetEnvironmentVariable("SALT_LEGACY_ITEM_DLL");
            if (!string.IsNullOrEmpty(legacyDll)) { WriteLegacyFixtures(legacyDll); return 0; }
            ReadAndCheck();
            Console.WriteLine($"PASS: {checks} custom-item migration checks in the game's Mono runtime (no preloader/legacy DLL).");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteLegacyFixtures(string path)
    {
        Assembly legacy = Assembly.LoadFrom(Path.GetFullPath(path));
        var appearance = (ItemComponent)Activator.CreateInstance(legacy.GetType("SilverpineMods.CustomItemFreezeFix.ManualAppearanceComponent"), "Legacy icon item");
        var quality = (ItemComponent)Activator.CreateInstance(legacy.GetType("SilverpineMods.CustomItemFreezeFix.ManualQualityComponent"), Quality.Legendary);
        var components = new List<ItemComponent> { appearance, quality, new ItemComponent_Durability(37f),
            new ItemComponent_Enchantment(ItemComponent_Enchantment.EnchantmentType.Knowledge) };
        Directory.CreateDirectory(FixtureRoot);
        using (var writer = new BinaryWriter(File.Create(Path.Combine(FixtureRoot, "legacy-components.bin"))))
            TypeSerializationUtility.SerializeListWithType(components, writer);
        var item = new Item { name = "Legacy handmade item", description = "Migration fixture", value = 71, bulk = 2.5f };
        Components.SetValue(item, components);
        foreach (var component in components) component.item = item;
        using (var writer = new BinaryWriter(File.Create(Path.Combine(FixtureRoot, "legacy-item.bin"))))
            TypeSerializationUtility.SerializeWithType(item, writer);
        Console.WriteLine("PASS: legacy fixtures serialized using the backed-up CustomItemFreezeFix DLL and native game serializer.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReadAndCheck()
    {
        Assembly merged = typeof(ManualQualityComponent).Assembly;
        Check(merged.GetName().Name == "SaltExtraDebug", "Components now belong to the debug assembly.");
        Check(!merged.GetReferencedAssemblies().Any(assembly => assembly.Name == "CustomItemFreezeFix"
            || assembly.Name == "CustomItemPromptPatcher" || assembly.Name == "ModdingTools"), "No legacy or framework hard dependency.");
        Type resolver = merged.GetType("SaltExtraDebug.CustomItemTypeResolution", true);
        MethodInfo install = resolver.GetMethod("Install", BindingFlags.Static | BindingFlags.NonPublic);
        install.Invoke(null, null);
        install.Invoke(null, null);
        Check((bool)resolver.GetField("_installed", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), "Type resolver installation is idempotent.");

        using (var reader = new BinaryReader(File.OpenRead(Path.Combine(FixtureRoot, "legacy-components.bin"))))
        {
            var components = TypeSerializationUtility.DeserializeListWithType<ItemComponent>(reader);
            VerifyComponents(components);
            Check(reader.BaseStream.Position == reader.BaseStream.Length, "Legacy component stream consumed exactly.");
        }
        Item item;
        using (var reader = new BinaryReader(File.OpenRead(Path.Combine(FixtureRoot, "legacy-item.bin"))))
        {
            item = TypeSerializationUtility.DeserializeWithType<Item>(reader);
            Check(reader.BaseStream.Position == reader.BaseStream.Length, "Legacy full-item stream consumed exactly.");
        }
        Check(item.name == "Legacy handmade item" && item.description == "Migration fixture" && item.value == 71
            && item.bulk == 2.5f, "Native full-item loader preserves item fields.");
        VerifyComponents((List<ItemComponent>)Components.GetValue(item));
        Check(((List<ItemComponent>)Components.GetValue(item)).All(component => ReferenceEquals(component.item, item)), "Native loader restores component back-references.");
        Item clone = item.DeepClone();
        Check(!ReferenceEquals(clone, item) && clone.name == item.name, "Native item deep clone succeeds.");
        VerifyComponents((List<ItemComponent>)Components.GetValue(clone));
        foreach (Quality quality in Enum.GetValues(typeof(Quality)))
        {
            var original = new ManualQualityComponent(quality);
            var copy = original.DeepClone();
            Check(copy.Quality == quality && copy.GetMultiplier() == original.GetMultiplier(), "Native generic clone preserves every rarity.");
        }
        var appearanceClone = new ManualAppearanceComponent("Other appearance").DeepClone();
        Check(ReadAppearance(appearanceClone) == "Other appearance", "Native generic clone preserves selected appearance.");

        var thirdParty = new ExternalProbeComponent { Value = 924 };
        Check(thirdParty.DeepClone().Value == 924, "Other loaded assemblies retain the old resolver's custom-component support.");
        var status = new ExternalStatusEffect { StoredValue = 315 };
        Check(status.DeepClone().StoredValue == 315, "Resolution also works for non-item generic types.");
        Check(new ItemComponent_Durability(58).DeepClone().Durability == 58, "Native component lookup remains unchanged.");
        Check(Type.GetType("Missing.Custom.Item.Type", throwOnError: false) == null, "Missing types return null without recursive overflow.");

        // Do not cache misses: an assembly introduced later must still resolve.
        const string lateName = "LateMod.ItemComponent";
        Check(Type.GetType(lateName, false) == null, "Late component starts unresolved.");
        var dynamicAssembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("LateMod"), AssemblyBuilderAccess.Run);
        var lateType = dynamicAssembly.DefineDynamicModule("Main").DefineType(lateName, TypeAttributes.Public, typeof(ItemComponent)).CreateType();
        ItemComponent late = (ItemComponent)Activator.CreateInstance(lateType);
        Check(late.DeepClone().GetType() == lateType, "Late-loaded component resolves after a previous miss.");

        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "CustomItemFreezeFix"
            || assembly.GetName().Name == "CustomItemPromptPatcher"), "No legacy plugin or patcher was loaded during migration checks.");
        Check(AccessTools.GetDeclaredMethods(typeof(TypeSerializationUtility)).All(method => Harmony.GetPatchInfo(method) == null),
            "Native generic serialization methods remain unpatched.");
        CheckWizardRouting();
    }

    private static void VerifyComponents(List<ItemComponent> components)
    {
        Check(components.Count == 4, "All legacy components survive.");
        Check(components[0] is ManualAppearanceComponent appearance && ReadAppearance(appearance) == "Legacy icon item", "Legacy appearance name preserved.");
        Check(components[1] is ManualQualityComponent quality && quality.Quality == Quality.Legendary, "Legacy rarity preserved in migrated type.");
        Check(components[2] is ItemComponent_Durability durability && durability.Durability == 37 && durability.maxDurability == 37, "Native durability data preserved.");
        Check(components[3] is ItemComponent_Enchantment enchantment && enchantment.enchantmentType == ItemComponent_Enchantment.EnchantmentType.Knowledge, "Native enchantment preserved.");
    }

    private static string ReadAppearance(ManualAppearanceComponent component)
    {
        using (var stream = new MemoryStream())
        {
            var writer = new BinaryWriter(stream); component.Serialize(writer); writer.Flush(); stream.Position = 0;
            var storage = new VersionSafeStorage(); storage.Deserialize(new BinaryReader(stream));
            return storage.GetString("appearanceItemName", "");
        }
    }

    private static void CheckWizardRouting()
    {
        var harmony = new Harmony("saltextradebug.tests.item-routing");
        harmony.Patch(AccessTools.Method(typeof(DebugGenerateCustomItemPatch), "OpenPrompt"),
            prefix: new HarmonyMethod(typeof(Program), nameof(CapturePrompt)));
        harmony.CreateClassProcessor(typeof(DebugGenerateCustomItemPatch)).Patch();
        Type action = AccessTools.Inner(typeof(DebugUI), "DebugAction_GenerateCustomItem");
        object instance = Activator.CreateInstance(action, true);
        MethodInfo call = AccessTools.Method(AccessTools.Inner(typeof(DebugUI), "DebugAction"), "Call");
        call.Invoke(instance, null);
        Check(promptCalls == 1, "Real debug action routes to merged wizard once without executing native generator.");
        AccessTools.DeclaredMethod(action, "OnCalled").Invoke(instance, null);
        Check(promptCalls == 2, "Direct OnCalled invocation also uses the runtime replacement for the old preloader rewrite.");
        MethodInfo prefix = AccessTools.Method(typeof(DebugGenerateCustomItemPatch), "Prefix");
        Check((bool)prefix.Invoke(null, new[] { new object() }), "Unrelated debug actions pass through.");
        Check((bool)prefix.Invoke(null, new object[] { null }), "Null actions are not intercepted.");
    }

    private static bool CapturePrompt() { promptCalls++; return false; }
    private static void Check(bool value, string description)
    {
        if (!value) throw new Exception(description);
        checks++;
    }
}

public sealed class ExternalProbeComponent : ItemComponent
{
    public int Value;
    public override void Serialize(BinaryWriter writer) => writer.Write(Value);
    public override void Deserialize(BinaryReader reader) => Value = reader.ReadInt32();
}
public sealed class ExternalStatusEffect : ISerializable
{
    public int StoredValue;
    public void Serialize(BinaryWriter writer) => writer.Write(StoredValue);
    public void Deserialize(BinaryReader reader) => StoredValue = reader.ReadInt32();
}
