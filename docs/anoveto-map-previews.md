# AnoVeto map images

AnoVeto's eight clickable cards select a prepared image by **Workshop ID** for
Workshop maps and by a stable hash of the exact **MapId** for other maps. Renaming
DisplayName does not change that identity. A new vote replaces the previous image
classes independently for each player; close, completion, cancellation and expiry
clear them. Existing voting, permissions, eight-map snapshots, map loading and
tournament integration remain authoritative.

## Why the pictures are prepared before the addon build

The CounterStrikeSharp custom-HUD API exposes text, class and input-capture
updates, but no image-source setter. The current custom-HUD validator accepts
static Image sources and rejects JavaScript. Consequently a preview_url cannot
be sent as a dialog variable and treated as a working image. AnoCore generates
local textures plus CSS classes and uses the supported per-player SetHasClass
path. Missing artwork leaves a neutral dark image area beside the actual map name;
Workshop maps never receive unrelated default-map artwork.

Sources checked for #331:

- [Valve GetPublishedFileDetails](https://partner.steamgames.com/doc/webapi/ISteamRemoteStorage#GetPublishedFileDetails): POST, itemcount and publishedfileids; no publisher key required for this method.
- [CounterStrikeSharp custom HUD extensions](https://github.com/roflmuffin/CounterStrikeSharp/blob/main/managed/CounterStrikeSharp.API/Modules/Extensions/CCSCustomHudLayoutExtensions.cs): the native class/text/input update boundary used by AnoCore.
- [Source2Toolkit authoring guidance](https://www.source2toolkit.net/docs/panorama/authoring): current custom-HUD validator limits and static-image/class workaround.

## Prepare the configured map catalog

Use the **same AnoCore maps.json** loaded by the server. Do not substitute a list
of Workshop IDs for maps.json: configured map IDs and names must be retained.
The tool accepts its case-insensitive Maps/DisplayName/MapId/WorkshopId properties,
checks duplicate identities, and bounds one build to 128 maps.

From the repository root or the extracted development package:

```powershell
py -m pip install -r tools/veto-preview-requirements.txt
py tools/prepare_veto_previews.py --maps "C:\server-config-copy\maps.json" --output "C:\AnoCore-preview-build" --cache "C:\AnoCore-preview-cache"
```

Choose a new output directory for every preparation. The tool refuses an existing
output and never changes maps.json, existing server configuration, permissions,
rank points or player data. The cache must be a dedicated preparer directory.

Workshop metadata uses batches of at most 50 IDs, a five-second request timeout,
an approximately 45-second total network budget and a 1 MiB response limit.
Only successful records for the requested item and CS2 app 730 are eligible.
The configured display name is kept; WorkshopTitle and PreviewUrl are diagnostic
metadata in manifest.json. Positive metadata is cached for 24 hours; failures for
five minutes. --offline uses existing cache without contacting Steam.

Preview downloads accept HTTPS Steam CDN hosts only, also across redirects;
credentials, fragments, custom ports and unsafe URLs are rejected. Images are
limited to 5 MiB and 16 million pixels, normalized from PNG/JPEG/WebP into PNG,
and scaled to at most 512×288. The dedicated cache is pruned to at most 256 files
and 320 MiB. API errors, exhausted network budget, removed/private/wrong-app items,
bad URLs and bad images produce neutral cards. Generated CSS contains only local
s2r resources; no remote URL, script, arbitrary command or player identifier is
sent to a client.

For non-Workshop maps, optionally supply a JSON file mapping exact map IDs to
**real local images**, relative to that JSON file:

```json
{ "de_dust2": "images/dust2.png", "de_mirage": "images/mirage.png" }
```

Pass --standard-images "C:\map-artwork\images.json". This input cannot override
Workshop artwork. Supply images you are permitted to distribute; the repository
does not invent or bundle third-party map previews.

## Build and deliver the UI addon

```powershell
.\ui\AnoCore\build.ps1 -Cs2 "C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive" -PreviewSource "C:\AnoCore-preview-build"
```

The build compiles the generated .vtex descriptors and preview stylesheet before
the AnoVeto layout. Without PreviewSource it compiles the neutral stylesheet.
The existing -InstallLocalClient option includes preview resources for a local
development test. Normal players need the rebuilt addon through the existing
Workshop delivery path; see [steam-addon.md](steam-addon.md). Plugin installation
alone cannot distribute these client assets.

Rebuild/re-upload when changing Workshop IDs, exact non-Workshop map IDs or
artwork. A maps live reload still pins the current vote's map snapshot; the next
vote uses the new catalog. An unprepared new map correctly shows no image until
the matching addon is delivered.

## Verification status and manual acceptance

Automated tests cover cross-language identity, replacement/cleanup and per-player
isolation; actual Workshop metadata/image preparation using mocked bounded HTTP;
offline reuse, negative cache, URL/redirect rejection, corrupt metadata/images,
explicit standard images, immutable configuration and output protection.

CS2 Workshop compilation, current-engine rendering and two-client delivery have
not been run here. Native tests remain manually skipped by project preference;
#331 stays open for that acceptance. When running it later:

1. Prepare the real catalog and inspect manifest.json: every pictured Workshop map
   has its own requested ID, app-730 metadata and genuine PreviewUrl.
2. Build/re-upload the existing addon and join with two clean clients, without
   InstallLocalClient masking the Workshop download. Confirm all prepared images
   match their map names and an unprepared/removed map has a neutral card.
3. Open on both clients, vote/close independently, cancel and start another vote
   with a different map order. Confirm no stale image, wrong vote identity or
   shared cursor/visibility state.
4. Check disconnect/reconnect, map change, hot reload and expiry release input;
   verify selected Workshop map loading and the existing tournament flow.

Do not call image delivery or native acceptance complete solely because CI passed.
