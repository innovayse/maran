<script setup lang="ts">
/**
 * The facts of one reseller as a description list: its name (which is also its login), contact
 * address, status, the language its invitation was written in and its creation date.
 *
 * Dumb: the page passes the reseller. While it loads the card is drawn already with its real terms
 * and a placeholder line in place of each value, so the reseller lands in the space held for it.
 */
import { useI18n } from 'vue-i18n'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import ResellerStatusBadge from './ResellerStatusBadge.vue'
import type { AppLocale } from '../../types/app'
import type { ResellerDetail } from '../../types/reseller'
import { formatDate } from '../../utils/formatDate'

/** Props accepted by {@link ResellerDetailsCard}. */
defineProps<{
  /** The reseller to describe; `null` is the one state in which it is still on its way. */
  reseller: ResellerDetail | null
  /** The interface language the creation date is written in. */
  locale: AppLocale
}>()

const { t } = useI18n()
</script>

<template>
  <UiCard>
    <UiDescriptionList :loading="reseller === null">
      <UiDescriptionItem :term="t('common.name')" mono>{{ reseller?.name }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.form.fields.contactEmail')" mono>{{ reseller?.contactEmail }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.detail.statusLabel')">
        <ResellerStatusBadge v-if="reseller !== null" :status="reseller.status" :label="reseller.statusName" />
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.form.fields.language')">{{ reseller?.languageName }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.detail.createdAtLabel')" mono>
        {{ reseller === null ? '' : formatDate(reseller.createdAt, locale) }}
      </UiDescriptionItem>
    </UiDescriptionList>
  </UiCard>
</template>
