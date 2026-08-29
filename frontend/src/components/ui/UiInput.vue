<script setup lang="ts">
/**
 * The panel's only text-input primitive. Always binds a real `<label>` to
 * its `<input>` via `for`/`id` so the field is accessible even when the
 * caller forgets — a label is a required prop, not an optional slot — and
 * exposes an error state wired to `aria-invalid`/`aria-describedby` so
 * assistive tech announces validation problems (rules/vue.md: "UI comes
 * from components/ui").
 */
import { computed, useId } from 'vue'
import type { ComputedRef } from 'vue'

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
  }>(),
  { type: 'text', placeholder: undefined, required: false, error: null },
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
const hasError: ComputedRef<boolean> = computed(() => props.error !== null && props.error !== undefined)

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
    <label :for="inputId" class="text-sm font-medium text-slate-900">{{ label }}</label>
    <input
      :id="inputId"
      :type="type"
      :value="modelValue"
      :placeholder="placeholder"
      :aria-required="required ? 'true' : undefined"
      :aria-invalid="hasError"
      :aria-describedby="hasError ? errorId : undefined"
      class="rounded-md border px-3 py-2 text-sm text-slate-900 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600"
      :class="hasError ? 'border-red-400' : 'border-slate-300'"
      @input="onInput"
    />
    <p v-if="hasError" :id="errorId" class="text-sm text-red-700">{{ error }}</p>
  </div>
</template>
