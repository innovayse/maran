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
 *
 * The one thing that needs attention comes first. A certificate that has expired, is about to, or
 * that the panel keeps failing to renew is a notice ABOVE the cards, not a date to be noticed in
 * a list: the site is about to show every visitor a browser security warning. Running out is a
 * `warning` (amber) and not an `info`; only an already-expired certificate is an `error`. The
 * notice carries its action ("Renew now") so the fix is where the problem is announced.
 *
 * Every figure shown is read from the certificate the panel reported. The panel renews an ACME
 * certificate thirty days before it ends, so an ACME certificate inside that window is NOT yet a
 * problem (renewal simply has not run) and only warns at fourteen days or after a failed renewal;
 * a customer's own certificate is never renewed by the panel, so it warns from thirty days out.
 */
import { differenceInMilliseconds, parseISO } from 'date-fns'
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
import UiIconTile from '../../components/ui/UiIconTile.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import UiTextarea from '../../components/ui/UiTextarea.vue'
import { useCertificatesStore } from '../../stores/certificates'
import { useLocaleStore } from '../../stores/locale'
import type { Certificate } from '../../types/certificate'
import { formatDate } from '../../utils/formatDate'

/** Why a certificate needs the operator's attention, most urgent first. */
type CertificateAttention = 'expired' | 'expiring' | 'renewalFailed'

/** One certificate laid out for the screen: the contract object plus what was worked out from it. */
interface CertificateRow {
  /** The certificate exactly as the panel reported it. */
  certificate: Certificate
  /** Whole days until it ends, never below zero, or `null` when the panel's timestamp is unreadable. */
  daysLeft: number | null
  /** What is wrong with it, or `null` when nothing is. */
  attention: CertificateAttention | null
}

/** Milliseconds in a day. */
const DAY_MS = 86_400_000

/** The panel renews its own certificates this many days before they end, and a customer's own warns from here. */
const RENEWAL_WINDOW_DAYS = 30

/** An ACME certificate warns only this close to its end: inside the renewal window the panel is still due to act. */
const ACME_WARNING_DAYS = 14

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

/** The moment the tab opened, against which "days left" is counted. */
const openedAt: number = Date.now()

/** Every certificate with its remaining days and, when there is one, the reason it needs attention. */
const rows: ComputedRef<CertificateRow[]> = computed(() => {
  return store.certificates.map((certificate) => {
    return {
      certificate,
      daysLeft: daysLeftOf(certificate),
      attention: attentionOf(certificate),
    }
  })
})

/** The rows that need attention, which are announced above the cards. */
const flagged: ComputedRef<CertificateRow[]> = computed(() => {
  return rows.value.filter((row) => {
    return row.attention !== null
  })
})

/** Whether the panel answered and reported no certificate for this site. */
const isEmpty: ComputedRef<boolean> = computed(() => {
  return !store.loading && store.errorMessage === null && store.certificates.length === 0
})

/**
 * Counts the whole days a certificate has left.
 * @param certificate The certificate to measure.
 * @returns Days until `notAfter`, floored at zero, or `null` when the timestamp cannot be read.
 */
const daysLeftOf = (certificate: Certificate): number | null => {
  const ends = parseISO(certificate.notAfter)
  if (Number.isNaN(ends.getTime())) {
    return null
  }
  return Math.max(0, Math.ceil(differenceInMilliseconds(ends, openedAt) / DAY_MS))
}

/**
 * Decides whether a certificate needs the operator, and why.
 * @param certificate The certificate to judge.
 * @returns The reason, or `null` when the certificate is fine.
 */
