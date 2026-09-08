<script setup lang="ts">
/**
 * A site's TLS certificates: what is installed, and the three things that can be done —
 * request one from the panel, install the customer's own, remove one.
 *
 * The tab takes the site's DOMAIN as well as its id, because that is what the panel's certificate
 * endpoints are addressed by: a certificate covers a domain, and the panel resolves the site from
 * it. Passing only an id and having this component look the domain up would be a second place that
 * decides which site a certificate belongs to.
 *
 * The private key is typed, submitted and forgotten: it is never read back into this component
 * after the request settles, and no panel screen displays one.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiDescriptionItem from '../../components/ui/UiDescriptionItem.vue'
import UiDescriptionList from '../../components/ui/UiDescriptionList.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import UiTextarea from '../../components/ui/UiTextarea.vue'
import { useCertificatesStore } from '../../stores/certificates'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'

/** Props accepted by {@link SiteSslTab}. */
const props = defineProps<{
  /** The site whose certificates are shown. */
  siteId: string
  /** That site's primary domain, which is how the panel addresses its certificates. */
  domain: string
}>()

const { t } = useI18n()
const store = useCertificatesStore()
const localeStore = useLocaleStore()

/** Whether the upload form is open. */
const isUploading: Ref<boolean> = ref(false)

/** The PEM-encoded chain the operator pasted. */
const certificatePem: Ref<string> = ref('')

/** The PEM-encoded private key the operator pasted. Cleared the moment the request settles. */
const privateKeyPem: Ref<string> = ref('')

/** Whether the upload form has been submitted, so nothing turns red before it is tried. */
const submitted: Ref<boolean> = ref(false)

/** The certificate awaiting a confirmed removal, or `null` when none is. */
const pendingRemovalId: Ref<string | null> = ref(null)

/** The domain of the certificate being removed, so the dialog names what it is asking about. */
const pendingRemovalDomain: ComputedRef<string> = computed(() => {
  const pending = store.certificates.find((certificate) => {
    return certificate.id === pendingRemovalId.value
  })
  return pending?.domain ?? props.domain
})

/** Validation message for the chain field, or `null`. */
const certificatePemError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  return certificatePem.value.trim().length === 0 ? t('certificates.errors.certificatePemRequired') : null
})

/** Validation message for the key field, or `null`. */
const privateKeyPemError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  return privateKeyPem.value.trim().length === 0 ? t('certificates.errors.privateKeyPemRequired') : null
})

/** Whether the panel answered and reported no certificate for this site. */
const isEmpty: ComputedRef<boolean> = computed(() => {
  return !store.loading && store.errorMessage === null && store.certificates.length === 0
})

/**
 * Loads the certificates installed for this site.
 * @returns Resolves once the request has settled, successfully or not.
 */
const load = async (): Promise<void> => {
  await store.load(props.siteId)
}

/**
 * Asks the panel to issue a certificate for this site's domain.
 * @returns Resolves once the request has settled.
 */
const issue = async (): Promise<void> => {
  await store.issue({ domain: props.domain })
}

/**
 * Opens the upload form.
 * @returns Nothing.
 */
const startUpload = (): void => {
  isUploading.value = true
}

/**
 * Closes the upload form and forgets the material typed into it.
 * @returns Nothing.
 */
const cancelUpload = (): void => {
  isUploading.value = false
  submitted.value = false
  certificatePem.value = ''
  privateKeyPem.value = ''
}

/**
 * Submits the customer's own certificate material.
 *
 * Both fields are cleared on success AND on failure: a private key left in a component's state
 * outlives the form that sent it, and a retry is a re-paste rather than a re-send of something
 * the operator can no longer see.
 * @returns Resolves once the attempt has settled.
 */
const submitUpload = async (): Promise<void> => {
  submitted.value = true
  if (certificatePemError.value !== null || privateKeyPemError.value !== null) {
    return
  }

  const installed = await store.installCustom({
    domain: props.domain,
    certificatePem: certificatePem.value,
    privateKeyPem: privateKeyPem.value,
  })

  certificatePem.value = ''
  privateKeyPem.value = ''
  if (installed) {
    isUploading.value = false
    submitted.value = false
  }
}

/**
 * Starts a removal, which then waits for confirmation.
 * @param id The certificate the operator clicked.
 * @returns Nothing.
 */
