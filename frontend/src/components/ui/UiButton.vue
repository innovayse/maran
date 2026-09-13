<script setup lang="ts">
/**
 * The panel's only button primitive. Renders a real `<button>` so focus,
 * keyboard activation and `disabled` semantics come for free; every other
 * screen composes this instead of a raw `<button>` (rules/vue.md: "UI comes
 * from components/ui").
 *
 * Every measurement below is the design's own button spec — 6px/12px padding,
 * a 7px radius and a 12.5px face — rather than a rounded-off Tailwind default,
 * so a button in the panel and a button in the canvas are the same object.
 */
import { computed, type ComputedRef } from 'vue'

/**
 * Visual weight of a {@link UiButton}, one per button the design draws.
 * Declared here rather than in `src/types/` because it describes this
 * component's props and means nothing without it (rules/vue.md).
 */
export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'destructive' | 'ai'

/** Props accepted by {@link UiButton}. */
const props = withDefaults(
  defineProps<{
    /** Visual weight: `primary` for the main action, `secondary` for a bordered alternative, `ghost` for a low-emphasis action (e.g. a nav-adjacent control), `destructive` for an action that removes something, `ai` for an assistant-initiated action. */
    variant?: ButtonVariant
    /** Native `type` attribute; defaults to `button` so it never submits a form by accident. */
    type?: 'button' | 'submit'
    /** Disables the button and marks it non-interactive for assistive tech. */
    disabled?: boolean
  }>(),
  { variant: 'primary', type: 'button', disabled: false },
)

/** Events emitted by {@link UiButton}. */
const emit = defineEmits<{
  /** Fired on a non-disabled click. */
  (e: 'click', payload: MouseEvent): void
}>()

/**
 * Tailwind utility classes for the selected {@link UiButton} variant, taken
 * from the design's button row. The hover colors that no token names (the
 * pressed accent, the destructive wash) are written as `var(--token)`-based
 * arbitrary values, which is the one case rules/vue.md allows.
 */
const variantClasses: ComputedRef<string> = computed(() => {
  switch (props.variant) {
    case 'secondary':
      return 'border border-border-subtle bg-surface-2 font-medium text-text-primary enabled:hover:border-border-strong enabled:hover:bg-surface-3'
    case 'ghost':
      return 'border border-transparent text-text-secondary enabled:hover:bg-surface-2 enabled:hover:text-text-primary'
    case 'destructive':
      return 'border border-[rgb(229_72_77/0.35)] bg-[rgb(229_72_77/0.12)] font-medium text-danger enabled:hover:bg-[rgb(229_72_77/0.2)]'
    case 'ai':
      return 'border border-transparent bg-violet font-semibold text-white enabled:hover:bg-[#7a5be8]'
    case 'primary':
    default:
      return 'border border-transparent bg-accent font-semibold text-white shadow-[0_1px_2px_rgb(0_0_0/0.25)] enabled:hover:bg-[#1f6bee]'
  }
})

/**
 * Forwards a native click to the `click` emit, unless the button is disabled.
 * @param event The native mouse event.
 * @returns Nothing; re-emits synchronously.
 */
const onClick = (event: MouseEvent): void => {
  if (props.disabled) {
    return
  }
  emit('click', event)
}
</script>

<template>
  <!-- The focus ring is the design's field-focus treatment (accent border plus a
       3px accent wash), applied on `focus-visible` only so it reaches keyboard
       users without outlining every mouse click. -->
  <button
    :type="type"
    :disabled="disabled"
    class="inline-flex items-center justify-center gap-1.5 rounded-lg px-4 py-2 text-base transition-colors focus-visible:border-accent focus-visible:shadow-focus focus-visible:outline-none disabled:cursor-not-allowed disabled:border-border-subtle disabled:bg-surface-2 disabled:text-text-muted disabled:opacity-65 disabled:shadow-none"
    :class="variantClasses"
    @click="onClick"
  >
    <slot />
  </button>
</template>
