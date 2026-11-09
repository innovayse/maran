<script setup lang="ts">
/**
 * A reseller's branding screen: the name, logo and accent colour its customers' shell shows in place
 * of the panel's own (issue #367). Renders a `<section>`, not a `<main>` — the single `<main>`
 * landmark lives in the layout this page is nested under.
 *
 * **The panel is the authority on every value.** This screen sends what was typed or chosen and
 * shows the refusal in the panel's own words (a name too long, a colour too light for white text, a
 * logo that is not a PNG, JPEG or WebP image); it holds no copy of those rules. What it keeps after a
 * save is what the panel answered, because the panel normalises (a colour is stored in lower case,
 * a blank name is no name). A value left empty goes back to the panel's own look.
 *
 * The preview draws a wordmark and a primary button the way a customer's shell will: the colour is
 * applied to a wrapper through the same `--acf` token the shell paints, so the button is the real
 * kit button and not a lookalike.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBrandLogo from '../../components/ui/UiBrandLogo.vue'
import UiBrandMark from '../../components/ui/UiBrandMark.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiFileInput from '../../components/ui/UiFileInput.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import { useResellerBrandingStore } from '../../stores/resellerBranding'
import type { ResellerBranding } from '../../types/branding'
import { brandingAccentStyle } from '../../utils/brandingAccentStyle'
import { readFileAsDataUri } from '../../utils/readFileAsDataUri'

const { t } = useI18n()
const brandingStore = useResellerBrandingStore()

/** The name shown to customers, as typed. Empty means the panel's own. */
const displayName: Ref<string> = ref('')

/** The accent colour, as typed. Empty means the panel's own. */
const accentColor: Ref<string> = ref('')

/** The logo as a data URI, or `null` for the panel's own mark. */
const logo: Ref<string | null> = ref(null)

/** Whether the browser failed to read the file the person chose, which is shown in place of a save error. */
const logoUnreadable: Ref<boolean> = ref(false)

/**
 * Bumped whenever the form is refilled from the panel, so the file chooser is rebuilt and stops
 * showing the name of a file the form no longer holds (a file input cannot be reset from script).
 */
const chooserKey: Ref<number> = ref(0)

/** Whether the typed colour looks like a `#rrggbb` value, which is when the preview paints it. */
const previewable: ComputedRef<boolean> = computed(() => {
  return /^#[0-9a-fA-F]{6}$/.test(accentColor.value.trim())
})

/** The style the preview wrapper wears: the same two tokens the shell paints, or nothing. */
const previewStyle: ComputedRef<Record<string, string>> = computed((): Record<string, string> => {
  if (!previewable.value) {
    return {}
  }

  return brandingAccentStyle(accentColor.value.trim())
})

/**
 * Copies the branding the panel reported into the form's fields.
 * @param branding The branding to show, or `null` while none has been read.
 * @returns Nothing; the fields are updated synchronously.
 */
const fill = (branding: ResellerBranding | null): void => {
  if (branding === null) {
    return
  }

  displayName.value = branding.displayName ?? ''
  accentColor.value = branding.accentColor ?? ''
  logo.value = branding.logo
  chooserKey.value += 1
}

/**
 * Takes the chosen image as the logo. Reading can fail only if the browser cannot open the file; the
 * panel decides whether the image is acceptable when the form is saved.
 * @param file The chosen file, or `null` when the choice was cleared.
 * @returns Resolves once the file has been read.
 */
const chooseLogo = async (file: File | null): Promise<void> => {
  if (file === null) {
    return
  }

  logoUnreadable.value = false
  try {
    logo.value = await readFileAsDataUri(file)
  } catch {
    // The reader's own error text is never shown (it is not localized); the page says it in the
    // person's language and keeps the logo it had.
    logoUnreadable.value = true
  }
}

/**
 * Drops the logo, so the panel's own mark shows once the form is saved.
 * @returns Nothing; the field is updated synchronously.
 */
