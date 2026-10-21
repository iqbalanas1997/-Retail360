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

## Barcode workflow
1. Open Catalog, then Products, then New product or an existing product.
2. Type the manufacturer barcode printed on the package, or choose Generate shop barcode. A generated code starts with `R360-`. It is an internal Retail360 code, not a manufacturer UPC or EAN.
3. Add the code to the list. Enter adds it and does not save the product.
4. Choose Save product. The code says Saved only after that save succeeds. A generated code is compared with other products at save time. Generation itself only avoids a code already typed on this form.
5. Print label only when the code says Saved. The label is a 50 mm by 30 mm Code 128 label with the product name and the same digits.
6. At the counter, scan or type that saved code and press Enter. The scan looks up the active product and does not complete a sale by itself.
7. A product with no barcode can still be found by name.

## Implemented counter
`/pos` scans or searches a product, takes a quantity and a shelf, and completes a cash sale. Enter in the barcode field looks the code up. A finished scan clears that field and returns focus to it, so the next keyboard-wedge scan starts empty. An unknown code, an inactive product, and an API failure stay on screen, and the cashier can scan again. `/sales` lists receipts and opens the API receipt, which the browser can print. Inventory Staff can open the sales list and cannot complete a sale. A package barcode is attached on the product form. A shop barcode starts with `R360-` and its label is printed from that saved product as Code 128.

## Important
Do not implement UI details only from imagination. When a screen is requested, first inspect existing components and the latest approved requirements/design.
