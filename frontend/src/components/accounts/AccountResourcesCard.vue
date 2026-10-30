<script setup lang="ts">
/**
 * What one hosting account holds against its plan: one row per resource the server counts, each a
 * way in to that module's list narrowed to this account (`?account=<id>`).
 *
 * Shown on the administrator's account page and on the customer's own account page. The card is
 * dumb: the page loads the summary through the accounts store and passes in where that read stands
 * — in flight, answered, refused with the server's message, or unanswered. A customer's list pages ignore `?account=`, because the server
 * already scopes every list to the account they own, so the same links serve both screens.
 *
 * **Every reading comes from an explicit field, never from a `null`.** A row whose `state` is
 * `unavailable` says so (cron's crontab is read from the host, which may not answer), because
 * "could not count" and "none" are different statements. A row whose `limitKind` is `unlimited`
 * (backups have no plan limit) shows its count and "no limit" with no bar, because a bar needs an
 * allowance to fill. A counted, limited row draws a `UiMeter`, which warns as the allowance fills.
 *
 * The server lists only the kinds whose module is installed, so no row ever leads to a module this
 * server lacks. The kind's label is the SPA's own vocabulary, the same one the plan limits use.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { RouteLocationRaw } from 'vue-router'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiMeter from '../ui/UiMeter.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import type {
  AccountResourceKind,
  AccountResourceSummaryRead,
  AccountResourceUsage,
} from '../../types/accountResourceSummary'

/** One resource as the card draws it. */
interface ResourceRow {
  /** Which resource the row counts; the list's key. */
  kind: AccountResourceKind
  /** The translated name of the resource. */
  label: string
  /** The row as the server sent it. */
  usage: AccountResourceUsage
  /** The figure printed beside the name, already translated. */
  figure: string
  /** The module list narrowed to this account. */
  to: RouteLocationRaw
}

/** Props accepted by {@link AccountResourcesCard}. */
const props = defineProps<{
  /** Where the read of the summary stands, and what it brought back. */
  read: AccountResourceSummaryRead
}>()

/**
 * The list page each kind links to. SFTP and FTPS logins share one screen, so both lead there.
 * Route names are the SPA's own navigation, not domain data.
 */
const ROUTE_OF_KIND: Readonly<Record<AccountResourceKind, string>> = {
  sites: 'sites',
  databases: 'databases',
  sftpLogins: 'file-transfer',
  ftpLogins: 'file-transfer',
  cronEntries: 'cron',
  backups: 'backups',
}

const { t } = useI18n()

/** The rows to draw, in the order the server sent them; empty until the summary has loaded. */
const rows: ComputedRef<ResourceRow[]> = computed(() => {
  if (props.read.state !== 'loaded') {
    return []
  }
  const summary = props.read.summary
  return summary.resources.map((usage) => {
    return {
      kind: usage.kind,
      label: t(`accounts.resources.kinds.${usage.kind}`),
      usage,
      figure: figureOf(usage),
      to: { name: ROUTE_OF_KIND[usage.kind], query: { account: summary.accountId } },
    }
  })
})

/**
 * Spells out one row's reading from its explicit state and limit kind.
 * @param usage The row as the server sent it.
 * @returns The translated figure: "unavailable", "3 of 10", or "3 · no limit".
 */
const figureOf = (usage: AccountResourceUsage): string => {
  if (usage.state === 'unavailable') {
    return t('accounts.resources.unavailable')
  }
  return usage.limitKind === 'limited'
    ? t('accounts.resources.usedOfLimit', { used: usage.used, limit: usage.limit })
    : t('accounts.resources.usedNoLimit', { used: usage.used })
}
</script>

<template>
  <UiCard data-testid="account-resources">
    <UiSectionHeading class="mb-4" :title="t('accounts.resources.heading')" />

    <UiSpinner v-if="read.state === 'loading'" :label="t('accounts.resources.loading')" />

    <UiAlert v-else-if="read.state === 'refused'" variant="error">{{ read.message }}</UiAlert>

    <UiAlert v-else-if="read.state === 'unreachable'" variant="error">
      {{ t('accounts.resources.unreachable') }}
    </UiAlert>

    <ul v-else class="flex flex-col gap-4">
      <li v-for="row in rows" :key="row.kind" class="flex min-w-0 flex-col gap-2" :data-testid="`resource-${row.kind}`">
        <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-1">
          <UiButton variant="quiet" :to="row.to">{{ row.label }}</UiButton>
          <span class="font-mono text-sm text-text-secondary tabular-nums wrap-anywhere">{{ row.figure }}</span>
        </div>
        <!-- A bar only where there is an allowance to fill and a reading to fill it with; the figure
             above already says it, so the meter does not print it twice. -->
        <UiMeter
          v-if="row.usage.state === 'counted' && row.usage.limitKind === 'limited'"
          :value="row.usage.used"
          :max="row.usage.limit"
          :label="row.label"
          :value-text="row.figure"
          :show-value="false"
        />
      </li>
    </ul>
  </UiCard>
</template>
