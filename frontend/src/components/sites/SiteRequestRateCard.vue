<script setup lang="ts">
/**
 * The request-limit section of a site's options: how many requests per second one visitor address may
 * make, and how many more it may send at once before the site starts answering "too many requests".
 * Both fields are typed text (an empty rate means the site is not limited); the bounds in the hints
 * come from the panel, and the message under each field is decided by the parent.
 */
import { useI18n } from 'vue-i18n'
import UiCard from '../ui/UiCard.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiInput from '../ui/UiInput.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { SiteOptionsLimits } from '../../types/siteOptions'

/** Props accepted by {@link SiteRequestRateCard}. */
defineProps<{
  /** The bounds the panel states for both fields. */
  limits: SiteOptionsLimits
  /** Whether the fields are inert, e.g. while a save is in flight. */
  disabled: boolean
  /** The message under the rate field, or `null`. */
  rateError: string | null
  /** The message under the burst field, or `null`. */
  burstError: string | null
}>()

/** The rate as typed, in requests per second. */
const perSecond = defineModel<string>('perSecond', { required: true })

/** The burst as typed, in requests. */
const burst = defineModel<string>('burst', { required: true })

const { t } = useI18n()
</script>

<template>
  <UiCard class="@container flex flex-col gap-4">
    <UiSectionHeading :title="t('siteOptions.requestRate.heading')" :subtitle="t('siteOptions.requestRate.subtitle')" />
    <div class="grid min-w-0 gap-4 @lg:grid-cols-2">
      <div class="flex min-w-0 flex-col gap-2">
        <UiInput
          v-model="perSecond"
          :label="t('siteOptions.requestRate.rateLabel')"
          :placeholder="t('siteOptions.requestRate.ratePlaceholder')"
          :disabled="disabled"
          :error="rateError"
          inputmode="numeric"
        />
        <UiFieldHint>
          {{
            t('siteOptions.requestRate.rateHint', {
              minimum: limits.minimumRequestsPerSecond,
              maximum: limits.maximumRequestsPerSecond,
            })
          }}
        </UiFieldHint>
      </div>
      <div class="flex min-w-0 flex-col gap-2">
        <UiInput
          v-model="burst"
          :label="t('siteOptions.requestRate.burstLabel')"
          :placeholder="t('siteOptions.requestRate.burstPlaceholder')"
          :disabled="disabled"
          :error="burstError"
          inputmode="numeric"
        />
        <UiFieldHint>
          {{
            t('siteOptions.requestRate.burstHint', {
              minimum: limits.minimumRequestBurst,
              maximum: limits.maximumRequestBurst,
            })
          }}
        </UiFieldHint>
      </div>
    </div>
  </UiCard>
</template>
