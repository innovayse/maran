<script setup lang="ts">
/**
 * How to authorize the panel on the remote server: the one `authorized_keys` line the panel built,
 * as selectable text with a copy control, the three steps that put it in place, and the plain
 * statement of what the key can do. The panel generated the matching private key and never shows
 * it; this component only ever receives the public line.
 *
 * **The limit is said, not hidden.** A key cannot be restricted to read-only on the remote server
 * (the restrictions in the line stop forwarding and terminals, not commands), so the component
 * states that next to the line and says when the line is limited to given source addresses.
 */
import { ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'

/** Props accepted by {@link PublicKeyInstructions}. */
withDefaults(
  defineProps<{
    /** The complete `authorized_keys` line, built by the panel. */
    authorizedKeysLine: string
    /** The addresses the line is limited to, or absent for no limit. */
    allowedFrom?: string | null
  }>(),
  { allowedFrom: null },
)

const { t } = useI18n()

/** Whether the line was copied during this showing, so the control can say so. */
const copied: Ref<boolean> = ref(false)

/**
 * Copies the line to the clipboard. A failure is swallowed on purpose and leaves the control
 * unchanged: the clipboard is blocked in some browsers, and the line stays selectable beside it.
 * @param line The line to copy.
 * @returns Resolves once the clipboard write has settled, successfully or not.
 */
const copyLine = async (line: string): Promise<void> => {
  try {
    await navigator.clipboard.writeText(line)
    copied.value = true
  } catch {
    copied.value = false
  }
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <UiSectionHeading :title="t('migration.key.title')" :subtitle="t('migration.key.intro')" />

    <ol class="list-decimal space-y-1 pl-5 text-sm text-text-secondary">
      <li>{{ t('migration.key.steps.open') }}</li>
      <li>{{ t('migration.key.steps.append') }}</li>
      <li>{{ t('migration.key.steps.test') }}</li>
    </ol>

    <div class="flex min-w-0 flex-col gap-2">
      <p class="text-xs font-semibold text-text-muted">{{ t('migration.key.lineLabel') }}</p>
      <!-- Selectable text, not a masked field: the clipboard may be unavailable. -->
      <code
        data-testid="authorized-keys-line"
        class="block rounded-control border border-border-strong bg-surface-2 px-3 py-2.5 font-mono text-sm text-text-primary select-all wrap-anywhere"
        >{{ authorizedKeysLine }}</code
      >
      <div>
        <UiButton variant="secondary" @click="copyLine(authorizedKeysLine)">
          <UiIcon :name="copied ? 'check' : 'copy'" size="sm" />
          {{ copied ? t('migration.key.copied') : t('migration.key.copy') }}
        </UiButton>
      </div>
      <!-- A change of a button's name is not announced by itself, so the copy result is also spoken
           from a status region that is in the page before it has anything to say. -->
      <span class="sr-only" role="status">{{ copied ? t('migration.key.copied') : '' }}</span>
    </div>

    <p class="text-sm text-text-muted">
      {{
        allowedFrom
          ? t('migration.key.limitedFrom', { addresses: allowedFrom })
          : t('migration.key.notLimitedFrom')
      }}
    </p>

    <UiAlert variant="warning">{{ t('migration.key.limit') }}</UiAlert>
  </div>
</template>
