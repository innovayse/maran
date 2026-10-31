<script setup lang="ts">
/**
 * A customer's own account screen: `/my-account`, showing the account's
 * identity, its lifecycle status and the limits of the plan it is created
 * against.
 *
 * Reads `GET /api/v1/accounts/me` through `stores/accounts.ts`'s `mine` state —
 * this page never touches an API composable directly (rules/vue.md). The
 * endpoint answers "the account YOU own" off the caller's own token, with no
 * id to pass, so nothing on this screen names an account either. That is what
 * keeps the page tenant-scoped: there is no id in the URL or the store call to
 * swap for another account's, and no administrator action (suspend, delete,
 * change plan) is offered here — those belong to the administrator's account
 * screens, and the backend refuses them to a customer regardless of what this
 * page draws.
 *
 * The plan's display name arrives already localized from the backend and is
 * rendered as-is (rules/vue.md "Data comes from the backend"); the limit
 * figures are plain numbers the panel's own chrome labels. The limits are the
 * plan's CAPS; what the account uses against them is a separate read,
 * `GET /api/v1/accounts/me/resource-summary`, drawn by `AccountResourcesCard`
 * below them, so a failed count never blanks the plan.
 *
 * A suspended account is the one thing that needs a customer's attention, so
 * it opens the page as a warning notice (amber, not blue), above the cards.
 *
 * Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in
 * the layout this page is nested under. The tiles wrap and never force a width
 * (`min-w-0`, `wrap-anywhere`), so nothing scrolls sideways at 390px in any of
 * the three languages.
 *
 * While the account loads, the heading, the identity card and the six limit tiles are drawn with
 * their real labels and a placeholder in place of each value ({@link UiLoadingRegion},
 * `UiDescriptionList loading`, a `kpi`-sized figure line per tile), so the account lands in the
 * space already held. A caller who owns no account gets the empty state once the answer is in.
 */
import { computed, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiDescriptionItem from '../../components/ui/UiDescriptionItem.vue'
import UiDescriptionList from '../../components/ui/UiDescriptionList.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiSkeleton from '../../components/ui/UiSkeleton.vue'
import AccountResourcesCard from '../../components/accounts/AccountResourcesCard.vue'
import { useAccountsStore } from '../../stores/accounts'
import type { MyAccountPlan } from '../../types/account'
import { formatMegabytes } from '../../utils/formatBytes'

/** The plan limits the page shows, by the name the locale file uses for each tile's label. */
type LimitKey = 'diskQuota' | 'sites' | 'databases' | 'sftpUsers' | 'ftpUsers' | 'cronEntries'

/** One plan limit as a tile: its label, and the figure shown under it. */
interface LimitTile {
  /** Stable key for the list, and the name of the label in the locale file. */
  key: LimitKey
  /** The translated label. */
  label: string
  /** The figure, already formatted (the disk quota carries its unit); empty until the plan is in. */
  value: string
}

/**
 * How each tile's figure is read from the plan, in the order the tiles are drawn. One table, so
 * the six tiles cannot drift apart in order or formatting.
 */
const LIMIT_FIGURES: Readonly<Record<LimitKey, (plan: MyAccountPlan) => string>> = {
  diskQuota: (plan) => {
    return formatMegabytes(plan.diskQuotaMb)
  },
  sites: (plan) => {
    return String(plan.maxSites)
  },
  databases: (plan) => {
    return String(plan.maxDatabases)
  },
  sftpUsers: (plan) => {
    return String(plan.maxSftpUsers)
  },
  ftpUsers: (plan) => {
    return String(plan.maxFtpUsers)
  },
  cronEntries: (plan) => {
    return String(plan.maxCronEntries)
  },
}

const { t } = useI18n()
const store = useAccountsStore()

/** Whether the account is still on its way, so every value draws a placeholder. */
const isLoading: ComputedRef<boolean> = computed(() => {
  return store.mineState === 'loading'
})

/**
 * The plan's limits as tiles. The tiles exist while the account loads too, labelled, so the
 * placeholders stand exactly where the figures will.
 */
const limits: ComputedRef<LimitTile[]> = computed(() => {
  const plan = store.mine?.plan
  return (Object.keys(LIMIT_FIGURES) as LimitKey[]).map((key) => {
    return {
      key,
      label: t(`myAccount.limits.${key}`),
      value: plan === undefined ? '' : LIMIT_FIGURES[key](plan),
    }
  })
})

onMounted(async () => {
  await Promise.all([store.loadMine(), store.loadMineResourceSummary()])
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiLoadingRegion :state="store.mineState">
      <template #error>
        <UiAlert variant="error">{{ store.mineErrorMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>

      <!-- Answered, and the caller owns no account. -->
      <UiEmptyState
        v-if="!isLoading && store.mine === null"
        :title="t('myAccount.notFoundTitle')"
        :description="t('myAccount.notFoundDescription')"
      />

      <div v-else class="flex flex-col gap-4">
        <UiPageHeading
          :title="store.mine?.name ?? ''"
          mono
          :subtitle="store.mine?.primaryDomain ?? ''"
          :loading="isLoading"
        />

        <UiAlert v-if="!isLoading && store.mine?.status === 'suspended'" variant="warning">
          {{ t('myAccount.suspendedNotice') }}
        </UiAlert>

        <UiCard>
          <UiSectionHeading class="mb-4" :title="t('myAccount.identityHeading')" />
          <UiDescriptionList :loading="isLoading">
            <UiDescriptionItem :term="t('common.name')">
              {{ store.mine?.name }}
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('myAccount.primaryDomainLabel')" mono>
              {{ store.mine?.primaryDomain }}
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('myAccount.statusLabel')">
              <UiBadge v-if="store.mine !== null" :variant="store.mine.status === 'active' ? 'success' : 'warning'">
                {{ t(`accounts.status.${store.mine.status}`) }}
              </UiBadge>
            </UiDescriptionItem>
          </UiDescriptionList>
        </UiCard>

        <UiCard>
          <UiSectionHeading
            class="mb-4"
            :title="t('myAccount.planHeading')"
            :subtitle="store.mine?.plan.displayName ?? ''"
          />
          <ul class="grid grid-cols-2 gap-3 lg:grid-cols-3">
            <li
              v-for="limit in limits"
              :key="limit.key"
              class="min-w-0 rounded-control border border-border-subtle bg-surface-2 p-4"
            >
              <p class="font-display text-xs font-extrabold text-text-secondary wrap-anywhere">
                {{ limit.label }}
              </p>
              <!-- The placeholder sits inside the figure's own line, which gives it the figure's height. -->
              <p class="mt-2 font-display text-2xl font-black text-text-primary tabular-nums wrap-anywhere">
                <span v-if="isLoading" class="inline-flex w-16 align-middle"><UiSkeleton shape="line" size="lg" /></span>
                <template v-else>{{ limit.value }}</template>
              </p>
            </li>
          </ul>
        </UiCard>
      </div>
    </UiLoadingRegion>

    <!-- Outside the account's region: the summary is its own read with its own loading state. -->
    <AccountResourcesCard v-if="isLoading || store.mine !== null" :read="store.mineResourceSummary" />
  </section>
</template>
