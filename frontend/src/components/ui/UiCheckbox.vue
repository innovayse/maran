<script setup lang="ts">
/**
 * Boolean toggle rendered as a styled box with a text label. The visible box
 * is decoration drawn next to a real, visually hidden `<input type="checkbox">`
 * — never instead of it: the native control keeps the component focusable,
 * space-activated, form-associated and correctly announced (checked state,
 * label, required and invalid flags) with no scripted key handling of our own.
 * Use it for a standalone on/off choice; for a mutually exclusive set use
 * `UiRadioGroup`.
 */
import { computed, useId, type ComputedRef } from 'vue'

/** Props accepted by {@link UiCheckbox}. */
const props = withDefaults(
  defineProps<{
    /** Whether the box is checked (`v-model` target). */
    modelValue: boolean
    /** Visible label text, always rendered — the box alone names nothing. */
    label: string
    /** Disables the control and marks it non-interactive for assistive tech. */
    disabled?: boolean
    /**
     * Marks the field required for assistive technology. Rendered as `aria-required`, not the
     * native `required` attribute: forms are `novalidate` (rules/vue.md), so the browser's own
     * bubbles must never appear.
     */
    required?: boolean
    /** Already-translated validation message; when set, the control renders as invalid. */
    error?: string | null
  }>(),
  { disabled: false, required: false, error: null },
)

/** Events emitted by {@link UiCheckbox}. */
const emit = defineEmits<{
  /** Fired when the user toggles the box, carrying the new checked state. */
  (e: 'update:modelValue', value: boolean): void
}>()

/** Stable, unique id pair for this instance's `<label for>`/`<input id>` and error association. */
const fieldId: string = useId()
const errorId: string = `${fieldId}-error`

/** Whether the control is currently in an error state. */
const hasError: ComputedRef<boolean> = computed(() => {
  return props.error !== null && props.error !== undefined
})

/**
 * Forwards the native checked state to the `update:modelValue` emit.
 * @param event The native change event.
 * @returns Nothing; re-emits synchronously.
 */
const onChange = (event: Event): void => {
  emit('update:modelValue', (event.target as HTMLInputElement).checked)
}
</script>

<template>
  <div class="flex flex-col gap-1">
    <div class="flex items-center gap-2">
      <!-- `sr-only peer` keeps the real control in the accessibility tree and the
           focus order while the sibling span carries the visual state. -->
      <input
        :id="fieldId"
        type="checkbox"
        class="peer sr-only"
        :checked="modelValue"
        :disabled="disabled"
        :aria-required="required ? 'true' : undefined"
        :aria-invalid="hasError"
        :aria-describedby="hasError ? errorId : undefined"
        @change="onChange"
      />
      <span
        class="inline-flex size-3.5 shrink-0 items-center justify-center rounded-sm border text-white transition-colors peer-focus-visible:border-accent peer-focus-visible:shadow-focus peer-disabled:opacity-65"
        :class="[
          modelValue ? 'border-accent bg-accent' : 'bg-surface-2',
          hasError && !modelValue ? 'border-[rgb(229_72_77/0.5)]' : '',
          !modelValue && !hasError ? 'border-border-strong' : '',
        ]"
        aria-hidden="true"
      >
        <svg v-if="modelValue" class="size-2.5" viewBox="0 0 20 20" fill="none" stroke="currentColor">
          <path d="M5 10l4 4 6-8" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round" />
        </svg>
      </span>
      <label
        :for="fieldId"
        class="text-base text-text-primary"
        :class="disabled ? 'cursor-not-allowed opacity-65' : 'cursor-pointer'"
        >{{ label }}</label
      >
    </div>
    <p v-if="hasError" :id="errorId" class="text-base text-danger">{{ error }}</p>
  </div>
</template>
