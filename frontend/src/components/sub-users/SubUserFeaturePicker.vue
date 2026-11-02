<script setup lang="ts">
/**
 * The set of checkboxes an owner ticks to say which gated features a sub-user may use. Dumb by the
 * usual contract: the feature list and the ticked ids come in, the new ticked ids go out. The ids and
 * labels are the panel's (`GET /api/v1/me/sub-users/features`); this component knows no feature by
 * name.
 */
import { useI18n } from 'vue-i18n'
import UiCheckbox from '../ui/UiCheckbox.vue'
import type { AccountFeatureName } from '../../types/accountFeature'
import type { GrantableFeature } from '../../types/subUser'

/** Props accepted by {@link SubUserFeaturePicker}. */
const props = defineProps<{
  /** Every feature the owner may give, in the panel's order. */
  features: GrantableFeature[]
  /** The ids currently ticked. */
  modelValue: AccountFeatureName[]
}>()

/** Events emitted by {@link SubUserFeaturePicker}. */
const emit = defineEmits<{
  /** The ticked ids changed. */
  (e: 'update:modelValue', value: AccountFeatureName[]): void
}>()

const { t } = useI18n()

/**
 * Ticks or unticks one feature and reports the new set, keeping the panel's order.
 * @param id The feature that was toggled.
 * @param ticked Whether it is now ticked.
 * @returns Nothing; emits synchronously.
 */
const toggle = (id: AccountFeatureName, ticked: boolean): void => {
  const next = props.features
    .map((feature) => {
      return feature.feature
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
      :key="feature.feature"
      :model-value="modelValue.includes(feature.feature)"
      :label="feature.featureDisplayName"
      @update:model-value="toggle(feature.feature, $event)"
    />
  </div>
</template>
