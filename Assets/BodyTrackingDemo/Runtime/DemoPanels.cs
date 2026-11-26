using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace Blacktheon.BodyTracking.Demo
{
    internal struct DemoChoices
    {
        const string Pref = "Blacktheon.SeatedDemo.";
        internal int Role; // zero selects the stock single-wearer solver.
        internal SoldierModel Model;
        internal string Pc, Tablet;
        internal bool Solo => Role == 0;
        internal int Slot => Mathf.Max(1, Role);
        internal string Label => Solo ? "Six trackers / solo" : "Paired feet / seat " + Slot;
        internal static bool Saved => PlayerPrefs.HasKey(Pref + "role");
        // Older builds saved the manually entered PC here. Use it as a discovery hint on upgrade.
        internal static string LastTrackingPc
        {
            get => PlayerPrefs.GetString(Pref + "pc", "");
            set
            {
                PlayerPrefs.SetString(Pref + "pc", value);
                PlayerPrefs.Save();
            }
        }
        // Existing installations and older network peers used armor only for seat two.
        internal static SoldierModel LegacyModel(int slot) => slot == 2 ? SoldierModel.Armored : SoldierModel.Uniform;

        internal static DemoChoices Load()
        {
            int role = Mathf.Clamp(PlayerPrefs.GetInt(Pref + "role", 0), 0, 3);
            int model = PlayerPrefs.GetInt(Pref + "model", (int)LegacyModel(role));
            return new DemoChoices
            {
                Role = role, Model = model == (int)SoldierModel.Armored ? SoldierModel.Armored : SoldierModel.Uniform,
                Pc = PlayerPrefs.GetString(Pref + "pcOverride", ""), Tablet = PlayerPrefs.GetString(Pref + "tablet", "")
            };
        }

        internal void Save()
        {
            PlayerPrefs.SetInt(Pref + "role", Role);
            PlayerPrefs.SetInt(Pref + "model", (int)Model);
            PlayerPrefs.SetString(Pref + "pcOverride", Pc);
            PlayerPrefs.SetString(Pref + "tablet", Tablet);
            PlayerPrefs.Save();
        }

        internal static bool TryAddress(string text, out string address)
        {
            address = "";
            string[] fields = text.Split('.');
            if (fields.Length != 4) return false;
            var numbers = new byte[4];
            for (int i = 0; i < fields.Length; i++)
                if (fields[i].Length > 3 || !byte.TryParse(fields[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
            address = string.Join(".", numbers);
            return numbers[0] > 0 && numbers[0] < 224;
        }
    }

    internal static class DemoUi
    {
        internal static readonly Color Panel = new Color32(14, 30, 42, 250);
        internal static readonly Color Row = new Color32(35, 54, 67, 255);
        internal static readonly Color Highlight = new Color32(33, 105, 108, 255);
        internal static readonly Color Muted = new Color32(176, 200, 208, 255);
        internal static readonly Color Accent = new Color32(113, 238, 213, 255);
        static Font font;

        internal static RectTransform Canvas(Transform parent, string name, bool world)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = world ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
            if (!world)
            {
                var scaler = go.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1120, 700);
                scaler.matchWidthOrHeight = 0.5f;
            }
            return rect;
        }

        static RectTransform Child(Transform parent, string name, float x, float y, float width, float height)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
            return rect;
        }

        internal static Image Block(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var image = Child(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        internal static Text Label(Transform parent, string name, float x, float y, float w, float h, int size,
            Color color, FontStyle style = FontStyle.Normal)
        {
            var text = Child(parent, name, x, y, w, h).gameObject.AddComponent<Text>();
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.raycastTarget = false;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            return text;
        }
    }

    internal sealed class QuestPanel
    {
        static readonly string[] Keys = { "1", "2", "3", "Delete", "4", "5", "6", "Clear", "7", "8", "9", "Auto", ".", "0", "Save", "Back" };
        readonly RectTransform menu, hud;
        readonly Text title, detail, hint, hudText;
        readonly Text[] lines = new Text[5], keys = new Text[16];
        readonly Image[] rows = new Image[5], cells = new Image[16];
        readonly Action<DemoChoices> apply;
        DemoChoices draft;
        int selected, editing, key;
        string buffer, error;
        Vector3 direction = Vector3.forward;
        internal bool Open => menu.gameObject.activeSelf;

        internal QuestPanel(Transform parent, Action<DemoChoices> apply, bool cableMission = false)
        {
            this.apply = apply;
            menu = DemoUi.Canvas(parent, "Headset settings", true);
            menu.sizeDelta = new Vector2(720, 575);
            menu.localScale = Vector3.one * 0.0015f;
            DemoUi.Block(menu, "Background", 0, 0, 720, 575, DemoUi.Panel);
            title = DemoUi.Label(menu, "Title", 28, 12, 664, 55, 38, Color.white, FontStyle.Bold);
            detail = DemoUi.Label(menu, "Current entry", 30, 71, 660, 46, 26, DemoUi.Muted);
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = DemoUi.Block(menu, "Choice", 26, 122 + 66 * i, 668, 59, DemoUi.Row);
                lines[i] = DemoUi.Label(rows[i].transform, "Label", 18, 0, 640, 59, 27, Color.white);
            }
            for (int i = 0; i < keys.Length; i++)
            {
                cells[i] = DemoUi.Block(menu, "Address key", 26 + i % 4 * 168, 133 + i / 4 * 71, 157, 59, DemoUi.Row);
                keys[i] = DemoUi.Label(cells[i].transform, "Label", 0, 0, 157, 59, 27, Color.white);
                keys[i].alignment = TextAnchor.MiddleCenter;
                keys[i].text = Keys[i];
            }
            hint = DemoUi.Label(menu, "Controls", 30, 457, 660, 89, 23, DemoUi.Accent);
            hud = DemoUi.Canvas(parent, "Tracking status", true);
            int hudHeight = cableMission ? 270 : 198;
            hud.sizeDelta = new Vector2(670, hudHeight);
            hud.localScale = Vector3.one * 0.0015f;
            DemoUi.Block(hud, "Background", 0, 0, 670, hudHeight, DemoUi.Panel);
            hudText = DemoUi.Label(hud, "Live status", 15, 4, 640, hudHeight - 8, 23, Color.white);
            menu.gameObject.SetActive(false);
        }

        internal void OpenSettings(DemoChoices choices, Transform head)
        {
            draft = choices;
            selected = editing = key = 0;
            error = "";
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude > 0.01f) direction = forward.normalized;
            menu.gameObject.SetActive(true);
            hud.gameObject.SetActive(false);
            Render();
        }

        internal void Close()
        {
            menu.gameObject.SetActive(false);
            hud.gameObject.SetActive(true);
        }

        internal void Place(Transform head, Quaternion body)
        {
            if (Open)
                menu.SetPositionAndRotation(head.position + direction * 1.45f, Quaternion.LookRotation(direction));
            else
            {
                Vector3 offset = body * new Vector3(-0.68f, 0.50f, 1.75f);
                hud.SetPositionAndRotation(head.position + offset, Quaternion.LookRotation(offset));
            }
        }

        internal void Status(string text) => hudText.text = text;

        internal void Handle(DemoControls input)
        {
            Vector2Int move = input.Navigate;
            if (input.Back)
            {
                if (editing == 0) Close();
                else { editing = 0; error = ""; Render(); }
                return;
            }
            if (move != Vector2Int.zero)
            {
                if (editing != 0) key = ((key / 4 - move.y + 4) % 4) * 4 + (key % 4 + move.x + 4) % 4;
                else if (move.y != 0) selected = (selected - move.y + rows.Length) % rows.Length;
                else if (selected == 0) draft.Role = (draft.Role + move.x + 4) % 4;
                else if (selected == 3) ToggleModel();
                Render();
            }
            if (!input.Confirm) return;
            if (editing != 0) PressKey();
            else if (selected == 0) draft.Role = (draft.Role + 1) % 4;
            else if (selected == 3) ToggleModel();
            else if (selected == 4) { apply(draft); Close(); return; }
            else
            {
                editing = selected;
                buffer = editing == 1 ? draft.Pc : draft.Tablet;
                key = 0;
                error = "";
            }
            Render();
        }

        void ToggleModel() => draft.Model = draft.Model == SoldierModel.Uniform ? SoldierModel.Armored : SoldierModel.Uniform;

        void PressKey()
        {
            error = "";
            string pressed = Keys[key];
            switch (pressed)
            {
                case "Delete": if (buffer.Length > 0) buffer = buffer.Substring(0, buffer.Length - 1); break;
                case "Clear": buffer = ""; break;
                case "Auto":
                    if (editing == 1) draft.Pc = "";
                    else draft.Tablet = "";
                    editing = 0;
                    break;
                case "Back": editing = 0; break;
                case "Save":
                    string address = "";
                    if (buffer.Length != 0 && !DemoChoices.TryAddress(buffer, out address))
                    {
                        error = "Enter four IPv4 numbers, each between 0 and 255.";
                        return;
                    }
                    if (editing == 1) draft.Pc = address;
                    else draft.Tablet = address;
                    editing = 0;
                    break;
                default: if (buffer.Length < 15) buffer += pressed; break;
            }
        }

        void Render()
        {
            bool keypad = editing != 0;
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].gameObject.SetActive(!keypad);
                rows[i].color = i == selected ? DemoUi.Highlight : DemoUi.Row;
            }
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i].gameObject.SetActive(keypad);
                cells[i].color = i == key ? DemoUi.Highlight : DemoUi.Row;
            }
            if (keypad)
            {
                title.text = editing == 1 ? "Tracking PC address" : "Tablet address";
                detail.text = string.IsNullOrEmpty(buffer) ? "Find on Wi-Fi or enter an IPv4 address" : buffer;
                hint.text = error.Length > 0 ? error : "Left stick moves between keys. A presses a key.\nB cancels the address change.";
            }
            else
            {
                title.text = "Seated body tracking";
                detail.text = "Sit facing ahead with both feet resting on the floor.";
                lines[0].text = "Mode      " + draft.Label;
                lines[1].text = "Tracking PC      " + (draft.Pc.Length == 0 ? "Find on Wi-Fi" : draft.Pc);
                lines[2].text = "Tablet      " + (draft.Tablet.Length == 0 ? "Find on Wi-Fi" : draft.Tablet);
                lines[3].text = "Soldier      " + draft.Model;
                lines[4].text = draft.Solo ? "Start solo test" : "Enter multiplayer room";
                hint.text = "Left stick selects. A selects or opens. B closes.\nA sits or stands. X calibrates. Side grips climb.";
            }
        }
    }
}
