<script setup lang="ts">
/**
 * The accent icon square of the redesign: a glyph in the accent colour on the accent wash, in a
 * rounded tile. It marks what a card, a row or an empty state is about, and it was written out
 * by hand, class for class, in a dozen screens before it was a component — which is how two of
 * them came to differ by a pixel and a third by a radius.
 *
 * **Decorative, always.** The tile sits beside a title or a label that names the thing, so it is
 * hidden from assistive technology rather than given a second name (rules/vue.md: a decorative
 * icon is declared, not omitted). A control whose only content is an icon is not a tile; it is a
 * button with its own accessible name.
 *
 * **Where it may sit — a measured limit, not a taste.** `--act` text on the `--acs` wash clears
 * 4.5:1 over the panel surface (`--s1`) and over the raised surface (`--s2`), which is every place
 * the panel draws one. Over `--s3` the same pair measures 4.40:1 and fails, so a tile must not be
 * put on a pressed or deeper surface; use the neutral `bg-surface-3` square of a quick-action row
 * there instead. The wash is translucent, so the tile takes the surface beneath it and the limit
 * is a property of that surface, not of this component.
 *
 * Pass `name` for a plain glyph. A caller whose glyph is composed by someone else (the empty state
 * receives its icon through a slot) passes the default slot instead, and `name` is then ignored.
 */
import { computed, type ComputedRef } from 'vue'
import UiIcon, { type UiIconName, type UiIconSize } from './UiIcon.vue'

/**
 * The named tile steps. A tile is chosen by role: `sm` inside a dense row or a table cell, `md`
 * beside a card heading or on a figure card, `lg` for the one glyph that leads a whole block.
 */
export type UiIconTileSize = 'sm' | 'md' | 'lg'

/**
 * Each step's tile edge and the icon step drawn inside it. The glyph grows with the tile so the
 * margin around it stays proportionate: a large glyph in a small tile touches the edge, a small
 * one in a large tile reads as lost.
 */
const STEPS: Record<UiIconTileSize, { tile: string; glyph: UiIconSize }> = {
  sm: { tile: 'size-8', glyph: 'md' },
  md: { tile: 'size-10', glyph: 'md' },
  lg: { tile: 'size-12', glyph: 'lg' },
}

/** Props accepted by {@link UiIconTile}. */
const props = withDefaults(
  defineProps<{
    /** Which glyph to draw; omit it when the glyph arrives through the default slot. */
    name?: UiIconName
    /** Which step of the tile scale to draw at. */
    size?: UiIconTileSize
  }>(),
  { name: undefined, size: 'md' },
)

/**
 * Slots exposed by {@link UiIconTile}.
 * @property default A glyph composed by the caller, drawn instead of `name`.
 */
defineSlots<{
  default?: () => unknown
}>()

/** The step's classes and glyph size for the requested {@link UiIconTileSize}. */
const step: ComputedRef<{ tile: string; glyph: UiIconSize }> = computed(() => {
  return STEPS[props.size]
})
</script>

<template>
  <span
    class="grid shrink-0 place-items-center rounded-control bg-accent-soft text-accent-text"
    :class="step.tile"
    aria-hidden="true"
  >
    <slot>
      <UiIcon v-if="name !== undefined" :name="name" :size="step.glyph" />
    </slot>
  </span>
</template>
