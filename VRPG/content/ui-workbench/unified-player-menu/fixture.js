// Isolated preview. Quantities remain exact decimal strings, including Int64.MaxValue.
window.VRPG_PLAYER_MENU_FIXTURE = {
  developerPreview: true,
  categories: [["gear", "Gear"], ["materials", "Materials"], ["consumables", "Consumables"], ["keysquest", "Keys & Quest"], ["miscellaneous", "Miscellaneous"]],
  capacity: { unique: 120, aggregate: 512 },
  materials: [
    { name: "Copper ingot", type: "Metal", quantity: "9223372036854775807" },
    { name: "Oxidized iron", type: "Metal", quantity: "12840" },
    { name: "Cinder shards", type: "Elemental", quantity: "2467" }
  ],
  selected: {
    name: "Copper axe", rarity: "uncommon", type: "Main hand", itemLevel: 21, quantity: "1",
    stats: ["+12 maximum health", "+8% movement speed"],
    affixes: ["Maximum health: 100 → 112", "Movement speed: 100% → 108%"]
  },
  slots: [{ slot: "Main hand", item: "Copper axe" }, { slot: "Body", item: "Padded jerkin" }]
};
