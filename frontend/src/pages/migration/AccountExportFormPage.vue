<script setup lang="ts">
/**
 * Export an account for another Maran server (issue #127). The administrator picks an account; the
 * panel takes a backup of it, gathers what every module hands over and signs the whole as a package.
 * The package's own screen opens when it is made, because everything after the export (the key
 * fingerprint to carry over, the download, the pull grants) belongs to that package.
 *
 * Administrators only: the endpoint refuses every other role, and the route's `adminOnly` is advice.
 * Renders a `<section>`, not a `<main>`.
 */
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import AccountExportForm from '../../components/migration/AccountExportForm.vue'
import { useAccountChoice } from '../../composables/useAccountChoice'
import { useAccountExportsStore } from '../../stores/accountExports'

const { t } = useI18n()
const router = useRouter()
const store = useAccountExportsStore()
const { picker: accountPicker, search: searchAccounts } = useAccountChoice()

/**
 * Exports the chosen account and opens the package's screen.
 * @param accountId The account the administrator chose.
 * @returns Resolves once the export has settled and the navigation was dispatched.
 */
const exportAccount = async (accountId: string): Promise<void> => {
  const made = await store.create(accountId)
  if (made !== null) {
    await router.push({ name: 'account-export-detail', params: { id: made.id } })
  }
}

onMounted(async () => {
  store.clearErrors()
  await searchAccounts('')
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('accountMove.export.heading')" :subtitle="t('accountMove.export.subtitle')" />

    <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>

    <AccountExportForm
      :account-picker="accountPicker"
      :submitting="store.busy === 'create'"
      @search-accounts="searchAccounts"
      @submit="exportAccount"
    />
  </section>
</template>
