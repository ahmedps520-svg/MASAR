using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Masar.Game.UI
{
    /// <summary>
    /// The launch intro: the MASAR logo, wordmark and kerb stripe animate in over black, then fade
    /// to reveal the main menu. Plays once per launch; any tap, click, key or gamepad button skips it.
    /// </summary>
    public sealed class IntroSequence : MonoBehaviour
    {
        const float Hold = 3.4f, End = 4.1f;

        /// <summary>True once the intro has played (or been skipped) in this launch.</summary>
        public static bool Played { get; private set; }
        public static bool Playing { get; private set; }

        private Action _done;
        private float _t;
        private Image _black;
        private CanvasGroup _content;
        private RectTransform _logo, _word, _stripeMask;
        private CanvasGroup _wordGroup, _tagGroup;

        public static void Play(Texture2D logo, Action done)
        {
            Played = true;
            Playing = true;
            var canvas = UIKit.CreateCanvas("Intro", 200);
            var intro = canvas.gameObject.AddComponent<IntroSequence>();
            intro._done = done;
            intro.Build(canvas.transform, logo);
        }

        private void Build(Transform root, Texture2D logo)
        {
            gameObject.AddComponent<UIKit.ModalToken>();   // hides the perf overlay while it plays
            _black = UIKit.Panel(root, "Black", Color.black);
            UIKit.Anchor(_black.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var content = UIKit.Rect(root, "Content");
            UIKit.Anchor(content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _content = content.gameObject.AddComponent<CanvasGroup>();

            if (logo != null)
            {
                var img = UIKit.Rect(content, "Logo").gameObject.AddComponent<RawImage>();
                img.texture = logo;
                img.raycastTarget = false;
                _logo = img.rectTransform;
                UIKit.Place(_logo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(380f, 380f));
            }

            var word = UIKit.Label(content, "M A S A R", 108, UIKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            _word = word.rectTransform;
            UIKit.Place(_word, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(1200f, 130f));
            _wordGroup = word.gameObject.AddComponent<CanvasGroup>();

            // Kerb stripe that draws itself in under the wordmark
            _stripeMask = UIKit.Rect(content, "StripeMask");
            _stripeMask.gameObject.AddComponent<RectMask2D>();
            UIKit.Place(_stripeMask, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -248f), new Vector2(0f, 12f));
            var stripe = UIKit.KerbStripe(_stripeMask, 10, 44f, 12f);
            UIKit.Place(stripe, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440f, 12f));

            var tag = UIKit.Label(content, "JEDDAH  DRIVING  SIMULATOR", 28, UIKit.TextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Place(tag.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -300f), new Vector2(1200f, 44f));
            _tagGroup = tag.gameObject.AddComponent<CanvasGroup>();
            Apply();
        }

        private void Update()
        {
            if (_t < Hold && SkipPressed()) _t = Hold;
            _t += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            Apply();
            if (_t >= End)
            {
                Playing = false;
                Destroy(gameObject);
                _done?.Invoke();
            }
        }

        private static bool SkipPressed()
        {
            return (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
                || (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                || (Gamepad.current != null && (Gamepad.current.buttonSouth.wasPressedThisFrame || Gamepad.current.startButton.wasPressedThisFrame));
        }

        private void Apply()
        {
            float logoIn = Phase(0.35f, 1.15f);
            if (_logo != null)
            {
                _logo.localScale = Vector3.one * (0.86f + 0.14f * EaseOutBack(logoIn));
                _logo.GetComponent<RawImage>().color = new Color(1f, 1f, 1f, logoIn);
            }
            float wordIn = Phase(1.0f, 1.6f);
            _wordGroup.alpha = wordIn;
            _word.anchoredPosition = new Vector2(0f, -170f - 24f * (1f - EaseOut(wordIn)));
            _stripeMask.sizeDelta = new Vector2(440f * EaseOut(Phase(1.3f, 1.9f)), 12f);
            _tagGroup.alpha = Phase(1.6f, 2.2f);

            float outro = Phase(Hold, End);
            _content.alpha = 1f - EaseOut(Mathf.Clamp01(outro * 1.6f));
            _black.color = new Color(0f, 0f, 0f, 1f - EaseOut(outro));
        }

        private float Phase(float a, float b) => Mathf.Clamp01((_t - a) / (b - a));
        private static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);
        private static float EaseOutBack(float x)
        {
            const float c1 = 1.4f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }
    }
}
