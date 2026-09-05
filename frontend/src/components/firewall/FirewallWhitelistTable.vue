<script setup lang="ts">
/**
 * The ranges the panel's automatic bans never touch, and the removal that takes one away.
 *
 * **One of these rows is usually not an administrator's.** On a fresh server the panel seeds the
 * whitelist, once, with the address the installer was run from — the operator's own SSH client —
 * because the brute-force detector cannot tell an administrator mistyping their password from an
 * attack, and an empty whitelist on day one is a server whose only administrator can lock
 * themselves out of it with a typo.
 *
 * That row is not marked as seeded on the wire: `WhitelistEntryDto` carries an id, a range, a note
 * and an instant, and nothing that says where the row came from. What identifies it is the note the
 * panel itself wrote when it seeded the row, which is why the note is a column here and is rendered
 * verbatim — the SPA does not decide which row is the seed, it shows what the panel wrote and lets
 * the operator read it. The removal is confirmed for the same reason: removing the range you
 * administer from is how an operator locks themselves out, and the screen cannot tell which range
 * that is.
 */
import { ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiTable from '../ui/UiTable.vue'
import UiTableCell from '../ui/UiTableCell.vue'
import UiTableHeaderCell from '../ui/UiTableHeaderCell.vue'
import UiTableRow from '../ui/UiTableRow.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import type { WhitelistEntry } from '../../types/firewall'

/** Props accepted by {@link FirewallWhitelistTable}. */
defineProps<{
  /** The exempt ranges the panel reported, oldest first. */
  entries: readonly WhitelistEntry[]
  /** Whether a change is already in flight, which disables every row's menu. */
  busy: boolean
}>()

/** Events emitted by {@link FirewallWhitelistTable}. */
const emit = defineEmits<{
  /** Fired when the operator confirmed removing one exemption. */
  (e: 'remove', id: string): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The row whose removal is awaiting confirmation, or the empty string when none is. */
const pendingId: Ref<string> = ref('')

/**
 * Starts the row's confirmation.
 * @param id The row the operator acted on.
 * @returns Nothing.
 */
const ask = (id: string): void => {
  pendingId.value = id
}

/**
 * Abandons a pending removal.
 * @returns Nothing.
 */
const cancel = (): void => {
  pendingId.value = ''
}

/**
 * Reports the confirmed removal to the page, which owns the request.
 * @param id The row to remove.
 * @returns Nothing; emits synchronously.
 */
const confirm = (id: string): void => {
  pendingId.value = ''
  emit('remove', id)
}
</script>

<template>
  <UiTable :caption="t('firewall.whitelist.tableCaption')">
    <template #head>
      <UiTableRow>
        <UiTableHeaderCell>{{ t('firewall.whitelist.columns.cidr') }}</UiTableHeaderCell>
        <UiTableHeaderCell>{{ t('firewall.whitelist.columns.note') }}</UiTableHeaderCell>
        <UiTableHeaderCell>{{ t('firewall.whitelist.columns.createdAt') }}</UiTableHeaderCell>
        <UiTableHeaderCell align="end">{{ t('firewall.whitelist.columns.actions') }}</UiTableHeaderCell>
      </UiTableRow>
    </template>
    <UiTableRow v-for="entry in entries" :key="entry.id">
      <UiTableCell class="font-mono font-medium">{{ entry.cidr }}</UiTableCell>
      <!-- The panel's own words, shown as written: this is what tells an operator that a row is the
           installer's seed rather than one of theirs. -->
      <UiTableCell class="text-text-secondary">
        <span class="block max-w-[420px] truncate">{{ entry.note }}</span>
      </UiTableCell>
      <UiTableCell class="font-mono text-text-muted">
        {{ formatDate(entry.createdAt, localeStore.current) }}
      </UiTableCell>
      <UiTableCell align="end">
        <div class="flex flex-wrap items-center justify-end gap-2">
          <template v-if="pendingId === entry.id">
            <span class="text-sm text-text-secondary">{{ t('firewall.whitelist.confirmRemove') }}</span>
            <UiButton variant="destructive" :disabled="busy" @click="confirm(entry.id)">
              {{ t('firewall.whitelist.confirm') }}
            </UiButton>
            <UiButton variant="secondary" @click="cancel">
              {{ t('firewall.whitelist.cancel') }}
            </UiButton>
          </template>
          <UiDropdown
            v-else
            :label="t('firewall.whitelist.columns.actions')"
            :aria-label="t('firewall.whitelist.rowActions', { cidr: entry.cidr })"
            align="end"
            variant="bare"
            :chevron="false"
            :disabled="busy"
          >
            <template #trigger>
              <UiIcon name="ellipsis" size="md" />
            </template>
            <UiDropdownItem destructive @select="ask(entry.id)">
              {{ t('firewall.whitelist.remove') }}
            </UiDropdownItem>
          </UiDropdown>
        </div>
      </UiTableCell>
    </UiTableRow>
  </UiTable>
</template>
