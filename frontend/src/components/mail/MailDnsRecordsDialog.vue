<script setup lang="ts">
/**
 * The DNS records one mail domain needs, in a dialog: what to publish at the domain's DNS host so
 * mail is delivered to this server and the domain's own mail is trusted (MX, SPF, DKIM, DMARC). The
 * domain and the records come from the mail domains store, which the page opens; this component
 * never reaches the API layer (rules/vue.md).
 *
 * **The panel publishes nothing.** The records are shown to be entered where the domain's DNS is
 * managed, and the dialog says so. Each value has a copy control, because a DKIM key is hundreds of
 * characters and retyping one is how a record ends up subtly wrong.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiEmptyState from '../ui/UiEmptyState.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiLoadingRegion from '../ui/UiLoadingRegion.vue'
import UiModal from '../ui/UiModal.vue'
import UiTable from '../ui/UiTable.vue'
import { useMailDomainsStore } from '../../stores/mailDomains'
import type { UiTableColumn } from '../../types/tableColumn'
import type { MailDnsRecord, MailDomain } from '../../types/mailDomain'

/** Props accepted by {@link MailDnsRecordsDialog}; the page mounts it only while a domain's records are open. */
defineProps<{
  /** The domain whose records are shown; the store holds the same domain and its records. */
  domain: MailDomain
}>()

/** Events emitted by {@link MailDnsRecordsDialog}. */
const emit = defineEmits<{
  /** The person closed the dialog. */
  (e: 'close'): void
}>()

const { t } = useI18n()
const store = useMailDomainsStore()

/** The record whose value was last copied, so its control can say so; `null` before any copy. */
const copied: Ref<MailDnsRecord | null> = ref(null)

/** The records table's columns: its header and its cells read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'purpose', label: t('mail.dns.columns.purpose'), kind: 'text' },
    { key: 'type', label: t('mail.dns.columns.type'), kind: 'identifier' },
    { key: 'name', label: t('mail.dns.columns.name'), kind: 'mono' },
    { key: 'value', label: t('mail.dns.columns.value'), kind: 'code', minWidth: '14rem' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/**
 * Names a record for a row key: a record has no identity of its own, and its type, name and value
 * together are what makes it that record.
 * @param record The record.
 * @returns A string that differs between any two distinct records.
 */
const keyOf = (record: MailDnsRecord): string => {
  return `${record.type}|${record.name}|${record.value}`
}

/**
 * Copies a record's value to the clipboard.
 *
 * A failure is swallowed on purpose and leaves the control unchanged: the clipboard is blocked
 * outright in some browser configurations, and the value stays selectable on screen beside it.
 * @param record The record whose value is copied.
 * @returns Resolves once the clipboard write has settled, successfully or not.
 */
const copyValue = async (record: MailDnsRecord): Promise<void> => {
  try {
    await navigator.clipboard.writeText(record.value)
    copied.value = record
  } catch {
    copied.value = null
  }
}
</script>

<template>
  <UiModal
    :open="true"
    :title="t('mail.dns.title', { name: domain.name })"
    :close-label="t('common.close')"
    @close="emit('close')"
  >
    <div class="flex flex-col gap-4">
      <p>{{ t('mail.dns.intro') }}</p>

      <UiLoadingRegion :state="store.recordsState">
        <template #error>
          <UiAlert variant="error">{{ store.recordsErrorMessage ?? t('common.list.unreachable') }}</UiAlert>
        </template>

        <UiEmptyState
          v-if="store.records.length === 0"
          :title="t('mail.dns.emptyTitle')"
          :description="t('mail.dns.emptyDescription')"
        />

        <UiTable
          v-else
          :caption="t('mail.dns.tableCaption', { name: domain.name })"
          :columns="columns"
          :rows="store.records"
        >
          <template #cell-purpose="{ row }">{{ row.purposeName }}</template>
          <template #cell-type="{ row }">
            {{ row.priority === null ? row.type : `${row.type} ${row.priority}` }}
          </template>
          <template #cell-name="{ row }">{{ row.name }}</template>
          <template #cell-value="{ row }">{{ row.value }}</template>
          <template #cell-actions="{ row }">
            <UiButton
              variant="ghost"
              size="icon-sm"
              :aria-label="t('mail.dns.copyValue', { name: row.name })"
              @click="copyValue(row)"
            >
              <UiIcon :name="copied !== null && keyOf(copied) === keyOf(row) ? 'check' : 'copy'" size="sm" />
            </UiButton>
          </template>
        </UiTable>
      </UiLoadingRegion>
    </div>

    <template #footer>
      <UiButton @click="emit('close')">{{ t('common.done') }}</UiButton>
    </template>
  </UiModal>
</template>
