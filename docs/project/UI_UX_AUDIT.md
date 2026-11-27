# Retail360 UI/UX audit

Audit date: 2026-11-21. No screen was changed.

The shop can be learned. Lists, forms, errors, and remove confirmations follow one pattern, and the counter already scans into a cart. The cost shows up across a full day: every sale ends by leaving the counter, the next scan is blocked until a shelf is chosen again, and the counter sits at the bottom of a long menu. Those are the changes worth making first.

## 1. Executive summary

Retail360 is one Angular 21 application with standalone pages and a single stylesheet. There is no component library. The visual language is already the right one for a shop: paper background, dark text, one green accent, plain tables, and short forms.

The repeated cashier path is longer than the business rules require.

Today, a cash sale is: open POS, choose a shelf, scan, complete, land on the receipt, then find POS again. The shelf choice is lost on that navigation. A scan before the shelf is chosen shows "Choose a shelf for every line." and adds nothing. Customer search is always on the cash screen. After a customer is selected, a payment amount for old credit sits next to the sale method and is easy to read as the amount of this sale.

Sign-in always opens Products. POS is the sixteenth link in the sidebar.

Inventory search only filters the rows already loaded. On a purchase, Enter in the product finder can save the document. Zero amounts render as `0.0`.

Do not add a UI kit, charts, supplier payment status, a receivable tile, or a profit figure. Those would add surface without removing work.

## 2. Existing UI architecture and styling

| Piece | Where it lives |
| --- | --- |
| Shell and role links | `frontend/src/app/shell/shell.html`, `frontend/src/app/core/session.service.ts` |
| Routes and page titles | `frontend/src/app/app.routes.ts` |
| Styles | `frontend/src/styles.scss` only. Tokens are `--ink` `#1f2933`, `--paper` `#f4f0e6`, `--green` `#1f6b4a`, shortage `#9b2c2c`, plus soft `--line` / `--line-strong` and `--radius` |
| Shared classes | `toolbar`, `form`, `card`, `table-wrap`, `notice`, `notice.ok`, `notice.error`, `hint`, `status`, `summary-grid`, `metric`, `button.secondary`, `check` |
| Pages | One standalone component per route. No shared widget folder |
| Tests | Vitest specs beside the pages |

Buttons are about 40px tall and type is 16px. That is readable. The stylesheet does not define a larger counter size or a `:focus-visible` ring. Remove is the only action that asks for confirmation. Errors use `notice error` and `role="alert"`. Success uses `notice ok`. Empty and loading states are text, not illustrations.

Below 800px the sidebar becomes a wrapping row of every link above the page (`styles.scss`).

Role gates that the layout must keep:

- Cashier, Manager, and Admin can sell. Inventory Staff cannot.
- Customers in the sidebar are Admin and Manager only (`canWriteCustomers`). Cashiers select a customer on POS.
- Dashboard is Admin and Manager. Audit and Users are Admin.
- Cashier customer reads omit address and notes. The layout must not reveal those fields.

## 3. Screen-by-screen findings

### Shell and navigation

The sidebar is deep ink with paper text. Group labels are quiet toggles (tracked uppercase with «/»). Open/closed state per group is stored in `localStorage` as `retail360.shell.navGroups` (defaults all open). Navigating into a closed group opens it so the active link stays visible. Active routes use a quiet green accent bar and soft fill. On wide screens a quiet « » control collapses the rail to first-letter marks with native `title` tooltips (`retail360.shell.navCollapsed`); in that letter-rail mode every link stays shown and group headers hide. Below 768px rail collapse is ignored; the Menu drawer keeps the same group toggles and still closes after a route.

A cashier still sees catalog, inventory, suppliers, purchases, and expenses before POS. Sign-in goes to `/catalog/products` (`login-page.ts`). That is the right home for someone editing products and the wrong home for someone selling.

On a narrow screen the same links wrap across the top before any work starts.

### Dashboard

