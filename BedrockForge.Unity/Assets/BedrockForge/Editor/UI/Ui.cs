using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace BedrockForge.EditorTools.UI
{
    /// <summary>Small factory helpers so panels read as a list of rows.</summary>
    public static class Ui
    {
        public static Label Section(string text) => Add(new Label(text.ToUpperInvariant()), "cc-section");

        public static Label Hint(string text) => Add(new Label(text), "cc-hint");

        public static Label Warning(string text) => Add(new Label(text), "cc-warning");

        public static VisualElement Row(string label, VisualElement field)
        {
            var row = Add(new VisualElement(), "cc-row");
            row.Add(Add(new Label(label) { tooltip = label }, "cc-label"));
            field.AddToClassList("cc-field");
            row.Add(field);
            return row;
        }

        public static Button Button(string text, Action onClick, string? modifier = null)
        {
            var b = Add(new Button(onClick) { text = text }, "cc-button");
            if (modifier != null) b.AddToClassList(modifier);
            return b;
        }

        public static VisualElement Buttons(params Button[] buttons)
        {
            var row = Add(new VisualElement(), "cc-buttons");
            foreach (var b in buttons) row.Add(b);
            return row;
        }

        public static TextField Text(string value, Action<string> onChange)
        {
            var f = new TextField { value = value, isDelayed = true };
            f.RegisterValueChangedCallback(e => onChange(e.newValue));
            return f;
        }

        public static IntegerField Int(int value, Action<int> onChange)
        {
            var f = new IntegerField { value = value, isDelayed = true };
            f.RegisterValueChangedCallback(e => onChange(e.newValue));
            return f;
        }

        public static LongField Long(long value, Action<long> onChange)
        {
            var f = new LongField { value = value, isDelayed = true };
            f.RegisterValueChangedCallback(e => onChange(e.newValue));
            return f;
        }

        public static FloatField Float(float value, Action<float> onChange)
        {
            var f = new FloatField { value = value, isDelayed = true };
            f.RegisterValueChangedCallback(e => onChange(e.newValue));
            return f;
        }

        public static DoubleField Double(double value, Action<double> onChange)
        {
            var f = new DoubleField { value = value, isDelayed = true };
            f.RegisterValueChangedCallback(e => onChange(e.newValue));
            return f;
        }

        public static Toggle Bool(bool value, Action<bool> onChange)
        {
            var t = new Toggle { value = value };
            t.RegisterValueChangedCallback(e => onChange(e.newValue));
            return t;
        }

        /// <summary>Dropdown over (value, label) pairs; unknown current values are shown as "autre (n)".</summary>
        public static DropdownField Choice(int value, IReadOnlyList<(int value, string label)> options, Action<int> onChange)
        {
            var labels = new List<string>();
            int index = -1;
            for (int i = 0; i < options.Count; i++)
            {
                labels.Add(options[i].label);
                if (options[i].value == value) index = i;
            }
            if (index < 0)
            {
                labels.Add($"other ({value})");
                index = labels.Count - 1;
            }
            var d = new DropdownField(labels, index);
            d.RegisterValueChangedCallback(_ =>
            {
                int i = d.index;
                if (i >= 0 && i < options.Count) onChange(options[i].value);
            });
            return d;
        }

        /// <summary>Three float fields side by side (X, Y, Z).</summary>
        public static VisualElement Xyz(float x, float y, float z, Action<float, float, float> onChange)
        {
            var box = Add(new VisualElement(), "cc-xyz");
            var fx = new FloatField { value = x, isDelayed = true, tooltip = "X" };
            var fy = new FloatField { value = y, isDelayed = true, tooltip = "Y" };
            var fz = new FloatField { value = z, isDelayed = true, tooltip = "Z" };
            void Changed(ChangeEvent<float> _) => onChange(fx.value, fy.value, fz.value);
            fx.RegisterValueChangedCallback(Changed);
            fy.RegisterValueChangedCallback(Changed);
            fz.RegisterValueChangedCallback(Changed);
            box.Add(fx);
            box.Add(fy);
            box.Add(fz);
            return box;
        }

        public static T Add<T>(T element, string className) where T : VisualElement
        {
            element.AddToClassList(className);
            return element;
        }
    }
}
