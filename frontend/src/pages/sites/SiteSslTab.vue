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
 * The upload form is its own component and holds the pasted material; this tab installs what it
 * submits and closes it only once the panel has accepted the pair. A refusal keeps the form open,
 * and the tab hands it the panel's sentence for the field it is about.
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
 *
 * While the list loads, one certificate card is drawn with its real terms and placeholder values
 * ({@link UiLoadingRegion}, `CertificateCard loading`); a list that would not load is the region's
 * error. The actions below the list do not wait for it.
 */
import { differenceInMilliseconds, parseISO } from "date-fns";
import CertificateCard from "../../components/ssl/CertificateCard.vue";
import CustomCertificateForm from "../../components/ssl/CustomCertificateForm.vue";
import { computed, onMounted, ref, type ComputedRef, type Ref } from "vue";
import { useI18n } from "vue-i18n";
import UiAlert from "../../components/ui/UiAlert.vue";
import UiButton from "../../components/ui/UiButton.vue";
import UiCard from "../../components/ui/UiCard.vue";
import UiConfirm from "../../components/ui/UiConfirm.vue";
import UiEmptyState from "../../components/ui/UiEmptyState.vue";
import UiLoadingRegion from "../../components/ui/UiLoadingRegion.vue";
import { useCertificatesStore } from "../../stores/certificates";
import { useLocaleStore } from "../../stores/locale";
import type { Certificate, CertificateAttention, CustomCertificateMaterial } from "../../types/certificate";
import { formatDate } from "../../utils/formatDate";

/** One certificate laid out for the screen: the contract object plus what was worked out from it. */
interface CertificateRow {
  /** The certificate exactly as the panel reported it. */
  certificate: Certificate;
  /** Whole days until it ends, never below zero, or `null` when the panel's timestamp is unreadable. */
  daysLeft: number | null;
  /** What is wrong with it, or `null` when nothing is. */
  attention: CertificateAttention | null;
}

/**
 * Refusal codes that belong under the certificate text area. Read for placement only: the
 * sentence shown is the panel's own (rules/vue.md "Errors from the server").
 */
const CERTIFICATE_FIELD_CODES: ReadonlySet<string> = new Set([
  "CertificatePemRequired",
  "CertificatePemInvalid",
  "CertificatePemTooLarge",
  "AgentCertificateUnreadable",
]);

/** Refusal codes that belong under the private key text area, the mismatch among them. */
const PRIVATE_KEY_FIELD_CODES: ReadonlySet<string> = new Set([
  "PrivateKeyPemRequired",
  "PrivateKeyPemInvalid",
  "PrivateKeyPemTooLarge",
  "AgentPrivateKeyUnreadable",
  "AgentPrivateKeyMismatch",
]);

/** Milliseconds in a day. */
const DAY_MS = 86_400_000;

/** The panel renews its own certificates this many days before they end, and a customer's own warns from here. */
const RENEWAL_WINDOW_DAYS = 30;

/** An ACME certificate warns only this close to its end: inside the renewal window the panel is still due to act. */
const ACME_WARNING_DAYS = 14;

/** Props accepted by {@link SiteSslTab}. */
const props = defineProps<{
  /** The site whose certificates are shown. */
  siteId: string;
  /** That site's primary domain, which is how the panel addresses its certificates. */
  domain: string;
}>();

const { t } = useI18n();
const store = useCertificatesStore();
const localeStore = useLocaleStore();

/** Whether the upload form is open. */
const isUploading: Ref<boolean> = ref(false);

/** The certificate awaiting a confirmed removal, or `null` when none is. */
const pendingRemovalId: Ref<string | null> = ref(null);

/** The domain of the certificate being removed, so the dialog names what it is asking about. */
const pendingRemovalDomain: ComputedRef<string> = computed(() => {
  const pending = store.certificates.find((certificate) => {
    return certificate.id === pendingRemovalId.value;
  });
  return pending?.domain ?? props.domain;
});