`/dashboard` is one board for Admin and Manager: period chips with a pressed state, custom dates, a sales-led metric strip, sales/purchase/expense panels, recent rows, and a low-stock panel that does not follow the selected dates. There is no chart and no profit number. That matches the module. A zero total still renders as `0.0`.

### POS and sales

`/pos` focuses the barcode field, clears it when a lookup starts, and returns focus for the next scan (`pos-page.ts` `findBarcode`, `focusBarcode`). Enter submits the lookup and does not complete the sale. Quantity defaults to 1. Cash is selected. Credit and the outstanding balance appear only after a customer is selected. The cart shows name, shelf, quantity, price, line total, and remove. The server total is labeled as such. A failed sale keeps the cart and the operation id.

`/sales` searches by receipt number and links to the receipt. The list shows Cash or Credit from `Sale.paymentMethod` on a soft white panel. The receipt uses clearer meta and total blocks, Print, and New sale when the role can sell. Print hides the sidebar. Existing receipt fields only.

### Catalog

The product list searches by name, can limit to offered products, and shows unit, selling price, minimum stock, barcodes, and offered state. Opening a product is one link. The form has the real catalog fields and no stock quantity. Enter on a barcode adds it and does not save. A saved code can be printed as a Code 128 label.

The barcode instructions are two long paragraphs. The list uses "Offered" where other modules use "Active". Several barcodes in one cell become hard to scan, which was visible on the live product list.

Categories, brands, and units share one Catalog names page at `/catalog/names`: one full-height board, segmented tabs, composer add row, ticket-style name rows with Save only when dirty, and a calm remove confirm. Cashiers see names as text. Old category/brand/unit routes redirect to the matching tab.

### Inventory

Stock, locations, opening, adjust, and transfer share one Inventory hub at `/inventory` with quiet tabs on the same board chrome as Catalog names. Old `/inventory/*` routes redirect to the matching tab. Opening, Adjust, and Transfer tabs show only for Admin, Manager, and Inventory Staff.

Stock shows product, on hand, minimum, status, and offered. Choosing a product loads shelf balances. Stock search still filters only loaded rows (`visibleRows`). A true name search needs an API change and is not part of a layout pass.

Locations edit every area and shelf inline. The actions are clear and the rows are dense. There is no search, which is acceptable while the shop has few areas.

### Purchasing

The list filters by supplier and by recorded or received, and shows reference, supplier, date, total, and status. The document form is supplier, date, reference, notes, and lines. Receive is a separate block that asks for an active shelf per line. Recorded and received stay distinct. There is no payment status, and the screen should not gain one.

The product finder sits inside the save form (`purchase-form-page.html`). Enter in that field finds products and does not save the purchase. The list and form use the soft white table/panel chrome from Phase 1. Recorded versus received stay separate; there is no payment or payable UI.

### Suppliers

The list shows name, contact, phone, email, and status. The form matches the stored fields. The page then lists that supplier's purchases. The pattern matches customers. No payable balance is shown, which is correct.

### Customers and credit

The list shows name, phone, email, status, and outstanding, and searches name, phone, or email. The form is name plus optional contact fields, then outstanding, one payment amount, and that customer's sales. A payment does not rewrite a receipt. This is a short, learnable credit screen for Admin and Manager.

The confusing credit interaction is on POS, not on this form. See section 9.

Outstanding of zero uses the same `0.0` formatting as other money.

### Expenses

The list filters by date and category and shows a total on a soft white panel with right-aligned amounts. The form is category, amount, date, and notes. Payment is fixed cash text, not a dropdown. Categories stay the fixed set. Notes on the list can make rows tall.

### Users

The list is user name, role, and status. The form is user name, one role, and a password, with a hint that a blank password keeps the current one and a new password signs that account out. Deactivate and remove are blocked for the signed-in account. That is the right amount of form.

"Active only" does not use the `check` class (`user-list-page.html`). On the live page the checkbox was a 254px by 13px block, unlike the compact checkbox on products, customers, and suppliers.

### Audit

