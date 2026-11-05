<script setup lang="ts">
/**
 * The Updates list's filter sheet: one field, the classification. Opens as a modal on desktop and a
 * bottom sheet on a phone (`UiFilterSheet`). Values go out on Apply exactly as the sheet emits
 * them; the page writes them to the URL, and the panel validates the value. The kinds offered and
 * their names are what the panel answered, held by the pending-updates store: the SPA lists none
 * itself.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiFilterSheet from '../ui/UiFilterSheet.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import { usePendingUpdatesStore } from '../../stores/pendingUpdates'

/** Props accepted by {@link PendingUpdatesFilterSheet}. */
defineProps<{
  /** Whether the sheet is shown. */
  open: boolean
  /** The applied filters by name (`classification`), empty when unset. */
  modelValue: Record<string, string>
}>()

/** Events emitted by {@link PendingUpdatesFilterSheet}. */
const emit = defineEmits<{
  /** The reader applied the filters; carries every field. */
  (e: 'update:modelValue', value: Record<string, string>): void
  /** The reader dismissed or applied the sheet. */
  (e: 'close'): void
}>()

const { t } = useI18n()
const store = usePendingUpdatesStore()

/** The kinds the panel named, after an "any" choice. */
const classificationOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    ...store.classifications.map((choice) => {
      return { value: choice.value, label: choice.name }
    }),
  ]
})

/**
 * Passes the applied filters up.
 * @param value Every field of the sheet.
 * @returns Nothing.
 */
const apply = (value: Record<string, string>): void => {
  emit('update:modelValue', value)
}

/**
 * Passes the dismissal up.
 * @returns Nothing.
 */
const close = (): void => {
  emit('close')
}
</script>

<template>
  <UiFilterSheet
    :open="open"
    :model-value="modelValue"
    :title="t('common.list.filters')"
    :close-label="t('common.close')"
    :apply-label="t('common.list.apply')"
    :reset-label="t('common.list.reset')"
    @update:model-value="apply"
    @close="close"
  >
    <template #default="{ draft }">
      <UiSelect
        v-model="draft.classification"
        :label="t('monitoring.updates.columnClassification')"
        :options="classificationOptions"
        :load-error="store.classificationsError"
      />
    </template>
  </UiFilterSheet>
</template>
