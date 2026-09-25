<script setup lang="ts">
/**
 * A customer's own account screen: `/my-account`, showing the account's
 * identity, its lifecycle status and the limits of the plan it is created
 * against.
 *
 * Reads `GET /api/v1/accounts/me` through `stores/accounts.ts`'s `mine` state —
 * this page never touches an API composable directly (rules/vue.md). The
 * endpoint answers "the account YOU own" off the caller's own token, with no
 * id to pass, so nothing on this screen names an account either.
 *
 * The plan's display name arrives already localized from the backend and is
 * rendered as-is (rules/vue.md "Data comes from the backend"); the limit
 * figures are plain numbers the panel's own chrome labels.
 *
 * Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in
 * the layout this page is nested under.
 */
import { onMounted } from 'vue'
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

const { t } = useI18n()
const store = useAccountsStore()

onMounted(async () => {
  await store.loadMine()
})
</script>

<template>
  <section class="w-full">
    <UiSpinner v-if="store.mineLoading" :label="t('myAccount.loading')" />

    <template v-else-if="store.mine !== null">
      <UiPageHeading class="mb-4" :title="store.mine.name" :subtitle="store.mine.primaryDomain" />

      <UiAlert v-if="store.mineErrorMessage !== null" variant="error" class="mb-4">
        {{ store.mineErrorMessage }}
      </UiAlert>

      <UiCard class="mb-4">
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

      <UiSectionHeading
        class="mb-4"
        :title="t('myAccount.planHeading')"
        :subtitle="store.mine.plan.displayName"
      />

      <UiCard>
        <UiDescriptionList>
          <UiDescriptionItem :term="t('myAccount.limits.diskQuota')">
            {{ t('myAccount.limits.diskQuotaValue', { disk: store.mine.plan.diskQuotaMb }) }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('myAccount.limits.sites')">
            {{ store.mine.plan.maxSites }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('myAccount.limits.databases')">
            {{ store.mine.plan.maxDatabases }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('myAccount.limits.sftpUsers')">
            {{ store.mine.plan.maxSftpUsers }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('myAccount.limits.ftpUsers')">
            {{ store.mine.plan.maxFtpUsers }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('myAccount.limits.cronEntries')">
            {{ store.mine.plan.maxCronEntries }}
          </UiDescriptionItem>
        </UiDescriptionList>
      </UiCard>
    </template>

    <UiAlert v-else-if="store.mineErrorMessage !== null" variant="error">
      {{ store.mineErrorMessage }}
    </UiAlert>

    <UiEmptyState v-else :title="t('myAccount.notFoundTitle')" :description="t('myAccount.notFoundDescription')" />
  </section>
</template>
