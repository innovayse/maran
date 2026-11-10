// The sample module's screen: a CLASSIC script (no import, no export, no type=module) that the panel's
// generated frame document loads with a plain script tag and a Subresource Integrity check
// (docs/marketplace/ipm-format.md "The spa folder"). It is written by hand and needs no build step.
// A real module would write the same thing in TypeScript with frontend/packages/module-bridge
// (`connectToShell`) and bundle it to one classic script; this file implements the same bridge protocol
// v1 by hand so the sample stays self-contained.
//
// What the frame is: a sandboxed iframe with an opaque origin, no cookies and no network (its policy is
// connect-src 'none'). Its only way out is the MessageChannel the shell hands over after a handshake, and
// the shell performs every API call with its own session. That is also why the translations below are
// written into this file instead of fetched from locales/*.json: the frame cannot fetch them. Keep
// MESSAGES in step with locales/en.json, ru.json and hy.json (the shell reads those files, not the frame).
(function () {
  'use strict'

  var MESSAGES = {
    en: {
      title: 'Hello',
      greeting: 'Hello from a packaged module',
      loading: 'Loading the greeting...',
      failed: 'The greeting could not be loaded (status {status}).',
    },
    ru: {
      title: 'Привет',
      greeting: 'Привет из подключаемого модуля',
      loading: 'Загрузка приветствия...',
      failed: 'Не удалось загрузить приветствие (статус {status}).',
    },
    hy: {
      title: 'Բարև',
      greeting: 'Բարև փաթեթավորված մոդուլից',
      loading: 'Ողջույնը բեռնվում է...',
      failed: 'Ողջույնը չհաջողվեց բեռնել (կարգավիճակ {status}).',
    },
  }

  // The state the screen renders from. `answer` is the API's greeting, or null while it is not known.
  var state = { locale: 'en', answer: null, failedStatus: null }
  var port = null

  // The nonce the shell put after the # of the frame address; the shell accepts `ready` only with it.
  var nonce = window.location.hash.slice(1)

  var heading = document.createElement('h1')
  var label = document.createElement('p')
  var answerLine = document.createElement('p')
  document.body.appendChild(heading)
  document.body.appendChild(label)
  document.body.appendChild(answerLine)

  // The text of a key in the current language, English when the language is not one the sample translates.
  function text(key) {
    return (MESSAGES[state.locale] || MESSAGES.en)[key]
  }

  // Draws the screen. Everything is set with textContent: the API's answer is data, never markup.
  function render() {
    heading.textContent = text('title')
    label.textContent = text('greeting')

    if (state.failedStatus !== null) {
      answerLine.textContent = text('failed').split('{status}').join(String(state.failedStatus))
    } else {
      answerLine.textContent = state.answer === null ? text('loading') : state.answer
    }

    reportHeight()
  }

  // Tells the shell how tall the content is, so it can size the frame (the shell clamps the number).
  function reportHeight() {
    if (port !== null) {
      port.postMessage({ v: 1, type: 'resize', height: Math.ceil(document.documentElement.scrollHeight) })
    }
  }

  // Applies the colour scheme through the style object, which the frame's policy allows, unlike a style attribute.
  function applyTheme(theme) {
    document.body.style.margin = '16px'
    document.body.style.fontFamily = 'system-ui, sans-serif'
    document.body.style.color = theme === 'dark' ? '#e6e8eb' : '#1a1d21'
    document.body.style.background = 'transparent'
  }

  // Asks the shell for GET /api/v1/hello and keeps the answer. The reply comes back over the port as an
  // 'api-result' with the same id; the answer is { text } and a failure is a problem document.
  function loadGreeting() {
    port.postMessage({ v: 1, type: 'api', id: 'greeting', method: 'GET', path: '/api/v1/hello' })
  }

  // What the shell says over the port: the answer to the request above, and later language/colour changes.
  function onPortMessage(event) {
    var message = event.data

    if (!message || message.v !== 1) {
      return
    }

    if (message.type === 'api-result' && message.id === 'greeting') {
      if (message.status === 200 && message.body && typeof message.body.text === 'string') {
        state.answer = message.body.text
        state.failedStatus = null
      } else {
        state.failedStatus = message.status
      }
      render()
    } else if (message.type === 'locale' && typeof message.locale === 'string') {
      state.locale = message.locale
      render()
    } else if (message.type === 'theme') {
      applyTheme(message.theme)
    }
  }

  // The handshake's second half: the shell answers `ready` with `init` and transfers one port.
  window.addEventListener('message', function (event) {
    var init = event.data

    if (event.source !== window.parent || port !== null || !init || init.v !== 1 || init.type !== 'init' || event.ports.length !== 1) {
      return
    }

    port = event.ports[0]
    port.onmessage = onPortMessage
    state.locale = init.locale
    applyTheme(init.theme)
    render()
    loadGreeting()

    if (typeof ResizeObserver === 'function') {
      new ResizeObserver(reportHeight).observe(document.documentElement)
    }
  })

  // Listening is set up above before this line, so a fast shell cannot answer into a frame that is not listening.
  window.parent.postMessage({ v: 1, type: 'ready', nonce: nonce }, '*')
})()
