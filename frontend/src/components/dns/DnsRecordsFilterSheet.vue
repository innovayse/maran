<script setup lang="ts">
/**
 * The records list's filter sheet: the record type. The choices are the panel's catalogue. Props
 * in, events out: the tab owns the applied filters in its URL.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiFilterSheet from '../ui/UiFilterSheet.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { DnsRecordTypeInfo } from '../../types/dns'

/** Props accepted by {@link DnsRecordsFilterSheet}. */
const props = defineProps<{
  /** Whether the sheet is open. */
  open: boolean
  /** The filters applied now, which the sheet's draft starts from. */
  filters: Record<string, string>
  /** The record types the panel serves. */
  recordTypes: readonly DnsRecordTypeInfo[]
}>()

/** Events emitted by {@link DnsRecordsFilterSheet}. */
const emit = defineEmits<{
  /** The reader applied the draft. */
  (e: 'apply', filters: Record<string, string>): void
  /** The reader closed the sheet. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/** The types the filter offers, after an "any type" choice. */
const typeOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    ...props.recordTypes.map((info) => {
      return { value: info.type, label: info.label }
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
      <UiSelect v-model="draft.type" :label="t('dns.records.filters.type')" :options="typeOptions" />
    </template>
  </UiFilterSheet>
</template>
