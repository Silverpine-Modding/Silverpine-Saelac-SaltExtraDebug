using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SaltExtraDebug;

internal static class NpcTeleportDebug
{
    // Cardinal tiles first, then diagonals. Never overlap the player.
    private static readonly Vector2Int[] LandingOffsets =
    {
        new Vector2Int(0, -1), new Vector2Int(1, 0),
        new Vector2Int(0, 1), new Vector2Int(-1, 0),
        new Vector2Int(-1, -1), new Vector2Int(1, -1),
        new Vector2Int(1, 1), new Vector2Int(-1, 1)
    };

    internal static void ShowPicker()
    {
        if (Player.Instance == null || GenericListUI.Instance == null)
        {
            return;
        }

        var npcs = NeuralNPC.neuralNPCs.Values
            .Where(npc => npc != null && npc.gameObject.activeInHierarchy
                && npc.GetComponent<NPCRoutineExecutor>() != null
                && npc.GetComponent<EntityMover>() != null)
            .Distinct()
            .OrderBy(npc => npc.GetFinalName())
            .ToArray();

        if (npcs.Length == 0)
        {
            Notify("There are no loaded NPCs to teleport.");
            return;
        }

        var items = new List<ListUIItem>
        {
            new ListUIItem_Generic("Teleport NPC To Player", null, "Wait 3 turns", null)
        };
        foreach (NeuralNPC npc in npcs)
        {
            NeuralNPC selected = npc;
            items.Add(new ListUIItem_Generic(
                npc.GetFinalName(),
                ListUIItem_Generic.GameObjectToIcon(npc.gameObject),
                "",
                delegate
                {
                    GenericListUI.Instance.Close();
                    Player player = Player.Instance;
                    if (player != null)
                    {
                        player.StartCoroutine(TeleportWhenIdle(player, selected));
                    }
                }));
        }
        items.Add(new ListUIItem_Generic("Back", null, "", delegate
        {
            GenericListUI.Instance.Close();
            DebugUI.Instance.ShowDebugOptions();
        }));

        DebugMenuScreens.Draw(GenericListUI.Instance, items, "teleport-npc");
        GenericListUI.Instance.Open();
        GameObject contentRoot = Traverse.Create(GenericListUI.Instance)
            .Field("instanceRoot").GetValue<GameObject>();
        if (contentRoot != null)
        {
            AddCheatButtonsPatch.AddCloseButton(contentRoot);
        }
    }

    private static IEnumerator TeleportWhenIdle(Player player, NeuralNPC npc)
    {
        EntityMover mover = npc != null ? npc.GetComponent<EntityMover>() : null;
        float deadline = Time.realtimeSinceStartup + 10f;
        // A running movement coroutine would otherwise snap the NPC back.
        while (player != null && player == Player.Instance && npc != null
            && mover != null && ActionQueue.Instance != null
            && (ActionQueue.Instance.turnBeingProcessed
                || ActionQueue.Instance.processTurnRequested || mover.moving))
        {
            if (Time.realtimeSinceStartup >= deadline)
            {
                Notify("Wait for the current turn to finish, then try teleporting again.");
                yield break;
            }
            yield return null;
        }

        if (player == null || player != Player.Instance || npc == null
            || mover == null || !npc.gameObject.activeInHierarchy
            || ActionQueue.Instance == null)
        {
            yield break;
        }

        NPCRoutineExecutor executor = npc.GetComponent<NPCRoutineExecutor>();
        if (executor == null || executor.currentRoutine == null
            || executor.pathfindingEntity == null)
        {
            Notify("This NPC does not have an active routine to temporarily suspend.");
            yield break;
        }

        Vector2Int playerPosition = player.transform.GetVector2IntPosition();
        if (!TryFindLanding(npc, playerPosition, out Vector2Int destination))
        {
            Notify("There is no free tile beside the player. Move into an open area first.");
            yield break;
        }

        mover.Teleport(destination);
        NpcTeleportRoutine.StartWaiting(executor, destination);
        mover.StopRandomWalking();
        executor.pathfindingEntity.ClearPathAndDeclareSuccess();
        mover.TurnTowards(playerPosition);
        Notify(npc.GetFinalName() + " teleported to you and will wait for 3 turns.");
    }

    internal static bool TryFindLanding(
        NeuralNPC npc, Vector2Int playerPosition, out Vector2Int destination)
    {
        foreach (Vector2Int offset in LandingOffsets)
        {
            Vector2Int candidate = playerPosition + offset;
            // NPCs may stack together; only terrain and non-NPC blockers count.
            if (!Turfs.IsValidTurf(candidate)
                || TileTags.HasTileTag(candidate, TileTag.Submerge)
                || Turfs.GetTurfComponent<IPseudoBumpHandler>(candidate) != null
                || Turfs.GetTurfComponents<EntityMover>(candidate)
                    .Any(other => other != null && other.gameObject != npc.gameObject
                        && other.GetComponent<NeuralNPC>() == null))
            {
                continue;
            }
            if (TurfCollider.TryGetImpassableColliders(candidate, out var colliders)
                && colliders.Any(c => c != null && c.gameObject != npc.gameObject
                    && c.GetComponent<NeuralNPC>() == null))
            {
                continue;
            }

            destination = candidate;
            return true;
        }
        destination = default;
        return false;
    }

    private static void Notify(string message)
    {
        if (UpperNotificationUI.Instance != null)
        {
            UpperNotificationUI.Instance.OneOff(message);
        }
    }
}
