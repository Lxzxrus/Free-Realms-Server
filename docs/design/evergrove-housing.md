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
| Votes | **Per character**, permanent, 1–5 stars, owner's own character can't vote. Needs: per account, weekly reset, changeable, eligibility rules |
| Rating | Plain average on `DbHouse.Rating`. Needs: weekly vote-weighted score |
| Featured house | Top of the directory sort. Needs: last week's confirmed winner |
| Houses | One per character per lot type; no limit on how many. Needs: personal and ranked plots per account |
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

- The competition week's timezone.
- Who the staff are, and how a staff member confirms results: a chat command, or a small page on the WebAPI.
- Every *(placeholder)* number.
- What happens to a build when a plot changes lot type.
