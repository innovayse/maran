<script setup lang="ts">
/**
 * Boolean toggle rendered as a styled box with a text label. The visible box
 * is decoration drawn next to a real, visually hidden `<input type="checkbox">`
 * — never instead of it: the native control keeps the component focusable,
 * space-activated, form-associated and correctly announced (checked state,
 * label, required and invalid flags) with no scripted key handling of our own.
 * Use it for a standalone on/off choice; for a mutually exclusive set use
 * `UiRadioGroup`.
 *
 * LOOK AND MEASUREMENTS. The unchecked box is a `bg-surface-2` square with a
 * `border-border-control` outline, not `border-border-strong`: the box is the ONLY
 * thing that says "tick here", and `--b2` is 1.47:1 against a panel. `--bc` is the
 * token that exists for a control's edge, so the boundary cannot drift with the text
 * tier the way a borrowed `text-text-muted` would. Measured against the surface the
 * box sits on (WCAG 1.4.11 wants 3:1 of a control boundary): dark 3.82 on the page,
 * 3.57 on `--s1`, 3.36 on `--s2`, 3.08 on `--s3`; light 3.39, 3.63, 3.30, 3.07.
 * Checked, the box is `bg-accent-fill` carrying a
 * white check mark at 5.14:1 (`bg-accent` would give 3.89:1 and fail); the fill
 * itself is 3.65:1 against a panel. The whole row is the label, so the click
 * target is the box plus its text, at least 32px tall (WCAG 2.5.8 asks for 24).
 */
import { computed, useId, type ComputedRef } from 'vue'
import UiIcon from './UiIcon.vue'

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
  <div class="flex flex-col gap-1.5">
    <!-- The input sits INSIDE its label so the whole row is one click target, and
         is `sr-only peer` so the real control stays in the accessibility tree and
         the focus order while the sibling box carries the visual state. The
         `for`/`id` pair is kept as well: an explicit association survives a later
         restructure of the wrapper. -->
    <label
      :for="fieldId"
      class="flex min-h-8 w-fit max-w-full items-center gap-2.5 text-sm text-text-primary"
      :class="disabled ? 'cursor-not-allowed opacity-65' : 'cursor-pointer'"
    >
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
        class="inline-flex size-5 shrink-0 items-center justify-center rounded-mark border text-white transition-colors"
        :class="[
          modelValue ? 'border-accent-fill bg-accent-fill' : 'bg-surface-2',
          !modelValue && hasError ? 'border-danger' : '',
          !modelValue && !hasError ? 'border-border-control' : '',
          hasError ? 'peer-focus-visible:shadow-focus-danger' : 'peer-focus-visible:shadow-focus',
        ]"
        aria-hidden="true"
      >
        <UiIcon v-if="modelValue" name="check" size="sm" />
      </span>
      <span class="min-w-0 wrap-anywhere">{{ label }}</span>
    </label>
    <p v-if="hasError" :id="errorId" class="text-sm text-danger wrap-anywhere">{{ error }}</p>
  </div>
</template>