/** The panel's refusal of the chain, or `null` when the last failure was not about that field. */
const certificatePemRefusal: ComputedRef<string | null> = computed(() => {
  return fieldRefusal(CERTIFICATE_FIELD_CODES);
});

/** The panel's refusal of the key, or `null` when the last failure was not about that field. */
const privateKeyPemRefusal: ComputedRef<string | null> = computed(() => {
  return fieldRefusal(PRIVATE_KEY_FIELD_CODES);
});

/** Whether the last failure is shown under a field, and so must not be repeated above the cards. */
const isFieldRefusal: ComputedRef<boolean> = computed(() => {
  return certificatePemRefusal.value !== null || privateKeyPemRefusal.value !== null;
});

/** The moment the tab opened, against which "days left" is counted. */
const openedAt: number = Date.now();

/** Every certificate with its remaining days and, when there is one, the reason it needs attention. */
const rows: ComputedRef<CertificateRow[]> = computed(() => {
  return store.certificates.map((certificate) => {
    return {
      certificate,
      daysLeft: daysLeftOf(certificate),
      attention: attentionOf(certificate),
    };
  });
});

/** The rows that need attention, which are announced above the cards. */
const flagged: ComputedRef<CertificateRow[]> = computed(() => {
  return rows.value.filter((row) => {
    return row.attention !== null;
  });
});

/** Whether the panel answered and reported no certificate for this site. */
const isEmpty: ComputedRef<boolean> = computed(() => {
  return store.listState === "loaded" && store.errorMessage === null && store.certificates.length === 0;
});

/**
 * Reads the panel's last refusal as a field message, when it is about one of the given codes.
 * @param codes The refusal codes that belong under the field.
 * @returns The panel's own sentence, or `null` when the last failure is about something else.
 */
const fieldRefusal = (codes: ReadonlySet<string>): string | null => {
  const code = store.errorCode;
  if (!isUploading.value || code === null || !codes.has(code)) {
    return null;
  }
  return store.errorMessage;
};

/**
 * Counts the whole days a certificate has left.
 * @param certificate The certificate to measure.
 * @returns Days until `notAfter`, floored at zero, or `null` when the timestamp cannot be read.
 */
const daysLeftOf = (certificate: Certificate): number | null => {
  const ends = parseISO(certificate.notAfter);
  if (Number.isNaN(ends.getTime())) {
    return null;
  }
  return Math.max(0, Math.ceil(differenceInMilliseconds(ends, openedAt) / DAY_MS));
};

/**
 * Decides whether a certificate needs the operator, and why.
 * @param certificate The certificate to judge.
 * @returns The reason, or `null` when the certificate is fine.
 */
const attentionOf = (certificate: Certificate): CertificateAttention | null => {
  const ends = parseISO(certificate.notAfter);
  if (Number.isNaN(ends.getTime())) {
    return null;
  }
  const left = differenceInMilliseconds(ends, openedAt) / DAY_MS;
  if (left <= 0) {
    return "expired";
  }
  const warnFrom = certificate.source === "acme" ? ACME_WARNING_DAYS : RENEWAL_WINDOW_DAYS;
  if (left <= warnFrom) {
    return "expiring";
  }
  return certificate.consecutiveRenewalFailures > 0 ? "renewalFailed" : null;
};

/**
 * Loads the certificates installed for this site.
 * @returns Resolves once the request has settled, successfully or not.
 */
const load = async (): Promise<void> => {
  await store.load(props.siteId);
};

/**
 * Asks the panel to issue a certificate for this site's domain.
 * @returns Resolves once the request has settled.
 */
const issue = async (): Promise<void> => {
  await store.issue({ domain: props.domain });
};

/**
 * Opens the upload form.
 * @returns Nothing.
 */
const startUpload = (): void => {
  isUploading.value = true;
};

/**
 * Closes the upload form, which forgets the material typed into it, and clears its refusal.
 * @returns Nothing.
 */
const cancelUpload = (): void => {
  isUploading.value = false;
  store.dismissError();
};

