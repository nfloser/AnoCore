# AnoArcade: Triple, profile AP and case simulation

## Current implementation

The first slice is an **isolated native animation probe**, not a production
economy or an exact Triple Triple Chance implementation. It is disabled by
default and requires `ano.casino.test`. `!anoarcadetest` opens the clean black/green
arcade selection; three equally sized Slots/Coinflip/Roulette cards retain the
approved layout, and a separate case-simulator entry accommodates the fourth
mode. Only Slots is interactive. The other entries explicitly say planned.

The connected player's name is projected from the existing player registry.
2,750 **Test-AP** exist only for that probe session. They have no relationship to
progression XP, rank points or a durable AP account. Closing/reopening preserves
the probe state; reconnect, map end and unload discard it. Repeatedly opening
the menu does not refill the current test account.

Load moves up to 500 available Test-AP into the machine; return transfers all
machine credit back. Stake changes in steps of 50 between 50 and 500, initially
100. The start button requires enough machine credit. During motion, stake and
transfer actions are rejected, including repeated clicks. The current-spin win
resets to zero at start. A fixed alternating no-win/2x-test-win sequence checks
both displays. **This is deliberately not a payout model or random game.**

### Why this probes the refresh problem

`CounterStrikeCustomHudService.SetText` applies dialog variables to the existing
entity. `SetClass` applies a class to the existing panel. The probe does not call
Show repeatedly, rebuild the layout or transmit animation frames.

Each of three bounded strips contains twelve static fruit panels, with three
visible at a time. CSS animates one repeating three-symbol period. Two distinct
keyframe names alternate between spins to avoid removing and readding the same
class in one network tick. The server stops reels at 2.6, 3.1 and 3.6 seconds;
the result display settles at four seconds. These are probe timings with hard
stops, not the final acceleration/deceleration animation.

After one second a dialog-variable update is deliberately sent during motion.
The **Textupdate testen** button allows additional manual updates. The timer
runs every 250 ms but emits only the single automatic text update and the three
stop transitions. Text/CSS behavior in native CS2 remains unverified until the
acceptance below passes. Dota/browser support does not establish CS2 support.

### Build and test on the server

1. Build the plugin and addon using the existing Windows workflow in
   [steam-addon.md](steam-addon.md). The build now includes `ano_arcade.xml`,
   `ano_arcade.css` and the three fruit textures. Confirm all resourcecompiler
   calls succeed; CI cannot compile Valve resources.
2. Publish/mount the matching Workshop addon on the test server and a clean
   client. Preserve all existing configuration. Add only these properties to
   `config/core.json`:

   ```json
   {
     "PanoramaMenusEnabled": true,
     "ArcadeAnimationProbeEnabled": true
   }
   ```

3. Restart with the matching plugin. A player with `ano.casino.test` runs
   `!anoarcadetest`. Close other HUD menus before the test.
4. Select Slots, load 500 Test-AP and spin at 100. Check clipping, smooth motion,
   all three staggered stops and correspondence of the final center row with
   the fixed no-win/win sequence.
5. During motion click **Textupdate testen** repeatedly. Check that the update
   at one second and manual updates do not restart, freeze or reset the strip.
6. Repeat at least ten spins (reload test credit as necessary), including fast
   repeated clicks. Test stake limits, insufficient funds, return, close during
   motion and reopen. Wins must settle once, even while closed.
7. With two authorized humans, verify independent balances and simultaneous
   animation. Revoke permissions while open; the next click must release HUD.
8. Disconnect/rejoin, change map and hot reload/unload while spinning. Verify
   no stale overlay, leaked cursor capture or state inherited by another player.
9. Record CS2/CSS/addon versions, compiler output, video and any client console
   errors. Disable `ArcadeAnimationProbeEnabled` after testing. No production
   release or wallet enablement is justified by source checks alone.

If text writes restart native CSS animation, the test has failed. Investigate
independent subtrees and the native HUD transport before implementing the
production wallet. Do not substitute per-frame server rebuilds or claim the
problem is solved from a browser preview.

## Replaceable fruit images

