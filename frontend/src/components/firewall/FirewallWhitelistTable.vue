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
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiConfirm from '../ui/UiConfirm.vue'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiTable from '../ui/UiTable.vue'
import type { UiTableColumn } from '../../types/tableColumn'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { WhitelistEntry } from '../../types/firewall'

/** Props accepted by {@link FirewallWhitelistTable}. */
const props = defineProps<{
  /** The page of exempt ranges on screen, in the server's order. */
  entries: readonly WhitelistEntry[]
  /** Whether a change is already in flight, which disables every row's menu. */
  busy: boolean
  /** Whether a page is being fetched: the real header over placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading: the page size. */
  skeletonRows: number
  /** The current sort key, `-` prefixed when descending; empty for the server's default. */
  sort: string
}>()

/** Events emitted by {@link FirewallWhitelistTable}. */
const emit = defineEmits<{
  /** Fired when the operator confirmed removing one exemption. */
  (e: 'remove', id: string): void
  /** A sortable heading was pressed; carries the server's sort field. */
  (e: 'sort', field: string): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The row whose removal is awaiting confirmation, or the empty string when none is. */
const pendingId: Ref<string> = ref('')

/**
 * The table's columns: its header, its cells and its loading placeholder all read this one list.
 * The range and the instant it was added sort by the server's `cidr` and `createdAt` keys.
 */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'cidr', label: t('firewall.whitelist.columns.cidr'), kind: 'identifier', sortField: 'cidr' },
    { key: 'note', label: t('firewall.whitelist.columns.note'), kind: 'text' },
    { key: 'createdAt', label: t('firewall.whitelist.columns.createdAt'), kind: 'date', sortField: 'createdAt' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/** The exemption the confirmation is open for, or `null` when none is. */
const pendingEntry: ComputedRef<WhitelistEntry | null> = computed(() => {
  return (
    props.entries.find((entry) => {
      return entry.id === pendingId.value
    }) ?? null
  )
})

/** The confirmation's accessible name, naming the range that would stop being exempt. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  const pending = pendingEntry.value
  return pending === null ? '' : t('firewall.whitelist.confirmRemoveTitle', { cidr: pending.cidr })
})

/**
 * Forwards a sortable heading's press.
 * @param field The server's sort field.
 * @returns Nothing.
 */
const sortBy = (field: string): void => {
  emit('sort', field)
}

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
 * @returns Nothing; emits synchronously.
 */
const confirm = (): void => {
  const pending = pendingEntry.value
  if (pending === null) {
    return
  }

  // Closed here rather than after the request, because the request is the page's: this table is
  // dumb, and `busy` is what tells the dialog the answer is already with the server.
  emit('remove', pending.id)
}

// The request belongs to the page, so the dialog cannot close itself when it settles: `busy`
// falling back to false is this table's only sight of that moment.
watch(
  (): boolean => {
    return props.busy
  },
  (busy, wasBusy): void => {
    if (wasBusy && !busy) {
      pendingId.value = ''
    }
  },
)
</script>

<template>
  <!-- `UiTable` scrolls inside its own container, so at 390px the table keeps its columns and the
       page never moves sideways: the machine-text columns refuse to wrap, the note is the one that
       does. -->
  <UiTable
    :caption="t('firewall.whitelist.tableCaption')"
    :columns="columns"
    :rows="entries"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="sortBy"
  >
    <!-- A range is machine text: the mono face, as typed, never uppercased. -->
    <template #cell-cidr="{ row }">{{ row.cidr }}</template>
    <!-- The panel's own words, shown whole and as written: this is what tells an operator that a
         row is the installer's seed rather than one of theirs, so it wraps instead of being cut
         off with an ellipsis. -->
    <template #cell-note="{ row }"><span class="block min-w-56 max-w-md wrap-anywhere">{{ row.note }}</span></template>
    <template #cell-createdAt="{ row }">{{ formatIsoTimestamp(row.createdAt, localeStore.current) }}</template>
    <template #cell-actions="{ row }">
      <UiDropdown
        :label="t('common.actions')"
        :aria-label="t('firewall.whitelist.rowActions', { cidr: row.cidr })"
        align="end"
        variant="bare"
        :chevron="false"
        :disabled="busy"
      >
        <template #trigger>
          <UiIcon name="ellipsis" size="md" />
        </template>
        <UiDropdownItem destructive @select="ask(row.id)">
          {{ t('firewall.whitelist.remove') }}
        </UiDropdownItem>
      </UiDropdown>
    </template>
  </UiTable>

  <!-- One dialog for the whole table rather than one per row: only one removal can be awaiting an
       answer, and the menu that opened it is already gone by the time it appears. -->
  <UiConfirm
    :open="pendingId.length > 0"
    :title="confirmationTitle"
    :question="t('firewall.whitelist.confirmRemove')"
    :confirm-label="t('firewall.whitelist.confirm')"
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="busy"
    :acting-label="t('firewall.whitelist.working')"
    @close="cancel"
    @confirm="confirm"
  />
</template>
