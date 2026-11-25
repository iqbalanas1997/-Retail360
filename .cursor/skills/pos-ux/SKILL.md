---
name: pos-ux
description: Design and implement simple grocery POS screens optimized for cashier speed, barcode scanners and full-day use.
paths: "**/*.html,**/*.scss,**/*.css,**/*.ts"
---
# POS UX Skill

1. Read `docs/ui/POS_UX.md` and `.cursor/skills/retail360-ui-ux/SKILL.md`.
2. Keep the main workflow on one screen: scan/search → quantity → cart → payment → receipt.
3. Prefer keyboard and scanner workflows. Enter in the barcode field adds the product and does not complete the sale. Ctrl+Enter completes the sale except from the barcode field.
4. Minimize fields and chrome. Reuse existing Angular pages and `frontend/src/styles.scss` classes.
5. Keep cashier UI separate from admin/reporting complexity.
6. Validate usability after implementation with realistic counter scenarios.

## Implemented counter pattern

- Cart stays full width under barcode and search. Lines are tickets, not a dense spreadsheet: name dominant; bold line total; quantity stepper with ≥44px targets; shelf, stock, and Remove secondary.
- To Pay stays in a green panel at the upper right, with the amount on a paper plate and one primary Complete sale. Cash is the walk-in default. Customer search, credit, and outstanding appear only after the cashier needs them. Outstanding collection does not change the sale total.
- Browse and search open in a dialog over the counter so the cart does not move. Results are product, unit, and selling price. There is no Add button. A row click or Enter on the highlighted row adds. Browse stays open after an add. Escape, Close, or the backdrop returns focus to the barcode field.
- Brand tokens: `--ink` `#1f2933`, `--paper` `#f4f0e6`, `--green` `#1f6b4a`, shortage `#9b2c2c`. Soft borders, modest radius, no glow, purple, emoji spam, or animation spectacle. Do not raise Angular budgets.

If a rule file still says every sale must pick a payment method, or still teaches in-flow browse with an Add button, follow this skill and `docs/ui/POS_UX.md`.