Filters are date, user, action, and record type. The table shows time, user, action, record, and a short detail summary. There is no edit or delete. The time is the raw `occurredAt` value, and the record column is the entity type plus the raw id (`audit-page.html`). An owner can filter, then has to read UUIDs.

### Account

`/account` asks for the current password and a new one, says the new password must be at least 8 characters, and says the user must sign in again. The form keeps typed values when validation fails. Sign-in itself does not focus the user name.

## 4. Usability problems

Verified means the behavior was read in source and, where noted, seen in the running app on 2026-11-21.

| Id | Priority | Problem | Evidence |
| --- | --- | --- | --- |
| U1 | P0 | Completing a sale leaves the counter | `pos-page.ts` `openReceipt` navigates to `/sales/:id`. The receipt has Back to sales and Print, not a way to start the next sale |
| U2 | P0 | The next scan needs a shelf again | `addProduct` returns "Choose a shelf for every line." when `shelfId` is empty. Leaving the page destroys that choice. Live POS opened on "Choose a shelf" |
| U3 | P1 | Sellers land in the catalog and POS is late in the menu | `login-page.ts` navigates to `/catalog/products`. `shell.html` places POS after the other modules. Live sign-in confirmed both |
| U4 | P1 | Customer and old-credit controls interrupt the cash sale | `pos-page.html` always shows customer search. After a customer is selected, Sale payment and Payment amount sit above the cart. The amount records a balance payment and does not change the cart |
| U5 | P1 | An edited quantity applies to the next scan | `addProduct` reads `quantity()` and does not set it back to 1 |
| U6 | P1 | Stock search misses products that are not loaded | `stock-page.html` hint and `visibleRows()` filter only `rows()` |
| U7 | P1 | Enter in a purchase product search can save the document | Done Phase 2. Finder Enter calls `onProductKey` and searches only |
| U8 | P1 | Narrow layout puts the whole menu above the work | `styles.scss` max-width 800px wraps the sidebar |
| U9 | P2 | Zero money is shown as `0.0` | `formatScaled` in `catalog-rules.ts`. Live dashboard totals and a product minimum of `0.0` |
| U10 | P2 | The dashboard does not show which period is selected, and low stock is last | `dashboard-page.html` has no selected style. Live period buttons were the same green. Low stock follows the other sections |
| U11 | P2 | Sales history hides cash versus credit | Done Phase 2. List and receipt show Cash or Credit |
| U12 | P2 | The audit row is an id and a raw timestamp | `audit-page.html` |
| U13 | P2 | The user filter checkbox is a full-width block | `user-list-page.html` omits `check`. Live size 254 by 13 |
| U14 | P2 | Catalog barcode help and the offered column take more reading than the task | `product-form-page.html`, `product-list-page.html` |
| U15 | P2 | Location and name-list rows are dense and the edit fields are unlabeled | Locations remain dense. Catalog names inputs now have a visually hidden label |
| U16 | P3 | The expense payment control can only be Cash | `expense-form-page.html` |
| U17 | P3 | Sign-in does not focus the user name | `login-page.html` has no autofocus |

A server-side stock name filter would fix U6 properly. That is an API change, so it waits for approval. Until then, the screen should not look like a shop-wide search.

## 5. Verified issues and proposed improvements

