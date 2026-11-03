<script setup lang="ts">
/**
 * The panel's calendar-date field: a real `<input type="date">` bound to a real `<label>`, drawn
 * exactly like {@link UiInput} (same label, boundary token, focus ring and phone-safe text size).
 *
 * THE VALUE is the calendar date the reader picked, as `YYYY-MM-DD`, or the empty string when the
 * field is cleared. It is a date and never an instant: which instant a day starts at depends on the
 * reader's time zone, and that conversion belongs to whoever sends the value, not to the field.
 *
 * The browser's date picker is its own accessible control (keyboard, screen reader, the reader's
 * locale for the order of day, month and year), which is why this wraps the native element rather
 * than drawing a calendar. No native validation attribute is set (rules/vue.md "Forms: the browser
 * never validates"): a range is checked by the server, whose message is shown.
 */
import { computed, useId, type ComputedRef } from 'vue'

/** Props accepted by {@link UiDateInput}. */
const props = withDefaults(
  defineProps<{
    /** The picked date as `YYYY-MM-DD` (`v-model` target); empty when none is picked. */
    modelValue: string
    /** Visible label text, always rendered. */
    label: string
    /** Disables the native input. A prop, because an attribute on the tag would land on the wrapper. */
    disabled?: boolean
    /** Already-translated validation message; when set, the field renders as invalid (as {@link UiInput}). */
    error?: string | null
  }>(),
  { disabled: false, error: null },
)

/** Events emitted by {@link UiDateInput}. */
const emit = defineEmits<{
  /** Fired on every change, carrying the picked date or the empty string. */
  (e: 'update:modelValue', value: string): void
}>()

/** Links the `<label>` to its `<input>`. */
const inputId: string = useId()

/** Id of the error message element, referenced by `aria-describedby` while the field is invalid. */
const errorId: string = `${inputId}-error`

/** Whether the field is currently in an error state. */
const hasError: ComputedRef<boolean> = computed(() => {
  return props.error !== null && props.error !== undefined
})

/**
 * Forwards the native value, which the browser already gives as `YYYY-MM-DD` or empty.
 * @param event The native input event.
 * @returns Nothing; re-emits synchronously.
 */
const onInput = (event: Event): void => {
  emit('update:modelValue', (event.target as HTMLInputElement).value)
}
</script>

<template>
  <div class="flex flex-col gap-1">
    <label
      :for="inputId"
      class="text-xs font-bold wrap-anywhere"
      :class="hasError ? 'text-danger' : 'text-text-muted'"
      >{{ label }}</label
    >
    <!-- 16px below `sm`, 14px from `sm` up, as UiInput: a phone zooms on a smaller field. -->
    <input
      :id="inputId"
      type="date"
      :value="modelValue"
      :disabled="disabled"
      :aria-invalid="hasError"
      :aria-describedby="hasError ? errorId : undefined"
      class="min-h-control w-full rounded-control border bg-surface-2 px-3 py-1.5 font-mono text-base text-text-primary transition-colors focus-visible:outline-none disabled:cursor-not-allowed disabled:text-text-muted sm:text-sm"
      :class="
        hasError
          ? 'border-danger focus-visible:shadow-field-focus-danger'
          : 'border-border-control enabled:hover:border-border-control-hover focus-visible:border-accent focus-visible:shadow-field-focus disabled:border-border-subtle'
      "
      @input="onInput"
    />
    <p v-if="hasError" :id="errorId" class="text-sm text-danger wrap-anywhere">{{ error }}</p>
  </div>
</template>
