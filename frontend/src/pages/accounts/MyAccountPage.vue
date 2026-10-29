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
 * plan's CAPS, not usage — the endpoint carries no usage, so no meter is drawn
 * here: a bar with an invented fill would be a lie.
 *
 * A suspended account is the one thing that needs a customer's attention, so
 * it opens the page as a warning notice (amber, not blue), above the cards.
 *
 * Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in
 * the layout this page is nested under. The tiles wrap and never force a width
 * (`min-w-0`, `wrap-anywhere`), so nothing scrolls sideways at 390px in any of
 * the three languages.
 */
import { computed, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiDescriptionItem from '../../components/ui/UiDescriptionItem.vue'
import UiDescriptionList from '../../components/ui/UiDescriptionList.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import { useAccountsStore } from '../../stores/accounts'

/** One plan limit as a tile: its label, and the figure shown under it. */
interface LimitTile {
  /** Stable key for the list: the locale key of the label. */
  key: string
  /** The translated label. */
  label: string
  /** The figure, already formatted (the disk quota carries its unit). */
  value: string
}

const { t } = useI18n()
const store = useAccountsStore()

/**
 * The plan's limits as tiles, empty until the account has loaded. Built in one
 * place so the six tiles cannot drift apart in order or formatting.
 */
const limits: ComputedRef<LimitTile[]> = computed(() => {
  const plan = store.mine?.plan
  if (plan === undefined) {
    return []
  }
  return [
    {
      key: 'diskQuota',
      label: t('myAccount.limits.diskQuota'),
      value: t('myAccount.limits.diskQuotaValue', { disk: plan.diskQuotaMb }),
    },
    { key: 'sites', label: t('myAccount.limits.sites'), value: String(plan.maxSites) },
    { key: 'databases', label: t('myAccount.limits.databases'), value: String(plan.maxDatabases) },
    { key: 'sftpUsers', label: t('myAccount.limits.sftpUsers'), value: String(plan.maxSftpUsers) },
    { key: 'ftpUsers', label: t('myAccount.limits.ftpUsers'), value: String(plan.maxFtpUsers) },
    { key: 'cronEntries', label: t('myAccount.limits.cronEntries'), value: String(plan.maxCronEntries) },
  ]
})

onMounted(async () => {
  await store.loadMine()
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiSpinner v-if="store.mineLoading" :label="t('myAccount.loading')" />

    <template v-else-if="store.mine !== null">
      <UiPageHeading
        :eyebrow="t('myAccount.eyebrow')"
        :title="store.mine.name"
        mono
        :subtitle="store.mine.primaryDomain"
      />

      <UiAlert v-if="store.mine.status === 'suspended'" variant="warning">
        {{ t('myAccount.suspendedNotice') }}
      </UiAlert>

      <UiAlert v-if="store.mineErrorMessage !== null" variant="error">
        {{ store.mineErrorMessage }}
      </UiAlert>

      <UiCard>
        <UiSectionHeading class="mb-4" :title="t('myAccount.identityHeading')" />
        <UiDescriptionList>
          <UiDescriptionItem :term="t('common.name')">
            {{ store.mine.name }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('myAccount.primaryDomainLabel')" mono>
            {{ store.mine.primaryDomain }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('myAccount.statusLabel')">
            <UiBadge :variant="store.mine.status === 'active' ? 'success' : 'warning'">
              {{ t(`accounts.status.${store.mine.status}`) }}
            </UiBadge>
          </UiDescriptionItem>
        </UiDescriptionList>
      </UiCard>

      <UiCard>
        <UiSectionHeading
          class="mb-4"
          :title="t('myAccount.planHeading')"
          :subtitle="store.mine.plan.displayName"
        />
        <ul class="grid grid-cols-2 gap-3 lg:grid-cols-3">
          <li
            v-for="limit in limits"
            :key="limit.key"
            class="min-w-0 rounded-control border border-border-subtle bg-surface-2 p-4"
          >
            <p class="font-display text-xs font-extrabold tracking-caps text-text-secondary uppercase wrap-anywhere">
              {{ limit.label }}
            </p>
            <p class="mt-2 font-display text-2xl font-black text-text-primary tabular-nums wrap-anywhere">
              {{ limit.value }}
            </p>
          </li>
        </ul>
      </UiCard>
    </template>

    <UiAlert v-else-if="store.mineErrorMessage !== null" variant="error">
      {{ store.mineErrorMessage }}
    </UiAlert>

    <UiEmptyState v-else :title="t('myAccount.notFoundTitle')" :description="t('myAccount.notFoundDescription')" />
  </section>
</template>
