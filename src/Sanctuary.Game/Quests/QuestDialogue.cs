using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Packet;

namespace Sanctuary.Game.Quests;

public static class QuestDialogue
{
    private const int YouGotItTextId = 103085;

    private const int PlusImageId = 303;
    private const int LeaveImageId = 4008;

    private const int GreenButtonImageSet = 17;

    public const int TalkAnimationId = 3105;

    private const int IdleAnimationId = 1;

    private const byte SetBaseAnimation = 1;

    private const int TalkAnimationMs = 1500;

    public static void PlayTalkAnimation(Player player, ulong npcGuid)
    {
        if (npcGuid == 0)
            return;

        if (player.TalkingNpcGuid != 0 && player.TalkingNpcGuid != npcGuid)
            StopTalkAnimation(player);

        player.TalkingNpcGuid = npcGuid;

        var ticket = ++player.TalkAnimationTicket;

        player.SendTunneled(new PlayerUpdatePacketSetAnimation
        {
            Guid = npcGuid,
            AnimationId = TalkAnimationId,
            Flags = SetBaseAnimation
        });

        _ = Task.Run(async () =>
        {
            await Task.Delay(TalkAnimationMs);

            if (player.TalkAnimationTicket == ticket)
                StopTalkAnimation(player);
        });
    }

    public static void StopTalkAnimation(Player player)
    {
        if (player.TalkingNpcGuid == 0)
            return;

        var npcGuid = player.TalkingNpcGuid;
        player.TalkingNpcGuid = 0;
        player.TalkAnimationTicket++;

        player.SendTunneled(new PlayerUpdatePacketSetAnimation
        {
            Guid = npcGuid,
            AnimationId = IdleAnimationId,
            Flags = SetBaseAnimation
        });
    }

    /// <summary>
    /// Starts a conversation with an NPC, replacing any other. <paramref name="onFinished"/> runs once its last line is
    /// answered (at once when there are no lines), never if the player walks off or it's replaced.
    /// </summary>
    public static void Begin(Player player, IReadOnlyList<QuestDialogueLine> lines, ulong npcGuid, Action? onFinished = null)
    {
        player.Conversation = null;

        if (lines.Count == 0)
        {
            onFinished?.Invoke();
            return;
        }

        var conversation = new QuestConversation(lines, npcGuid, onFinished);
        player.Conversation = conversation;

        Show(player, conversation);
    }

    /// <summary>
    /// Answers the line on screen with the response the player picked. Returns false when there is nothing more to
    /// show; the caller closes the dialog and then runs <paramref name="onFinished"/>, if the conversation finished
    /// rather than being abandoned.
    /// </summary>
    public static bool TryAdvance(Player player, int responseId, out Action? onFinished)
    {
        onFinished = null;

        var conversation = player.Conversation;
        if (conversation is null)
        {
            StopTalkAnimation(player);
            return false;
        }

        var step = conversation.Answer(responseId);
        if (step == QuestConversationStep.Next)
        {
            Show(player, conversation);
            return true;
        }

        if (step == QuestConversationStep.Finished)
            onFinished = conversation.OnFinished;

        Clear(player);
        return false;
    }

    public static void Clear(Player player)
    {
        player.Conversation = null;
        StopTalkAnimation(player);
    }

    private static void Show(Player player, QuestConversation conversation)
    {
        var line = conversation.Current;

        var dialog = new CommandPacketShowDialog
        {
            DialogueTextId = line.TextId,
            NpcGuid = conversation.NpcGuid,
            CameraFocusParam = 1f,
        };

        if (line.Choices.Count > 0)
        {
            for (var i = 0; i < line.Choices.Count; i++)
            {
                dialog.Responses.Add(new CommandPacketShowDialog.Response
                {
                    Id = i + 1,
                    LabelTextId = line.Choices[i].ResponseTextId,
                    Param1 = PlusImageId,
                    Param2 = GreenButtonImageSet,
                });
            }
        }
        else
        {
            dialog.Responses.Add(new CommandPacketShowDialog.Response
            {
                Id = 1,
                LabelTextId = line.ResponseTextId != 0 ? line.ResponseTextId : YouGotItTextId,
                Param1 = conversation.IsLastLine ? LeaveImageId : PlusImageId,
                Param2 = GreenButtonImageSet,
            });
        }

        PlayTalkAnimation(player, conversation.NpcGuid);

        player.SendTunneled(dialog);
    }
}
