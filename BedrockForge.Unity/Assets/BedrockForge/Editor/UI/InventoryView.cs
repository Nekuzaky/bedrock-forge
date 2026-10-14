using System;
using System.Linq;
using BedrockForge.Nbt;
using UnityEngine.UIElements;

namespace BedrockForge.EditorTools.UI
{
    /// <summary>
    /// A grid of item slots over a Bedrock item list (Inventory, Armor, ChestItems, Items…). Items carry a
    /// <c>Slot</c> byte when the list is sparse; otherwise the list index is the slot. Click a slot to edit it.
    /// </summary>
    public sealed class InventoryView : VisualElement
    {
        private readonly NbtList _items;
        private readonly int _slotCount;
        private readonly bool _usesSlotField;
        private readonly Action _onChanged;
        private readonly VisualElement _grid;
        private readonly VisualElement _details;
        private int _selected = -1;

        public InventoryView(NbtList items, int slotCount, Action onChanged)
        {
            _items = items;
            _onChanged = onChanged;
            _usesSlotField = items.OfType<NbtCompound>().Any(i => i.ContainsKey("Slot"));
            _slotCount = Math.Max(slotCount, items.Count == 0 ? 0 : MaxSlot() + 1);
            _grid = Ui.Add(new VisualElement(), "cc-grid");
            _details = new VisualElement();
            Add(_grid);
            Add(_details);
            Rebuild();
        }

        private int MaxSlot() => _usesSlotField
            ? _items.OfType<NbtCompound>().Select(i => i.GetInt("Slot") ?? 0).DefaultIfEmpty(0).Max()
            : _items.Count - 1;

        private NbtCompound? ItemAt(int slot)
        {
            if (_usesSlotField)
                return _items.OfType<NbtCompound>().FirstOrDefault(i => (i.GetInt("Slot") ?? -1) == slot);
            return slot < _items.Count ? _items[slot] as NbtCompound : null;
        }

        private NbtCompound GetOrCreate(int slot)
        {
            var item = ItemAt(slot);
            if (item != null)
                return item;

            item = EmptyItem();
            if (_usesSlotField)
            {
                item["Slot"] = (byte)slot;
                _items.Add(item);
            }
            else
            {
                // Dense lists (armor, hands): the index is the slot.
                while (_items.Count < slot)
                    _items.Add(EmptyItem());
                if (_items.Count == slot)
                    _items.Add(item);
                else
                    _items[slot] = item;
            }
            return item;
        }

        private static NbtCompound EmptyItem() =>
            new NbtCompound { ["Count"] = (byte)0, ["Damage"] = (short)0, ["Name"] = "", ["WasPickedUp"] = (byte)0 };

        private void Rebuild()
        {
            _grid.Clear();
            for (int slot = 0; slot < _slotCount; slot++)
            {
                int s = slot;
                var item = ItemAt(slot);
                string name = ShortName(item?.GetString("Name"));
                int count = item?.GetInt("Count") ?? 0;
                bool filled = !string.IsNullOrEmpty(name) && count > 0;

                var cell = Ui.Add(new Button(() => Select(s)) { tooltip = filled ? $"{item!.GetString("Name")} ×{count}" : $"Slot {slot}" }, "cc-slot");
                cell.text = filled ? $"{name}\n×{count}" : "";
                if (filled) cell.AddToClassList("cc-slot--filled");
                if (slot == _selected) cell.AddToClassList("cc-slot--selected");
                _grid.Add(cell);
            }
        }

        private void Select(int slot)
        {
            _selected = slot;
            Rebuild();
            _details.Clear();

            var item = ItemAt(slot);
            _details.Add(Ui.Section($"Slot {slot}"));
            _details.Add(Ui.Row("Item", Ui.Text(item?.GetString("Name") ?? "", v =>
            {
                var it = GetOrCreate(slot);
                it["Name"] = v.Length == 0 || v.Contains(":") ? v : "minecraft:" + v;
                if (v.Length > 0 && (it.GetInt("Count") ?? 0) == 0) it["Count"] = (byte)1;
                Changed();
            })));
            _details.Add(Ui.Row("Count", Ui.Int(item?.GetInt("Count") ?? 0, v =>
            {
                GetOrCreate(slot)["Count"] = (byte)Math.Max(0, Math.Min(255, v));
                Changed();
            })));
            _details.Add(Ui.Row("Damage / variant", Ui.Int(item?.GetInt("Damage") ?? 0, v =>
            {
                GetOrCreate(slot)["Damage"] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, v));
                Changed();
            })));
            _details.Add(Ui.Buttons(Ui.Button("Clear slot", () =>
            {
                var it = GetOrCreate(slot);
                it["Name"] = "";
                it["Count"] = (byte)0;
                it["Damage"] = (short)0;
                it.Remove("tag");
                it.Remove("Block");
                Changed();
                Select(slot);
            }, "cc-button--danger")));

            if (item != null)
            {
                _details.Add(Ui.Hint("The whole item (enchantments in \"tag\", custom name…):"));
                _details.Add(new NbtEditor(item, Changed, expanded: false));
            }
        }

        private void Changed()
        {
            _onChanged();
            Rebuild();
        }

        private static string ShortName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int colon = name!.IndexOf(':');
            return (colon >= 0 ? name.Substring(colon + 1) : name).Replace('_', ' ');
        }
    }
}