const attentionOf = (certificate: Certificate): CertificateAttention | null => {
  const ends = parseISO(certificate.notAfter)
  if (Number.isNaN(ends.getTime())) {
    return null
  }
  const left = differenceInMilliseconds(ends, openedAt) / DAY_MS
  if (left <= 0) {
    return 'expired'
  }
  const warnFrom = certificate.source === 'acme' ? ACME_WARNING_DAYS : RENEWAL_WINDOW_DAYS
  if (left <= warnFrom) {
    return 'expiring'
  }
  return certificate.consecutiveRenewalFailures > 0 ? 'renewalFailed' : null
}

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
  <div class="flex flex-col gap-4">
    <UiSpinner v-if="store.loading" :label="t('certificates.loading')" />

    <template v-else>
      <!-- What needs attention goes first. Amber for "running out", red only once it has run out.
           These sit before the error/empty chain below, which must stay an unbroken v-if/v-else-if. -->
      <UiAlert
        v-for="row in flagged"
        :key="row.certificate.id"
        :variant="row.attention === 'expired' ? 'error' : 'warning'"
      >
        {{
          t(`certificates.alerts.${row.attention}`, {
            domain: row.certificate.domain,
            date: formatDate(row.certificate.notAfter, localeStore.current),
            count: row.certificate.consecutiveRenewalFailures,
          })
        }}
        <template #actions>
          <UiButton variant="secondary" :disabled="store.acting" @click="issue">
            {{ t('certificates.renew') }}
          </UiButton>
        </template>
      </UiAlert>

      <UiAlert v-if="store.errorMessage !== null" variant="error">{{ store.errorMessage }}</UiAlert>

      <UiEmptyState
        v-else-if="isEmpty"
        :title="t('certificates.emptyTitle')"
        :description="t('certificates.emptyDescription')"
      />

      <UiCard v-for="row in rows" :key="row.certificate.id">
        <div class="flex flex-wrap items-center justify-between gap-4">
          <div class="flex min-w-0 items-center gap-3">
            <UiIconTile name="shieldCheck" size="lg" />
            <div v-if="row.daysLeft !== null" class="min-w-0">
              <p class="font-display text-xs font-extrabold tracking-caps text-text-muted uppercase wrap-anywhere">
                {{ t('certificates.daysLeftLabel') }}
              </p>
              <p
                class="font-display text-2xl font-extrabold tabular-nums"
                :class="
                  row.attention === 'expired'
                    ? 'text-danger'
                    : row.attention === 'expiring'
                      ? 'text-warning'
                      : 'text-text-primary'
                "
              >
                {{ row.daysLeft }}
              </p>
            </div>
          </div>
          <UiButton variant="destructive" @click="askRemove(row.certificate.id)">
            {{ t('certificates.remove') }}
          </UiButton>
        </div>

        <div class="mt-4 border-t border-border-subtle pt-4">
          <UiDescriptionList>
            <UiDescriptionItem :term="t('certificates.domainLabel')" mono>
              {{ row.certificate.domain }}
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('certificates.sourceLabel')">
              <UiBadge variant="info">{{ t(`certificates.source.${row.certificate.source}`) }}</UiBadge>
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('certificates.issuedAtLabel')" mono>
              {{ formatDate(row.certificate.issuedAt, localeStore.current) }}
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('certificates.expiresAtLabel')" mono>
              {{ formatDate(row.certificate.notAfter, localeStore.current) }}
            </UiDescriptionItem>
          </UiDescriptionList>
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

      <UiCard>
        <div class="flex flex-wrap items-center gap-2">
          <UiButton :disabled="store.acting" @click="issue">{{ t('certificates.issue') }}</UiButton>
          <UiButton v-if="!isUploading" variant="secondary" @click="startUpload">
            {{ t('certificates.installCustom') }}
          </UiButton>
        </div>
        <p class="mt-3 text-sm text-text-muted">{{ t('certificates.issueHint') }}</p>
      </UiCard>

      <UiCard v-if="isUploading">
        <UiForm @submit="submitUpload">
          <div class="flex flex-col gap-4">
            <UiSectionHeading :title="t('certificates.installCustom')" />
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
            <div class="flex flex-wrap justify-end gap-2">
              <UiButton variant="secondary" type="button" @click="cancelUpload">
                {{ t('common.cancel') }}
              </UiButton>
              <UiButton type="submit" :disabled="store.acting">{{ t('certificates.submitCustom') }}</UiButton>
            </div>
          </div>
        </UiForm>
      </UiCard>
    </template>
  </div>
</template>
