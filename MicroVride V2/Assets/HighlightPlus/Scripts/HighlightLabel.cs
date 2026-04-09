using System;
using TMPro;
using UnityEngine;

namespace HighlightPlus {

    [ExecuteAlways]
    public partial class HighlightLabel : MonoBehaviour {

        public Camera cam;

        [NonSerialized]
        public Transform target;
        [NonSerialized]
        public Vector3 localPosition;
        [NonSerialized]
        public Vector3 worldOffset;

        [NonSerialized]
        public bool isVisible;

        [NonSerialized]
        public LabelAlignment labelAlignment = LabelAlignment.Auto;

        public GameObject labelPrefab;

        internal bool isPooled;

        TextMeshProUGUI text;
        RectTransform panel;
        CanvasGroup canvasGroup;

        public virtual float alpha {
            get {
                return canvasGroup?.alpha ?? 1f;
            }
            set {
                if (canvasGroup != null) {
                    canvasGroup.alpha = value;
                }
            }
        }

        public virtual Color textColor {
            get {
                return text?.color ?? Color.white;
            }
            set {
                if (text != null) {
                    text.color = value;
                }
            }
        }

        public virtual string textLabel {
            get {
                return text?.text ?? "";
            }
            set {
                if (text != null) {
                    text.text = value;
                }
            }
        }

        public virtual float textSize {
            get {
                return text?.fontSize ?? 14;
            }
            set {
                if (text != null) {
                    text.fontSize = value;
                }
            }
        }
        

        public virtual float width {
            get {
                return text?.rectTransform.sizeDelta.x ?? 200;
            } 
            set {
                if (text == null) return;
                if (text.rectTransform != null) {
                    Vector2 currentSize = text.rectTransform.sizeDelta;
                    if (currentSize.x != value) {
                        text.rectTransform.sizeDelta = new Vector2(value, currentSize.y);
                    }
                }
                RectTransform panelRectTransform = panel.GetComponent<RectTransform>();
                if (panelRectTransform != null) {
                    Vector2 currentSize = panelRectTransform.sizeDelta;
                    if (currentSize.x != value) {
                        panelRectTransform.sizeDelta = new Vector2(value, currentSize.y);
                    }
                }
            }
        }

        void Awake () {
            text = GetComponentInChildren<TextMeshProUGUI>();
            panel = transform.GetChild(0).GetComponentInChildren<RectTransform>();
            canvasGroup = GetComponent<CanvasGroup>();
        }

        /// <summary>
        /// Return the label to the pool
        /// </summary>
        public virtual void ReturnToPool() {
            if (!isPooled) return;
            gameObject.SetActive(false);
            HighlightLabelPoolManager.ReturnToPool(this);
        }


        public virtual void SetPosition(Transform target, Vector3 localPosition, Vector3 worldOffset) {
            this.target = target;
            this.localPosition = localPosition;
            this.worldOffset = worldOffset;
        }

        public virtual void UpdatePosition () {
            if (panel == null || text == null) return;
            if (cam == null) {
                cam = Camera.main;
                if (cam == null) return;
            }
            if (target == null) return;

            Vector3 worldPosition = target.TransformPoint(localPosition);
            panel.position = cam.WorldToScreenPoint(worldPosition + worldOffset);

            if (labelAlignment == LabelAlignment.Left || (labelAlignment == LabelAlignment.Auto && panel.position.x + width * 2f > cam.pixelWidth * 0.95f)) {
                panel.pivot = Vector2.right;
                text.alignment = TextAlignmentOptions.BottomLeft;
            }
            else {
                panel.pivot = Vector2.zero;
                text.alignment = TextAlignmentOptions.BottomRight;
            }
        }

        /// <summary>
        /// Show the label
        /// </summary>
        public virtual void Show () {
            isVisible = true;
#if UNITY_EDITOR
            if (!Application.isPlaying) {
                HighlightLabelPoolManager.Refresh();
            }
#endif
        }

        /// <summary>
        /// Hide the label
        /// </summary>
        public virtual void Hide () {
            if (this == null) return;
            gameObject.SetActive(false);
            isVisible = false;
        }

        /// <summary>
        /// Hide & destroy the label
        /// </summary>
        public virtual void Release () {
            if (this == null) return;
            Hide();
            if (isPooled) {
                ReturnToPool();
            } 
        }
    }
}