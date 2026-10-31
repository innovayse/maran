<script setup lang="ts">
/**
 * The addresses the host is currently refusing, why each one is refused, and how long is left.
 *
 * **The reason column is the reason this table exists.** What the kernel holds is an address and a
 * countdown; the panel's own rows are the only record of WHY anybody was banned, because the agent
 * stores no reason and cannot — the one place a reason could go on that side is an nftables comment
 * whose argument `nft` parses in its own grammar. So this is the product's whole answer to "why is
 * this customer's office cut off", and a table without it would be a list of numbers.
 *
 * The countdown ticks: an expiry is the one value on this screen that changes while nobody touches
 * the page, and an operator deciding whether to wait or to lift a ban is reading exactly that. The
 * instant it counts from is held here and passed to the formatter, rather than read inside it, so
 * the behaviour can be driven by a test clock instead of by whatever the wall clock happens to say.
 */
import { computed, onBeforeUnmount, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge from '../ui/UiBadge.vue'
import UiConfirm from '../ui/UiConfirm.vue'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiTable from '../ui/UiTable.vue'
import type { UiTableColumn } from '../../types/tableColumn'
import { useLocaleStore } from '../../stores/locale'
import { formatCountdown } from '../../utils/formatCountdown'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { Ban } from '../../types/firewall'

/** Props accepted by {@link FirewallBansTable}. */
const props = defineProps<{
  /** The page of bans on screen, in the server's order. */
  bans: readonly Ban[]
  /** Whether a change is already in flight, which disables every row's menu. */
  busy: boolean
  /** Whether a page is being fetched: the real header over placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading: the page size. */
  skeletonRows: number
  /** The current sort key, `-` prefixed when descending; empty for the server's default. */
  sort: string
}>()

/** Events emitted by {@link FirewallBansTable}. */
const emit = defineEmits<{
  /** Fired when the operator confirmed lifting every ban in force for one address. */
  (e: 'unban', address: string): void
  /** A sortable heading was pressed; carries the server's sort field. */
  (e: 'sort', field: string): void
}>()

/**
 * How often the countdown is recomputed. A second, because that is the unit an expiry is quoted in
 * and a coarser tick would leave a row saying "in 1 minute" for a minute after it had run out.
 */
const TICK_INTERVAL_MS = 1000

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The instant every countdown is measured from, moved forward by the ticker below. */
const now: Ref<number> = ref(Date.now())

/** The ticker's handle, kept so it can be stopped when the screen goes away. */
const ticker: Ref<ReturnType<typeof setInterval> | null> = ref(null)

/** The ban whose lift is awaiting confirmation, or the empty string when none is. */
const pendingId: Ref<string> = ref('')

/**
 * The table's columns: its header, its cells and its loading placeholder all read this one list.
 * The address and the two instants sort by the server's keys of the same names.
 */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'address', label: t('firewall.bans.columns.address'), kind: 'identifier', sortField: 'address' },
    { key: 'reason', label: t('firewall.bans.columns.reason'), kind: 'badge' },
    { key: 'failures', label: t('firewall.bans.columns.failures'), kind: 'number' },
    { key: 'bannedAt', label: t('firewall.bans.columns.bannedAt'), kind: 'date', sortField: 'bannedAt' },
    { key: 'expiresAt', label: t('firewall.bans.columns.expiresAt'), kind: 'date', sortField: 'expiresAt' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/** The ban the confirmation is open for, or `null` when none is. */
const pendingBan: ComputedRef<Ban | null> = computed(() => {
  return (
    props.bans.find((ban) => {
      return ban.id === pendingId.value
    }) ?? null
  )
})

/** The confirmation's accessible name, naming the address that would be let back in. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  const pending = pendingBan.value
  return pending === null ? '' : t('firewall.bans.confirmUnbanTitle', { address: pending.ipAddress })
})

/**
 * How long is left on a ban, or the word for one that lasts until somebody lifts it.
 * @param ban The row being rendered.
 * @returns Ready-to-render text for the expiry column.
 */
const expiry = (ban: Ban): string => {
  return ban.expiresAt === null
    ? t('firewall.bans.permanent')
    : formatCountdown(ban.expiresAt, now.value, localeStore.current)
}

/**
 * Forwards a sortable heading's press.
 * @param field The server's sort field.
 * @returns Nothing.
 */
const sortBy = (field: string): void => {
  emit('sort', field)
}

/**
 * Starts the row's confirmation. Lifting a ban lets an address back in, so it is not something a
 * single press of a menu item should do.
 * @param id The ban the operator acted on.
 * @returns Nothing.
 */
const ask = (id: string): void => {
  pendingId.value = id
}

/**
 * Abandons a pending lift.
 * @returns Nothing.
 */
const cancel = (): void => {
  pendingId.value = ''
}

/**
 * Reports the confirmed lift to the page, which owns the request.
 * @returns Nothing; emits synchronously.
 */
const confirm = (): void => {
  const pending = pendingBan.value
  if (pending === null) {
    return
  }

  // Closed when the page's request settles rather than here: this table is dumb, and `busy` is
  // what tells the dialog the answer is already with the server.
  emit('unban', pending.ipAddress)
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

onMounted(() => {
  ticker.value = setInterval(() => {
    now.value = Date.now()
  }, TICK_INTERVAL_MS)
})

// An interval outlives the component that started it, and this one closes over a ref: leaving it
// running would keep the whole table alive for as long as the tab is open.
onBeforeUnmount(() => {
  if (ticker.value !== null) {
    clearInterval(ticker.value)
  }
})
</script>

<template>
  <!-- The page puts this table in its panel. Machine text — the address, the failure count, the
       dates — is set in the mono face by its column's kind and never uppercased; the table scrolls
       inside its own container, so a phone never scrolls the page sideways. Cells do not wrap:
       a wrapped address or date would make a row two lines tall for no gain. -->
  <UiTable
    :caption="t('firewall.bans.tableCaption')"
    :columns="columns"
    :rows="bans"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="sortBy"
  >
    <template #cell-address="{ row }">{{ row.ipAddress }}</template>
    <template #cell-reason="{ row }">
      <UiBadge :variant="row.reason === 'bruteForce' ? 'danger' : 'neutral'">
        {{ t(`firewall.bans.reasons.${row.reason}`) }}
      </UiBadge>
    </template>
    <template #cell-failures="{ row }">{{ row.failures }}</template>
    <template #cell-bannedAt="{ row }">{{ formatIsoTimestamp(row.bannedAt, localeStore.current) }}</template>
    <template #cell-expiresAt="{ row }">{{ expiry(row) }}</template>
    <template #cell-actions="{ row }">
      <UiDropdown
        :label="t('common.actions')"
        :aria-label="t('firewall.bans.rowActions', { address: row.ipAddress })"
        align="end"
        variant="bare"
        :chevron="false"
        :disabled="busy"
      >
        <template #trigger>
          <UiIcon name="ellipsis" size="md" />
        </template>
        <UiDropdownItem @select="ask(row.id)">{{ t('firewall.bans.unban') }}</UiDropdownItem>
      </UiDropdown>
    </template>
  </UiTable>

  <!-- One dialog for the whole table rather than one per row: only one lift can be awaiting an
       answer, and the menu that opened it is already gone by the time it appears. Lifting a ban is
       not a removal, so the confirm button is not weighted as destructive. -->
  <UiConfirm
    :open="pendingId.length > 0"
    :title="confirmationTitle"
    :question="t('firewall.bans.confirmUnban')"
    :confirm-label="t('firewall.bans.confirm')"
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="busy"
    :acting-label="t('firewall.bans.working')"
    :destructive="false"
    @close="cancel"
    @confirm="confirm"
  />
</template>
