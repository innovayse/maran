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
import { onBeforeUnmount, onMounted, ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge from '../ui/UiBadge.vue'
import UiButton from '../ui/UiButton.vue'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiTable from '../ui/UiTable.vue'
import UiTableCell from '../ui/UiTableCell.vue'
import UiTableHeaderCell from '../ui/UiTableHeaderCell.vue'
import UiTableRow from '../ui/UiTableRow.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatCountdown } from '../../utils/formatCountdown'
import { formatDate } from '../../utils/formatDate'
import type { Ban } from '../../types/firewall'

/** Props accepted by {@link FirewallBansTable}. */
defineProps<{
  /** The bans the panel reported, newest first. */
  bans: readonly Ban[]
  /** Whether a change is already in flight, which disables every row's menu. */
  busy: boolean
}>()

/** Events emitted by {@link FirewallBansTable}. */
const emit = defineEmits<{
  /** Fired when the operator confirmed lifting every ban in force for one address. */
  (e: 'unban', address: string): void
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
 * @param address The address to let back in.
 * @returns Nothing; emits synchronously.
 */
const confirm = (address: string): void => {
  pendingId.value = ''
  emit('unban', address)
}

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
  <UiTable :caption="t('firewall.bans.tableCaption')">
    <template #head>
      <UiTableRow>
        <UiTableHeaderCell>{{ t('firewall.bans.columns.address') }}</UiTableHeaderCell>
        <UiTableHeaderCell>{{ t('firewall.bans.columns.reason') }}</UiTableHeaderCell>
        <UiTableHeaderCell>{{ t('firewall.bans.columns.failures') }}</UiTableHeaderCell>
        <UiTableHeaderCell>{{ t('firewall.bans.columns.bannedAt') }}</UiTableHeaderCell>
        <UiTableHeaderCell>{{ t('firewall.bans.columns.expiresAt') }}</UiTableHeaderCell>
        <UiTableHeaderCell align="end">{{ t('firewall.bans.columns.actions') }}</UiTableHeaderCell>
      </UiTableRow>
    </template>
    <UiTableRow v-for="ban in bans" :key="ban.id">
      <UiTableCell class="font-mono font-medium">{{ ban.ipAddress }}</UiTableCell>
      <UiTableCell>
        <UiBadge :variant="ban.reason === 'bruteForce' ? 'danger' : 'neutral'">
          {{ t(`firewall.bans.reasons.${ban.reason}`) }}
        </UiBadge>
      </UiTableCell>
      <UiTableCell class="font-mono text-text-secondary">{{ ban.failures }}</UiTableCell>
      <UiTableCell class="font-mono text-text-muted">
        {{ formatDate(ban.bannedAt, localeStore.current) }}
      </UiTableCell>
      <UiTableCell class="text-text-secondary">{{ expiry(ban) }}</UiTableCell>
      <UiTableCell align="end">
        <div class="flex flex-wrap items-center justify-end gap-2">
          <!-- The confirmation stays inline rather than becoming a second menu item: a menu is a
               list of things one might do, and a question already asked belongs where the answer
               is given. -->
          <template v-if="pendingId === ban.id">
            <span class="text-sm text-text-secondary">{{ t('firewall.bans.confirmUnban') }}</span>
            <UiButton variant="destructive" :disabled="busy" @click="confirm(ban.ipAddress)">
              {{ t('firewall.bans.confirm') }}
            </UiButton>
            <UiButton variant="secondary" @click="cancel">{{ t('firewall.bans.cancel') }}</UiButton>
          </template>
          <UiDropdown
            v-else
            :label="t('firewall.bans.columns.actions')"
            :aria-label="t('firewall.bans.rowActions', { address: ban.ipAddress })"
            align="end"
            variant="bare"
            :chevron="false"
            :disabled="busy"
          >
            <template #trigger>
              <UiIcon name="ellipsis" size="md" />
            </template>
            <UiDropdownItem @select="ask(ban.id)">{{ t('firewall.bans.unban') }}</UiDropdownItem>
          </UiDropdown>
        </div>
      </UiTableCell>
    </UiTableRow>
  </UiTable>
</template>
