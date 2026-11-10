<script setup lang="ts">
/**
 * The DS record of a signed zone, and its DNSKEY, as copyable fields: what the owner enters at the
 * registrar so resolvers can validate the zone. Each field has its own copy control, because a
 * digest is sixty-four characters and a public key is longer, and retyping one is how a record
 * ends up subtly wrong. Dumb by the usual contract: the zone page owns the store.
 *
 * The panel publishes nothing at the registrar. This component only shows the values; the text
 * that tells the owner where to enter them sits beside it.
 */
import { ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { DnsDnskeyRecord, DnsDsRecord } from '../../types/dnsDnssec'

/** Props accepted by {@link DnssecDsRecord}. */
defineProps<{
  /** The DS record to publish at the registrar. */
  ds: DnsDsRecord
  /** The public key, for registrars that ask for the key rather than the DS record, or `null`. */
  dnskey: DnsDnskeyRecord | null
}>()

const { t } = useI18n()

/** The field whose value was last copied (`ds.digest`), so its control can say so; `null` before any copy. */
const copied: Ref<string | null> = ref(null)

/**
 * Copies a field's value to the clipboard.
 *
 * A failure is swallowed on purpose and leaves the control unchanged: the clipboard is blocked
 * outright in some browser configurations, and the value stays selectable on screen beside it.
 * @param field The field's identity, used to mark the copied control.
 * @param value The text to copy.
 * @returns Resolves once the clipboard write has settled, successfully or not.
 */
const copyValue = async (field: string, value: string): Promise<void> => {
  try {
    await navigator.clipboard.writeText(value)
    copied.value = field
  } catch {
    copied.value = null
  }
}

/**
 * Names a field's copy control for assistive technology, and says so once the value is on the clipboard.
 * @param field The field's identity, as {@link copyValue} marks it.
 * @param term The field's translated name.
 * @returns The control's accessible name.
 */
const copyLabel = (field: string, term: string): string => {
  return copied.value === field ? t('dns.dnssec.copied', { field: term }) : t('dns.dnssec.copy', { field: term })
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <UiSectionHeading :title="t('dns.dnssec.ds.title')" :subtitle="t('dns.dnssec.ds.subtitle')" />
    <UiDescriptionList>
      <UiDescriptionItem :term="t('dns.dnssec.ds.keyTag')" mono>
        <div class="flex min-w-0 items-center gap-2">
          <span class="min-w-0 break-all" data-testid="ds-keyTag">{{ ds.keyTag }}</span>
          <UiButton
            variant="ghost"
            size="icon-sm"
            :aria-label="copyLabel('ds.keyTag', t('dns.dnssec.ds.keyTag'))"
            @click="copyValue('ds.keyTag', String(ds.keyTag))"
          >
            <UiIcon :name="copied === 'ds.keyTag' ? 'check' : 'copy'" size="sm" />
          </UiButton>
        </div>
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('dns.dnssec.ds.algorithm')" mono>
        <div class="flex min-w-0 items-center gap-2">
          <span class="min-w-0 break-all" data-testid="ds-algorithm">{{ ds.algorithm }}</span>
          <UiButton
            variant="ghost"
            size="icon-sm"
            :aria-label="copyLabel('ds.algorithm', t('dns.dnssec.ds.algorithm'))"
            @click="copyValue('ds.algorithm', String(ds.algorithm))"
          >
            <UiIcon :name="copied === 'ds.algorithm' ? 'check' : 'copy'" size="sm" />
          </UiButton>
        </div>
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('dns.dnssec.ds.digestType')" mono>
        <div class="flex min-w-0 items-center gap-2">
          <span class="min-w-0 break-all" data-testid="ds-digestType">{{ ds.digestType }}</span>
          <UiButton
            variant="ghost"
            size="icon-sm"
            :aria-label="copyLabel('ds.digestType', t('dns.dnssec.ds.digestType'))"
            @click="copyValue('ds.digestType', String(ds.digestType))"
          >
            <UiIcon :name="copied === 'ds.digestType' ? 'check' : 'copy'" size="sm" />
          </UiButton>
        </div>
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('dns.dnssec.ds.digest')" mono>
        <div class="flex min-w-0 items-center gap-2">
          <span class="min-w-0 break-all" data-testid="ds-digest">{{ ds.digest }}</span>
          <UiButton
            variant="ghost"
            size="icon-sm"
            :aria-label="copyLabel('ds.digest', t('dns.dnssec.ds.digest'))"
            @click="copyValue('ds.digest', ds.digest)"
          >
            <UiIcon :name="copied === 'ds.digest' ? 'check' : 'copy'" size="sm" />
          </UiButton>
        </div>
      </UiDescriptionItem>
    </UiDescriptionList>

    <template v-if="dnskey !== null">
      <UiSectionHeading :title="t('dns.dnssec.dnskey.title')" :subtitle="t('dns.dnssec.dnskey.subtitle')" />
      <UiDescriptionList>
        <UiDescriptionItem :term="t('dns.dnssec.dnskey.flags')" mono>{{ dnskey.flags }}</UiDescriptionItem>
        <UiDescriptionItem :term="t('dns.dnssec.dnskey.protocol')" mono>{{ dnskey.protocol }}</UiDescriptionItem>
        <UiDescriptionItem :term="t('dns.dnssec.dnskey.algorithm')" mono>{{ dnskey.algorithm }}</UiDescriptionItem>
        <UiDescriptionItem :term="t('dns.dnssec.dnskey.publicKey')" mono>
          <div class="flex min-w-0 items-center gap-2">
            <span class="min-w-0 break-all" data-testid="dnskey-publicKey">{{ dnskey.publicKey }}</span>
            <UiButton
              variant="ghost"
              size="icon-sm"
              :aria-label="copyLabel('dnskey.publicKey', t('dns.dnssec.dnskey.publicKey'))"
              @click="copyValue('dnskey.publicKey', dnskey.publicKey)"
            >
              <UiIcon :name="copied === 'dnskey.publicKey' ? 'check' : 'copy'" size="sm" />
            </UiButton>
          </div>
        </UiDescriptionItem>
      </UiDescriptionList>
    </template>
  </div>
</template>
