---
name: retail360-ui-ux
description: >-
  Guide Retail360 shop-screen UI and UX for cashiers, managers, and inventory
  staff who use the app all day. Use when changing Angular layout, styling,
  navigation, forms, tables, empty or error states, or the POS, dashboard,
  catalog, inventory, purchasing, supplier, customer, expense, user, audit, or
  account screens, and when the user mentions UI, UX, usability, or visual design.
paths: "frontend/**/*.html,frontend/**/*.scss,frontend/**/*.css,frontend/**/*.ts"
---

# Retail360 UI/UX

Usability comes before decoration. Shop staff may use these screens for about nine hours. A person should see what to do without instructions. This skill guides a requested screen change. It does not authorize a redesign of screens outside that request.

## Read first

1. This skill.
2. `AGENTS.md` and `docs/ui/POS_UX.md` when the counter is involved.
3. The relevant rows in `docs/project/MODULE_CATALOG.md` and `docs/domain/DOMAIN_MODEL.md`.
4. The existing page, its template, its `*.spec.ts`, and `frontend/src/styles.scss`.
5. `.cursor/skills/pos-ux/SKILL.md` for counter work. This skill does not replace it.

Business behavior stays with `AGENTS.md`, the domain docs, and `.cursor/skills/grocery-domain/SKILL.md`. If those conflict with a visual idea, keep the business behavior and say so.

## Existing frontend

- Angular 21 standalone pages. There is no shared component library and no UI kit.
- One global stylesheet, `frontend/src/styles.scss`. Tokens are `--ink` (`#1f2933`), `--paper` (`#f4f0e6`), and `--green` (`#1f6b4a`). Shortage status uses `#9b2c2c`. Those are the brand colors. Add another accent only when contrast or status cannot be solved with them, and say why.
- Visual system is lightweight and calm: soft borders as ink at low opacity, modest radius, clear spacing. No purple themes, glow, glass, emoji clusters, or animation/transition spectacle. Usability comes before decoration.
- Reuse the existing classes before inventing new ones: `toolbar`, `form`, `inline`, `actions`, `table-wrap`, `notice`, `notice.ok`, `notice.error`, `hint`, `status`, `summary-grid`, `metric`, `button`, and `button.secondary`. Prefer those and the existing POS classes in `styles.scss` over new names.
- Pages own their templates. API access and rules live in `frontend/src/app/core/`. Keep business rules there.
- Shell navigation is `frontend/src/app/shell/shell.html`, gated by `SessionService`. Routes are in `frontend/src/app/app.routes.ts`. Home redirects to `/catalog/products`. A cashier still signs in to `/pos`. The groups are Daily operations, Products and inventory, People, and Management. Hide a group that has no links for the signed-in role. POS stays first when the role can sell.
- Tests are Vitest: `npx ng test --watch=false` from `frontend/`. Production build is `npx ng build --configuration=production`. Leave the 500 kB warning and 1 MB error budgets, and the 4 kB / 8 kB component-style budgets, unchanged.
- Feedback uses a `notice` with `role="alert"` for errors, `notice ok` for success, and a `status` line while loading. A failed save keeps what the user typed.

## Design principles

### Simplicity

Show only what the current task needs. Skip extra controls, icons, cards, and nested steps. Use shop language: product, shelf, quantity, receipt, customer, outstanding. Do not make every section a card. The dashboard may keep its current metric blocks; do not add a card per new number.

### Comfort for a full day

Use the existing type size and spacing as the floor. Do not shrink text or pack controls to fit more on the screen. Put the same kind of action in the same place on list and form pages. Avoid animation, gradients, and heavy shadows.

### Interaction

Prefer fewer steps and less typing. Keep the common path on one screen. Use a confirmation only for a destructive or irreversible action that already has one, such as remove. Do not add a confirm step to save, search, scan, or complete sale. Give a default and leave the field editable. After a failed request, keep the entered values and show the server message. Loading, empty, success, and error states must be visible. The user must be able to tell that a save, sale, or payment finished.

### Forms

Keep the fields the API already accepts. Mark optional fields in the existing hint style. Use the input type the page already uses (`text`, `date`, `decimal` input mode). Keyboard order follows the visual order. Put focus on the first field a person must use: the barcode field on POS, the search field on a list when that is the task. Do not ask for a price, total, receipt number, or balance the server calculates.

A form that records data must not navigate as a browser GET. Reactive forms use `[formGroup]` and `(ngSubmit)`. A small form without `NgForm` must listen to `submit` and call `preventDefault()`.

### Lists

Keep columns few enough to scan. Align money and quantities the way the existing table does. Show the fact that decides the next action in the row: active state, receipt total, outstanding, received state. Row actions stay the existing text buttons. Keep the current search, filter, and page size unless the requested task changes that screen.

### Accessibility

Text on `--paper` and `--ink` must stay readable. Focus must be visible. Controls need a real `<label>`, `<button>`, or link name, not a placeholder alone. Status uses text as well as color. `notice error` is not color-only.

## Screen standards

### POS and sales

`/pos` is the priority screen. Follow `docs/ui/POS_UX.md` and the implemented counter when they conflict with older rule wording.

