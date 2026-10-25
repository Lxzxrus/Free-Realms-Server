# Evergrove: housing and economy design

*Agreed with Nate on 2026-10-08. This is the source of truth for housing, the ranked-plot competition and Station
Cash. Tasks that build these features follow this document; a change to the design is made here first.*

**Evergrove** (presented as *Free Realms Evergrove*) is built around one idea: **creative housing is the heart of the
server.** Building is free, showing off what you built is rewarded, and the premium currency is something you earn by
playing.

Numbers marked *(placeholder)* are starting values to tune after real players arrive. Everything else is decided.

---

## 1. Creative building

- Building parts ("fixtures") are **free and unlimited**, like Minecraft's creative mode. The build menu offers every
  placeable fixture in every tint.
- Placing a fixture costs nothing; picking one up gives nothing back. Creative fixtures **never** enter inventory, so
  they can't be traded or sold.
- **Lots are free.**
- Both are settings (`Housing:CreativeMode`, `Housing:FreeLots`), on by default. Specified as task 21.

## 2. Plots

- Every player has **two plots**:
  - a **personal plot**: their home, private unless they choose otherwise;
  - a **ranked plot**: built to be visited, rated and entered into the weekly competition.
- **Plots belong to the account, not the character.** One account has one personal and one ranked plot, whichever
  character is playing. Otherwise a player could enter the competition once per character.
  *(As built in tasks 32 and 35, section 8: every house, the ranked plot included, belongs to the account, and any of
  its characters can use it.)*
- Each plot can use any lot type (seaside, snowhill, and so on), and the owner can switch lot type.
  *(Open: what happens to a build when the lot type changes. Simplest is to keep both builds and let the player
  switch back.)*

## 3. The weekly competition

### The cycle

- A **competition week** runs from Monday 00:00 to Sunday 23:59, server time *(placeholder: pick a timezone)*.
- Each week has a **theme**, chosen by staff ahead of time ("haunted house", "underwater", "tiny home"). Themes give
  new builders a fair shot and stop the same build winning forever. A week can also be themeless.
- A ranked plot is **entered** while it's published. The owner can keep building during the week.

### Voting

- Votes are **1 to 5 stars**, as the client's rating interface already supports.
- **Who can vote:** an account with at least **2 hours of playtime and at least 3 days old** *(placeholder)*. This
  is the main defence against alt accounts.
- **One vote per account per plot per week.** A voter can change their vote during the week. Votes reset when a new
  week starts, so every week is a fresh contest. Past votes are kept for history and moderation.
- **Nobody votes on a plot their own account owns.**

### Scoring

A plain average lets a plot with three 5-star votes beat one with two hundred votes averaging 4.8. The week's score
is a **vote-weighted average** instead:

```
score = (v * R + m * C) / (v + m)
```

- `R`: the plot's average this week; `v`: its number of votes this week;
- `C`: the average of all ranked plots this week; `m`: a minimum vote count *(placeholder: 10)*.

A plot with few votes is pulled towards the overall average, so it needs real support to rank high.

### Results and rewards

- At the end of the week the server builds a **results list**: the top plots by score, with their votes.
- **A staff member confirms the results before anything is paid.** They check for vote rings, alt accounts and
  rule-breaking builds, and can disqualify a plot with a written reason. At Evergrove's size, this one human check is
  the strongest defence against cheating.
- **Prizes** in Station Cash *(placeholders)*: 1st 500, 2nd 300, 3rd 150, and **25 for every entry** that got at
  least 5 votes, so everyone who takes part gains something.
- The winner joins the **Hall of Fame**. A winner can't win 1st place again for **4 weeks** *(placeholder)*; their
  plot can still be visited and voted on, and the next plot takes the prize.
- The **featured house** in the client's directory shows last week's winner.

### Moderation

- Players can **report** a plot. Staff can **unpublish** it, which removes it from the competition and the directory,
  with a reason sent to the owner.
- Staff actions on plots (disqualify, unpublish, results confirmed) are written to the audit log.

## 4. Station Cash

Station Cash stays the **premium currency**, but it's **earned, never bought**: there's no real money on Evergrove.

### Rules

- **Station Cash can't be transferred.** It can't be traded, mailed or given, and items bought with it are **bound**
  to the character, so they can't be traded either. This is what stops one person farming rewards on many alt
  accounts and funnelling them to one character.
- Station Cash belongs to the character today (`Characters.StationCash`). Keep it per character, but limit daily
  rewards per **account**, so extra characters don't multiply them.

### Where it comes from

