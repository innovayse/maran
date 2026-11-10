<script setup lang="ts">
/**
 * The panel's code editor: `UiTextarea`'s job (edit a long text that is machine text) for a text that
 * has a grammar, built on CodeMirror 6. Line numbers, the current-line highlight, bracket matching,
 * auto-indent, find and replace, go to line and syntax colour chosen from the file's name.
 *
 * **Loaded lazily, and only when one is drawn.** The editor, its theme and every grammar are
 * reached through dynamic `import()` from `onMounted`, so the bundler makes them separate chunks and
 * no other page downloads a byte of CodeMirror. Until the first chunk arrives a placeholder of the
 * field's footprint stands in. If the chunk cannot be fetched (a flaky link, a panel restarted
 * mid-update), the field falls back to the plain {@link UiTextarea} with the same value and events,
 * so the person can still edit and save.
 *
 * **Accessible name.** The label is a visible `<span>`; the editable surface (`role="textbox"`,
 * `aria-multiline`) is named by it through `aria-labelledby`, because a `<label for>` cannot point at
 * a content-editable element. `disabled` makes the surface read-only and sets `aria-disabled` and
 * `aria-readonly`: an edit made before the text arrived would be silently replaced by it (#456).
 *
 * **Keyboard.** The Tab key moves focus on, like everywhere else (WCAG 2.1.2: no keyboard trap).
 * `tabIndents` makes Tab indent instead, and Escape releases it for one keystroke: Escape, then Tab,
 * leaves the text. The hint under the field says so while it is on. Find, replace and go to line
 * have their keyboard shortcuts and are also reachable through the exposed `openSearch` and
 * `openGoToLine`, for the screen's toolbar buttons (a phone has no Ctrl+F). `Ctrl/Cmd+S` emits `save`.
 *
 * **Look.** The same field box as `UiTextarea`: the fill is `bg-surface-2`, the edge
 * `border-border-control` (`--bc`, 3:1 or better on every surface in both themes), stronger on hover,
 * and focus inside the editor turns it `border-accent` with `shadow-field-focus`. Syntax colours and
 * their measured contrast are in `utils/codeEditorTheme.ts`. The editor scrolls inside its own box,
 * so the page never scrolls sideways at 390px. Text is 16px below `sm`, so a phone browser does not
 * zoom on focus.
 */
