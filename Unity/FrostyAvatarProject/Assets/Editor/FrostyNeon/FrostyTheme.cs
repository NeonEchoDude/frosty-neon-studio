// Frosty Neon look for the toolkit's windows (2026-11-15, the owner asked for fun, VRChat-flavoured interfaces).
// Dark night-club background, neon cyan / pink / violet accents, step cards and status badges. Original styling
// only: no VRChat logos or artwork. Only characters the Unity editor font is sure to have (no emoji).

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FrostyNeon.Editor
{
    public static class FrostyTheme
    {
        public static readonly Color Night = Hex("0D0F1E");
        public static readonly Color Card = Hex("181B35");
        public static readonly Color CardDone = Hex("15283A");
        public static readonly Color Line = Hex("2A2F5A");
        public static readonly Color Cyan = Hex("3EF2FF");
        public static readonly Color Pink = Hex("FF4FD8");
        public static readonly Color Violet = Hex("8A5CFF");
        public static readonly Color Mint = Hex("4CFFA0");
        public static readonly Color Amber = Hex("FFC94A");
        public static readonly Color Hot = Hex("FF5A7A");
        public static readonly Color Text = Hex("E8ECFF");
        public static readonly Color Dim = Hex("9AA3D6");

        static readonly Dictionary<Color, Texture2D> Textures = new Dictionary<Color, Texture2D>();
        static GUIStyle _title, _subtitle, _cardTitle, _body, _muted, _button, _buttonAlt, _badge, _choice, _choiceOn, _card;

        public static Texture2D Tex(Color color)
        {
            if (Textures.TryGetValue(color, out Texture2D texture) && texture != null) return texture;
            texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return Textures[color] = texture;
        }

        static GUIStyle Label(int size, Color color, FontStyle font = FontStyle.Normal, bool wrap = true) =>
            new GUIStyle(EditorStyles.label)
            {
                fontSize = size, fontStyle = font, wordWrap = wrap, richText = true,
                normal = { textColor = color }, hover = { textColor = color }, active = { textColor = color }, focused = { textColor = color },
            };

        // Unity 2022's own skin draws its button image from scaledBackgrounds, over a plain background colour; both are
        // set (seen in the first screenshots on 2026-11-15, where the buttons came out grey).
        static GUIStyleState State(Color fill, Color text) =>
            new GUIStyleState { background = Tex(fill), scaledBackgrounds = new[] { Tex(fill) }, textColor = text };

        static GUIStyle MakeButton(Color fill, Color hover, Color text) => new GUIStyle(GUI.skin.button)
        {
            fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, fixedHeight = 32,
            margin = new RectOffset(4, 4, 4, 4), padding = new RectOffset(10, 10, 4, 4), border = new RectOffset(0, 0, 0, 0),
            normal = State(fill, text), hover = State(hover, text), active = State(Color.white, Night), focused = State(fill, text),
            onNormal = State(fill, text), onHover = State(hover, text), onActive = State(Color.white, Night), onFocused = State(fill, text),
        };

        public static GUIStyle Title => _title = _title ?? Label(22, Cyan, FontStyle.Bold, false);
        public static GUIStyle Subtitle => _subtitle = _subtitle ?? Label(12, Pink);
        public static GUIStyle CardTitle => _cardTitle = _cardTitle ?? Label(15, Text, FontStyle.Bold);
        public static GUIStyle Body => _body = _body ?? Label(12, Text);
        public static GUIStyle Muted => _muted = _muted ?? Label(11, Dim);
        public static GUIStyle Button => _button = _button ?? MakeButton(Pink, Hex("FF7BE3"), Night);
        public static GUIStyle ButtonAlt => _buttonAlt = _buttonAlt ?? MakeButton(Cyan, Hex("8CF8FF"), Night);
        public static GUIStyle Choice => _choice = _choice ?? new GUIStyle(MakeButton(Line, Hex("353B70"), Text))
        { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Normal, fontSize = 12, fixedHeight = 26 };
        public static GUIStyle ChoiceOn => _choiceOn = _choiceOn ?? new GUIStyle(MakeButton(Violet, Violet, Text))
        { alignment = TextAnchor.MiddleLeft, fontSize = 12, fixedHeight = 26 };
        public static GUIStyle Badge => _badge = _badge ?? new GUIStyle(Label(11, Night, FontStyle.Bold, false))
        { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(8, 8, 2, 2), margin = new RectOffset(2, 6, 3, 3) };
        static GUIStyle CardStyle => _card = _card ?? new GUIStyle { padding = new RectOffset(16, 12, 10, 12), margin = new RectOffset(10, 10, 6, 6) };

        /// <summary>Paints the whole window in the night colour. Call first in OnGUI.</summary>
        public static void Background(EditorWindow window) =>
            EditorGUI.DrawRect(new Rect(0, 0, window.position.width, window.position.height), Night);

        /// <summary>The neon banner: three stripes, the title, a subtitle.</summary>
        public static void Header(string title, string subtitle)
        {
            Rect area = GUILayoutUtility.GetRect(10, 74, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(area, Hex("12142A"));
            float third = area.width / 3f;
            EditorGUI.DrawRect(new Rect(area.x, area.yMax - 4, third, 4), Cyan);
            EditorGUI.DrawRect(new Rect(area.x + third, area.yMax - 4, third, 4), Violet);
            EditorGUI.DrawRect(new Rect(area.x + 2 * third, area.yMax - 4, third + 1, 4), Pink);
            for (int i = 0; i < 18; i++)                         // a little "frost" sparkle along the top
            {
                float x = area.x + (i * 73 % 997) / 997f * area.width, y = area.y + 6 + (i * 37 % 19);
                EditorGUI.DrawRect(new Rect(x, y, 2, 2), i % 3 == 0 ? Cyan : i % 3 == 1 ? Pink : Violet);
            }
            // Narrow windows get smaller text, so the title is never cut off.
            int size = area.width < 360 ? 15 : area.width < 470 ? 18 : 22;
            var title1 = new GUIStyle(Title) { fontSize = size, clipping = TextClipping.Clip };
            var subtitle1 = new GUIStyle(Subtitle) { fontSize = area.width < 470 ? 11 : 12, wordWrap = false, clipping = TextClipping.Clip };
            GUI.Label(new Rect(area.x + 14, area.y + 12, area.width - 20, 30), title, title1);
            GUI.Label(new Rect(area.x + 15, area.y + 44, area.width - 20, 20), subtitle, subtitle1);
        }

        /// <summary>Starts a step card: numbered disc, title, and a coloured edge (pink while active, mint when done).</summary>
        public static void BeginStep(int number, string title, bool done)
        {
            Rect card = EditorGUILayout.BeginVertical(CardStyle);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(card, done ? CardDone : Card);
                EditorGUI.DrawRect(new Rect(card.x, card.y, 4, card.height), done ? Mint : Pink);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect disc = GUILayoutUtility.GetRect(26, 26, GUILayout.Width(26), GUILayout.Height(26));
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(disc, done ? Mint : Cyan);
                    GUI.Label(disc, number.ToString(), new GUIStyle(Badge) { fontSize = 13, margin = new RectOffset() });
                }
                GUILayout.Space(6);
                GUILayout.Label(title, CardTitle, GUILayout.Height(26));
                GUILayout.FlexibleSpace();
                if (done) Pill("DONE", Mint);
            }
            GUILayout.Space(4);
        }

        public static void EndStep() => EditorGUILayout.EndVertical();

        /// <summary>A small coloured badge with dark text.</summary>
        public static void Pill(string text, Color color)
        {
            var style = new GUIStyle(Badge) { normal = { background = Tex(color), textColor = Night } };
            GUILayout.Label(text, style, GUILayout.ExpandWidth(false));
        }

        /// <summary>Colour for a preflight status.</summary>
        public static Color StatusColor(string status) => status == "pass" ? Mint : status == "warn" ? Amber : Hot;

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            return color;
        }
    }
}
