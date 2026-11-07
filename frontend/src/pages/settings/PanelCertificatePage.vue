<script setup lang="ts">
/**
 * The certificate the panel's own interface is served with (issue #132): the
 * trusted one the panel ordered for its host name, or the installer's
 * self-signed one, with the action that orders a trusted certificate. Renders a
 * `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout.
 *
 * A refused order leaves the certificate the panel was serving in place; the
 * panel's own sentence says why — a host name that does not point here, a
 * certificate authority that refused, or a server too close to full to write the
 * verification file.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiDescriptionItem from '../../components/ui/UiDescriptionItem.vue'
import UiDescriptionList from '../../components/ui/UiDescriptionList.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import { useLocaleStore } from '../../stores/locale'
import { usePanelCertificateStore } from '../../stores/panelCertificate'
import { formatDate } from '../../utils/formatDate'

const { t } = useI18n()
const certificateStore = usePanelCertificateStore()
const localeStore = useLocaleStore()

/** Whether the confirmation for returning to a self-signed certificate is open. */
const confirmingRestore: Ref<boolean> = ref(false)

/** True only while a trusted certificate is recorded: with none, there is nothing to return from. */
const canRestore: ComputedRef<boolean> = computed(() => {
  return certificateStore.status?.kind === 'trusted'
})

/**
 * Orders a certificate for the panel's host name.
 * @returns Resolves once the order has settled.
 */
const issue = async (): Promise<void> => {
  await certificateStore.issue()
}

/**
 * Opens the confirmation that names the consequence of returning to a self-signed certificate.
 * @returns Nothing.
 */
const askRestore = (): void => {
  confirmingRestore.value = true
}

/**
 * Closes the confirmation without acting; ignored while the request is in flight.
 * @returns Nothing.
 */
const cancelRestore = (): void => {
  if (!certificateStore.restoring) {
    confirmingRestore.value = false
  }
}

/**
 * Returns the panel to a self-signed certificate, then closes the confirmation whatever the
 * outcome: a refusal is shown by the page underneath.
 * @returns Resolves once the request has settled.
 */
const restore = async (): Promise<void> => {
  await certificateStore.restore()
  confirmingRestore.value = false
}

onMounted(() => {
  void certificateStore.load()
})
</script>

<template>
  <section class="flex w-full max-w-3xl flex-col gap-4">
    <UiPageHeading :title="t('panelCertificate.heading')" :subtitle="t('panelCertificate.subtitle')" />

    <!-- A refused order; a refused read is the region's own error below. -->
    <UiAlert v-if="certificateStore.orderMessage !== null" variant="error">
      {{ certificateStore.orderMessage }}
    </UiAlert>

    <UiAlert v-if="certificateStore.issued" variant="success">
      {{ t('panelCertificate.issued') }}
    </UiAlert>

    <!-- A refused return to the self-signed certificate; the trusted one stays in place. -->
    <UiAlert v-if="certificateStore.restoreMessage !== null" variant="error">
      {{ certificateStore.restoreMessage }}
    </UiAlert>

    <UiAlert v-if="certificateStore.restored" variant="success">
      {{ t('panelCertificate.restored') }}
    </UiAlert>

    <UiLoadingRegion :state="certificateStore.loadState">
      <template #error>
        <UiAlert variant="error">{{ certificateStore.loadMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>
      <UiCard>
        <UiSectionHeading class="mb-4" :title="t('panelCertificate.currentTitle')" />

        <UiDescriptionList v-if="certificateStore.status?.kind === 'trusted'">
          <UiDescriptionItem :term="t('panelCertificate.hostNameLabel')" mono>
            {{ certificateStore.status.certificate.hostName }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('panelCertificate.issuedAtLabel')" mono>
            {{ formatDate(certificateStore.status.certificate.issuedAt, localeStore.current) }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('panelCertificate.expiresAtLabel')" mono>
            {{ formatDate(certificateStore.status.certificate.expiresAt, localeStore.current) }}
          </UiDescriptionItem>
        </UiDescriptionList>

        <p v-else-if="certificateStore.status?.kind === 'selfSigned'" class="text-sm text-text-secondary">
          {{ t('panelCertificate.selfSigned') }}
        </p>

        <UiAlert v-else-if="certificateStore.status?.kind === 'noHostName'" variant="warning">
          {{ certificateStore.status.message }}
        </UiAlert>

        <div v-if="certificateStore.status !== null && certificateStore.status.kind !== 'noHostName'" class="mt-4 flex flex-col gap-2">
          <p class="text-sm text-text-muted">{{ t('panelCertificate.issueHint') }}</p>
          <div class="flex justify-end">
            <UiButton :disabled="certificateStore.issuing" @click="issue">
              {{
                certificateStore.issuing
                  ? t('panelCertificate.working')
                  : certificateStore.status.kind === 'trusted'
                    ? t('panelCertificate.renew')
                    : t('panelCertificate.issue')
              }}
            </UiButton>
          </div>
        </div>

        <div v-if="canRestore" class="mt-4 flex flex-col gap-2 border-t border-border-subtle pt-4">
          <p class="text-sm text-text-muted">{{ t('panelCertificate.restoreHint') }}</p>
          <div class="flex justify-end">
            <UiButton variant="secondary" :disabled="certificateStore.restoring" @click="askRestore">
              {{ t('panelCertificate.restore') }}
            </UiButton>
          </div>
        </div>
      </UiCard>
    </UiLoadingRegion>

    <UiConfirm
      :open="confirmingRestore"
      :title="t('panelCertificate.restoreTitle')"
      :question="t('panelCertificate.restoreQuestion')"
      :confirm-label="t('panelCertificate.restoreAction')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="certificateStore.restoring"
      :acting-label="t('panelCertificate.restoring')"
      @close="cancelRestore"
      @confirm="restore"
    />
  </section>
</template>
