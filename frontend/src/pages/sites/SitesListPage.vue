<script setup lang="ts">
/**
 * Sites list screen: loading, empty and error states, then a row per site the panel reports.
 * Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout
 * this page is nested under. State comes exclusively from the sites store; the page never
 * touches the API layer (rules/vue.md: API composables are called from stores only).
 *
 * The domain is a link, not text. Every action a site has — the PHP version, enable, disable,
 * delete, its logs, its certificate — lives on the detail page, so a row that cannot be opened
 * would put all of them out of reach.
 */
import { computed, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import UiTable from '../../components/ui/UiTable.vue'
import UiTableCell from '../../components/ui/UiTableCell.vue'
import UiTableHeaderCell from '../../components/ui/UiTableHeaderCell.vue'
import UiTableRow from '../../components/ui/UiTableRow.vue'
import SiteStatusBadge from '../../components/sites/SiteStatusBadge.vue'
import { useLocaleStore } from '../../stores/locale'
import { useSitesStore } from '../../stores/sites'
import { formatDate } from '../../utils/formatDate'

const { t } = useI18n()
const router = useRouter()
const store = useSitesStore()
const localeStore = useLocaleStore()

/** Whether the panel answered successfully and reported no sites at all. */
const isEmpty: ComputedRef<boolean> = computed(() => {
  return store.isLoaded && store.sites.length === 0
})

/**
 * Loads the site list once the page is mounted.
 * @returns Resolves when the load has settled, successfully or not.
 */
const refresh = async (): Promise<void> => {
  await store.load()
}

/**
 * Opens the create-site form.
 * @returns Resolves once navigation has been dispatched.
 */
const goToCreate = async (): Promise<void> => {
  await router.push({ name: 'sites-new' })
}

onMounted(refresh)
</script>

<template>
  <section class="w-full">
    <div class="mb-4 flex flex-wrap items-end justify-between gap-4">
      <div>
        <h1 class="text-3xl font-semibold tracking-title text-text-primary">
          {{ t('sites.list.heading') }}
        </h1>
        <p class="mt-1 text-base text-text-secondary">{{ t('sites.list.subtitle') }}</p>
      </div>
      <UiButton @click="goToCreate">{{ t('sites.list.createAction') }}</UiButton>
    </div>

    <UiSpinner v-if="store.loading" :label="t('sites.list.loading')" />

    <UiAlert v-else-if="store.errorMessage !== null" variant="error">{{ store.errorMessage }}</UiAlert>

    <UiEmptyState
      v-else-if="isEmpty"
      :title="t('sites.list.emptyTitle')"
      :description="t('sites.list.emptyDescription')"
    >
      <UiButton @click="goToCreate">{{ t('sites.list.createAction') }}</UiButton>
    </UiEmptyState>

    <UiTable v-else-if="store.sites.length > 0" :caption="t('sites.list.tableCaption')">
      <template #head>
        <UiTableRow>
          <UiTableHeaderCell>{{ t('sites.list.columns.domain') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('sites.list.columns.backendType') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('sites.list.columns.phpVersion') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('sites.list.columns.status') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('sites.list.columns.createdAt') }}</UiTableHeaderCell>
        </UiTableRow>
      </template>
      <UiTableRow v-for="site in store.sites" :key="site.id">
        <UiTableCell class="font-mono font-medium">
          <!-- The way in: everything that can be done to a site lives on its detail page. -->
          <RouterLink
            :to="{ name: 'site-detail', params: { id: site.id } }"
            class="text-accent hover:underline focus-visible:underline focus-visible:outline-none"
            >{{ site.domain }}</RouterLink
          >
        </UiTableCell>
        <UiTableCell class="text-text-secondary">{{ t(`sites.backendType.${site.backendType}`) }}</UiTableCell>
        <!-- The backend sends an empty version for a non-PHP site; that is an absence, and it is
             shown as one rather than as a blank cell a reader would mistake for missing data. -->
        <UiTableCell class="font-mono text-text-secondary">
          {{ site.phpVersion.length > 0 ? site.phpVersion : t('sites.list.noPhpVersion') }}
        </UiTableCell>
        <UiTableCell><SiteStatusBadge :status="site.status" /></UiTableCell>
        <UiTableCell class="font-mono text-text-muted">
          {{ formatDate(site.createdAt, localeStore.current) }}
        </UiTableCell>
      </UiTableRow>
    </UiTable>
  </section>
</template>
