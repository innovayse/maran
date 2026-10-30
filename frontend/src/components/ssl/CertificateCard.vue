<script setup lang="ts">
/**
 * One installed certificate: the days it has left, coloured by how urgent that is, its domain,
 * source and dates, and the button that asks to remove it.
 *
 * It only shows what it is given. The parent works out the days left and the reason for
 * attention, and owns the removal: this card emits the request and the parent confirms it.
 */
import { useI18n } from "vue-i18n";
import UiBadge from "../ui/UiBadge.vue";
import UiButton from "../ui/UiButton.vue";
import UiCard from "../ui/UiCard.vue";
import UiDescriptionItem from "../ui/UiDescriptionItem.vue";
import UiDescriptionList from "../ui/UiDescriptionList.vue";
import UiIconTile from "../ui/UiIconTile.vue";
import type { AppLocale } from "../../types/app";
import type { Certificate, CertificateAttention } from "../../types/certificate";
import { formatDate } from "../../utils/formatDate";

/** Props accepted by {@link CertificateCard}. */
const props = defineProps<{
  /** The certificate exactly as the panel reported it. */
  certificate: Certificate;
  /** Whole days until it ends, never below zero, or `null` when the panel's timestamp is unreadable. */
  daysLeft: number | null;
  /** What is wrong with it, or `null` when nothing is. */
  attention: CertificateAttention | null;
  /** The interface language the dates are written in. */
  locale: AppLocale;
}>();

const emit = defineEmits<{
  /**
   * The operator asked to remove this certificate.
   * @param e Event name.
   * @param id The certificate's id.
   */
  (e: "remove", id: string): void;
}>();

const { t } = useI18n();

/**
 * Asks the parent to remove this certificate.
 * @returns Nothing.
 */
const remove = (): void => {
  emit("remove", props.certificate.id);
};
</script>

<template>
  <UiCard>
    <div class="flex flex-wrap items-center justify-between gap-4">
      <div class="flex min-w-0 items-center gap-3">
        <UiIconTile name="shieldCheck" size="lg" />
        <div v-if="props.daysLeft !== null" class="min-w-0">
          <p class="font-display text-xs font-extrabold text-text-muted wrap-anywhere">
            {{ t("certificates.daysLeftLabel") }}
          </p>
          <p
            class="font-display text-2xl font-extrabold tabular-nums"
            :class="
              props.attention === 'expired'
                ? 'text-danger'
                : props.attention === 'expiring'
                  ? 'text-warning'
                  : 'text-text-primary'
            "
          >
            {{ props.daysLeft }}
          </p>
        </div>
      </div>
      <UiButton variant="destructive" @click="remove">
        {{ t("certificates.remove") }}
      </UiButton>
    </div>

    <div class="mt-4 border-t border-border-subtle pt-4">
      <UiDescriptionList>
        <UiDescriptionItem :term="t('certificates.domainLabel')" mono>
          {{ props.certificate.domain }}
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('certificates.sourceLabel')">
          <UiBadge variant="info">{{ t(`certificates.source.${props.certificate.source}`) }}</UiBadge>
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('certificates.issuedAtLabel')" mono>
          {{ formatDate(props.certificate.issuedAt, props.locale) }}
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('certificates.expiresAtLabel')" mono>
          {{ formatDate(props.certificate.notAfter, props.locale) }}
        </UiDescriptionItem>
      </UiDescriptionList>
    </div>
  </UiCard>
</template>
