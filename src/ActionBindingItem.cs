using System;
using System.Collections.Generic;

namespace EldenInstantRoll
{
    public sealed class ActionBindingItem
    {
        public const string CategoryCore = "핵심 액션";
        public const string CategoryCustom = "사용자 추가";

        public const string DodgeActionId = "dodge";

        public string Id { get; private set; }
        public string Category { get; private set; }
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public KeyBinding Binding { get; set; }
        public bool IsCustom { get; private set; }

        public ActionBindingItem(string id, string category, string name, bool enabled, KeyBinding binding, bool isCustom)
        {
            Id = id;
            Category = category;
            Name = name;
            Enabled = enabled;
            Binding = binding;
            IsCustom = isCustom;
        }

        public ActionBindingItem Clone()
        {
            return new ActionBindingItem(Id, Category, Name, Enabled, Binding, IsCustom);
        }

        public static List<ActionBindingItem> CreateDefaultNightreignActions()
        {
            List<ActionBindingItem> list = new List<ActionBindingItem>();

            // Elden Ring Nightreign standard 7 core actions (only Dodge enabled by default)
            list.Add(new ActionBindingItem(DodgeActionId, CategoryCore, "구르기", true, KeyBinding.FromKeyboard(0x2A, false, "LShift"), false));
            list.Add(new ActionBindingItem("surge_sprint", CategoryCore, "달리기 (질주)", false, KeyBinding.FromKeyboard(0x38, false, "LAlt"), false));
            list.Add(new ActionBindingItem("character_skill", CategoryCore, "스킬", false, KeyBinding.FromKeyboard(0x12, false, "E"), false));
            list.Add(new ActionBindingItem("ultimate_art", CategoryCore, "아츠", false, KeyBinding.FromKeyboard(0x13, false, "R"), false));
            list.Add(new ActionBindingItem("flask", CategoryCore, "성배병", false, KeyBinding.FromKeyboard(0x02, false, "1"), false));
            list.Add(new ActionBindingItem("use_item", CategoryCore, "아이템 사용", false, KeyBinding.FromKeyboard(0x22, false, "G"), false));
            list.Add(new ActionBindingItem("switch_item", CategoryCore, "아이템 변경", false, KeyBinding.FromKeyboard(0x03, false, "2"), false));

            return list;
        }
    }
}
