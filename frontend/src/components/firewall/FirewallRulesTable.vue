<script setup lang="ts">
/**
 * The rules the host's firewall is running, one row each, with the removal that takes one away.
 *
 * **What is NOT here is as important as what is.** The unconditional accepts the agent renders for
 * the host's SSH ports and for the panel's own port are not in the panel's listing, so they cannot
 * be in this table: offering an administrator a "remove" button for the rule holding their session
 * open is the lockout with a button on it. Every row here is a rule somebody asked for.
 *
 * **Layout (redesign, October 2026).** The table sits in a {@link UiCard} panel and is flat: hairlines
 * between rows, no zebra, the header in the display face. Ports, protocols and source ranges are
 * machine text, so every data cell is monospace (`mono`) and is never uppercased. The table scrolls
 * inside its own container ({@link UiTable}), so a narrow phone never gets a page that scrolls
 * sideways.
 *
 * Dumb by the usual contract: props in, emits out. The page owns the store, the confirmation and
 * the request.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiCard from '../ui/UiCard.vue'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiTable from '../ui/UiTable.vue'
import type { UiTableColumn } from '../../types/tableColumn'
import { portRangeLabel } from '../../utils/portRangeLabel'
import type { FirewallRule } from '../../types/firewall'

/** Props accepted by {@link FirewallRulesTable}. */
defineProps<{
  /** The rules the panel reported, in the order it reported them. */
  rules: readonly FirewallRule[]
  /** Whether a rule change is already in flight, which disables every row's menu. */
  busy: boolean
  /** Whether the screen's lists are still loading: the real header over placeholder rows. */
  loading: boolean
}>()

/** Events emitted by {@link FirewallRulesTable}. */
const emit = defineEmits<{
  /** Fired when the operator asks for a rule to be removed; the page confirms before sending it. */
  (e: 'remove', rule: FirewallRule): void
}>()

const { t } = useI18n()

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'port', label: t('firewall.rules.columns.port'), kind: 'identifier' },
    { key: 'protocol', label: t('firewall.rules.columns.protocol'), kind: 'mono' },
    { key: 'source', label: t('firewall.rules.columns.source'), kind: 'mono' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/**
 * Names one rule the way the panel's own audit journal names it, for a row's accessible label.
 * @param rule The rule to describe.
 * @returns The rule as one line of text.
 */
const describe = (rule: FirewallRule): string => {
  return t('firewall.rules.ruleSummary', {
    protocol: rule.protocol,
    ports: portRangeLabel(rule.port, rule.portTo),
    source: rule.sourceCidr,
  })
}
</script>

<template>
  <UiCard>
    <!-- A rule has no identity beyond its three values, so its rows are keyed by position. -->
    <UiTable :caption="t('firewall.rules.tableCaption')" :columns="columns" :rows="rules" :loading="loading">
      <!-- The range as one cell, not a port with a bound hidden elsewhere: what the row says is
           what the firewall opens. -->
      <template #cell-port="{ row }">{{ portRangeLabel(row.port, row.portTo) }}</template>
      <template #cell-protocol="{ row }">{{ t(`firewall.protocols.${row.protocol}`) }}</template>
      <template #cell-source="{ row }">{{ row.sourceCidr }}</template>
      <template #cell-actions="{ row }">
        <!-- One trigger rather than a button per command, and `align="end"` because this is the
             last column: a menu aligned to the start would open off the right edge. -->
        <UiDropdown
          :label="t('common.actions')"
          :aria-label="t('firewall.rules.rowActions', { rule: describe(row) })"
          align="end"
          variant="bare"
          :chevron="false"
          :disabled="busy"
        >
          <template #trigger>
            <UiIcon name="ellipsis" size="md" />
          </template>
          <UiDropdownItem destructive @select="emit('remove', row)">
            {{ t('firewall.rules.remove') }}
          </UiDropdownItem>
        </UiDropdown>
      </template>
    </UiTable>
  </UiCard>
</template>
