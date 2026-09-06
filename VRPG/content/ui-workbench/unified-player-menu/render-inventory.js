window.VSUI_RENDER_INVENTORY = (() => {
  function render(root, state, data) {
    const escape = value => String(value).replace(/[&<>"']/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[character]));
    const inventory = window.VRPG_PLAYER_MENU_FIXTURE;
    const tabs = inventory.categories;
    const activeTab = state.inventoryTab || "gear";
    const gear = [inventory.selected, ...inventory.slots.map((slot, index) => ({
      name: slot.item,
      type: slot.slot,
      rarity: index === 0 ? "uncommon" : "common",
      quantity: "1",
      equipped: true
    }))];
    const materials = inventory.materials;
    const all = activeTab === "gear" ? gear : activeTab === "materials" ? materials : [];
    const visible = all.filter(item => item.name.toLowerCase().includes((state.inventorySearch || "").toLowerCase()));
    const selectedIndex = Math.min(state.inventoryEntry || 0, Math.max(0, visible.length - 1));
    const selected = visible[selectedIndex];
    const frame = root.closest(".game-frame");
    const width = Math.min(1120 * state.scale, (frame?.clientWidth || 1120) - 28);
    const height = Math.min(680 * state.scale, (frame?.clientHeight || 760) - 70);
    const alternate = width / state.scale < 800 || height / state.scale < 520;
    const inspect = alternate && state.inventoryInspect;
    const capacity = activeTab === "gear" ? `${gear.length} / ${inventory.capacity.unique} unique items` : `${all.length} / ${inventory.capacity.aggregate} stored types`;

    root.innerHTML = `
      <link rel="stylesheet" href="styles/player-menu-inventory.css?v=20260906-002">
      <section class="vrpg-menu-shell vrpg-focus-shell vrpg-inventory-shell vrpg-task4 ${alternate ? "is-alternate" : ""}" style="width:${width}px;height:${height}px">
        <div class="menu-preview-bar"><button data-preview-back ${!inspect ? "hidden" : ""}>Back</button><span>Developer preview · Local examples</span><button data-preview-close>Close</button></div>
        ${VSUI_RENDER_VRPG.menuHeader("Inventory", "Inventory", state)}
        <section class="vrpg-inventory-workspace">
          <section class="vrpg-panel vrpg-inventory-browser" ${inspect ? "hidden" : ""}>
            <nav class="vrpg-inventory-tabs">
              ${tabs.map(([code, label]) => `<button class="${activeTab === code ? "is-active" : ""}" data-inventory-tab="${code}">${label}</button>`).join("")}
            </nav>
            <div class="vrpg-inventory-tools">
              <input class="vrpg-search" aria-label="Search examples" value="${escape(state.inventorySearch || "")}" placeholder="Search examples">
              <span>${capacity}</span>
            </div>
            <div class="vrpg-inventory-grid">
              ${visible.map((item, index) => `
                <button class="vrpg-item-plate ${index === selectedIndex ? "is-active" : ""}" data-inventory-entry="${index}">
                  <span class="vrpg-item-glyph">${activeTab === "materials" ? "◆" : "⬡"}</span>
                  <span><strong>${item.name}</strong><em>${item.type}${item.equipped ? " · Equipped" : ""}</em></span>
                  ${item.quantity && BigInt(item.quantity) > 1n ? `<b>${BigInt(item.quantity).toLocaleString()}</b>` : ""}
                </button>
              `).join("") || (all.length > 0 ? `<div class="vrpg-empty-state"><strong>No matches</strong><span>Try another search.</span></div>` : `<div class="vrpg-empty-state"><strong>No examples in this category</strong><span>Choose another category.</span></div>`)}
            </div>
          </section>
          <aside class="vrpg-panel vrpg-inventory-inspector" ${alternate && !inspect ? "hidden" : ""}>
            ${!selected ? "<p>Choose an example to inspect it.</p>" : activeTab === "materials" ? `
              <div class="vrpg-kicker">Selected material</div>
              <h2>${selected.name}</h2>
              <strong class="vrpg-large-quantity">${BigInt(selected.quantity).toLocaleString()}</strong>
              <p>Local quantity example. Your items are unchanged.</p>
            ` : `
              <div class="vrpg-kicker">Selected gear</div>
              <h2 class="rarity-${selected.rarity}">${selected.name}</h2>
              <p>${selected.type}</p>
              <div class="vrpg-item-effects">${(selected.stats || []).map(line => `<span>${line}</span>`).join("")}</div>
              <div class="vrpg-item-effects">${(selected.affixes || []).map(line => `<span>${line}</span>`).join("")}</div>
              <p>Example comparison. Your items are unchanged.</p>
            `}
            ${activeTab === "gear" ? `<details class="vrpg-equipped-disclosure">
              <summary>Equipment <span>${inventory.slots.length}</span></summary>
              <div>${inventory.slots.map(slot => `<span><em>${slot.slot}</em><strong>${slot.item}</strong></span>`).join("")}</div>
            </details>` : ""}
          </aside>
        </section>
      </section>
    `;

    VSUI_RENDER_VRPG.attachNav(root, state);
    root.querySelectorAll("[data-inventory-tab]").forEach(button => {
      button.addEventListener("click", () => {
        state.inventoryTab = button.dataset.inventoryTab;
        state.inventoryEntry = 0;
        state.inventoryInspect = false;
        render(root, state, data);
      });
    });
    root.querySelectorAll("[data-inventory-entry]").forEach(button => button.addEventListener("click", () => {
      state.inventoryEntry = parseInt(button.dataset.inventoryEntry, 10); state.inventoryInspect = true; render(root, state, data);
    }));
    root.querySelector(".vrpg-search").addEventListener("change", event => {
      state.inventorySearch = event.target.value; state.inventoryEntry = 0; render(root, state, data);
    });
    root.querySelector("[data-preview-back]").addEventListener("click", () => { state.inventoryInspect = false; render(root, state, data); });
    root.querySelector("[data-preview-close]").addEventListener("click", () => {
      root.innerHTML = '<button class="vrpg-fitted-action" data-preview-reopen>Open developer preview</button>';
      root.querySelector("[data-preview-reopen]").addEventListener("click", () => render(root, state, data));
    });

    const spec = VSUI_MODEL.createSpec("inventory", state);
    VSUI_MODEL.add(spec, "TypedInventory", "entries", { tab: activeTab, count: visible.length, capacity });
    VSUI_MODEL.add(spec, "InventoryTooltip", "selectedItem", { item: selected?.name || "", stats: selected?.stats?.length || 0 });
    VSUI_MODEL.add(spec, "DetailPane", "inventoryState", { rarity: selected?.rarity || "", alternate, inspect });
    const specOutput = document.getElementById("specOutput");
    if (specOutput) specOutput.textContent = JSON.stringify(spec, null, 2);
    return spec;
  }

  return { render };
})();
