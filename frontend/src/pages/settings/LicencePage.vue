<script setup lang="ts">
/**
 * The panel's licence (issue #8): its status, this server's ID with the link to claim a licence for it
 * (issue #536), and the form that installs a new one. Renders a
 * `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout.
 *
 * Administrators only: the route flag is advice and the endpoints refuse everyone else. The install
 * form sits outside the status region on purpose, so a licence can still be installed when the
 * status could not be read.
 */
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import LicenceInstallForm from '../../components/licence/LicenceInstallForm.vue'
import LicenceServerCard from '../../components/licence/LicenceServerCard.vue'
import LicenceStatusCard from '../../components/licence/LicenceStatusCard.vue'
import { useLicenceStore } from '../../stores/licence'

const { t } = useI18n()
const licenceStore = useLicenceStore()

onMounted(() => {
  // A previous visit's install outcome does not describe this one.
  licenceStore.resetInstall()
  void licenceStore.load()
})
</script>

<template>
  <section class="flex w-full max-w-3xl flex-col gap-4">
    <UiPageHeading :title="t('licence.heading')" :subtitle="t('licence.subtitle')" />

    <UiLoadingRegion :state="licenceStore.loadState">
      <template #error>
        <UiAlert variant="error">{{ licenceStore.error ?? t('app.status.unreachable') }}</UiAlert>
      </template>
      <LicenceStatusCard v-if="licenceStore.state !== null" :status="licenceStore.state" />
    </UiLoadingRegion>

    <LicenceServerCard
      v-if="licenceStore.state !== null"
      :server-id="licenceStore.state.serverId"
      :claim-url="licenceStore.state.claimUrl"
    />

    <LicenceInstallForm />
  </section>
</template>
