using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace NW.FairyToon.Editor
{
    // Equivalent to lilToon's gamma HDR picker: retain Color upload conversion and allow values above 1.
    // Native [HDR] changes the shader input interpretation, so the drawer carries no HDR property flag.
    public sealed class FairyToonHDRDrawer : MaterialPropertyDrawer
    {
        public override void OnGUI(Rect position, MaterialProperty prop, string label, MaterialEditor editor)
        {
            bool mixed = EditorGUI.showMixedValue;
            try
            {
                EditorGUI.showMixedValue = prop.hasMixedValue;
                Color value = prop.colorValue;
                if (Draw(position, FairyToonL10n.Text(label), ref value)) prop.colorValue = value;
            }
            finally { EditorGUI.showMixedValue = mixed; }
        }

        // Keeps both the swatch and the hex inside the real row. A leftover width is split instead of zeroing either one.
        private static void PlaceHdr(float x, float y, float h, float limit, float swatch, float hexWidth, float gap, out Rect field, out Rect hexRect)
        {
            float room = Mathf.Max(0f, limit - x);
            float swatchWidth = Mathf.Min(swatch, room);
            float hexBudget = room - swatchWidth;
            float usedGap = hexBudget > gap ? gap : 0f;
            float hexW = Mathf.Min(hexWidth, Mathf.Max(0f, hexBudget - usedGap));
            if (room >= 2f && (hexW < 8f || swatchWidth < 8f))
            {
                usedGap = room >= 20f + gap + 8f ? gap : 0f;
                swatchWidth = Mathf.Clamp(room - usedGap - 8f, 8f, Mathf.Min(swatch, room - 1f));
                if (swatchWidth + usedGap >= room) { usedGap = 0f; swatchWidth = Mathf.Max(1f, room * 0.5f); }
                hexW = Mathf.Max(1f, room - swatchWidth - usedGap);
                if (swatchWidth < 1f) swatchWidth = Mathf.Min(room, 1f);
            }
            field = new Rect(x, y, swatchWidth, h);
            hexRect = new Rect(x + swatchWidth + usedGap, y, Mathf.Max(0f, Mathf.Min(hexW, limit - (x + swatchWidth + usedGap))), h);
        }

        // Same layout as lilToon's lilHDRDrawer: a 50px colour field (eyedropper and HDR badge included)
        // and a 50px hex field beside it. An 18px rect is too narrow for the eyedropper.
        internal static bool Draw(Rect position, string label, ref Color value, Rect hexLine = default, bool wrapped = false, Rect stackLine = default)
        {
            bool labeled = !string.IsNullOrEmpty(label);
            const float gap = 4f;
            const float swatch = 50f;
            const float hexWidth = 72f;
            float height = position.height > 1f ? position.height : EditorGUIUtility.singleLineHeight;
            Rect field = position;
            Rect hexRect;
            if (stackLine.width > 1f)
            {
                if (labeled) EditorGUI.LabelField(position, label);
                float swatchLine = hexLine.height > 1f ? hexLine.height : height;
                field = new Rect(hexLine.x, hexLine.y, Mathf.Max(1f, hexLine.width), swatchLine);
                hexRect = new Rect(stackLine.x, stackLine.y, Mathf.Max(1f, stackLine.width), stackLine.height > 1f ? stackLine.height : height);
            }
            else if (wrapped)
            {
                if (labeled) EditorGUI.LabelField(position, label);
                PlaceHdr(hexLine.x, hexLine.y, hexLine.height > 1f ? hexLine.height : height, hexLine.xMax, swatch, hexWidth, gap, out field, out hexRect);
            }
            else
            {
                if (labeled) EditorGUI.PrefixLabel(position, new GUIContent(label));
                float x = position.x + (labeled ? EditorGUIUtility.labelWidth : 0f);
                PlaceHdr(x, position.y, height, position.xMax, swatch, hexWidth, gap, out field, out hexRect);
            }
            float intensity = value.maxColorComponent > 1f ? value.maxColorComponent : 1f;
            int indent = EditorGUI.indentLevel;
            bool checking = false;
            bool colorChanged = false;
            bool hexChanged = false;
            Color picked = value;
            string hex = ColorUtility.ToHtmlStringRGB(new Color(value.r / intensity, value.g / intensity, value.b / intensity, 1f));
            try
            {
                EditorGUI.indentLevel = 0;
                EditorGUI.BeginChangeCheck();
                checking = true;
                picked = EditorGUI.ColorField(field, GUIContent.none, value, true, true, true);
                colorChanged = EditorGUI.EndChangeCheck();
                checking = false;
                EditorGUI.BeginChangeCheck();
                checking = true;
                var unit = new Color(picked.r / intensity, picked.g / intensity, picked.b / intensity, 1f);
                hex = EditorGUI.TextField(hexRect, GUIContent.none, ColorUtility.ToHtmlStringRGB(unit));
                hexChanged = EditorGUI.EndChangeCheck();
                checking = false;
            }
            finally
            {
                if (checking) EditorGUI.EndChangeCheck();
                EditorGUI.indentLevel = indent;
            }
            if (hexChanged)
            {
                if (!ColorUtility.TryParseHtmlString(hex.StartsWith("#", StringComparison.Ordinal) ? hex : "#" + hex, out Color parsed)) return colorChanged && Assign(ref value, picked);
                picked.r = parsed.r * intensity;
                picked.g = parsed.g * intensity;
                picked.b = parsed.b * intensity;
                value = picked;
                return true;
            }
            if (!colorChanged) return false;
            value = picked;
            return true;
        }

        private static bool Assign(ref Color value, Color picked)
        {
            value = picked;
            return true;
        }
    }

    /// <summary>FairyToon inspector in lilToon 2.4.1's layout: Advanced, Preset, and Shader Setting.</summary>
    /// <remarks>Advanced shows every property. Preset is a placeholder until material presets land.
    /// Derived state (keywords, blend, queue, passes) is never edited here:
    /// <see cref="FairyToonMaterialSetup.Apply"/> runs after each change.
    /// Foldout header and the gear menu follow lilToon; see THIRD_PARTY_NOTICES.md.</remarks>
    public sealed class FairyToonGUI : ShaderGUI
    {
        private const string PrefsPrefix = "NW.FairyToon.GUI.";

        private static GUIStyle s_HeaderLabel;
        private static GUIStyle s_BoldFoldout;
        private static GUIStyle s_Box;
        private static GUIStyle s_Outer;
        private static GUIStyle s_OuterBeforeOutline;
        private static GUIStyle s_OutlineBox;
        private static GUIStyle s_Inner;
        private static GUIStyle s_InnerHalf;
        private static GUIStyle s_InnerFull;
        private static GUIStyle s_Custom;
        private static GUIStyle s_Foldout;
        private Rect m_TextureRow;
        private static GUIStyle s_Gear;
        private static Color s_Line;
        private bool m_AlphaMaskAdvanced;
        private int m_InnerDepth;
        private int m_Verticals;
        private bool m_HeaderGapPending;
        private bool m_AlphaInner;
        private bool m_AlphaGearDown;
        private GUIStyle m_BoxStyle;
        private readonly Dictionary<string, bool> m_HdrWrap = new Dictionary<string, bool>();
        private readonly Dictionary<string, int> m_HdrLines = new Dictionary<string, int>();
        private FairyToonEditorMode m_LayoutMode = FairyToonEditorMode.Advanced;
        private bool m_LayoutModeReady;
        private string m_LayoutQuery = "";
        private bool m_HaveQuery;
        private static string s_ActiveQuery = "";
        private readonly List<bool> m_Frames = new List<bool>();
        private static bool s_ProSkin;
        private static string s_Search = "";

        private MaterialEditor m_Editor;
        private MaterialProperty[] m_Props;

        // One property write validates every target, then MaterialEditor validates again after OnGUI.
        // The result is recorded per material so the first target cannot consume the skip for the rest,
        // and a later inspector cannot inherit it. Search and foldouts do not count as writes.
        private struct NativeState
        {
            public int RawQueue;
            public bool Instancing;
            public bool DoubleSidedGI;
        }

        // Keyed by the material reference. Unity 6000.6 makes Object.GetInstanceID a hard error.
        private static readonly HashSet<Object> s_ConsumedTargets = new HashSet<Object>();
        private static readonly HashSet<Object> s_DirectTargets = new HashSet<Object>();
        private static readonly HashSet<Object> s_NativeChanged = new HashSet<Object>();
        private static readonly HashSet<Object> s_PendingSkip = new HashSet<Object>();
        private static readonly HashSet<Object> s_InspectorTargets = new HashSet<Object>();
        private static readonly Dictionary<Object, NativeState> s_NativeBefore = new Dictionary<Object, NativeState>();
        private static bool s_InspectorPass;
        private static bool s_MenuOperation;
        private static int s_PostPassesLeft;
        private static int s_ProbeDepth;

        internal static bool WroteAsset => s_DirectTargets.Count > 0;
        private static readonly HashSet<string> s_ExplicitProperties = new HashSet<string>();

        internal static void NoteExplicitProperty(string name)
        {
            if (!string.IsNullOrEmpty(name)) s_ExplicitProperties.Add(name);
        }

        internal static bool ExplicitProperty(string name)
        {
            return s_ExplicitProperties.Contains(name);
        }

        private sealed class MaterialShot
        {
            public string Text;
            public Dictionary<string, Texture> Textures = new Dictionary<string, Texture>();
        }

        private struct MenuFrame
        {
            public int Id;
            public ValidationSnapshot Validation;
            public Dictionary<Object, MaterialShot> Assets;
        }

        private static int s_MenuSerial;
        private static readonly Stack<MenuFrame> s_MenuFrames = new Stack<MenuFrame>();

        private static Dictionary<Object, MaterialShot> CaptureMenuAssets(MaterialProperty[] props)
        {
            var shot = new Dictionary<Object, MaterialShot>();
            if (props != null)
            {
                for (int i = 0; i < props.Length; i++)
                {
                    MaterialProperty prop = props[i];
                    if (prop == null || prop.targets == null) continue;
                    for (int t = 0; t < prop.targets.Length; t++)
                    {
                        if (prop.targets[t] is Material material && !shot.ContainsKey(material))
                            shot[material] = SnapshotMaterial(material);
                    }
                }
            }
            return shot;
        }

        // Called after the menu setters return, including Unity's default write when a callback returns false.
        internal static void CollectMenuAssetChanges()
        {
            if (s_MenuFrames.Count == 0) return;
            Dictionary<Object, MaterialShot> shot = s_MenuFrames.Peek().Assets;
            var changed = new HashSet<Object>();
            foreach (KeyValuePair<Object, MaterialShot> pair in shot)
            {
                if (pair.Key is Material material && !SameShot(pair.Value, SnapshotMaterial(material)))
                    changed.Add(material);
            }
            if (s_MenuOperation)
            {
                var stale = new List<Object>();
                foreach (Object target in s_DirectTargets)
                    if (shot.ContainsKey(target) && !changed.Contains(target)) stale.Add(target);
                for (int i = 0; i < stale.Count; i++) s_DirectTargets.Remove(stale[i]);
            }
            foreach (Object target in changed) s_DirectTargets.Add(target);
        }

        private static bool SameShot(MaterialShot before, MaterialShot after)
        {
            if (before == null || after == null) return before == after;
            if (before.Text != after.Text) return false;
            if (before.Textures.Count != after.Textures.Count) return false;
            foreach (KeyValuePair<string, Texture> pair in before.Textures)
            {
                if (!after.Textures.TryGetValue(pair.Key, out Texture texture) || texture != pair.Value) return false;
            }
            return true;
        }

        private static MaterialShot SnapshotMaterial(Material material)
        {
            var shot = new MaterialShot();
            if (material == null || material.shader == null) { shot.Text = ""; return shot; }
            var text = new System.Text.StringBuilder();
            text.Append(material.rawRenderQueue).Append('|').Append(material.enableInstancing).Append('|').Append(material.doubleSidedGI).Append('|');
            string[] keywords = material.shaderKeywords;
            if (keywords != null)
                for (int i = 0; i < keywords.Length; i++) text.Append(keywords[i]).Append(',');
            Shader shader = material.shader;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string name = shader.GetPropertyName(i);
                if (!material.HasProperty(name)) continue;
                text.Append('\n').Append(name).Append('=');
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color:
                        AppendExact(text, material.GetColor(name));
                        break;
                    case ShaderPropertyType.Vector:
                        AppendExact(text, material.GetVector(name));
                        break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        text.Append(material.GetFloat(name).ToString("R", CultureInfo.InvariantCulture));
                        break;
                    case ShaderPropertyType.Int:
                        text.Append(material.GetInteger(name));
                        break;
                    case ShaderPropertyType.Texture:
                        shot.Textures[name] = material.GetTexture(name);
                        AppendExact(text, material.GetTextureScale(name));
                        AppendExact(text, material.GetTextureOffset(name));
                        break;
                }
            }
            shot.Text = text.ToString();
            return shot;
        }

        private static void AppendBit(System.Text.StringBuilder text, float value, bool first)
        {
            if (!first) text.Append('|');
            text.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendExact(System.Text.StringBuilder text, Color color)
        {
            AppendBit(text, color.r, true);
            AppendBit(text, color.g, false);
            AppendBit(text, color.b, false);
            AppendBit(text, color.a, false);
        }

        private static void AppendExact(System.Text.StringBuilder text, Vector4 value)
        {
            AppendBit(text, value.x, true);
            AppendBit(text, value.y, false);
            AppendBit(text, value.z, false);
            AppendBit(text, value.w, false);
        }

        private static void AppendExact(System.Text.StringBuilder text, Vector2 value)
        {
            AppendBit(text, value.x, true);
            AppendBit(text, value.y, false);
        }

        internal static void NoteCallbackResult(MaterialProperty property, bool handled)
        {
            if (s_ProbeDepth > 0 || property == null || property.targets == null) return;
            for (int i = 0; i < property.targets.Length; i++)
            {
                Object target = property.targets[i];
                if (target == null) continue;
                if (!handled) continue;
                s_ConsumedTargets.Add(target);
                // A false return has not written yet. FinishMenuOperation records targets that actually change.
                s_PendingSkip.Add(target);
            }
        }

        private static void ClearValidationScope()
        {
            s_ConsumedTargets.Clear();
            s_DirectTargets.Clear();
            s_NativeChanged.Clear();
            s_PendingSkip.Clear();
            s_InspectorTargets.Clear();
            s_NativeBefore.Clear();
            s_InspectorPass = false;
            s_PostPassesLeft = 0;
        }

        private void CaptureNativeState()
        {
            s_NativeBefore.Clear();
            foreach (Object target in m_Editor.targets)
            {
                var material = target as Material;
                if (material == null) continue;
                s_InspectorTargets.Add(material);
                s_NativeBefore[material] = new NativeState
                {
                    RawQueue = material.rawRenderQueue,
                    Instancing = material.enableInstancing,
                    DoubleSidedGI = material.doubleSidedGI
                };
            }
        }

        private void NoteNativeChanges()
        {
            foreach (Object target in m_Editor.targets)
            {
                var material = target as Material;
                if (material == null || !s_NativeBefore.TryGetValue(material, out NativeState before)) continue;
                if (before.RawQueue != material.rawRenderQueue
                    || before.Instancing != material.enableInstancing
                    || before.DoubleSidedGI != material.doubleSidedGI)
                    s_NativeChanged.Add(material);
            }
        }

        public override void ValidateMaterial(Material material)
        {
            if (s_ProbeDepth > 0 || material == null) return;
            // A kept callback skips Apply, but the inspector's follow-up pass still has to count this material
            // exactly once. Returning before the count left the skip for a later, unrelated validation.
            bool pending = s_PendingSkip.Remove(material);
            if (s_MenuOperation)
            {
                // The menu finish compares the asset snapshot. A validation notice is not itself a write.
                return;
            }
            if (s_InspectorPass)
            {
                if (pending) return;
                s_DirectTargets.Add(material);
                FairyToonMaterialSetup.Apply(material);
                return;
            }
            if (s_PostPassesLeft > 0 && s_InspectorTargets.Contains(material))
            {
                s_PostPassesLeft--;
                if (!pending && s_NativeChanged.Contains(material)) FairyToonMaterialSetup.Apply(material);
                if (s_PostPassesLeft == 0) ClearValidationScope();
                return;
            }
            if (pending) return;
            FairyToonMaterialSetup.Apply(material);
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            FairyToonMaterialSetup.Apply(material);
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            m_Editor = materialEditor;
            m_Props = properties;
            if (Event.current != null && Event.current.type == EventType.Layout)
            {
                m_HdrWrap.Clear();
                m_HdrLines.Clear();
                m_LayoutChoice.Clear();
            }
            DrawVersionFoldout();
            DrawHelpFoldout();
            ClearValidationScope();
            s_InspectorPass = true;
            CaptureNativeState();
            var wrapped = new List<SavedCallback>();
            if (properties != null)
                for (int i = 0; i < properties.Length; i++)
                    WrapCallback(properties[i], wrapped);
            bool exitGui = false;
            try
            {
                InitStyles();
                UseInspectorQuery();
                FairyToonL10n.DrawLanguagePopup();
                FairyToonL10n.DrawModeToolbar();
                EditorGUILayout.Space(2f);
                if (Event.current == null || Event.current.type == EventType.Layout)
                {
                    m_LayoutMode = FairyToonL10n.Mode;
                    m_LayoutModeReady = true;
                }
                FairyToonEditorMode page = m_LayoutModeReady && Event.current != null && Event.current.type != EventType.Layout
                    ? m_LayoutMode
                    : FairyToonL10n.Mode;

                switch (page)
                {
                    case FairyToonEditorMode.Advanced:
                        DrawAdvanced();
                        break;
                    case FairyToonEditorMode.Preset:
                        DrawPresetPage();
                        break;
                    case FairyToonEditorMode.ShaderSetting:
                        DrawShaderSettingPage();
                        break;
                    default:
                        throw new InvalidOperationException("Unhandled FairyToon editor mode " + FairyToonL10n.Mode);
                }
            }
            catch (ExitGUIException)
            {
                exitGui = true;
                throw;
            }
            catch (Exception)
            {
                exitGui = true;
                throw;
            }
            finally
            {
                RestoreCallbacks(wrapped);
                s_InspectorPass = false;
                while (m_Verticals > 0) PopVertical();
                m_InnerDepth = 0;
                m_HeaderGapPending = false;
                m_Frames.Clear();
                if (exitGui)
                {
                    ClearValidationScope();
                }
                else
                {
                    NoteNativeChanges();
                    if (GUI.changed) s_PostPassesLeft = s_InspectorTargets.Count;
                    else ClearValidationScope();
                }
            }
        }

        internal struct SavedCallback
        {
            public MaterialProperty Property;
            public MaterialProperty.ApplyPropertyCallback Original;
        }

        private static void WrapCallback(MaterialProperty property, List<SavedCallback> wrapped)
        {
            if (property == null || property.applyPropertyCallback == null) return;
            MaterialProperty.ApplyPropertyCallback original = property.applyPropertyCallback;
            wrapped.Add(new SavedCallback { Property = property, Original = original });
            property.applyPropertyCallback = (changed, mask, previous) =>
            {
                bool handled = false;
                bool returned = false;
                try
                {
                    handled = original(changed, mask, previous);
                    returned = true;
                }
                finally
                {
                    if (returned) NoteCallbackResult(changed, handled);
                }
                return handled;
            };
        }

        private static void RestoreCallbacks(List<SavedCallback> wrapped)
        {
            for (int i = 0; i < wrapped.Count; i++)
                if (wrapped[i].Property != null) wrapped[i].Property.applyPropertyCallback = wrapped[i].Original;
        }

        // The gear menu runs after OnGUI restores the original callbacks. Wrap them again for that one action
        // so its setter validation uses the same per-material result as a field edit.
        // Returns 0 when the frame was not pushed, so a throw during the snapshot cannot pop an outer frame.
        internal static int BeginMenuOperation(MaterialProperty[] props, List<SavedCallback> wrapped)
        {
            var frame = new MenuFrame
            {
                Id = ++s_MenuSerial,
                Validation = TakeValidationSnapshot(),
                Assets = CaptureMenuAssets(props)
            };
            s_MenuFrames.Push(frame);
            try
            {
                s_ExplicitProperties.Clear();
                ClearValidationScope();
                s_MenuOperation = true;
                if (props != null)
                    for (int i = 0; i < props.Length; i++) WrapCallback(props[i], wrapped);
                return frame.Id;
            }
            catch
            {
                if (s_MenuFrames.Count > 0 && s_MenuFrames.Peek().Id == frame.Id)
                    RestoreValidationSnapshot(s_MenuFrames.Pop().Validation);
                throw;
            }
        }

        internal static void EndMenuOperation(List<SavedCallback> wrapped, int frameId)
        {
            if (wrapped != null) RestoreCallbacks(wrapped);
            if (frameId == 0 || s_MenuFrames.Count == 0 || s_MenuFrames.Peek().Id != frameId) return;
            RestoreValidationSnapshot(s_MenuFrames.Pop().Validation);
        }

        private struct ValidationSnapshot
        {
            public HashSet<Object> Consumed;
            public HashSet<Object> Direct;
            public HashSet<Object> NativeChanged;
            public HashSet<Object> Pending;
            public HashSet<Object> Inspector;
            public Dictionary<Object, NativeState> NativeBefore;
            public bool InspectorPass;
            public bool Menu;
            public int PostPasses;
        }

        private static ValidationSnapshot TakeValidationSnapshot()
        {
            return new ValidationSnapshot
            {
                Consumed = new HashSet<Object>(s_ConsumedTargets),
                Direct = new HashSet<Object>(s_DirectTargets),
                NativeChanged = new HashSet<Object>(s_NativeChanged),
                Pending = new HashSet<Object>(s_PendingSkip),
                Inspector = new HashSet<Object>(s_InspectorTargets),
                NativeBefore = new Dictionary<Object, NativeState>(s_NativeBefore),
                InspectorPass = s_InspectorPass,
                Menu = s_MenuOperation,
                PostPasses = s_PostPassesLeft
            };
        }

        private static void RestoreValidationSnapshot(ValidationSnapshot snapshot)
        {
            CopySet(s_ConsumedTargets, snapshot.Consumed);
            CopySet(s_DirectTargets, snapshot.Direct);
            CopySet(s_NativeChanged, snapshot.NativeChanged);
            CopySet(s_PendingSkip, snapshot.Pending);
            CopySet(s_InspectorTargets, snapshot.Inspector);
            s_NativeBefore.Clear();
            if (snapshot.NativeBefore != null)
                foreach (var pair in snapshot.NativeBefore) s_NativeBefore[pair.Key] = pair.Value;
            s_InspectorPass = snapshot.InspectorPass;
            s_MenuOperation = snapshot.Menu;
            s_PostPassesLeft = snapshot.PostPasses;
        }

        private static void CopySet(HashSet<Object> destination, HashSet<Object> source)
        {
            destination.Clear();
            if (source == null) return;
            foreach (Object item in source) destination.Add(item);
        }

        internal static void NoteDirectWrite(Object target)
        {
            if (target != null) s_DirectTargets.Add(target);
        }

        internal static bool TargetWroteAsset(Object target)
        {
            return target != null && s_DirectTargets.Contains(target);
        }

        private void DrawVersionFoldout()
        {
            string key = PrefsPrefix + "Foldout.Version";
            bool stored = EditorPrefs.GetBool(key, false);
            bool open = LayoutChoice(key, stored);
            var versionStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
            Rect position = EditorGUILayout.GetControlRect();
            EditorGUI.LabelField(position, "FairyToon 0.2.3", versionStyle);
            bool nextVersion = EditorGUI.Foldout(position, open, GUIContent.none, true);
            if (nextVersion != stored) EditorPrefs.SetBool(key, nextVersion);
            if (!open) return;
            EditorGUI.indentLevel++;
            if (GUILayout.Button("GitHub")) Application.OpenURL("https://github.com/p2rumia-prog/FairyToon");
            EditorGUI.indentLevel--;
        }

        private void DrawHelpFoldout()
        {
            string key = PrefsPrefix + "Foldout.Help";
            bool stored = EditorPrefs.GetBool(key, false);
            bool open = LayoutChoice(key, stored);
            bool nextHelp = EditorGUILayout.Foldout(open, FairyToonL10n.Text("sHelp"), true);
            if (nextHelp != stored) EditorPrefs.SetBool(key, nextHelp);
            if (!open) return;
            EditorGUI.indentLevel++;
            if (GUILayout.Button("README")) Application.OpenURL("https://github.com/p2rumia-prog/FairyToon#readme");
            EditorGUI.indentLevel--;
        }

        // Section order follows lilToon 2.4.1's full inspector, with FairyToon-only sections beside the
        // group they belong to. See the pull request for the before/after list.
        //   Base Setting, Lighting
        //   Color: Main, Main 2nd, Main 3rd, Sheer Fabric, 3D Iris (Toon) or Endfield (in Shadow's place), Shadow, RimShade, Emission
        //   Normal Map & Reflection: Normal, Normal 2nd, Anisotropy, Backlight, Reflection, Wetness, MatCap, Rim Light, Glitter, Gem
        //   Advanced: Outline, Dissolve, Refraction, Fur, See-through Hair, Stencil, Rendering
        private void DrawAdvanced()
        {
            s_Search = EditorGUILayout.TextField(s_Search, EditorStyles.toolbarSearchField);
            if (Event.current == null || Event.current.type == EventType.Layout)
            {
                m_LayoutQuery = s_Search ?? "";
                m_HaveQuery = true;
            }
            UseInspectorQuery();
            RenderingMode();
            EditorGUI.BeginChangeCheck();
            try
            {
            if (!Searching || SearchAllows("_ShadingModel", "_TransparentMode|sRenderingMode", "_Cull", "_ZWrite", "_Cutoff", "_FlipNormal", "_BackfaceColor", "_BackfaceForceShadow", "_AAStrength", "_ShadingModel|Shading Model", "_BackfaceColor|sAlpha", "|sBaseSetting"))
                Group("sBaseSetting");
            BaseSetting();
            Lighting();
            UvSetting();
            Group("sColors");
            MainColor();
            if (Prop("_SheerAmount") != null) SheerFabric();
            var shading = Prop("_ShadingModel");
            bool mixedShading = shading != null && shading.hasMixedValue;
            bool endfield = shading != null && !shading.hasMixedValue
                && Mathf.RoundToInt(Number(shading)) == FairyToonMaterialSetup.ShadingEndfield;
            bool showToon = LayoutChoice("show-toon", mixedShading || !endfield);
            bool showEndfield = LayoutChoice("show-endfield", mixedShading || endfield);
            if (showToon && !EverySelectedIsGem()) ToonIris();
            if (showEndfield) Endfield();
            if (showToon && !EverySelectedIsGem()) Shadow();
            if (!EverySelectedIsGem()) RimShade();
            EmissionGroup();
            Group("sNormalMapReflection");
            NormalGroup();
            if (!EverySelectedIsGem()) Backlight();
            if (!EverySelectedIsGem()) Reflection();
            Wetness(endfield && !mixedShading);
            MatCapGroup();
            RimLight();
            Glitter();
            if (PrimaryMode() == FairyToonMaterialSetup.Gem) Gem();
            Group("sAdvanced");
            if (PrimaryMode() != FairyToonMaterialSetup.Fur && PrimaryMode() != FairyToonMaterialSetup.Gem)
                Outline();
            if (AnyRunsWholeDissolve()) Dissolve();
            if (PrimaryMode() == FairyToonMaterialSetup.Refraction) Refraction();
            if (PrimaryMode() == FairyToonMaterialSetup.Fur) Fur();
            if (PrimaryMode() != FairyToonMaterialSetup.Refraction && PrimaryMode() != FairyToonMaterialSetup.Fur && PrimaryMode() != FairyToonMaterialSetup.Gem)
                SeeThrough();
            Stencil();
            Rendering();
            }
            finally
            {
                if (EditorGUI.EndChangeCheck()) NoteNativeChanges();
            }
        }

        private void Group(string key)
        {
            string shown = FairyToonL10n.Text(key);
            // lilToon keeps the Color, Normal Map & Reflection, and Advanced headings visible during search.
            EditorGUILayout.Space();
            GUILayout.Label(shown, EditorStyles.boldLabel);
        }

        private void EmissionGroup()
        {

            if (!PrefixMatches("_Emission") && !PrefixMatches("_UseEmission") && !SearchAllows("|ft.Tiling", "|ft.Offset", "|UV Animation", "|UV Setting")
                && !ComponentHit("_EmissionMap_ScrollRotate") && !ComponentHit("_Emission2ndMap_ScrollRotate")
                && !ComponentHit("_EmissionBlendMask_ScrollRotate") && !ComponentHit("_Emission2ndBlendMask_ScrollRotate")) return;
            if (!BeginSection("EmissionGroup", "sEmissionSetting", null, false, PropertyBlock.Emission, true, false)) return;
            Emission(false);
            Emission(true);
            EndSection();
        }

        private void NormalGroup()
        {

            if (!SearchAllows("_UseBumpMap", "_BumpMap", "_BumpScale", "_UseBump2ndMap", "_Bump2ndMap", "_Bump2ndScale", "_Bump2ndMap_UVMode", "_Bump2ndScaleMask", "_UseAnisotropy", "_AnisotropyTangentMap", "_AnisotropyScale", "_AnisotropyScaleMask", "_AnisotropyTangentWidth", "_AnisotropyBitangentWidth", "_AnisotropyShift", "_AnisotropyShiftNoiseScale", "_AnisotropySpecularStrength", "_Anisotropy2ndTangentWidth", "_Anisotropy2ndBitangentWidth", "_Anisotropy2ndShift", "_Anisotropy2ndShiftNoiseScale", "_Anisotropy2ndSpecularStrength", "_AnisotropyShiftNoiseMask", "_Anisotropy2Reflection", "_Anisotropy2MatCap", "_Anisotropy2MatCap2nd", "|ft.Tiling", "|ft.Offset")) return;
            if (!BeginSection("NormalGroup", "sNormalMapSetting", null, false, PropertyBlock.NormalMap, true, false)) return;
            NormalMaps();
            Anisotropy();
            EndSection();
        }

        private void MatCapGroup()
        {

            if (!PrefixMatches("_MatCap") && !SearchAllows("_UseMatCap", "_UseMatCap2nd", "|ft.Tiling", "|ft.Offset", "|sZRotCancel", "|sFixPerspective", "|sCustomNormal")) return;
            if (!BeginSection("MatCapGroup", "sMatCapSetting", null, false, PropertyBlock.MatCaps, true, false)) return;
            MatCap();
            EndSection();
        }

        private bool AnyRunsWholeDissolve()
        {
            bool any = false;
            foreach (Object target in m_Editor.targets)
            {
                int mode = FairyToonMaterialSetup.GetRenderingMode(target as Material);
                if (mode != FairyToonMaterialSetup.Opaque && mode != FairyToonMaterialSetup.Gem) any = true;
            }
            return any;
        }

        private int PrimaryMode() => FairyToonMaterialSetup.GetRenderingMode(m_Editor.target as Material);

        // Unity only draws this GUI when every selected material uses the same shader, so the first target's shader is the selection.
        private bool EverySelectedIsGem() => PrimaryMode() == FairyToonMaterialSetup.Gem;

        private readonly Dictionary<string, bool> m_LayoutChoice = new Dictionary<string, bool>();

        private bool LayoutChoice(string key, bool live)
        {
            bool layout = Event.current == null || Event.current.type == EventType.Layout;
            if (layout)
            {
                m_LayoutChoice[key] = live;
                return live;
            }
            return m_LayoutChoice.TryGetValue(key, out bool cached) && cached;
        }

        // Each inspector keeps the query from its own Layout. The text field itself can stay shared.
        private void UseInspectorQuery()
        {
            s_ActiveQuery = Event.current != null && Event.current.type != EventType.Layout && m_HaveQuery
                ? m_LayoutQuery
                : (s_Search ?? "");
        }

        private static string Query => s_ActiveQuery ?? "";

        private static bool Searching => !string.IsNullOrEmpty(Query);

        private static bool Hit(string text) => !string.IsNullOrEmpty(text) && text.IndexOf(Query, StringComparison.Ordinal) >= 0;

        private static bool HitIgnore(string text) => !string.IsNullOrEmpty(text) && text.IndexOf(Query, StringComparison.OrdinalIgnoreCase) >= 0;

        private bool ComponentHit(string name)
        {
            if (!Searching || string.IsNullOrEmpty(name)) return false;
            return HitIgnore(name) || HitIgnore(name + ".x") || HitIgnore(name + ".y") || HitIgnore(name + ".z") || HitIgnore(name + ".w");
        }

        /// <summary>The query contains the whole phrase, so a short word such as Offset does not open a longer Alpha Mask label.</summary>
        private bool Phrase(string phrase) =>
            Searching && !string.IsNullOrEmpty(phrase) && Query.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>True when this row would be drawn and its property name or final label contains the query.</summary>
        private bool LabelOnly(string key)
        {
            if (!Searching) return true;
            return HitIgnore(key) || HitIgnore(FairyToonL10n.Text(key));
        }

        private bool UnityRowVisible(string english)
        {
            if (!Searching) return true;
            return HitIgnore(english) || HitIgnore(L10n.Tr(english));
        }

        private bool RowVisible(string name, string shown, string tooltip = null)
        {
            if (!Searching) return true;
            if (!string.IsNullOrEmpty(name) && HitIgnore(name)) return true;
            if (!string.IsNullOrEmpty(shown) && (HitIgnore(shown) || HitIgnore(FairyToonL10n.Text(shown)))) return true;
            if (!string.IsNullOrEmpty(tooltip) && (HitIgnore(tooltip) || HitIgnore(FairyToonL10n.Text(tooltip)))) return true;
            if (string.IsNullOrEmpty(name)) return false;
            var prop = Prop(name);
            if (prop == null) return false;
            return HitIgnore(prop.displayName) || HitIgnore(FairyToonL10n.Property(name, prop.displayName));
        }

        /// <summary>A section stays visible while any row it actually draws matches. A leading "|" is a label key with no property.</summary>
        private bool SearchAllows(params string[] rows)
        {
            if (!Searching) return true;
            for (int i = 0; i < rows.Length; i++)
            {
                string row = rows[i];
                if (string.IsNullOrEmpty(row)) continue;
                if (row[0] == '|')
                {
                    string key = row.Substring(1);
                    if (HitIgnore(key) || HitIgnore(FairyToonL10n.Text(key))) return true;
                    continue;
                }
                int split = row.IndexOf('|');
                if (split >= 0)
                {
                    string name = row.Substring(0, split);
                    string rest = row.Substring(split + 1);
                    int tipSplit = rest.IndexOf('|');
                    string label = tipSplit >= 0 ? rest.Substring(0, tipSplit) : rest;
                    string tip = tipSplit >= 0 ? rest.Substring(tipSplit + 1) : null;
                    if (RowVisible(name, label, tip)) return true;
                    continue;
                }
                if (RowVisible(row, null)) return true;
            }
            return false;
        }

        /// <summary>Matches a drawn property whose name starts with <paramref name="prefix"/> and not <paramref name="except"/>.</summary>
        private bool PrefixMatches(string prefix, string except = null)
        {
            if (!Searching) return true;
            if (m_Props == null) return false;
            for (int i = 0; i < m_Props.Length; i++)
            {
                MaterialProperty prop = m_Props[i];
                if (prop == null || !prop.name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (except != null && prop.name.StartsWith(except, StringComparison.Ordinal)) continue;
                if (RowVisible(prop.name, null)) return true;
            }
            return false;
        }

        private void DrawPresetPage()
        {
            Help(FairyToonL10n.Text("ft.presetPlaceholder"), MessageType.Info);
        }

        private void DrawShaderSettingPage()
        {
            Help(FairyToonL10n.Text("ft.shaderSettingIntro"), MessageType.Info);
            EditorGUILayout.LabelField(FairyToonL10n.Text("ft.languageLivesInTopBar"), EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(FairyToonL10n.Text("ft.defaultShadingHeader"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(FairyToonL10n.Text("ft.defaultShadingBody"), EditorStyles.wordWrappedLabel);
        }

        // ---- Sections (lilToon order) ----

        private void BaseSetting()
        {
            if (!SearchAllows("_ShadingModel", "_TransparentMode|sRenderingMode", "_Cull", "_ZWrite", "_Cutoff", "_FlipNormal", "_BackfaceColor", "_BackfaceForceShadow", "_AAStrength", "_ShadingModel|Shading Model", "_BackfaceColor|sAlpha")) return;
            if (!BeginSection("Base", "sBaseSetting", null, true, PropertyBlock.Base, true, true, false)) return;
            P("_ShadingModel", "Shading Model");
            if (!EverySelectedIsGem())
            {
                P("_Cull");
                var cull = Prop("_Cull");
                bool front = cull != null && !cull.hasMixedValue && Mathf.RoundToInt(Number(cull)) == 1;
                if (LayoutChoice("cull-front", front))
                    DrawCullWarning(cull);
            }
            if (!EverySelectedIsGem() && LayoutChoice("backface", CullShowsBackface()))
            {
                EditorGUI.indentLevel++;
                P("_FlipNormal");
                P("_BackfaceForceShadow");
                P("_BackfaceColor", "Color");
                ColorAlpha("_BackfaceColor");
                EditorGUI.indentLevel--;
            }
            DrawZWrite();
            if (AnyCoverage(FairyToonMaterialSetup.Cutout, FairyToonMaterialSetup.Transparent, FairyToonMaterialSetup.Fur))
                P("_Cutoff", "Cutoff");
            P("_AAStrength");
            EndSection();
        }

        private void DrawCullWarning(MaterialProperty cull)
        {
            const string message = "If the material does not look right, set the CullMode to Off or Back.";
            float scale = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
            float height = 75f / scale;
            float icon = 48f / scale;
            float buttonWidth = 86f / scale;
            float buttonHeight = 29f / scale;
            float pad = 8f / scale;
            Rect row = EditorGUILayout.GetControlRect(false, height);
            if (Event.current != null && Event.current.type == EventType.Repaint)
                EditorStyles.helpBox.Draw(row, false, false, false, false);
            Rect iconRect = new Rect(row.x + 15f / scale, row.y + 11f / scale, icon, icon);
            GUIContent iconContent = EditorGUIUtility.IconContent("console.warnicon");
            if (iconContent.image != null && Event.current != null && Event.current.type == EventType.Repaint)
                GUI.DrawTexture(iconRect, iconContent.image, ScaleMode.StretchToFill);
            float rightInset = 13f / scale;
            float buttonWidthPx = buttonWidth + 1f / scale;
            Rect button = new Rect(row.xMax - rightInset - buttonWidthPx, row.yMax - pad - buttonHeight, buttonWidthPx, buttonHeight);
            var mini = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            Rect text = new Rect(iconRect.xMax + 4f - 10f / scale, row.y + 4f + 3f / scale, Mathf.Max(0f, button.x - iconRect.xMax - 8f), button.yMax - row.y - 6f);
            GUI.Label(text, message, mini);
            BeginScope(row, cull);
            try
            {
                if (GUI.Button(button, "Fix Now")) SetNumber(cull, 2f);
            }
            finally { EndScope(); }
        }

        private string MainColorLabel()
        {
            var mode = Prop("_TransparentMode");
            int modeId = PrimaryMode();
            if (modeId == FairyToonMaterialSetup.Gem || modeId == FairyToonMaterialSetup.Refraction) return "sColor";
            bool alpha = (mode != null && mode.hasMixedValue) || modeId != FairyToonMaterialSetup.Opaque;
            return alpha ? "sColorAlpha" : "sColor";
        }

        private void UvSetting()
        {
            // lilToon draws _MainTex scale and offset in UV Setting, after Lighting, not on the Color row.
            if (Prop("_MainTex") == null) return;
            if (!SearchAllows("_MainTex", "|ft.Tiling", "|ft.Offset", "|sMainUV")) return;
            if (!BeginSection("UV", "sMainUV", null, false, null)) return;
            ScaleOffset("_MainTex", Searching && HitIgnore(FairyToonL10n.Text("sMainUV")));
            EndSection();
        }

        private void Lighting()
        {

            if (!SearchAllows("_LightMinLimit", "_LightMaxLimit", "_MonochromeLighting", "_AsUnlit", "_LightMinLimit|sLightMinLimit", "_LightMaxLimit|sLightMaxLimit", "_MonochromeLighting|sMonochromeLighting", "_AsUnlit|sAsUnlit")) return;
            if (!BeginSection("Lighting", "sLightingSettings", null, false, PropertyBlock.Lighting)) return;
            P("_LightMinLimit", "sLightMinLimit");
            P("_LightMaxLimit", "sLightMaxLimit");
            P("_MonochromeLighting", "sMonochromeLighting");
            P("_AsUnlit", "sAsUnlit");
            EndSection();
        }

        private void MainColor()
        {
            bool gem = EverySelectedIsGem();
            bool first = gem
                ? SearchAllows("_MainTex", "_Color")
                : SearchAllows("_MainTex", "_Color", "_MainTexHSVG", "_MainColorAdjustMask", "|sReset", "|sColorAdjust", "|sMask", "|HSV / Gamma");
            bool alpha = (SearchAllows("_AlphaMaskMode", "_AlphaMask", "_Cutoff", "|sAlphaMaskWarnOpaque", "|ft.AlphaMaskInvert", "|ft.AlphaMaskTransparency", "|ft.AlphaMaskAdvanced")
                || (m_AlphaMaskAdvanced && (RowVisible("_AlphaMaskScale", "Scale") || RowVisible("_AlphaMaskValue", "Offset")))) && AlphaSectionVisible();
            bool second = PrefixMatches("_Main2nd") || PrefixMatches("_FT_Main2nd") || SearchAllows("_Color2nd", "_UseMain2ndTex", "|UV Line Angle (radians)", "|Position / Direction (XY UV, XYZ Object, W UV Angle)", "|ft.Tiling", "|ft.Offset", "|sAsDecal", "|Decal", "|sMirrorMode", "|sCopyMode", "|sPositionX", "|sPositionY", "|sScaleX", "|sScaleY", "|sScroll", "|sRotate", "|sDissolveMode");
            bool third = PrefixMatches("_Main3rd") || PrefixMatches("_FT_Main3rd") || SearchAllows("_Color3rd", "_UseMain3rdTex", "|UV Line Angle (radians)", "|Position / Direction (XY UV, XYZ Object, W UV Angle)", "|ft.Tiling", "|ft.Offset", "|sAsDecal", "|Decal", "|sMirrorMode", "|sCopyMode", "|sPositionX", "|sPositionY", "|sScaleX", "|sScaleY", "|sScroll", "|sRotate", "|sDissolveMode");
            if (Searching && !first && !alpha && !second && !third) return;
            if (!BeginSection("Main", "sMainColorSetting", null, true, PropertyBlock.MainColor, true, false)) return;
            if (first) MainColorFirst();
            if (second) MainColorLayer("2nd");
            if (third) MainColorLayer("3rd");
            if (alpha) AlphaMask();
            EndSection();
        }

        private void MainColorFirst()
        {
            // lilToon draws this group directly under the parent, with a label and gear, not another foldout.
            BeginGearRow("sMainColor", PropertyBlock.MainColor1st);
            Tex("_MainTex", "_Color", MainColorLabel());
            if (!EverySelectedIsGem())
            {
                Separator();
                string adjustKey = PrefsPrefix + "Foldout.MainAdjust";
                bool adjust = FoldShown(adjustKey, Searching);
                bool nextAdjust = EditorGUILayout.Foldout(adjust, FairyToonL10n.Text("sColorAdjust"), true, s_BoldFoldout);
                SaveFoldClick(adjustKey, adjust, nextAdjust);
                if (adjust || Searching)
                {
                    // lilToon draws the mask, then a bold indented HSV / Gamma with its own reset.
                    Tex("_MainColorAdjustMask", null, "sMask");
                    if (!Searching || RowVisible("_MainTexHSVG", "Hue / Saturation / Value / Gamma") || HitIgnore("HSV / Gamma"))
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.LabelField("HSV / Gamma", EditorStyles.boldLabel);
                        EditorGUI.indentLevel++;
                        HsvgSliders("_MainTexHSVG", HitIgnore("HSV / Gamma"));
                        EditorGUI.indentLevel--;
                        Separator();
                        EditorGUI.indentLevel--;
                    }
                    var hsvg = Prop("_MainTexHSVG");
                    if (hsvg != null && (!Searching || LabelOnly("sReset")) && GUILayout.Button(FairyToonL10n.Text("sReset")))
                        hsvg.vectorValue = new Vector4(0f, 1f, 1f, 1f);
                }
            }
            EndGearRow();
        }

        private void AlphaMask()
        {
            if (EverySelectedIsGem()) return;
            PixelGap(1);
            var coverage = Prop("_TransparentMode");
            bool mixedCoverage = coverage != null && coverage.hasMixedValue;
            bool opaque = PrimaryMode() == FairyToonMaterialSetup.Opaque && !mixedCoverage;
            if (opaque)
            {
                string warning = FairyToonL10n.Text("sAlphaMaskWarnOpaque");
                if (!Searching || HitIgnore(warning) || HitIgnore("Alpha Mask"))
                    EditorGUILayout.LabelField(warning, EditorStyles.wordWrappedLabel);
                return;
            }
            // lilToon draws the mode popup in the outer title bar, labeled Alpha Mask, and always
            // draws it (shouldCheck false) so a search such as Alpha Mask Scale keeps the bar.
            ConsumeHeaderGap();
            PushVertical(s_Outer);
            var mode = Prop("_AlphaMaskMode");
            DrawAlphaMaskTitle(mode, PropertyBlock.AlphaMask);
            if (AlphaInner(mode))
            {
                PushVertical(s_InnerHalf);
                m_InnerDepth++;
                Tex("_AlphaMask", null, "Alpha Mask");
                bool alphaOffsetRow = !Searching || LabelOnly("ft.Offset") || LabelOnly("ft.Tiling") || HitIgnore("_AlphaMask");
                ScaleOffset("_AlphaMask");
                // 2 points is 3 px at 150%. Skip it when search hides the Offset row.
                if (alphaOffsetRow) GUILayout.Space(2f);
                var scale = Prop("_AlphaMaskScale");
                var offset = Prop("_AlphaMaskValue");
                if (scale != null && offset != null)
                {
                    // Alt shows the real property names, as lilToon does. Ordinary search uses each control's own label.
                    string invertShown = FairyToonL10n.Text("ft.AlphaMaskInvert");
                    string transparencyShown = FairyToonL10n.Text("ft.AlphaMaskTransparency");
                    string invertLabel = Event.current.alt ? scale.name : invertShown;
                    string transparencyLabel = Event.current.alt ? scale.name + ", " + offset.name : transparencyShown;
                    bool invert = Number(scale) < 0f;
                    float transparency = Number(offset) - (invert ? 1f : 0f);
                    bool invertChanged = false;
                    bool transparencyChanged = false;
                    bool blocked = AlphaPairBlocked(scale, offset);
                    bool scaleOwned = LayoutChoice("alpha-scale-owned", scale.applyPropertyCallback != null);
                    bool offsetOwned = LayoutChoice("alpha-offset-owned", offset.applyPropertyCallback != null);
                    bool alphaHelp = LayoutChoice("alpha-help", blocked || scaleOwned || offsetOwned);
                    if (alphaHelp && blocked)
                        EditorGUILayout.HelpBox("Invert and Transparency stay unchanged while the selection is mixed or locked.", MessageType.Info);
                    else if (alphaHelp && (scaleOwned || offsetOwned))
                        EditorGUILayout.HelpBox("A side owned by an animation preview stays unchanged. The other side can still be edited.", MessageType.Info);
                    if (!Searching || HitIgnore(invertShown) || HitIgnore(scale.name))
                    {
                        Rect row = EditorGUILayout.GetControlRect();
                        using (new EditorGUI.DisabledScope(blocked || scaleOwned || offsetOwned))
                        {
                        BeginScope(row, scale);
                        BeginScope(row, offset);
                        try
                        {
                            EditorGUI.showMixedValue = scale.hasMixedValue || offset.hasMixedValue;
                            EditorGUI.BeginChangeCheck();
                            invert = EditorGUI.Toggle(row, invertLabel, invert);
                            invertChanged = EditorGUI.EndChangeCheck();
                        }
                        finally
                        {
                            EditorGUI.showMixedValue = false;
                            EndScope();
                            EndScope();
                        }
                        }
                    }
                    if (!Searching || HitIgnore(transparencyShown) || HitIgnore(offset.name))
                    {
                        Rect row = EditorGUILayout.GetControlRect();
                        using (new EditorGUI.DisabledScope(blocked || offsetOwned))
                        {
                        BeginScope(row, offset);
                        try
                        {
                            EditorGUI.showMixedValue = scale.hasMixedValue || offset.hasMixedValue;
                            EditorGUI.BeginChangeCheck();
                            transparency = EditorGUI.Slider(row, transparencyLabel, transparency, -1f, 1f);
                            transparencyChanged = EditorGUI.EndChangeCheck();
                        }
                        finally
                        {
                            EditorGUI.showMixedValue = false;
                            EndScope();
                        }
                        }
                    }
                    if (!blocked && (invertChanged || transparencyChanged))
                        WriteAlphaMask(scale, offset, invertChanged, transparencyChanged, invert, transparency);
                }
                if (Prop("_Cutoff") != null) P("_Cutoff", "Cutoff");
                string advancedShown = FairyToonL10n.Text("ft.AlphaMaskAdvanced");
                string advancedLabel = Event.current.alt ? "_AlphaMaskScale, _AlphaMaskValue" : advancedShown;
                if (!Searching || HitIgnore(advancedShown) || HitIgnore("Show advanced editor"))
                    m_AlphaMaskAdvanced = EditorGUILayout.Toggle(advancedLabel, m_AlphaMaskAdvanced);
                if (LayoutChoice("alpha-advanced", m_AlphaMaskAdvanced))
                {
                    EditorGUI.indentLevel++;
                    P("_AlphaMaskScale", "Scale");
                    P("_AlphaMaskValue", "Offset");
                    EditorGUI.indentLevel--;
                }
            }
            CloseBox();
        }

        private bool AlphaSectionVisible()
        {
            if (EverySelectedIsGem()) return false;
            var coverage = Prop("_TransparentMode");
            bool opaque = PrimaryMode() == FairyToonMaterialSetup.Opaque && (coverage == null || !coverage.hasMixedValue);
            if (!opaque || !Searching) return true;
            string warning = FairyToonL10n.Text("sAlphaMaskWarnOpaque");
            return HitIgnore(warning) || HitIgnore("Alpha Mask");
        }

        private bool CullShowsBackface()
        {
            var cull = Prop("_Cull");
            if (cull == null) return true;
            if (cull.hasMixedValue) return true;
            foreach (Object target in m_Editor.targets)
            {
                var material = target as Material;
                if (material == null || !material.HasProperty("_Cull")) continue;
                if (Mathf.RoundToInt(material.GetFloat("_Cull")) <= 1) return true;
            }
            return Mathf.RoundToInt(Number(cull)) <= 1;
        }

        private bool m_AlphaBlocked;

        private bool AlphaPairBlocked(MaterialProperty scale, MaterialProperty offset)
        {
            if (Event.current == null || Event.current.type == EventType.Layout)
            {
                bool locked = false;
                if (scale.targets != null)
                {
                    foreach (Object target in scale.targets)
                    {
                        var material = target as Material;
                        if (material != null && (Locked(material, scale.name) || Locked(material, offset.name))) locked = true;
                    }
                }
                m_AlphaBlocked = scale.hasMixedValue || offset.hasMixedValue || locked;
            }
            return m_AlphaBlocked;
        }

        private bool AlphaInner(MaterialProperty mode)
        {
            if (Event.current != null && Event.current.type == EventType.Layout)
                m_AlphaInner = mode != null && (mode.hasMixedValue || Number(mode) != 0f);
            return m_AlphaInner;
        }

        private void DrawAlphaMaskTitle(MaterialProperty mode, PropertyBlock block)
        {
            // A single line sits in the title band. A 22-point row made the band 39 px and left 9 px under the popup.
            Rect row = EditorGUILayout.GetControlRect();
            Rect gear = new Rect(row.xMax - 22f, row.y, 20f, row.height);
            string shown = mode != null && Event.current.alt ? mode.name : FairyToonL10n.Text("sAlphaMask");
            Event current = Event.current;
            if (current != null && current.type == EventType.MouseDown && current.button == 0)
            {
                m_AlphaGearDown = gear.Contains(current.mousePosition);
                if (m_AlphaGearDown) current.Use();
            }
            else if (current != null && current.type == EventType.MouseUp && current.button == 0)
            {
                if (m_AlphaGearDown && gear.Contains(current.mousePosition))
                {
                    FairyToonClipboard.ShowMenu(block, m_Editor, () => m_Props);
                    current.Use();
                }
                m_AlphaGearDown = false;
            }
            if (mode != null)
            {
                if (!DrawAlphaMaskPopup(mode, shown, row))
                    m_Editor.ShaderProperty(row, mode, shown);
            }
            else GUI.Label(row, shown, s_HeaderLabel);
            if (current != null && current.type == EventType.Repaint && s_Gear != null)
                s_Gear.Draw(gear, EditorGUIUtility.IconContent("_Popup"), false, false, false, false);
        }

        private bool DrawAlphaMaskPopup(MaterialProperty prop, string shown, Rect rect)
        {
            if (!DrawLocalizedEnum(prop, shown, null, rect, true)) return false;
            return true;
        }

        private void MainColorLayer(string layer)
        {
            string prefix = "_Main" + layer;
            string titleKey = layer == "2nd" ? "sMainColor2nd" : "sMainColor3rd";
            var block = layer == "2nd" ? PropertyBlock.MainColor2nd : PropertyBlock.MainColor3rd;
            if (!BeginSection("Main" + layer, titleKey, Prop("_UseMain" + layer + "Tex"), false, block, false)) return;
            Tex(prefix + "Tex", "_Color" + layer, "sColor");
            EditorGUI.indentLevel += 2;
            ColorAlpha("_Color" + layer);
            P(prefix + "TexIsMSDF", "sMSDFTexture");
            P(prefix + "Tex_Cull", "sCullMode");
            EditorGUI.indentLevel -= 2;
            P(prefix + "EnableLighting", "sEnableLighting");
            P(prefix + "TexBlendMode", "sBlendMode");
            P(prefix + "TexAlphaMode", "sTransparentMode");
            Separator();
            P(prefix + "Tex_UVMode", "UV Mode");
            DrawDecal(prefix);
            var decal = Prop(prefix + "TexIsDecal");
            bool showMainSt = !LayoutChoice(prefix + "-decal-st", decal != null && !decal.hasMixedValue && Number(decal) > 0.5f);
            if (showMainSt) ScaleOffset(prefix + "Tex");
            if (showMainSt && (!Searching || LabelOnly("ft.Offset"))) GUILayout.Space(2f);
            AngleSlider(prefix + "TexAngle");
            ScrollAndRotate(prefix + "Tex_ScrollRotate", false, true);
            Separator();
            Tex(prefix + "BlendMask", null, "sMask");
            if (!EverySelectedIsGem()) DissolveControls(prefix + "Dissolve", false);
            EndSection();
        }

        // lilToon UV4Decal: one Decal toggle, then Mirror Mode and Copy Mode. Turning Decal off clears the five flags.
        private void DrawDecal(string prefix)
        {
            var isDecal = Prop(prefix + "TexIsDecal");
            var left = Prop(prefix + "TexIsLeftOnly");
            var right = Prop(prefix + "TexIsRightOnly");
            var copy = Prop(prefix + "TexShouldCopy");
            var flipMirror = Prop(prefix + "TexShouldFlipMirror");
            var flipCopy = Prop(prefix + "TexShouldFlipCopy");
            if (isDecal == null || left == null || right == null || copy == null || flipMirror == null || flipCopy == null) return;
            if (!Searching || RowVisible(isDecal.name, "sAsDecal"))
            {
                Rect row = EditorGUILayout.GetControlRect();
                BeginScope(row, isDecal);
                try
                {
                    EditorGUI.showMixedValue = isDecal.hasMixedValue;
                    EditorGUI.BeginChangeCheck();
                    bool on = EditorGUI.Toggle(row, FairyToonL10n.Text("sAsDecal"), Number(isDecal) > 0.5f);
                    bool changed = EditorGUI.EndChangeCheck();
                    if (changed)
                    {
                        SetNumber(isDecal, on ? 1f : 0f);
                        if (!on)
                        {
                            SetNumber(left, 0f);
                            SetNumber(right, 0f);
                            SetNumber(flipMirror, 0f);
                            SetNumber(copy, 0f);
                            SetNumber(flipCopy, 0f);
                        }
                    }
                }
                finally
                {
                    EditorGUI.showMixedValue = false;
                    EndScope();
                }
            }
            bool decalRows = LayoutChoice(prefix + "-decal-rows", isDecal.hasMixedValue || Number(isDecal) > 0.5f);
            if (!decalRows) return;
            DrawMirrorMode(left, right, flipMirror);
            DrawCopyMode(copy, flipCopy);
            if (LayoutChoice(prefix + "-decal-place", !isDecal.hasMixedValue && Number(isDecal) > 0.5f))
                DecalPlacement(prefix + "Tex", copy, flipCopy);
        }

        private void DrawMirrorMode(MaterialProperty left, MaterialProperty right, MaterialProperty flip)
        {
            string label = Event.current.alt
                ? left.name + ", " + right.name + ", " + flip.name
                : FairyToonL10n.Text("sMirrorMode");
            if (Searching && !HitIgnore(label)) return;
            bool mixed = left.hasMixedValue || right.hasMixedValue || flip.hasMixedValue;
            int mode = 0;
            if (!mixed)
            {
                if (Number(right) > 0.5f) mode = 3;
                if (Number(flip) > 0.5f) mode++;
                if (Number(left) > 0.5f) mode = 2;
            }
            EditorGUI.showMixedValue = mixed;
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup(label, mixed ? -1 : mode, new[]
            {
                FairyToonL10n.Text("sMirrorModeNormal"),
                FairyToonL10n.Text("sMirrorModeFlip"),
                FairyToonL10n.Text("sMirrorModeLeft"),
                FairyToonL10n.Text("sMirrorModeRight"),
                FairyToonL10n.Text("sMirrorModeRightFlip")
            });
            EditorGUI.showMixedValue = false;
            if (!EditorGUI.EndChangeCheck() || next < 0) return;
            SetNumber(left, next == 2 ? 1f : 0f);
            SetNumber(right, next >= 3 ? 1f : 0f);
            SetNumber(flip, next == 1 || next == 4 ? 1f : 0f);
        }

        private void DrawCopyMode(MaterialProperty copy, MaterialProperty flipCopy)
        {
            string label = Event.current.alt ? copy.name + ", " + flipCopy.name : FairyToonL10n.Text("sCopyMode");
            if (Searching && !HitIgnore(label)) return;
            bool mixed = copy.hasMixedValue || flipCopy.hasMixedValue;
            int mode = 0;
            if (!mixed)
            {
                if (Number(copy) > 0.5f) mode = 1;
                if (Number(flipCopy) > 0.5f) mode = 2;
            }
            EditorGUI.showMixedValue = mixed;
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup(label, mixed ? -1 : mode, new[]
            {
                FairyToonL10n.Text("sCopyModeNormal"),
                FairyToonL10n.Text("sCopyModeSymmetry"),
                FairyToonL10n.Text("sCopyModeFlip")
            });
            EditorGUI.showMixedValue = false;
            if (!EditorGUI.EndChangeCheck() || next < 0) return;
            SetNumber(copy, next == 0 ? 0f : 1f);
            SetNumber(flipCopy, next == 2 ? 1f : 0f);
        }

        // Root dissolve is an advanced section after Outline and before Refraction/Fur, as in lilToon.
        private void Dissolve()
        {
            if (!PrefixMatches("_Dissolve") && !PrefixMatches("_FT_Dissolve") && !SearchAllows("|Mode", "|Shape", "|Threshold", "|Edge Width", "|Edge Color", "|Position", "|Direction", "|UV Line Angle (radians)", "|ft.DissolveOff", "|ft.DissolveMask", "|ft.DissolveUV", "|ft.DissolveObject", "|ft.DissolveRadial", "|ft.DissolveLinear", "|sDissolveMode")) return;
            if (!BeginSection("Dissolve", "sDissolve", null, false, PropertyBlock.Dissolve)) return;
            DissolveControls("_Dissolve", true);
            EndSection();
        }
        // Mirrors lilMainInspectorGUI's dissolve ordering; native Vector flags need no lilToon drawer dependency.
        private void DissolveControls(string prefix, bool wholeMaterial)
        {
            if (Prop(prefix + "Params") == null) return;
            if (!wholeMaterial) Separator();
            if (SelectedRenderingMode(FairyToonMaterialSetup.Fur))
            Help("Dissolve affects the Fur body only; Fur ribbon shells ignore whole-material and layer dissolve.", MessageType.Info);

            DissolveParameter(prefix + "Params", 0, "sDissolveMode", new[] { "ft.DissolveOff", "ft.DissolveMask", "ft.DissolveUV", "ft.DissolveObject" });
            var parameters = Prop(prefix + "Params");
            int mode = Mathf.RoundToInt(parameters.vectorValue.x);
            bool mixedMode = DissolveComponentMixed(parameters, 0);
            if (mode == 0 && !mixedMode) return;
            using (new EditorGUI.IndentLevelScope())
            {
                if (mode >= 2 || mixedMode)
                DissolveParameter(prefix + "Params", 1, "Shape", new[] { "ft.DissolveRadial", "ft.DissolveLinear" });
                // lilDissolve uses sliders for mask mode and unrestricted values for positional modes.
                bool maskOnly = mode == 1 && !mixedMode;
                DissolveParameter(prefix + "Params", 2, "Threshold", null, maskOnly, -1f, 2f);
                DissolveParameter(prefix + "Params", 3, "Edge Width", null, maskOnly, 0f, 1f);
                if (mode == 1 || mixedMode)
                {
                    Tex(prefix + "Mask", null, "Mask");
                    ScaleOffset(prefix + "Mask");
                }

                string noiseToggle = "_FT_" + prefix.Substring(1) + "UseNoise";
                var useNoise = Prop(noiseToggle);
                bool noNoise = useNoise != null && !useNoise.hasMixedValue && Number(useNoise) < 0.5f;
                parameters = Prop(prefix + "Params");
                int shape = Mathf.RoundToInt(parameters.vectorValue.y);
                bool mixedShape = DissolveComponentMixed(parameters, 1);
                if (mode >= 2 || mixedMode)
                {
                    string positionLabel = shape == 1 && !mixedShape ? "Direction" : "Position";
                    if (mixedMode || mixedShape || (mode == 2 && shape == 1 && useNoise != null && useNoise.hasMixedValue))
                    P(prefix + "Pos", "Position / Direction (XY UV, XYZ Object, W UV Angle)");
                    else if (mode == 2 && shape == 1 && !mixedShape && noNoise)
                    DissolveParameter(prefix + "Pos", 3, "UV Line Angle (radians)");
                    else
                    DissolvePosition(prefix + "Pos", positionLabel, mode == 2 ? 2 : 3);
                }

                P(noiseToggle, "Use Noise Helper (lilToon Feature)");
                useNoise = Prop(noiseToggle);
                if (useNoise == null || useNoise.hasMixedValue || Number(useNoise) > 0.5f)
                {
                    Tex(prefix + "NoiseMask", prefix + "NoiseStrength", "Noise / Strength");
                    ScaleOffset(prefix + "NoiseMask");
                    DissolvePosition(prefix + "NoiseMask_ScrollRotate", "Noise Scroll XY", 2);
                }
                if (useNoise != null && (useNoise.hasMixedValue || Number(useNoise) < 0.5f))
                Help("Disabled selects lilToon's no-noise helper. UV Linear then uses Position.w as a rotation angle; enabled uses the XY direction. The default matches lilToon's enabled noise feature even with an empty noise texture.", MessageType.Info);
                P(prefix + "Color", "Edge Color");
            }
        }

        private void DissolveParameter(string name, int component, string label, string[] options = null,
        bool slider = false, float minimum = 0f, float maximum = 1f)
        {
            var property = Prop(name);
            if (property == null) return;
            bool optionHit = false;
            if (Searching && options != null)
                for (int i = 0; i < options.Length; i++)
                    optionHit |= Hit(options[i]) || Hit(FairyToonL10n.Text(options[i]));
            if (!RowVisible(name, label) && !optionHit) return;
            Vector4 value = property.vectorValue;
            bool previousMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = DissolveComponentMixed(property, component);
            string shown = FairyToonL10n.Text(label);
            string[] localized = null;
            if (options != null)
            {
                localized = new string[options.Length];
                for (int i = 0; i < options.Length; i++) localized[i] = FairyToonL10n.Text(options[i]);
            }
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, property);
            try
            {
                EditorGUI.BeginChangeCheck();
                value[component] = options != null
                ? EditorGUI.Popup(row, shown, Mathf.RoundToInt(value[component]), localized)
                : slider ? EditorGUI.Slider(row, shown, value[component], minimum, maximum)
                : EditorGUI.FloatField(row, shown, value[component]);
                if (EditorGUI.EndChangeCheck()) SetDissolveComponents(property, value, 1 << component);
            }
            finally { EndScope(); }
            EditorGUI.showMixedValue = previousMixed;
        }

        private void DissolvePosition(string name, string label, int dimensions)
        {
            var scope = Prop(name);
            if (scope == null || !RowVisible(name, label)) return;
            Rect row = EditorGUILayout.GetControlRect();
            bool previousMixed = EditorGUI.showMixedValue;
            BeginScope(row, scope);
            try
            {
            Rect values = EditorGUI.PrefixLabel(row, new GUIContent(FairyToonL10n.Text(label)));
            using (var axes = new AxisFieldScope(values, dimensions))
            {
                for (int component = 0; component < dimensions; component++)
                {
                    // Refresh after each write, then edit only this axis across the selected materials.
                    var property = Prop(name);
                    Vector4 value = property.vectorValue;
                    EditorGUI.showMixedValue = DissolveComponentMixed(property, component);
                    EditorGUI.BeginChangeCheck();
                    value[component] = EditorGUI.FloatField(axes.Field(component), "XYZ"[component].ToString(), value[component]);
                    if (EditorGUI.EndChangeCheck()) SetDissolveComponents(property, value, 1 << component);
                }
            }
            }
            finally
            {
                EditorGUI.showMixedValue = previousMixed;
                EndScope();
            }
        }
        private static bool DissolveComponentMixed(MaterialProperty property, int component)
        {
            // The descriptor is the value on screen, including a property-block snapshot Unity already read in.
            // material.GetVector would mix that with the asset. Per-component mixed is not exposed, so any
            // mixed vector marks each component mixed. `component` stays in the signature for the callers.
            if (component < 0 || component > 3) return false;
            return property.hasMixedValue;
        }

        // MaterialProperty only accepts the whole vector. A shared descriptor would copy one target's
        // unedited components onto the others, so an asset edit merges the changed component into each material.
        // A callback is one renderer snapshot: merge into that snapshot and let the callback keep it.
        private void SetDissolveComponents(MaterialProperty property, Vector4 value, int components)
        {
            Vector4 merged = property.vectorValue;
            for (int component = 0; component < 4; component++)
                if ((components & (1 << component)) != 0) merged[component] = value[component];
            if (property.applyPropertyCallback != null)
            {
                // Returning true stops Unity writing this one vector onto every target. If the
                // callback did not keep the write, merge the edited axis into each material.
                bool handled = false;
                MaterialProperty.ApplyPropertyCallback original = property.applyPropertyCallback;
                s_ProbeDepth++;
                property.applyPropertyCallback = (changed, mask, previous) =>
                {
                    handled = original(changed, mask, previous);
                    return true;
                };
                try
                {
                    if (!property.hasMixedValue && property.vectorValue == merged)
                        handled = original(property, 1, property.vectorValue);
                    else property.vectorValue = merged;
                }
                finally
                {
                    property.applyPropertyCallback = original;
                    s_ProbeDepth--;
                }
                if (handled) return;
            }
            else if (property.targets == null || property.targets.Length <= 1)
            {
                property.vectorValue = merged;
                return;
            }
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Modify " + property.displayName);
            try
            {
                foreach (Object target in property.targets)
                {
                    var material = target as Material;
                    if (material == null || !material.HasProperty(property.name) || Locked(material, property.name)) continue;
                    Vector4 current = material.GetVector(property.name);
                    Vector4 next = current;
                    for (int component = 0; component < 4; component++)
                        if ((components & (1 << component)) != 0) next[component] = value[component];
                    if (next == current) continue;
                    Undo.RecordObject(material, "Modify " + property.displayName);
                    material.SetVector(property.name, next);
                    FairyToonMaterialSetup.Apply(material);
                }
            }
            finally { Undo.CollapseUndoOperations(group); }
        }

        private void Shadow()
        {

            if (!SearchAllows("_UseShadow", "_ShadowMaskType", "_ShadowStrengthMask", "_ShadowStrength", "_ShadowFlatBorder", "_ShadowFlatBlur", "_UseShadowRamp", "_ShadowRampTex", "_ShadowColorTex", "_ShadowColor", "_ShadowBorder", "_ShadowBlur", "_ShadowNormalStrength", "_ShadowReceive", "_CharShadowReceive", "_Shadow2ndColorTex", "_Shadow2ndColor", "_Shadow2ndBorder", "_Shadow2ndBlur", "_Shadow2ndNormalStrength", "_Shadow2ndReceive", "_Shadow3rdColorTex", "_Shadow3rdColor", "_Shadow3rdBorder", "_Shadow3rdBlur", "_Shadow3rdNormalStrength", "_Shadow3rdReceive", "_ShadowMainStrength", "_ShadowEnvStrength", "_ShadowBorderColor", "_ShadowBorderRange", "_ShadowBlurMask", "_BackfaceForceShadow", "_ShadowBorderMask", "_ShadowAOShift", "_ShadowAOShift2", "_ShadowPostAO", "_SSAOStrength", "_SSAOLitStrength", "_SSAOMask", "_ShadowStrengthMask|Face SDF", "_ShadowFlatBorder|SDF Offset", "_ShadowFlatBlur|SDF Y Scale", "_ShadowStrengthMask|Strength", "_ShadowFlatBorder|Flat Border", "_ShadowFlatBlur|Flat Blur", "_UseShadowRamp|Ramp (FairyToon)", "_ShadowRampTex|Ramp (U = half lambert)", "_ShadowColorTex|Color", "_CharShadowReceive|Character Shadow", "_Shadow2ndColorTex|Color (A = strength)", "_Shadow2ndBorder|Border", "_Shadow2ndBlur|Blur", "_Shadow2ndNormalStrength|Normal Strength", "_Shadow2ndReceive|Receive Shadow", "_Shadow3rdColorTex|Color (A = strength)", "_Shadow3rdBorder|Border", "_Shadow3rdBlur|Blur", "_Shadow3rdNormalStrength|Normal Strength", "_Shadow3rdReceive|Receive Shadow", "_ShadowBlurMask|Blur Mask", "_ShadowBorderMask|AO Map", "_ShadowAOShift|AO Shift (1st scale, offset, 2nd scale, offset)", "_ShadowAOShift2|AO Shift (3rd scale, offset)", "_SSAOStrength|Ambient Strength", "_SSAOLitStrength|Lit Strength", "_SSAOMask|Mask (white = SSAO on)", "|1st Min", "|1st Max", "|2nd Min", "|2nd Max", "|3rd Min", "|3rd Max")) return;
            if (!BeginSection("Shadow", "sShadowSetting", Prop("_UseShadow"), true, PropertyBlock.Shadow)) return;
            var maskType = Prop("_ShadowMaskType");
            P("_ShadowMaskType");
            bool faceMask = LayoutChoice("shadow-mask-face", maskType != null && !maskType.hasMixedValue && Mathf.RoundToInt(Number(maskType)) == 2);
            if (faceMask)
            {
                Tex("_ShadowStrengthMask", "_ShadowStrength", "Face SDF");
                Help("Face SDF in lilToon's format: R / G thresholds for light from either side, " +
                "B blend back to normal shading, A strength. Add FairyToonFaceShadow to the avatar root (import the map linear, uncompressed).",
                MessageType.None);
                P("_ShadowFlatBorder", "SDF Offset");
                P("_ShadowFlatBlur", "SDF Y Scale");
            }
            else
            {
                Tex("_ShadowStrengthMask", "_ShadowStrength", "sMaskStrength");
                P("_ShadowFlatBorder", "Flat Border");
                P("_ShadowFlatBlur", "Flat Blur");

            }

            ToggleRow("_UseShadowRamp", "Ramp (FairyToon)");
            bool ramp = LayoutChoice("shadow-ramp", Float("_UseShadowRamp") > 0.5f || Prop("_UseShadowRamp")?.hasMixedValue == true);
            if (ramp)
            {
                using (new EditorGUI.IndentLevelScope())
                Tex("_ShadowRampTex", null, "Ramp (U = half lambert)");
            }

            Separator();
            if (!ramp)
                TexWideColor("_ShadowColorTex", "_ShadowColor", "1st Color");
            EditorGUI.indentLevel += 2;
            P("_ShadowBorder");
            P("_ShadowBlur");
            P("_ShadowNormalStrength");
            P("_ShadowReceive");
            P("_CharShadowReceive", "Character Shadow");
            EditorGUI.indentLevel -= 2;

            if (!ramp)
            {
                if (!Searching || HitIgnore("2nd Shadow") || HitIgnore("2nd Color")) Separator();
                TexWideColor("_Shadow2ndColorTex", "_Shadow2ndColor", "2nd Color");
                EditorGUI.indentLevel += 2;
                ColorAlpha("_Shadow2ndColor");
                var shadow2 = Prop("_Shadow2ndColor");
                if (LayoutChoice("shadow2-on", shadow2 == null || shadow2.hasMixedValue || shadow2.colorValue.a > 0f))
                {
                    P("_Shadow2ndBorder", "Border");
                    P("_Shadow2ndBlur", "Blur");
                    P("_Shadow2ndNormalStrength", "Normal Strength");
                    P("_Shadow2ndReceive", "Receive Shadow");
                }
                EditorGUI.indentLevel -= 2;
                if (!Searching || HitIgnore("3rd Shadow") || HitIgnore("3rd Color")) Separator();
                TexWideColor("_Shadow3rdColorTex", "_Shadow3rdColor", "3rd Color");
                EditorGUI.indentLevel += 2;
                ColorAlpha("_Shadow3rdColor");
                var shadow3 = Prop("_Shadow3rdColor");
                if (LayoutChoice("shadow3-on", shadow3 == null || shadow3.hasMixedValue || shadow3.colorValue.a > 0f))
                {
                    P("_Shadow3rdBorder", "Border");
                    P("_Shadow3rdBlur", "Blur");
                    P("_Shadow3rdNormalStrength", "Normal Strength");
                    P("_Shadow3rdReceive", "Receive Shadow");
                }
                EditorGUI.indentLevel -= 2;
            }

            bool shadowTail = !Searching || RowVisible("_ShadowBorderColor", "Border Color") || RowVisible("_ShadowBorderRange", "Border Range") || RowVisible("_ShadowMainStrength", "Multiply Main Color") || RowVisible("_ShadowEnvStrength", "Environment Strength") || RowVisible("_ShadowBlurMask", "Blur Mask");
            bool shadowLayers = !Searching || HitIgnore("1st") || HitIgnore("2nd") || HitIgnore("3rd") || HitIgnore("Shadow");
            if (shadowLayers && shadowTail) Separator();
            P("_ShadowBorderColor");
            P("_ShadowBorderRange");
            if (!Searching || RowVisible("_ShadowBorderRange", "Border Range") || RowVisible("_ShadowEnvStrength", "Environment strength on shadow color"))
                Separator();
            P("_ShadowEnvStrength", "Environment strength on shadow color");
            P("_ShadowMainStrength");
            if (!Searching || HitIgnore("Blur Mask")) Separator();
            Tex("_ShadowBlurMask", null, "Blur Mask");
            if (!Searching || HitIgnore("AO")) Separator();

            string aoKey = PrefsPrefix + "Foldout.ShadowAO";
            bool aoNamed = Searching && (HitIgnore("1st Min") || HitIgnore("1st Max") || HitIgnore("2nd Min") || HitIgnore("2nd Max") || HitIgnore("3rd Min") || HitIgnore("3rd Max") || HitIgnore("AO Map"));
            bool aoOpen = FoldShown(aoKey, aoNamed);
            EditorGUI.indentLevel++;
            bool drewAo = Tex("_ShadowBorderMask", null, "AO Map", null, aoNamed);
            if (drewAo) SaveFoldClick(aoKey, aoOpen, DrawFoldArrow(aoOpen));
            EditorGUI.indentLevel--;
            if (aoOpen || aoNamed)
            {
                EditorGUI.indentLevel++;
                AoPair("_ShadowAOShift", 0, "1st Min", "1st Max");
                AoPair("_ShadowAOShift", 2, "2nd Min", "2nd Max");
                AoPair("_ShadowAOShift2", 0, "3rd Min", "3rd Max");
                P("_ShadowPostAO");
                P("_SSAOStrength", "Ambient Strength");
                P("_SSAOLitStrength", "Lit Strength");
                Tex("_SSAOMask", null, "Mask (white = SSAO on)");
                EditorGUI.indentLevel--;
            }
            EndSection();
        }

        private void AoPair(string name, int start, string minLabel, string maxLabel)
        {
            var property = Prop(name);
            if (property == null || property.propertyType != ShaderPropertyType.Vector) return;
            bool showMin = !Searching || HitIgnore(minLabel) || HitIgnore(property.name);
            bool showMax = !Searching || HitIgnore(maxLabel) || HitIgnore(property.name);
            if (!showMin && !showMax) return;
            Vector4 shown = FirstAoVector(property);
            AoEndpoints(shown[start], shown[start + 1], out float min, out float max);
            float minNext = min;
            float maxNext = max;
            bool minChanged = showMin && DrawAoEndpoint(property, minLabel, min, out minNext);
            bool maxChanged = showMax && DrawAoEndpoint(property, maxLabel, max, out maxNext);
            if (!minChanged && !maxChanged) return;
            WriteAoEndpoints(property, start, minChanged, minNext, maxChanged, maxNext);
        }

        private static Vector4 FirstAoVector(MaterialProperty property)
        {
            return property.vectorValue;
        }

        private static void WriteAoEndpoints(MaterialProperty property, int start, bool minEdited, float newMin, bool maxEdited, float newMax)
        {
            if (property.targets == null) return;
            if (property.applyPropertyCallback != null)
            {
                Vector4 preview = property.vectorValue;
                AoEndpoints(preview[start], preview[start + 1], out float min, out float max);
                if (minEdited) min = newMin;
                if (maxEdited) max = newMax;
                if (min == max) max = min + 0.001f;
                AoScaleOffset(min, max, out float scale, out float offset);
                preview[start] = scale;
                preview[start + 1] = offset;
                bool handled = false;
                MaterialProperty.ApplyPropertyCallback original = property.applyPropertyCallback;
                s_ProbeDepth++;
                property.applyPropertyCallback = (changed, mask, previous) =>
                {
                    handled = original(changed, mask, previous);
                    return true;
                };
                try { property.vectorValue = preview; }
                finally
                {
                    property.applyPropertyCallback = original;
                    s_ProbeDepth--;
                }
                if (handled) return;
            }
            Undo.RecordObjects(property.targets, "Shadow AO");
            foreach (Object target in property.targets)
            {
                var material = target as Material;
                if (material == null || !material.HasProperty(property.name) || Locked(material, property.name)) continue;
                Vector4 value = material.GetVector(property.name);
                AoEndpoints(value[start], value[start + 1], out float min, out float max);
                if (minEdited) min = newMin;
                if (maxEdited) max = newMax;
                if (min == max) max = min + 0.001f;
                AoScaleOffset(min, max, out float scale, out float offset);
                value[start] = scale;
                value[start + 1] = offset;
                material.SetVector(property.name, value);
            }
        }

        // Shader applies saturate(sample * scale + offset). The fields are the sample values that map to 0 and 1.
        private static void AoEndpoints(float scale, float offset, out float min, out float max)
        {
            if (Mathf.Abs(scale) < 1e-8f)
            {
                min = offset;
                max = offset;
                return;
            }
            min = -offset / scale;
            max = (1f - offset) / scale;
        }

        private static void AoScaleOffset(float min, float max, out float scale, out float offset)
        {
            float span = max - min;
            if (Mathf.Abs(span) < 1e-8f)
            {
                scale = 0f;
                offset = min;
                return;
            }
            scale = 1f / span;
            offset = -min * scale;
        }

        private bool DrawAoEndpoint(MaterialProperty property, string label, float shown, out float next)
        {
            next = shown;
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, property);
            try
            {
                EditorGUI.showMixedValue = property.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                string text = Event.current != null && Event.current.alt ? property.name : label;
                next = EditorGUI.Slider(row, text, shown, -0.01f, 1.01f);
                return EditorGUI.EndChangeCheck();
            }
            finally
            {
                EditorGUI.showMixedValue = false;
                EndScope();
            }
        }

        private void Emission(bool second)
        {

            string prefix = second ? "_Emission2nd" : "_Emission";
            if (!PrefixMatches(prefix, second ? null : "_Emission2nd") && !SearchAllows(second ? "_UseEmission2nd" : "_UseEmission", "|ft.Tiling", "|ft.Offset", "|sScroll", "|sRotate", "|sAngle", "|UV Animation", "|UV Setting")
                && !ComponentHit(prefix + "Map_ScrollRotate") && !ComponentHit(prefix + "BlendMask_ScrollRotate")) return;
            string prefs = second ? "Emission 2nd" : "Emission";
            string title = second ? "sEmission2nd" : "sEmissionSetting";
            var block = second ? PropertyBlock.Emission2nd : PropertyBlock.Emission1st;
            if (!BeginSection(prefs, title, Prop(second ? "_UseEmission2nd" : "_UseEmission"), false, block, false)) return;
            bool emissionUvSearch = Searching && (LabelOnly("ft.Tiling") || LabelOnly("ft.Offset") || LabelOnly("UV Mode") || LabelOnly("sScroll") || LabelOnly("sRotate") || LabelOnly("sAngle") || HitIgnore("Scroll") || HitIgnore("Angle") || HitIgnore("UV Setting") || HitIgnore("UV Animation") || ComponentHit(prefix + "Map_ScrollRotate"));
            string emissionUv = PrefsPrefix + "Foldout." + prefix + "UV";
            bool emissionOpen = FoldShown(emissionUv, emissionUvSearch);
            EditorGUI.indentLevel++;
            bool drewEmission = Tex(prefix + "Map", prefix + "Color", "Color / Mask", null, emissionUvSearch);
            if (drewEmission) SaveFoldClick(emissionUv, emissionOpen, DrawFoldArrow(emissionOpen));
            EditorGUI.indentLevel--;
            if (emissionOpen || emissionUvSearch)
            {
                EditorGUI.indentLevel++;
                bool settingHit = !Searching || HitIgnore("UV Setting") || LabelOnly("ft.Tiling") || LabelOnly("ft.Offset") || LabelOnly("sAngle") || HitIgnore("Angle") || HitIgnore(prefix + "Map_ScrollRotate.z");
                bool animHit = !Searching || HitIgnore("UV Animation") || LabelOnly("sScroll") || LabelOnly("sRotate") || LabelOnly("UV Mode") || HitIgnore(prefix + "Map_ScrollRotate.x") || HitIgnore(prefix + "Map_ScrollRotate.y") || HitIgnore(prefix + "Map_ScrollRotate.w");
                if (settingHit)
                {
                    EditorGUILayout.LabelField("UV Setting", EditorStyles.boldLabel);
                    ScaleOffset(prefix + "Map");
                    if (!Searching || LabelOnly("ft.Offset")) GUILayout.Space(2f);
                    ScrollAndRotate(prefix + "Map_ScrollRotate", true);
                }
                if (animHit)
                {
                    if (settingHit) Separator();
                    EditorGUILayout.LabelField("UV Animation", EditorStyles.boldLabel);
                    ScrollAndRotate(prefix + "Map_ScrollRotate", false);
                    P(prefix + "Map_UVMode", "UV Mode");
                }
                EditorGUI.indentLevel--;
            }
            ColorAlpha(prefix + "Color");
            P(prefix + "MainStrength", "sStrengthFromMain");
            Separator();
            bool maskTiling = Searching && (LabelOnly("ft.Tiling") || LabelOnly("ft.Offset"));
            string maskUv = PrefsPrefix + "Foldout." + prefix + "MaskUV";
            bool maskAnimHit = ComponentHit(prefix + "BlendMask_ScrollRotate");
            bool maskOpen = FoldShown(maskUv, maskTiling || maskAnimHit);
            EditorGUI.indentLevel++;
            bool drewMask = Tex(prefix + "BlendMask", prefix + "Blend", "sMask", null, maskTiling || maskAnimHit);
            if (drewMask) SaveFoldClick(maskUv, maskOpen, DrawFoldArrow(maskOpen));
            if (maskOpen || maskTiling || maskAnimHit)
            {
                bool maskSetting = !Searching || maskTiling || HitIgnore("UV Setting") || LabelOnly("sAngle") || HitIgnore(prefix + "BlendMask_ScrollRotate.z");
                bool maskAnim = !Searching || HitIgnore("UV Animation") || LabelOnly("sScroll") || LabelOnly("sRotate") || HitIgnore(prefix + "BlendMask_ScrollRotate.x") || HitIgnore(prefix + "BlendMask_ScrollRotate.y") || HitIgnore(prefix + "BlendMask_ScrollRotate.w");
                if (maskSetting)
                {
                    EditorGUILayout.LabelField("UV Setting", EditorStyles.boldLabel);
                    ScaleOffset(prefix + "BlendMask");
                    if (!Searching || LabelOnly("ft.Offset")) GUILayout.Space(2f);
                    ScrollAndRotate(prefix + "BlendMask_ScrollRotate", true);
                }
                if (maskAnim)
                {
                    if (maskSetting) Separator();
                    EditorGUILayout.LabelField("UV Animation", EditorStyles.boldLabel);
                    ScrollAndRotate(prefix + "BlendMask_ScrollRotate", false);
                }
            }
            EditorGUI.indentLevel--;
            P(prefix + "BlendMode", "sBlendMode");
            if (!Searching) Separator();
            BlinkVector(prefix + "Blink");
            if (!Searching) Separator();
            P(prefix + "UseGrad", "Gradation");
            var gradProp = Prop(prefix + "UseGrad");
            bool gradOpen = gradProp != null && LayoutChoice(prefix + "-grad", gradProp.hasMixedValue || Float(prefix + "UseGrad") > 0.5f);
            if (gradOpen)
            {
                Tex(prefix + "GradTex", null, "Gradation Texture");
                P(prefix + "GradSpeed", "Gradation Speed");
            }
            if (!Searching) Separator();
            P(prefix + "Fluorescence", "Fluorescence");

            EndSection();
        }

        private void BlinkVector(string name)
        {
            var property = Prop(name);
            if (property == null || property.propertyType != ShaderPropertyType.Vector) return;
            bool named = !Searching || HitIgnore(property.name) || HitIgnore("Blink Strength") || HitIgnore("Blink Type") || HitIgnore("Blink Speed") || HitIgnore("Blink Offset");
            if (!named) return;
            Vector4 value = property.vectorValue;
            bool alt = Event.current != null && Event.current.alt;
            Rect strengthRow = EditorGUILayout.GetControlRect();
            BeginScope(strengthRow, property);
            try
            {
                EditorGUI.showMixedValue = property.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                float strength = EditorGUI.Slider(strengthRow, alt ? name + ".x" : "Blink Strength", value.x, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    value = property.vectorValue;
                    value.x = strength;
                    SetDissolveComponents(property, value, 1);
                }
            }
            finally
            {
                EditorGUI.showMixedValue = false;
                EndScope();
            }
            bool more = LayoutChoice(name + "-blink", property.hasMixedValue || Mathf.Abs(property.vectorValue.x) > 0.0001f
                || (Searching && (HitIgnore("Blink Type") || HitIgnore("Blink Speed") || HitIgnore("Blink Offset"))));
            if (!more) return;
            value = property.vectorValue;
            Rect typeRow = EditorGUILayout.GetControlRect();
            BeginScope(typeRow, property);
            try
            {
                EditorGUI.showMixedValue = property.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                bool hard = EditorGUI.Toggle(typeRow, alt ? name + ".y" : "Blink Type", value.y > 0.5f);
                if (EditorGUI.EndChangeCheck())
                {
                    value = property.vectorValue;
                    value.y = hard ? 1f : 0f;
                    SetDissolveComponents(property, value, 2);
                }
            }
            finally
            {
                EditorGUI.showMixedValue = false;
                EndScope();
            }
            DrawBlinkPi(property, 2, alt ? name + ".z" : "Blink Speed");
            DrawBlinkPi(property, 3, alt ? name + ".w" : "Blink Offset");
        }

        private void DrawBlinkPi(MaterialProperty property, int component, string label)
        {
            Vector4 value = property.vectorValue;
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, property);
            try
            {
                EditorGUI.showMixedValue = property.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                float shown = value[component] / Mathf.PI;
                float next = EditorGUI.FloatField(row, label, shown);
                if (EditorGUI.EndChangeCheck())
                {
                    value = property.vectorValue;
                    value[component] = next * Mathf.PI;
                    SetDissolveComponents(property, value, 1 << component);
                }
            }
            finally
            {
                EditorGUI.showMixedValue = false;
                EndScope();
            }
        }

        private void Endfield()
        {

            if (!IrisMatches() && !SearchAllows("|ft.Tiling", "|ft.Offset", "_EFPart", "_EFEyeLight", "_SkinLUT", "_EFSSSColor", "_EFSSSStrength", "_ShadowStrengthMask", "_EFFaceSDFScale", "_RimColorTex", "_EFEyeMask", "_EFFaceHighlight", "_EFFaceHighlightColor", "_EFFaceHighlightShift", "_PBRMap", "_EFMetallic", "_EFSmoothness", "_EFReflectivity", "_EFOcclusion", "_EFCurvature", "_EFCurvatureScale", "_EFFabricSheen", "_DiffuseRamp", "_ShadowBorder", "_ShadowBlur", "_EFShadowDarkness", "_EFShadowDarker", "_EFShadowSaturation", "_EFBacklight", "_CharShadowReceive", "_ShadowReceive", "_BackfaceForceShadow", "_SSAOStrength", "_SSAOLitStrength", "_SSAOMask", "_EFMultiBounceAO", "_EFContactAO", "_EFContactAORadius", "_EFContactAOBias", "_HairMask", "_SpecRamp", "_EFHairCentre", "_EFHairBandOffset", "_EFHairLightFollow", "_EFHairFlatten", "_EFHairShift", "_EFHairExponent", "_EFHairSpecular", "_EFHairTopCut", "_EFHairShift2", "_EFHairExponent2", "_EFHairSpecular2", "_EFHairColor2", "_EFHairJitter", "_EFHairHalo", "_EFHairStrandBreakup", "_EFHairStrandBlur", "_EFHairStrandLines", "_EFHairStrandFreq", "_EFHairRootAO", "_EFHairRootLift", "_EFHairRootInner", "_EFHairRootOuter", "_EFHairRootColor", "_EFSpecular", "_EFSpecRampFresnel", "_EFEnvironment", "_EFEnvContrast", "_EFMetalGrazingColor", "_EFClearcoat", "_EFClearcoatSmoothness", "_GSAAStrength", "_ReflectionCubeTex", "_ReflectionCubeOverride", "_EFRimColor", "_EFRimStrength", "_EFRimWidth", "_EFRimAlbedo", "_EFLightRim", "_EFRimDepth", "_EFRimDepthWidth", "_EFRimDepthGap", "_EFEyeOverlay", "_EFEyeShadowColor", "_EFEyeScatterColor", "_EFIris", "_EFIrisDepth", "_EFPupilLayer", "_EFPupilDepth", "_EFIrisRefraction", "_EFIrisScale", "_EFIrisRim", "_EFIrisEdgeFade", "_EFIrisVertical", "_EFIrisMaxShift", "_EFIrisEdge", "_EFIrisEdgeSoftness", "_EFIrisGaze", "_EFCorneaBulge", "_EFCorneaNormalRange", "_EFCorneaIOR", "_EFIrisSparkleTex", "_EFIrisSparkle", "_EFIrisGlowStrength", "_EFIrisSparkleScale", "_EFIrisGlowColor", "_EFIrisFrame", "_EFIrisWorldRadius", "_EFIrisDish", "_EFIrisCenterL", "_EFIrisCenterR", "_EFIrisRadius", "_EFPupil", "_EFPupilCenterL", "_EFPupilCenterR", "_EFPupilRadius", "_EFPupilYRatio", "_EFPupilScale", "_EFPupilOffset", "_SkinLUT|Shadow LUT (no LUT: Shadow Tone)", "_SkinLUT|Shadow LUT (no LUT: Shadow Tone)", "_ShadowStrengthMask|Face SDF (lilToon format)", "_RimColorTex|Rim Mask (R)", "_EFEyeMask|Eye Mask (R: shades as Eye)", "_EFFaceHighlight|Highlight (R: nose tip, lips)", "_EFFaceHighlightShift|Highlight View Shift", "_PBRMap|PBR Map (R metal, G refl., B AO, A smooth)", "_EFReflectivity|Reflectivity (F0 = 0.04 x value)", "_EFOcclusion|Occlusion (map B)", "_DiffuseRamp|Ramp (RGB hue, A lit weight)", "_ShadowBorder|Border (no ramp)", "_ShadowBlur|Blur (no ramp)", "_CharShadowReceive|Character Shadow", "_ShadowReceive|Receive Scene Shadow", "_SSAOStrength|Ambient Strength", "_SSAOLitStrength|Lit Strength", "_SSAOMask|Mask (white = SSAO on)", "_HairMask|Hair Mask (R highlight, G strand noise, B AO)", "_SpecRamp|Band Colour Ramp (U band strength, V side)", "_SkinLUT|Shadow LUT (optional: warm hair, as skin)", "_EFHairShift|Shift", "_EFHairExponent|Sharpness", "_EFHairSpecular|Strength", "_EFHairTopCut|Top Cut", "_EFHairShift2|Shift", "_EFHairExponent2|Sharpness", "_EFHairSpecular2|Strength", "_EFHairColor2|Colour (A: 0 hair colour, 1 this colour)", "_EFHairStrandBlur|Blur (mip levels)", "_EFHairStrandFreq|Density (per UV unit)", "_EFHairRootLift|Lift (m)", "_EFHairRootInner|Inner (m)", "_EFHairRootOuter|Outer (m)", "_EFHairRootColor|Colour", "_SpecRamp|Specular Ramp", "_GSAAStrength|GSAA (at least 0.15)", "_ReflectionCubeTex|Cubemap Fallback (x ambient)")) return;
            bool mixedPart = Prop("_EFPart") != null && Prop("_EFPart").hasMixedValue;
            int part = Mathf.RoundToInt(Float("_EFPart"));
            bool eye = mixedPart || part == FairyToonMaterialSetup.PartEye;
            bool skin = mixedPart || part == FairyToonMaterialSetup.PartSkin;
            bool face = mixedPart || part == FairyToonMaterialSetup.PartFace;
            bool hair = mixedPart || part == FairyToonMaterialSetup.PartHair;
            bool other = mixedPart || (part != FairyToonMaterialSetup.PartEye && part != FairyToonMaterialSetup.PartFace);
            bool notHair = mixedPart || part != FairyToonMaterialSetup.PartHair;
            if (BeginSection("EFSurface", "ft.EFSurface", null, true, PropertyBlock.EndfieldSurface))
            {
                P("_EFPart");
                if (eye)
                {
                    SubHeader("Eye");
                    P("_EFEyeLight");
                    EyeOverlay();
                    Iris();
                    Help("Eyes ignore shadows and the light direction; they only dim a little when the light " +
                    "is behind the face. Use MatCap for the cornea highlight. Head vectors come from FairyToonFaceShadow.",
                    MessageType.None);
                    SubHeader("Surface");
                }
                if (skin)
                {
                    SubHeader("Skin");
                    Tex("_SkinLUT", null, "Shadow LUT (no LUT: Shadow Tone)");
                    P("_EFSSSColor");
                    P("_EFSSSStrength");
                    SubHeader("Surface");
                }
                if (face)
                {
                    SubHeader("Face");
                    Tex("_SkinLUT", null, "Shadow LUT (no LUT: Shadow Tone)");
                    P("_EFSSSColor");
                    P("_EFSSSStrength");
                    Tex("_ShadowStrengthMask", null, "Face SDF (lilToon format)");
                        P("_EFFaceSDFScale");
                        RimMaskEditor(null, "Rim Mask (R)");
                        Tex("_EFEyeMask", null, "Eye Mask (R: shades as Eye)");
                        P("_EFEyeLight");
                        EyeOverlay();
                        Iris();
                        Tex("_EFFaceHighlight", "_EFFaceHighlightColor", "Highlight (R: nose tip, lips)");
                        P("_EFFaceHighlightShift", "Highlight View Shift");
                        Help("Face SDF: R / G thresholds for light from either side, B blend back to the mesh " +
                        "shading (neck), as in lilToon. Head vectors come from FairyToonFaceShadow on the avatar root. " +
                        "SDF Light Scale: 0.5 for Endfield-style maps (the shadow sweeps over 0-180 degrees), 1 for maps made " +
                        "for lilToon (60-120 degrees).", MessageType.None);
                    SubHeader("Surface");
                }
                if (other) Iris();
                var pbr = Prop("_PBRMap");
                Tex("_PBRMap", null, "PBR Map (R metal, G refl., B AO, A smooth)");
                bool map = pbr != null && (pbr.hasMixedValue || pbr.textureValue != null);
                if (!map) P("_EFMetallic");
                P("_EFSmoothness", map ? "Smoothness (x map A)" : "Smoothness");
                P("_EFReflectivity", "Reflectivity (F0 = 0.04 x value)");
                P("_EFOcclusion", "Occlusion (map B)");
                if (mixedPart || (!face && !eye && !hair))
                {
                    P("_EFCurvature");
                    var curvature = Prop("_EFCurvature");
                    if (curvature != null && (curvature.hasMixedValue || Number(curvature) > 0f))
                        P("_EFCurvatureScale");
                }
                if (mixedPart || part == FairyToonMaterialSetup.PartCloth)
                    P("_EFFabricSheen");
                Help("Sun / shade state, top fill light, light override, terminator offset, directional ambient " +
                "and small lights (FairyToonCharacterLight) come from FairyToonCharacter on the avatar root.", MessageType.None);
                EndSection();
            }

            if (BeginSection("EFDiffuse", "ft.EFDiffuse", null, true, PropertyBlock.EndfieldDiffuse))
            {
                var ramp = Prop("_DiffuseRamp");
                Tex("_DiffuseRamp", null, "Ramp (RGB hue, A lit weight)");
                if (ramp != null && !ramp.hasMixedValue && ramp.textureValue == null)
                {
                    P("_ShadowBorder", "Border (no ramp)");
                    P("_ShadowBlur", "Blur (no ramp)");
                }
                P("_EFShadowDarkness");
                P("_EFShadowDarker");
                P("_EFShadowSaturation");
                P("_EFBacklight");
                P("_CharShadowReceive", "Character Shadow");
                if (face)
                Help("The face's SDF area ignores the character shadow; only the SDF's mesh blend (B: neck, " +
                "ears) takes it.", MessageType.None);
                P("_ShadowReceive", "Receive Scene Shadow");
                P("_BackfaceForceShadow");

                SubHeader("SSAO (FairyToon)");
                P("_SSAOStrength", "Ambient Strength");
                P("_SSAOLitStrength", "Lit Strength");
                Tex("_SSAOMask", null, "Mask (white = SSAO on)");
                P("_EFMultiBounceAO");
                if (!eye)
                {
                    P("_EFContactAO");
                    var contactAo = Prop("_EFContactAO");
                    if (contactAo != null && (contactAo.hasMixedValue || Number(contactAo) > 0f))
                    {
                        P("_EFContactAORadius");
                        P("_EFContactAOBias");
                    }
                }
                EndSection();
            }

            if (hair && BeginSection("EFHair", "ft.EFHair", null, true, PropertyBlock.EndfieldHair))
            {
                Tex("_HairMask", null, "Hair Mask (R highlight, G strand noise, B AO)");
                Tex("_SpecRamp", null, "Band Colour Ramp (U band strength, V side)");
                Tex("_SkinLUT", null, "Shadow LUT (optional: warm hair, as skin)");
                P("_EFHairCentre");
                P("_EFHairBandOffset");
                P("_EFHairLightFollow");
                P("_EFHairFlatten");
                SubHeader("Primary");
                P("_EFHairShift", "Shift");
                P("_EFHairExponent", "Sharpness");
                P("_EFHairSpecular", "Strength");
                P("_EFHairTopCut", "Top Cut");
                SubHeader("Secondary");
                P("_EFHairShift2", "Shift");
                P("_EFHairExponent2", "Sharpness");
                P("_EFHairSpecular2", "Strength");
                P("_EFHairColor2", "Colour (A: 0 hair colour, 1 this colour)");
                SubHeader("Strands");
                P("_EFHairJitter");
                P("_EFHairHalo");
                P("_EFHairStrandBreakup");
                var breakup = Prop("_EFHairStrandBreakup");
                if (breakup != null && (breakup.hasMixedValue || Number(breakup) > 0f)) P("_EFHairStrandBlur", "Blur (mip levels)");
                P("_EFHairStrandLines");
                var lines = Prop("_EFHairStrandLines");
                if (lines != null && (lines.hasMixedValue || Number(lines) > 0f)) P("_EFHairStrandFreq", "Density (per UV unit)");
                P("_EFHairRootAO");
                var rootAO = Prop("_EFHairRootAO");
                if (rootAO != null && (rootAO.hasMixedValue || Number(rootAO) > 0f))
                {
                    P("_EFHairRootLift", "Lift (m)");
                    P("_EFHairRootInner", "Inner (m)");
                    P("_EFHairRootOuter", "Outer (m)");
                    P("_EFHairRootColor", "Colour");
                }
                Help("Kajiya-Kay on a sphere around the head, so the band stays one horizontal ring tied to " +
                "the head. Head frame from FairyToonFaceShadow on the avatar root; Head Centre Height lifts the sphere " +
                "centre above the head bone. Root Occlusion multiplies hair near that centre (roots, inner layers) " +
                "by Root Colour, fading out between Inner and Outer; keep the colour warm or blonde turns olive.",MessageType.None);
                EndSection();
            }

            if (notHair && BeginSection("EFSpecular", "ft.EFSpecular", null, true, PropertyBlock.EndfieldSpecular))
            {
                P("_EFSpecular");
                Tex("_SpecRamp", null, "Specular Ramp");
                P("_EFSpecRampFresnel");
                P("_EFEnvironment");
                if (mixedPart || part == FairyToonMaterialSetup.PartCloth)
                {
                    P("_EFEnvContrast");
                    P("_EFMetalGrazingColor");
                    P("_EFClearcoat");
                    var clearcoat = Prop("_EFClearcoat");
                    if (clearcoat != null && (clearcoat.hasMixedValue || Number(clearcoat) > 0f))
                        P("_EFClearcoatSmoothness");
                }
                P("_GSAAStrength", "GSAA (at least 0.15)");
                Tex("_ReflectionCubeTex", null, "Cubemap Fallback (x ambient)");
                P("_ReflectionCubeOverride");

                EndSection();
            }

            if (BeginSection("EFRim", "ft.EFRim", null, false, PropertyBlock.EndfieldRim))
            {
                P("_EFRimColor");
                P("_EFRimStrength");
                P("_EFRimWidth");
                P("_EFRimAlbedo");
                P("_EFLightRim");
                P("_EFRimDepth");
                var rimDepth = Prop("_EFRimDepth");
                if (rimDepth != null && (rimDepth.hasMixedValue || Number(rimDepth) > 0f))
                {
                    P("_EFRimDepthWidth");
                    P("_EFRimDepthGap");
                }
                EndSection();
            }
        }

        private void NormalMaps()
        {

            bool firstNormal = SearchAllows("_UseBumpMap", "_BumpMap", "_BumpScale", "_BumpMap|Normal Map", "|ft.Tiling", "|ft.Offset");
            bool secondNormal = SearchAllows("_UseBump2ndMap", "_Bump2ndMap", "_Bump2ndScale", "_Bump2ndMap_UVMode", "_Bump2ndScaleMask", "_Bump2ndScaleMask|Mask & Strength", "|ft.Tiling", "|ft.Offset");
            if (!firstNormal && !secondNormal) return;
            if (firstNormal && BeginSection("Normal", "sNormalMapSetting", Prop("_UseBumpMap"), false, PropertyBlock.NormalMap1st, false))
            {
                bool normalTiling = Searching && (LabelOnly("ft.Tiling") || LabelOnly("ft.Offset"));
                string normalKey = PrefsPrefix + "Foldout.NormalUV";
                bool normalOpen = FoldShown(normalKey, normalTiling, true);
                EditorGUI.indentLevel++;
                bool drewNormal = Tex("_BumpMap", "_BumpScale", "Normal Map", null, normalTiling);
                if (drewNormal) SaveFoldClick(normalKey, normalOpen, DrawFoldArrow(normalOpen));
                if (normalOpen || normalTiling) ScaleOffset("_BumpMap");
                EditorGUI.indentLevel--;
                EndSection();
            }

            if (secondNormal && BeginSection("Normal2nd", "sNormalMap2nd", Prop("_UseBump2ndMap"), false, PropertyBlock.NormalMap2nd, false))
            {
            bool secondTiling = Searching && (LabelOnly("ft.Tiling") || LabelOnly("ft.Offset") || LabelOnly("UV Mode"));
            string secondKey = PrefsPrefix + "Foldout.Normal2ndUV";
            bool secondOpen = FoldShown(secondKey, secondTiling, true);
            EditorGUI.indentLevel++;
            bool drewSecond = Tex("_Bump2ndMap", "_Bump2ndScale", "Normal Map", null, secondTiling);
            if (drewSecond) SaveFoldClick(secondKey, secondOpen, DrawFoldArrow(secondOpen));
            if (secondOpen || secondTiling)
            {
                ScaleOffset("_Bump2ndMap");
                if (!Searching || LabelOnly("ft.Offset")) GUILayout.Space(2f);
                if (!Searching || RowVisible("_Bump2ndMap_UVMode", "UV Mode") || LabelOnly("UV Mode"))
                    P("_Bump2ndMap_UVMode");
                Separator();
            }
            string maskKey = PrefsPrefix + "Foldout.Normal2ndMask";
            bool maskOpen = FoldShown(maskKey, secondTiling, true);
            bool drewMask = Tex("_Bump2ndScaleMask", null, "Mask & Strength", null, secondTiling);
            if (drewMask) SaveFoldClick(maskKey, maskOpen, DrawFoldArrow(maskOpen));
            if (maskOpen || secondTiling) ScaleOffset("_Bump2ndScaleMask");
            EditorGUI.indentLevel--;
            EndSection();
            }
        }

        private void Anisotropy()
        {

            if (!SearchAllows("_UseAnisotropy", "_AnisotropyTangentMap", "_AnisotropyScale", "_AnisotropyScaleMask", "_AnisotropyTangentWidth", "_AnisotropyBitangentWidth", "_AnisotropyShift", "_AnisotropyShiftNoiseScale", "_AnisotropySpecularStrength", "_Anisotropy2ndTangentWidth", "_Anisotropy2ndBitangentWidth", "_Anisotropy2ndShift", "_Anisotropy2ndShiftNoiseScale", "_Anisotropy2ndSpecularStrength", "_AnisotropyShiftNoiseMask", "_Anisotropy2Reflection", "_Anisotropy2MatCap", "_Anisotropy2MatCap2nd", "_AnisotropyTangentMap|Tangent Map", "_AnisotropyScaleMask|Scale Mask", "_AnisotropyShiftNoiseMask|Shift Noise")) return;
            if (!BeginSection("Anisotropy", "sAnisotropy", Prop("_UseAnisotropy"), false, PropertyBlock.Anisotropy)) return;
            Tex("_AnisotropyTangentMap", "_AnisotropyScale", "Tangent Map");
            Tex("_AnisotropyScaleMask", null, "Scale Mask");
            ScaleOffset("_AnisotropyTangentMap");
            ScaleOffset("_AnisotropyScaleMask");
            SubHeader("1st Highlight");
            P("_AnisotropyTangentWidth");
            P("_AnisotropyBitangentWidth");
            P("_AnisotropyShift");
            P("_AnisotropyShiftNoiseScale");
            P("_AnisotropySpecularStrength");
            SubHeader("2nd Highlight");
            P("_Anisotropy2ndTangentWidth");
            P("_Anisotropy2ndBitangentWidth");
            P("_Anisotropy2ndShift");
            P("_Anisotropy2ndShiftNoiseScale");
            P("_Anisotropy2ndSpecularStrength");
            Tex("_AnisotropyShiftNoiseMask", null, "Shift Noise");
            ScaleOffset("_AnisotropyShiftNoiseMask");
            P("_Anisotropy2Reflection");
            P("_Anisotropy2MatCap");
            P("_Anisotropy2MatCap2nd");

            EndSection();
        }

        private void Backlight()
        {

            if (!SearchAllows("_UseBacklight", "_BacklightColorTex", "_BacklightColor", "_BacklightBorder", "_BacklightBlur", "_BacklightDirectivity", "_BacklightViewStrength", "_BacklightMainStrength", "_BacklightNormalStrength", "_BacklightReceiveShadow", "_BacklightBackfaceMask", "_BacklightColorTex|Color")) return;
            if (!BeginSection("Backlight", "sBacklightSetting", Prop("_UseBacklight"), false, PropertyBlock.Backlight)) return;
            Tex("_BacklightColorTex", "_BacklightColor", "Color");
            ScaleOffset("_BacklightColorTex");
            P("_BacklightBorder");
            P("_BacklightBlur");
            P("_BacklightDirectivity");
            P("_BacklightViewStrength");
            P("_BacklightMainStrength");
            P("_BacklightNormalStrength");
            P("_BacklightReceiveShadow");
            P("_BacklightBackfaceMask");

            EndSection();
        }

        private void SheerFabric()
        {

            if (!SearchAllows("_SheerAmount", "_SheerColor", "_SheerPower", "_SheerAlpha", "_SheerAmount|Sheer|Thin fabric such as stockings: the skin colour shows where the fabric faces the view.", "_SheerColor|Skin Color|Skin seen through the fabric (already dimmed by it).", "_SheerPower|Edge Falloff|Higher keeps the skin to the part facing the view; the edges show the fabric colour.", "_SheerAlpha|Min Alpha|Transparent materials: alpha where the fabric faces the view (most see-through); the edges stay opaque.")) return;
            if (!BeginSection("Sheer", "ft.Sheer", null, false, PropertyBlock.Sheer)) return;
            P("_SheerAmount", "Sheer", "Thin fabric such as stockings: the skin colour shows where the fabric faces the view.");
            var amount = Prop("_SheerAmount");
            if (amount != null && (Number(amount) > 0f || amount.hasMixedValue))
            {
                P("_SheerColor", "Skin Color", "Skin seen through the fabric (already dimmed by it).");
                P("_SheerPower", "Edge Falloff", "Higher keeps the skin to the part facing the view; the edges show the fabric colour.");
                P("_SheerAlpha", "Min Alpha", "Transparent materials: alpha where the fabric faces the view (most see-through); the edges stay opaque.");
            }
            EndSection();
        }

        private void Wetness(bool endfield)
        {

            if (!SearchAllows("_UseWet", "_WetPorosity", "_WetDarken", "_WetSaturation", "_WetHaze", "_WetSpecular", "_WetEdgeDarken", "_WetEdgeNoise", "_WetStreakAmount", "_WetStreakScale", "_WetStreakBump", "_WetStreakAxis", "_WetStreakDarken", "_WetStreakFlow", "_WetFilm", "_WetGlintAmount", "_WetSmoothness", "_WetFilmStretch", "_WetDropAmount", "_WetDropScale", "_WetPorosity|Porosity|Higher values soak and darken more; use low values for skin and metal.", "_WetDarken|Darken|Overall wet darkening, scaled by porosity.", "_WetSaturation|Saturation|Colour saturation of the wet area.", "_WetHaze|Dry Haze Loss|How much of the dry MatCap / rim sheen soaked fabric loses (what makes wet fabric read darker).", "_WetSpecular|Wet Specular|lilToon mode multiplier for the glints and droplets.", "_WetEdgeDarken|Front Darken|Dark band along the soaking edge.", "_WetEdgeNoise|Front Noise (m)|UV-stable height variation along the soaking edge.", "_WetStreakAmount|Coverage|Share of trail columns that are wet; columns cluster in groups.", "_WetStreakScale|Column Spacing (m)|Spacing of the trails around each limb, in metres.", "_WetStreakBump|Width|Trail width multiplier (1 = 1.5-6 mm).", "_WetStreakAxis|Wander|How much the trails meander as they run down (0 = straight).", "_WetStreakDarken|Darken / Opacity|How much darker and less sheer a soaked trail is.", "_WetStreakFlow|Fresh Flow (m/s)|Trails keep growing downwards for a few seconds after leaving water; rain keeps them running.", "_WetFilm|Glint Strength|Brightness of glints and droplets, in both shading modes.", "_WetGlintAmount|Glint Amount|Share of trail stretches still holding free water.", "_WetSmoothness|Glint Sharpness|Higher = thinner, sharper glints.", "_WetFilmStretch|Glint Stretch|Stretches glints along the limb.", "_WetDropAmount|Droplet Chance|Chance of a droplet per 9 mm of trail.", "_WetDropScale|Droplet Size (m)|Largest droplet radius in metres; the smallest are 40% of it.")) return;
            var toggle = Prop("_UseWet");
            if (toggle == null || !BeginSection("Wetness", "ft.Wetness", toggle, false, PropertyBlock.Wetness)) return;
            P("_WetPorosity", "Porosity", "Higher values soak and darken more; use low values for skin and metal.");
            P("_WetDarken", "Darken", "Overall wet darkening, scaled by porosity.");
            P("_WetSaturation", "Saturation", "Colour saturation of the wet area.");
            P("_WetHaze", "Dry Haze Loss", "How much of the dry MatCap / rim sheen soaked fabric loses (what makes wet fabric read darker).");
            if (!endfield) P("_WetSpecular", "Wet Specular", "lilToon mode multiplier for the glints and droplets.");
            P("_WetEdgeDarken", "Front Darken", "Dark band along the soaking edge.");
            P("_WetEdgeNoise", "Front Noise (m)", "UV-stable height variation along the soaking edge.");
            string detailsKey = PrefsPrefix + "Foldout.WetDetails";
            bool details = EditorGUILayout.Foldout(EditorPrefs.GetBool(detailsKey, false), FairyToonL10n.Text("Wet details"), true);
            EditorPrefs.SetBool(detailsKey, details);
            if (details)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.LabelField(FairyToonL10n.Text("Trails (soaked streaks along each limb bone)"), EditorStyles.miniBoldLabel);
                    P("_WetStreakAmount", "Coverage", "Share of trail columns that are wet; columns cluster in groups.");
                    P("_WetStreakScale", "Column Spacing (m)", "Spacing of the trails around each limb, in metres.");
                    P("_WetStreakBump", "Width", "Trail width multiplier (1 = 1.5-6 mm).");
                    P("_WetStreakAxis", "Wander", "How much the trails meander as they run down (0 = straight).");
                    P("_WetStreakDarken", "Darken / Opacity", "How much darker and less sheer a soaked trail is.");
                    P("_WetStreakFlow", "Fresh Flow (m/s)", "Trails keep growing downwards for a few seconds after leaving water; rain keeps them running.");
                    EditorGUILayout.LabelField(FairyToonL10n.Text("Glints (free water on the trails)"), EditorStyles.miniBoldLabel);
                    P("_WetFilm", "Glint Strength", "Brightness of glints and droplets, in both shading modes.");
                    P("_WetGlintAmount", "Glint Amount", "Share of trail stretches still holding free water.");
                    P("_WetSmoothness", "Glint Sharpness", "Higher = thinner, sharper glints.");
                    P("_WetFilmStretch", "Glint Stretch", "Stretches glints along the limb.");
                    EditorGUILayout.LabelField(FairyToonL10n.Text("Droplets"), EditorStyles.miniBoldLabel);
                    P("_WetDropAmount", "Droplet Chance", "Chance of a droplet per 9 mm of trail.");
                    P("_WetDropScale", "Droplet Size (m)", "Largest droplet radius in metres; the smallest are 40% of it.");
                }
            }
            EndSection();
        }

        private void Reflection()
        {

            if (!SearchAllows("_UseReflection", "_SmoothnessTex", "_Smoothness", "_MetallicGlossMap", "_Metallic", "_Reflectance", "_GSAAStrength", "_ApplySpecular", "_SpecularToon", "_SpecularBorder", "_SpecularBlur", "_SpecularNormalStrength", "_ApplyReflection", "_ReflectionNormalStrength", "_ReflectionCubeTex", "_ReflectionCubeColor", "_ReflectionCubeEnableLighting", "_ReflectionCubeOverride", "_ReflectionColorTex", "_ReflectionColor", "_ReflectionBlendMode", "_ReflectionApplyTransparency", "_SmoothnessTex|Smoothness", "_MetallicGlossMap|Metallic", "_ApplySpecular|Specular", "_SpecularToon|Toon", "_SpecularBorder|Border", "_SpecularBlur|Blur", "_SpecularNormalStrength|Normal Strength", "_ApplyReflection|sApplyReflection", "_ReflectionNormalStrength|Normal Strength", "_ReflectionCubeTex|Cubemap Fallback", "_ReflectionCubeEnableLighting|Cubemap Enable Lighting", "_ReflectionColorTex|Color")) return;
            if (!BeginSection("Reflection", "sReflection", Prop("_UseReflection"), false, PropertyBlock.Reflection)) return;
            Tex("_SmoothnessTex", "_Smoothness", "Smoothness");
            ScaleOffset("_SmoothnessTex");
            Tex("_MetallicGlossMap", "_Metallic", "Metallic");
            ScaleOffset("_MetallicGlossMap");
            P("_Reflectance");
            P("_GSAAStrength");

            ToggleRow("_ApplySpecular", "Specular");
            var applySpecular = Prop("_ApplySpecular");
            bool specularOn = applySpecular != null && LayoutChoice("refl-spec", applySpecular.hasMixedValue || Number(applySpecular) > 0.5f);
            if (specularOn)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    P("_SpecularToon", "Toon");
                    P("_SpecularBorder", "Border");
                    P("_SpecularBlur", "Blur");
                    P("_SpecularNormalStrength", "Normal Strength");
                }
            }
            ToggleRow("_ApplyReflection", "sApplyReflection");
            var applyReflection = Prop("_ApplyReflection");
            bool reflectionOn = applyReflection != null && LayoutChoice("refl-on", applyReflection.hasMixedValue || Number(applyReflection) > 0.5f);
            if (reflectionOn)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    P("_ReflectionNormalStrength", "Normal Strength");
                    Tex("_ReflectionCubeTex", "_ReflectionCubeColor", "Cubemap Fallback");
                    P("_ReflectionCubeEnableLighting", "Cubemap Enable Lighting");
                    P("_ReflectionCubeOverride");
                }
            }
            SubHeader("Color");
            Tex("_ReflectionColorTex", "_ReflectionColor", "Color");
            ScaleOffset("_ReflectionColorTex");
            P("_ReflectionBlendMode");
            P("_ReflectionApplyTransparency");

            EndSection();
        }

        private void MatCap()
        {

            bool firstCap = PrefixMatches("_MatCap", "_MatCap2nd") || SearchAllows("_UseMatCap", "|ft.Tiling", "|ft.Offset", "|sZRotCancel", "|sFixPerspective", "|sCustomNormal");
            bool secondCap = PrefixMatches("_MatCap2nd") || SearchAllows("_UseMatCap2nd", "|ft.Tiling", "|ft.Offset", "|sZRotCancel", "|sFixPerspective", "|sCustomNormal");
            if (!firstCap && !secondCap) return;
            if (firstCap && BeginSection("MatCap", "sMatCap", Prop("_UseMatCap"), false, PropertyBlock.MatCap1st, false))
            {
                MatCapLayer("");
                EndSection();
            }
            // (2nd works without the 1st: either one turns on _FT_MATCAP)
            if (secondCap && BeginSection("MatCap2nd", "sMatCap2nd", Prop("_UseMatCap2nd"), false, PropertyBlock.MatCap2nd, false))
            {
                MatCapLayer("2nd");
                EndSection();
            }
        }

        // (layer "" = 1st, "2nd" = 2nd; the property sets are identical apart from the infix)
        private void MatCapLayer(string layer)
        {
            string n = "_MatCap" + layer;
            bool matCapTiling = Searching && (LabelOnly("ft.Tiling") || LabelOnly("ft.Offset") || LabelOnly("sZRotCancel") || LabelOnly("sFixPerspective"));
            string uvKey = PrefsPrefix + "Foldout.MatCapUV" + layer;
            bool uvOpen = FoldShown(uvKey, matCapTiling);
            EditorGUI.indentLevel++;
            bool drewMatCap = Tex(n + "Tex", n + "Color", "sMatCap", null, matCapTiling);
            if (drewMatCap) SaveFoldClick(uvKey, uvOpen, DrawFoldArrow(uvOpen));
            EditorGUI.indentLevel--;
            if (uvOpen || (Searching && (LabelOnly("ft.Tiling") || LabelOnly("ft.Offset") || LabelOnly("sZRotCancel") || LabelOnly("sFixPerspective"))))
            {
                EditorGUI.indentLevel++;
                ScaleOffset(n + "Tex");
                if (!Searching || LabelOnly("ft.Offset")) GUILayout.Space(2f);
                P(n + "ZRotCancel", "sZRotCancel");
                P(n + "Perspective", "sFixPerspective");
                EditorGUI.indentLevel--;
            }
            ColorAlpha(n + "Color");
            P(n + "MainStrength", "sStrengthFromMain");
            P(n + "NormalStrength", "sNormalStrength");
            Separator();
            bool maskTiling = Searching && (LabelOnly("ft.Tiling") || LabelOnly("ft.Offset"));
            string maskKey = PrefsPrefix + "Foldout.MatCapMask" + layer;
            bool maskOpen = FoldShown(maskKey, maskTiling);
            EditorGUI.indentLevel++;
            bool drewMask = maskTiling
                ? Tex(n + "BlendMask", n + "Blend", "sMask", null, true)
                : Tex(n + "BlendMask", n + "Blend", "sMask");
            if (drewMask) SaveFoldClick(maskKey, maskOpen, DrawFoldArrow(maskOpen));
            EditorGUI.indentLevel--;
            if (maskOpen || maskTiling)
            {
                EditorGUI.indentLevel++;
                ScaleOffset(n + "BlendMask");
                EditorGUI.indentLevel--;
            }
            P(n + "EnableLighting", "sEnableLighting");
            P(n + "ShadowMask", "sShadowMask");
            P(n + "BackfaceMask", "sBackfaceMask");
            P(n + "Lod", "sBlur");
            P(n + "BlendMode", "sBlendMode");
            if (ShowsApplyTransparency()) P(n + "ApplyTransparency", "sApplyTransparency");
            if (!Searching || LabelOnly("sCustomNormal")) Separator();
            P(n + "CustomNormal", "sCustomNormal");
            var customNormal = Prop(n + "CustomNormal");
            bool customOn = customNormal != null && LayoutChoice(n + "-custom", customNormal.hasMixedValue || Number(customNormal) > 0.5f);
            if (customOn)
            {
                bool normalTiling = Searching && (LabelOnly("ft.Tiling") || LabelOnly("ft.Offset"));
                string normalKey = PrefsPrefix + "Foldout.MatCapNormal" + layer;
                bool normalOpen = FoldShown(normalKey, normalTiling);
                EditorGUI.indentLevel++;
                bool drewNormal = Tex(n + "BumpMap", n + "BumpScale", "sNormalMap", null, normalTiling);
                if (drewNormal) SaveFoldClick(normalKey, normalOpen, DrawFoldArrow(normalOpen));
                EditorGUI.indentLevel--;
                if (normalOpen || (Searching && (LabelOnly("ft.Tiling") || LabelOnly("ft.Offset"))))
                {
                    EditorGUI.indentLevel++;
                    ScaleOffset(n + "BumpMap");
                    EditorGUI.indentLevel--;
                }
            }
        }

        private void RimMaskEditor(string extra, string label)
        {
            // lilToon indents the texture row, then folds Tiling/Offset from that same row.
            EditorGUI.indentLevel++;
            bool tiling = Searching && (LabelOnly("ft.Tiling") || LabelOnly("ft.Offset"));
            string key = PrefsPrefix + "Foldout.RimUV." + label;
            bool open = FoldShown(key, tiling);
            bool drew = Tex("_RimColorTex", extra, label, "Color (RGB) and Transparency (A)", tiling);
            if (drew) SaveFoldClick(key, open, DrawFoldArrow(open));
            if (open || tiling)
            {
                ScaleOffset("_RimColorTex");
                if (!Searching || LabelOnly("ft.Offset")) GUILayout.Space(2f);
            }
            EditorGUI.indentLevel--;
        }

        private void RimLight()
        {

            if (!SearchAllows("_UseRim", "_RimColorTex", "_RimColor", "_RimBorder", "_RimBlur", "_RimFresnelPower", "_RimShadowMask", "_RimMainStrength", "_RimNormalStrength", "_RimEnableLighting", "_RimBackfaceMask", "_RimApplyTransparency", "_RimBlendMode", "_RimDirStrength", "_RimDirRange", "_RimIndirRange", "_RimIndirColor", "_RimIndirBorder", "_RimIndirBlur", "_RimColorTex|Color / Mask", "|ft.Tiling", "|ft.Offset", "|Color (RGB) and Transparency (A)")) return;
            if (!BeginSection("Rim", "sRimLightSetting", Prop("_UseRim"), false, PropertyBlock.RimLight)) return;
            RimMaskEditor("_RimColor", "Color / Mask");
            ColorAlpha("_RimColor");
            P("_RimMainStrength", "sStrengthFromMain");
            P("_RimEnableLighting", "sEnableLighting");
            P("_RimShadowMask", "sShadowMask");
            P("_RimBackfaceMask", "sBackfaceMask");
            if (ShowsApplyTransparency())
                P("_RimApplyTransparency", "sApplyTransparency");
            P("_RimBlendMode", "sBlendMode");
            Separator();
            P("_RimDirStrength", "sLightDirection");
            bool directed = LayoutChoice("rim-dir", Float("_RimDirStrength") != 0f || Prop("_RimDirStrength").hasMixedValue);
            if (directed)
            {
                EditorGUI.indentLevel++;
                P("_RimDirRange");
                InvBorder("_RimBorder");
                P("_RimBlur", "sBlur");
                Separator();
                P("_RimIndirRange");
                P("_RimIndirColor");
                InvBorder("_RimIndirBorder");
                P("_RimIndirBlur");
                EditorGUI.indentLevel--;
                Separator();
            }
            else
            {
                InvBorder("_RimBorder");
                P("_RimBlur", "sBlur");
            }
            P("_RimNormalStrength", "sNormalStrength");
            P("_RimFresnelPower", "sFresnelPower");

            EndSection();
        }

        private void RimShade()
        {

            if (!SearchAllows("_UseRimShade", "_RimShadeMask", "_RimShadeColor", "_RimShadeBorder", "_RimShadeBlur", "_RimShadeFresnelPower", "_RimShadeNormalStrength", "_RimShadeMask|Color / Mask")) return;
            if (!BeginSection("RimShade", "sRimShadeSetting", Prop("_UseRimShade"), false, PropertyBlock.RimShade)) return;
            Tex("_RimShadeMask", "_RimShadeColor", "Color / Mask");
            P("_RimShadeNormalStrength", "sNormalStrength");
            P("_RimShadeBorder", "sBorder");
            P("_RimShadeBlur", "sBlur");
            P("_RimShadeFresnelPower", "sFresnelPower");
            EndSection();
        }

        private void Glitter()
        {

            if (!SearchAllows("_UseGlitter", "_GlitterUVMode", "_GlitterColorTex", "_GlitterColor", "_GlitterColorTex_UVMode", "_GlitterParams1", "_GlitterParams2", "_GlitterScaleRandomize", "_GlitterSensitivity", "_GlitterPostContrast", "_GlitterMainStrength", "_GlitterNormalStrength", "_GlitterEnableLighting", "_GlitterShadowMask", "_GlitterBackfaceMask", "_GlitterApplyTransparency", "_GlitterVRParallaxStrength", "_GlitterApplyShape", "_GlitterShapeTex", "_GlitterAtras", "_GlitterAngleRandomize", "_GlitterColorTex|Color", "_GlitterParams1|Tiling XY / Size Squared Z / Contrast W", "_GlitterParams2|Speed X / Angle Y / Light Direction Z / Color Randomness W", "_GlitterApplyShape|Particle Shape", "_GlitterShapeTex|Shape", "_GlitterAtras|Shape Atlas (columns X / rows Y)")) return;
            if (!BeginSection("Glitter", "sGlitterSetting", Prop("_UseGlitter"), false, PropertyBlock.Glitter)) return;
            P("_GlitterUVMode");
            Tex("_GlitterColorTex", "_GlitterColor", "Color");
            P("_GlitterColorTex_UVMode");
            P("_GlitterParams1", "Tiling XY / Size Squared Z / Contrast W");
            P("_GlitterParams2", "Speed X / Angle Y / Light Direction Z / Color Randomness W");
            P("_GlitterScaleRandomize");
            P("_GlitterSensitivity");
            P("_GlitterPostContrast");
            ScaleOffset("_GlitterColorTex");
            P("_GlitterMainStrength");
            P("_GlitterNormalStrength");
            P("_GlitterEnableLighting");
            P("_GlitterShadowMask");
            P("_GlitterBackfaceMask");
            P("_GlitterApplyTransparency");
            P("_GlitterVRParallaxStrength");
            ToggleRow("_GlitterApplyShape", "Particle Shape");
            if (Float("_GlitterApplyShape") > 0.5f || Prop("_GlitterApplyShape").hasMixedValue)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    Tex("_GlitterShapeTex", null, "Shape");
                    ScaleOffset("_GlitterShapeTex");
                    P("_GlitterAtras", "Shape Atlas (columns X / rows Y)");
                    P("_GlitterAngleRandomize");
                }
            }

            EndSection();
        }

        private void Outline()
        {

            if (!SearchAllows("_UseOutline", "_ShadingModel", "_EFOutlineColor", "_EFOutlineSaturation", "_EFOutlineEdge", "_EFOutlineFlatten", "_EFOutlinePull", "_OutlineTex", "_OutlineColor", "_OutlineMode", "_OutlineWidthMask", "_OutlinePixelWidth", "_OutlineMinPixels", "_OutlineFadeStart", "_OutlineFadeEnd", "_OutlineWidth", "_OutlineFixWidth", "_OutlineVertexR2Width", "_OutlineCornerTaper", "_OutlineZBias", "_OutlineEnableLighting", "|ft.Tiling", "|ft.Offset", "_EFOutlineColor|Tint (x local shadow colour)", "_EFOutlineSaturation|Saturation", "_EFOutlineEdge|Edge Tint (A strength)", "_EFOutlineFlatten|Flatten (towards mid grey)", "_EFOutlinePull|Tint Pull (A amount)", "_OutlineTex|Color", "_OutlineWidthMask|Width (px at 1080p)", "_OutlineWidthMask|Width (cm)")) return;
            if (!BeginSection("Outline", "sOutlineSetting", Prop("_UseOutline"), false, PropertyBlock.Outline)) return;
            bool mixedShade = Prop("_ShadingModel") != null && Prop("_ShadingModel").hasMixedValue;
            bool endfield = mixedShade || Float("_ShadingModel") > 0.5f;
            bool toonOutline = mixedShade || Float("_ShadingModel") <= 0.5f;
            if (endfield)
            {
                P("_EFOutlineColor", "Tint (x local shadow colour)");
                P("_EFOutlineSaturation", "Saturation");
                P("_EFOutlineEdge", "Edge Tint (A strength)");
                P("_EFOutlineFlatten", "Flatten (towards mid grey)");
                P("_EFOutlinePull", "Tint Pull (A amount)");
            }
            if (toonOutline)
            {
                string outlineUv = PrefsPrefix + "Foldout.OutlineUV";
                bool outlineForce = Searching && (LabelOnly("ft.Tiling") || LabelOnly("ft.Offset"));
                bool outlineOpen = FoldShown(outlineUv, outlineForce);
                EditorGUI.indentLevel++;
                bool drewOutline = Tex("_OutlineTex", "_OutlineColor", "sColor");
                if (drewOutline) SaveFoldClick(outlineUv, outlineOpen, DrawFoldArrow(outlineOpen));
                if (outlineOpen || outlineForce) ScaleOffset("_OutlineTex");
                EditorGUI.indentLevel--;
                P("_OutlineEnableLighting", "sEnableLighting");
            }
            P("_OutlineMode", "Width Mode");
            bool mixedOutline = Prop("_OutlineMode") != null && Prop("_OutlineMode").hasMixedValue;
            bool screenWidth = mixedOutline || Float("_OutlineMode") > 0.5f;
            if (screenWidth)
                Tex("_OutlineWidthMask", "_OutlinePixelWidth", "sMaskWidth");
            if (mixedOutline || Float("_OutlineMode") <= 0.5f)
                Tex("_OutlineWidthMask", "_OutlineWidth", "sMaskWidth");
            EditorGUI.indentLevel++;
            P("_OutlineFixWidth", "sFixWidth");
            P("_OutlineVertexR2Width", "sVertexColor");
            P("_OutlineZBias", "sZBias");
            EditorGUI.indentLevel--;
            if (screenWidth)
            {
                P("_OutlineMinPixels");
                P("_OutlineFadeStart");
                P("_OutlineFadeEnd");
            }
            var vertexWidth = Prop("_OutlineVertexR2Width");
            if (vertexWidth != null && (vertexWidth.hasMixedValue || Mathf.Approximately(Number(vertexWidth), 3f)))
            {
                P("_OutlineCornerTaper");
                Help("Reads a tangent-space smoothed normal from UV7 (W: coherence, low at hard corners). " +
                "Bake it with Assets > FairyToon > Bake Smooth Normals (UV7) on the model.", MessageType.None);
            }

            EndSection();
        }

        private void Refraction()
        {

            bool blurOn = Float("_RefractionBlur") > 0.5f || Prop("_RefractionBlur")?.hasMixedValue == true;
            bool blurTiling = blurOn && Searching && (LabelOnly("ft.Tiling") || LabelOnly("ft.Offset"));
            if (!blurTiling && !SearchAllows("_RefractionStrength", "_RefractionFresnelPower", "_RefractionColorFromMain", "_RefractionColor", "_RefractionBlur", "_RefractionStrength|Strength", "_RefractionFresnelPower|Fresnel Power", "_RefractionColorFromMain|sColorFromMain", "_RefractionColor|Color", "_RefractionBlur|Blur")
                && !(blurOn && SearchAllows("_SmoothnessTex", "_Smoothness", "_SmoothnessTex|Blur Smoothness"))) return;
            if (!BeginSection("Refraction", "sRefraction", null, true, PropertyBlock.Refraction)) return;
            P("_RefractionStrength", "sStrength");
            P("_RefractionFresnelPower", "sFresnel");
            P("_RefractionColorFromMain", "sColorFromMain");
            P("_RefractionColor", "Color");
            P("_RefractionBlur", "Blur");
            if (blurOn)
            {
                bool tiling = Searching && (LabelOnly("ft.Tiling") || LabelOnly("ft.Offset"));
                Tex("_SmoothnessTex", "_Smoothness", "Blur Smoothness", null, tiling);
                ScaleOffset("_SmoothnessTex");
            }
            EndSection();
        }

        private void Fur()
        {
            var furMode = Prop("_FurMode");
            bool cutoutLength = furMode != null && (furMode.hasMixedValue || Number(furMode) > 1.5f);
            bool furRows = SearchAllows("_FurMode|ft.FurMode", "_FurNoiseMask", "_FurMask", "_FurLengthMask", "_FurVectorTex", "_FurVectorScale", "_FurVector", "_VertexColor2FurVector", "_FurGravity", "_FurRandomize", "_FurAO", "_FurLayerNum", "_FurRootOffset", "_FurRimColor", "_FurRimFresnelPower", "_FurRimAntiLight", "_FurVectorTex|sVectorTex", "_FurVectorTex|sNormalMap", "_FurLengthMask|sLengthMask", "_FurVector|sFurVector", "_VertexColor2FurVector|sVertexColor2Vector", "_FurGravity|sGravity", "_FurRandomize|sRandomize", "_FurNoiseMask|sNoise", "_FurMask|sAlphaMask", "_FurAO|sAmbientOcclusion", "_FurLayerNum|sLayerNum", "_FurRootOffset|sRootWidth", "_FurCutoutLength|sCutoutLength", "_FurRimColor|Color", "_FurRimFresnelPower|sFresnelPower", "_FurRimAntiLight|Anti Light", "|ft.Tiling", "|ft.Offset", "|Direction (XYZ)", "|Length (W)");
            if (!furRows && !(cutoutLength && SearchAllows("_FurCutoutLength"))) return;
            if (!BeginSection("Fur", "sFurSetting", null, true, PropertyBlock.Fur)) return;
            FurMode();
            Help("Fur does not render Outline or See-through Hair. Their settings are kept when switching modes.", MessageType.None);
            Tex("_FurVectorTex", "_FurVectorScale", "sNormalMap");
            Tex("_FurLengthMask", null, "sLengthMask");
            FurVectorAndLength();
            if (cutoutLength)
                P("_FurCutoutLength", "sCutoutLength");
            P("_VertexColor2FurVector", "sVertexColor2Vector");
            P("_FurGravity", "sGravity");
            P("_FurRandomize", "sRandomize");
            // lilToon draws these lines even while search hides the rows between them, so a Vector
            // query stacks the layer line and the Rim Light line directly above the Rim Light label.
            Separator();
            Tex("_FurNoiseMask", null, "sNoise");
            ScaleOffset("_FurNoiseMask");
            Tex("_FurMask", null, "sAlphaMask");
            P("_FurAO", "sAmbientOcclusion");
            Separator();
            P("_FurLayerNum", "sLayerNum");
            MinusRange("_FurRootOffset", "sRootWidth");
            Separator();
            EditorGUILayout.LabelField(FairyToonL10n.Text("sRimLight"), EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            P("_FurRimColor", "Color");
            P("_FurRimFresnelPower", "Fresnel Power");
            P("_FurRimAntiLight", "Anti Light");
            EditorGUI.indentLevel--;
            EndSection();
        }

        private void Gem()
        {

            if (!SearchAllows("_RefractionStrength", "_RefractionFresnelPower", "_GemChromaticAberration", "_GemEnvContrast", "_GemEnvColor", "_GemParticleLoop", "_GemParticleColor", "_GemVRParallaxStrength", "_SmoothnessTex", "_Smoothness", "_Reflectance", "_ReflectionCubeOverride", "_ReflectionCubeTex", "_ReflectionCubeColor", "_ReflectionCubeEnableLighting", "|sFallbackLighting", "|sEnvironmentColor", "|sCubemapFallback", "|Cubemap Color", "|sFresnel", "_RefractionStrength|Refraction Strength", "_RefractionFresnelPower|Refraction Fresnel Power", "_GemChromaticAberration|Chromatic Aberration", "_GemEnvContrast|Environment Contrast", "_GemEnvColor|Environment Color", "_GemParticleLoop|Particle Loop", "_GemParticleColor|Particle Color", "_GemVRParallaxStrength|VR Parallax Strength", "_SmoothnessTex|Smoothness", "_Reflectance|Reflectance", "_ReflectionCubeOverride|Override Cubemap", "_ReflectionCubeTex|Reflection Cubemap")) return;
            if (!BeginSection("Gem", "sGem", null, true, PropertyBlock.Gem)) return;
            bool showRefraction = !Searching || RowVisible("_RefractionStrength", "sStrength") || RowVisible("_RefractionFresnelPower", "sFresnel") || HitIgnore("Refraction");
            bool showGemBody = !Searching || RowVisible("_GemChromaticAberration", "sChromaticAberration") || RowVisible("_GemEnvContrast", "sContrast") || RowVisible("_GemEnvColor", "sEnvironmentColor") || RowVisible("_GemParticleLoop", "sParticleLoop") || RowVisible("_GemParticleColor", "sColor") || HitIgnore("Gem");
            if (showRefraction)
            {
            EditorGUILayout.LabelField(FairyToonL10n.Text("sRefraction"), EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            P("_RefractionStrength", "sStrength");
            P("_RefractionFresnelPower", "sFresnel");
            EditorGUI.indentLevel--;
            }
            if (showRefraction && showGemBody) Separator();
            if (showGemBody)
            {
            EditorGUILayout.LabelField(FairyToonL10n.Text("sGem"), EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            bool gemEnv = !Searching || RowVisible("_GemChromaticAberration", "sChromaticAberration") || RowVisible("_GemEnvContrast", "sContrast") || RowVisible("_GemEnvColor", "sEnvironmentColor");
            bool gemParticle = !Searching || RowVisible("_GemParticleLoop", "sParticleLoop") || RowVisible("_GemParticleColor", "sColor");
            if (gemEnv)
            {
                P("_GemChromaticAberration", "sChromaticAberration");
                P("_GemEnvContrast", "sContrast");
                P("_GemEnvColor", "sEnvironmentColor");
            }
            if (gemEnv && gemParticle) Separator();
            if (gemParticle)
            {
                P("_GemParticleLoop", "sParticleLoop");
                P("_GemParticleColor", "sColor");
            }
            EditorGUI.indentLevel--;
            }
            bool gemLower = !Searching || RowVisible("_GemVRParallaxStrength", "sVRParallaxStrength") || RowVisible("_SmoothnessTex", "sSmoothness") || RowVisible("_Reflectance", "sReflectance");
            if ((showRefraction || showGemBody) && gemLower) Separator();
            P("_GemVRParallaxStrength", "sVRParallaxStrength");
            Tex("_SmoothnessTex", "_Smoothness", "sSmoothness");
            P("_Reflectance", "sReflectance");
            Tex("_ReflectionCubeTex", "_ReflectionCubeColor", "sCubemapFallback");
            P("_ReflectionCubeOverride", "sOverride");
            P("_ReflectionCubeEnableLighting", "sFallbackLighting");
            EndSection();
        }

        private void SeeThrough()
        {

            if (!SearchAllows("_SeeThroughRole", "_SeeThroughMask", "_SeeThroughMaskChannel", "_SeeThroughMaskCutoff", "_TransparentMode", "_SeeThroughOpacity", "_SeeThroughEarlyZ", "_SeeThroughFade", "_SeeThroughFadeSideAlpha", "_SeeThroughFadeHorizontalMin", "_SeeThroughFadeHorizontalMax", "_SeeThroughFadeVerticalMin", "_SeeThroughFadeVerticalMax", "_UseFakeShadow", "_FakeShadowColor", "_FakeShadowVector", "_SeeThroughRole|Role", "_SeeThroughMask|Eye / Brow Mask", "_SeeThroughMaskCutoff|Cutoff", "_SeeThroughOpacity|Opacity", "_SeeThroughEarlyZ|Early Z (front layer only)", "_SeeThroughFade|View Angle Fade", "_SeeThroughFadeSideAlpha|Side Opacity", "_SeeThroughFadeHorizontalMin|Horizontal Start (°)", "_SeeThroughFadeHorizontalMax|Horizontal End (°)", "_SeeThroughFadeVerticalMin|Vertical Start (°)", "_SeeThroughFadeVerticalMax|Vertical End (°)", "_UseFakeShadow|Fake Shadow", "_FakeShadowColor|Colour (multiply)", "_FakeShadowVector|Direction (xyz) / Offset (w, m)")) return;
            var role = Prop("_SeeThroughRole");
            if (role == null || !BeginSection("SeeThrough", "ft.SeeThrough", null, false, PropertyBlock.SeeThrough)) return;
            P("_SeeThroughRole", "Role");
            int r = role.hasMixedValue ? -1 : Mathf.RoundToInt(Number(role));
            if (r == FairyToonMaterialSetup.RoleNone)
            Help("Face: the eye / brow mask marks where the fringe turns see-through. Hair: the fringe drawn over that mask. Needs the FairyToon renderer feature and, for the fade, FairyToonSeeThroughHair on the avatar.", MessageType.None);
            if (r == FairyToonMaterialSetup.RoleFace || r < 0)
            {
                SubHeader("Face");
                Tex("_SeeThroughMask", "_SeeThroughMaskChannel", "Eye / Brow Mask");
                P("_SeeThroughMaskCutoff", "Cutoff");
                if (r == FairyToonMaterialSetup.RoleFace && m_Editor.targets.Length == 1) PaintButton((Material)m_Editor.target);
                var coverage = Prop("_TransparentMode");
                if (coverage != null && (coverage.hasMixedValue || Mathf.RoundToInt(Number(coverage)) == FairyToonMaterialSetup.Transparent))
                Help("A Transparent face draws after the hair, too late to cut the fringe: use Opaque or Cutout.", MessageType.Warning);
            }
            if (r == FairyToonMaterialSetup.RoleHair || r < 0)
            {
                SubHeader("Hair over the mask");
                P("_SeeThroughOpacity", "Opacity");
                P("_SeeThroughEarlyZ", "Early Z (front layer only)");
                P("_SeeThroughFade", "View Angle Fade");
                var fade = Prop("_SeeThroughFade");
                if (fade != null && (fade.hasMixedValue || Number(fade) > 0.5f))
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        P("_SeeThroughFadeSideAlpha", "Side Opacity");
                        P("_SeeThroughFadeHorizontalMin", "Horizontal Start (°)");
                        P("_SeeThroughFadeHorizontalMax", "Horizontal End (°)");
                        P("_SeeThroughFadeVerticalMin", "Vertical Start (°)");
                        P("_SeeThroughFadeVerticalMax", "Vertical End (°)");

                    }
                }
                SubHeader("Fake shadow on the face");
                P("_UseFakeShadow", "Fake Shadow");
                var fakeShadow = Prop("_UseFakeShadow");
                if (fakeShadow != null && (fakeShadow.hasMixedValue || Number(fakeShadow) > 0.5f))
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        P("_FakeShadowColor", "Colour (multiply)");
                        P("_FakeShadowVector", "Direction (xyz) / Offset (w, m)");
                    }
                }
            }
            if (r != FairyToonMaterialSetup.RoleNone && !FairyToonSeeThroughSetup.FeatureInstalled())
            {
                Help("The active URP renderer has no FairyToon renderer feature: the see-through fringe, early Z and fake shadow are not drawn.", MessageType.Warning);
                if (GUILayout.Button(FairyToonL10n.Text("Add FairyToon Feature to the URP Renderer"))) FairyToonSeeThroughSetup.InstallFeature();
            }
            EndSection();
        }

        private static void PaintButton(Material material)
        {
            var renderers = FairyToonSeeThroughSetup.RenderersUsing(material);
            using (new EditorGUI.DisabledScope(renderers.Count == 0))
            {
                if (GUILayout.Button(FairyToonL10n.Text(renderers.Count == 0 ? "Paint Mask (no renderer in the open scenes uses this material)" : "Paint Mask in the Scene View")))
                {
                    var (renderer, slot) = renderers[0];
                    FairyToonMaskPainter.Open(renderer, slot, "_SeeThroughMask", Mathf.RoundToInt(material.GetFloat("_SeeThroughMaskChannel")));
                }
            }
        }

        private void Stencil()
        {
            bool body = SearchAllows("_StencilRef", "_StencilReadMask", "_StencilWriteMask", "_StencilComp", "_StencilPass", "_StencilFail", "_StencilZFail");
            bool fur = SelectedRenderingMode(FairyToonMaterialSetup.Fur) && SearchAllows("_FurStencilRef", "_FurStencilReadMask", "_FurStencilWriteMask", "_FurStencilComp", "_FurStencilPass", "_FurStencilFail", "_FurStencilZFail");
            if (!body && !fur) return;
            if (!BeginSection("Stencil", "sStencilSetting", null, false, PropertyBlock.Stencil)) return;
            var role = Prop("_SeeThroughRole");
            bool managed = !SelectedSpecialMode()
            && role != null && (role.hasMixedValue || Number(role) > 0.5f);
            if (managed) Help("Set by the See-through Hair role.", MessageType.None);
            using var locked = new EditorGUI.DisabledScope(managed);
            P("_StencilRef");
            P("_StencilReadMask");
            P("_StencilWriteMask");
            P("_StencilComp");
            P("_StencilPass");
            P("_StencilFail");
            P("_StencilZFail");
            if (SelectedRenderingMode(FairyToonMaterialSetup.Fur))
            {
                SubHeader("Fur Stencil");
                P("_FurStencilRef");
                P("_FurStencilReadMask");
                P("_FurStencilWriteMask");
                P("_FurStencilComp");
                P("_FurStencilPass");
                P("_FurStencilFail");
                P("_FurStencilZFail");
            }
            EndSection();
        }

        private void Rendering()
        {
            bool queue = SearchAllows("|sRenderingReset", "|Render Queue", "|Enable GPU Instancing", "|Double Sided Global Illumination",
                "|" + L10n.Tr("Render Queue"), "|" + L10n.Tr("Enable GPU Instancing"), "|" + L10n.Tr("Double Sided Global Illumination"),
                "_SrcBlend", "_DstBlend", "_SrcBlendAlpha", "_DstBlendAlpha", "_ZWrite", "_ZTest", "_OffsetFactor", "_OffsetUnits", "_Cull", "|Alpha To Coverage (hair)");
            bool fur = SelectedRenderingMode(FairyToonMaterialSetup.Fur) && SearchAllows("_FurCull", "_FurZClip", "_FurZTest", "_FurColorMask", "_FurOffsetFactor", "_FurOffsetUnits", "_FurSrcBlend", "_FurDstBlend", "_FurSrcBlendAlpha", "_FurDstBlendAlpha", "_FurZWrite", "_FurAlphaToMask");
            bool outlineHit = Prop("_OutlineCull") != null
                && !SelectedRenderingMode(FairyToonMaterialSetup.Fur)
                && !SelectedRenderingMode(FairyToonMaterialSetup.Gem)
                && Searching && RowVisible("_OutlineCull", "sCullMode");
            if (!queue && !fur && !outlineHit) return;
            if (!BeginSection("Rendering", "sRenderingSetting", null, false, PropertyBlock.Rendering, true, false)) return;
            if (!Searching || LabelOnly("sRenderingReset"))
            {
                // lilToon uses one control-rect button, which is 23 px at 150%. GUILayout.Button was 25.
                Rect reset = EditorGUILayout.GetControlRect();
                if (GUI.Button(EditorGUI.IndentedRect(reset), FairyToonL10n.Text("sRenderingReset")))
                {
                    Undo.RecordObjects(m_Editor.targets, "FairyToon Reset Rendering");
                    foreach (Object target in m_Editor.targets)
                    {
                        var material = target as Material;
                        if (material == null) continue;
                        FairyToonMaterialSetup.ResetRendering(material);
                        s_NativeBefore[material] = new NativeState
                        {
                            RawQueue = material.rawRenderQueue,
                            Instancing = material.enableInstancing,
                            DoubleSidedGI = material.doubleSidedGI
                        };
                    }
                    m_Props = MaterialEditor.GetMaterialProperties(m_Editor.targets);
                }
                // Header gap is 3 px. The button-to-box measurement is 6 px, one pixel more.
                GUILayout.Space(1f / Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint));
            }
            if (m_Frames.Count > 0) m_Frames.RemoveAt(m_Frames.Count - 1);
            bool outlineNext = false;
            if (queue)
            {
            outlineNext = LayoutChoice("outline-next", Prop("_OutlineCull") != null
                && !SelectedRenderingMode(FairyToonMaterialSetup.Fur)
                && !SelectedRenderingMode(FairyToonMaterialSetup.Gem)
                && (!Searching || RowVisible("_OutlineCull", "sCullMode")));
            if (outlineNext) m_BoxStyle = s_OuterBeforeOutline;
            OpenBox(FairyToonL10n.Text("sRendering"), null, PropertyBlock.Rendering, true);
            m_Frames.Add(true);
            P("_Cull");
            DrawZWrite();
            P("_ZTest");
            P("_OffsetFactor");
            P("_OffsetUnits");
            if (AnyCoverage(FairyToonMaterialSetup.Cutout))
                P("_AlphaToMask", "Alpha To Coverage (hair)");
            SubHeader("Derived (set from Rendering Mode)");
            using (new EditorGUI.DisabledScope(true))
            {
                P("_SrcBlend");
                P("_DstBlend");
                P("_SrcBlendAlpha");
                P("_DstBlendAlpha");
            }
            if (UnityRowVisible("Enable GPU Instancing")) m_Editor.EnableInstancingField();
            if (UnityRowVisible("Render Queue")) DrawRenderQueue();
            if (UnityRowVisible("Double Sided Global Illumination")) m_Editor.DoubleSidedGIField();

            EndSection();
            }
            if (fur && OpenBox(FairyToonL10n.Text("sFur"), null, null, true))
            {
                IntPopup("_FurCull", "sCullMode", "sCullModeOff", "sCullModeFront", "sCullModeBack");
                P("_FurZClip");
                P("_FurZWrite");
                P("_FurZTest");
                P("_FurOffsetFactor");
                P("_FurOffsetUnits");
                IntPopup("_FurColorMask", "sColorMask",
                    "None", "A", "B", "BA", "G", "GA", "GB", "GBA", "R", "RA", "RB", "RBA", "RG", "RGA", "RGB", "RGBA");
                P("_FurAlphaToMask");
                bool forward = !Searching || RowVisible("_FurSrcBlend", "Source Blend") || RowVisible("_FurDstBlend", "Destination Blend")
                    || RowVisible("_FurSrcBlendAlpha", "Source Blend Alpha") || RowVisible("_FurDstBlendAlpha", "Destination Blend Alpha");
                if (forward)
                {
                    if (!Searching) Separator();
                    string furKey = PrefsPrefix + "Foldout.FurForward";
                    bool furOpen = FoldShown(furKey, Searching && forward);
                    Rect furRow = EditorGUILayout.GetControlRect();
                    Rect furIndented = EditorGUI.IndentedRect(furRow);
                    float furScale = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
                    Rect furArrow = new Rect(furIndented.x + 15f - 6f / furScale, furRow.y, 13f, furRow.height);
                    Rect furHit = new Rect(furArrow.x + 3f, furArrow.y, furArrow.width - 6f, furArrow.height);
                    bool nextFur = furOpen;
                    if (Event.current != null && Event.current.type == EventType.MouseDown && Event.current.button == 0 && furHit.Contains(Event.current.mousePosition))
                    {
                        nextFur = !furOpen;
                        Event.current.Use();
                        GUI.changed = true;
                    }
                    if (Event.current != null && Event.current.type == EventType.Repaint)
                        EditorStyles.foldout.Draw(furArrow, false, false, furOpen, false);
                    var furText = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
                    Rect furLabel = furIndented;
                    furLabel.xMin += 30f;
                    GUI.Label(furLabel, "Forward", furText);
                    SaveFoldClick(furKey, furOpen, nextFur);
                    if (furOpen)
                    {
                        EditorGUI.indentLevel++;
                        P("_FurSrcBlend");
                        P("_FurDstBlend");
                        P("_FurSrcBlendAlpha");
                        P("_FurDstBlendAlpha");
                        EditorGUI.indentLevel--;
                    }
                }
                CloseBox();
            }
            // lilToon puts Outline Cull in its own box under Rendering. FairyToon has no separate outline ZTest or colour mask.
            bool outline = LayoutChoice("outline-next", Prop("_OutlineCull") != null
                && !SelectedRenderingMode(FairyToonMaterialSetup.Fur)
                && !SelectedRenderingMode(FairyToonMaterialSetup.Gem)
                && (!Searching || RowVisible("_OutlineCull", "sCullMode")));
            if (!outline) return;
            // Adjacent margins use max, so the 5 px gap is its own rect. 5 / 1.5 = 3.333 pt, which is 5 device px.
            // A requested 5 device pixels measured 6, one pixel over. 4 device pixels lands on the 5 px gap.
            if (outline) PixelGap(4);
            if (outline) m_BoxStyle = s_OutlineBox;
            if (!OpenBox(FairyToonL10n.Text("sOutlineSetting"), null, null, true)) return;
            P("_OutlineCull", "sCullMode");
            CloseBox();
        }

        // ---- Layout helpers ----

        private static GUIStyle s_PixelGap;

        private static void PixelGap(int devicePixels)
        {
            if (s_PixelGap == null)
                s_PixelGap = new GUIStyle { margin = new RectOffset(0, 0, 0, 0), padding = new RectOffset(0, 0, 0, 0) };
            float height = devicePixels / Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
            GUILayoutUtility.GetRect(0f, height, s_PixelGap, GUILayout.ExpandWidth(true));
        }

        // A queue equal to this shader's Queue tag is lilToon's From Shader, including an explicit copy of that number.
        private void DrawRenderQueue()
        {
            var materials = new List<Material>();
            foreach (Object target in m_Editor.targets)
                if (target is Material material && material.shader != null) materials.Add(material);
            if (materials.Count == 0) return;
            int Shown(Material material)
            {
                int raw = material.rawRenderQueue;
                return raw < 0 || raw == material.shader.renderQueue ? -1 : raw;
            }
            int first = Shown(materials[0]);
            bool mixedPopup = false;
            bool mixedNumber = false;
            int effective = materials[0].renderQueue;
            for (int i = 1; i < materials.Count; i++)
            {
                if (Shown(materials[i]) != first) mixedPopup = true;
                if (materials[i].renderQueue != effective) mixedNumber = true;
            }
            Rect row = EditorGUILayout.GetControlRect();
            float scale = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
            float popupWidth = 121f / scale;
            float numberWidth = 96f / scale;
            float gap = 3f / scale;
            Rect number = new Rect(row.xMax - numberWidth, row.y, numberWidth, row.height);
            Rect popup = new Rect(number.x - gap - popupWidth, row.y, popupWidth, row.height);
            if (popup.x < row.x + 80f)
            {
                popup.x = row.x + 80f;
                popup.width = Mathf.Max(0f, number.x - gap - popup.x);
            }
            EditorGUI.LabelField(new Rect(row.x, row.y, Mathf.Max(0f, popup.x - row.x), row.height), FairyToonL10n.Text("Render Queue"));
            int geometry = (int)RenderQueue.Geometry;
            int alphaTest = (int)RenderQueue.AlphaTest;
            int transparent = (int)RenderQueue.Transparent;
            bool custom = !mixedPopup && first >= 0 && first != geometry && first != alphaTest && first != transparent;
            string[] queueLabels = custom
                ? new[] { L10n.Tr("From Shader"), L10n.Tr("Geometry"), L10n.Tr("AlphaTest"), L10n.Tr("Transparent"), "Custom" }
                : new[] { L10n.Tr("From Shader"), L10n.Tr("Geometry"), L10n.Tr("AlphaTest"), L10n.Tr("Transparent") };
            int[] queueValues = custom
                ? new[] { -1, geometry, alphaTest, transparent, first }
                : new[] { -1, geometry, alphaTest, transparent };
            EditorGUI.showMixedValue = mixedPopup;
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUI.IntPopup(popup, mixedPopup ? -1 : first, queueLabels, queueValues);
            bool popupChanged = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = mixedNumber;
            EditorGUI.BeginChangeCheck();
            int edited = EditorGUI.IntField(number, effective);
            bool numberChanged = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = false;
            if (!popupChanged && !numberChanged) return;
            Undo.RecordObjects(m_Editor.targets, "Render Queue");
            foreach (Material material in materials)
            {
                int value = numberChanged ? edited : picked;
                // From Shader stores -1. An explicit number, including one equal to the shader tag, stays explicit.
                material.renderQueue = value < 0 ? -1 : value;
            }
        }

        private void RenderingMode()
        {
            if (!RowVisible("_TransparentMode", "sRenderingMode")) return;
            int mode = FairyToonMaterialSetup.GetRenderingMode(m_Editor.target as Material);
            bool mixed = false;
            foreach (Object target in m_Editor.targets)
            mixed |= FairyToonMaterialSetup.GetRenderingMode(target as Material) != mode;
            EditorGUI.showMixedValue = mixed;
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup(FairyToonL10n.Text("sRenderingMode"), mixed ? -1 : mode, new[]
            {
                FairyToonL10n.Text("sRenderingModeOpaque"),
                FairyToonL10n.Text("sRenderingModeCutout"),
                FairyToonL10n.Text("sRenderingModeTransparent"),
                FairyToonL10n.Text("ft.ModeRefraction"),
                FairyToonL10n.Text("ft.ModeFur"),
                FairyToonL10n.Text("ft.ModeGem")
            });
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = false;
            if (!changed) return;
            Undo.RecordObjects(m_Editor.targets, "FairyToon Rendering Mode");
            foreach (Object target in m_Editor.targets)
            {
                var material = target as Material;
                if (!FairyToonMaterialSetup.SetRenderingMode(material, selected))
                {
                    Debug.LogError("FairyToon shader was not found for rendering mode " + selected + ".", material);
                    continue;
                }
                EditorUtility.SetDirty(material);
            }
            // Refresh immediately: the rest of this draw must use the new shader's property descriptors.
            m_Props = MaterialEditor.GetMaterialProperties(m_Editor.targets);
            m_Editor.PropertiesChanged();
            GUIUtility.ExitGUI();
        }

        private void FurMode()
        {
            var property = Prop("_FurMode");
            if (property == null || !RowVisible("_FurMode", "ft.FurMode")) return;
            EditorGUI.showMixedValue = property.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup(FairyToonL10n.Text("ft.FurMode"), property.hasMixedValue ? -1 : Mathf.RoundToInt(Number(property)), new[]
            {
                FairyToonL10n.Text("sRenderingModeTransparent"),
                FairyToonL10n.Text("sRenderingModeCutout"),
                FairyToonL10n.Text("ft.FurTwoPass")
            });
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = false;
            if (!changed) return;
            Undo.RecordObjects(m_Editor.targets, "FairyToon Fur Mode");
            foreach (Object target in m_Editor.targets)
            {
                var material = target as Material;
                if (FairyToonMaterialSetup.SetFurMode(material, selected)) EditorUtility.SetDirty(material);
            }
            m_Props = MaterialEditor.GetMaterialProperties(m_Editor.targets);
            m_Editor.PropertiesChanged();
            GUIUtility.ExitGUI();
        }

        private bool AnyCoverage(params int[] modes)
        {
            foreach (Object target in m_Editor.targets)
            {
                int mode = FairyToonMaterialSetup.GetRenderingMode(target as Material);
                for (int i = 0; i < modes.Length; i++)
                    if (mode == modes[i]) return true;
            }
            return false;
        }

        private bool SelectedRenderingMode(int mode)
        {
            foreach (Object target in m_Editor.targets)
            if (FairyToonMaterialSetup.GetRenderingMode(target as Material) == mode) return true;
            return false;
        }

        private bool SelectedSpecialMode() => SelectedRenderingMode(FairyToonMaterialSetup.Refraction)
        || SelectedRenderingMode(FairyToonMaterialSetup.Fur) || SelectedRenderingMode(FairyToonMaterialSetup.Gem);

        /// <summary>Shuriken header with a foldout arrow, optional feature toggle, and lilToon's gear menu.</summary>
        /// <remarks>A section whose feature toggle is off stays closed, as in lilToon. The gear copies this section's properties.</remarks>
        private void BeginScope(Rect rect, MaterialProperty prop)
        {
            MaterialEditor.BeginProperty(rect, prop);
            m_Editor.BeginAnimatedCheck(rect, prop);
        }

        private void EndScope()
        {
            m_Editor.EndAnimatedCheck();
            MaterialEditor.EndProperty();
        }

        private void PropertyGearRow(string name, string label, PropertyBlock block)
        {
            var prop = Prop(name);
            if (prop == null || !RowVisible(name, label)) return;
            Rect row = EditorGUILayout.GetControlRect();
            Rect gear = new Rect(row.xMax - 22f, row.y, 20f, row.height);
            row.width = Mathf.Max(0f, row.width - 24f);
            string shown = FairyToonL10n.Text(label);
            if (!DrawLocalizedEnum(prop, shown, null, row, true))
                m_Editor.ShaderProperty(row, prop, shown);
            if (GUI.Button(gear, EditorGUIUtility.IconContent("_Popup"), s_Gear))
                FairyToonClipboard.ShowMenu(block, m_Editor, () => m_Props);
        }

        private void BeginGearRow(string titleKey, PropertyBlock block)
        {
            OpenBox(FairyToonL10n.Text(titleKey), null, block);
        }

        private void EndGearRow() => CloseBox();

        private bool BeginSection(string key, string title, MaterialProperty toggle = null, bool openByDefault = false,
            PropertyBlock? block = null, bool foldout = true, bool frame = true, bool titled = true)
        {
            string prefsKey = PrefsPrefix + "Foldout." + key;
            bool open = LayoutChoice("section:" + key, !foldout || EditorPrefs.GetBool(prefsKey, openByDefault));
            string shown = FairyToonL10n.Text(title);
            if (foldout)
            {
                // The style paints 22 pt. A collapsed header keeps a 20 pt slot (30 px at 150%);
                // the next header covers the extra shadow, leaving 2 px. An open header reserves
                // the full 22 pt so that shadow stays above the 3 px gap.
                float painted = s_Foldout != null && s_Foldout.fixedHeight > 0f ? s_Foldout.fixedHeight : 22f;
                float header = open ? painted : 20f;
                Rect rect = GUILayoutUtility.GetRect(16f, header, GUILayout.ExpandWidth(true));
                // The background itself starts 8 points further left, matching lilToon's foldout. 12 px at 150%.
                rect.xMin -= 8f;
                if (s_Foldout != null) GUI.Box(rect, GUIContent.none, s_Foldout);
                float x = rect.x + 4f;
                Rect arrow = new Rect(x, rect.y + 3f, 14f, 16f);
                x += 16f;
                Rect gear = new Rect(rect.xMax - 22f, rect.y + 1f, 20f, rect.height - 2f);
                GUI.Label(new Rect(x, rect.y, Mathf.Max(0f, gear.x - x - 2f), rect.height), shown, s_HeaderLabel);
                Event e = Event.current;
                if (block.HasValue && GUI.Button(gear, EditorGUIUtility.IconContent("_Popup"), s_Gear))
                    FairyToonClipboard.ShowMenu(block.Value, m_Editor, () => m_Props);
                Rect click = rect;
                click.width = Mathf.Max(0f, click.width - 24f);
                if (e.type == EventType.MouseDown && e.button == 0 && click.Contains(e.mousePosition))
                {
                    EditorPrefs.SetBool(prefsKey, !open);
                    e.Use();
                }
                if (e.type == EventType.Repaint) EditorStyles.foldout.Draw(arrow, false, false, open, false);
            }
            if (!open) return false;
            if (foldout) m_HeaderGapPending = true;
            if (!frame)
            {
                m_Frames.Add(false);
                return true;
            }
            if (!titled)
            {
                OpenContentBox();
                m_Frames.Add(true);
                return true;
            }
            if (!OpenBox(shown, toggle, block)) return false;
            m_Frames.Add(true);
            return true;
        }

        // Outer fill is the measured title-bar colour. The inner fill is the darker content, inset so the outer colour reads as a 2px outline.
        private bool OpenBox(string shown, MaterialProperty toggle, PropertyBlock? block, bool full = false)
        {
            ConsumeHeaderGap();
            GUIStyle box = m_BoxStyle ?? s_Outer;
            m_BoxStyle = null;
            PushVertical(box);
            Rect row = GUILayoutUtility.GetRect(16f, 22f, GUILayout.ExpandWidth(true));
            float x = row.x + 1f;
            bool enabled = true;
            if (toggle != null)
            {
                Rect toggleRect = new Rect(x, row.y + 3f, 16f, 16f);
                BeginScope(toggleRect, toggle);
                try
                {
                    EditorGUI.showMixedValue = toggle.hasMixedValue;
                    EditorGUI.BeginChangeCheck();
                    bool value = EditorGUI.Toggle(toggleRect, Number(toggle) > 0.5f);
                    if (EditorGUI.EndChangeCheck()) SetNumber(toggle, value ? 1f : 0f);
                }
                finally
                {
                    EditorGUI.showMixedValue = false;
                    EndScope();
                }
                enabled = LayoutChoice("box-on:" + toggle.name, toggle.hasMixedValue || Number(toggle) > 0.5f);
                x += 20f;
            }
            Rect gear = new Rect(row.xMax - 22f, row.y, 20f, row.height);
            GUI.Label(new Rect(x, row.y, Mathf.Max(0f, gear.x - x - 2f), row.height), shown, s_HeaderLabel);
            if (block.HasValue && GUI.Button(gear, EditorGUIUtility.IconContent("_Popup"), s_Gear))
                FairyToonClipboard.ShowMenu(block.Value, m_Editor, () => m_Props);
            if (!enabled)
            {
                PopVertical();
                return false;
            }
            PushVertical(full ? s_InnerFull : s_InnerHalf);
            m_InnerDepth++;
            return true;
        }

        private void OpenContentBox()
        {
            // lilToon's untitled Base box is only customBox: a 1px edge, not the solid outer frame around it.
            ConsumeHeaderGap();
            PushVertical(s_Custom);
        }

        // 2 points is 3 px at 150%. Only the first box after a section header consumes it, so sibling boxes keep their own margin.
        private void ConsumeHeaderGap()
        {
            if (!m_HeaderGapPending) return;
            m_HeaderGapPending = false;
            GUILayoutUtility.GetRect(0f, 2f, GUILayout.ExpandWidth(true));
        }

        private void PushVertical(GUIStyle style)
        {
            EditorGUILayout.BeginVertical(style);
            m_Verticals++;
        }

        private void PopVertical()
        {
            if (m_Verticals <= 0) return;
            EditorGUILayout.EndVertical();
            m_Verticals--;
        }

        private void CloseBox()
        {
            if (m_InnerDepth > 0)
            {
                PopVertical();
                m_InnerDepth--;
            }
            PopVertical();
        }

        private void EndSection()
        {
            m_HeaderGapPending = false;
            if (m_Frames.Count == 0) return;
            bool framed = m_Frames[m_Frames.Count - 1];
            m_Frames.RemoveAt(m_Frames.Count - 1);
            if (framed) CloseBox();
        }

        private void Separator()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1f);
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(EditorGUI.IndentedRect(rect), s_Line);
        }

        private void HsvgSliders(string name, bool force = false)
        {
            var property = Prop(name);
            if (property == null) return;
            HsvgAxis(property, 0, "sHue", -0.5f, 0.5f, force);
            HsvgAxis(property, 1, "sSaturation", 0f, 2f, force);
            HsvgAxis(property, 2, "sValue", 0f, 2f, force);
            HsvgAxis(property, 3, "sGamma", 0.01f, 2f, force);
        }

        private void HsvgAxis(MaterialProperty property, int component, string label, float min, float max, bool force = false)
        {
            if (!force && !RowVisible(property.name, label)) return;
            Vector4 value = property.vectorValue;
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, property);
            try
            {
                EditorGUI.showMixedValue = property.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                string shown = Event.current.alt ? property.name + "." + "xyzw"[component] : FairyToonL10n.Text(label);
                value[component] = EditorGUI.Slider(row, shown, value[component], min, max);
                if (EditorGUI.EndChangeCheck()) SetDissolveComponents(property, value, 1 << component);
            }
            finally
            {
                EditorGUI.showMixedValue = false;
                EndScope();
            }
        }

        private void ScrollAndRotate(string name, bool? anglePart = null, bool reset = false)
        {
            var property = Prop(name);
            if (property == null) return;
            bool showScroll = anglePart != true && (!Searching || LabelOnly("sScroll") || HitIgnore("UV Animation") || HitIgnore(property.name + ".x") || HitIgnore(property.name + ".y") || (RowVisible(name, "sScroll") && !LabelOnly("sRotate") && !LabelOnly("sAngle")));
            bool showRotate = anglePart != true && (!Searching || LabelOnly("sRotate") || HitIgnore("UV Animation") || HitIgnore(property.name + ".w") || (RowVisible(name, "sRotate") && !LabelOnly("sScroll") && !LabelOnly("sAngle")));
            bool showAngle = anglePart != false && (!Searching || LabelOnly("sAngle") || HitIgnore("Angle") || HitIgnore(property.name + ".z"));
            if (!showScroll && !showRotate && !showAngle) return;
            Vector4 value = property.vectorValue;
            if (showAngle)
            {
                Rect row = EditorGUILayout.GetControlRect();
                BeginScope(row, property);
                try
                {
                    EditorGUI.showMixedValue = property.hasMixedValue;
                    EditorGUI.BeginChangeCheck();
                    string shown = Event.current != null && Event.current.alt ? property.name + ".z" : "Angle";
                    float degrees = value.z * Mathf.Rad2Deg;
                    degrees = EditorGUI.Slider(row, shown, degrees, -180f, 180f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        value = property.vectorValue;
                        value.z = degrees * Mathf.Deg2Rad;
                        SetDissolveComponents(property, value, 4);
                    }
                }
                finally
                {
                    EditorGUI.showMixedValue = false;
                    EndScope();
                }
            }
            if (showScroll)
            {
                Rect row = EditorGUILayout.GetControlRect();
                BeginScope(row, property);
                try
                {
                EditorGUI.showMixedValue = property.hasMixedValue;
                Rect label = row;
                label.width = EditorGUIUtility.labelWidth;
                EditorGUI.LabelField(label, FairyToonL10n.Text("sScroll"));
                var values = new Rect(label.xMax, row.y, row.xMax - label.xMax, row.height);
                int mask = 0;
                using (var axes = new AxisFieldScope(values, 2))
                {
                    EditorGUI.BeginChangeCheck();
                    float scrollX = EditorGUI.FloatField(axes.Field(0), "X", value.x);
                    if (EditorGUI.EndChangeCheck()) { value.x = scrollX; mask |= 1; }
                    EditorGUI.BeginChangeCheck();
                    float scrollY = EditorGUI.FloatField(axes.Field(1), "Y", value.y);
                    if (EditorGUI.EndChangeCheck()) { value.y = scrollY; mask |= 2; }
                }
                if (mask != 0) SetDissolveComponents(property, value, mask);
                EditorGUI.showMixedValue = false;
                }
                finally
                {
                    EditorGUI.showMixedValue = false;
                    EndScope();
                }
            }
            if (showRotate)
            {
                Rect row = EditorGUILayout.GetControlRect();
                BeginScope(row, property);
                try
                {
                EditorGUI.showMixedValue = property.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                float rotate = value.w / (Mathf.PI * 2f);
                rotate = EditorGUI.FloatField(row, FairyToonL10n.Text("sRotate"), rotate);
                if (EditorGUI.EndChangeCheck())
                {
                    value = property.vectorValue;
                    value.w = rotate * Mathf.PI * 2f;
                    SetDissolveComponents(property, value, 8);
                }
                EditorGUI.showMixedValue = false;
                }
                finally
                {
                    EditorGUI.showMixedValue = false;
                    EndScope();
                }
            }
            if (reset && (!Searching || LabelOnly("sReset")))
            {
                Rect resetRow = EditorGUILayout.GetControlRect(false, 18f);
                if (GUI.Button(resetRow, FairyToonL10n.Text("sReset"))
                    && EditorUtility.DisplayDialog("Reset UV", "Reset UV and Decal settings for this layer?", "Reset", "Cancel"))
                    ResetLayerUv(m_Props, name);
            }
        }

        internal static void ResetLayerUv(MaterialProperty[] properties, string scrollName)
        {
            string tex = scrollName.EndsWith("_ScrollRotate", StringComparison.Ordinal)
                ? scrollName.Substring(0, scrollName.Length - "_ScrollRotate".Length)
                : scrollName;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Reset UV");
            // These UV values do not drive keywords or passes. Avoid resyncing authored blend state on each setter.
            s_ProbeDepth++;
            try
            {
                // Match the confirmed lilToon 2.4.1 UV4Decal reset, including its button-specific sub-parameter.
                foreach (string suffix in new[] { "_UVMode", "IsDecal", "IsLeftOnly", "IsRightOnly", "ShouldCopy", "ShouldFlipMirror", "ShouldFlipCopy", "Angle" })
                    SetNumber(FindProperty(tex + suffix, properties, false), 0f);
                var animation = FindProperty(tex + "DecalAnimation", properties, false);
                if (animation != null) animation.vectorValue = new Vector4(1f, 1f, 1f, 30f);
                var subParam = FindProperty(tex + "DecalSubParam", properties, false);
                if (subParam != null) subParam.vectorValue = new Vector4(1f, 1f, 0.01f, 1f);
            }
            finally
            {
                s_ProbeDepth--;
                Undo.CollapseUndoOperations(group);
            }
        }

        private void AngleSlider(string name)
        {
            var property = Prop(name);
            if (property == null || !RowVisible(name, "sAngle")) return;
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, property);
            try
            {
                EditorGUI.showMixedValue = property.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                float degrees = RoundMillion(Number(property) / Mathf.PI * 180f);
                degrees = EditorGUI.Slider(row, Event.current.alt ? name : FairyToonL10n.Text("sAngle"), degrees, -180f, 180f);
                if (EditorGUI.EndChangeCheck()) SetNumber(property, degrees * Mathf.PI / 180f);
            }
            finally
            {
                EditorGUI.showMixedValue = false;
                EndScope();
            }
        }

        private static float RoundMillion(float value) => Mathf.Floor(value * 1000000f + 0.5f) * 0.000001f;

        private void InvBorder(string name)
        {
            var property = Prop(name);
            if (property == null || !RowVisible(name, "sBorder")) return;
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, property);
            try
            {
                EditorGUI.showMixedValue = property.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                string shown = Event.current != null && Event.current.alt ? property.name : FairyToonL10n.Text("sBorder");
                float border = 1f - EditorGUI.Slider(row, shown, 1f - Number(property), 0f, 1f);
                if (EditorGUI.EndChangeCheck()) SetNumber(property, border);
            }
            finally
            {
                EditorGUI.showMixedValue = false;
                EndScope();
            }
        }

        private void MinusRange(string name, string label)
        {
            var property = Prop(name);
            if (property == null || !RowVisible(name, label)) return;
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, property);
            try
            {
                EditorGUI.showMixedValue = property.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                float shown = EditorGUI.Slider(row, Event.current.alt ? property.name : FairyToonL10n.Text(label), -Number(property), 0f, 1f);
                if (EditorGUI.EndChangeCheck()) SetNumber(property, -shown);
            }
            finally
            {
                EditorGUI.showMixedValue = false;
                EndScope();
            }
        }

        private void FurVectorAndLength()
        {
            var property = Prop("_FurVector");
            if (property == null) return;
            Vector4 value = property.vectorValue;
            bool bothAxes = HitIgnore("_FurVector") || Phrase("Direction (XYZ), Length (W)");
            bool showDirection = !Searching || bothAxes || LabelOnly("sVectorTex") || HitIgnore("Direction (XYZ)");
            bool showLength = !Searching || bothAxes || LabelOnly("sLength") || HitIgnore("Length (W)");
            if (!showDirection && !showLength) return;
            if (showDirection)
            {
            Rect dirRow = EditorGUILayout.GetControlRect();
            BeginScope(dirRow, property);
            try
            {
            EditorGUI.showMixedValue = property.hasMixedValue;
            Rect label = dirRow;
            label.width = EditorGUIUtility.labelWidth;
            EditorGUI.LabelField(label, FairyToonL10n.Text("sVectorTex"));
            var values = new Rect(label.xMax, dirRow.y, dirRow.xMax - label.xMax, dirRow.height);
            int mask = 0;
            using (var axes = new AxisFieldScope(values, 3))
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    EditorGUI.BeginChangeCheck();
                    float next = EditorGUI.FloatField(axes.Field(axis), "XYZ"[axis].ToString(), value[axis]);
                    if (!EditorGUI.EndChangeCheck()) continue;
                    value[axis] = next;
                    mask |= 1 << axis;
                }
            }
            if (mask != 0) SetDissolveComponents(property, value, mask);
            EditorGUI.showMixedValue = false;
            }
            finally
            {
                EditorGUI.showMixedValue = false;
                EndScope();
            }
            }
            if (!showLength) return;
            Rect lengthRow = EditorGUILayout.GetControlRect();
            BeginScope(lengthRow, property);
            try
            {
            EditorGUI.showMixedValue = property.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            float length = EditorGUI.FloatField(lengthRow, FairyToonL10n.Text("sLength"), value.w);
            if (EditorGUI.EndChangeCheck())
            {
                value = property.vectorValue;
                value.w = length;
                SetDissolveComponents(property, value, 8);
            }
            EditorGUI.showMixedValue = false;
            }
            finally
            {
                EditorGUI.showMixedValue = false;
                EndScope();
            }
        }

        private void DrawZWrite()
        {
            if (!ZWriteOverwritten())
            {
                P("_ZWrite");
                return;
            }
            using (new EditorGUI.DisabledScope(true)) P("_ZWrite");
        }

        // Fur's body pass always writes depth. Opaque and cutout keep the value set in Base.
        private bool ZWriteOverwritten()
        {
            if (m_Editor == null) return false;
            foreach (Object target in m_Editor.targets)
                if (FairyToonMaterialSetup.GetRenderingMode(target as Material) == FairyToonMaterialSetup.Fur)
                    return true;
            return false;
        }

        private bool ShowsApplyTransparency()
        {
            if (m_Editor == null) return false;
            foreach (Object target in m_Editor.targets)
            {
                var material = target as Material;
                if (material == null) continue;
                int mode = FairyToonMaterialSetup.GetRenderingMode(material);
                // Refraction composites with coverage 1, so Apply Transparency does nothing there.
                if (mode == FairyToonMaterialSetup.Transparent || mode == FairyToonMaterialSetup.Gem)
                    return true;
                if (mode != FairyToonMaterialSetup.Fur || !material.HasProperty("_FurMode")) continue;
                if (Mathf.RoundToInt(material.GetFloat("_FurMode")) != FairyToonMaterialSetup.FurCutout) return true;
            }
            return false;
        }

        private void DecalPlacement(string texture, MaterialProperty copy, MaterialProperty flipCopy)
        {
            var tex = Prop(texture);
            if (tex == null) return;
            Vector4 st = tex.textureScaleAndOffset;
            DecodeDecalAxis(st.x, st.z, out float posX, out float scaleX);
            DecodeDecalAxis(st.y, st.w, out float posY, out float scaleY);
            int copyMode = 0;
            if (copy != null && Number(copy) > 0.5f) copyMode = 1;
            if (flipCopy != null && Number(flipCopy) > 0.5f) copyMode = 2;
            if (copyMode > 0 && posX < 0.5f) posX = 1f - posX;
            float nextX = posX;
            float nextY = posY;
            float nextScaleX = scaleX;
            float nextScaleY = scaleY;
            bool changeX = DecalSlider(tex, copy, flipCopy, tex.name + "_ST.z", "sPositionX", posX, copyMode > 0 ? 0.5f : 0f, 1f, ref nextX);
            bool changeY = DecalSlider(tex, copy, flipCopy, tex.name + "_ST.w", "sPositionY", posY, 0f, 1f, ref nextY);
            bool changeScaleX = DecalSlider(tex, copy, flipCopy, tex.name + "_ST.x", "sScaleX", scaleX, -1f, 1f, ref nextScaleX);
            bool changeScaleY = DecalSlider(tex, copy, flipCopy, tex.name + "_ST.y", "sScaleY", scaleY, -1f, 1f, ref nextScaleY);
            if (!changeX && !changeY && !changeScaleX && !changeScaleY) return;
            Vector4 proposed = DecalVector(st, copyMode, changeX, changeY, changeScaleX, changeScaleY, nextX, nextY, nextScaleX, nextScaleY);
            // Unity bit 0 is the texture reference. Scale X/Y are bits 1 and 2. Offset X/Y are bits 3 and 4.
            int editedMask = (changeScaleX ? 2 : 0) | (changeScaleY ? 4 : 0) | (changeX || changeScaleX ? 8 : 0) | (changeY || changeScaleY ? 16 : 0);
            if (OfferTextureScale(tex, proposed, editedMask)) return;
            if (tex.targets == null) return;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                foreach (Object target in tex.targets)
                {
                    var material = target as Material;
                    if (material == null || !material.HasProperty(texture) || Locked(material, texture + "_ST")) continue;
                    Vector4 current = material.GetVector(texture + "_ST");
                    int mode = CopyModeOf(material, copy, flipCopy);
                    Vector4 next = DecalVector(current, mode, changeX, changeY, changeScaleX, changeScaleY, nextX, nextY, nextScaleX, nextScaleY);
                    if (next == current) continue;
                    Undo.RecordObject(material, "Modify Decal");
                    material.SetVector(texture + "_ST", next);
                    FairyToonMaterialSetup.Apply(material);
                }
            }
            finally { Undo.CollapseUndoOperations(group); }
        }

        // A scale edit has to rebuild that axis's offset. Keeping the old offset slides the decal.
        private static Vector4 DecalVector(Vector4 stored, int copyMode, bool changeX, bool changeY, bool changeScaleX, bool changeScaleY, float nextX, float nextY, float nextScaleX, float nextScaleY)
        {
            DecodeDecalAxis(stored.x, stored.z, out float posX, out float scaleX);
            DecodeDecalAxis(stored.y, stored.w, out float posY, out float scaleY);
            if (copyMode > 0 && posX < 0.5f) posX = 1f - posX;
            if (changeX) posX = nextX;
            if (changeY) posY = nextY;
            if (changeScaleX) scaleX = nextScaleX;
            if (changeScaleY) scaleY = nextScaleY;
            if (changeX && copyMode > 0 && posX < 0.5f) posX = 1f - posX;
            Vector4 next = stored;
            if (changeX || changeScaleX) EncodeDecalAxis(posX, scaleX, out next.x, out next.z);
            if (changeY || changeScaleY) EncodeDecalAxis(posY, scaleY, out next.y, out next.w);
            return next;
        }

        private bool DecalSlider(MaterialProperty tex, MaterialProperty copy, MaterialProperty flipCopy, string alt, string label, float current, float min, float max, ref float value)
        {
            string shown = FairyToonL10n.Text(label);
            if (Searching && !HitIgnore(label) && !HitIgnore(shown) && !HitIgnore(alt)) return false;
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, tex);
            try
            {
                EditorGUI.showMixedValue = DecalAxisMixed(tex, copy, flipCopy, label);
                EditorGUI.BeginChangeCheck();
                value = EditorGUI.Slider(row, Event.current.alt ? alt : shown, current, min, max);
                EditorGUI.showMixedValue = false;
                return EditorGUI.EndChangeCheck();
            }
            finally { EndScope(); }
        }

        private static bool DecalAxisMixed(MaterialProperty tex, MaterialProperty copy, MaterialProperty flipCopy, string label)
        {
            // Texture hasMixedValue is only bit 0. Position and scale use the ST bits, and Copy changes the displayed X.
            if (tex.applyPropertyCallback != null || AnimationMode.InAnimationMode())
            {
                int bit = label == "sScaleX" ? 2 : label == "sScaleY" ? 4 : label == "sPositionX" ? (2 | 8) : label == "sPositionY" ? (4 | 16) : 0;
                if ((MixedMask(tex) & bit) != 0) return true;
                if (label == "sPositionX" && ((copy != null && copy.hasMixedValue) || (flipCopy != null && flipCopy.hasMixedValue)))
                    return true;
                return false;
            }
            if (tex.targets == null || tex.targets.Length < 2 || !(tex.targets[0] is Material first) || !first.HasProperty(tex.name))
                return false;
            float reference = DisplayedDecalAxis(first, tex, copy, flipCopy, label);
            for (int i = 1; i < tex.targets.Length; i++)
            {
                if (!(tex.targets[i] is Material material) || !material.HasProperty(tex.name)) continue;
                if (!Mathf.Approximately(DisplayedDecalAxis(material, tex, copy, flipCopy, label), reference)) return true;
            }
            return false;
        }

        private static float DisplayedDecalAxis(Material material, MaterialProperty tex, MaterialProperty copy, MaterialProperty flipCopy, string label)
        {
            float value = DecalAxis(material.GetVector(tex.name + "_ST"), label);
            if (label == "sPositionX" && CopyModeOf(material, copy, flipCopy) > 0 && value < 0.5f)
                value = Round6(1f - value);
            return value;
        }

        private static float DecalAxis(Vector4 stored, string label)
        {
            DecodeDecalAxis(stored.x, stored.z, out float posX, out float scaleX);
            DecodeDecalAxis(stored.y, stored.w, out float posY, out float scaleY);
            if (label == "sPositionX") return posX;
            if (label == "sPositionY") return posY;
            if (label == "sScaleX") return scaleX;
            return scaleY;
        }

        private static void DecodeDecalAxis(float storedScale, float storedOffset, out float pos, out float scale)
        {
            if (storedScale == 0f) { pos = 0.5f; scale = 0.000001f; }
            else { pos = (0.5f - storedOffset) / storedScale; scale = 1f / storedScale; }
            pos = Round6(pos);
            scale = Round6(scale);
        }

        private static void EncodeDecalAxis(float pos, float scale, out float storedScale, out float storedOffset)
        {
            if (scale == 0f) scale = 0.000001f;
            storedScale = 1f / scale;
            storedOffset = (-pos * storedScale) + 0.5f;
        }

        private static float Round6(float value) => Mathf.Floor(value * 1000000f + 0.5f) * 0.000001f;

        private static int CopyModeOf(Material material, MaterialProperty copy, MaterialProperty flipCopy)
        {
            if (flipCopy != null && material.HasProperty(flipCopy.name) && material.GetFloat(flipCopy.name) > 0.5f) return 2;
            if (copy != null && material.HasProperty(copy.name) && material.GetFloat(copy.name) > 0.5f) return 1;
            return 0;
        }

        private static bool OfferTextureScale(MaterialProperty tex, Vector4 value, int mask)
        {
            if (tex.applyPropertyCallback == null) return false;
            Vector4 before = tex.textureScaleAndOffset;
            // The setter returns before the callback when the first descriptor already has this ST
            // and the texture reference is not mixed. An explicit axis submit still has to reach animation.
            bool setterRuns = tex.hasMixedValue || before != value;
            bool handled = false;
            MaterialProperty.ApplyPropertyCallback original = tex.applyPropertyCallback;
            s_ProbeDepth++;
            try
            {
                if (!setterRuns)
                {
                    handled = original(tex, mask, before);
                    return handled;
                }
                tex.applyPropertyCallback = (changed, changedMask, previous) =>
                {
                    handled = original(changed, mask, previous);
                    return true;
                };
                tex.textureScaleAndOffset = value;
                return handled;
            }
            finally
            {
                tex.applyPropertyCallback = original;
                s_ProbeDepth--;
            }
        }

        private static readonly FieldInfo s_MixedMask = typeof(MaterialProperty).GetField("m_MixedValueMask", BindingFlags.Instance | BindingFlags.NonPublic);

        private static int MixedMask(MaterialProperty property)
        {
            if (property == null || s_MixedMask == null) return 0;
            return s_MixedMask.GetValue(property) is int mask ? mask : 0;
        }

        private static bool Locked(Material material, string name)
        {
            return material.isVariant && material.IsPropertyLockedByAncestor(name);
        }

        private static void WriteAlpha(MaterialProperty prop, float alpha)
        {
            if (prop.applyPropertyCallback != null)
            {
                bool handled = false;
                MaterialProperty.ApplyPropertyCallback original = prop.applyPropertyCallback;
                s_ProbeDepth++;
                prop.applyPropertyCallback = (changed, mask, previous) =>
                {
                    handled = original(changed, mask, previous);
                    return true;
                };
                try
                {
                    Color color = prop.colorValue;
                    color.a = alpha;
                    prop.colorValue = color;
                }
                finally
                {
                    prop.applyPropertyCallback = original;
                    s_ProbeDepth--;
                }
                if (handled) return;
            }
            if (prop.targets == null) return;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                Undo.RecordObjects(prop.targets, "Modify Alpha");
                foreach (Object target in prop.targets)
                {
                    var material = target as Material;
                    if (material == null || Locked(material, prop.name) || !material.HasProperty(prop.name)) continue;
                    Color color = material.GetColor(prop.name);
                    color.a = alpha;
                    material.SetColor(prop.name, color);
                }
            }
            finally { Undo.CollapseUndoOperations(group); }
        }

        private static void WriteAlphaMask(MaterialProperty scale, MaterialProperty offset, bool invertChanged, bool transparencyChanged, bool invert, float transparency)
        {
            if (scale == null || offset == null || scale.targets == null) return;
            // A mixed preview cannot be split into one pair. Leave every material as it is.
            if (scale.hasMixedValue || offset.hasMixedValue) return;
            bool previewInvert = Number(scale) < 0f;
            float previewTransparency = Number(offset) - (previewInvert ? 1f : 0f);
            if (invertChanged) previewInvert = invert;
            if (transparencyChanged) previewTransparency = transparency;
            float nextScale = previewInvert ? -1f : 1f;
            float nextOffset = previewTransparency + (previewInvert ? 1f : 0f);
            float previousScale = Number(scale);
            float previousOffset = Number(offset);
            bool wantScale = invertChanged && !Mathf.Approximately(nextScale, previousScale);
            bool wantOffset = (invertChanged || transparencyChanged) && !Mathf.Approximately(nextOffset, previousOffset);
            if (!wantScale && !wantOffset) return;
            foreach (Object target in scale.targets)
            {
                var material = target as Material;
                if (material == null) continue;
                if ((wantScale && Locked(material, scale.name)) || (wantOffset && Locked(material, offset.name))) return;
            }
            bool scaleCallback = wantScale && scale.applyPropertyCallback != null;
            bool offsetCallback = wantOffset && offset.applyPropertyCallback != null;
            // A preview callback can accept the write and then refuse the restore, leaving the new value in the
            // preview after the descriptor has been put back. Do not start a write that cannot be rolled back.
            if (scaleCallback || offsetCallback) return;
            var scaleAssets = CaptureFloats(scale);
            var offsetAssets = CaptureFloats(offset);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                if (wantScale) SetNumber(scale, nextScale);
                if (wantOffset) SetNumber(offset, nextOffset);
            }
            catch (Exception first)
            {
                Exception restore = null;
                try { RestoreAlphaSide(scale, wantScale, previousScale, scaleAssets); }
                catch (Exception ex) { restore = ex; }
                try { RestoreAlphaSide(offset, wantOffset, previousOffset, offsetAssets); }
                catch (Exception ex) { if (restore == null) restore = ex; }
                if (restore != null) throw restore;
                throw first;
            }
            finally { Undo.CollapseUndoOperations(group); }
        }

        private static Dictionary<Material, float> CaptureFloats(MaterialProperty prop)
        {
            var values = new Dictionary<Material, float>();
            if (prop == null || prop.targets == null) return values;
            foreach (Object target in prop.targets)
            {
                var material = target as Material;
                if (material != null && material.HasProperty(prop.name))
                    values[material] = material.GetFloat(prop.name);
            }
            return values;
        }

        private enum OfferResult { NoCallback, Unchanged, Accepted, Refused, Threw }

        private static OfferResult OfferFloat(MaterialProperty prop, float value, out Exception failure)
        {
            failure = null;
            if (prop.applyPropertyCallback == null) return OfferResult.NoCallback;
            // The setter returns before the callback when the value already matches and is not mixed.
            if (!prop.hasMixedValue && Mathf.Approximately(Number(prop), value))
            {
                s_ProbeDepth++;
                try { return prop.applyPropertyCallback(prop, 1, value) ? OfferResult.Accepted : OfferResult.Refused; }
                catch (Exception ex)
                {
                    failure = ex;
                    return OfferResult.Threw;
                }
                finally { s_ProbeDepth--; }
            }
            bool handled = false;
            MaterialProperty.ApplyPropertyCallback original = prop.applyPropertyCallback;
            s_ProbeDepth++;
            prop.applyPropertyCallback = (changed, mask, previous) =>
            {
                handled = original(changed, mask, previous);
                return true;
            };
            try { SetNumber(prop, value); }
            catch (Exception ex)
            {
                failure = ex;
                return OfferResult.Threw;
            }
            finally
            {
                prop.applyPropertyCallback = original;
                s_ProbeDepth--;
            }
            return handled ? OfferResult.Accepted : OfferResult.Refused;
        }

        private static void RestoreAlphaSide(MaterialProperty prop, bool wanted, float value, Dictionary<Material, float> assets)
        {
            if (!wanted || prop == null) return;
            bool restored = false;
            try { restored = RestoreFloat(prop, value); }
            catch (Exception) { restored = false; }
            if (!restored || !Mathf.Approximately(Number(prop), value))
            {
                try { SetNumber(prop, value); }
                catch (Exception) { }
            }
            if (assets == null) return;
            foreach (KeyValuePair<Material, float> pair in assets)
            {
                if (pair.Key == null || !pair.Key.HasProperty(prop.name)) continue;
                if (ExactFloat(pair.Key.GetFloat(prop.name), pair.Value)) continue;
                pair.Key.SetFloat(prop.name, pair.Value);
                if (!ExactFloat(pair.Key.GetFloat(prop.name), pair.Value))
                    throw new InvalidOperationException("Alpha Mask restore did not stick on " + prop.name);
            }
        }

        private static bool ExactFloat(float a, float b)
        {
            return System.BitConverter.SingleToInt32Bits(a) == System.BitConverter.SingleToInt32Bits(b);
        }

        private static bool RestoreFloat(MaterialProperty prop, float value)
        {
            if (prop.applyPropertyCallback == null)
            {
                if (!Mathf.Approximately(Number(prop), value)) SetNumber(prop, value);
                return Mathf.Approximately(Number(prop), value);
            }
            OfferResult result = OfferFloat(prop, value, out Exception failure);
            if (failure != null || result == OfferResult.Refused || result == OfferResult.Threw) return false;
            return true;
        }

        private void ColorAlpha(string name)
        {
            var prop = Prop(name);
            if (prop == null || prop.propertyType != ShaderPropertyType.Color) return;
            if (!RowVisible(name, "sAlpha")) return;
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, prop);
            try
            {
                EditorGUI.showMixedValue = prop.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                string shown = Event.current != null && Event.current.alt ? prop.name + ".a" : FairyToonL10n.Text("sAlpha");
                float alpha = EditorGUI.Slider(row, shown, prop.colorValue.a, 0f, 1f);
                if (EditorGUI.EndChangeCheck()) WriteAlpha(prop, alpha);
            }
            finally
            {
                EditorGUI.showMixedValue = false;
                EndScope();
            }
        }

        private void SubHeader(string title)
        {
            string shown = FairyToonL10n.Text(title);
            if (Searching && !Hit(shown)) return;
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(shown, EditorStyles.label);
        }

        private void Help(string message, MessageType type)
        {
            string shown = FairyToonL10n.Text(message);
            if (Searching && !Hit(shown)) return;
            EditorGUILayout.HelpBox(shown, type);
        }

        private MaterialProperty Prop(string name) => FindProperty(name, m_Props, false);

        private float Float(string name) => Number(Prop(name));

        /// <summary>
        /// ShaderLab <c>Int</c> is the legacy float-backed type (<see cref="ShaderPropertyType.Float"/>).
        /// ShaderLab <c>Integer</c> is <see cref="ShaderPropertyType.Int"/> and must use <c>intValue</c>.
        /// <c>floatValue</c> on a real int returns 0 and does not write.
        /// </summary>
        private static float Number(MaterialProperty prop)
        {
            if (prop == null) return 0f;
            return prop.propertyType == ShaderPropertyType.Int ? prop.intValue : prop.floatValue;
        }

        private static void SetNumber(MaterialProperty prop, float value)
        {
            if (prop == null) return;
            if (prop.propertyType == ShaderPropertyType.Int) prop.intValue = Mathf.RoundToInt(value);
            else prop.floatValue = value;
        }

        // Endfield eye overlay (Face and Eye parts): the painted lid shadow on the eye white and the iris scatter.
        private void EyeOverlay()
        {
            Tex("_EFEyeOverlay", null, "Eye Overlay (R lid shadow, G iris scatter)");
            var overlay = Prop("_EFEyeOverlay");
            if (overlay == null || (!overlay.hasMixedValue && overlay.textureValue == null)) return;
            P("_EFEyeShadowColor");
            P("_EFEyeScatterColor");
        }

        private void ToonIris()
        {
            if (!IrisMatches()) return;
            if (!BeginSection("Iris", "ft.Iris", Prop("_EFIris"), false, PropertyBlock.Iris)) return;
            Help("Head vectors come from FairyToonFaceShadow; without it the object axes are used (gaze then ignores head turns).", MessageType.None);
            // The section header owns the Toon toggle; Endfield keeps its existing inline toggle and placement.
            Iris(false);
            EndSection();
        }


        private bool IrisMatches() => SearchAllows("_EFIris", "_EFIrisDepth", "_EFPupilLayer", "_EFPupilDepth", "_EFIrisRefraction", "_EFIrisScale", "_EFIrisRim", "_EFIrisEdgeFade", "_EFIrisVertical", "_EFIrisMaxShift", "_EFIrisEdge", "_EFIrisEdgeSoftness", "_EFIrisGaze", "_EFCorneaBulge", "_EFCorneaNormalRange", "_EFCorneaIOR", "_EFIrisSparkleTex", "_EFIrisSparkle", "_EFIrisGlowStrength", "_EFIrisSparkleScale", "_EFIrisGlowColor", "_EFIrisFrame", "_EFIrisWorldRadius", "_EFIrisDish", "_EFIrisCenterL", "_EFIrisCenterR", "_EFIrisRadius", "_EFPupil", "_EFPupilCenterL", "_EFPupilCenterR", "_EFPupilRadius", "_EFPupilYRatio", "_EFPupilScale", "_EFPupilOffset", "_EFIris|3D Iris|Sink the iris behind a curved cornea and add its reflection and glow.", "_EFIrisDepth|Depth|How far the iris sinks; more makes it slide further when the head or camera turns.", "_EFPupilLayer|Pupil Layer|On: the pupil mesh moves as its own layer, so pupil shape keys (size, star, heart, off) work. Off: one default pupil is painted into the iris (older fold).", "_EFPupilDepth|Pupil Depth|How deep the pupil sits: 0 stays on the eye surface, 1 moves with the iris pattern. Middle values give a layered look and keep the pupil inside the iris at head turns.", "_EFIrisRefraction|Refraction|How much the cornea bends the view; also magnifies the iris centre face-on.", "_EFIrisScale|Art Scale|Zoom of the iris art inside the eye; lower values enlarge the pupil and show less of the iris rim.", "_EFIrisRim|Fixed Rim|Radius (iris units) from which the painted rim stays on the eye surface; set just inside the dark ring. 1.5 = off (the whole iris sinks).", "_EFIrisEdgeFade|Edge Fade|Fades the parallax to zero toward Fixed Rim (or the iris edge) so the rim never tears and the pattern never runs off the art. The band widens by itself at steep angles so the pattern never folds. 0 = off (hard switch at Fixed Rim).", "_EFIrisVertical|Vertical Strength|Scales up/down parallax; tall anime eyes often look better with less vertical shift.", "_EFIrisMaxShift|Max Shift|Soft limit on how far the pattern can shift, in iris radii, for steep viewing angles. 2 = off.", "_EFIrisEdge|Pattern Edge|Maximum lookup radius inside the painted pattern; keep it inside the painted dark ring and clear of painted highlight dots.", "_EFIrisEdgeSoftness|Edge Softness|How the pattern behaves near Pattern Edge: 0 repeats the edge colour (fibre streaks), higher squeezes the outer pattern toward the edge instead (no streaks); the inner part is untouched below (1 - softness) x Pattern Edge.", "_EFIrisGaze|Gaze Frame|1 uses head forward, centring the iris when the face looks at the camera; 0 uses the mesh surface.", "_EFCorneaBulge|Cornea Bulge|Height of the clear dome over the iris; more curves the view and reflection further.", "_EFCorneaNormalRange|Normal Range|Softens the dome's curve into the flat rim; higher values make a wider, flatter transition.", "_EFCorneaIOR|IOR|How strongly the cornea bends the matcap reflection; higher bends it more.", "_EFIrisSparkleTex|Texture (RGB light, A glow)|Reflection picture over the iris; RGB supplies light and alpha supplies the coloured glow.", "_EFIrisSparkle|Reflection Strength|Brightness of the matcap reflection; 0 removes the window panes without changing glow.", "_EFIrisGlowStrength|Glow Strength|Strength of the coloured glow; 0 removes glow without changing the reflection.", "_EFIrisSparkleScale|Matcap Scale|Size of the reflection pattern; larger values make the window panes smaller.", "_EFIrisGlowColor|Glow Colour|Colour and brightness of the glow screened over the iris.", "_EFIrisFrame|Frame (linear data, baked from UV islands)|Baked eye-island data that keeps iris motion aligned to this avatar's mesh.", "_EFIrisWorldRadius|Iris World Radius|World size of one iris radius in metres, measured from the mesh. Re-measure after rescaling.", "_EFIrisDish|Iris Dish|How much the iris mesh surface rises from centre to rim, in iris radii; keeps the moved pupil on it.", "_EFIrisCenterL|Iris Centre L|Centre of the left iris in the texture; shifts where its art is sampled.", "_EFIrisCenterR|Iris Centre R|Centre of the right iris in the texture; shifts where its art is sampled.", "_EFIrisRadius|Iris Radius|Size of each iris island in the texture; sets the reach of the art lookup.", "_EFPupil|Pupil Fold|How much of the separate pupil is folded into the iris picture.", "_EFPupilCenterL|Pupil Centre L|Centre of the left pupil island in the texture; aligns its folded picture.", "_EFPupilCenterR|Pupil Centre R|Centre of the right pupil island in the texture; aligns its folded picture.", "_EFPupilRadius|Pupil X Radius|Width of the pupil island in the texture; sets how much pupil art is sampled.", "_EFPupilYRatio|Pupil Y / X|Pupil height / width; shapes the fold and reserves vertical extent for the layer travel limit.", "_EFPupilScale|Pupil Scale in Iris Units|Pupil size in iris units; sizes the fold and reserves extent for the layer travel limit.", "_EFPupilOffset|Pupil Offset (left iris units)|Rest pupil centre in left iris units, used by both fold and layer; the right eye mirrors the horizontal offset.");
        private void Iris(bool showToggle = true)
        {

            if (!IrisMatches()) return;

            if (showToggle) P("_EFIris", "3D Iris", "Sink the iris behind a curved cornea and add its reflection and glow.");
            var iris = Prop("_EFIris");
            if (iris == null || (!iris.hasMixedValue && Number(iris) <= 0.5f)) return;
            EditorGUI.indentLevel++;
            // Put the visible look first; measured island data should not get in the way of hand tuning.
            SubHeader("Iris");
            P("_EFIrisDepth", "Depth", "How far the iris sinks; more makes it slide further when the head or camera turns.");
            P("_EFPupilLayer", "Pupil Layer", "On: the pupil mesh moves as its own layer, so pupil shape keys (size, star, heart, off) work. Off: one default pupil is painted into the iris (older fold).");
            P("_EFPupilDepth", "Pupil Depth", "How deep the pupil sits: 0 stays on the eye surface, 1 moves with the iris pattern. Middle values give a layered look and keep the pupil inside the iris at head turns.");
            P("_EFIrisRefraction", "Refraction", "How much the cornea bends the view; also magnifies the iris centre face-on.");
            P("_EFIrisScale", "Art Scale", "Zoom of the iris art inside the eye; lower values enlarge the pupil and show less of the iris rim.");
            P("_EFIrisRim", "Fixed Rim", "Radius (iris units) from which the painted rim stays on the eye surface; set just inside the dark ring. 1.5 = off (the whole iris sinks).");
            P("_EFIrisEdgeFade", "Edge Fade", "Fades the parallax to zero toward Fixed Rim (or the iris edge) so the rim never tears and the pattern never runs off the art. The band widens by itself at steep angles so the pattern never folds. 0 = off (hard switch at Fixed Rim).");
            P("_EFIrisVertical", "Vertical Strength", "Scales up/down parallax; tall anime eyes often look better with less vertical shift.");
            P("_EFIrisMaxShift", "Max Shift", "Soft limit on how far the pattern can shift, in iris radii, for steep viewing angles. 2 = off.");
            P("_EFIrisEdge", "Pattern Edge", "Maximum lookup radius inside the painted pattern; keep it inside the painted dark ring and clear of painted highlight dots.");
            P("_EFIrisEdgeSoftness", "Edge Softness", "How the pattern behaves near Pattern Edge: 0 repeats the edge colour (fibre streaks), higher squeezes the outer pattern toward the edge instead (no streaks); the inner part is untouched below (1 - softness) x Pattern Edge.");
            P("_EFIrisGaze", "Gaze Frame", "1 uses head forward, centring the iris when the face looks at the camera; 0 uses the mesh surface.");
            SubHeader("Cornea");
            P("_EFCorneaBulge", "Cornea Bulge", "Height of the clear dome over the iris; more curves the view and reflection further.");
            P("_EFCorneaNormalRange", "Normal Range", "Softens the dome's curve into the flat rim; higher values make a wider, flatter transition.");
            P("_EFCorneaIOR", "IOR", "How strongly the cornea bends the matcap reflection; higher bends it more.");
            SubHeader("Cornea matcap");
            P("_EFIrisSparkleTex", "Texture (RGB light, A glow)", "Reflection picture over the iris; RGB supplies light and alpha supplies the coloured glow.");
            P("_EFIrisSparkle", "Reflection Strength", "Brightness of the matcap reflection; 0 removes the window panes without changing glow.");
            P("_EFIrisGlowStrength", "Glow Strength", "Strength of the coloured glow; 0 removes glow without changing the reflection.");
            P("_EFIrisSparkleScale", "Matcap Scale", "Size of the reflection pattern; larger values make the window panes smaller.");
            P("_EFIrisGlowColor", "Glow Colour", "Colour and brightness of the glow screened over the iris.");
            if (BeginSection("IrisAvatarData", "ft.IrisAvatar", null, false, PropertyBlock.IrisAvatar))
            {
                P("_EFIrisFrame", "Frame (linear data, baked from UV islands)", "Baked eye-island data that keeps iris motion aligned to this avatar's mesh.");
                P("_EFIrisWorldRadius", "Iris World Radius", "World size of one iris radius in metres, measured from the mesh. Re-measure after rescaling.");
                P("_EFIrisDish", "Iris Dish", "How much the iris mesh surface rises from centre to rim, in iris radii; keeps the moved pupil on it.");
                P("_EFIrisCenterL", "Iris Centre L", "Centre of the left iris in the texture; shifts where its art is sampled.");
                P("_EFIrisCenterR", "Iris Centre R", "Centre of the right iris in the texture; shifts where its art is sampled.");
                P("_EFIrisRadius", "Iris Radius", "Size of each iris island in the texture; sets the reach of the art lookup.");
                SubHeader("Pupil fold");
                Help("Measured per avatar. Fold uses pupil centres, radius, Y ratio, scale, offset and Pupil Fold. Layer uses offset, scale and Y ratio for its travel limit.", MessageType.None);
                P("_EFPupil", "Pupil Fold", "How much of the separate pupil is folded into the iris picture.");
                P("_EFPupilCenterL", "Pupil Centre L", "Centre of the left pupil island in the texture; aligns its folded picture.");
                P("_EFPupilCenterR", "Pupil Centre R", "Centre of the right pupil island in the texture; aligns its folded picture.");
                P("_EFPupilRadius", "Pupil X Radius", "Width of the pupil island in the texture; sets how much pupil art is sampled.");
                P("_EFPupilYRatio", "Pupil Y / X", "Pupil height / width; shapes the fold and reserves vertical extent for the layer travel limit.");
                P("_EFPupilScale", "Pupil Scale in Iris Units", "Pupil size in iris units; sizes the fold and reserves extent for the layer travel limit.");
                P("_EFPupilOffset", "Pupil Offset (left iris units)", "Rest pupil centre in left iris units, used by both fold and layer; the right eye mirrors the horizontal offset.");
                EndSection();
            }
            EditorGUI.indentLevel--;
        }

        private void IntPopup(string name, string label, params string[] options)
        {
            var prop = Prop(name);
            if (prop == null || !RowVisible(name, label)) return;
            var labels = new string[options.Length];
            for (int i = 0; i < options.Length; i++)
                labels[i] = options[i].StartsWith("s", StringComparison.Ordinal) ? FairyToonL10n.Text(options[i]) : options[i];
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, prop);
            try
            {
                EditorGUI.showMixedValue = prop.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                int next = EditorGUI.Popup(row, FairyToonL10n.Text(label), Mathf.RoundToInt(Number(prop)), labels);
                if (EditorGUI.EndChangeCheck()) SetNumber(prop, next);
            }
            finally
            {
                EditorGUI.showMixedValue = false;
                EndScope();
            }
        }

        private int HdrLines(string propertyName)
        {
            string key = string.IsNullOrEmpty(propertyName) ? "?" : propertyName;
            bool layout = Event.current != null && Event.current.type == EventType.Layout;
            if (!layout)
                return m_HdrLines.TryGetValue(key, out int cached) ? cached : 0;
            float indent = EditorGUI.indentLevel * 15f;
            const float boxChrome = 24f;
            float view = EditorGUIUtility.currentViewWidth;
            float room = view - EditorGUIUtility.labelWidth - indent - boxChrome;
            int lines = room < 20f + 4f + 24f ? 2 : room < 50f + 4f + 48f ? 1 : 0;
            m_HdrLines[key] = lines;
            m_HdrWrap[key] = lines > 0;
            return lines;
        }

        private void P(string name, string label = null, string tooltip = null)
        {
            var prop = Prop(name);
            if (prop == null) return;
            string shown = label != null ? FairyToonL10n.Text(label) : FairyToonL10n.Property(name, prop.displayName);
            if (!RowVisible(name, shown, tooltip)) return;
            string tip = string.IsNullOrEmpty(tooltip) ? null : FairyToonL10n.Text(tooltip);
            if (DrawLocalizedEnum(prop, shown, tip, default, false)) return;
            if (prop.propertyType == ShaderPropertyType.Color)
            {
                if (IsGammaHdr(name) || IsBuiltinHdr(name))
                {
                    if (!LayoutChoice("hdr:" + name, true)) return;
                    int lines = HdrLines(name);
                    Rect row = EditorGUILayout.GetControlRect();
                    Rect hexLine = lines >= 1 ? EditorGUILayout.GetControlRect() : default;
                    Rect stackLine = lines >= 2 ? EditorGUILayout.GetControlRect() : default;
                    BeginScope(row, prop);
                    try
                    {
                        EditorGUI.showMixedValue = prop.hasMixedValue;
                        Color color = prop.colorValue;
                        if (FairyToonHDRDrawer.Draw(row, shown, ref color, hexLine, lines >= 1, stackLine)) prop.colorValue = color;
                    }
                    finally
                    {
                        EditorGUI.showMixedValue = false;
                        EndScope();
                    }
                }
                else m_Editor.ColorProperty(prop, shown);
                return;
            }
            m_Editor.ShaderProperty(prop, new GUIContent(shown, tip));
        }

        /// <summary>Enum popups use the catalog for each option, the same way lilToon builds sAlphaMaskModes.</summary>
        private bool DrawLocalizedEnum(MaterialProperty prop, string shown, string tip, Rect rect, bool supplied, GUIStyle style = null)
        {
            if (m_Editor.target is not Material material || material.shader == null) return false;
            int index = material.shader.FindPropertyIndex(prop.name);
            if (index < 0) return false;
            string raw = null;
            foreach (string attribute in material.shader.GetPropertyAttributes(index))
            {
                if (attribute.StartsWith("Enum(", StringComparison.Ordinal) && attribute.EndsWith(")", StringComparison.Ordinal))
                {
                    raw = attribute.Substring(5, attribute.Length - 6);
                    break;
                }
            }
            if (string.IsNullOrEmpty(raw)) return false;
            if (!TryEnumOptions(raw, out string[] names, out int[] values)) return false;
            var labels = new string[names.Length];
            for (int i = 0; i < names.Length; i++) labels[i] = EnumOption(prop.name, names[i]);
            int current = prop.hasMixedValue ? -1 : Mathf.RoundToInt(Number(prop));
            int selected = -1;
            for (int i = 0; i < values.Length; i++) if (values[i] == current) selected = i;
            var content = new GUIContent(shown, tip);
            // A layout rect is a 1px placeholder, so its size must not decide whether another row is reserved.
            if (!supplied) rect = EditorGUILayout.GetControlRect();
            BeginScope(rect, prop);
            try
            {
                EditorGUI.showMixedValue = prop.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                var options = new GUIContent[labels.Length];
                for (int i = 0; i < labels.Length; i++) options[i] = new GUIContent(labels[i]);
                // Popup(Rect, GUIContent, int, string[]) is internal in Unity 6000.6. The GUIContent[] overload is public.
                int next = style == null
                    ? EditorGUI.Popup(rect, content, prop.hasMixedValue ? -1 : selected, options)
                    : EditorGUI.Popup(rect, content, prop.hasMixedValue ? -1 : selected, options, style);
                bool changed = EditorGUI.EndChangeCheck();
                if (changed && next >= 0 && next < values.Length) SetNumber(prop, values[next]);
            }
            finally
            {
                EditorGUI.showMixedValue = false;
                EndScope();
            }
            return true;
        }

        private static string EnumOption(string property, string raw)
        {
            bool blend = property.IndexOf("BlendMode", StringComparison.Ordinal) >= 0;
            if (blend && raw == "Normal") return FairyToonL10n.Text("sBlendModeNormal");
            if (blend && raw == "Add") return FairyToonL10n.Text("sBlendModeAdd");
            if (blend && raw == "Screen") return FairyToonL10n.Text("sBlendModeScreen");
            if (blend && raw == "Multiply") return FairyToonL10n.Text("sBlendModeMul");
            return FairyToonL10n.Text(raw);
        }

        private static bool TryEnumOptions(string raw, out string[] names, out int[] values)
        {
            names = null;
            values = null;
            if (!raw.Contains(","))
            {
                Type type = Type.GetType(raw)
                    ?? Type.GetType(raw + ", UnityEngine.CoreModule")
                    ?? Type.GetType(raw + ", UnityEngine")
                    ?? typeof(CullMode).Assembly.GetType(raw);
                if (type == null || !type.IsEnum) return false;
                Array members = Enum.GetValues(type);
                names = Enum.GetNames(type);
                values = new int[members.Length];
                for (int i = 0; i < members.Length; i++) values[i] = Convert.ToInt32(members.GetValue(i));
                return names.Length > 0;
            }
            string[] parts = raw.Split(',');
            if ((parts.Length % 2) != 0) return false;
            int count = parts.Length / 2;
            names = new string[count];
            values = new int[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = parts[i * 2].Trim();
                if (!int.TryParse(parts[i * 2 + 1].Trim(), out values[i])) return false;
            }
            return count > 0;
        }

        private void ToggleRow(string name, string label)
        {
            var prop = Prop(name);
            if (prop == null) return;
            string shown = FairyToonL10n.Text(label);
            if (!RowVisible(name, shown, null)) return;
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, prop);
            try
            {
                EditorGUI.showMixedValue = prop.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                bool value = EditorGUI.ToggleLeft(row, new GUIContent(shown), Number(prop) > 0.5f);
                if (EditorGUI.EndChangeCheck()) SetNumber(prop, value ? 1f : 0f);
            }
            finally
            {
                EditorGUI.showMixedValue = false;
                EndScope();
            }
        }

        private void TexWideColor(string texture, string colorName, string label)
        {
            var tex = Prop(texture);
            var color = Prop(colorName);
            if (tex == null && color == null) return;
            string shown = FairyToonL10n.Text(label);
            if (Searching && !Hit(shown) && !Hit(label) && (tex == null || !Hit(tex.name)) && (color == null || !Hit(color.name))) return;
            if (tex == null)
            {
                P(colorName, label);
                return;
            }
            // Same split as lilToon's TexturePropertySingleLine: a 32-point thumbnail in the label
            // column, and the colour filling the value column.
            var content = new GUIContent(shown);
            bool widen = EditorGUI.indentLevel > 0 && EditorGUIUtility.currentViewWidth < 400f;
            float savedLabel = EditorGUIUtility.labelWidth;
            if (widen) EditorGUIUtility.labelWidth += 15f;
            try
            {
                if (color == null) m_Editor.TexturePropertySingleLine(content, tex);
                else m_Editor.TexturePropertySingleLine(content, tex, color);
            }
            finally
            {
                if (widen) EditorGUIUtility.labelWidth = savedLabel;
            }
        }

        // Draw after the texture row, with indent restored, so the hit rect and the arrow are the same pixels.
        // The measured arrow was 8 px right of lilToon's at 150%.
        private bool DrawFoldArrow(bool open)
        {
            Rect row = m_TextureRow;
            float x = EditorGUI.IndentedRect(row).x - 8f / Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            // The graphic stays on the 13 pt rect. The hit is 3 pt inside each side, matching lilToon's arrow.
            Rect graphic = new Rect(x, row.y, 13f, row.height);
            Rect hit = new Rect(graphic.x + 3f, graphic.y, graphic.width - 6f, graphic.height);
            bool next = open;
            try
            {
                Event current = Event.current;
                if (current != null && current.type == EventType.MouseDown && current.button == 0 && hit.Contains(current.mousePosition))
                {
                    next = !open;
                    current.Use();
                    GUI.changed = true;
                }
                if (current != null && current.type == EventType.Repaint)
                    EditorStyles.foldout.Draw(graphic, false, false, open, false);
            }
            finally
            {
                EditorGUI.indentLevel = indent;
            }
            return next;
        }

        private bool FoldShown(string prefsKey, bool force, bool openByDefault = false)
        {
            bool stored = EditorPrefs.GetBool(prefsKey, openByDefault);
            return LayoutChoice(prefsKey, stored || force);
        }

        private static void SaveFoldClick(string prefsKey, bool shown, bool next)
        {
            if (next != shown) EditorPrefs.SetBool(prefsKey, next);
        }

        /// <summary>lilToon-style single line: texture slot plus its colour or value.</summary>
        private bool Tex(string texture, string extra, string label, string tooltip = null, bool force = false)
        {
            var tex = Prop(texture);
            if (tex == null) return false;
            string localized = FairyToonL10n.Text(label);
            if (!force && !RowVisible(texture, localized, tooltip) && (extra == null || !RowVisible(extra, localized, tooltip))) return false;
            bool alt = Event.current != null && Event.current.alt;
            string shown = alt ? (string.IsNullOrEmpty(extra) ? texture : texture + ", " + extra) : localized;
            var other = extra != null ? Prop(extra) : null;
            // These lilToon colours are gamma-authored even above 1; HDR editing must not add the native HDR flag.
            // TexturePropertySingleLine ignores custom drawers, so any [FairyToonHDR] colour takes the HDR picker here.
            var content = new GUIContent(shown, string.IsNullOrEmpty(tooltip) ? null : tooltip);
            bool colorExtra = other != null && other.propertyType == ShaderPropertyType.Color;
            bool hdr = colorExtra && (IsGammaHdr(extra) || IsBuiltinHdr(extra));
            int lines = hdr ? HdrLines(extra) : 0;
            bool wrap = lines >= 1;
            // Scalars and ordinary colours are drawn once, by the native row. Only HDR takes a custom rect.
            bool widen = EditorGUI.indentLevel > 0 && EditorGUIUtility.currentViewWidth < 400f;
            float savedLabel = EditorGUIUtility.labelWidth;
            if (widen) EditorGUIUtility.labelWidth += 15f;
            Rect row;
            try
            {
                row = m_Editor.TexturePropertySingleLine(content, tex, hdr ? null : other);
            }
            finally
            {
                if (widen) EditorGUIUtility.labelWidth = savedLabel;
            }
            m_TextureRow = row;
            if (!hdr) return true;
            Rect slot = wrap ? EditorGUILayout.GetControlRect() : row;
            Rect stackLine = lines >= 2 ? EditorGUILayout.GetControlRect() : default;
            if (!wrap)
            {
                float valueX = row.x + EditorGUIUtility.labelWidth;
                slot.xMin = valueX + 50f + 4f + 24f <= row.xMax
                    ? valueX
                    : Mathf.Max(row.x, row.xMax - (50f + 4f + 72f));
            }
            BeginScope(slot, other);
            try
            {
                EditorGUI.showMixedValue = other.hasMixedValue;
                Color color = other.colorValue;
                if (FairyToonHDRDrawer.Draw(wrap ? row : slot, null, ref color, wrap ? slot : default, wrap, stackLine)) other.colorValue = color;
            }
            finally
            {
                EditorGUI.showMixedValue = false;
                EndScope();
            }
            return true;
        }

        private bool IsGammaHdr(string property) => HasAttribute(property, "FairyToonHDR");

        private bool IsBuiltinHdr(string property)
        {
            if (m_Editor.target is not Material material || material.shader == null) return false;
            int index = material.shader.FindPropertyIndex(property);
            if (index < 0) return false;
            if ((material.shader.GetPropertyFlags(index) & ShaderPropertyFlags.HDR) != 0) return true;
            return HasAttribute(property, "HDR");
        }

        private bool HasAttribute(string property, string attribute)
        {
            if (string.IsNullOrEmpty(property) || m_Editor.target is not Material material || material.shader == null) return false;
            int index = material.shader.FindPropertyIndex(property);
            return index >= 0 && System.Array.IndexOf(material.shader.GetPropertyAttributes(index), attribute) >= 0;
        }

        // Vector2Field gives every axis the width of the letter W, a 4-point gap, and no extra indent.
        private readonly struct AxisFieldScope : IDisposable
        {
            private readonly float m_LabelWidth;
            private readonly int m_Indent;
            private readonly Rect m_Values;
            private readonly float m_Width;

            public AxisFieldScope(Rect values, int count)
            {
                m_LabelWidth = EditorGUIUtility.labelWidth;
                m_Indent = EditorGUI.indentLevel;
                EditorGUIUtility.labelWidth = EditorStyles.label.CalcSize(new GUIContent("W")).x;
                EditorGUI.indentLevel = 0;
                m_Values = values;
                const float gap = 4f;
                m_Width = count <= 1 ? values.width : (values.width - gap * (count - 1)) / count;
            }

            public Rect Field(int index)
            {
                const float gap = 4f;
                return new Rect(m_Values.x + index * (m_Width + gap), m_Values.y, m_Width, m_Values.height);
            }

            public void Dispose()
            {
                EditorGUIUtility.labelWidth = m_LabelWidth;
                EditorGUI.indentLevel = m_Indent;
            }
        }

        private void ScaleOffset(string texture, bool force = false)
        {
            var tex = Prop(texture);
            if (tex == null || (tex.propertyFlags & UnityEngine.Rendering.ShaderPropertyFlags.NoScaleOffset) != 0) return;
            bool named = !Searching || HitIgnore(texture) || HitIgnore(tex.displayName) || HitIgnore(FairyToonL10n.Property(texture, tex.displayName));
            bool showTiling = force || named || LabelOnly("ft.Tiling");
            bool showOffset = force || named || LabelOnly("ft.Offset");
            if (!showTiling && !showOffset) return;
            Vector4 st = tex.textureScaleAndOffset;
            if (showTiling) DrawTextureAxes(tex, st, true);
            if (showOffset) DrawTextureAxes(tex, st, false);
        }

        // Native TextureScaleOffsetProperty scopes the row with BeginProperty and BeginAnimatedCheck, and its mixed
        // mask is the scale/offset bits, not the texture reference. Each label gets that scope. Mixed state is read
        // from the materials so a different texture does not mark both rows mixed.
        private void DrawTextureAxes(MaterialProperty tex, Vector4 st, bool scaleRow)
        {
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, tex);
            try
            {
            bool mixed = TextureAxesDiffer(tex, scaleRow);
            float shownX = scaleRow ? st.x : st.z;
            float shownY = scaleRow ? st.y : st.w;
            Rect label = row;
            label.width = EditorGUIUtility.labelWidth;
            EditorGUI.LabelField(label, FairyToonL10n.Text(scaleRow ? "ft.Tiling" : "ft.Offset"));
            var values = new Rect(label.xMax, row.y, row.xMax - label.xMax, row.height);
            EditorGUI.showMixedValue = mixed;
            float editedX;
            float editedY;
            bool changeX;
            bool changeY;
            using (var axes = new AxisFieldScope(values, 2))
            {
                EditorGUI.BeginChangeCheck();
                editedX = EditorGUI.FloatField(axes.Field(0), "X", shownX);
                changeX = EditorGUI.EndChangeCheck();
                EditorGUI.BeginChangeCheck();
                editedY = EditorGUI.FloatField(axes.Field(1), "Y", shownY);
                changeY = EditorGUI.EndChangeCheck();
            }
            if (changeX || changeY)
            {
                Vector4 current = tex.textureScaleAndOffset;
                Vector4 next = current;
                int mask = 0;
                if (scaleRow)
                {
                    if (changeX) { next.x = editedX; mask |= 2; }
                    if (changeY) { next.y = editedY; mask |= 4; }
                }
                else
                {
                    if (changeX) { next.z = editedX; mask |= 8; }
                    if (changeY) { next.w = editedY; mask |= 16; }
                }
                Vector2 edited = new Vector2(editedX, editedY);
                if (mask != 0 && !OfferTextureScale(tex, next, mask) && tex.targets != null)
                {
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                try
                {
                    foreach (Object target in tex.targets)
                    {
                        var material = target as Material;
                        if (material == null || !material.HasProperty(tex.name) || Locked(material, tex.name + "_ST")) continue;
                        Vector4 stored = material.GetVector(tex.name + "_ST");
                        Vector4 merged = stored;
                        if ((mask & 2) != 0) merged.x = edited.x;
                        if ((mask & 4) != 0) merged.y = edited.y;
                        if ((mask & 8) != 0) merged.z = edited.x;
                        if ((mask & 16) != 0) merged.w = edited.y;
                        if (merged == stored) continue;
                        Undo.RecordObject(material, "Modify UV");
                        material.SetVector(tex.name + "_ST", merged);
                    }
                }
                finally { Undo.CollapseUndoOperations(group); }
                }
            }
            EditorGUI.showMixedValue = false;
            }
            finally
            {
                EditorGUI.showMixedValue = false;
                EndScope();
            }
        }

        private static bool TextureAxesDiffer(MaterialProperty tex, bool scale)
        {
            if (tex.targets == null || tex.targets.Length < 2 || !(tex.targets[0] is Material first)) return false;
            Vector2 baseline = scale ? first.GetTextureScale(tex.name) : first.GetTextureOffset(tex.name);
            for (int i = 1; i < tex.targets.Length; i++)
            {
                if (!(tex.targets[i] is Material material)) continue;
                Vector2 value = scale ? material.GetTextureScale(tex.name) : material.GetTextureOffset(tex.name);
                if (value != baseline) return true;
            }
            return false;
        }

        private static void InitStyles()
        {
            if (s_HeaderLabel != null && s_ProSkin == EditorGUIUtility.isProSkin) return;
            s_ProSkin = EditorGUIUtility.isProSkin;
            s_Foldout = new GUIStyle("ShurikenModuleTitle")
            {
                font = EditorStyles.label.font,
                fontSize = EditorStyles.label.fontSize,
                border = new RectOffset(15, 7, 4, 4),
                contentOffset = new Vector2(20f, -2f),
                fixedHeight = 22,
                overflow = new RectOffset(0, 0, 0, 0)
            };
            if (s_Foldout.normal.background == null)
            {
                s_Foldout = new GUIStyle(EditorStyles.toolbarButton)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    fixedHeight = 22
                };
            }
            s_HeaderLabel = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Normal };
            s_BoldFoldout = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            s_HeaderLabel.normal.textColor = s_Foldout.normal.textColor;
            // Section boxes use lilToon's 12px GUI textures. The header gap is the outer top margin.
            s_Line = s_ProSkin ? new Color(0.35f, 0.35f, 0.35f, 1f) : new Color(0.4f, 0.4f, 0.4f, 1f);
            string skin = s_ProSkin ? "dark" : "light";
            s_Outer = BoxStyle(4, 4, 2, GuiBox("gui_box_outer_" + skin));
            // The header gap is ConsumeHeaderGap(), not this margin. A top margin was covered by the header shadow.
            s_Outer.margin = new RectOffset(4, 4, 0, 4);
            // Vertical padding and the inner top margin are the same colour as the title, so they
            // made the bar 39 px. The 22-point title row is the whole bar, 33 px at 150%.
            s_Outer.padding = new RectOffset(2, 2, 0, 0);
            s_OuterBeforeOutline = new GUIStyle(s_Outer);
            s_OuterBeforeOutline.margin = new RectOffset(4, 4, 0, 0);
            s_OutlineBox = new GUIStyle(s_Outer);
            s_OutlineBox.margin = new RectOffset(4, 4, 0, 4);
            s_InnerHalf = BoxStyle(4, 2, 2, GuiBox("gui_box_inner_half_" + skin));
            s_InnerHalf.margin = new RectOffset(2, 2, 0, 2);
            s_InnerFull = BoxStyle(4, 2, 2, GuiBox("gui_box_inner_" + skin));
            s_InnerFull.margin = new RectOffset(2, 2, 0, 2);
            s_Custom = BoxStyle(6, 4, 4, GuiBox("gui_custom_box_" + skin));
            s_Custom.margin = new RectOffset(4, 4, 0, 4);
            s_Inner = s_InnerHalf;
            s_Box = s_Outer;
            s_Gear = new GUIStyle(GUIStyle.none) { alignment = TextAnchor.MiddleCenter };
        }

        private static GUIStyle BoxStyle(int border, int margin, int padding, Texture2D texture)
        {
            var style = new GUIStyle
            {
                border = new RectOffset(border, border, border, border),
                margin = new RectOffset(margin, margin, margin, margin),
                padding = new RectOffset(padding, padding, padding, padding),
                overflow = new RectOffset(0, 0, 0, 0)
            };
            style.normal.background = texture;
            return style;
        }

        private static readonly Dictionary<string, Texture2D> s_FallbackBoxes = new Dictionary<string, Texture2D>();

        private static Texture2D GuiBox(string name)
        {
            const string path = "Packages/com.nw.fairytoon/Editor/Resources/";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path + name + ".png");
            return texture != null ? texture : FallbackBox(name);
        }

        private static Texture2D FallbackBox(string name)
        {
            if (s_FallbackBoxes.TryGetValue(name, out Texture2D cached) && cached != null) return cached;
            bool light = name.EndsWith("light", StringComparison.Ordinal);
            bool custom = name.StartsWith("gui_custom", StringComparison.Ordinal);
            bool outer = name.StartsWith("gui_box_outer", StringComparison.Ordinal);
            Color32 fill = custom
                ? (light ? new Color32(222, 222, 222, 255) : new Color32(64, 64, 64, 255))
                : outer
                    ? (light ? new Color32(255, 255, 255, 255) : new Color32(89, 89, 89, 255))
                    : (light ? new Color32(222, 222, 222, 255) : new Color32(56, 56, 56, 255));
            var texture = new Texture2D(12, 12, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[144];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;
            texture.SetPixels32(pixels);
            texture.Apply();
            s_FallbackBoxes[name] = texture;
            return texture;
        }

    }
}
