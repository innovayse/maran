<script setup lang="ts">
/**
 * One runtime ceiling of a plan — CPU, memory or processes (issue #133): a figure, or a ticked
 * "No limit". The two are one field with two cases, never a figure that reads zero as "none": the
 * server refuses a limited ceiling of zero and an unlimited one carrying a figure, so the figure
 * is disabled while "No limit" is ticked and is not sent.
 *
 * Dumb by the usual contract: the parent holds the state and the validation, this draws it.
 */
import UiCheckbox from '../ui/UiCheckbox.vue'
import UiInput from '../ui/UiInput.vue'

/** Props accepted by {@link PlanCeilingField}. */
defineProps<{
  /** The figure's label, unit included. */
  label: string
  /** The label of the "no ceiling" box. */
  unlimitedLabel: string
  /** The already-translated validation message for the figure, or `null`. */
  error: string | null
}>()

/** Whether the plan states no ceiling for this resource. */
const unlimited = defineModel<boolean>('unlimited', { required: true })

/** The figure as typed. */
const figure = defineModel<string>('figure', { required: true })
</script>

<template>
  <div class="flex min-w-0 flex-col gap-2">
    <UiInput
      v-model="figure"
      :label="label"
      :error="unlimited ? null : error"
      :disabled="unlimited"
      inputmode="numeric"
      mono
    />
    <UiCheckbox v-model="unlimited" :label="unlimitedLabel" />
  </div>
</template>
