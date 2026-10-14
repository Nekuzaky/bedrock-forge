using System;
using System.Globalization;
using System.Linq;
using BedrockForge.Nbt;
using UnityEngine.UIElements;

namespace BedrockForge.EditorTools.UI
{
    /// <summary>
    /// Editable tree for any NBT compound: every value can be changed, entries removed, list items duplicated and
    /// new fields added. Compounds and lists build their children only when unfolded, so huge records stay fast.
    /// <c>onChanged</c> is called after every edit; the owner writes the record back.
    /// </summary>
    public sealed class NbtEditor : VisualElement
    {
        private readonly Action _onChanged;

        private static readonly (string label, Func<object> create)[] NewTypes =
        {
            ("Text", () => ""),
            ("Integer (int)", () => 0),
            ("Byte", () => (byte)0),
            ("Short", () => (short)0),
            ("Long", () => 0L),
            ("Float", () => 0f),
            ("Double", () => 0d),
            ("Compound", () => new NbtCompound()),
            ("List of compounds", () => new NbtList(NbtType.Compound)),
        };

        public NbtEditor(NbtCompound root, Action onChanged, bool expanded = true)
        {
            _onChanged = onChanged;
            var container = new VisualElement();
            Add(container);
            FillCompound(container, root, expanded);
        }

        private void Changed() => _onChanged();

        private void FillCompound(VisualElement container, NbtCompound compound, bool rootLevel = false)
        {
            container.Clear();
            foreach (var key in compound.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList())
            {
                object value = compound[key];
                container.Add(Entry(key, value,
                    v => { compound[key] = v; Changed(); },
                    () => { compound.Remove(key); Changed(); FillCompound(container, compound); }));
            }
            container.Add(AddFieldRow(compound, () => FillCompound(container, compound)));
        }

        private void FillList(VisualElement container, NbtList list)
        {
            container.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                int index = i;
                container.Add(Entry($"[{i}]", list[i],
                    v => { list[index] = v; Changed(); },
                    () => { list.RemoveAt(index); Changed(); FillList(container, list); }));
            }

            if (list.Count > 0)
            {
                container.Add(Ui.Buttons(Ui.Button("+ Duplicate last", () =>
                {
                    list.Add(NbtExtensions.DeepClone(list[list.Count - 1]));
                    Changed();
                    FillList(container, list);
                })));
            }
            else if (list.ElementType == NbtType.Compound || list.ElementType == NbtType.End)
            {
                container.Add(Ui.Buttons(Ui.Button("+ Add compound", () =>
                {
                    list.Add(new NbtCompound());
                    Changed();
                    FillList(container, list);
                })));
            }
        }

        private VisualElement Entry(string name, object value, Action<object> set, Action remove)
        {
            switch (value)
            {
                case NbtCompound c:
                    return Folder($"{name}  {{{c.Count}}}", remove, content => FillCompound(content, c));
                case NbtList l:
                    return Folder($"{name}  [{l.Count}]", remove, content => FillList(content, l));
            }

            var row = Ui.Add(new VisualElement(), "cc-row");
            row.Add(Ui.Add(new Label(name) { tooltip = name }, "cc-label"));
            row.Add(Ui.Add(new Label(TypeTag(value)), "cc-nbt-type"));
            var field = Field(value, set);
            field.AddToClassList("cc-field");
            row.Add(field);
            row.Add(DeleteButton(remove));
            return row;
        }

        private VisualElement Folder(string title, Action remove, Action<VisualElement> fill)
        {
            var foldout = Ui.Add(new Foldout { text = title, value = false }, "cc-nbt-foldout");
            bool built = false;
            foldout.RegisterValueChangedCallback(e =>
            {
                if (e.target != foldout || !e.newValue || built) return;
                built = true;
                fill(foldout.contentContainer);
            });

            var header = foldout.Q<Toggle>();
            header?.Add(DeleteButton(remove));
            return foldout;
        }

        private static Button DeleteButton(Action remove)
        {
            var b = Ui.Add(new Button(remove) { text = "×", tooltip = "Delete" }, "cc-icon-button");
            return b;
        }

        private static VisualElement Field(object value, Action<object> set)
        {
            switch (value)
            {
                case byte b:
                    return Ui.Int(b, v => set((byte)Math.Max(0, Math.Min(255, v))));
                case short s:
                    return Ui.Int(s, v => set((short)Math.Max(short.MinValue, Math.Min(short.MaxValue, v))));
                case int i:
                    return Ui.Int(i, v => set(v));
                case long l:
                    return Ui.Long(l, v => set(v));
                case float f:
                    return Ui.Float(f, v => set(v));
                case double d:
                    return Ui.Double(d, v => set(v));
                case string str:
                    return Ui.Text(str, v => set(v));
                case int[] ia:
                    return Ui.Text(string.Join(", ", ia), v => set(ParseArray(v, int.Parse)));
                case long[] la:
                    return Ui.Text(string.Join(", ", la), v => set(ParseArray(v, long.Parse)));
                case byte[] ba:
                    return new Label($"{ba.Length} bytes");
                default:
                    return new Label(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
        }

        private static T[] ParseArray<T>(string text, Func<string, T> parse) =>
            text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => parse(s.Trim())).ToArray();

        private VisualElement AddFieldRow(NbtCompound compound, Action refresh)
        {
            var row = Ui.Add(new VisualElement(), "cc-row");
            var name = new TextField { value = "", tooltip = "New field name" };
            name.style.flexGrow = 1;
            var type = new DropdownField(NewTypes.Select(t => t.label).ToList(), 0);
            type.style.width = 130;
            var add = Ui.Button("+ Field", () =>
            {
                string key = name.value.Trim();
                if (key.Length == 0 || compound.ContainsKey(key)) return;
                compound[key] = NewTypes[Math.Max(0, type.index)].create();
                Changed();
                refresh();
            });
            row.Add(name);
            row.Add(type);
            row.Add(add);
            return row;
        }

        private static string TypeTag(object value) => value switch
        {
            byte _ => "b",
            short _ => "s",
            int _ => "i",
            long _ => "L",
            float _ => "f",
            double _ => "d",
            string _ => "txt",
            byte[] _ => "[B]",
            int[] _ => "[I]",
            long[] _ => "[L]",
            _ => "",
        };
    }
}