- Layout: barcode and product search across the top left; cart full width below them; To Pay, item count, quantity, and Complete sale in a green checkout panel at the upper right. Cash is the default and the shortest path: scan, search, or browse, then quantity, shelf, cart, complete sale, receipt.
- Cart: ticket/list lines, not a dense admin spreadsheet. Product name is dominant; line total is bold and easy to find; quantity is one control with ≥44px hit targets; unit price, shelf, stock status, and Remove stay secondary.
- To Pay: green panel with the amount on a paper plate, strong total typography, and one primary Complete sale. Customer search, credit, and outstanding appear only when needed. Cash stays the walk-in default. Credit appears only after an active customer is selected. Outstanding collection is a separate amount and does not change the sale total.
- Browse and search: open in a dialog over the counter so the cart does not move or shrink. Results are a table of product, unit, and selling price that scrolls once inside the dialog. There is no Add button. The whole row is the control: click/tap or Enter on the highlighted row adds. Arrow keys move the highlight. Browse stays open after an add. Escape, Close, or the backdrop returns focus to the barcode field. Barcode and Find barcode stay on the main page, not inside the dialog.
- Barcode: focused on arrival and after a scan. Enter looks up the code and does not complete the sale. A finished scan clears the field. An unknown code, an inactive product, or an API error stays on the page and the cart remains.
- Stock on cart lines: show available quantity and status. A shelf option that cannot cover the line stays disabled. If no shelf can cover the quantity, name the product, each shelf's usable quantity, and the quantity required. Low may still be sold. Do not split one line across shelves.

Omit `paymentMethod` for cash. Send `credit` only when credit is selected and a customer is present.

`/sales` lists receipts. `/sales/:id` is the saved receipt and can be printed. A later payment does not rewrite it. Inventory Staff can read sales and cannot sell. Do not put dashboard or admin tools on the counter.

### Catalog

`/catalog/products` searches quickly. The product form edits the current catalog fields and barcodes. Enter in the barcode field adds the code and does not save the product. Generate shop barcode proposes an `R360-` code. Print the Code 128 label only after save says Saved. Categories, brands, and units stay on `NamedCatalogPage`. Do not add a SKU or a stock field to the product form.

### Inventory

`/inventory/stock` shows on-hand quantity by shelf. Locations, opening, adjustment, and transfer stay separate routes. Inactive shelves and failed posts must still be obvious. Do not hide a validation error behind a new step.

### Purchasing and suppliers

A purchase is recorded, then received. Those states stay distinct. There is no supplier payment and no payable balance. Do not add a payment column or status. Supplier details keep that supplier's purchase history.

### Customers

`/customers` is the Admin and Manager list, behind `canWriteCustomers`. Cashiers find an active customer on POS. Show outstanding where that response already includes it. Recording a payment stays a single amount on the customer form or POS and does not edit the sale. Cashier views continue to omit address and notes.

### Dashboard

`/dashboard` is read-only for Admin and Manager. Lead with the existing sales, expense, purchase, and low-stock figures for the selected dates. Do not add charts, a profit figure, or a receivable tile.

### Expenses, users, audit, account

Use the same list, form, notice, and toolbar patterns. Expenses stay cash records with the fixed category list. Users stay Admin-only. Audit stays an Admin read-only list with no edit or delete. Password change stays on `/account`.

## Angular implementation

- Follow the page that is already there: signals, `inject()`, standalone `imports`, and the core service for that module.
- Reuse a page, service, model, or rule before adding one. Search `frontend/src/app` first.
- Do not add an npm package or component library for a visual change.
- Do not put pricing, stock, credit, or permission rules in a template-only component.
- Keep routes and `SessionService` gates. A hidden action for a role is intentional.
- Match the current responsive shell: a compact sidebar beside the page from 768px up, and a Menu drawer below 768px. The drawer closes after a route is chosen and does not push the page down.
- Do not reach into the DOM except where POS already focuses the barcode field.
- A UI-only change does not update API, domain, or database docs.

## Choosing a design

When more than one layout works, choose the one that:

1. Takes fewer actions.
2. Asks for less typing.
3. Is obvious the first time.
4. Stays comfortable when repeated all day.
5. Matches the classes and pages above.
6. Is smaller to maintain.
7. Preserves the current workflow.

Do not add structure to make a screen look newer.

## Hard stops

- Do not change business rules, validation, or who can do an action.
- Do not change an API request or response to make the screen easier.
- Do not change database schema, migrations, or stored data.
- Do not invent features for a UI task (discounts, payables, profit tiles, coupons, drafts, SKU, product images).
- Do not remove a feature, route, or control without explicit approval.
- Do not bypass role checks or show cashier-hidden customer fields.
- Do not weaken an error, audit entry, or idempotency behavior.
- Do not raise the production bundle budgets.
- If documentation and code disagree, say so. Prefer the implemented counter and `docs/ui/POS_UX.md` for counter UX.
- If the layout needs a backend or database change, stop, explain the need, and wait.

`.cursor/rules/60-pos-ux.mdc` may still list selecting a payment method as a sale step. The implemented counter and `docs/ui/POS_UX.md` keep cash selected until a customer is chosen. Follow the implemented counter.

## Workflow

1. Inspect the screen, its permission gate, and one neighboring page that shares the pattern.
2. Name the files to change and what will be reused.
3. Implement only the requested scope.
4. Check keyboard order, focus, loading, empty, success, and error states.
5. Check the desktop shell and the layout below 768px when the layout changed.
6. Run the page spec and `npx ng test --watch=false`. Run `npx ng build --configuration=production` after a visual change.
7. Exercise the changed flow in the browser, including the unchanged cash sale when POS was touched.
8. Report the files, the visible change, the test and build results, and anything left unfinished.

Do not start a UI audit or a redesign until the user asks for that screen.