import { onBeforeUnmount, onMounted, ref, useId, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { CodeEditorHandle } from '../../types/codeLanguage'
import { codeLanguageOf } from '../../utils/codeLanguageOf'
import UiFieldHint from './UiFieldHint.vue'
import UiSkeleton from './UiSkeleton.vue'
import UiTextarea from './UiTextarea.vue'

/** Props accepted by {@link UiCodeEditor}. */
const props = withDefaults(
  defineProps<{
    /** Current text (`v-model` target). */
    modelValue: string
    /** Visible label text, always rendered; it is also the editable surface's accessible name. */
    label: string
    /** The file's base name; the extension chooses the highlighting. Empty for plain text. */
    fileName?: string
    /** Refuses typing, for a text that has not arrived yet. */
    disabled?: boolean
    /** Keeps long lines on one row and scrolls sideways instead of wrapping them. */
    noWrap?: boolean
    /** Makes the Tab key indent instead of moving focus on; Escape then Tab always leaves. */
    tabIndents?: boolean
  }>(),
  { fileName: '', disabled: false, noWrap: false, tabIndents: false },
)

/** Events emitted by {@link UiCodeEditor}. */
const emit = defineEmits<{
  /** Fired after every edit, carrying the whole new text. */
  (e: 'update:modelValue', value: string): void
  /** The person pressed Ctrl/Cmd+S inside the text. */
  (e: 'save'): void
}>()

const { t } = useI18n()

/** Id of the visible label, which names the editable surface. */
const labelId: string = useId()

/** The element CodeMirror is drawn into. */
const host: Ref<HTMLElement | null> = ref(null)

/** The running editor, once its chunk has loaded. */
let handle: CodeEditorHandle | null = null

/** Whether the component was torn down before the chunk arrived. */
let gone = false

/** Whether the editor is drawn; until then the placeholder shows. */
const ready: Ref<boolean> = ref(false)

/** Whether the editor chunk could not be loaded, which switches to the plain textarea. */
const failed: Ref<boolean> = ref(false)

/**
 * The search panel's texts in the interface language, keyed by the English phrase CodeMirror asks for.
 * @returns The phrase table.
 */
const phrases = (): Record<string, string> => {
  return {
    Find: t('app.codeEditor.find'),
    Replace: t('app.codeEditor.replaceWith'),
    next: t('app.codeEditor.next'),
    previous: t('app.codeEditor.previous'),
    all: t('app.codeEditor.all'),
    'match case': t('app.codeEditor.matchCase'),
    regexp: t('app.codeEditor.regexp'),
    'by word': t('app.codeEditor.byWord'),
    replace: t('app.codeEditor.replace'),
    'replace all': t('app.codeEditor.replaceAll'),
    close: t('app.codeEditor.close'),
    'Go to line': t('app.codeEditor.goToLine'),
    go: t('app.codeEditor.go'),
    'current match': t('app.codeEditor.currentMatch'),
    'on line': t('app.codeEditor.onLine'),
    'replaced $ matches': t('app.codeEditor.replacedMatches'),
    'replaced match on line $': t('app.codeEditor.replacedMatchOnLine'),
    'Selection deleted': t('app.codeEditor.selectionDeleted'),
    'Control character': t('app.codeEditor.controlCharacter'),
  }
}

/**
 * Forwards the plain textarea's text when the editor could not be loaded.
 * @param value The textarea's new text.
 * @returns Nothing.
 */
const forward = (value: string): void => {
  emit('update:modelValue', value)
}

/**
 * Loads the editor chunk and draws it into the host. A failed load leaves the plain textarea in
 * place of the editor instead of an empty box.
 * @returns Resolves once the editor is drawn or the fallback is chosen.
 */
const start = async (): Promise<void> => {
  try {
    const { createCodeEditor } = await import('../../utils/createCodeEditor')
    if (gone || host.value === null) {
      return
    }
    const created = await createCodeEditor({
      parent: host.value,
      text: props.modelValue,
      language: codeLanguageOf(props.fileName),
      labelledBy: labelId,
      phrases: phrases(),
      editable: !props.disabled,
      noWrap: props.noWrap,
      tabIndents: props.tabIndents,
      onChange: (text: string): void => {
        emit('update:modelValue', text)
      },
      onSave: (): void => {
        emit('save')
      },
    })
    if (gone) {
      created.destroy()
      return
    }
    handle = created
    ready.value = true
    // The text may have changed while the chunk was on its way (the file arrived).
    if (handle.text() !== props.modelValue) {
      handle.setText(props.modelValue)
    }
  } catch {
    // The editor is an upgrade over a textarea, never a requirement for editing: any failure to
    // fetch or start it lands on the plain field with the same value.
    failed.value = true
  }
}

/**
 * Moves the keyboard into the text.
 * @returns Nothing.
 */
const focus = (): void => {
  handle?.focus()
}

/**
 * Opens the find and replace panel.
 * @returns Nothing.
 */
const openSearch = (): void => {
  handle?.openSearch()
}

/**
 * Opens the go-to-line prompt.
 * @returns Nothing.
 */
const openGoToLine = (): void => {
  handle?.openGoToLine()
}

onMounted(start)

onBeforeUnmount(() => {
  gone = true
  handle?.destroy()
  handle = null
})

watch(
  () => {return props.modelValue},
  (value: string) => {
    // Text that came from outside (a file read, a reload). An edit the person just made is already
    // in the editor, so the comparison keeps the cursor where it is.
    if (handle !== null && handle.text() !== value) {
      handle.setText(value)
    }
  },
)

watch(
  () => {return props.fileName},
  async (name: string) => {
    await handle?.setLanguage(codeLanguageOf(name))
  },
)

watch(
  () => {return props.disabled},
  (disabled: boolean) => {
    handle?.setEditable(!disabled)
  },
)

watch(
  () => {return props.noWrap},
  (noWrap: boolean) => {
    handle?.setNoWrap(noWrap)
  },
)

watch(
  () => {return props.tabIndents},
  (tabIndents: boolean) => {
    handle?.setTabIndents(tabIndents)
  },
)

defineExpose({ focus, openSearch, openGoToLine })
</script>

<template>
  <UiTextarea
    v-if="failed"
    :model-value="modelValue"
    :label="label"
    :rows="24"
    mono
    :no-wrap="noWrap"
    :disabled="disabled"
    @update:model-value="forward"
  />
  <div v-else class="flex min-w-0 flex-col gap-1">
    <span :id="labelId" class="text-xs font-bold text-text-muted wrap-anywhere">{{ label }}</span>
    <div
      class="relative h-96 max-w-full min-w-0 overflow-hidden rounded-control border bg-surface-2 text-base sm:h-128 sm:text-sm"
      :class="[
        disabled ? 'cursor-not-allowed' : '',
        'border-border-control hover:border-border-control-hover focus-within:border-accent focus-within:shadow-field-focus',
      ]"
    >
      <div ref="host" class="h-full" />
      <div v-if="!ready" class="absolute inset-0 bg-surface-2">
        <UiSkeleton shape="block" control="area" />
      </div>
    </div>
    <UiFieldHint v-if="tabIndents">{{ t('app.codeEditor.tabHint') }}</UiFieldHint>
  </div>
</template>
