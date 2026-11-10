<script setup lang="ts">
/**
 * Import an account from a package another Maran server exported (issue #127). The administrator
 * uploads the package and types the fingerprint of the key that signed it; the panel verifies the
 * signature before it accepts a byte of the artifact, and nothing about any account changes. The
 * import's own screen opens when the package is accepted, with the dry run waiting on it.
 *
 * Administrators only: the endpoint refuses every other role, and the route's `adminOnly` is advice.
 * Renders a `<section>`, not a `<main>`.
 */
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import AccountImportUploadForm from '../../components/migration/AccountImportUploadForm.vue'
import { useAccountImportsStore } from '../../stores/accountImports'

const { t } = useI18n()
const router = useRouter()
const store = useAccountImportsStore()

/**
 * Uploads the package and opens the import's screen when the panel accepted it.
 * @param file The package file.
 * @param fingerprint The sending server's key fingerprint, as typed.
 * @returns Resolves once the upload has settled and the navigation was dispatched.
 */
const upload = async (file: File, fingerprint: string): Promise<void> => {
  const staged = await store.stage(file, fingerprint)
  if (staged !== null) {
    await router.push({ name: 'account-import-detail', params: { id: staged.id } })
  }
}

onMounted(() => {
  store.clearErrors()
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('accountMove.import.heading')" :subtitle="t('accountMove.import.subtitle')" />

    <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>

    <AccountImportUploadForm :submitting="store.busy === 'stage'" @submit="upload" />
  </section>
</template>