const removeLogo = (): void => {
  logo.value = null
  logoUnreadable.value = false
  chooserKey.value += 1
}

/**
 * Saves the branding as the form holds it. An empty field is sent as `null`, which is how a value
 * goes back to the panel's own.
 * @returns Resolves once the request has settled.
 */
const submit = async (): Promise<void> => {
  await brandingStore.save({
    displayName: displayName.value.trim() === '' ? null : displayName.value,
    accentColor: accentColor.value.trim() === '' ? null : accentColor.value.trim(),
    logo: logo.value,
  })
}

// The form follows whatever the panel last reported, including after a save.
watch(
  () => {
    return brandingStore.branding
  },
  fill,
  { immediate: true },
)

void brandingStore.load()
</script>

<template>
  <section class="w-full">
    <UiPageHeading class="mb-4" :title="t('branding.heading')" :subtitle="t('branding.subtitle')" />

    <div class="mb-4 flex flex-col gap-3 empty:hidden">
      <UiAlert v-if="brandingStore.submitMessage !== null" variant="error">
        {{ brandingStore.submitMessage }}
      </UiAlert>

      <UiAlert v-if="logoUnreadable" variant="error">{{ t('branding.logoUnreadable') }}</UiAlert>

      <UiAlert v-if="brandingStore.saved" variant="success">{{ t('branding.saved') }}</UiAlert>
    </div>

    <UiLoadingRegion :state="brandingStore.loadState">
      <template #error>
        <UiAlert variant="error">{{ brandingStore.loadMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>
      <div class="grid grid-cols-1 items-start gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,24rem)]">
        <UiForm :loading="brandingStore.loadState === 'loading'" @submit="submit">
          <div class="flex flex-col gap-4">
            <UiCard>
              <UiSectionHeading class="mb-4" :title="t('branding.nameTitle')" :subtitle="t('branding.nameHint')" />
              <UiInput v-model="displayName" :label="t('branding.nameLabel')" />
            </UiCard>

            <UiCard>
              <UiSectionHeading class="mb-4" :title="t('branding.colorTitle')" :subtitle="t('branding.colorHint')" />
              <UiInput v-model="accentColor" :label="t('branding.colorLabel')" mono placeholder="#2468dc" />
            </UiCard>

            <UiCard>
              <UiSectionHeading class="mb-4" :title="t('branding.logoTitle')" :subtitle="t('branding.logoHint')" />
              <div class="flex flex-col gap-3">
                <UiFileInput
                  :key="chooserKey"
                  :label="t('branding.logoLabel')"
                  accept="image/png,image/jpeg,image/webp"
                  @select="chooseLogo"
                />
                <div v-if="logo !== null" class="flex items-center gap-3">
                  <UiBrandLogo :src="logo" size="lg" />
                  <UiButton variant="ghost" @click="removeLogo">{{ t('branding.removeLogo') }}</UiButton>
                </div>
              </div>
            </UiCard>

            <div>
              <UiButton type="submit" :disabled="brandingStore.loading">{{ t('branding.save') }}</UiButton>
            </div>
          </div>
        </UiForm>

        <UiCard>
          <UiSectionHeading class="mb-4" :title="t('branding.previewTitle')" :subtitle="t('branding.previewHint')" />
          <div class="flex flex-col gap-4" :style="previewStyle" data-testid="branding-preview">
            <span class="inline-flex min-w-0 items-center gap-2.5">
              <UiBrandLogo v-if="logo !== null" :src="logo" />
              <UiBrandMark v-else :size="30" />
              <span class="font-display text-base font-black text-text-primary">
                {{ displayName.trim() === '' ? t('app.title') : displayName }}
              </span>
            </span>
            <div>
              <UiButton type="button">{{ t('branding.previewButton') }}</UiButton>
            </div>
          </div>
        </UiCard>
      </div>
    </UiLoadingRegion>
  </section>
</template>
