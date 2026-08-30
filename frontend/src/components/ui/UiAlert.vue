<script setup lang="ts">
/**
 * Inline status/error banner. Used to render server-provided text verbatim
 * (rules/vue.md: "the backend owns their text") as well as frontend-owned
 * chrome messages — the primitive itself holds no copy of its own.
 *
 * The design has no standalone alert banner, so this reuses the one tinted
 * strip it does draw — the bulk-selection bar: 8px/12px padding, an 8px
 * radius, a 1px border in the tone's own colour over a wash of it. The error
 * tone borrows the destructive button's wash for the same reason.
 */
import { computed, type ComputedRef } from 'vue'

/** Props accepted by {@link UiAlert}. */
const props = withDefaults(
  defineProps<{
    /** Severity, controlling color and the ARIA live-region politeness. */
    variant?: 'info' | 'error'
  }>(),
  { variant: 'info' },
)

/** Tailwind utility classes for the selected {@link UiAlert} variant. */
const variantClasses: ComputedRef<string> = computed(() =>
  props.variant === 'error'
    ? 'border-[rgb(229_72_77/0.35)] bg-[rgb(229_72_77/0.12)] text-danger'
    : 'border-[rgb(46_123_255/0.35)] bg-accent-soft text-text-primary',
)
</script>

<template>
  <!-- assertive for errors so screen readers interrupt; polite otherwise. -->
  <div
    class="rounded-lg border px-3 py-2 text-xs leading-normal"
    :class="variantClasses"
    role="status"
    :aria-live="variant === 'error' ? 'assertive' : 'polite'"
  >
    <slot />
  </div>
</template>
