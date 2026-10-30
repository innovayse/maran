<script setup lang="ts">
/**
 * Sites list screen: every site on this server and whether it is serving. It resolves to one of
 * four states — loading, an error, the empty state, or the list — and the list is the only one
 * with anything to act on.
 *
 * Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout
 * this page is nested under. State comes exclusively from the sites store; the page never
 * touches the API layer (rules/vue.md: API composables are called from stores only).
 *
 * Lessons this page carries:
 * - The domain is a link, not text. Every action a site has — the PHP version, enable, disable,
 *   delete, its logs, its certificate — lives on the detail page, so a row that cannot be opened
 *   would put all of them out of reach. It is also the ONLY link in a row, and its name is the
 *   bare domain: the end-to-end specs find the way in by that name.
 * - PHP runtimes are a property of this server that only sites consume, so the link to them lives
 *   here, beside the create action. It used to be reachable only from the account menu, where it
 *   had nothing to do with the person signed in.
 * - The backend sends an empty PHP version for a non-PHP site. That is an absence, and it is shown
 *   as one rather than as a blank cell a reader would mistake for missing data.
 * - A server's error is rendered verbatim by the alert, and the page draws no table and no empty
 *   state beside it: "nothing here" and "could not ask" are different statements.
 *
 * The one thing that needs attention goes first: a disabled site is not serving its visitors, so
 * when there is one the list opens with an amber notice saying how many. The heading of the page is
 * the only heading on it — a second one containing the word "sites" would make the page's name
 * ambiguous to a screen reader and to the specs that find it by role and name.
 */
