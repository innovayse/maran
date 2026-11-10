<script setup lang="ts">
/**
 * One installed certificate: the days it has left, coloured by how urgent that is, its domain,
 * source and dates, and the button that asks to remove it.
 *
 * It only shows what it is given. The parent works out the days left and the reason for
 * attention, and owns the removal: this card emits the request and the parent confirms it.
 *
 * Its `read` is either a certificate with the figures worked out for it, or `loading`: then the card
 * stands in for a list still on its way, the figure and every value draw a placeholder under their
 * real labels (`UiDescriptionList loading`), and no removal is offered, because there is nothing yet
 * to remove.
 */
import { useI18n } from "vue-i18n";
import UiAlert from "../ui/UiAlert.vue";
import UiBadge from "../ui/UiBadge.vue";
import UiButton from "../ui/UiButton.vue";
import UiCard from "../ui/UiCard.vue";
import UiSkeleton from "../ui/UiSkeleton.vue";
import UiDescriptionItem from "../ui/UiDescriptionItem.vue";
import UiDescriptionList from "../ui/UiDescriptionList.vue";
import UiIconTile from "../ui/UiIconTile.vue";
import type { AppLocale } from "../../types/app";
import type { Certificate, CertificateAttention } from "../../types/certificate";
import { formatDate } from "../../utils/formatDate";
import { formatIsoTimestamp } from "../../utils/formatIsoTimestamp";

/**
 * What a {@link CertificateCard} is given to draw: the placeholder for a list still on its way, or
 * one certificate with the figures the parent worked out for it. The two cannot be mixed, so a
 * card is never asked to draw a missing certificate without saying it is loading.
 */
export type CertificateCardRead =
  | {
      /** The list has not answered yet. */
      state: "loading";
    }
  | {
      /** The panel reported this certificate. */
      state: "loaded";
      /** The certificate exactly as the panel reported it. */
      certificate: Certificate;
      /** Whole days until it ends, never below zero, or `null` when the panel's timestamp is unreadable. */
      daysLeft: number | null;
      /** What is wrong with it, or `null` when nothing is. */
      attention: CertificateAttention | null;
    };

/** Props accepted by {@link CertificateCard}. */
const props = defineProps<{
  /** What to draw: a certificate, or the placeholder while the list loads. */
  read: CertificateCardRead;
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
  if (props.read.state === "loaded") {
    emit("remove", props.read.certificate.id);
  }
};
</script>

<template>
  <UiCard>
    <div class="flex flex-wrap items-center justify-between gap-4">
      <div class="flex min-w-0 items-center gap-3">
        <UiIconTile name="shieldCheck" size="lg" />
        <!-- A column whose items keep their own width, so the label's box does not follow the
             figure's: the placeholder and the figure differ in width, the label never moves. -->
        <div v-if="props.read.state === 'loading' || props.read.daysLeft !== null" class="flex min-w-0 flex-col items-start">
          <p class="font-display text-xs font-extrabold text-text-muted wrap-anywhere">
            {{ t("certificates.daysLeftLabel") }}
          </p>
          <!-- The placeholder sits in the figure's own line, which gives it the figure's height. -->
          <p v-if="props.read.state === 'loading'" class="font-display text-2xl font-extrabold">
            <span class="inline-flex w-12 align-middle"><UiSkeleton shape="line" size="lg" /></span>
          </p>
          <p
            v-else
            class="font-display text-2xl font-extrabold tabular-nums"
            :class="
              props.read.attention === 'expired'
                ? 'text-danger'
                : props.read.attention === 'expiring'
                  ? 'text-warning'
                  : 'text-text-primary'
            "
          >
            {{ props.read.daysLeft }}
          </p>
        </div>
      </div>
      <UiButton v-if="props.read.state === 'loaded'" variant="destructive" @click="remove">
        {{ t("certificates.remove") }}
      </UiButton>
    </div>

    <div class="mt-4 border-t border-border-subtle pt-4">
      <UiDescriptionList :loading="props.read.state === 'loading'">
        <!-- The values are read only once there is a certificate: while loading each item draws a
             placeholder line in place of its slot, so the slots below stay empty then. -->
        <UiDescriptionItem :term="t('certificates.domainLabel')" mono>
          {{ props.read.state === "loaded" ? props.read.certificate.domain : "" }}
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('certificates.sourceLabel')">
          <span v-if="props.read.state === 'loaded'" class="flex flex-wrap items-center gap-2">
            <UiBadge variant="info">
              {{ t(`certificates.source.${props.read.certificate.source}`) }}
            </UiBadge>
            <UiBadge v-if="props.read.certificate.wildcard" variant="neutral">
              {{ t("certificates.wildcard.badge", { domain: props.read.certificate.domain }) }}
            </UiBadge>
          </span>
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('certificates.issuedAtLabel')" mono>
          {{ props.read.state === "loaded" ? formatDate(props.read.certificate.issuedAt, props.locale) : "" }}
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('certificates.expiresAtLabel')" mono>
          {{ props.read.state === "loaded" ? formatDate(props.read.certificate.notAfter, props.locale) : "" }}
        </UiDescriptionItem>
        <!-- Only the panel renews a certificate it issued; an uploaded one has no attempts to report. -->
        <UiDescriptionItem
          v-if="props.read.state === 'loading' || props.read.certificate.source === 'acme'"
          :term="t('certificates.lastRenewalAttemptLabel')"
          mono
        >
          <template v-if="props.read.state === 'loaded'">
            {{
              props.read.certificate.lastRenewalAttemptAt === null
                ? t("certificates.noRenewalAttempt")
                : formatIsoTimestamp(props.read.certificate.lastRenewalAttemptAt, props.locale)
            }}
          </template>
        </UiDescriptionItem>
      </UiDescriptionList>

      <!-- The sentence arrives from the panel already in the reader's language and is plain text. -->
      <UiAlert
        v-if="props.read.state === 'loaded' && props.read.certificate.consecutiveRenewalFailures > 0"
        class="mt-4"
        variant="warning"
      >
        <span class="flex flex-wrap items-center gap-2">
          <UiBadge variant="danger">
            {{ t("certificates.failedAttempts", { count: props.read.certificate.consecutiveRenewalFailures }) }}
          </UiBadge>
          <span v-if="props.read.certificate.lastRenewalError !== null" class="wrap-anywhere">
            {{ props.read.certificate.lastRenewalError }}
          </span>
        </span>
      </UiAlert>
    </div>
  </UiCard>
</template>
