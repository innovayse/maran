<script setup lang="ts">
/**
 * The records of a template being edited: a table of the draft's records with add, change and
 * remove, and the dialog that edits one. The draft lives in the parent's form (`v-model`), so
 * nothing is sent until the template itself is saved; removing a row here only changes the draft.
 * The record type list, labels and bounds are the panel's catalogue, handed in by the parent.
 *
 * A row is identified by its position in the draft: a template's record has no identity of its
 * own, and the panel replaces the whole set on save.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiEmptyState from '../ui/UiEmptyState.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiTable from '../ui/UiTable.vue'
import DnsTemplateRecordDialog from './DnsTemplateRecordDialog.vue'
import { dnsRecordValue } from '../../utils/dnsRecordValue'
import type { DnsRecordCatalog } from '../../types/dns'
import type { DnsTemplateRecord } from '../../types/dnsTemplate'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link DnsTemplateRecordsEditor}. */
const props = defineProps<{
  /** The panel's record type catalogue, or `null` while it has not arrived. */
  catalog: DnsRecordCatalog | null
}>()

/** The draft's records, in order. */
const records = defineModel<DnsTemplateRecord[]>('records', { required: true })

/** A draft record as a table row: the record and its position, which is its key. */
interface Row extends DnsTemplateRecord {
  /** The record's position in the draft. */
  position: number
}

const { t } = useI18n()

/** Whether the dialog is open. */
const dialogOpen: Ref<boolean> = ref(false)

/** The position of the record being changed, or `null` while the dialog adds a new one. */
const editing: Ref<number | null> = ref(null)

/** The table's columns. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'name', label: t('dns.records.columns.name'), kind: 'identifier', minWidth: '10rem' },
    { key: 'type', label: t('dns.records.columns.type'), kind: 'mono' },
    { key: 'ttl', label: t('dns.records.columns.ttl'), kind: 'number' },
    { key: 'value', label: t('dns.records.columns.value'), kind: 'code', minWidth: '14rem' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/** The draft's records as rows. */
const rows: ComputedRef<Row[]> = computed(() => {
  return records.value.map((record, position): Row => {
    return { ...record, position }
  })
})

/** The record the dialog starts from, or `null` for a new one. */
const editedRecord: ComputedRef<DnsTemplateRecord | null> = computed(() => {
  return editing.value === null ? null : (records.value[editing.value] ?? null)
})

/**
 * Finds the catalogue's label of a record's type.
 * @param row The record.
 * @returns The label, or the wire type while the catalogue has not arrived.
 */
const typeLabel = (row: Row): string => {
  return props.catalog?.types.find((info) => { return info.type === row.type })?.label ?? row.type
}

/**
 * Writes a record's data the way a zone file reads it.
 * @param row The record.
 * @returns The data as one line of text.
 */
const valueOf = (row: Row): string => {
  return dnsRecordValue(
    row,
    props.catalog?.types.find((info) => { return info.type === row.type }),
  )
}

/**
 * Opens the dialog for a new record.
 * @returns Nothing.
 */
const add = (): void => {
  editing.value = null
  dialogOpen.value = true
}

/**
 * Opens the dialog for an existing record.
 * @param row The record to change.
 * @returns Nothing.
 */
const edit = (row: Row): void => {
  editing.value = row.position
  dialogOpen.value = true
}

/**
 * Drops a record from the draft.
 * @param row The record to remove.
 * @returns Nothing.
 */
const remove = (row: Row): void => {
  records.value = records.value.filter((_record, position) => { return position !== row.position })
}

/**
 * Closes the dialog without changing the draft.
 * @returns Nothing.
 */
const close = (): void => {
  dialogOpen.value = false
}

/**
 * Puts the dialog's record into the draft, at its old position or at the end, and closes.
 * @param record The record the dialog accepted.
 * @returns Nothing.
 */
const keep = (record: DnsTemplateRecord): void => {
  const position = editing.value
  records.value =
    position === null
      ? [...records.value, record]
      : records.value.map((existing, index) => { return index === position ? record : existing })
  dialogOpen.value = false
}
</script>

<template>
  <div class="flex flex-col gap-4">
    <UiSectionHeading :title="t('dns.templates.records.heading')" :subtitle="t('dns.templates.records.subtitle')" />
    <UiEmptyState
      v-if="records.length === 0"
      :title="t('dns.templates.records.emptyTitle')"
      :description="t('dns.templates.records.emptyDescription')"
    >
      <UiButton type="button" @click="add">{{ t('dns.records.addAction') }}</UiButton>
    </UiEmptyState>
    <template v-else>
      <UiTable :caption="t('dns.templates.records.tableCaption')" :columns="columns" :rows="rows" row-key="position">
        <template #cell-name="{ row }">{{ row.name }}</template>
        <template #cell-type="{ row }">{{ typeLabel(row) }}</template>
        <template #cell-ttl="{ row }">{{ row.ttl ?? t('dns.templates.records.defaultTtl') }}</template>
        <template #cell-value="{ row }">{{ valueOf(row) }}</template>
        <template #cell-actions="{ row }">
          <div class="flex items-center justify-end">
            <UiDropdown
              :label="t('common.actions')"
              :aria-label="t('dns.records.rowActions', { name: row.name, type: typeLabel(row) })"
              align="end"
              variant="bare"
              :chevron="false"
            >
              <template #trigger>
                <UiIcon name="ellipsis" size="md" />
              </template>
              <UiDropdownItem @select="edit(row)">{{ t('dns.records.edit') }}</UiDropdownItem>
              <UiDropdownItem destructive @select="remove(row)">{{ t('common.delete') }}</UiDropdownItem>
            </UiDropdown>
          </div>
        </template>
      </UiTable>
      <div>
        <UiButton type="button" variant="secondary" @click="add">{{ t('dns.records.addAction') }}</UiButton>
      </div>
    </template>

    <DnsTemplateRecordDialog v-if="dialogOpen" :record="editedRecord" :catalog="catalog" @submit="keep" @close="close" />
  </div>
</template>
