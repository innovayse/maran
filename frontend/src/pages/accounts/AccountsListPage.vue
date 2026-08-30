<script setup lang="ts">
/**
 * Accounts list screen: loading, empty and error states, then a table of
 * every hosting account the panel knows. Renders a `<section>`, not a
 * `<main>` — the single `<main>` landmark lives in the layout this page is
 * nested under. Reads state exclusively from the accounts store and
 * triggers a load on mount; it never touches the API layer directly
 * (rules/vue.md: API composables are called from stores only).
 *
 * The layout follows the design canvas's data screens (page header row,
 * toolbar, bordered table panel). It renders only what the backend sends —
 * a name, a primary domain, a status and a creation instant — so the
 * design's fictional metrics, sparklines and bulk actions are deliberately
 * absent (rules/vue.md: "Data comes from the backend").
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import UiTable from '../../components/ui/UiTable.vue'
import UiTableCell from '../../components/ui/UiTableCell.vue'
import UiTableHeaderCell from '../../components/ui/UiTableHeaderCell.vue'
import UiTableRow from '../../components/ui/UiTableRow.vue'
import AccountStatusBadge from '../../components/accounts/AccountStatusBadge.vue'
import { useAccountsStore } from '../../stores/accounts'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import { useRouter } from 'vue-router'
import type { Account } from '../../types/account'

const { t } = useI18n()
const store = useAccountsStore()
const localeStore = useLocaleStore()
const router = useRouter()

/**
 * Free-text filter over the accounts already loaded. Purely a display
 * concern — it narrows what is on screen and never asks the backend for a
 * different set, so it adds no domain knowledge to the SPA.
 */
const query: Ref<string> = ref('')

/** The accounts matching the current query, matched on the two text columns the table shows. */
const visibleAccounts: ComputedRef<Account[]> = computed(() => {
  const needle = query.value.trim().toLowerCase()
  if (needle.length === 0) {
    return store.accounts
  }
  return store.accounts.filter((account) => {
    return account.name.toLowerCase().includes(needle) || account.primaryDomain.toLowerCase().includes(needle)
  })
})

/** Whether the panel reported accounts but the current query matches none of them. */
const hasNoMatches: ComputedRef<boolean> = computed(() => {
  return store.accounts.length > 0 && visibleAccounts.value.length === 0
})

/**
 * Kicks off the initial account list load once the page is mounted.
 * @returns Resolves when the load has settled (successfully or not).
 */
const refresh = async (): Promise<void> => {
  await store.load()
}

onMounted(refresh)

/**
 * Navigates to the create-account form.
 * @returns Resolves once navigation has been dispatched.
 */
const goToCreate = async (): Promise<void> => {
  await router.push({ name: 'accounts-new' })
}

/**
 * Empties the filter so the full list comes back.
 * @returns Nothing; the computed list re-evaluates synchronously.
 */
const clearQuery = (): void => {
  query.value = ''
}
</script>

<template>
  <section class="w-full">
    <div class="mb-4 flex flex-wrap items-end justify-between gap-4">
      <div>
        <h1 class="text-3xl font-semibold tracking-title text-text-primary">
          {{ t('accounts.list.heading') }}
        </h1>
        <p class="mt-1 text-base text-text-secondary">{{ t('accounts.list.subtitle') }}</p>
      </div>
      <UiButton @click="goToCreate">{{ t('accounts.list.createAction') }}</UiButton>
    </div>

    <div v-if="store.accounts.length > 0" class="mb-3 flex flex-wrap items-center gap-2">
      <UiSearchInput
        v-model="query"
        class="min-w-[220px] max-w-[340px] flex-1"
        :label="t('accounts.list.searchLabel')"
        :placeholder="t('accounts.list.searchPlaceholder')"
        :clear-label="t('accounts.list.searchClear')"
      />
      <span class="ml-auto font-mono text-base text-text-muted">
        {{ t('accounts.list.resultCount', { shown: visibleAccounts.length, total: store.accounts.length }) }}
      </span>
    </div>

    <UiSpinner v-if="store.loading" :label="t('accounts.list.loading')" />

    <UiAlert v-else-if="store.errorMessage !== null" variant="error">{{ store.errorMessage }}</UiAlert>

    <UiEmptyState
      v-else-if="store.isLoaded && store.accounts.length === 0"
      :title="t('accounts.list.emptyTitle')"
      :description="t('accounts.list.emptyDescription')"
    >
      <UiButton @click="goToCreate">{{ t('accounts.list.createAction') }}</UiButton>
    </UiEmptyState>

    <UiEmptyState
      v-else-if="hasNoMatches"
      :title="t('accounts.list.noMatchesTitle', { query })"
      :description="t('accounts.list.noMatchesDescription')"
    >
      <UiButton variant="secondary" @click="clearQuery">{{ t('accounts.list.clearSearch') }}</UiButton>
    </UiEmptyState>

    <UiTable v-else-if="visibleAccounts.length > 0" :caption="t('accounts.list.tableCaption')">
      <template #head>
        <UiTableRow>
          <UiTableHeaderCell>{{ t('accounts.list.columns.name') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('accounts.list.columns.primaryDomain') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('accounts.list.columns.status') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('accounts.list.columns.createdAt') }}</UiTableHeaderCell>
        </UiTableRow>
      </template>
      <UiTableRow v-for="account in visibleAccounts" :key="account.id">
        <!-- The identifier column: monospace and the strongest text tone, so a
             column of names reads as a column of handles rather than prose. -->
        <UiTableCell class="font-mono font-medium">{{ account.name }}</UiTableCell>
        <!-- Secondary identifier: same monospace, one tone back. -->
        <UiTableCell class="font-mono text-text-secondary">{{ account.primaryDomain }}</UiTableCell>
        <UiTableCell><AccountStatusBadge :status="account.status" /></UiTableCell>
        <!-- Timestamps sit furthest back: they are context, never the thing being scanned for. -->
        <UiTableCell class="font-mono text-text-muted">
          {{ formatDate(account.createdAt, localeStore.current) }}
        </UiTableCell>
      </UiTableRow>
    </UiTable>
  </section>
</template>