| Issue | Today | Proposed experience | Changes business rules? |
| --- | --- | --- | --- |
| U1, U2 | Sale opens the receipt. The next customer starts with an empty shelf | Stay on POS. Show "Sale completed" with the receipt number, a Print link, and a cleared cart. Keep the shelf. Focus the barcode | No. The receipt route stays |
| U3 | Admin and cashier both open Products | Cashier opens POS. Admin and Manager can still open Dashboard or Products. Move POS up for roles that can sell | No. Same routes and gates |
| U4 | Customer search is always visible. Credit collection sits in the scan column | Keep cash as the untouched default. Put customer search with the cart, closed until needed. Label the balance payment "Collect outstanding", separate from this sale | No. Same credit rules |
| U5 | Quantity 2 stays armed for the next scan | After a line is added, show that line's quantity and return the next-scan quantity to 1 | No |
| U6 | A name search can hide stock that exists on a later page | Say "Showing loaded rows" and keep the status filter. A real name query needs an approved API field | The honest label is UI-only |
| U7 | Enter may save a half-finished purchase | Enter in the finder only searches, as it already does on stock posting | No |
| U8 | Links consume the top of a phone screen | Keep the current links. On a small screen, put POS, Sales, and Stock in one row and leave the rest under the existing groups | No |
| U9 | `0.0` | Show `0.00` for money and `0.000` for quantities | Display only |
| U10 | Period and low stock compete with totals | Mark the active period. Move the low-stock count up beside the totals. Keep the UTC sentence as a hint, not the first thing | No new metrics |
| U11 | Open each receipt to see cash or credit | Add the payment column the API already returns | No |
| U12 | UUID and raw time | Show the same local time style as receipts, and the detail summary before the id | No audit writes |
| U13–U17 | Small inconsistencies | Use `check`, shorten barcode help, label inline fields, show Cash as text, focus the sign-in name | No |

## 6. Priorities

- **P0.** U1 and U2. They add steps to every sale.
- **P1.** U3, U4, U5, U6, U7, U8. Daily friction or a real mis-click.
- **P2.** U9 through U15. Readability and consistency.
- **P3.** U16 and U17. Small, do them with the neighboring screen.

## 7. Design system refinements

Keep the three colors, the global stylesheet, and the existing classes. Do not add a library.

- Add one `:focus-visible` ring in `--ink` so keyboard focus does not depend on the browser.
- Add one `aria-pressed` or class for the selected dashboard period, using the existing green.
- Let POS controls use the current type size as a minimum. Do not shrink the counter to fit more on the screen.
- Use `check` for every checkbox.
- Keep remove confirmation inline. Do not add confirms to save, search, scan, or complete sale.
- Fix `formatScaled` so a whole number still shows the scale. Do not change rounding rules.
- Leave the production bundle budgets where they are.

## 8. Navigation

Recommended order for someone who can sell: POS, Sales, then the modules they already can open. Admin keeps Users, Audit, and Dashboard where they are, with Dashboard available at the top of Shop.

Recommended landing page:

- Cashier: `/pos`
- Inventory Staff: `/inventory`
- Admin and Manager: `/dashboard`

Do not remove a module from a role that can already open it. Do not show the customer admin form to cashiers.

## 9. POS layout and flow

### Today

1. Sign in and land on Products.
2. Find POS below the other modules.
3. Barcode is focused, which is right.
4. Choose a shelf in the product row or the scan does nothing useful.
5. Scan. The line uses the current quantity and shelf.
6. Customer search and its hint sit between the barcode and the cart even for a walk-in cash sale.
7. If a customer is selected, choose Cash or Credit, and a second amount collects old credit.
8. Complete sale.
9. The receipt page replaces the counter.
10. Return to POS and choose the shelf again.

### Recommended

1. Sign in and land on POS when the role can sell.
2. The barcode is focused. The last shelf used on this counter stays selected.
3. Scan. Quantity 1 is added. The cart and total stay on screen. The next scan quantity returns to 1.
4. Change a line's quantity or shelf in the cart, or remove the line. A bad quantity stays on that line and does not wipe the cart.
5. Cash needs nothing else. Complete sale.
6. POS remains. A line says the receipt number and offers Print and Sales history. The cart is empty. The barcode is focused. The shelf remains.
7. Customer is a closed search under the cart. Choosing one shows outstanding and Cash or Credit. Credit still applies to the whole sale.
8. Collecting old credit is a separate amount, labeled as outstanding, and it does not change the cart or the receipt.

Business rules that stay: cash by default, credit only with an active customer, one shelf per line, server prices and totals, no split tender, no discount, stock still posted by the existing sale.

## 10. Suggested implementation sequence

