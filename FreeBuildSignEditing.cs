using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SaltExtraDebug;

[HarmonyPatch(typeof(Sign), nameof(Sign.OnInteract))]
internal static class FreeBuildSignEditing
{
    private static readonly MethodInfo OwnershipCheck = AccessTools.Method(
        typeof(WorldInfoManager), nameof(WorldInfoManager.IsOnPlayerOwnedPlot));
    private static readonly MethodInfo EditingPermission = AccessTools.Method(
        typeof(FreeBuildSignEditing), nameof(CanEditSign));

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (!instruction.Calls(OwnershipCheck))
            {
                yield return instruction;
                continue;
            }

            // Change only this sign's Edit-button decision, not world ownership
            // or the native text input, writing interaction, NPC logic or save data.
            var check = new CodeInstruction(instruction);
            var permission = new CodeInstruction(OpCodes.Call, EditingPermission);
            foreach (ExceptionBlock block in check.blocks)
                if (block.blockType == ExceptionBlockType.EndExceptionBlock)
                    permission.blocks.Add(block);
            check.blocks.RemoveAll(block => block.blockType == ExceptionBlockType.EndExceptionBlock);
            yield return check;
            yield return permission;
        }
    }

    private static bool CanEditSign(bool playerOwned) =>
        playerOwned || Plugin.FreeBuildEnabled?.Value == true;
}