| Source | Amount *(placeholders)* | Notes |
|---|---|---|
| Daily reward | 10, rising to 20 on a 7-day streak | Once per account per day, and only after 15 minutes of play that day, so logging in alone isn't enough |
| Difficult quests | 25–100 per quest | Set per quest in the quest data |
| Level-up milestones | 50 at chosen levels | Needs an experience system; OSFR has one in review (PR #120) |
| Weekly competition | See section 3 | Staff-confirmed |

### Where it goes

- The **Marketplace** sells premium items for Station Cash. Today every item costs **1**, which makes Station Cash
  meaningless; the Marketplace needs real prices before any of the above goes live. Coins remain the everyday
  currency for NPC vendors. Building parts are free (section 1), so Station Cash is for cosmetics, mounts, pets and
  similar.

## 5. What exists today

| Piece | State |
|---|---|
| House directory, publish/unpublish, voting, rating, featured, search | Exists (`BaseRatingPacketHandler`, `DbHouse`, `DbHouseVote`) |
| Votes | **Per account per week (task 32):** one vote per account per ranked plot per week, changeable, eligibility rules. See section 8 |
| Rating | **Weekly vote-weighted score (task 32)**, worked out from this week's votes when it's shown. See section 8 |
| Featured house | Top of the directory sort. Needs: last week's confirmed winner |
| Houses | One per account per lot type, and no limit on how many lots. **Personal and ranked plots (task 32):** one ranked plot per account. **Shared (task 35):** every character on the account enters, builds in and manages all its houses. See section 8 |
| Fixtures | **Free (PR #19):** `Housing:CreativeMode` offers all 1,706 placeable fixtures, unlimited; each placed piece records `IsCreative`, so it never returns an item. Wallpapers, floors and lots (`Housing:FreeLots`) are free. Dyeable pieces appear once, in their default colour |
| Station Cash | A per-character balance with no way to earn it. Needs everything in section 4 |
| Experience and levels | Not yet. OSFR PR #120 |

## 6. Build order

Each phase becomes cloud tasks once the one before it has been playtested.

1. **Creative building** (task 21). Then the housing playtest.
2. **Plots:** two per account, personal and ranked.
3. **Voting and scoring:** per-account weekly votes with eligibility rules, and the vote-weighted score.
4. **Competition cycle:** weeks, themes, the results list, staff confirmation, prizes, Hall of Fame, featured
   winner, reports and unpublishing.
5. **Station Cash economy:** binding and non-transferability, Marketplace prices, daily rewards and quest rewards.
   Level milestones follow when an experience system exists.

## 7. Open questions

- The competition week's timezone. Since task 32 it's a setting (`Competition:TimeZone`, default UTC); pick the zone.
- Who the staff are, and how a staff member confirms results: a chat command, or a small page on the WebAPI.
- Every *(placeholder)* number.
- What happens to a build when a plot changes lot type.
- Deleting the character who bought a house still deletes the house, although it's the account's (section 8). It could
  move to another character on the account, or the deletion could be refused while the account has other characters.

## 8. As built: plots, shared houses and weekly voting (tasks 32 and 35)

Build-order phases 2 and 3.

- **Plots.** A house is a **personal plot** unless its account has made it the **ranked plot**: standing in their own
  house, the owner says `!house ranked`. An account has one ranked plot across all its characters. Making another
  house ranked turns the old one back into a personal plot with its build untouched, which is section 2's "keep both
  builds" answer for switching lot type. `!house personal` takes the ranked plot out of the competition, and
  `!house list` shows the ranked plot, its votes and score this week, and when the week runs. Existing houses are
  personal.
- **A plot's votes count from when it last became the ranked plot.** Moving the ranked plot away and back starts it
  again from no votes that week, so an account can't run two builds in a week and keep whichever scored better.
- **Houses belong to the account (task 35).** Every character on the account can do whatever the character who bought
  a house can: enter it, locked or unpublished, from My Houses or `!house enter`; build in it (place, move, paint,
  pick up, wallpapers and floors, `!build`); publish and unpublish it; lock it and change its other settings; and make
  it the ranked or a personal plot. My Houses and `!house list` show all the account's houses, and `!house list` says
  which character bought those the player didn't. Inside, a character on the account is told it's the owner, so the
  client offers Decorate. A character on another account never gets any of this, whatever its friends.
- **Visitors see the character who bought it.** The house still records its buyer (`Houses.CharacterId`). The
  directory shows the buyer as the owner, "Go to House" on a character and `!house visit <lot> <name>` find the houses
  that character bought, and the friends who may come into a locked house are the buyer's. A friend of another
  character on the account gains nothing. So no visitor's rights changed, and nothing shows a visitor which characters
  share an account.
- **One of each lot per account.** A lot any character on the account has can't be bought again. Before task 35 two
  characters on an account could each buy the same lot; both houses are kept, and `!house enter` goes to the player's
  own first.
- **Who placed what.** Each placed part records the character that placed it (`HouseFixtures.PlacedByCharacterId`),
  for history and moderation. Parts placed before task 35 record the house's buyer, the only character that could build
  there. A part placed from inventory (only with `Housing:CreativeMode` off) goes back to the inventory of whichever
  character picks it up: another character on the account may be online, and its inventory isn't changed behind its
  back.
- **Deleting the buyer** still deletes its houses, as before task 35 (section 7).
- **Publishing** stays separate from the kind of plot. A published, unlocked ranked plot is **entered**. A locked one
  isn't: it's out of the directory and only the owner's friends can come in. A published personal plot can be visited
  and is listed in the directory, but takes no votes and shows no rating.
- **Votes** (`PlotVotes`) are cast from the client's rating panel while standing in the plot. A second vote in the
  same week changes the first. Each week starts empty, and past weeks' votes are kept, with the account that owned the
  plot, even after the house is deleted. A refused vote gets a chat message saying why. The per-character votes from
  before (`HouseVotes`) are kept as history; nothing writes them.
- **Who can vote:** the account's age is from its creation date. Its playtime is what all its characters have saved
  at logout, plus the voting character's session in progress. No character votes on its own account's plots, whichever
  character bought them; sharing houses (task 35) didn't change voting.
- **Score:** section 3's formula, with `C` the average of every vote counted this week across the competition. Each
  plot weighs in by its votes, so one account giving 1 star to a few plots nobody else voted for barely moves it. A
  plot with no votes this week shows no rating rather than `C`. Wherever the client showed the old average, it
  now shows this score and this week's vote count. The directory's top-rated list puts plots with votes first, by
  score, then the rest of the competition, then personal plots. Featured is the top of that list until phase 4.

The numbers are settings in the Gateway's `Competition` section (`gateway.json`, documented in
`gateway.local.example.json`). The Gateway reads them at startup and refuses to start if they're invalid.

| Setting | Default | What it does |
|---|---|---|
| `Competition:TimeZone` | `UTC` | The competition week's time zone, as an IANA id such as `Europe/London` |
| `Competition:WeekStartDay` | `Monday` | The day each week starts. Votes reset then |
| `Competition:WeekStartTime` | `00:00:00` | The time each week starts. Across a daylight-saving change a week is an hour shorter or longer |
| `Competition:MinAccountAgeDays` | 3 *(placeholder)* | How old an account must be to vote |
| `Competition:MinPlaytimeMinutes` | 120 *(placeholder)* | How long its characters must have played, between them, to vote |
| `Competition:MinimumVotes` | 10 *(placeholder)* | `m` in the score. 0 makes it a plain average |

1 to 5 stars is the client's, not a setting. Phase 4 (results, staff confirmation, prizes, the Hall of Fame, the
featured winner, reports) is not built.

