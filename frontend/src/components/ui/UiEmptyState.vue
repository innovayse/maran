<script setup lang="ts">
/**
 * Placeholder content for a screen with nothing to show — an unknown route,
 * an empty list, a not-yet-populated panel. Renders a heading, an optional
 * description, and a slot for actions (e.g. a `UiNavLink` back home).
 *
 * The design draws it as a dashed-border panel on the raised surface with a
 * deliberately large 76px of vertical air — the emptiness is the message, so
 * the panel is not allowed to look like a collapsed row — then a 46px icon
 * tile, a 15px/600 title, a 12.5px secondary body capped at 400px so the
 * sentence wraps into a readable column rather than the full table width, and
 * the actions set 4px below.
 */

/** Props accepted by {@link UiEmptyState}. */
defineProps<{
  /** Short heading naming the empty condition (already translated by the caller). */
  title: string
  /** Optional longer explanation (already translated by the caller). */
  description?: string
}>()

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
    class="flex flex-col items-center gap-2.5 rounded-xl border border-dashed border-border-strong bg-surface-1 px-6 py-19 text-center"
  >
    <!-- The tile is drawn only when a caller supplies a glyph: an empty bordered
         square would read as a missing image rather than as decoration. -->
    <div
      v-if="$slots.icon"
      class="grid size-11.5 place-items-center rounded-xl border border-border-subtle bg-surface-2 text-text-muted"
    >
      <slot name="icon" />
    </div>
    <p class="text-sm font-semibold text-text-primary">{{ title }}</p>
    <p v-if="description" class="max-w-[400px] text-xs leading-normal text-text-secondary">
      {{ description }}
    </p>
    <div v-if="$slots.default" class="mt-1 flex gap-2">
      <slot />
    </div>
  </div>
</template>
