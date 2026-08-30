# Design canvas (read-only reference)

This directory holds a local copy of the Claude Design canvas that Maran's user
interface is built from.

- `ServerPanel.dc.html` — the canvas itself: every screen, component and state
  of the panel, plus the design system (type scale, palette, spacing, buttons,
  badges, inputs).
- `support.js` — the Claude Design runtime the canvas needs in order to render.

They are saved here because the Claude Design connection is not always
available, and the UI work must not stall when it is not.

## Rules

These files are a **read-only reference**. The source of truth is the Claude
Design project:

https://claude.ai/design/p/1b6fb6ec-ce07-46e3-9206-c297327f2971

Never edit them here. A design change belongs in the canvas; once it is made
there, re-save the files into this directory so the local copy stays a faithful
mirror.
