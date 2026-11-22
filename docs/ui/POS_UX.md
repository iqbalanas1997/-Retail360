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
`/pos` keeps the barcode and product search across the top left, the cart below them, and the total, item count, quantity, customer, and Complete sale in a checkout panel at the upper right. It scans or searches a product, takes a quantity and a shelf, and completes a sale without leaving the counter. Cash is the walk-in default. Customer search stays closed until the cashier adds a customer. Credit appears only then. Outstanding is collected with a separate amount that does not change the sale total. The next scan quantity returns to 1. The selected shelf stays until the cashier changes it or that shelf is no longer in the list. A completed sale shows its receipt on the counter, with New sale, Print, and a link to `/sales/:id`. The next successful scan clears that receipt and starts a new cart. The same product on the same shelf increases the existing line. The same product on another shelf stays a separate line. Each cart line shows the on-hand quantity of its shelf and whether that quantity covers the line after other lines of the same product on that shelf. The shelf list shows every shelf's quantity and cannot select a shelf that cannot cover the current quantity. The remembered shelf is kept when it has enough; otherwise the shelf with the most remaining stock is selected. If no single shelf can cover the quantity, the line names the product, each shelf's usable quantity, and the quantity required, and Complete sale stays disabled. A line that uses the last usable quantity on its shelf is marked Low and can still be sold. The sale is not split across shelves. A sale the server rejects for stock names each short product, shelf, required quantity, and available quantity. Search matches a name while typing and also accepts a barcode. Arrow keys move through the matches, Enter or a click adds the product, and Escape closes the list. Browse products opens a paged list in the cart column and stays open so the next product is one click. The checkout panel stays where it is. The product list has no category filter, so browse does not offer one. On-shelf quantity comes from the existing shelf balances when a shelf is selected. Search and browse use the current quantity, then return it to 1. Ctrl+Enter completes the sale except from the barcode field, where Enter only adds the product. Enter in the barcode field looks the code up and does not complete the sale. A finished scan clears that field and returns focus to it. An unknown code, an inactive product, and an API failure stay on screen with the cart. `/sales` still lists receipts and opens the same receipt, which the browser can print. A cashier signs in to `/pos`. POS is the first item under Daily operations for Admin, Manager, and Cashier. On a narrow screen the same links open from Menu and close after a choice, so the barcode stays at the top of the counter. Inventory Staff can open the sales list and cannot complete a sale. A package barcode is attached on the product form. A shop barcode starts with `R360-` and its label is printed from that saved product as Code 128.

## Important
Do not implement UI details only from imagination. When a screen is requested, first inspect existing components and the latest approved requirements/design.
