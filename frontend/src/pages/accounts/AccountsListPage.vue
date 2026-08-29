<script setup lang="ts">
/**
 * Accounts list screen: loading, empty and error states, then a table of
 * every hosting account the panel knows. Renders a `<section>`, not a
 * `<main>` — the single `<main>` landmark lives in the layout this page is
 * nested under. Reads state exclusively from the accounts store and
 * triggers a load on mount; it never touches the API layer directly
 * (rules/vue.md: API composables are called from stores only).
 */
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import UiTable from '../../components/ui/UiTable.vue'
import AccountStatusBadge from '../../components/accounts/AccountStatusBadge.vue'
import { useAccountsStore } from '../../stores/accounts'
import { useRouter } from 'vue-router'

const { t } = useI18n()
const store = useAccountsStore()
const router = useRouter()

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
</script>

<template>
  <section class="mx-auto max-w-4xl">
    <div class="mb-4 flex items-center justify-between">
      <h1 class="text-2xl font-semibold">{{ t('accounts.list.heading') }}</h1>
      <UiButton @click="goToCreate">{{ t('accounts.list.createAction') }}</UiButton>
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

    <UiTable v-else-if="store.accounts.length > 0" :caption="t('accounts.list.tableCaption')">
      <template #head>
        <tr>
          <th class="px-3 py-2 font-medium" scope="col">{{ t('accounts.list.columns.name') }}</th>
          <th class="px-3 py-2 font-medium" scope="col">{{ t('accounts.list.columns.primaryDomain') }}</th>
          <th class="px-3 py-2 font-medium" scope="col">{{ t('accounts.list.columns.status') }}</th>
          <th class="px-3 py-2 font-medium" scope="col">{{ t('accounts.list.columns.createdAt') }}</th>
        </tr>
      </template>
      <tr v-for="account in store.accounts" :key="account.id">
        <td class="px-3 py-2">{{ account.name }}</td>
        <td class="px-3 py-2">{{ account.primaryDomain }}</td>
        <td class="px-3 py-2"><AccountStatusBadge :status="account.status" /></td>
        <td class="px-3 py-2">{{ new Date(account.createdAt).toLocaleDateString() }}</td>
      </tr>
    </UiTable>
  </section>
</template>
