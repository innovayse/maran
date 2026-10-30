<script setup lang="ts">
/**
 * The line that says a list is narrowed and offers the way back: "Only alpha · Show all". A list
 * opened from a link that carries a filter in its URL must say so, or the operator reads a short
 * list as the whole of it.
 *
 * A kit component rather than markup repeated on every list page that takes a URL filter, so the
 * wording's layout, the separator and the clearing action look and announce the same everywhere.
 * Both texts are required props, already translated by the caller.
 */
import UiButton from './UiButton.vue'

/** Props accepted by {@link UiActiveFilter}. */
defineProps<{
  /** What the list is narrowed to, already translated ("Only alpha"). */
  text: string
  /** The clearing action's label, already translated ("Show all"). */
  clearLabel: string
}>()

/** Events emitted by {@link UiActiveFilter}. */
const emit = defineEmits<{
  /** The operator asked for the unfiltered list. */
  (e: 'clear'): void
}>()

/**
 * Forwards the clearing action to the page, which owns the URL.
 * @returns Nothing.
 */
const clear = (): void => {
  emit('clear')
}
</script>

<template>
  <div class="flex flex-wrap items-center gap-x-3 gap-y-2" data-testid="active-filter">
    <span class="text-sm text-text-secondary wrap-anywhere">{{ text }}</span>
    <span class="text-sm text-text-muted" aria-hidden="true">·</span>
    <UiButton variant="secondary" @click="clear">{{ clearLabel }}</UiButton>
  </div>
</template>
