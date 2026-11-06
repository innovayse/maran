<script setup lang="ts">
/**
 * The API keys list's filter sheet: the state filter, the list's one filter. The states offered
 * are the ones the panel lists, under its own names. Props in, events out: the page owns the
 * applied filters in its URL.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiFilterSheet from '../ui/UiFilterSheet.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { ApiKeyChoice } from '../../types/apiKey'

/** Props accepted by {@link ApiKeysFilterSheet}. */
const props = defineProps<{
  /** Whether the sheet is open. */
  open: boolean
  /** The filters applied now, which the sheet's draft starts from. */
  filters: Record<string, string>
  /** The states the panel lists, under its names. */
  statuses: ApiKeyChoice[]
}>()

/** Events emitted by {@link ApiKeysFilterSheet}. */
const emit = defineEmits<{
  /** The reader applied the draft. */
  (e: 'apply', filters: Record<string, string>): void
  /** The reader closed the sheet. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/** The states the filter offers, after an "any" choice. */
const statusOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    ...props.statuses.map((choice) => {
      return { value: choice.value, label: choice.name }
    }),
  ]
})

/**
 * Forwards the applied draft.
 * @param filters The draft the reader applied.
 * @returns Nothing; the event is emitted synchronously.
 */
const apply = (filters: Record<string, string>): void => {
  emit('apply', filters)
}

/**
 * Forwards a close.
 * @returns Nothing; the event is emitted synchronously.
 */
const close = (): void => {
  emit('close')
}
</script>

<template>
  <UiFilterSheet
    :open="open"
    :model-value="filters"
    :title="t('common.list.filters')"
    :close-label="t('common.close')"
    :apply-label="t('common.list.apply')"
    :reset-label="t('common.list.reset')"
    @update:model-value="apply"
    @close="close"
  >
    <template #default="{ draft }">
      <UiSelect v-model="draft.status" :label="t('apiKeys.filters.status')" :options="statusOptions" />
    </template>
  </UiFilterSheet>
</template>
