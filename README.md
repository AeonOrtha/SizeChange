# SizeChange - Delta Suite

A configurable FFXIV Dalamud plugin by **Aeon Ortha**. Grow characters from damage, collect growth into timed pulses, and add proximity-driven growth, bone effects, sounds and VFX.

Based on the initial idea and original SizeChange plugin by [InsubstantialCoder](https://github.com/InsubstantialCoder). This fork's development and expanded features are maintained by [Aeon Ortha](https://github.com/AeonOrtha/SizeChange).

## Getting started

Add this URL to Dalamud's custom plugin repositories, install **SizeChange - Delta Suite**, then use `/sizechange` to open its settings:

```text
https://raw.githubusercontent.com/AeonOrtha/SizeChange/master/repo.json
```

Configure **Self**, **Added Players** or **Monsters** separately. Use **Growth Preview** to simulate damage outside combat without losing real HP.

## What delta growth does

**Delta growth turns health lost into accumulated size.** It reacts to changes in health rather than choosing a size from the character's current health percentage. Healing does not directly undo earned growth; the shrink settings control the return toward normal size.

1. **Take damage.** The fraction of maximum HP lost is multiplied by **Damage Growth Multiplier**. With a multiplier of 2, losing 10% of maximum HP earns +0.2× size before other modifiers or limits.
2. **Accumulate.** With an accumulator delay, incoming growth builds up and releases together when the timer expires. A zero delay releases growth immediately.
3. **Grow and settle.** Growth moves toward the new size. Optional overshoot rises beyond that target, then settles back, with separate rise and settle controls. The visual pulse can exceed the limit and still play at the cap.
4. **Shrink.** Ambient shrink gradually removes earned size; the out-of-combat decay multiplier controls the faster return outside combat. Preview counts as combat while active and allows out-of-combat shrink after it is switched off.

The **HP Loss Cap** limits how much damage a trigger counts. **Limit Delta Growth** caps settled damage-driven growth. **Scale Growth With Size** makes gains proportional to the character's existing settled size, so equivalent hits remain noticeable at larger sizes. Combat-only operation is configurable.

## Immediate growth commands and skills

Use `/deltapulse 0.1` to immediately add **+0.1×** to Self's intended size. It starts the normal lerp, overshoot and configured growth feedback, bypassing the accumulator wait for this input. Existing pending damage remains separate. Self and delta growth must be enabled; the command also works outside combat and can be placed in a skill macro. A macro command is manual: it runs even if the skill beside it fails to cast.

Add `<t>` to pulse your current target: `/deltapulse 0.1 <t>` (also accepts `/deltapulse <t> 0.1`). The target must be alive and match an enabled Self, Added Players, or Monsters delta-growth profile. It uses that profile's size limit, lerp, overshoot and effects; the Skills & Actions toggle is not required. Missing or unconfigured targets produce an error and never fall back to Self. Changes are local to your client.

Switch a profile's **Growth From Delta** setting from an in-game macro:

```text
/deltapulse enable profile self
/deltapulse disable profile self
/deltapulse enable profile others
/deltapulse disable profile others
/deltapulse enable profile monsters
/deltapulse disable profile monsters
```

`others` means the **Added Players** tab, using its existing tracked-player list; `monsters` uses the tracked-monster list. Commands ignore letter case and save the setting, just like the checkbox. Enabling selects delta growth instead of the alternate **Grow From Damage** mode. Disabling follows the existing checkbox behaviour: pending accumulation is cleared and the normal non-delta size behaviour resumes; it does not freeze the current size. The plugin-wide enable switch, Self/Added Players enable switches, lists, limits and effect settings remain unchanged. Configuration commands also work during loading; actual growth still obeys the normal runtime restrictions.

For automatic confirmed triggers, open **Skills & Actions** in any profile, enable it, click **+**, and enter an exact skill name with its decimal growth amount. Names use your game language and ignore case. Each successfully affected, configured recipient receives one immediate contribution per cast, including AoE recipients. Misses, fully resisted/immune effects and interrupted casts do not trigger growth. Healing and buffs can trigger without HP loss. Normal damage growth may contribute alongside this skill bonus. Normal limits, effect cooldowns and shrinking still apply; direct amounts do not use the damage multiplier or feed the digestion reserve.

Multiple action IDs can share a name; those all match that name. Duplicate rule names use the first matching row. This observes action-effect events; periodic status/ground-effect ticks are not separate skill casts. Skill rules are included in profile exports and Self ↔ Added Players imports.

## Chat triggers

In any profile, open **Chat triggers**, enable it, then use **+** to add words or phrases and a decimal growth amount. Every matching occurrence stacks: `grow` at +0.05 and `bigger` at +0.10 makes “grow grow bigger” contribute +0.20 in one immediate pulse. Different matching rows also stack, including overlapping word/phrase rules. Matching ignores case; **Whole words** is on by default. Turning it off allows substrings such as `grow` in `growing`.

Select chat channels and **Anyone**, **Yourself**, or **Others** as speaker. Yourself always means the local player running the plugin. Unknown senders match Anyone only. Say, Tell and Party are selected by default; custom `/em` text can be enabled separately. Echo, system messages and automatic emote log lines are excluded. Only messages delivered to your client are observed.

Growth goes to living characters selected by that profile: Self, tracked Added Players, or tracked Monsters. It requires **Growth From Delta**, uses the existing pulse/effects, and leaves pending damage and its timer untouched. A profile-wide cooldown can discard matching messages for a configured interval; zero accepts every message. **Bypass size limit** is optional and off by default. These settings are included in profile imports/exports. Chat text is processed locally and is not saved or transmitted by this feature.

## Other growth and effects

- **Aetherytes and size drain:** nearby crystals or configured players/NPCs can contribute simulated damage, including outside combat. Proximity growth can exceed the normal damage-growth limit. Size drain has separate duty controls.
- **Digestion reserve:** adds a configurable bonus to proximity growth without reducing the original gain. **Bonus Stored** (0–500%) is the bonus at **Balance Point**. **0.01% HP Boost** multiplies that bonus for small hits, smoothly returning to 1x at the balance point; smaller positive hits retain the maximum boost. Above the balance point, deposits taper to zero at **Zero Deposit At**. Both thresholds still use original growth per contribution; balance/cutoff take priority if the low-HP anchor overlaps them. Storage has no capacity limit. The entire balance drives continuous growth (+2x stored means +2x/s), even while feeding. **Initial Digestion Rate** drains the balance and deflates the stomach; **Digestion Increase / Pulse** adds percentage points/s after each released growth pulse. New deposits keep the current rate; emptying or disabling the reserve resets it. Without further pulses, the last rate continues. Digestion never multiplies growth output. The optional reserve bone's **Full Appearance At** threshold limits only visual expansion.
- **Height and bones:** base and growth-driven height offsets, size-driven bone chains, custom JP bone names and limits, heartbeat pulses, stagger, inverse neck growth and jaw breathing with open/closed holds.
- **Sound and visuals:** growth and accumulator VFX layers, heartbeat sounds, size-driven sound volume/playback rate, and weighted growth-animation choices for players.

## Customize+ and synced players

Bone effects require **Customize+**. Self uses your selected base profile. For synced Added Players, enable **Synced Profile Effects → Intercept Incoming Profiles**, select the players, then request a Lightless reapply/resync to capture their profiles.

Effects are calculated from each captured original profile and each player's own growth state. They remain **local to your view**. **Base Height Offset** is under Synced Profile Effects; the growth-driven addition is under **Shrink & Height**.

### Size-driven profile blend

Under **Bones & Heartbeat → Size-driven profile blend**, select a base and target Customize+ profile, then set **Base at size** and **Target at size**. Defaults are 1× and 3×: at 2×, the transforms are halfway between them. Blending is independent of the Enable Bone Heartbeat checkbox. Use **Refresh choices** to load the available profiles.

Self's **Base Profile** selection is above the blend section; Auto uses the active persistent Customize+ profile. Added Players can use **Intercepted profile (Auto)** for each player's incoming synced appearance or select an explicit local base for the blend. They still require enabled interception and a captured incoming profile. A new incoming sync replaces the saved source; generated frames never become the source.

Settled size determines the target blend, excluding visual overshoot. **Blend transition time (s)** smoothly approaches that target after each growth pulse instead of jumping immediately; default 0.35 seconds, configurable from 0.05 to 10 seconds. It takes about that long to complete 99% of a change and eases the remainder into place. Shrinking and new pulses retarget from the current blend without restarting at the base. The transition changes body shape and the blend's height offsets only; earned size, accumulator timing and growth-pulse timing are unchanged.

The blend interpolates bone scale, effective child scale, position and rotation, then applies size-driven bone growth, custom overrides, reservoir additions, heartbeat pulses, jaw movement and height offsets on top. Shrinking reverses the blend; growth beyond the end size keeps the target shape. Missing bones blend to/from their identity transform. With matching inheritance flags, rotations follow the shorter arc per Euler axis.

**Base profile height offset** and **Target profile height offset** add separate root Y offsets for the two profiles. They interpolate using the same progress as the bones: offsets 0.2 and 0.8 produce 0.5 at halfway. Negative values lower the character. These add to the blended profile's own root position and the existing flat/growth/sit/emote height calculations; they do not replace those controls. Both default to zero, work for Self and Added Players, and are included in profile exports.

When scale inheritance differs, the blend converts each endpoint to the scale actually passed to children (identity for propagation off, parent scale for coupled children, or the explicit independent child scale), then blends that value continuously. Layered bone growth and pulses retain a continuously blended influence on children. Endpoint profiles remain exact. Use compatible skeletons.

Different inheritance flags no longer disable the entire body blend. Customize+ cannot express fractional position/rotation inheritance, so affected components ease to neutral, switch inheritance at neutral, then ease toward the target. A scale-propagation switch that would also move positioned children uses the same neutral handoff. This takes a curved route through neutral rather than a straight interpolation for those components; other bones and the bone's own scale keep blending. Jaw breathing briefly fades at that handoff only if the jaw's rotation inheritance changes. Root height keeps its ordinary interpolation. The status reports inheritance transitions when this handling is needed.

Source profiles are read-only and refreshed about once per second. Missing or invalid blend inputs report **Blend unavailable** and fall back to the normal baseline and existing effects. Turning blending off removes the blend while preserving enabled effects. Disabling the feature/actor still uses the existing temporary-profile ownership and restoration rules.

### Outgoing blend snapshot test

Under **Your Character → Blend Sync (Test)**, enable **Share captured blend via Lightless**. With the Self profile blend active, click **Capture Current Blend**, then trigger an outgoing appearance refresh in Lightless. **Served** must increase and **Last snapshot** must match the captured revision. A resync that only resends cached data will not read a new snapshot; changing a visible equipment piece can trigger an appearance rebuild in the public Lightless version inspected. Leave it changed long enough for the update to finish. Check the result on a paired player's screen.

This first test sends one frozen Customize+ appearance, including the current body blend, bone effects, jaw position and root-height values. Further local growth does not alter it until you capture again and trigger another refresh. Ordinary game draw scale, VFX, sounds, animation timing and growth settings are not included. A snapshot is not a saved Customize+ profile and is never written to your profile files.

Receivers do not need the sender's target profile locally. If they use SizeChange's incoming interception, select the sender under **Added Players → Synced Profile Effects**, then enable **Follow Incoming Appearance**. This bypasses local bone/blend/jaw/reserve/root-height additions for the selected intercepted players, preventing duplicate effects. Their existing whole-character growth simulation remains separate. Without SizeChange interception, Lightless applies the received Customize+ values normally.

**Clear Snapshot** or disable sharing to restore normal future reads, then trigger another outgoing refresh to restore the normal appearance on receivers too. Logout, zoning, disabling the Self blend, invalid local output, and actor replacement clear the snapshot. No network reset is sent automatically: an already received or cached snapshot remains until Lightless processes a new appearance. Captured data is session-only and is not exported.

The outgoing hook is optional and off by default. It wraps the managed `LightlessSync.Interop.Ipc.IpcCallerCustomize.GetScaleAsync(nint)` reader and substitutes a base64 UTF-8 JSON result only for the currently tracked local actor. It uses the existing isolated Harmony runtime; there are no new native hooks, memory writes, server changes, or automatic update notifications. Other actors and unavailable snapshots use the original reader. **Reads/Served** diagnose the local hook; they do not confirm network delivery. Unsupported runtime signatures stop this integration and report their status.

The candidate was verified in public Lightless commit `dc0265f` (1.11.4, 2025-08-30). The current Lightless source host could not be accessed, so installed-version compatibility and delivery still require the in-game test above. Continuous updates and receiver interpolation are deliberately deferred until that test succeeds.

If the hook is unavailable, **Copy Diagnostics** in **Blend Sync (Test)** captures the exact loaded Lightless assembly version, reader method signatures, availability-member types and failure reason. It works while Capture is disabled; **Compatibility Details** shows the same metadata in a collapsed section. The diagnostic reads metadata only, with no profile contents, player identities or runtime field values. Retry reruns the check; it cannot adapt an incompatible method by itself. A missing reader, changed return type and missing availability property now have distinct messages.

## Import and export

Each tab supports JSON import/export. **Self and Added Players exports work in both directions**, including custom bone names and effect settings. Cross-tab imports preserve destination player selections and enable switches. Normal heartbeat imports retain the destination base-profile selection; an enabled profile blend carries its explicit base/target choices and range. Profile choices are local Customize+ IDs, so the referenced profiles must exist on the importing machine. Same-tab imports replace that tab's configuration. Imports reset its live growth and preview; older exports remain supported, with chat triggers and profile blending off. Monster exports stay separate.

## License

[GNU AGPL-3.0-or-later](LICENSE.md).

### Skills and monster animations

In **Skills & Actions**, each row can trigger on **Receiving effect**, **Successful use**, or **Over time**. Successful use grows the caster once per action, even for area attacks. Growth uses the existing immediate pulse, limits and effects; misses and failed attempts do not trigger it.

Expand a name filter in **Added Monsters** to add animation slots. Each slot has a game `.tmb` timeline path, a maximum duration in seconds, and **Is idle**. Idle animations require active accumulation or a recent growth pulse, and hold eligibility for two configured accumulator intervals after the last activity. With zero accumulator delay, this uses two rise-and-settle cycles. They use a maintained base-animation override while stationary and outside combat. Movement releases the idle so locomotion can play; it returns after 0.3 seconds of stillness. Leaving combat also rearms it; growth pulses do not restart an active idle pose. An idle duration of 0 removes the extra duration cap; movement, combat, Stop, or expiry of the accumulation hold still releases it. Other slots play on growth pulses. Multiple slots of the same kind form a random choice pool. Matching uses the existing partial name filters; if filters overlap, the first filter with animation slots wins.

Paths must match a game ActionTimeline entry and suit that monster's model. Duration caps the override; the selected animation must support looping for a continuous idle. Test and Stop buttons act on the first matching nearby monster. The status shows pulse/request counts, the requested timeline and the active base slot. These settings are included in monster profile exports.

**Over time** adds the row's configured growth for each damage-over-time or healing-over-time notification received by the selected profile (Self, Added Players, or Monsters). It uses the existing immediate pulse and size limit. This mode applies to any periodic effect, so the skill-name field is replaced by “Any damage / healing tick”; the first Over time row is used. The game combines many ongoing effects into a single reported tick, which counts once. Separate reported ticks each contribute, including simultaneous damage and healing. Full-health healing still counts if the game sends a tick. Natural regeneration and ordinary HP updates are not counted. Old settings and profile import/export retain their existing behavior.

### Emote growth

Each profile has an **Emotes** section. Enable it, press **+**, choose an emote, then set **Used** or **Received**, **Growth**, and **Every (s)**. Used grows the performer; Received grows the character the emote is explicitly directed at. Used contributes on the first active observation. Received counts each server emote notification directed at that character, including same-emote spam and log-only repeats that do not restart the animation. **Every (s) = 0** makes each received use a one-off contribution; a positive interval also repeats growth while the emote animation remains active. Natural animation loop cycles do not count as new uses. The notification and its animation sample are not counted twice. Separate performers contribute independently. Ending, changing, or losing access to the emote removes its timer. The first matching row per emote and direction is used.

Emote growth follows the selected profile's immediate-growth path, overshoot and feedback settings. Each row has **Bypass size limit**, off by default. When enabled, growth earned by that row can exceed the profile's settled-size limit; ordinary damage, skills and commands retain their normal growth allowance. Earned bypass growth still shrinks normally and is tracked separately from proximity/duty state. The checkbox is included in profile exports and Self ↔ Added Players imports. It also works outside combat. Ordinary chat `/em` text and arbitrary TMB playback do not establish an active game emote. Emote rules are included in profile export/import, including Self ↔ Added Players transfers. Received growth reads the game's emote notifications, with current animation state sustaining timed repeats; Used growth still samples the live emote controller. Optional playback-speed control hooks the game's per-slot speed setter. In-game validation remains necessary for individual emotes and animation replacements.

**Afterglow** now pauses ambient shrink for its active duration in all profiles, both in and out of combat. Shrink resumes at the normal configured rate after afterglow expires. Existing afterglow visuals and bone pulses remain enabled according to their settings.

### Sitting, emote height and playback speed

**Shrink & Height → Sit Height Offset** replaces the normal flat offset while the character sits on the ground or in a chair. Each **Emotes** row also has an optional **Height offset** replacement. Priority is enabled emote height, then enabled sitting height, then normal flat height; these values never stack. Negative values lower the character, and enabled zero is a valid replacement. The existing height added with growth and overshoot remains unchanged. Standing up or ending the emote restores the appropriate normal/sitting value through the existing height route. Self uses Customize+; synced Added Players require their existing profile interception.

Each emote row has **Play speed**: `1` leaves playback unchanged, `0.5` halves it, and the allowed range is `0.1–2`. This changes matching local emote animation slots and restores owned speeds when the emote ends or the setting is disabled. Game speed updates are multiplied at the slot setter, so a normal speed refresh cannot silently cancel the setting. Only registered actors performing their configured emote and matching its timeline are adjusted; other calls pass through unchanged. It does not change growth tick intervals, skill cooldowns or server timing. Game interruptions still end emotes; slowing playback does not extend their server-controlled lifetime. Special replacement timelines that do not match the emote's listed timelines may retain normal speed.

Height and speed always apply to the configured character **performing** the emote, regardless of the row's Used/Received growth direction. The first row for that emote wins for these pose settings. Set **Growth** to zero for height/speed only. All these settings are included in profile exports and Self ↔ Added Players imports.

**Emote speed diagnostics:** when a row uses a speed other than 1, the Emotes section shows waiting, no matching slots, or a matched slot count. Hover for intercepted game-write count, active emote/timeline IDs, and slot speeds. Unavailable errors appear in the same tooltip; Retry retries speed-hook initialization too. The status covers all currently configured actors. A special timeline replacement may not match the emote's listed timelines; include the tooltip and emote name when reporting that case.

**Received emote diagnostics:** the Emotes section shows `Received emotes: listening` when notification capture is active. Hover for the notification count. Retry retries the observer if unavailable. The observer reads the actual recipient in the notification, ignores replay events, and discards stale or pre-zone-change events. Unrelated chat and custom `/em` text are not matched. If enabled while someone is already looping an emote, positive intervals begin timed growth; zero requires a fresh received notification. If the observer is unavailable, animation polling remains a fallback, but cannot count same-emote spam reliably.