1. POS stay-on-screen, remembered shelf, quantity reset, and a visible receipt link. Cover the cash scan path with the existing POS spec.
2. Role landing page and move POS up in the shell. Check Cashier, Inventory Staff, and Admin.
3. Move customer and credit collection out of the scan path. Wording only. Same requests.
4. Stop Enter from saving a purchase. Point stock search at loaded rows until a name filter is approved.
5. Money and quantity scale display, dashboard period and low-stock placement, sales payment column.
6. Checkbox, audit time, barcode help, expense payment text, sign-in focus.

Each step is one screen or one shared formatter. Do not combine them into a visual redesign.

## 11. Regression risks and verification

| Change | Risk | Check |
| --- | --- | --- |
| Stay on POS after a sale | A second complete could reuse or drop the operation id | The cart clears, the attempt completes, and the next sale gets a new id. Replay of the same id still returns the original sale |
| Remembered shelf | A line could keep a shelf the user no longer wants | The cart shelf stays editable. An inactive shelf can still be sold from, matching the current rule |
| Quantity reset | A deliberate quantity of 2 could be lost | The added line keeps 2. Only the next-scan field returns to 1 |
| Customer block moves | Credit could be omitted or sent for cash | Cash request still has no `paymentMethod`. Credit is sent only when Credit is selected and a customer is present |
| Landing page | A cashier could lose catalog access, or an admin could lose the dashboard | Each role opens its home and can still open every link it has today |
| `formatScaled` | Prices and stock quantities could gain or lose digits | `12.50` stays `12.50`. `0` money becomes `0.00`. Extra fractional digits are still rejected |
| Purchase Enter | Save could stop working from the submit button | Enter searches. The Save button still saves |

Verification for each implementation:

- `npx ng test --watch=false` and `npx ng build --configuration=production` from `frontend/`.
- Browser: cash sale by scan, failed scan with the cart intact, credit sale with a customer, payment that does not change the cart, receipt print, and the unchanged list and form for that step.
- Desktop width and a width below 768px when the shell changes.

## 12. Completed and remaining

Checked against the shell and the counter on 2026-11-21. Shared chrome Phase 1 updated 2026-11-25. Daily ops Phase 2 verified 2026-11-26 (wide + ~390px, Angular 140 tests, production build).

| Id | Status |
| --- | --- |
| U1, U2, U4, U5 | Done on the counter. The sale stays on `/pos`, the shelf is remembered, the next quantity returns to 1, and customer search and outstanding stay closed for a walk-in cash sale. The counter is entry, cart, and checkout. A repeat scan on the same shelf increases that line. The next scan clears the on-screen receipt. Browse and search open in a dialog; a row click adds |
| U3 | Done for the cashier landing page and for menu order. POS is first under Daily operations for Admin, Manager, and Cashier. Admin and Manager still sign in to `/catalog/products` |
| U8 | Done. Below 768px the links are in a Menu drawer. Choosing a route closes it. The page is not pushed down. Drawer matches the ink sidebar |
| Shared chrome | Done Phase 1. Soft `--line` borders, white inputs/cards/tables, quieter topbar, and calmer notices apply shop-wide from `styles.scss`. POS panels keep their own white entry/cart and green checkout. Sidebar: ink rail, group expand/collapse with localStorage, desktop letter-rail collapse; letter-rail shows all links |
| U7, U11 | Done Phase 2. Purchase product Enter finds products and does not save. Sales history shows Cash or Credit. Receipt layout is clearer. Expenses list and form use the soft chrome; payment stays cash text. Menu drawer unchanged at ~390px |
| U6, U9, U10, U12–U17 | Open. Next: Phase 3 Catalog and Inventory. U6 still needs an approved stock-name API |

The shell now uses the existing routes in four groups: Daily operations, Products and inventory, People, and Management. Inventory is one shell link with in-page tabs. A group is omitted when the role has no link in it. No role or request changed.

Product entry on the counter also accepts a name or barcode while typing, and Browse products lists one page at a time. The catalog list still has no category or barcode query, and shelf quantity still comes from the existing balances call.

## Out of scope

Supplier payments, profit, returns, discounts, split tender, opening historical credit, and any database or API change. A stock name filter is the only backend idea in this audit, and it is not approved.
