<script setup lang="ts">
/**
 * The set of checkboxes an owner ticks to say which gated features a sub-user may use. Dumb by the
 * usual contract: the feature list and the ticked ids come in, the new ticked ids go out. The ids and
 * labels are the panel's (`GET /api/v1/me/sub-users/features`); this component knows no feature by
 * name.
 */
import { useI18n } from 'vue-i18n'
import UiCheckbox from '../ui/UiCheckbox.vue'
import type { GrantableFeature } from '../../types/subUser'

/** Props accepted by {@link SubUserFeaturePicker}. */
const props = defineProps<{
  /** Every feature the owner may give, in the panel's order. */
  features: GrantableFeature[]
  /** The ids currently ticked. */
  modelValue: string[]
}>()

/** Events emitted by {@link SubUserFeaturePicker}. */
const emit = defineEmits<{
  /** The ticked ids changed. */
  (e: 'update:modelValue', value: string[]): void
}>()

const { t } = useI18n()

/**
 * Ticks or unticks one feature and reports the new set, keeping the panel's order.
 * @param id The feature that was toggled.
 * @param ticked Whether it is now ticked.
 * @returns Nothing; emits synchronously.
 */
const toggle = (id: string, ticked: boolean): void => {
  const next = props.features
    .map((feature) => {
      return feature.id
    })
    .filter((candidate) => {
      return candidate === id ? ticked : props.modelValue.includes(candidate)
    })
  emit('update:modelValue', next)
}
</script>

<template>
  <div role="group" :aria-label="t('subUsers.features.groupLabel')" class="flex flex-col gap-1">
    <UiCheckbox
      v-for="feature in features"
      :key="feature.id"
      :model-value="modelValue.includes(feature.id)"
      :label="feature.label"
      @update:model-value="toggle(feature.id, $event)"
    />
  </div>
</template>
