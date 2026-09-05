<script setup lang="ts">
/**
 * The rules the host's firewall is running, one row each, with the removal that takes one away.
 *
 * **What is NOT here is as important as what is.** The unconditional accepts the agent renders for
 * the host's SSH ports and for the panel's own port are not in the panel's listing, so they cannot
 * be in this table: offering an administrator a "remove" button for the rule holding their session
 * open is the lockout with a button on it. Every row here is a rule somebody asked for.
 *
 * Dumb by the usual contract: props in, emits out. The page owns the store, the confirmation and
 * the request.
 */
import { useI18n } from 'vue-i18n'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiTable from '../ui/UiTable.vue'
import UiTableCell from '../ui/UiTableCell.vue'
import UiTableHeaderCell from '../ui/UiTableHeaderCell.vue'
import UiTableRow from '../ui/UiTableRow.vue'
import type { FirewallRule } from '../../types/firewall'

/** Props accepted by {@link FirewallRulesTable}. */
defineProps<{
  /** The rules the panel reported, in the order it reported them. */
  rules: readonly FirewallRule[]
  /** Whether a rule change is already in flight, which disables every row's menu. */
  busy: boolean
}>()

/** Events emitted by {@link FirewallRulesTable}. */
const emit = defineEmits<{
  /** Fired when the operator asks for a rule to be removed; the page confirms before sending it. */
  (e: 'remove', rule: FirewallRule): void
}>()

const { t } = useI18n()

/**
 * Names one rule the way the panel's own audit journal names it, for a row's accessible label.
 * @param rule The rule to describe.
 * @returns The rule as one line of text.
 */
const describe = (rule: FirewallRule): string => {
  return t('firewall.rules.ruleSummary', {
    protocol: rule.protocol,
    port: rule.port,
    source: rule.sourceCidr,
  })
}
</script>

<template>
  <UiTable :caption="t('firewall.rules.tableCaption')">
    <template #head>
      <UiTableRow>
        <UiTableHeaderCell>{{ t('firewall.rules.columns.port') }}</UiTableHeaderCell>
        <UiTableHeaderCell>{{ t('firewall.rules.columns.protocol') }}</UiTableHeaderCell>
        <UiTableHeaderCell>{{ t('firewall.rules.columns.source') }}</UiTableHeaderCell>
        <UiTableHeaderCell align="end">{{ t('firewall.rules.columns.actions') }}</UiTableHeaderCell>
      </UiTableRow>
    </template>
    <UiTableRow
      v-for="rule in rules"
      :key="`${rule.protocol}-${rule.port}-${rule.sourceCidr}`"
    >
      <UiTableCell class="font-mono font-medium">{{ rule.port }}</UiTableCell>
      <UiTableCell class="font-mono text-text-secondary">
        {{ t(`firewall.protocols.${rule.protocol}`) }}
      </UiTableCell>
      <UiTableCell class="font-mono text-text-secondary">{{ rule.sourceCidr }}</UiTableCell>
      <UiTableCell align="end">
        <div class="flex items-center justify-end">
          <!-- One trigger rather than a button per command, and `align="end"` because this is the
               last column: a menu aligned to the start would open off the right edge. -->
          <UiDropdown
            :label="t('firewall.rules.columns.actions')"
            :aria-label="t('firewall.rules.rowActions', { rule: describe(rule) })"
            align="end"
            variant="bare"
            :chevron="false"
            :disabled="busy"
          >
            <template #trigger>
              <UiIcon name="ellipsis" size="md" />
            </template>
            <UiDropdownItem destructive @select="emit('remove', rule)">
              {{ t('firewall.rules.remove') }}
            </UiDropdownItem>
          </UiDropdown>
        </div>
      </UiTableCell>
    </UiTableRow>
  </UiTable>
</template>