## 9. As built: invites, house names and the directory panel

- **Invites.** Standing in a house their account owns, a player can invite an online player into it with "Invite to
  House" on the friends list. The invitee gets the client's prompt, "[Player] has invited you to enter their house."
  Accepting brings them in **even if the house is locked or unpublished**, which is what the client's own tip promises
  ("This prevents people from visiting your house without an invitation from you"). An invite lasts 5 minutes and is
  used up on arrival. Once in, the guest stays when the house is locked, until they leave. Declining tells the inviter.
  An invite to a player who ignores the inviter is dropped without a word, like a tell. Characters on the house's own
  account need no invite. Invites live in memory, so a server restart drops unanswered ones.
- **House names.** The owner names a house from the title bar, from the name picker or typed. A name is 3 to 32
  characters: letters, numbers, single spaces and `' - ! & , .`. It's checked against the same name filter as character
  and guild names (`NameFilter.txt`). A refused name gets a chat message saying why, and the client's title bar goes back
  to the real name. Everyone in the house sees a new name straight away; the directory and My Houses show it.
- **Directory panel.** A house is in the directory when it's published and unlocked (`HouseDirectory.IsListed`). The
  owner's panel shows "submitted" only then: a published house that's locked shows as not submitted, since it's out of
  the directory until it's unlocked. Visitors get the rating panel only for a listed house. Publishing, unpublishing,
  locking, unlocking and `!house ranked`/`personal` update the panels of everyone in the house straight away; before,
  they kept what they saw on arrival.
- **Why a player may see no vote button.** Section 3's eligibility rules don't hide the button: a refused vote says why
  in chat. The button is missing when the plot isn't entered (not the ranked plot, unpublished or locked) or when the
  player's account owns the plot. Every character on the owning account gets the owner's panel instead, alts included.
  So none of a player's characters can vote for their own ranked plot, while anyone else's account can. Staff have no
  special voting rule.
