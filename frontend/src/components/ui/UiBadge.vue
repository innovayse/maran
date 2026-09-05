<script setup lang="ts">
/**
 * Small status label. Used wherever a short state has to be scannable inside a
 * dense list (an account's lifecycle state, a locked navigation entry, a task
 * outcome). The badge always renders its text, so color is never the only
 * carrier of meaning — an accessibility requirement, not a style preference.
 * It owns no copy: callers pass already-translated text (frontend chrome) or
 * backend-localized text through the default slot.
 *
 * The geometry is the design's `badge` helper exactly — 10.5px, 600 weight,
 * .03em tracking, 2px/7px padding, a 5px radius, uppercase — and the tones are
 * its `sBadge` map. Uppercase is applied by CSS rather than by the caller so
 * the underlying text stays translatable and copyable in its own casing.
 */
import { computed, type ComputedRef } from 'vue'

/**
 * Severity/tone of a {@link UiBadge}. Declared here rather than in `src/types/`
 * because it describes this component's props and means nothing without it
 * (rules/vue.md); exported because feature components map their own domain
 * state onto it — an account status, a task outcome.
 */
export type BadgeVariant = 'neutral' | 'success' | 'warning' | 'danger' | 'info' | 'ai'

/** Props accepted by {@link UiBadge}. */
const props = withDefaults(
  defineProps<{
    /** Tone of the badge, controlling its background and text color. */
    variant?: BadgeVariant
  }>(),
  { variant: 'neutral' },
)

/**
 * Tailwind utility classes for the selected {@link UiBadge} variant. The washes
 * are the design's literal alpha values; no token names them, so they are
 * written as arbitrary color values rather than approximated with a token.
 */
const variantClasses: ComputedRef<string> = computed(() => {
  switch (props.variant) {
    case 'success':
      return 'bg-[rgb(47_181_116/0.13)] text-success'
    case 'warning':
      return 'bg-[rgb(224_160_48/0.14)] text-warning'
    case 'danger':
      return 'bg-[rgb(229_72_77/0.13)] text-danger'
    case 'info':
      return 'bg-accent-soft text-accent'
    case 'ai':
      return 'bg-violet-soft text-violet'
    case 'neutral':
    default:
      return 'bg-surface-3 text-text-muted'
  }
})
</script>

<template>
  <span
    class="inline-flex items-center rounded-md px-2.5 py-1 text-sm font-semibold tracking-wide uppercase"
    :class="variantClasses"
  >
    <slot />
  </span>
</template>
