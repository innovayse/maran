<script setup lang="ts">
/**
 * The overview pane of a zone: what the zone is, how many records it holds, and where it stands
 * (its SOA serial, which moves on every change, and when it last did). Dumb by the usual contract:
 * the zone page owns the store; while the zone loads the details are drawn with placeholders in
 * place of each value (`UiDescriptionList loading`).
 */
import { useI18n } from 'vue-i18n'
import UiCard from '../../components/ui/UiCard.vue'
import UiDescriptionItem from '../../components/ui/UiDescriptionItem.vue'
import UiDescriptionList from '../../components/ui/UiDescriptionList.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import type { DnsZone } from '../../types/dns'

/** Props accepted by {@link DnsOverviewTab}. */
defineProps<{
  /** The zone, or `null` while it loads. */
  zone: DnsZone | null
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()
</script>

<template>
  <UiCard class="max-w-3xl">
    <UiDescriptionList :loading="zone === null">
      <UiDescriptionItem :term="t('dns.overview.name')" mono>
        <span class="break-all">{{ zone?.name }}</span>
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('dns.overview.nameservers')" mono>
        <span class="break-all">{{ zone?.nameservers.join(', ') }}</span>
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('dns.overview.owner')">{{ zone?.accountName }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('dns.overview.records')" mono>{{ zone?.recordCount }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('dns.overview.serial')" mono>{{ zone?.serial }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('dns.overview.createdAt')">
        {{ zone === null ? '' : formatDate(zone.createdAt, localeStore.current) }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('dns.overview.updatedAt')">
        {{ zone === null ? '' : formatDate(zone.updatedAt, localeStore.current) }}
      </UiDescriptionItem>
    </UiDescriptionList>
  </UiCard>
</template>
