<script setup lang="ts">
/**
 * The panel's only multi-line text primitive — `UiInput`'s API, one axis
 * bigger. Like `UiInput` it binds a real `<label>` to its `<textarea>` via
 * `for`/`id` (the label is a required prop, never an optional slot) and wires
 * an error state to `aria-invalid`/`aria-describedby`. Used for free-form
 * server-side content such as a cron command or a configuration snippet.
 */
import { computed, useId, type ComputedRef } from 'vue'

/** Props accepted by {@link UiTextarea}. */
const props = withDefaults(
  defineProps<{
    /** Current field value (`v-model` target). */
    modelValue: string
    /** Visible label text, always rendered — never omitted for a placeholder instead. */
    label: string
    /** Placeholder text shown when the field is empty. */
    placeholder?: string
    /** Number of visible text rows; the field still grows with the layout, this is its resting height. */
    rows?: number
    /**
     * Marks the field required for assistive technology. Rendered as `aria-required`, not the
     * native `required` attribute: forms are `novalidate` (rules/vue.md), so the browser's own
     * bubbles must never appear — they are unstyled, untranslatable, and would compete with the
     * field's own error message.
     */
    required?: boolean
    /** Already-translated validation message; when set, the field renders as invalid. */
    error?: string | null
  }>(),
  { placeholder: undefined, rows: 4, required: false, error: null },
)

/** Events emitted by {@link UiTextarea}. */
const emit = defineEmits<{
  /** Fired on every input, carrying the field's new value. */
  (e: 'update:modelValue', value: string): void
}>()

/** Stable, unique id pair for this instance's `<label for>`/`<textarea id>` and error association. */
const fieldId: string = useId()
const errorId: string = `${fieldId}-error`

/** Whether the field is currently in an error state. */
const hasError: ComputedRef<boolean> = computed(() => {
  return props.error !== null && props.error !== undefined
})

/**
 * Forwards the native textarea value to the `update:modelValue` emit.
 * @param event The native input event.
 * @returns Nothing; re-emits synchronously.
 */
const onInput = (event: Event): void => {
  emit('update:modelValue', (event.target as HTMLTextAreaElement).value)
}
</script>

<template>
  <div class="flex flex-col gap-1">
    <label
      :for="fieldId"
      class="text-base font-medium"
      :class="hasError ? 'text-danger' : 'text-text-secondary'"
      >{{ label }}</label
    >
    <textarea
      :id="fieldId"
      :value="modelValue"
      :placeholder="placeholder"
      :rows="rows"
      :aria-required="required ? 'true' : undefined"
      :aria-invalid="hasError"
      :aria-describedby="hasError ? errorId : undefined"
      class="resize-y rounded-lg border bg-surface-2 px-2 py-1.5 text-base leading-normal text-text-primary placeholder:text-text-muted focus-visible:outline-none"
      :class="
        hasError
          ? 'border-[rgb(229_72_77/0.5)] focus-visible:shadow-focus-danger'
          : 'border-border-subtle focus-visible:border-accent focus-visible:shadow-focus'
      "
      @input="onInput"
    />
    <p v-if="hasError" :id="errorId" class="text-base text-danger">{{ error }}</p>
  </div>
</template>
