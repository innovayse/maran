<script setup lang="ts">
/**
 * Indeterminate loading indicator. Announced to assistive tech via
 * `role="status"` and a caller-supplied, already-translated label — the
 * spinner itself owns no text, so it carries no i18n key of its own. The label
 * is visually hidden (`sr-only`) and the arc is `aria-hidden`: the accessible
 * name is the label and nothing else, so a screen reader does not read out a
 * decorative ring as well.
 *
 * It draws a stroked arc: a sunken ring with one accent quadrant, spinning.
 * The accent is a graphic here (a ring needs 3:1 against the surface, and the
 * accent core clears that on a panel in both themes), never text.
 *
 * Under `prefers-reduced-motion` the arc slows rather than stopping: unlike a
 * skeleton, a spinner that holds still reads as a frozen interface, so the
 * signal has to survive the accommodation. The rotation is declared in this
 * component's own stylesheet, not as a utility, so the slower speed for the
 * reduced-motion case is one rule next to the one it overrides rather than a
 * second utility fighting the first for the cascade.
 */

/** Props accepted by {@link UiSpinner}. */
defineProps<{
  /** Accessible label describing what is loading (translated by the caller). */
  label: string;
}>();
</script>

<template>
  <span role="status" class="inline-flex items-center gap-2 text-sm text-text-secondary">
    <span class="ui-spinner-arc size-4 rounded-full border-2 border-border-strong border-t-accent" aria-hidden="true" />
    <span class="sr-only">{{ label }}</span>
  </span>
</template>

<style scoped>
.ui-spinner-arc {
  animation: ui-spinner-turn 0.8s linear infinite;
}

/* Slowed, not halted — see the component's doc comment. */
@media (prefers-reduced-motion: reduce) {
  .ui-spinner-arc {
    animation-duration: 2.4s;
  }
}

@keyframes ui-spinner-turn {
  to {
    transform: rotate(360deg);
  }
}
</style>
