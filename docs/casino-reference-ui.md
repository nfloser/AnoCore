# Native Casino reference adaptation (#472)

This test branch adapts interface concepts to CS2 Panorama. It does not embed a
browser, React, Canvas or the Web Animations API. No third-party source code,
images, fonts, rules or payouts are copied. Existing ANOMEME textures and native
Panel/Label/Button contracts remain the only presentation dependencies.

## References and choices

| Reference | Concept used | Native adaptation |
| --- | --- | --- |
| [HTML5 Slot Machine](https://github.com/johakr/html5-slot-machine/blob/main/src/js/Reel.js) | A symbol ribbon ending with the chosen result, staggered reel duration | Fixed double strips; three booked symbols followed by a decorative tail; downward ease-out into the booked grid |
| [slots-js](https://github.com/HorrellTech/slots-js) | Independent reel stops and winning-line feedback | Preserve five result-derived Slots line markers and staggered stop times |
| [roulette](https://github.com/pjhjohn/roulette) | Explicit angular motion and deceleration | Preserve the existing five forward wheel laps with bounded angular keyframes and server-result orientation |
| [Blackjack](https://github.com/Saganaki22/Blackjack) | Animated card dealing and clear table areas | Three bounded native card rows: dealer and up to two player hands; active-hand border, suit colours and changed-card entrances |
| [FlipDrop](https://github.com/miller-dota/FlipDrop) | Choose result before animation and land on that face | Preserve existing server-targeted native coin toss and repeated-spin variants |
| [Momentum Panorama](https://github.com/momentum-mod/panorama) | Dedicated Panorama layouts/styles instead of browser CSS assumptions | Named CSS positioning classes; inert button decorations; no inline style attributes |

The HTML5 reel source was inspected; the other projects are conceptual references
from their repository descriptions/READMEs. Their visual quality, performance and
compatibility have not been measured in CS2. Licenses must be checked again if a
future change actually copies upstream code or assets.

## Behaviour

- Slots: fast downward motion switches to a 450 ms booked-result ribbon before
  stops at 2.6/3.1/3.6 seconds. Alternating animation names support repeated spins.
  Existing line reveal and AP settlement stay unchanged.
- Book: complete 16-symbol strips loop downward, eight symbols per lap; five
  reels settle for 450 ms before the existing 2.0/2.2/2.4/2.6/2.8-second stops.
  The bonus summary shows free spins and collected payout after motion finishes.
- Blackjack: card rows reserve 21 slots each to match the stored-state bound.
  Empty slots collapse. Only changed cards enter; a new Start clears presentation
  identity so a repeated dealt face can animate again. The dealer hole card emits
  only `?`, never rank, suit or colour. Both split hands remain visible, with
  active-hand emphasis. There is no client deck, random outcome or AP mutation.
- Risk: held amount, next amount and five completed-step indicators replace a
  text-only intermediate result. Result amounts stay concealed during motion.
- Decorative children of Casino buttons have `hittest="false"`, following the
  registered-button routing contract used by the Veto action fix.

## Validation and native acceptance

Python operator checks include XML/resource completeness, forbidden inline-style
attributes, repeated strip alignment, bounded card rows, inert input children
and alternating animation names. Added fake-clock presenter regressions cover
settle phases, repeated Book spins and hidden/cleared Blackjack cards.

This environment has no .NET SDK, PowerShell, Valve resourcecompiler or running
CS2. The local source/operator checks are available; .NET build/test/format,
MariaDB integration, PowerShell checks and the CI package remain required.
Native acceptance must verify each game opens, all controls work, two consecutive
spins land correctly, split hands fit, dealer hole cards remain concealed,
close/reopen/reconnect preserve AP and round state, and Veto fixes still work.

Do not publish Workshop resources before local-client acceptance. Rebuild the
matching plugin and addon together for #472; the #473 inline-style fix alone
requires only recompiling the Slots XML/CSS resources.

## Rollback

Baseline: `11a78e895d817bd7e1eb5f45bcd2326f2bd96cfe` (PR #471).
Keep the #473 inline-style repair while testing #472: revert the presentation
commits only, then build matching binaries and resources from that revision.
Never restore the rejected inline-style Slots XML to the client.
No schema migration, config replacement or AP data rollback is needed.

## Native reel timing experiment (#492)

The native report shows Book jumping from its initial to final image. The cause
has not been confirmed in CS2. Book and new Biggest Boy rounds now use a separate
200 ms preparation phase before enabling the animation class on a later presenter
update. Input remains blocked and no outcome is redrawn or settled by this phase.
The Casino presentation timer runs every 50 ms, so the 200 ms preparation and
staggered stops are quantized to that interval rather than the former 250 ms.
This is not a promise of an exact client paint delay; no per-frame drawing is
performed by the server.

Each looping strip moves down by one 80 px symbol over about 100 ms, then holds
for about 20 ms. Eight Book steps take 0.96 seconds; eleven Biggest Boy steps take
1.32 seconds. Both strip halves are identical, so loop wrap preserves symbol order.
The 0.45 second result settling and 0.2 second staggered reel stops are preserved.
The pauses live in native CSS keyframes; they add no per-frame server traffic.

Native acceptance: compile the matching addon and replace the matching plugin;
open Book, run two consecutive paid/bonus spins, then repeat with Biggest Boy.
Check whether symbols visibly cross cell boundaries before the stop. If the
client still renders only the first and last frame, this experiment has not
solved interpolation and must remain unaccepted. Record a short clip and any
Panorama parser errors before changing timings again. Compare the same client,
addon and server build. No Workshop publication or production merge before this
native check. Existing 5x5 legacy strips are retained separately.
