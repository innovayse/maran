<script setup lang="ts">
/**
 * Indeterminate loading indicator. Announced to assistive tech via
 * `role="status"` and a caller-supplied, already-translated label — the
 * spinner itself owns no text, so it carries no i18n key of its own.
 *
 * The design spins a 12px stroked arc at 0.8s inside its "Working" button;
 * this draws the same arc as a bordered circle, sunken ring plus one accent
 * quadrant, at the same size and speed.
 *
 * Under `prefers-reduced-motion` the arc slows rather than stopping: unlike a
 * skeleton, a spinner that holds still reads as a frozen interface, so the
 * signal has to survive the accommodation.
 */

/** Props accepted by {@link UiSpinner}. */
defineProps<{
  /** Accessible label describing what is loading (translated by the caller). */
  label: string
}>()
</script>

<template>
  <span role="status" class="inline-flex items-center gap-2 text-xs text-text-secondary">
    <span
      class="ui-spinner-arc size-3 animate-[spin_0.8s_linear_infinite] rounded-full border-2 border-border-strong border-t-accent"
      aria-hidden="true"
    />
    <span class="sr-only">{{ label }}</span>
  </span>
</template>

<style scoped>
/* Slowed, not halted — see the component's doc comment. */
@media (prefers-reduced-motion: reduce) {
  .ui-spinner-arc {
    animation-duration: 2.4s;
  }
}
</style>
