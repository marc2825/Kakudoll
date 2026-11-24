using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace VLat.Editor.Authoring
{
    /// <summary>
    /// 角度の軸。キー（ひし形）、補正の効く範囲（帯）、編集ビューの角度（白い線）、比較ビューの角度（A・B）を
    /// 1 本の軸に描き、押す・引くの入力をcallbackで返します。DocumentとPreviewは変更しません。
    /// </summary>
    internal sealed class VlatAngleRail : VisualElement
    {
        internal const string UssClassName = "vlat-angle-rail";
        private const float SidePadding = 18f;
        // 上から: A・Bの文字(0〜13) / 編集ビューの印(14〜) / 軸(32) / 目盛りの数字(48〜)。
        private const float PlayheadTop = 14f;
        private const float TrackY = 32f;
        private const float KeyRadius = 7f;
        private const float HitRadius = 10f;
        private const float DragThreshold = 4f;
        private const float SnapPixels = 6f;

        private static readonly CustomStyleProperty<Color> TrackProperty = new CustomStyleProperty<Color>("--rail-track-color");
        private static readonly CustomStyleProperty<Color> TickProperty = new CustomStyleProperty<Color>("--rail-tick-color");
        private static readonly CustomStyleProperty<Color> KeyProperty = new CustomStyleProperty<Color>("--rail-key-color");
        private static readonly CustomStyleProperty<Color> KeyHollowProperty = new CustomStyleProperty<Color>("--rail-key-hollow-color");
        private static readonly CustomStyleProperty<Color> BandProperty = new CustomStyleProperty<Color>("--rail-band-color");
        private static readonly CustomStyleProperty<Color> PlayheadProperty = new CustomStyleProperty<Color>("--rail-playhead-color");
        private static readonly CustomStyleProperty<Color> CompareProperty = new CustomStyleProperty<Color>("--rail-compare-color");

        private readonly List<double> _keys = new List<double>();
        private readonly List<bool> _keyShapes = new List<bool>();
        private readonly List<Label> _tickLabels = new List<Label>();
        private readonly Label _markerA;
        private readonly Label _markerB;
        private readonly Label _dragLabel;
        private Color _track = new Color(0.45f, 0.45f, 0.45f);
        private Color _tick = new Color(0.55f, 0.55f, 0.55f);
        private Color _key = new Color(1f, 0.54f, 0.24f);
        private Color _keyHollow = new Color(0.17f, 0.17f, 0.17f);
        private Color _band = new Color(1f, 0.54f, 0.24f, 0.22f);
        private Color _playhead = Color.white;
        private Color _compare = new Color(0.55f, 0.72f, 0.92f);
        private double _min = -180;
        private double _max = 180;
        private bool _hasRange;
        private double _rangeStart;
        private double _rangeEnd;
        private int _selectedKey = -1;
        private float _mainYaw;
        private float _compareAYaw = 45f;
        private float _compareBYaw = 90f;
        private bool _showCompare;
        private bool _keysEditable;
        private int _pointerId = -1;
        private Gesture _gesture;
        private int _gestureKey = -1;
        private Vector2 _gestureStart;
        private double _gestureKeyYaw;
        private bool _gestureMoved;

        internal VlatAngleRail()
        {
            AddToClassList(UssClassName);
            focusable = true;
            _markerA = CreateMarker("A");
            _markerB = CreateMarker("B");
            _dragLabel = new Label { name = "angle-rail-drag-label", pickingMode = PickingMode.Ignore };
            _dragLabel.AddToClassList("vlat-rail-drag-label");
            _dragLabel.style.display = DisplayStyle.None;
            Add(_dragLabel);
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            RegisterCallback<GeometryChangedEvent>(_ => LayoutChildren());
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => EndGesture(false));
            RebuildTickLabels();
        }

        private enum Gesture
        {
            None,
            Scrub,
            Key,
            CompareA,
            CompareB,
        }

        /// <summary>編集ビューの角度を引いて変えたとき（押している間、何度も呼ばれる）。</summary>
        internal Action<float> MainYawChanged { get; set; }

        /// <summary>引き終えたとき。引き終えた角度がキーに重なっていれば、そのキーの番号（なければ -1）。</summary>
        internal Action<int> ScrubEnded { get; set; }

        /// <summary>ひし形を押したとき。</summary>
        internal Action<int> KeyClicked { get; set; }

        /// <summary>ひし形を横に引いて離したとき（キーの番号、新しい角度）。</summary>
        internal Action<int, double> KeyMoved { get; set; }

        /// <summary>比較ビューの印（A・B）を引いたとき。</summary>
        internal Action<VlatPreviewView, float> CompareYawChanged { get; set; }

        internal double DisplayMin => _min;
        internal double DisplayMax => _max;
        internal int SelectedKey => _selectedKey;
        internal float MainYaw => _mainYaw;
        internal bool IsInteracting => _gesture != Gesture.None;

        /// <summary>表示する内容をまとめて設定します。操作の途中は、引いている印の位置を保ちます。</summary>
        internal void SetState(
            IReadOnlyList<double> keys,
            IReadOnlyList<bool> keyShapes,
            int selectedKey,
            bool hasRange,
            double rangeStart,
            double rangeEnd,
            float mainYaw,
            float compareAYaw,
            float compareBYaw,
            bool showCompare,
            bool keysEditable)
        {
            _keys.Clear();
            _keyShapes.Clear();
            if (keys != null)
            {
                for (var index = 0; index < keys.Count; index++)
                {
                    _keys.Add(keys[index]);
                    _keyShapes.Add(keyShapes != null && index < keyShapes.Count && keyShapes[index]);
                }
            }

            _selectedKey = selectedKey >= 0 && selectedKey < _keys.Count ? selectedKey : -1;
            _hasRange = hasRange && rangeEnd > rangeStart;
            _rangeStart = rangeStart;
            _rangeEnd = rangeEnd;
            if (_gesture != Gesture.Scrub) _mainYaw = mainYaw;
            if (_gesture != Gesture.CompareA) _compareAYaw = compareAYaw;
            if (_gesture != Gesture.CompareB) _compareBYaw = compareBYaw;
            _showCompare = showCompare;
            _keysEditable = keysEditable;
            var min = -180d;
            var max = 180d;
            if (_hasRange)
            {
                min = Math.Max(-180d, Math.Min(_rangeStart, -90d) - 15d);
                max = Math.Min(180d, Math.Max(_rangeEnd, 90d) + 15d);
            }

            if (min != _min || max != _max)
            {
                _min = min;
                _max = max;
                RebuildTickLabels();
            }

            LayoutChildren();
            MarkDirtyRepaint();
        }

        /// <summary>角度をレール上の横位置（px）にします。</summary>
        internal float XForYaw(double yaw)
        {
            var width = Math.Max(1f, contentRect.width - (SidePadding * 2f));
            var t = (yaw - _min) / (_max - _min);
            return SidePadding + (float)(Math.Max(0d, Math.Min(1d, t)) * width);
        }

        /// <summary>レール上の横位置（px）を角度にします。</summary>
        internal double YawForX(float x)
        {
            var width = Math.Max(1f, contentRect.width - (SidePadding * 2f));
            var t = Math.Max(0d, Math.Min(1d, (x - SidePadding) / width));
            return _min + (t * (_max - _min));
        }

        private Label CreateMarker(string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("vlat-rail-marker");
            Add(label);
            return label;
        }

        private void RebuildTickLabels()
        {
            foreach (var label in _tickLabels) label.RemoveFromHierarchy();
            _tickLabels.Clear();
            var first = (int)Math.Ceiling(_min / 45d) * 45;
            for (var yaw = first; yaw <= _max + 0.001; yaw += 45)
            {
                var label = new Label(FormatYaw(yaw)) { pickingMode = PickingMode.Ignore, userData = (double)yaw };
                label.AddToClassList("vlat-rail-tick-label");
                Add(label);
                _tickLabels.Add(label);
            }

            LayoutChildren();
        }

        private void LayoutChildren()
        {
            if (float.IsNaN(contentRect.width) || contentRect.width <= 0f) return;
            foreach (var label in _tickLabels)
            {
                label.style.left = XForYaw((double)label.userData) - 24f;
            }

            PlaceMarker(_markerA, _compareAYaw);
            PlaceMarker(_markerB, _compareBYaw);
        }

        private void PlaceMarker(Label marker, float yaw)
        {
            marker.style.display = _showCompare ? DisplayStyle.Flex : DisplayStyle.None;
            marker.style.left = XForYaw(yaw) - 7f;
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            var style = evt.customStyle;
            if (style.TryGetValue(TrackProperty, out var track)) _track = track;
            if (style.TryGetValue(TickProperty, out var tick)) _tick = tick;
            if (style.TryGetValue(KeyProperty, out var key)) _key = key;
            if (style.TryGetValue(KeyHollowProperty, out var hollow)) _keyHollow = hollow;
            if (style.TryGetValue(BandProperty, out var band)) _band = band;
            if (style.TryGetValue(PlayheadProperty, out var playhead)) _playhead = playhead;
            if (style.TryGetValue(CompareProperty, out var compare)) _compare = compare;
            MarkDirtyRepaint();
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            var rect = contentRect;
            if (rect.width < (SidePadding * 2f) + 20f) return;
            var painter = context.painter2D;
            var left = XForYaw(_min);
            var right = XForYaw(_max);

            if (_hasRange)
            {
                var x0 = XForYaw(_rangeStart);
                var x1 = XForYaw(_rangeEnd);
                painter.fillColor = _band;
                FillRect(painter, x0, TrackY - 8f, x1 - x0, 16f);
                painter.strokeColor = WithAlpha(_key, 0.9f);
                painter.lineWidth = 2f;
                Line(painter, x0, TrackY - 8f, x0, TrackY + 8f);
                Line(painter, x1, TrackY - 8f, x1, TrackY + 8f);
            }

            painter.strokeColor = _track;
            painter.lineWidth = 2f;
            Line(painter, left, TrackY, right, TrackY);

            var firstTick = (int)Math.Ceiling(_min / 15d) * 15;
            painter.lineWidth = 1f;
            for (var yaw = firstTick; yaw <= _max + 0.001; yaw += 15)
            {
                var major = yaw % 45 == 0;
                var x = XForYaw(yaw);
                painter.strokeColor = major ? _tick : WithAlpha(_tick, 0.6f);
                Line(painter, x, TrackY + 4f, x, TrackY + (major ? 12f : 8f));
            }

            if (_showCompare)
            {
                painter.strokeColor = _compare;
                painter.lineWidth = 1.5f;
                var ax = XForYaw(_compareAYaw);
                var bx = XForYaw(_compareBYaw);
                Line(painter, ax, PlayheadTop, ax, TrackY - 2f);
                Line(painter, bx, PlayheadTop, bx, TrackY - 2f);
            }

            for (var index = 0; index < _keys.Count; index++)
            {
                var yaw = _gesture == Gesture.Key && _gestureMoved && index == _gestureKey ? _gestureKeyYaw : _keys[index];
                var x = XForYaw(yaw);
                var selected = index == _selectedKey;
                var radius = selected ? KeyRadius + 1f : KeyRadius;
                Diamond(painter, x, TrackY, radius);
                painter.fillColor = _keyShapes[index] ? _key : _keyHollow;
                painter.Fill();
                painter.strokeColor = _key;
                painter.lineWidth = 2f;
                Diamond(painter, x, TrackY, radius);
                painter.Stroke();
                if (selected)
                {
                    painter.strokeColor = _playhead;
                    painter.lineWidth = 1.5f;
                    Diamond(painter, x, TrackY, radius + 4f);
                    painter.Stroke();
                }
            }

            var main = XForYaw(_mainYaw);
            painter.strokeColor = _playhead;
            painter.lineWidth = 2f;
            Line(painter, main, PlayheadTop + 6f, main, TrackY + 14f);
            painter.fillColor = _playhead;
            painter.BeginPath();
            painter.MoveTo(new Vector2(main - 5f, PlayheadTop));
            painter.LineTo(new Vector2(main + 5f, PlayheadTop));
            painter.LineTo(new Vector2(main, PlayheadTop + 7f));
            painter.ClosePath();
            painter.Fill();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || _gesture != Gesture.None) return;
            Focus();
            var position = (Vector2)evt.localPosition;
            _gestureStart = position;
            _gestureMoved = false;
            var key = HitKey(position);
            if (key >= 0)
            {
                _gesture = Gesture.Key;
                _gestureKey = key;
                _gestureKeyYaw = _keys[key];
                KeyClicked?.Invoke(key);
            }
            else if (_showCompare && HitCompare(position, _compareAYaw))
            {
                _gesture = Gesture.CompareA;
            }
            else if (_showCompare && HitCompare(position, _compareBYaw))
            {
                _gesture = Gesture.CompareB;
            }
            else
            {
                _gesture = Gesture.Scrub;
                Scrub(position.x);
            }

            _pointerId = evt.pointerId;
            this.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_gesture == Gesture.None || evt.pointerId != _pointerId) return;
            var position = (Vector2)evt.localPosition;
            switch (_gesture)
            {
                case Gesture.Scrub:
                    Scrub(position.x);
                    break;
                case Gesture.Key:
                    if (!_keysEditable) break;
                    if (!_gestureMoved && Mathf.Abs(position.x - _gestureStart.x) < DragThreshold) break;
                    _gestureMoved = true;
                    var yaw = Math.Round(YawForX(position.x));
                    if (_hasRange) yaw = Math.Max(_rangeStart + 1d, Math.Min(_rangeEnd - 1d, yaw));
                    _gestureKeyYaw = yaw;
                    _dragLabel.text = FormatYaw(yaw);
                    _dragLabel.style.left = XForYaw(yaw) - 24f;
                    _dragLabel.style.display = DisplayStyle.Flex;
                    MarkDirtyRepaint();
                    break;
                case Gesture.CompareA:
                    _compareAYaw = (float)Math.Round(YawForX(position.x));
                    PlaceMarker(_markerA, _compareAYaw);
                    MarkDirtyRepaint();
                    CompareYawChanged?.Invoke(VlatPreviewView.SubA, _compareAYaw);
                    break;
                case Gesture.CompareB:
                    _compareBYaw = (float)Math.Round(YawForX(position.x));
                    PlaceMarker(_markerB, _compareBYaw);
                    MarkDirtyRepaint();
                    CompareYawChanged?.Invoke(VlatPreviewView.SubB, _compareBYaw);
                    break;
            }

            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (_gesture == Gesture.None || evt.pointerId != _pointerId || evt.button != 0) return;
            EndGesture(true);
            evt.StopPropagation();
        }

        private void EndGesture(bool completed)
        {
            if (_gesture == Gesture.None) return;
            var gesture = _gesture;
            var key = _gestureKey;
            var moved = _gestureMoved;
            var yaw = _gestureKeyYaw;
            _gesture = Gesture.None;
            _gestureKey = -1;
            _gestureMoved = false;
            _dragLabel.style.display = DisplayStyle.None;
            if (_pointerId >= 0 && this.HasPointerCapture(_pointerId)) this.ReleasePointer(_pointerId);
            _pointerId = -1;
            MarkDirtyRepaint();
            if (!completed) return;
            if (gesture == Gesture.Key && moved && Math.Abs(yaw - _keys[key]) > 0.0001) KeyMoved?.Invoke(key, yaw);
            if (gesture == Gesture.Scrub) ScrubEnded?.Invoke(KeyAtYaw(_mainYaw));
        }

        private void Scrub(float x)
        {
            var yaw = YawForX(x);
            var snapped = -1;
            for (var index = 0; index < _keys.Count; index++)
            {
                if (Mathf.Abs(XForYaw(_keys[index]) - x) <= SnapPixels)
                {
                    snapped = index;
                    break;
                }
            }

            _mainYaw = snapped >= 0 ? (float)_keys[snapped] : (float)Math.Round(yaw);
            MarkDirtyRepaint();
            MainYawChanged?.Invoke(_mainYaw);
        }

        private int KeyAtYaw(float yaw)
        {
            for (var index = 0; index < _keys.Count; index++)
            {
                if (Math.Abs(_keys[index] - yaw) < 0.05) return index;
            }

            return -1;
        }

        private int HitKey(Vector2 position)
        {
            if (Mathf.Abs(position.y - TrackY) > HitRadius + 2f) return -1;
            var best = -1;
            var bestDistance = HitRadius;
            for (var index = 0; index < _keys.Count; index++)
            {
                var distance = Mathf.Abs(XForYaw(_keys[index]) - position.x);
                if (distance <= bestDistance)
                {
                    best = index;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private bool HitCompare(Vector2 position, float yaw)
        {
            return position.y < PlayheadTop && Mathf.Abs(XForYaw(yaw) - position.x) <= 8f;
        }

        private static string FormatYaw(double yaw)
        {
            var text = yaw.ToString("0.#", CultureInfo.InvariantCulture);
            return (yaw < 0 ? "−" + text.Substring(1) : text) + "°";
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a *= alpha;
            return color;
        }

        private static void Line(Painter2D painter, float x0, float y0, float x1, float y1)
        {
            painter.BeginPath();
            painter.MoveTo(new Vector2(x0, y0));
            painter.LineTo(new Vector2(x1, y1));
            painter.Stroke();
        }

        private static void FillRect(Painter2D painter, float x, float y, float width, float height)
        {
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, y));
            painter.LineTo(new Vector2(x + width, y));
            painter.LineTo(new Vector2(x + width, y + height));
            painter.LineTo(new Vector2(x, y + height));
            painter.ClosePath();
            painter.Fill();
        }

        private static void Diamond(Painter2D painter, float x, float y, float radius)
        {
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, y - radius));
            painter.LineTo(new Vector2(x + radius, y));
            painter.LineTo(new Vector2(x, y + radius));
            painter.LineTo(new Vector2(x - radius, y));
            painter.ClosePath();
        }
    }
}
