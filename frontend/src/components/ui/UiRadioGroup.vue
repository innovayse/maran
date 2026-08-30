<script setup lang="ts">
/**
 * Mutually exclusive choice between a small, fully visible set of options —
 * the form counterpart of `UiSelect`, which is for longer lists. The group
 * owns the selected value and renders its own `UiRadio` children, so callers
 * never have to keep a `name` in sync by hand.
 *
 * It renders a real `<fieldset>`/`<legend>`: that pairing is what names the
 * group for a screen reader, and the shared radio `name` gives the ARIA
 * radiogroup keyboard contract (one tab stop, arrow keys move and select)
 * natively.
 */
import { computed, useId, type ComputedRef } from 'vue'
import UiRadio from './UiRadio.vue'

/**
 * One choice offered by {@link UiRadioGroup}. Declared here rather than borrowed
 * from `UiSelect`: a component's own types live in its own file (rules/vue.md),
 * and sharing one shape would tie two primitives together that only happen to
 * look alike today.
 *
 * Values and labels are supplied by the caller — the SPA never invents domain
 * data, so a list of plans or tiers arrives from the backend already localized.
 */
export interface RadioOption {
  /** Stable machine value bound through `v-model` when this choice is picked. */
  value: string
  /** Ready-to-render text for the choice; the primitive holds no copy of its own. */
  label: string
  /** Whether the choice is present but not choosable. */
  disabled?: boolean
}

/** Props accepted by {@link UiRadioGroup}. */
const props = withDefaults(
  defineProps<{
    /** Value of the currently selected option (`v-model` target); empty string when nothing is selected. */
    modelValue: string
    /** Visible group label, rendered as the fieldset's legend. */
    legend: string
    /** Choices to offer, already localized by the caller or by the backend. */
    options: readonly RadioOption[]
    /** Disables every choice in the group. */
    disabled?: boolean
    /**
     * Marks the group required for assistive technology. Rendered as `aria-required`, not the
     * native `required` attribute: forms are `novalidate` (rules/vue.md).
     */
    required?: boolean
    /** Already-translated validation message; when set, the group renders as invalid. */
    error?: string | null
  }>(),
  { disabled: false, required: false, error: null },
)

/** Events emitted by {@link UiRadioGroup}. */
const emit = defineEmits<{
  /** Fired when the selection changes, carrying the newly selected option's value. */
  (e: 'update:modelValue', value: string): void
}>()

/** Group name shared by every radio in this instance, plus the ids its legend and error message are announced under. */
const groupName: string = useId()
const errorId: string = `${groupName}-error`
const legendId: string = `${groupName}-legend`

/** Whether the group is currently in an error state. */
const hasError: ComputedRef<boolean> = computed(() => props.error !== null && props.error !== undefined)

/**
 * Publishes the newly selected option's value.
 * @param value The value of the option the user picked.
 * @returns Nothing; re-emits synchronously.
 */
const onSelect = (value: string): void => {
  emit('update:modelValue', value)
}
</script>

<template>
  <!-- `role="radiogroup"` is not decoration: a bare `<fieldset>` maps to `group`,
       and `group` supports neither `aria-required` nor `aria-invalid`, so both
       flags below would be dropped by assistive technology. Overriding the role
       also overrides the legend-based name, hence the explicit `aria-labelledby`. -->
  <fieldset
    role="radiogroup"
    class="flex flex-col gap-2 border-0 p-0"
    :aria-labelledby="legendId"
    :aria-required="required ? 'true' : undefined"
    :aria-invalid="hasError"
    :aria-describedby="hasError ? errorId : undefined"
  >
    <legend
      :id="legendId"
      class="mb-1 text-xs font-medium"
      :class="hasError ? 'text-danger' : 'text-text-secondary'"
    >
      {{ legend }}
    </legend>
    <UiRadio
      v-for="option in options"
      :key="option.value"
      :value="option.value"
      :label="option.label"
      :name="groupName"
      :checked="option.value === modelValue"
      :disabled="disabled || option.disabled === true"
      @select="onSelect"
    />
    <p v-if="hasError" :id="errorId" class="text-xs text-danger">{{ error }}</p>
  </fieldset>
</template>
