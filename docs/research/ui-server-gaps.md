# What the client's UI asks the server for, and what Evergrove doesn't answer yet

*2026-10-25. Drafted from an overnight run of Nate's local Qwen model over the client's decompiled UI scripts
(`UI/ScriptsBase.lua`, 216 modules; see `client-reverse-engineering` notes). For every module Qwen listed each call
into the client's own API and flagged the ones that look like server requests: 258 flags, about 150 distinct calls once
five false positives (UI tables, not API) are dropped. Claude matched them by name against the Gateway's packet
handlers (`src/Sanctuary.Gateway/Handlers`) and confirmed in the Lua that the calls listed as gaps exist. A name match
is a lead, not proof: the packet each API call sends is in `FreeRealms.exe`, not the Lua. The raw reports are in Nate's
sandbox (`qwen-sandbox/reports/004-ui-calls`).*

## Already answered

Guilds (create, invite, promote, demote, kick, quit, name check), houses (enter, the house list, pick up everything,
lock, flora, pet spawn, invites, names), trading, quests (accept, decline, abandon, select, take me there), friends (add, remove,
ignore), equipping, coin-store buy and sell, character and guild name checks, job switching, abilities, joining
activities, minigames, fishing, mounts, titles, teleports to points of interest, the strip-effect request, quick chat.

Two leads from the first draft of this list turned out to be answered, checked in `FreeRealms.exe` (Ghidra, the
Lua binding table beside each name):

- **Un-ignore** (`Ui.StopIgnoringPlayer`, `0xc24c00`) is `Ui.IgnorePlayer` (`0xc24b00`) with one argument changed: both
  build `CommandPacketIgnoreRequest` (command `0x1e`, `0xc1dc10`) and only its last field differs, 1 to ignore and 0
  to stop. `CommandPacketIgnoreRequestHandler` answers both through `Ignore`.
- **`QuestScreen.EndQuest`** (`0xc0a1a0`) isn't a turn-in from the quest book. The Lua calls it when the quest end
  screen (`Main.wndQuestEnd`, the reward window an npc's turn-in opens) closes, and it sends `QuestEndReplyPacket`
  (quest sub-opcode `0xe`, `0xa864c0`) with the quest id, once, only while one is pending. That's
  `QuestEndPacket.SubOpCode + 1` in `BaseQuestPacketHandler`, which hands the pending quest in. The quest book has no
  turn-in of its own.

## Not answered: worth building

| Feature | Calls (UI module) | Notes |
|---|---|---|
| **Pets** | `Pet.Summon`, `Dismiss`, `PerformTrick`, `PlayWithToy`, `RaiseNeed`, `StartGroom`, `EquipItem`, `SetName`, `WasGood` | The whole pet system; no pet handler exists |
| **Groups (parties)** | `Ui.InviteToGroup`, `GroupScreen.DepartGroup`, `RemovePlayerFromGroupByGuid`, `Ui.TargetGroupMember` | No group handler; matters for group activities |
| **Storage (bank)** | `Ui.InitializeStorage`, `RequestStorageUsage`, `SendItemToStorage` | No storage handler |
| **Coin-store buy-back** | `CoinStoreScreen.BuyBackItem` | Buying back something you sold |
| **Use an item** | `EquipScreen.UseImmediateActivationItem`, `CheckImmediateActivationItem` | Consumables used from the inventory |
| **Crafting** | `Ui.RecipeStart`, `Factory.CreateBlueprint`, `AddBlueprintToFoundation`, `RequestToolData` | Recipes and the blueprint factory |
| **Teleport to a friend** | `Ui.TeleportToFriend` | Friends' positions are answered, the teleport isn't |
| **Map pings** | `Ui.SendMapPing` | |
| **Leaderboards** | `Ui.RequestLeaderBoardData` | Minigame and activity high scores |
| **Mystery chests** | `Ui.GetMysteryChestInfo` | |
| **Progressive quests** | `Ui.ProgressiveQuestRequestStartQuest`, `RequestClaimSlot`, `RequestClaimPrize` | |
| **Codes and claims** | `PromotionalScreen.RedeemCode`, `ClaimScreen:StartClaim`, `AddClaimItem` | Could serve Evergrove's own event codes |
| **Social zones** | `Ui.EnterSocialZone` | |

## Not answered: combat, for later

`Ui.RevivePlayer`, `Ui.OnPaidRevive`, `Ui.StartPendingEncounter`, `CancelPendingEncounter`,
`GroupScreen.ActivateEncounter`: combat and encounters, outside Evergrove's current scope.

## Not answered, on purpose

Real-money purchases (credit cards, PayPal, SMS, subscriptions, wallet funding in `InGamePurchaseStoreScreen`):
Evergrove has no real money, and these should stay off. Outside services that are gone (`SocialShare`, `VideoUpload`,
`LiveGamer`, `ClientNudge`).

## Client-side only

Searching, sorting and filtering (`CoinStoreScreen.SearchItems`, `InventoryScreen.SearchInventoryItems` and the like),
previews (`EquipScreen.PreviewEquipItemById`), camera and screenshot settings, and `Achievement.Select*` were flagged
but don't need the server.