Sources are individual transparent PNGs under
`ui/AnoCore/styles/custom_game/anocore/arcade/`: `cherry.png`, `lemon.png` and
`plum.png`, with matching VTEX descriptors. The initial art is original generated
placeholder fruit, not extracted commercial game art. Replace a PNG under the
same name to restyle it without changing symbol identity. Keep a square canvas,
transparent background, centered subject and comparable visual scale.

Rebuild and publish the addon after a texture change; changing a server PNG alone
does not distribute client textures. Future symbols add an explicit ID-to-CSS
mapping and texture pair. Do not generate an unbounded CSS class per spin or
item. No dynamic Image.src or arbitrary client JavaScript is assumed.

## Profile-linked AP account — follow-up #417

Stable identity is the existing SteamID-backed `PlayerId`, never name or slot.
The profile, dashboard and arcade all read the same durable account. Display
the available account balance separately from funds allocated to the machine.
Loading a user-selected amount conserves total funds; returning funds does too.
Earning AP must use configured, audited sources and idempotent event IDs; neither
login nor opening a game grants recurring free credit. Progression XP remains
independent.

The production model needs accounts, allocations, ledger entries and game
operations. Each operation has an ID, player, immutable rules version, stake,
outcome, payout and revision. Debit, outcome and credit commit transactionally;
duplicate requests replay the recorded result. Compare revisions/lock rows to
prevent simultaneous overspending across servers. Use checked integer AP, bound
stakes/payouts and fail closed on database errors. Client input selects actions,
never balance or outcome. Disconnect and restart recover the recorded operation
without rolling again or awarding twice. AP administration requires permission
and audit. Production tests cover concurrent spends/transfers, rollback,
duplicates, overflow, reconnect and restart recovery.

## Production Triple — after native acceptance

Preserve the approved frame and account/machine/stake/current-spin counters.
Provide a load-amount selector and a return control; the probe's fixed 500 AP
load is not the final selector. Final reel strips, five-line configuration,
symbol set, payouts and bonus behavior require a versioned rules specification
based on the user's reference. The current three-fruit sequence is not a
commercial game's mathematics. Show a transparent payout table once those
rules are settled. Do not introduce Book mode until Triple passes acceptance.

## Case-opening simulator — follow-up #418

The additional arcade entry opens a catalog of simulated cases. A case shows
its AP cost, preview contents and our configured probabilities. After confirming
an opening, the server atomically debits AP and records a collectible grant
before presentation. A horizontal item strip slows beneath a fixed central
pointer and ends at that exact recorded item. Skipping, closing or disconnecting
cannot reroll the result.

Collectibles belong to an independent simulated inventory linked to `PlayerId`.
They are not Steam items or actual weapon-skin ownership. The first version
provides inspect/sort/filter and opening history, without trading, real-money
redemption or automatic AP resale. Configurable case/item IDs select bounded
precompiled image classes and rarity colors. Original placeholder weapon/case
art comes first; the simulator does not claim Valve's real drop probabilities.

Keep catalog content versioned so a later config change cannot alter an already
committed opening. Persist the opening and inventory grant in the same database
transaction, with server-owned randomness and unique operation IDs. Reuse the
animation acceptance tests for consecutive opens, text updates, two-client
isolation and lifecycle recovery.

## Verification record (2026-10-14)

- Arcade module Release build: passed with zero warnings/errors (.NET 10.0.402).
- Offline operator suite: 40 tests, 32 passed, 8 PowerShell-dependent skips.
- Added regression coverage for manual text updates, staggered stop classes and
  delayed old-session disconnects. Native texture/layout contracts cover all
  three fruit images and the middle-row result mapping.
- Full solution Release build now passed with zero warnings/errors after
  restoring dependencies using single-worker MSBuild.
- Regular MSTest: 1,068 passed, 182 skipped without MariaDB, six failures in
  management-pipe tests due to denied Unix sockets in this environment. Casino
  tests pass; CI must still verify database and pipe integration. The earlier
  dependency-restore blocker has been resolved.
- Full dotnet format was also blocked by the environment: its build-host
  named-pipe connection was denied. git diff --check passed.
