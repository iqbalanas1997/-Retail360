# POS UX Direction

## Primary goal
A counter person should be able to operate the POS all day with minimal cognitive load.

## Preferred flow
Scan/search -> quantity -> cart -> payment -> receipt.

## UX constraints
- Prefer one primary POS screen.
- Large readable product/cart/total information.
- Keyboard-first and barcode-scanner friendly.
- Minimal buttons.
- Minimal popups.
- Avoid exposing admin/reporting functions to cashier.
- Use no more than three primary visual colors unless accessibility/usability requires otherwise.
- Keep common actions reachable without deep navigation.

## Implemented counter
`/pos` scans or searches a product, takes a quantity and a shelf, and completes a cash sale. `/sales` lists receipts and opens the API receipt, which the browser can print. Inventory Staff can open the sales list and cannot complete a sale.

## Important
Do not implement UI details only from imagination. When a screen is requested, first inspect existing components and the latest approved requirements/design.
