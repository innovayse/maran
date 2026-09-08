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
 * The design draws it as a dashed-border panel on the raised surface with a
 * deliberately large 76px of vertical air — the emptiness is the message, so
 * the panel is not allowed to look like a collapsed row — then a 46px icon
 * tile, a 15px/600 title, a 12.5px secondary body capped at 400px so the
 * sentence wraps into a readable column rather than the full table width, and
 * the actions set 4px below.
 */

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
    class="flex flex-col items-center gap-2.5 rounded-xl border border-dashed border-border-strong bg-surface-1 px-6 py-20 text-center"
  >
    <!-- The tile is drawn only when a caller supplies a glyph: an empty bordered
         square would read as a missing image rather than as decoration. -->
    <div
      v-if="$slots.icon"
      class="grid size-11.5 place-items-center rounded-xl border border-border-subtle bg-surface-2 text-text-muted"
    >
      <slot name="icon" />
    </div>
    <component :is="titleAs" class="text-lg font-semibold text-text-primary">{{ title }}</component>
    <p v-if="description" class="max-w-[400px] text-base leading-normal text-text-secondary">
      {{ description }}
    </p>
    <div v-if="$slots.default" class="mt-2 flex gap-2">
      <slot />
    </div>
  </div>
</template>
