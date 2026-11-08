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
 * boundary is the control-edge token `border-border-control` (`--bc`), 3.98:1 on
 * `--s1`, 3.74 on `--s2` and 3.43 on `--s3` in dark, 3.79 / 3.43 / 3.20 in light (WCAG
 * 1.4.11 asks 3:1). The visible label and the `shadow-focus` ring sit on top of it.
 * Hover steps the edge one notch stronger to `border-border-control-hover`, the same step
 * on every control. Focus turns the edge `border-accent` and `shadow-field-focus` adds a
 * crisp 1px ring of the same colour (a 2px solid edge, no halo); the danger state does the
 * same in `danger`. See {@link UiInput} for the measured figures and the error-text pairs,
 * which are identical here.
 *
 * LOADING. Inside a {@link UiForm} with `loading`, the label stays (it is the form's real text)
 * and the control is replaced by a {@link UiSkeleton} of its resting four-row footprint, so the
 * loaded field lands in the same space.
 */
import { computed, useId, type ComputedRef } from 'vue'
import { useFormLoading } from '../../composables/useFormLoading'
import UiSkeleton from './UiSkeleton.vue'

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
    /**
     * Draws the text in the monospace face, for machine text whose columns must line up (a file
     * being edited, a command, a configuration snippet). A real prop: a class on the tag would
     * land on the wrapper `div`, not the `<textarea>`.
     */
    mono?: boolean
    /**
     * Keeps long lines on one line and scrolls the field sideways instead of wrapping them, so a
     * file's own line breaks are the only ones shown. Off by default: prose wraps.
     */
    noWrap?: boolean
    /**
     * Refuses typing, for a field whose text is not in yet (a file still being read): an edit made
     * before the text arrives would be silently replaced by it. A real prop: a `disabled` attribute
     * on the tag would land on the wrapper `div` and do nothing.
     */
    disabled?: boolean
  }>(),
  { placeholder: undefined, rows: 4, required: false, error: null, mono: false, noWrap: false, disabled: false },
)

/** Events emitted by {@link UiTextarea}. */
const emit = defineEmits<{
  /** Fired on every input, carrying the field's new value. */
  (e: 'update:modelValue', value: string): void
}>()

/** Stable, unique id pair for this instance's `<label for>`/`<textarea id>` and error association. */
const fieldId: string = useId()
const errorId: string = `${fieldId}-error`

/** Whether the enclosing form is still loading, so the control is drawn as a placeholder. */
const formLoading = useFormLoading()

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
      :for="formLoading ? undefined : fieldId"
      class="text-xs font-bold wrap-anywhere"
      :class="hasError ? 'text-danger' : 'text-text-muted'"
      >{{ label }}</label
    >
    <!-- 16px below `sm` so a phone browser does not zoom the page on focus; see UiInput. -->
    <UiSkeleton v-if="formLoading" shape="block" control="area" />
    <textarea
      v-else
      :id="fieldId"
      :value="modelValue"
      :placeholder="placeholder"
      :rows="rows"
      :disabled="disabled"
      :aria-required="required ? 'true' : undefined"
      :aria-invalid="hasError"
      :aria-describedby="hasError ? errorId : undefined"
      :wrap="noWrap ? 'off' : undefined"
      class="resize-y rounded-control border disabled:cursor-not-allowed disabled:text-text-muted disabled:opacity-100 bg-surface-2 px-3 py-1.5 text-base leading-normal text-text-primary transition-colors placeholder:text-text-muted focus-visible:outline-none sm:text-sm"
      :class="[
        mono ? 'font-mono' : '',
        hasError
          ? 'border-danger focus-visible:shadow-field-focus-danger'
          : 'border-border-control enabled:hover:border-border-control-hover focus-visible:border-accent focus-visible:shadow-field-focus',
      ]"
      @input="onInput"
    />
    <p v-if="hasError" :id="errorId" class="text-sm text-danger wrap-anywhere">{{ error }}</p>
  </div>
</template>
