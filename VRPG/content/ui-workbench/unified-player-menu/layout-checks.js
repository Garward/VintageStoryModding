// Read-only browser geometry audit. Run after fonts and stylesheet load; does not click or alter fixtures.
window.VRPG_MENU_LAYOUT_AUDIT = function () {
  const shell = document.querySelector(".vrpg-task4");
  if (!shell) return { ok: false, failures: ["Player-menu preview is not open."] };
  const failures = [], buttons = [], rows = [];
  const visible = element => element && element.getBoundingClientRect().width > 0 && element.getBoundingClientRect().height > 0;
  const browser = shell.querySelector(".vrpg-inventory-browser");
  if (visible(browser) && browser.scrollWidth > browser.clientWidth + 1) failures.push("Browser requires horizontal scrolling.");
  for (const button of shell.querySelectorAll(".vrpg-inventory-tabs button")) {
    if (!visible(button)) continue;
    const bounds = button.getBoundingClientRect(), range = document.createRange();
    range.selectNodeContents(button);
    const fragments = [...range.getClientRects()];
    const fits = fragments.every(rect => rect.left >= bounds.left - .5 && rect.right <= bounds.right + .5 &&
      rect.top >= bounds.top - .5 && rect.bottom <= bounds.bottom + .5);
    if (!fits || button.scrollWidth > button.clientWidth + 1) failures.push("Category label clips: " + button.textContent);
    buttons.push({ label: button.textContent, fits, width: bounds.width, scrollWidth: button.scrollWidth, clientWidth: button.clientWidth });
  }
  for (const row of shell.querySelectorAll(".vrpg-item-plate")) {
    if (!visible(row)) continue;
    const bounds = row.getBoundingClientRect(), icon = row.querySelector(".vrpg-item-glyph").getBoundingClientRect();
    const label = row.querySelector("strong").getBoundingClientRect(), amount = row.querySelector("b")?.getBoundingClientRect();
    if (icon.right > label.left + .5) failures.push("Icon overlaps label: " + row.querySelector("strong").textContent);
    if (amount && (amount.bottom > bounds.bottom + .5 || amount.right > bounds.right + .5)) failures.push("Quantity escapes item row.");
    rows.push({ label: row.querySelector("strong").textContent, iconRight: icon.right, labelLeft: label.left,
      rowBottom: bounds.bottom, quantityBottom: amount?.bottom ?? null });
  }
  return { ok: failures.length === 0, failures, buttons, rows,
    browser: visible(browser) ? { scrollWidth: browser.scrollWidth, clientWidth: browser.clientWidth } : null };
};
