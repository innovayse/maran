<script setup lang="ts">
/**
 * The values the panel writes into every zone it serves: the primary and the secondary name
 * server (the NS set and the glue a registrar needs), the hostmaster contact of the SOA, the public
 * addresses default records point at, and the default TTL. Saving re-renders every zone, so the
 * form says so.
 *
 * The checks are advice that saves a round trip (rules/vue.md: client rules mirror the server's
 * validator): host names and addresses by shape, the TTL against the bounds the panel's catalogue
 * states. The server validates everything again and its already-localized refusal is shown by the
 * page. Dumb by the usual contract: it never touches a store.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import { isHostname, isIpv4, isIpv6 } from '../../utils/dnsAddress'
import type { DnsRecordCatalog } from '../../types/dns'
import type { DnsSettings, SaveDnsSettingsRequest } from '../../types/dnsSettings'

/** Props accepted by {@link DnsNameserversForm}. */
const props = defineProps<{
  /** The settings as saved, which the draft starts from. */
  settings: DnsSettings
  /** The panel's catalogue for the TTL bounds, or `null` while it has not arrived. */
  catalog: DnsRecordCatalog | null
  /** Whether the save request is in flight, which disables submit. */
  saving: boolean
}>()

/** Events emitted by {@link DnsNameserversForm}. */
const emit = defineEmits<{
  /** Fired only when every field passes the client-side reading, carrying the settings. */
  (e: 'submit', settings: SaveDnsSettingsRequest): void
}>()

const { t } = useI18n()

/** The primary name server as typed. */
const primaryNameserver: Ref<string> = ref(props.settings.primaryNameserver)

/** The secondary name server as typed. */
const secondaryNameserver: Ref<string> = ref(props.settings.secondaryNameserver)

/** The hostmaster mailbox as typed. */
const hostmasterMailbox: Ref<string> = ref(props.settings.hostmasterMailbox)

/** The public IPv4 address as typed. */
const publicIpv4: Ref<string> = ref(props.settings.publicIpv4)

/** The public IPv6 address as typed. */
const publicIpv6: Ref<string> = ref(props.settings.publicIpv6)

/** The default TTL as typed. */
const defaultTtl: Ref<string> = ref(String(props.settings.defaultTtl))

/** Whether a submit has been attempted, so nothing turns red before the person has tried. */
const submitted: Ref<boolean> = ref(false)

/** The message for the primary name server, or `null`. */
const primaryError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const text = primaryNameserver.value.trim()
  if (text.length === 0) {
    return t('dns.nameservers.errors.primaryRequired')
  }
  return isHostname(text) ? null : t('dns.nameservers.errors.nameserverInvalid')
})

/** The message for the secondary name server, or `null`; empty is allowed. */
const secondaryError: ComputedRef<string | null> = computed(() => {
  const text = secondaryNameserver.value.trim()
  return submitted.value && text.length > 0 && !isHostname(text) ? t('dns.nameservers.errors.nameserverInvalid') : null
})

/** The message for the hostmaster, or `null`. */
const hostmasterError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  // The SOA form is a host name of at least two labels (`hostmaster.example.com`); an `@` is never part of it.
  const text = hostmasterMailbox.value.trim()
  return isHostname(text) && text.includes('.') ? null : t('dns.nameservers.errors.hostmasterInvalid')
})

/** The message for the IPv4 address, or `null`; empty is allowed. */
const ipv4Error: ComputedRef<string | null> = computed(() => {
  const text = publicIpv4.value.trim()
  return submitted.value && text.length > 0 && !isIpv4(text) ? t('dns.nameservers.errors.ipv4Invalid') : null
})

/** The message for the IPv6 address, or `null`; empty is allowed. */
const ipv6Error: ComputedRef<string | null> = computed(() => {
  const text = publicIpv6.value.trim()
  return submitted.value && text.length > 0 && !isIpv6(text) ? t('dns.nameservers.errors.ipv6Invalid') : null
})

/** The message for the default TTL, or `null`. */
const ttlError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const text = defaultTtl.value.trim()
  const seconds = /^\d+$/.test(text) ? Number(text) : Number.NaN
  if (Number.isNaN(seconds)) {
    return t('dns.record.errors.ttlInvalid')
  }
  const bounds = props.catalog
  if (bounds !== null && (seconds < bounds.minTtl || seconds > bounds.maxTtl)) {
    return t('dns.record.errors.ttlRange', { min: bounds.minTtl, max: bounds.maxTtl })
  }
  return null
})

/**
 * Emits the settings when every field passes the client-side reading; otherwise shows what is wrong.
 * @returns Nothing.
 */
const submit = (): void => {
  submitted.value = true
  const problems = [primaryError, secondaryError, hostmasterError, ipv4Error, ipv6Error, ttlError]
  if (
    problems.some((problem): boolean => {
      return problem.value !== null
    })
  ) {
    return
  }
  emit('submit', {
    primaryNameserver: primaryNameserver.value.trim(),
    secondaryNameserver: secondaryNameserver.value.trim(),
    hostmasterMailbox: hostmasterMailbox.value.trim(),
    publicIpv4: publicIpv4.value.trim(),
    publicIpv6: publicIpv6.value.trim(),
    defaultTtl: Number(defaultTtl.value.trim()),
  })
}
</script>

<template>
  <UiCard>
    <UiForm @submit="submit">
      <div class="flex flex-col gap-5">
        <UiSectionHeading :title="t('dns.nameservers.settingsHeading')" :subtitle="t('dns.nameservers.settingsSubtitle')" />
        <div class="grid gap-4 md:grid-cols-2">
          <UiInput
            v-model="primaryNameserver"
            :label="t('dns.nameservers.fields.primaryNameserver')"
            :error="primaryError"
            mono
            required
          />
          <UiInput
            v-model="secondaryNameserver"
            :label="t('dns.nameservers.fields.secondaryNameserver')"
            :error="secondaryError"
            mono
          />
        </div>
        <UiFieldHint>{{ t('dns.nameservers.hints.nameservers') }}</UiFieldHint>
        <div class="grid gap-4 md:grid-cols-2">
          <UiInput
            v-model="hostmasterMailbox"
            :label="t('dns.nameservers.fields.hostmasterMailbox')"
            :error="hostmasterError"
            mono
            required
          />
          <UiInput
            v-model="defaultTtl"
            :label="t('dns.nameservers.fields.defaultTtl')"
            :error="ttlError"
            inputmode="numeric"
            mono
            required
          />
          <UiInput
            v-model="publicIpv4"
            :label="t('dns.nameservers.fields.publicIpv4')"
            :error="ipv4Error"
            inputmode="numeric"
            mono
          />
          <UiInput v-model="publicIpv6" :label="t('dns.nameservers.fields.publicIpv6')" :error="ipv6Error" mono />
        </div>
        <UiFieldHint>{{ t('dns.nameservers.hints.hostmaster') }}</UiFieldHint>
        <UiFieldHint>{{ t('dns.nameservers.hints.rerender') }}</UiFieldHint>
        <div class="flex flex-col-reverse gap-2 border-t border-border-subtle pt-5 sm:flex-row sm:justify-end">
          <UiButton type="submit" :disabled="saving">{{ t('dns.nameservers.save') }}</UiButton>
        </div>
      </div>
    </UiForm>
  </UiCard>
</template>
