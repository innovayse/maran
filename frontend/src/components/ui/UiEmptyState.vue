<script setup lang="ts">
/**
 * Placeholder content for a screen with nothing to show — an unknown route,
 * an empty list, a not-yet-populated panel. Renders a title, an optional
 * description, and a slot for actions (e.g. a `UiNavLink` back home).
 *
 * The title is an emphasized paragraph by default and a real `<h1>` only when
 * the caller says so through `titleAs` — see that prop for why the default is
 * the un-promoted one.
 *
 * It is drawn as a dashed-border panel on the panel surface with deliberately
 * large vertical air — the emptiness is the message, so the panel must not
 * look like a collapsed row — then an optional icon tile, the title, a body
 * capped at 400px so the sentence wraps into a readable column rather than the
 * full table width, and the actions below. The dashed edge is decoration (it
 * is far under the 3:1 a boundary would need) and nothing relies on it: the
 * title and description say what is empty.
 *
 * The title is sentence case, not the uppercase of a page title: it is a
 * message ("No sites yet"), and a sentence set in capitals reads as shouting
 * and is slower to read. It still wraps anywhere, since it may be the
 * translated name of a thing in Armenian or a server-sent name.
 */
import UiIconTile from './UiIconTile.vue'

/**
 * The element the title is rendered as. `'p'` is the emphasized paragraph the
 * panel has always drawn; `'h1'` is the same text promoted to the document's
 * top-level heading.
 */
export type UiEmptyStateTitleAs = 'p' | 'h1'

/** Props accepted by {@link UiEmptyState}. */
withDefaults(
  defineProps<{
    /** Short heading naming the empty condition (already translated by the caller). */
    title: string
    /** Optional longer explanation (already translated by the caller). */
    description?: string
    /**
     * Which element carries the title, and therefore whether the title joins
     * the page's outline. Defaults to `'p'`, because in nineteen of the
     * twenty places this component is drawn it sits INSIDE a page that
     * already opened with its own `<h1>` — a table with no rows, a chart with
     * no points, a tab with nothing in it. There the title names a region,
     * not the screen, and promoting it would give those pages a second
     * top-level heading.
     *
     * `'h1'` exists for the one case where the empty state IS the whole page:
     * `NotFoundPage`, which has no other title and was consequently the only
     * routed page in the panel with no heading at all.
     *
     * This is deliberately NOT the drift-freezing kind of prop that
     * {@link ./UiSectionHeading.vue} refuses (a `size`, or a free `level`).
     * Both values render the SAME class string, so the prop cannot express a
     * typography decision and no call site can drift a step away from another
     * through it; it changes the accessibility tree and nothing else. And the
     * choice is not a matter of taste that each caller re-decides — it is
     * determined by one fact the component cannot see, whether a page heading
     * already exists above it. `'h2'` and below are absent on purpose: a
     * subordinate heading has no consumer yet, and
     * {@link ./UiSectionHeading.vue} is the component that would own it.
     */
    titleAs?: UiEmptyStateTitleAs
  }>(),
  { description: undefined, titleAs: 'p' },
)

/**
 * Slots exposed by {@link UiEmptyState}.
 * @property icon Optional glyph for the tile above the title; purely decorative, so the caller marks it `aria-hidden`.
 * @property default Actions offered to escape the empty state.
 */
defineSlots<{
  icon?: () => unknown
  default?: () => unknown
}>()
</script>

<template>
  <div
    class="flex min-w-0 flex-col items-center gap-3 rounded-panel border border-dashed border-border-strong bg-surface-1 px-6 py-16 text-center"
  >
    <!-- The tile is drawn only when a caller supplies a glyph: an empty tinted
         square would read as a missing image rather than as decoration. The
         accent text sits on the accent wash over the panel surface (--s1),
         never over --s3, where that pairing measures under 4.5:1. -->
    <UiIconTile v-if="$slots.icon" size="lg">
      <slot name="icon" />
    </UiIconTile>
    <component :is="titleAs" class="font-display text-lg font-extrabold text-text-primary wrap-anywhere">{{
      title
    }}</component>
    <p v-if="description" class="max-w-100 text-sm leading-normal text-text-secondary wrap-anywhere">
      {{ description }}
    </p>
    <div v-if="$slots.default" class="mt-1 flex flex-wrap justify-center gap-2">
      <slot />
    </div>
  </div>
</template>
