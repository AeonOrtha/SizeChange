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

For automatic confirmed triggers, open **Skills & Actions** in any profile, enable it, click **+**, and enter an exact skill name with its decimal growth amount. Names use your game language and ignore case. Each successfully affected, configured recipient receives one immediate contribution per cast, including AoE recipients. Misses, fully resisted/immune effects and interrupted casts do not trigger growth. Healing and buffs can trigger without HP loss. Normal damage growth may contribute alongside this skill bonus. Normal limits, effect cooldowns and shrinking still apply; direct amounts do not use the damage multiplier or feed the digestion reserve.

Multiple action IDs can share a name; those all match that name. Duplicate rule names use the first matching row. This observes action-effect events; periodic status/ground-effect ticks are not separate skill casts. Skill rules are included in profile exports and Self ↔ Added Players imports.

## Other growth and effects

- **Aetherytes and size drain:** nearby crystals or configured players/NPCs can contribute simulated damage, including outside combat. Proximity growth can exceed the normal damage-growth limit. Size drain has separate duty controls.
- **Digestion reserve:** stores part of proximity growth and releases it after leaving the sources. An optional bone expands with the stored amount.
- **Height and bones:** base and growth-driven height offsets, size-driven bone chains, custom JP bone names and limits, heartbeat pulses, stagger, inverse neck growth and jaw breathing with open/closed holds.
- **Sound and visuals:** growth and accumulator VFX layers, heartbeat sounds, size-driven sound volume/playback rate, and weighted growth-animation choices for players.

## Customize+ and synced players

Bone effects require **Customize+**. Self uses your selected base profile. For synced Added Players, enable **Synced Profile Effects → Intercept Incoming Profiles**, select the players, then request a Lightless reapply/resync to capture their profiles.

Effects are calculated from each captured original profile and each player's own growth state. They remain **local to your view**. **Base Height Offset** is under Synced Profile Effects; the growth-driven addition is under **Shrink & Height**.

## Import and export

Each tab supports JSON import/export. **Self and Added Players exports work in both directions**, including custom bone names and effect settings. Cross-tab imports preserve destination player selections, enable switches and base-profile selection. Same-tab imports replace that tab's configuration. Imports reset its live growth and preview; older exports remain supported. Monster exports stay separate.

## License

[GNU AGPL-3.0-or-later](LICENSE.md).

### Skills and monster animations

In **Skills & Actions**, each row can trigger on **Receiving effect**, **Successful use**, or **Over time**. Successful use grows the caster once per action, even for area attacks. Growth uses the existing immediate pulse, limits and effects; misses and failed attempts do not trigger it.

Expand a name filter in **Added Monsters** to add animation slots. Each slot has a game `.tmb` timeline path, a maximum duration in seconds, and **Is idle**. Idle animations require active accumulation or a recent growth pulse, and hold eligibility for two configured accumulator intervals after the last activity. With zero accumulator delay, this uses two rise-and-settle cycles. They use a maintained base-animation override while stationary and outside combat. Movement releases the idle so locomotion can play; it returns after 0.3 seconds of stillness. Leaving combat also rearms it; growth pulses do not restart an active idle pose. An idle duration of 0 removes the extra duration cap; movement, combat, Stop, or expiry of the accumulation hold still releases it. Other slots play on growth pulses. Multiple slots of the same kind form a random choice pool. Matching uses the existing partial name filters; if filters overlap, the first filter with animation slots wins.

Paths must match a game ActionTimeline entry and suit that monster's model. Duration caps the override; the selected animation must support looping for a continuous idle. Test and Stop buttons act on the first matching nearby monster. The status shows pulse/request counts, the requested timeline and the active base slot. These settings are included in monster profile exports.

**Over time** adds the row's configured growth for each damage-over-time or healing-over-time notification received by the selected profile (Self, Added Players, or Monsters). It uses the existing immediate pulse and size limit. This mode applies to any periodic effect, so the skill-name field is replaced by “Any damage / healing tick”; the first Over time row is used. The game combines many ongoing effects into a single reported tick, which counts once. Separate reported ticks each contribute, including simultaneous damage and healing. Full-health healing still counts if the game sends a tick. Natural regeneration and ordinary HP updates are not counted. Old settings and profile import/export retain their existing behavior.

### Emote growth

Each profile has an **Emotes** section. Enable it, press **+**, choose an emote, then set **Used** or **Received**, **Growth**, and **Every (s)**. Used grows the performer; Received grows the character the emote is explicitly directed at. The first observation contributes once. **Every (s) = 0** makes that a one-off pulse; a positive interval repeats growth for as long as the game reports that emote active. Loop restarts do not add extra ticks. Separate performers contribute independently. Ending, changing, or losing access to the emote removes its timer. The first matching row per emote and direction is used.

Emote growth follows the selected profile's immediate-growth path, size limit, overshoot and feedback settings. It also works outside combat. Ordinary chat `/em` text and arbitrary TMB playback do not establish an active game emote. Emote rules are included in profile export/import, including Self ↔ Added Players transfers. Growth detection reads the live emote controller; optional playback-speed control hooks the game's per-slot speed setter. In-game validation remains necessary for individual emotes and animation replacements.

**Afterglow** now pauses ambient shrink for its active duration in all profiles, both in and out of combat. Shrink resumes at the normal configured rate after afterglow expires. Existing afterglow visuals and bone pulses remain enabled according to their settings.

### Sitting, emote height and playback speed

**Shrink & Height → Sit Height Offset** replaces the normal flat offset while the character sits on the ground or in a chair. Each **Emotes** row also has an optional **Height offset** replacement. Priority is enabled emote height, then enabled sitting height, then normal flat height; these values never stack. Negative values lower the character, and enabled zero is a valid replacement. The existing height added with growth and overshoot remains unchanged. Standing up or ending the emote restores the appropriate normal/sitting value through the existing height route. Self uses Customize+; synced Added Players require their existing profile interception.

Each emote row has **Play speed**: `1` leaves playback unchanged, `0.5` halves it, and the allowed range is `0.1–2`. This changes matching local emote animation slots and restores owned speeds when the emote ends or the setting is disabled. Game speed updates are multiplied at the slot setter, so a normal speed refresh cannot silently cancel the setting. Only registered actors performing their configured emote and matching its timeline are adjusted; other calls pass through unchanged. It does not change growth tick intervals, skill cooldowns or server timing. Game interruptions still end emotes; slowing playback does not extend their server-controlled lifetime. Special replacement timelines that do not match the emote's listed timelines may retain normal speed.

Height and speed always apply to the configured character **performing** the emote, regardless of the row's Used/Received growth direction. The first row for that emote wins for these pose settings. Set **Growth** to zero for height/speed only. All these settings are included in profile exports and Self ↔ Added Players imports.

**Emote speed diagnostics:** when a row uses a speed other than 1, the Emotes section shows waiting, no matching slots, or a matched slot count. Hover for intercepted game-write count, active emote/timeline IDs, and slot speeds. Unavailable errors appear in the same tooltip; Retry retries speed-hook initialization too. The status covers all currently configured actors. A special timeline replacement may not match the emote's listed timelines; include the tooltip and emote name when reporting that case.
