<script setup lang="ts">
/**
 * One installed certificate: the days it has left, coloured by how urgent that is, its domain,
 * source and dates, and the button that asks to remove it.
 *
 * It only shows what it is given. The parent works out the days left and the reason for
 * attention, and owns the removal: this card emits the request and the parent confirms it.
 *
 * With `loading` and no `certificate` the card stands in for a list still on its way: the figure
 * and every value draw a placeholder under their real labels (`UiDescriptionList loading`), and no
 * removal is offered, because there is nothing yet to remove.
 */
import { useI18n } from "vue-i18n";
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

/** Props accepted by {@link CertificateCard}. */
const props = defineProps<{
  /** The certificate exactly as the panel reported it; absent only while `loading`. */
  certificate?: Certificate;
  /** Whole days until it ends, never below zero, or `null` when the panel's timestamp is unreadable. */
  daysLeft: number | null;
  /** What is wrong with it, or `null` when nothing is. */
  attention: CertificateAttention | null;
  /** The interface language the dates are written in. */
  locale: AppLocale;
  /** Whether the card stands in for a certificate still loading; see the component note. */
  loading?: boolean;
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
  if (props.certificate !== undefined) {
    emit("remove", props.certificate.id);
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
        <div v-if="props.loading || props.daysLeft !== null" class="flex min-w-0 flex-col items-start">
          <p class="font-display text-xs font-extrabold text-text-muted wrap-anywhere">
            {{ t("certificates.daysLeftLabel") }}
          </p>
          <!-- The placeholder sits in the figure's own line, which gives it the figure's height. -->
          <p v-if="props.loading" class="font-display text-2xl font-extrabold">
            <span class="inline-flex w-12 align-middle"><UiSkeleton shape="line" size="lg" /></span>
          </p>
          <p
            v-else
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
      <UiButton v-if="!props.loading" variant="destructive" @click="remove">
        {{ t("certificates.remove") }}
      </UiButton>
    </div>

    <div class="mt-4 border-t border-border-subtle pt-4">
      <UiDescriptionList :loading="props.loading">
        <!-- The values are read only once there is a certificate: while loading each item draws a
             placeholder line in place of its slot. -->
        <UiDescriptionItem :term="t('certificates.domainLabel')" mono>
          {{ props.certificate?.domain }}
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('certificates.sourceLabel')">
          <UiBadge v-if="props.certificate !== undefined" variant="info">
            {{ t(`certificates.source.${props.certificate.source}`) }}
          </UiBadge>
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('certificates.issuedAtLabel')" mono>
          {{ props.certificate === undefined ? "" : formatDate(props.certificate.issuedAt, props.locale) }}
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('certificates.expiresAtLabel')" mono>
          {{ props.certificate === undefined ? "" : formatDate(props.certificate.notAfter, props.locale) }}
        </UiDescriptionItem>
      </UiDescriptionList>
    </div>
  </UiCard>
</template>