const askRemove = (id: string): void => {
  pendingRemovalId.value = id
}

/**
 * Abandons a pending removal.
 * @returns Nothing.
 */
const cancelRemove = (): void => {
  pendingRemovalId.value = null
}

/**
 * Removes the certificate whose removal was confirmed.
 * @returns Resolves once the request has settled.
 */
const confirmRemove = async (): Promise<void> => {
  const id = pendingRemovalId.value
  if (id !== null) {
    await store.remove(id)
  }

  // Closed after the request settles rather than before it is sent: the dialog holds the
  // spinner that tells the operator the panel is working.
  pendingRemovalId.value = null
}

onMounted(load)
</script>

<template>
  <div class="flex flex-col gap-3">
    <UiSpinner v-if="store.loading" :label="t('certificates.loading')" />

    <template v-else>
      <UiAlert v-if="store.errorMessage !== null" variant="error">{{ store.errorMessage }}</UiAlert>

      <UiEmptyState
        v-else-if="isEmpty"
        :title="t('certificates.emptyTitle')"
        :description="t('certificates.emptyDescription')"
      />

      <UiCard v-for="certificate in store.certificates" :key="certificate.id">
        <UiDescriptionList>
          <UiDescriptionItem :term="t('certificates.domainLabel')" mono>
            {{ certificate.domain }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('certificates.sourceLabel')">
            <UiBadge variant="info">{{ t(`certificates.source.${certificate.source}`) }}</UiBadge>
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('certificates.issuedAtLabel')">
            {{ formatDate(certificate.issuedAt, localeStore.current) }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('certificates.expiresAtLabel')">
            {{ formatDate(certificate.notAfter, localeStore.current) }}
          </UiDescriptionItem>
        </UiDescriptionList>
        <div class="mt-3 flex flex-wrap items-center gap-2">
          <UiButton variant="destructive" @click="askRemove(certificate.id)">
            {{ t('certificates.remove') }}
          </UiButton>
        </div>
      </UiCard>

      <!-- One dialog for the whole list rather than one per card: only one removal can be
           awaiting an answer, and a dialog rendered inside a `v-for` would be torn down by the
           list refresh its own confirmation triggers. -->
      <UiConfirm
        :open="pendingRemovalId !== null"
        :title="t('certificates.confirmRemoveTitle', { domain: pendingRemovalDomain })"
        :question="t('certificates.confirmRemove')"
        :confirm-label="t('common.confirm')"
        :cancel-label="t('common.cancel')"
        :close-label="t('common.close')"
        :acting="store.acting"
        :acting-label="t('certificates.working')"
        @close="cancelRemove"
        @confirm="confirmRemove"
      />

      <div class="flex flex-wrap items-center gap-2">
        <UiButton :disabled="store.acting" @click="issue">{{ t('certificates.issue') }}</UiButton>
        <UiButton v-if="!isUploading" variant="secondary" @click="startUpload">
          {{ t('certificates.installCustom') }}
        </UiButton>
      </div>
      <p class="text-sm text-text-muted">{{ t('certificates.issueHint') }}</p>

      <div v-if="isUploading" class="overflow-hidden rounded-xl border border-border-subtle bg-surface-1">
        <UiForm @submit="submitUpload">
          <div class="flex flex-col gap-3.5 p-4.5">
            <UiTextarea
              v-model="certificatePem"
              :label="t('certificates.fields.certificatePem')"
              :error="certificatePemError"
              :rows="6"
              required
            />
            <div>
              <UiTextarea
                v-model="privateKeyPem"
                :label="t('certificates.fields.privateKeyPem')"
                :error="privateKeyPemError"
                :rows="6"
                required
              />
              <p class="mt-1 text-sm text-text-muted">{{ t('certificates.hints.privateKeyPem') }}</p>
            </div>
          </div>
          <div class="flex justify-end gap-2 border-t border-border-subtle bg-surface-2 px-4.5 py-3">
            <UiButton variant="secondary" type="button" @click="cancelUpload">
              {{ t('common.cancel') }}
            </UiButton>
            <UiButton type="submit" :disabled="store.acting">{{ t('certificates.submitCustom') }}</UiButton>
          </div>
        </UiForm>
      </div>
    </template>
  </div>
</template>
