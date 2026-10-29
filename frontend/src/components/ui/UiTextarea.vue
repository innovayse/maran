<script setup lang="ts">
/**
 * The panel's only multi-line text primitive — `UiInput`'s API, one axis
 * bigger. Like `UiInput` it binds a real `<label>` to its `<textarea>` via
 * `for`/`id` (the label is a required prop, never an optional slot) and wires
 * an error state to `aria-invalid`/`aria-describedby`. Used for free-form
 * server-side content such as a cron command or a configuration snippet.
 *
 * It is found the way `UiInput` is, and for the same reason: the decorative borders
 * are 1.2 to 1.5:1 against a panel and the fill (`bg-surface-2`) only 1.06:1, so the
 * boundary is the control-edge token `border-border-control` (`--bc`), 3.57:1 on
 * `--s1`, 3.36 on `--s2` and 3.08 on `--s3` in dark, 3.63 / 3.30 / 3.07 in light (WCAG
 * 1.4.11 asks 3:1). The visible label and the `shadow-focus` ring sit on top of it.
 * Hover steps the edge to `border-text-secondary` (7.08:1 on a dark panel, 6.00 on a
 * light one): that is a 1.98:1 step away from `--bc` in dark and 1.65:1 in light,
 * where the muted tier would move it only 1.50:1 / 1.49:1 — too small to read as a
 * response. See {@link UiInput} for the measured figures and the error-text pairs,
 * which are identical here.
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
  <div class="flex flex-col gap-1.5">
    <label
      :for="fieldId"
      class="text-sm font-medium wrap-anywhere"
      :class="hasError ? 'text-danger' : 'text-text-secondary'"
      >{{ label }}</label
    >
    <!-- 16px below `sm` so a phone browser does not zoom the page on focus; see UiInput. -->
    <textarea
      :id="fieldId"
      :value="modelValue"
      :placeholder="placeholder"
      :rows="rows"
      :aria-required="required ? 'true' : undefined"
      :aria-invalid="hasError"
      :aria-describedby="hasError ? errorId : undefined"
      class="resize-y rounded-control border bg-surface-2 px-3 py-2 text-base leading-normal text-text-primary transition-colors placeholder:text-text-muted focus-visible:outline-none sm:text-sm"
      :class="
        hasError
          ? 'border-danger focus-visible:shadow-focus-danger'
          : 'border-border-control hover:border-text-secondary focus-visible:border-accent focus-visible:shadow-focus'
      "
      @input="onInput"
    />
    <p v-if="hasError" :id="errorId" class="text-sm text-danger wrap-anywhere">{{ error }}</p>
  </div>
</template>
