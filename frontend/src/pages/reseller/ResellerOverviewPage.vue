<script setup lang="ts">
/**
 * A reseller's own overview, its home in place of the server dashboard (which is not theirs): what
 * it may sell (its envelope), what it has used of that, and the two places it works, plans and
 * accounts. Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout
 * this page is nested under. State comes from the resellers store; the page never touches the API
 * layer (rules/vue.md).
 *
 * The envelope is shown read only: only an administrator changes it, and the server enforces every
 * figure on each plan and account the reseller writes. A suspended reseller still reads this page
 * (its token lives a few minutes) and is told why its writes are refused. An administrator who is
 * viewing as the reseller lands here too; the endpoint answers the viewed reseller either way.
 */
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import ResellerEnvelopeCard from '../../components/resellers/ResellerEnvelopeCard.vue'
import ResellerUsageCard from '../../components/resellers/ResellerUsageCard.vue'
import { usePlansStore } from '../../stores/plans'
import { useResellersStore } from '../../stores/resellers'

const { t } = useI18n()
const store = useResellersStore()
const plansStore = usePlansStore()

onMounted(async () => {
  // The feature names are their own read: a failed one only leaves the envelope naming features by their wire name.
  await Promise.all([store.loadMine(), plansStore.loadFeatureChoices()])
})
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiPageHeading
      :title="t('resellers.overview.heading')"
      :subtitle="store.mine === null ? t('resellers.overview.subtitle') : store.mine.name"
      :loading="store.mineState === 'loading'"
    >
      <template #actions>
        <UiButton variant="secondary" :to="{ name: 'plans' }">{{ t('resellers.overview.plansAction') }}</UiButton>
        <UiButton :to="{ name: 'accounts' }">{{ t('resellers.overview.accountsAction') }}</UiButton>
      </template>
    </UiPageHeading>

    <UiLoadingRegion :state="store.mineState">
      <template #error>
        <UiAlert variant="error">{{ store.mineErrorMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>

      <div class="flex flex-col gap-4">
        <UiAlert v-if="store.mine?.status === 'suspended'" variant="warning">
          {{ t('resellers.overview.suspendedNotice') }}
        </UiAlert>

        <div class="grid gap-4 lg:grid-cols-3">
          <ResellerEnvelopeCard class="lg:col-span-2" :envelope="store.mine?.envelope ?? null" :feature-choices="plansStore.featureChoices" />
          <ResellerUsageCard :reseller="store.mine" />
        </div>
      </div>
    </UiLoadingRegion>
  </section>
</template>
