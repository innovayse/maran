import type { Extension } from '@codemirror/state'
import type { CodeLanguage } from '../types/codeLanguage'

/**
 * Loads the grammar for one language. Each branch is its own dynamic import, so the bundler makes
 * one chunk per grammar and a person editing a `.php` file downloads the PHP grammar and nothing
 * for the other eighteen.
 * @param language The language to highlight.
 * @returns The CodeMirror extension that highlights and indents it; none for `plain`.
 */
export const loadCodeLanguage = async (language: CodeLanguage): Promise<Extension> => {
  switch (language) {
    case 'php':
      return (await import('@codemirror/lang-php')).php()
    case 'javascript':
      return (await import('@codemirror/lang-javascript')).javascript()
    case 'jsx':
      return (await import('@codemirror/lang-javascript')).javascript({ jsx: true })
    case 'typescript':
      return (await import('@codemirror/lang-javascript')).javascript({ typescript: true })
    case 'tsx':
      return (await import('@codemirror/lang-javascript')).javascript({ typescript: true, jsx: true })
    case 'css':
      return (await import('@codemirror/lang-css')).css()
    case 'scss':
      return (await import('@codemirror/lang-sass')).sass()
    case 'html':
      return (await import('@codemirror/lang-html')).html()
    case 'vue':
      return (await import('@codemirror/lang-vue')).vue()
    case 'json':
      return (await import('@codemirror/lang-json')).json()
    case 'yaml':
      return (await import('@codemirror/lang-yaml')).yaml()
    case 'xml':
      return (await import('@codemirror/lang-xml')).xml()
    case 'sql':
      return (await import('@codemirror/lang-sql')).sql()
    case 'markdown':
      return (await import('@codemirror/lang-markdown')).markdown()
    case 'python':
      return (await import('@codemirror/lang-python')).python()
    case 'shell': {
      const [{ StreamLanguage }, { shell }] = await Promise.all([
        import('@codemirror/language'),
        import('@codemirror/legacy-modes/mode/shell'),
      ])
      return StreamLanguage.define(shell)
    }
    case 'nginx': {
      const [{ StreamLanguage }, { nginx }] = await Promise.all([
        import('@codemirror/language'),
        import('@codemirror/legacy-modes/mode/nginx'),
      ])
      return StreamLanguage.define(nginx)
    }
    case 'ini': {
      // INI, .htaccess, php.ini and env files share one shape: `key = value` lines and comments.
      // The properties grammar colours exactly that and nothing more, which is all they have.
      const [{ StreamLanguage }, { properties }] = await Promise.all([
        import('@codemirror/language'),
        import('@codemirror/legacy-modes/mode/properties'),
      ])
      return StreamLanguage.define(properties)
    }
    case 'plain':
      return []
  }
}