/**
 * Installs the customer's own certificate material.
 *
 * The form is closed only once the panel has installed the pair, and closing it is what forgets
 * the material. A refusal keeps it open: the refusal names which half is wrong, and emptying the
 * form would make the operator paste the half that was right as well. The key is never stored
 * anywhere but that open form.
 * @param material The chain and key the operator submitted.
 * @returns Resolves once the attempt has settled.
 */
const submitUpload = async (material: CustomCertificateMaterial): Promise<void> => {
  const installed = await store.installCustom({ domain: props.domain, ...material });
  if (installed) {
    isUploading.value = false;
  }
};

/**
 * Starts a removal, which then waits for confirmation.
 * @param id The certificate the operator clicked.
 * @returns Nothing.
 */
const askRemove = (id: string): void => {
  pendingRemovalId.value = id;
};

/**
 * Abandons a pending removal.
 * @returns Nothing.
 */
const cancelRemove = (): void => {
  pendingRemovalId.value = null;
};

/**
 * Removes the certificate whose removal was confirmed.
 * @returns Resolves once the request has settled.
 */
const confirmRemove = async (): Promise<void> => {
  const id = pendingRemovalId.value;
  if (id !== null) {
    await store.remove(id);
  }

  // Closed after the request settles rather than before it is sent: the dialog holds the
  // spinner that tells the operator the panel is working.
  pendingRemovalId.value = null;
};

onMounted(load);
</script>

<template>
  <div class="flex flex-col gap-4">
    <UiLoadingRegion :state="store.listState">
      <template #error>
        <UiAlert variant="error">{{ store.errorMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>

      <!-- How many certificates the site has is not known until the panel answers; one card holds
           the list's place, with the real terms of a certificate. -->
      <CertificateCard
        v-if="store.listState === 'loading'"
        loading
        :days-left="null"
        :attention="null"
        :locale="localeStore.current"
      />

      <div v-else class="flex flex-col gap-4">
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
              {{ t("certificates.renew") }}
            </UiButton>
          </template>
        </UiAlert>

        <UiAlert v-if="store.errorMessage !== null && !isFieldRefusal" variant="error">
          {{ store.errorMessage }}
        </UiAlert>

        <UiEmptyState
          v-else-if="isEmpty"
          :title="t('certificates.emptyTitle')"
          :description="t('certificates.emptyDescription')"
        />

        <CertificateCard
          v-for="row in rows"
          :key="row.certificate.id"
          :certificate="row.certificate"
          :days-left="row.daysLeft"
          :attention="row.attention"
          :locale="localeStore.current"
          @remove="askRemove"
        />
      </div>
    </UiLoadingRegion>

    <!-- One dialog for the whole list rather than one per card: only one removal can be
         awaiting an answer, and a dialog rendered inside a `v-for` would be torn down by the
         list refresh its own confirmation triggers. -->
    <UiConfirm
      :open="pendingRemovalId !== null"
      :title="t('certificates.confirmRemoveTitle', { domain: pendingRemovalDomain })"
      :question="t('certificates.confirmRemove')"
      :confirm-label="t('certificates.remove')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="store.acting"
      :acting-label="t('certificates.working')"
      @close="cancelRemove"
      @confirm="confirmRemove"
    />

    <UiCard>
      <div class="flex flex-wrap items-center gap-2">
        <UiButton :disabled="store.acting" @click="issue">{{ t("certificates.issue") }}</UiButton>
        <UiButton v-if="!isUploading" variant="secondary" @click="startUpload">
          {{ t("certificates.installCustom") }}
        </UiButton>
      </div>
      <p class="mt-3 text-sm text-text-muted">{{ t("certificates.issueHint") }}</p>
    </UiCard>

    <CustomCertificateForm
      v-if="isUploading"
      :acting="store.acting"
      :certificate-pem-refusal="certificatePemRefusal"
      :private-key-pem-refusal="privateKeyPemRefusal"
      @submit="submitUpload"
      @cancel="cancelUpload"
    />
  </div>
</template>
