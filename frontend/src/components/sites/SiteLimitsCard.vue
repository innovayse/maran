<script setup lang="ts">
/**
 * The limits section of a site's options: how large an upload may be and how long browsers may keep
 * static files. Both fields are typed text (empty means "not set"); the bounds in the hints come from
 * the panel, and the message under each field is decided by the parent.
 */
import { useI18n } from 'vue-i18n'
import UiCard from '../ui/UiCard.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiInput from '../ui/UiInput.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { SiteOptionsLimits } from '../../types/siteOptions'

/** Props accepted by {@link SiteLimitsCard}. */
defineProps<{
  /** The bounds the panel states for both fields. */
  limits: SiteOptionsLimits
  /** Whether the fields are inert, e.g. while a save is in flight. */
  disabled: boolean
  /** The message under the upload-size field, or `null`. */
  uploadError: string | null
  /** The message under the cache-lifetime field, or `null`. */
  cacheError: string | null
}>()

/** The upload size as typed, in MiB. */
const bodySize = defineModel<string>('bodySize', { required: true })

/** The cache lifetime as typed, in days. */
const cacheDays = defineModel<string>('cacheDays', { required: true })

const { t } = useI18n()
</script>

<template>
  <UiCard class="@container flex flex-col gap-4">
    <UiSectionHeading :title="t('siteOptions.limits.heading')" :subtitle="t('siteOptions.limits.subtitle')" />
    <div class="grid min-w-0 gap-4 @lg:grid-cols-2">
      <div class="flex min-w-0 flex-col gap-2">
        <UiInput
          v-model="bodySize"
          :label="t('siteOptions.limits.uploadLabel')"
          :placeholder="t('siteOptions.limits.uploadPlaceholder')"
          :disabled="disabled"
          :error="uploadError"
          inputmode="numeric"
        />
        <UiFieldHint>
          {{
            t('siteOptions.limits.uploadHint', {
              minimum: limits.minimumBodySizeMib,
              maximum: limits.maximumBodySizeMib,
            })
          }}
        </UiFieldHint>
      </div>
      <div class="flex min-w-0 flex-col gap-2">
        <UiInput
          v-model="cacheDays"
          :label="t('siteOptions.limits.cacheLabel')"
          :placeholder="t('siteOptions.limits.cachePlaceholder')"
          :disabled="disabled"
          :error="cacheError"
          inputmode="numeric"
        />
        <UiFieldHint>
          {{
            t('siteOptions.limits.cacheHint', {
              minimum: limits.minimumCacheDays,
              maximum: limits.maximumCacheDays,
            })
          }}
        </UiFieldHint>
      </div>
    </div>
  </UiCard>
</template>
