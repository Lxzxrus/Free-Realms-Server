# The Hero's Journal and the welcome window's quest box

Found 2026-10-29 by reverse engineering `FreeRealms.exe` (Ghidra; addresses in the 32-bit exe, image base 0x400000)
and the client's UI (its decompiled Lua and `welcome.gfx`). The server calls the journal the Adventurer's Journal;
the client's text calls it the Hero's Journal.

## What the player sees

- The welcome window's **Hero's Journal Quest** box (`welcome.gfx`, `HerosQuest.populatePanel`): with a current quest,
  its title and objective text and a working **Go**; without one, the client's "no quests" text and a greyed-out Go.
  Go sends `OpenHerosJournal`, which closes the welcome window and opens the journal (`StoryBook`). Nothing goes to
  the server.
- When a quest that a journal hub lists is added to the quest log, the client shows a **Hero's Journal** button at the
  top of the screen (0xa44bb0 → `CallHandler:ShowAdvJournal` → `HerosJournalButton:Show`). Clicking it opens the
  journal.

## Where the box's quest comes from

- The box reads `BaseClient.AdventurersJournal.HubData` (`WelcomeHandler:PopulateHerosQuest`): the first row whose
  Is Active is 1, with its Current Quest Title and Description. The box is filled once, when the window opens.
- That data source (row getter 0xc98170) has no quest per hub. Is Active means "this is the journal's active hub", and
  Current Quest Id, Title and Description are the journal's one current quest, the same in every row. Title and
  description come from the client's quest log (lookup 0xc7a610: title at +0x15c, objective at +0x16c) and are empty
  when the quest isn't in it. Only `QuestAddPacket` (quest sub-op 3) puts a quest there.
- The client picks the active hub and the current quest itself: after the info packet and when a hub's quest is added
  (0xa447d0), and on every finished-set packet (journal dispatcher 0xa45d80).
  - **Region:** it keeps the region it is in, and moves to the next region only if that region has a quest in the
    quest log or finished (0xa44100).
  - **Active hub:** the region's unfinished hub with the lowest display order (0xa43f80). A hub is finished when all
    its quests are in the finished set (0xa43f00); without a finished set, none is.
  - **Current quest:** the active hub's unfinished quest with the lowest number (0xa44020). The number is a hub
    quest's third field, `AdventurersJournalHubQuestDefinition.Unknown`.
- The finished set is `AdventurersJournalQuestUpdatePacket` (base opcode 0xd1, sub-op 2): a count, then (quest id,
  value) pairs. The client only looks at the ids.

## What it means for Evergrove

- Region 1 used to list the live game's 2013 story first: Briarwood Caverns (quests 2513-2514), Farnum Farmstead
  (2521-2526), then Cobblestone Village (2527-2530, 2566). None of those quests are on this server, so the current
  quest was never one the client had, and the box always said there were no quests.
- Region 1 now holds one hub, Cobblestone Village, listing Evergrove's story (`AdventurersJournal`).
- A story lists quests in the order they are played, each given where the one before it is handed in. Between two
  story quests the client makes the untaken one current and shows the box with no text. So at login the server
  leaves the finished set out while the player is between two story quests (the box then says there are no quests),
  and sends it when the next story quest is taken.
- The welcome packet goes after the quest packets, so the box sees the current quest.
- A brand-new player has nothing in the quest log, so the box says there are no quests until they take Introduce
  Yourself.

## Open

- Regions 2-4 still list the 2013 story's later hubs. They never become current (no quest of theirs is ever taken or
  finished), but the journal's tabs for them show hubs without quests. Give them Evergrove stories: which chain
  belongs to which town is a question for Ariel.
- What a region's GeometryId is compared with (the journal starts in the region it matches, else region 1), and what
  hides a region's tab (`ShouldBeHidden`).
- A lead, not checked: the job browser and the map's points of interest call `Ui.StartBreadcrumbQuest(questId)`,
  which probably sends `QuestStartBreadcrumbPacket` (quest sub-op 15; the client's quest dispatcher doesn't handle
  15, so it only goes out). The server doesn't handle it. It could start a guiding quest from the map or job browser.
