<script setup lang="ts">
/**
 * The panel's only button primitive. Renders a real `<button>` so focus,
 * keyboard activation and `disabled` semantics come for free; every other
 * screen composes this instead of a raw `<button>` (rules/vue.md: "UI comes
 * from components/ui").
 */
import { computed } from 'vue'
import type { ComputedRef } from 'vue'

/** Props accepted by {@link UiButton}. */
const props = withDefaults(
  defineProps<{
    /** Visual weight: `primary` for the main action, `secondary` for a bordered alternative, `ghost` for a low-emphasis action (e.g. a nav-adjacent control). */
    variant?: 'primary' | 'secondary' | 'ghost'
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

/** Tailwind utility classes for the selected {@link UiButton} variant. */
const variantClasses: ComputedRef<string> = computed(() => {
  switch (props.variant) {
    case 'secondary':
      return 'border border-slate-300 bg-white text-slate-900 hover:bg-slate-50'
    case 'ghost':
      return 'text-slate-700 hover:bg-slate-100'
    case 'primary':
    default:
      return 'bg-blue-600 text-white hover:bg-blue-700'
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
  <button
    :type="type"
    :disabled="disabled"
    class="inline-flex items-center justify-center rounded-md px-3 py-2 text-sm font-medium transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600 disabled:cursor-not-allowed disabled:opacity-50"
    :class="variantClasses"
    @click="onClick"
  >
    <slot />
  </button>
</template>
