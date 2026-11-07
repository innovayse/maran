<script setup lang="ts">
/**
 * The audit journal's export menu: one trigger, two commands (CSV and JSON). The file holds
 * everything the journal's current search and filters match, not just the page on screen, so the
 * menu is handed the table's request and sends its search and filters (the store drops the page
 * and the sort: an export is the whole matching journal in chronological order).
 *
 * The file is produced and refused by the panel. A refusal (an export too large to hand over, an
 * exhausted allowance) is kept by the store as the panel's own sentence; this menu does not render
 * it, the page shows it beside the journal. While a file is on its way the trigger is disabled and
 * says so, which is also what stops a second click from asking for a second file.
 */
import { useI18n } from 'vue-i18n'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import { useAuditStore } from '../../stores/audit'
import type { AuditExportFormat } from '../../types/audit'
import type { ListRequest } from '../../types/list'

/** Props accepted by {@link AuditExportMenu}. */
const props = defineProps<{
  /** The table's current request; its search and filters decide what the file holds. */
  request: ListRequest
}>()

/** Events emitted by {@link AuditExportMenu}. */
const emit = defineEmits<{
  /** The file was received and handed to the browser. */
  (e: 'exported'): void
}>()

const { t } = useI18n()
const auditStore = useAuditStore()

/**
 * Asks the panel for the matching journal in one format and saves the file it sends.
 * @param format The file format chosen from the menu.
 * @returns Resolves once the download settled, successfully or not.
 */
const exportAs = async (format: AuditExportFormat): Promise<void> => {
  if (await auditStore.exportJournal(props.request, format)) {
    emit('exported')
  }
}
</script>

<template>
  <UiDropdown
    :label="auditStore.exporting ? t('app.audit.export.working') : t('app.audit.export.menuLabel')"
    :disabled="auditStore.exporting"
    align="end"
    trigger-variant="secondary"
  >
    <UiDropdownItem @select="exportAs('csv')">{{ t('app.audit.export.csv') }}</UiDropdownItem>
    <UiDropdownItem @select="exportAs('json')">{{ t('app.audit.export.json') }}</UiDropdownItem>
  </UiDropdown>
</template>
