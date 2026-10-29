<script setup lang="ts">
/**
 * Accounts list screen: loading, empty and error states, then one panel holding the filter and a
 * table of every hosting account the panel knows. Renders a `<section>`, not a `<main>` — the
 * single `<main>` landmark lives in the layout this page is nested under. Reads state exclusively
 * from the accounts store and triggers a load on mount; it never touches the API layer directly
 * (rules/vue.md: API composables are called from stores only).
 *
 * The layout is the approved dashboard's accounts table: an initials avatar beside the account's
 * name, the domain in the monospace face, a status badge and the creation date right-aligned. It
 * renders only what the backend sends — a name, a primary domain, a plan id, a status and a
 * creation instant. The mockup's plan pill, site count and disk meter are deliberately absent: the
 * list endpoint carries none of them, and the plan name would need the plans request, whose failure
 * the store reports through the same `errorMessage` this page renders in place of the table — so a
 * plans hiccup would blank a healthy list (rules/vue.md: "Data comes from the backend").
 *
 * Lessons kept from the earlier page:
 * - The account's name is a link to its detail page. A row that shows an account but cannot open
 *   it leaves suspend, reactivate and delete unreachable from the panel entirely.
 * - The name cell holds ONLY the link as its accessible name (the avatar is `aria-hidden`), and the
 *   domain has its own cell: a cell named "alpha alpha.example.com" is not the cell named "alpha"
 *   the specs and a screen-reader user look for.
 * - The backend's own error text is rendered verbatim, never replaced by SPA copy.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiAvatar from '../../components/ui/UiAvatar.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
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
import type { Account } from '../../types/account'

const { t } = useI18n()
const store = useAccountsStore()
const localeStore = useLocaleStore()

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
 * Empties the filter so the full list comes back.
 * @returns Nothing; the computed list re-evaluates synchronously.
 */
const clearQuery = (): void => {
  query.value = ''
}
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiPageHeading
      :eyebrow="t('accounts.list.eyebrow')"
      :title="t('accounts.list.heading')"
      :subtitle="t('accounts.list.subtitle')"
    >
      <template #actions>
        <UiButton :to="{ name: 'accounts-new' }">{{ t('accounts.list.createAction') }}</UiButton>
      </template>
    </UiPageHeading>

    <UiSpinner v-if="store.loading" :label="t('accounts.list.loading')" />

    <UiAlert v-else-if="store.errorMessage !== null" variant="error">{{ store.errorMessage }}</UiAlert>

    <UiEmptyState
      v-else-if="store.isLoaded && store.accounts.length === 0"
      :title="t('accounts.list.emptyTitle')"
      :description="t('accounts.list.emptyDescription')"
    >
      <UiButton :to="{ name: 'accounts-new' }">{{ t('accounts.list.createAction') }}</UiButton>
    </UiEmptyState>

    <UiCard v-else-if="store.accounts.length > 0">
      <div class="mb-4 flex flex-wrap items-center gap-x-4 gap-y-2">
        <UiSearchInput
          v-model="query"
          class="min-w-55 max-w-85 flex-1"
          :label="t('accounts.list.searchLabel')"
          :placeholder="t('accounts.list.searchPlaceholder')"
          :clear-label="t('accounts.list.searchClear')"
        />
        <span class="ml-auto font-mono text-sm text-text-muted tabular-nums">
          {{ t('accounts.list.resultCount', { shown: visibleAccounts.length, total: store.accounts.length }) }}
        </span>
      </div>

      <UiEmptyState
        v-if="hasNoMatches"
        :title="t('accounts.list.noMatchesTitle', { query })"
        :description="t('accounts.list.noMatchesDescription')"
      >
        <UiButton variant="secondary" @click="clearQuery">{{ t('accounts.list.clearSearch') }}</UiButton>
      </UiEmptyState>

      <UiTable v-else :caption="t('accounts.list.tableCaption')">
        <template #head>
          <UiTableRow>
            <UiTableHeaderCell>{{ t('common.name') }}</UiTableHeaderCell>
            <UiTableHeaderCell>{{ t('accounts.list.columns.primaryDomain') }}</UiTableHeaderCell>
            <UiTableHeaderCell>{{ t('accounts.list.columns.status') }}</UiTableHeaderCell>
            <UiTableHeaderCell align="end">{{ t('accounts.list.columns.createdAt') }}</UiTableHeaderCell>
          </UiTableRow>
        </template>
        <UiTableRow v-for="account in visibleAccounts" :key="account.id">
          <!-- An account name and a domain are identifiers: one unit each. `whitespace-nowrap` is what
               keeps `acme-industrial-holdings` from breaking at its hyphens into three lines (a hyphen
               is a break opportunity), so the column takes the identifier's width and the table
               scrolls inside its own container. -->
          <UiTableCell class="whitespace-nowrap">
            <div class="flex items-center gap-3">
              <UiAvatar :name="account.name" size="sm" />
              <!-- The name is the way in to the account's detail page. -->
              <RouterLink
                :to="{ name: 'account-detail', params: { id: account.id } }"
                class="inline-flex min-h-6 items-center rounded-control font-mono font-semibold text-accent-text hover:underline focus-visible:shadow-focus focus-visible:underline"
                >{{ account.name }}</RouterLink
              >
            </div>
          </UiTableCell>
          <UiTableCell mono tone="secondary" class="whitespace-nowrap">{{ account.primaryDomain }}</UiTableCell>
          <UiTableCell><AccountStatusBadge :status="account.status" /></UiTableCell>
          <!-- A timestamp is context, never the thing being scanned for: the faintest text tier. -->
          <UiTableCell mono align="end" tone="muted" class="whitespace-nowrap">
            {{ formatDate(account.createdAt, localeStore.current) }}
          </UiTableCell>
        </UiTableRow>
      </UiTable>
    </UiCard>
  </section>
</template>
