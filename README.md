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
