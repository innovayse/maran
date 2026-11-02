<script setup lang="ts">
/**
 * The features a plan sells, drawn as one checkbox per feature the panel offers (issue #316). Every
 * unticked box is a feature the plan leaves out: its customers lose the menu item and the panel
 * refuses their requests for it.
 *
 * Dumb by the usual contract: the parent holds the selection, this draws it and reports each
 * toggle. The choices are the panel's own list and their names arrive already translated, so the
 * field holds no feature name of its own (rules/architecture.md "The backend owns the data").
 * While the list is on its way, or when it could not be read, the field says so instead of
 * drawing an empty group that would read as "this plan sells nothing".
 */
import UiCheckbox from '../ui/UiCheckbox.vue'
import type { LoadingState } from '../../types/loading'
import type { PlanFeatureChoice } from '../../types/plan'

/** Props accepted by {@link PlanFeaturesField}. */
defineProps<{
  /** The group's accessible name. */
  label: string
  /** The features the panel offers, or `null` before they arrived and after the read failed. */
  choices: PlanFeatureChoice[] | null
  /** Where the read of the choices stands. */
  state: LoadingState
  /** The features currently ticked, by name. */
  selected: readonly string[]
  /** The sentence shown while the choices are on their way. */
  loadingText: string
  /** The sentence shown when the choices could not be read. */
  unavailableText: string
}>()

/** Events emitted by {@link PlanFeaturesField}. */
const emit = defineEmits<{
  /** Fired when the administrator ticks or clears one feature, carrying its name and the new state. */
  (e: 'toggle', feature: string, ticked: boolean): void
}>()

/**
 * Reports one box being toggled.
 * @param feature The feature's name on the wire.
 * @param ticked Whether the box is now ticked.
 * @returns Nothing; re-emits synchronously.
 */
const toggle = (feature: string, ticked: boolean): void => {
  emit('toggle', feature, ticked)
}
</script>

<template>
  <div role="group" :aria-label="label" class="grid gap-x-4 gap-y-1 sm:grid-cols-2 xl:grid-cols-3">
    <template v-if="choices !== null">
      <UiCheckbox
        v-for="choice in choices"
        :key="choice.feature"
        :model-value="selected.includes(choice.feature)"
        :label="choice.displayName"
        @update:model-value="(ticked: boolean) => toggle(choice.feature, ticked)"
      />
    </template>
    <p v-else-if="state === 'loading'" class="text-sm text-text-muted sm:col-span-2 xl:col-span-3">
      {{ loadingText }}
    </p>
    <p v-else class="text-sm text-text-muted sm:col-span-2 xl:col-span-3">{{ unavailableText }}</p>
  </div>
</template>