import { computed, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiActiveFilter from '../../components/ui/UiActiveFilter.vue'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiIconTile from '../../components/ui/UiIconTile.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import UiToast from '../../components/ui/UiToast.vue'
import UiTable from '../../components/ui/UiTable.vue'
import UiTableCell from '../../components/ui/UiTableCell.vue'
import UiTableHeaderCell from '../../components/ui/UiTableHeaderCell.vue'
import UiTableRow from '../../components/ui/UiTableRow.vue'
import SiteStatusBadge from '../../components/sites/SiteStatusBadge.vue'
import { useAccountChoice } from '../../composables/useAccountChoice'
import { useAccountFilter } from '../../composables/useAccountFilter'
import { useCallerRole } from '../../composables/useCallerRole'
import { useLocaleStore } from '../../stores/locale'
import { useSitesStore } from '../../stores/sites'
import { formatDate } from '../../utils/formatDate'
import { takeFlashNotice } from '../../utils/flashNotice'

const { t } = useI18n()
const store = useSitesStore()
const localeStore = useLocaleStore()
const { isAdmin } = useCallerRole()
const { load: loadAccounts } = useAccountChoice()
const { filter: accountListFilter, isActive: isAccountFiltered, text: accountFilterText, clear: clearAccountFilter } = useAccountFilter()

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** Whether the panel answered successfully and reported no sites at all. */
const isEmpty: ComputedRef<boolean> = computed(() => {
  return store.isLoaded && store.sites.length === 0
})

/** How many of the listed sites are disabled, i.e. answer with a suspension page. */
const disabledCount: ComputedRef<number> = computed(() => {
  return store.sites.filter((site) => {
    return site.status === 'disabled'
  }).length
})

/**
 * Loads the site list, narrowed to the account the URL names when it names one.
 * @returns Resolves when the load has settled, successfully or not.
 */
const refresh = async (): Promise<void> => {
  // The accounts are read only to name the account the list is narrowed to; an unfiltered list
  // does not need them and does not ask.
  const filter = accountListFilter.value
  await Promise.all(isAccountFiltered.value ? [store.load(filter), loadAccounts()] : [store.load(filter)])
}

/**
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}

onMounted(async () => {
  notice.value = takeFlashNotice()
  await refresh()
})

// "Show all" and a link to another account change only the query, so the page is not remounted.
watch(() => {return accountListFilter.value.accountId}, refresh)
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading
      :eyebrow="t('sites.list.eyebrow')"
      :title="t('sites.list.heading')"
      :subtitle="t('sites.list.subtitle')"
    >
      <template #actions>
        <!-- PHP versions are an administrator's server setting; hidden from a customer as
             presentation only, the endpoint refuses them on its own (rules/security.md). -->
        <UiButton v-if="isAdmin" variant="secondary" :to="{ name: 'php-versions' }">{{ t('sites.list.phpVersionsLink') }}</UiButton>
        <UiButton :to="{ name: 'sites-new' }">{{ t('sites.list.createAction') }}</UiButton>
      </template>
    </UiPageHeading>

    <UiActiveFilter
      v-if="isAccountFiltered"
      :text="accountFilterText"
      :clear-label="t('common.accountFilter.showAll')"
      @clear="clearAccountFilter"
    />

    <UiSpinner v-if="store.loading" :label="t('sites.list.loading')" />

    <UiAlert v-else-if="store.errorMessage !== null" variant="error">{{ store.errorMessage }}</UiAlert>

    <UiEmptyState
      v-else-if="isEmpty"
      :title="t('sites.list.emptyTitle')"
      :description="t('sites.list.emptyDescription')"
    >
      <template #icon><UiIcon name="earth" /></template>
      <UiButton :to="{ name: 'sites-new' }">{{ t('sites.list.createAction') }}</UiButton>
    </UiEmptyState>

    <template v-else-if="store.sites.length > 0">
      <UiAlert v-if="disabledCount > 0" variant="warning">
        {{ t('sites.list.disabledNotice', { count: disabledCount }, disabledCount) }}
      </UiAlert>

      <UiCard>
        <UiTable :caption="t('sites.list.tableCaption')">
          <template #head>
            <UiTableRow>
              <UiTableHeaderCell>{{ t('sites.list.columns.domain') }}</UiTableHeaderCell>
              <UiTableHeaderCell>{{ t('sites.list.columns.backendType') }}</UiTableHeaderCell>
              <UiTableHeaderCell>{{ t('sites.list.columns.phpVersion') }}</UiTableHeaderCell>
              <UiTableHeaderCell>{{ t('sites.list.columns.status') }}</UiTableHeaderCell>
              <UiTableHeaderCell align="end">{{ t('sites.list.columns.createdAt') }}</UiTableHeaderCell>
            </UiTableRow>
          </template>
          <UiTableRow v-for="site in store.sites" :key="site.id">
            <!-- A domain is an identifier: one unit, so it never breaks at a hyphen; the table scrolls. -->
            <UiTableCell class="whitespace-nowrap">
              <div class="flex min-w-48 items-center gap-3">
                <UiIconTile name="earth" />
                <!-- The way in: everything that can be done to a site lives on its detail page. -->
                <RouterLink
                  :to="{ name: 'site-detail', params: { id: site.id } }"
                  class="inline-flex min-h-6 items-center rounded-control font-mono font-semibold text-accent-text hover:underline focus-visible:shadow-focus focus-visible:outline-none"
                  >{{ site.domain }}</RouterLink
                >
              </div>
            </UiTableCell>
            <UiTableCell tone="secondary">{{ t(`sites.backendType.${site.backendType}`) }}</UiTableCell>
            <UiTableCell mono tone="secondary">
              {{ site.phpVersion.length > 0 ? site.phpVersion : t('common.emptyValue') }}
            </UiTableCell>
            <UiTableCell><SiteStatusBadge :status="site.status" /></UiTableCell>
            <UiTableCell mono tone="muted" align="end" class="whitespace-nowrap">
              {{ formatDate(site.createdAt, localeStore.current) }}
            </UiTableCell>
          </UiTableRow>
        </UiTable>
      </UiCard>
    </template>
    <div class="pointer-events-none fixed inset-x-4 bottom-4 z-50 flex justify-end sm:left-auto">
      <UiToast v-if="notice !== null" variant="success" :close-label="t('common.close')" @dismiss="dismissNotice">
        {{ notice }}
      </UiToast>
    </div>
  </section>
</template>