- Valve resourcecompiler, a CS2 client and the test server are unavailable in
  this workspace. Native compilation/rendering/input/animation acceptance has
  therefore **not** been performed. This remains a test prototype.

## Modular casino packaging

The casino has its own HUD definition and module assembly. AnoMenu is a launcher,
not the owner of reel animation or game state. Keep `AnoCore.Runtime` independent
of Arcade. The composition root wires services; a future optional menu entry
opens the casino and releases the menu's input capture before showing the casino.
Do not show two capturing HUDs simultaneously. Until that handoff is tested,
use `!anomenu` → **Open menu** → **AnoCasino · Test**. The entry appears only
when the probe is enabled and the player is authorized. `!anoarcadetest` remains
available. The production adapter closes the logical menu and reconciles its
HUD before showing the casino, including direct command opens.

A separate Workshop resource package can now be built without veto previews,
tournament layouts or the main menu:

```powershell
powershell -ExecutionPolicy Bypass -File .\ui\AnoCore\build-casino.ps1 -Cs2 "C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive"
```

Default addon name: `anocasino_ui`. Create its Workshop project and mount it
alongside `anomeme_ui` using the server's existing addon loader. This script
compiles the three fruit textures and the standalone arcade layout/styles only.
It does not create/publish a Workshop item or configure MultiAddonManager.
The existing combined build remains supported. Choose exactly one package to
supply the arcade resource paths; publishing both copies risks mount-order
version conflicts. Use a main-menu package built before Arcade inclusion when
mounting the dedicated casino package for the initial test. Native multi-addon
mounting and clean-client downloads remain acceptance gates.

### Project boundaries

- AnoCore: player identity, shared durable AP account contract, permissions,
  menu launcher and HUD transport.
- AnoCasino: slot rules/presentation, coinflip/roulette later, case simulator,
  simulated inventory and game history. No dependency on tournament logic.
- Betting centre: separate future feature/project for fixtures, markets,
  settlement and web UI. Shares identity and the wallet service, never a second
  independent AP balance. Its implementation is outside the animation probe.

Keep the prototype in this repository until native acceptance passes. After
that, extract the optional Casino module and resource project against a
versioned AnoCore API. A separate Workshop addon alone does not make the current
module an independently deployable server plugin: its initialization is still
wired in AnoCore's composition root. Extraction needs module-loader integration,
compatibility tests and independent CI before claiming that capability.

## Menu integration acceptance

Enable the two core flags described above and deploy the matching plugin and
combined Workshop addon for the initial test. Authorized players use
`!anomenu` → Open menu → AnoCasino · Test. Test navigation paging if other
modules push the entry onto a later page. The test currently requires `ano.casino.test`
so testers can be authorized without granting full server administration.
Enabling the feature does not open it to every server player.

Confirm that the main menu vanishes, the casino captures the cursor, and the
first Slots/load/spin clicks work. Reconcile/main-menu refresh must not steal
input after the handoff. Close the casino before reopening AnoMenu. Test two
players independently. The main-menu/casino integration tests are added to
MSTest and now pass with the regular test runner.

### Authorize testers (server console)

The correct **AnoCore** permission is `ano.casino.test`. It is independent of
CSS `@css/root`; editing CSS admins.json alone does not grant this permission.
Run these as separate commands in the server console, replacing the SteamID:

```text
css_anoroledefine casino_tester 0 ano.casino.test
css_anograntrole <SteamID64> casino_tester 0 Casino-Test
```

Repeat only the grant for each tester. Role administration persists and reloads
assignments. Define this dedicated role once; inspect existing role policy
before changing it. To remove access:

```text
css_anorevokerole <SteamID64> casino_tester 0 Test-beendet
```

No database credentials or server configuration are replaced by these steps.

## Spielbare Folgeversion

Die volle Drei-Spiele-Anbindung und das persistente AP-Konto sind separat
unter [casino.md](casino.md) dokumentiert. Aktivierung über `CasinoEnabled`,
Berechtigung `ano.casino.play` und Befehl `anocasino`. Der ursprüngliche
Animationstest bleibt unabhängig und kann deaktiviert bleiben.
