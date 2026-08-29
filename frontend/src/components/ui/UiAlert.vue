<script setup lang="ts">
/**
 * Inline status/error banner. Used to render server-provided text verbatim
 * (rules/vue.md: "the backend owns their text") as well as frontend-owned
 * chrome messages — the primitive itself holds no copy of its own.
 */
import { computed } from 'vue'
import type { ComputedRef } from 'vue'

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
    ? 'border-red-200 bg-red-50 text-red-800'
    : 'border-slate-200 bg-slate-50 text-slate-800',
)
</script>

<template>
  <!-- assertive for errors so screen readers interrupt; polite otherwise. -->
  <div
    class="rounded-md border p-3 text-sm"
    :class="variantClasses"
    role="status"
    :aria-live="variant === 'error' ? 'assertive' : 'polite'"
  >
    <slot />
  </div>
</template>
