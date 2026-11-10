import { defaultKeymap, history, historyKeymap, indentLess, indentMore, insertTab } from '@codemirror/commands'
import { bracketMatching, indentOnInput, indentUnit } from '@codemirror/language'
import { gotoLine, openSearchPanel, search, searchKeymap } from '@codemirror/search'
import { Compartment, EditorState, type Extension } from '@codemirror/state'
import {
  EditorView,
  drawSelection,
  highlightActiveLine,
  highlightActiveLineGutter,
  keymap,
  lineNumbers,
} from '@codemirror/view'
import type { CodeEditorHandle, CodeEditorOptions, CodeLanguage } from '../types/codeLanguage'
import { loadCodeLanguage } from './codeEditorLanguage'
import { codeEditorTheme } from './codeEditorTheme'

/** Keys that are only modifiers: pressing one of them is not "typing something else" after Escape. */
const MODIFIER_KEYS: readonly string[] = ['Shift', 'Control', 'Alt', 'Meta', 'AltGraph', 'CapsLock']

/**
 * Builds a CodeMirror 6 editor inside `parent` and returns the handle a Vue component steers it by.
 * Everything CodeMirror is imported here (and in the grammar loader), so a page that never opens an
 * editor never downloads any of it: callers reach this module through a dynamic `import()`.
 *
 * **The Tab key does not trap keyboard focus by default** (WCAG 2.1.2): Tab moves on to the next
 * control like everywhere else in the panel. A person who wants Tab to indent switches it on, and
 * even then Escape releases it: after Escape, the next Tab moves focus on, and any other key puts the
 * indentation back. This is CodeMirror's own recommended arrangement for an accessible editor.
 * @param options What to draw, where, and who to tell about edits.
 * @returns The handle, once the first grammar has loaded.
 */
export const createCodeEditor = async (options: CodeEditorOptions): Promise<CodeEditorHandle> => {
  const languageSlot = new Compartment()
  const wrapSlot = new Compartment()
  const editableSlot = new Compartment()

  /** What the person has chosen so far; a re-created state starts from these, not from the options. */
  let language: Extension = await loadCodeLanguage(options.language)
  let editable: boolean = options.editable
  let noWrap: boolean = options.noWrap
  let tabIndents: boolean = options.tabIndents
  /** True between Escape and the next key that is not Tab: Tab then moves focus on. */
  let released = false
  /** Bumped by every language switch so a slow grammar cannot overwrite a newer choice. */
  let languageRequest = 0

  /**
   * The access state of the text for the current `editable`: refuses typing and tells assistive
   * technology why the box is inert.
   * @returns The extensions that make the text read-only or editable.
   */
  const editableExtension = (): Extension => {
    return [
      EditorView.editable.of(editable),
      EditorState.readOnly.of(!editable),
      EditorView.contentAttributes.of({
        'aria-multiline': 'true',
        'aria-labelledby': options.labelledBy,
        ...(editable ? {} : { 'aria-disabled': 'true', 'aria-readonly': 'true' }),
      }),
    ]
  }

  /**
   * Builds the extensions list for a fresh state from the current choices.
   * @returns The extensions.
   */
  const extensions = (): Extension[] => {
    return [
      lineNumbers(),
      highlightActiveLineGutter(),
      highlightActiveLine(),
      drawSelection(),
      history(),
      indentOnInput(),
      indentUnit.of('  '),
      bracketMatching(),
      search({ top: true }),
      EditorState.phrases.of(options.phrases),
      languageSlot.of(language),
      wrapSlot.of(noWrap ? [] : EditorView.lineWrapping),
      editableSlot.of(editableExtension()),
      codeEditorTheme,
      EditorView.domEventHandlers({
        keydown: (event: KeyboardEvent, view: EditorView): boolean => {
          if (event.key === 'Escape') {
            released = true
            return false
          }
          if (event.key === 'Tab') {
            const plain = !event.ctrlKey && !event.altKey && !event.metaKey
            if (tabIndents && !released && plain) {
              event.preventDefault()
              if (event.shiftKey) {
                indentLess(view)
              } else if (view.state.selection.ranges.some((range) => {return !range.empty})) {
                indentMore(view)
              } else {
                insertTab(view)
              }
              return true
            }
            return false
          }
          if (!MODIFIER_KEYS.includes(event.key)) {
            released = false
          }
          return false
        },
        // Leaving the text and coming back starts with the Tab behaviour the person chose.
        blur: (): boolean => {
          released = false
          return false
        },
      }),
      keymap.of([
        {
          key: 'Mod-s',
          preventDefault: true,
          run: (): boolean => {
            options.onSave()
            return true
          },
        },
        ...searchKeymap,
        ...historyKeymap,
        ...defaultKeymap,
      ]),
      EditorView.updateListener.of((update): void => {
        // `setText` replaces the whole state and never reaches this listener, so every change seen
        // here was made by the person.
        if (update.docChanged) {
          options.onChange(update.state.doc.toString())
        }
      }),
    ]
  }

  const view = new EditorView({
    parent: options.parent,
    state: EditorState.create({ doc: options.text, extensions: extensions() }),
  })

  return {
    text: (): string => {
      return view.state.doc.toString()
    },
    setText: (text: string): void => {
      // A whole new state, not a transaction: the history of the previous file must not be undoable.
      view.setState(EditorState.create({ doc: text, extensions: extensions() }))
    },
    setLanguage: async (next: CodeLanguage): Promise<void> => {
      languageRequest += 1
      const mine = languageRequest
      const loaded = await loadCodeLanguage(next)
      if (mine !== languageRequest) {
        return
      }
      language = loaded
      view.dispatch({ effects: languageSlot.reconfigure(language) })
    },
    setEditable: (next: boolean): void => {
      editable = next
      view.dispatch({ effects: editableSlot.reconfigure(editableExtension()) })
    },
    setNoWrap: (next: boolean): void => {
      noWrap = next
      view.dispatch({ effects: wrapSlot.reconfigure(noWrap ? [] : EditorView.lineWrapping) })
    },
    setTabIndents: (next: boolean): void => {
      tabIndents = next
      released = false
    },
    openSearch: (): void => {
      view.focus()
      openSearchPanel(view)
    },
    openGoToLine: (): void => {
      view.focus()
      gotoLine(view)
    },
    focus: (): void => {
      view.focus()
    },
    destroy: (): void => {
      view.destroy()
    },
  }
}
