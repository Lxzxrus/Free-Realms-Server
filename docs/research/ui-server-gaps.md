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
lock, flora, pet spawn), trading, quests (accept, decline, abandon, select, take me there), friends (add, remove,
ignore), equipping, coin-store buy and sell, character and guild name checks, job switching, abilities, joining
activities, minigames, fishing, mounts, titles, teleports to points of interest, the strip-effect request, quick chat.

## Not answered: worth building

| Feature | Calls (UI module) | Notes |
|---|---|---|
| **Pets** | `Pet.Summon`, `Dismiss`, `PerformTrick`, `PlayWithToy`, `RaiseNeed`, `StartGroom`, `EquipItem`, `SetName`, `WasGood` | The whole pet system; no pet handler exists |
| **Groups (parties)** | `Ui.InviteToGroup`, `GroupScreen.DepartGroup`, `RemovePlayerFromGroupByGuid`, `Ui.TargetGroupMember` | No group handler; matters for group activities |
| **Storage (bank)** | `Ui.InitializeStorage`, `RequestStorageUsage`, `SendItemToStorage` | No storage handler |
| **House invites and names** | `House.InviteToHouse`, `House.SetName`, `SetNameCustom` | The rest of housing is handled |
| **Coin-store buy-back** | `CoinStoreScreen.BuyBackItem` | Buying back something you sold |
| **Use an item** | `EquipScreen.UseImmediateActivationItem`, `CheckImmediateActivationItem` | Consumables used from the inventory |
| **Crafting** | `Ui.RecipeStart`, `Factory.CreateBlueprint`, `AddBlueprintToFoundation`, `RequestToolData` | Recipes and the blueprint factory |
| **Teleport to a friend** | `Ui.TeleportToFriend` | Friends' positions are answered, the teleport isn't |
| **Map pings** | `Ui.SendMapPing` | |
| **Leaderboards** | `Ui.RequestLeaderBoardData` | Minigame and activity high scores |
| **Mystery chests** | `Ui.GetMysteryChestInfo` | |
| **Progressive quests** | `Ui.ProgressiveQuestRequestStartQuest`, `RequestClaimSlot`, `RequestClaimPrize` | |
| **Codes and claims** | `PromotionalScreen.RedeemCode`, `ClaimScreen:StartClaim`, `AddClaimItem` | Could serve Evergrove's own event codes |
| **Quest turn-in from the book** | `QuestScreen.EndQuest` | Check whether it reaches the existing quest reply |
| **Un-ignore** | `Ui.StopIgnoringPlayer` | Check whether it's the same packet as ignore |
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
