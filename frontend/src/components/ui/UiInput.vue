<script setup lang="ts">
/**
 * The panel's only text-input primitive. Always binds a real `<label>` to
 * its `<input>` via `for`/`id` so the field is accessible even when the
 * caller forgets — a label is a required prop, not an optional slot — and
 * exposes an error state wired to `aria-invalid`/`aria-describedby` so
 * assistive tech announces validation problems (rules/vue.md: "UI comes
 * from components/ui").
 */
import { computed, useId, type ComputedRef } from 'vue'

/** Props accepted by {@link UiInput}. */
const props = withDefaults(
  defineProps<{
    /** Current field value (`v-model` target). */
    modelValue: string
    /** Visible label text, always rendered — never omitted for a placeholder instead. */
    label: string
    /** Native `type` attribute. */
    type?: 'text' | 'email' | 'password'
    /** Placeholder text shown when the field is empty. */
    placeholder?: string
    /**
     * Marks the field required for assistive technology. Rendered as `aria-required`, not the
     * native `required` attribute: forms are `novalidate` (rules/vue.md), so the browser's own
     * bubbles must never appear — they are unstyled, untranslatable, and would compete with the
     * field's own error message.
     */
    required?: boolean
    /** Already-translated validation message; when set, the field renders as invalid. */
    error?: string | null
    /**
     * Native `autocomplete` token. A real prop rather than a fallthrough attribute: this
     * component renders a wrapper `div`, so an attribute written on the tag would land on the
     * wrapper and never reach the input. Password managers rely on it, and a sign-in form that
     * cannot be filled by one pushes people towards passwords they can retype from memory.
     */
    autocomplete?: string
  }>(),
  { type: 'text', placeholder: undefined, required: false, error: null, autocomplete: undefined },
)

/** Events emitted by {@link UiInput}. */
const emit = defineEmits<{
  /** Fired on every input, carrying the field's new value. */
  (e: 'update:modelValue', value: string): void
}>()

/** Stable, unique id pair for this instance's `<label for>`/`<input id>` and error association. */
const inputId: string = useId()
const errorId: string = `${inputId}-error`

/** Whether the field is currently in an error state. */
const hasError: ComputedRef<boolean> = computed(() => {
  return props.error !== null && props.error !== undefined
})

/**
 * Forwards the native input value to the `update:modelValue` emit.
 * @param event The native input event.
 * @returns Nothing; re-emits synchronously.
 */
const onInput = (event: Event): void => {
  emit('update:modelValue', (event.target as HTMLInputElement).value)
}
</script>

<template>
  <div class="flex flex-col gap-1">
    <!-- The label turns red with the field: in the design an invalid field is
         readable as invalid from the label down, not only from the border. -->
    <label
      :for="inputId"
      class="text-base font-medium"
      :class="hasError ? 'text-danger' : 'text-text-secondary'"
      >{{ label }}</label
    >
    <input
      :id="inputId"
      :type="type"
      :value="modelValue"
      :placeholder="placeholder"
      :autocomplete="autocomplete"
      :aria-required="required ? 'true' : undefined"
      :aria-invalid="hasError"
      :aria-describedby="hasError ? errorId : undefined"
      class="rounded-lg border bg-surface-2 px-2 py-1.5 text-base text-text-primary placeholder:text-text-muted focus-visible:outline-none"
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
