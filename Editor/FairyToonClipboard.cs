// Adapted from lilToon 2.4.1 (MIT, Copyright (c) 2020-2024 lilxyzw).
// Clipboard: Assets/lilToon/Editor/lilInspector/lilEditorVariables.cs (copiedProperties, copiedVectorProperties)
// and lilGUIUtility.cs CopyProperties / PasteProperties / ResetProperties
// at commit e698a2f791bd28dd1aabf227c74f14bedeca3587.
// See THIRD_PARTY_NOTICES.md.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NW.FairyToon.Editor
{
    /// <summary>
    /// Accumulating clipboard, same shape as lilToon. <see cref="CopiedProperties"/> is static and keyed by
    /// property name. Copy adds or overwrites that block's names and does not clear the others, so Copy on
    /// MatCap and then Copy on Rim Light leaves both in the dictionary. Paste writes only the destination
    /// block's names that are already in the dictionary, and skips textures unless Paste (with texture).
    /// Texture scale/offset is stored in <see cref="CopiedVectorProperties"/> as <c>name + "_ST"</c> and is
    /// applied only with textures, and only on the first selected material, as lilToon does.
    /// Values are the <see cref="MaterialProperty"/> objects the inspector received in OnGUI, read at paste time.
    /// Copy and paste do not call <c>MaterialEditor.GetMaterialProperties</c>, so a renderer property block or
    /// animation-mode callback on those objects is kept. Texture scale/offset still uses the first target only.
    /// The dictionaries live for the editor session and are empty again after a domain reload, same as lilToon's static fields.
    /// A lilToon inspector does not share this clipboard.
    /// </summary>
    internal static class FairyToonClipboard
    {
        internal static readonly Dictionary<string, MaterialProperty> CopiedProperties = new Dictionary<string, MaterialProperty>();
        internal static readonly Dictionary<string, Vector4> CopiedVectorProperties = new Dictionary<string, Vector4>();

        private static readonly Dictionary<PropertyBlock, string> s_ManualAnchors = new Dictionary<PropertyBlock, string>
        {
            { PropertyBlock.Base, "base/base" },
            { PropertyBlock.Lighting, "base/lighting" },
            { PropertyBlock.MainColor, "color/maincolor" },
            { PropertyBlock.MainColor1st, "color/maincolor" },
            { PropertyBlock.MainColor2nd, "color/maincolor_layer" },
            { PropertyBlock.MainColor3rd, "color/maincolor_layer" },
            { PropertyBlock.AlphaMask, "color/alphamask" },
            { PropertyBlock.Shadow, "color/shadow" },
            { PropertyBlock.RimShade, "color/rimshade" },
            { PropertyBlock.Emission, "color/emission" },
            { PropertyBlock.Emission1st, "color/emission" },
            { PropertyBlock.Emission2nd, "color/emission" },
            { PropertyBlock.NormalMap, "reflections/normal" },
            { PropertyBlock.NormalMap1st, "reflections/normal" },
            { PropertyBlock.NormalMap2nd, "reflections/normal" },
            { PropertyBlock.Anisotropy, "reflections/anisotropy" },
            { PropertyBlock.Backlight, "reflections/backlight" },
            { PropertyBlock.Reflection, "reflections/reflection" },
            { PropertyBlock.MatCaps, "reflections/matcap" },
            { PropertyBlock.MatCap1st, "reflections/matcap" },
            { PropertyBlock.MatCap2nd, "reflections/matcap" },
            { PropertyBlock.RimLight, "reflections/rimlight" },
            { PropertyBlock.Glitter, "reflections/glitter" },
            { PropertyBlock.Gem, "reflections/gem" },
            { PropertyBlock.Outline, "advanced/outline" },
            { PropertyBlock.Dissolve, "advanced/dissolve" },
            { PropertyBlock.Refraction, "advanced/refraction" },
            { PropertyBlock.Fur, "advanced/fur" },
            { PropertyBlock.Stencil, "advanced/stencil" },
            { PropertyBlock.Rendering, "advanced/rendering" }
        };

        internal static void ShowMenu(PropertyBlock block, MaterialEditor editor, System.Func<MaterialProperty[]> currentProperties)
        {
            if (editor == null || currentProperties == null) return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(FairyToonL10n.Text("sCopy")), false, () => CopyProperties(block, editor, currentProperties()));
            menu.AddItem(new GUIContent(FairyToonL10n.Text("sPaste")), false, () => PasteProperties(block, editor, currentProperties(), false));
            menu.AddItem(new GUIContent(FairyToonL10n.Text("sPasteWithTexture")), false, () => PasteProperties(block, editor, currentProperties(), true));
            menu.AddItem(new GUIContent(FairyToonL10n.Text("sReset")), false, () => ResetProperties(block, editor, currentProperties()));
            if (s_ManualAnchors.TryGetValue(block, out string anchor))
            {
                string url = FairyToonL10n.Text("sManualURL") + anchor;
                menu.AddItem(new GUIContent(FairyToonL10n.Text("sOpenManual")), false, () => Application.OpenURL(url));
            }
            menu.ShowAsContext();
        }

        internal static void CopyProperties(PropertyBlock block, MaterialEditor editor, MaterialProperty[] props)
        {
            var material = editor.target as Material;
            if (material == null || props == null) return;
            for (int i = 0; i < FairyToonPropertyBlocks.All.Length; i++)
            {
                FairyToonPropertyEntry entry = FairyToonPropertyBlocks.All[i];
                if (!Contains(entry, block)) continue;
                MaterialProperty prop = Find(props, entry.Name);
                if (prop == null) continue;
                CopiedProperties[entry.Name] = prop;
                if (!entry.IsTexture) continue;
                string st = entry.Name + "_ST";
                if (material.HasProperty(st))
                    CopiedVectorProperties[st] = material.GetVector(st);
            }
        }

        internal static void PasteProperties(PropertyBlock block, MaterialEditor editor, MaterialProperty[] props, bool shouldCopyTex)
        {
            var material = editor.target as Material;
            if (material == null || props == null) return;
            var coverage = SnapshotCoverage(editor);
            if (!PasteWouldChange(block, material, props, shouldCopyTex, out bool callback)) return;
            string undo = shouldCopyTex ? "FairyToon Paste (with textures)" : "FairyToon Paste";
            // One group for every property and every texture scale in this paste, recorded before the writes.
            int group = 0;
            int menuFrame = 0;
            var wrapped = new List<FairyToonGUI.SavedCallback>();
            bool started = false;
            try
            {
                group = BeginUndo(editor, undo);
                started = true;
                menuFrame = FairyToonGUI.BeginMenuOperation(props, wrapped);
                for (int i = 0; i < FairyToonPropertyBlocks.All.Length; i++)
                {
                    FairyToonPropertyEntry entry = FairyToonPropertyBlocks.All[i];
                    if (!Contains(entry, block)) continue;
                    if (!shouldCopyTex && entry.IsTexture) continue;
                    if (!CopiedProperties.TryGetValue(entry.Name, out MaterialProperty source) || source == null) continue;
                    MaterialProperty dest = Find(props, entry.Name);
                    // lilToon enters the texture and _ST branch only when the target descriptor exists.
                    if (dest == null) continue;
                    if (ChangesValue(dest, source)) ApplyValue(dest, source);
                    if (!entry.IsTexture || !shouldCopyTex) continue;
                    string st = entry.Name + "_ST";
                    if (!CopiedVectorProperties.TryGetValue(st, out Vector4 scale) || !material.HasProperty(st) || SameVector(material.GetVector(st), scale))
                        continue;
                    material.SetVector(st, scale);
                    FairyToonGUI.NoteDirectWrite(material);
                }
            }
            finally
            {
                if (started) FinishMenuOperation(editor, coverage, group, wrapped, callback, menuFrame);
            }
        }

        internal static void ResetProperties(PropertyBlock block, MaterialEditor editor, MaterialProperty[] props)
        {
            if (props == null) return;
            var coverage = SnapshotCoverage(editor);
            if (!ResetWouldChange(block, props, out bool callback)) return;
            int group = 0;
            int menuFrame = 0;
            var wrapped = new List<FairyToonGUI.SavedCallback>();
            bool started = false;
            try
            {
                group = BeginUndo(editor, "FairyToon Reset");
                started = true;
                menuFrame = FairyToonGUI.BeginMenuOperation(props, wrapped);
                for (int i = 0; i < props.Length; i++)
                {
                    MaterialProperty prop = props[i];
                    if (!ResetTarget(prop, block, out Shader shader, out int index) || AlreadyDefault(prop, shader, index)) continue;
                    ApplyDefault(prop, shader, index);
                }
            }
            finally
            {
                if (started) FinishMenuOperation(editor, coverage, group, wrapped, callback, menuFrame);
            }
        }

        // Restores callbacks, syncs materials that actually changed, collapses the undo group,
        // and clears the menu scope even when a setter throws.
        private static void FinishMenuOperation(MaterialEditor editor, List<CoverageSnapshot> coverage, int group, List<FairyToonGUI.SavedCallback> wrapped, bool callback, int menuFrame)
        {
            try
            {
                try
                {
                    if (menuFrame != 0)
                    {
                        FairyToonGUI.CollectMenuAssetChanges();
                        if (!(callback && !FairyToonGUI.WroteAsset))
                        {
                            MigrateCoverage(coverage);
                            ApplyMaterials(editor, coverage);
                        }
                    }
                }
                finally
                {
                    for (int i = 0; i < wrapped.Count; i++)
                        if (wrapped[i].Property != null) wrapped[i].Property.applyPropertyCallback = wrapped[i].Original;
                }
            }
            finally
            {
                try { Undo.CollapseUndoOperations(group); }
                finally { FairyToonGUI.EndMenuOperation(null, menuFrame); }
            }
        }

        private static bool PasteWouldChange(PropertyBlock block, Material material, MaterialProperty[] props, bool shouldCopyTex, out bool callback)
        {
            callback = false;
            bool change = false;
            for (int i = 0; i < FairyToonPropertyBlocks.All.Length; i++)
            {
                FairyToonPropertyEntry entry = FairyToonPropertyBlocks.All[i];
                if (!Contains(entry, block) || (!shouldCopyTex && entry.IsTexture)) continue;
                if (!CopiedProperties.TryGetValue(entry.Name, out MaterialProperty source) || source == null) continue;
                MaterialProperty dest = Find(props, entry.Name);
                if (dest == null) continue;
                if (ChangesValue(dest, source))
                {
                    change = true;
                    // An unchanged property never calls its setter, so its callback must not cancel the undo snapshot.
                    if (dest.applyPropertyCallback != null) callback = true;
                }
                if (!shouldCopyTex || !entry.IsTexture) continue;
                string st = entry.Name + "_ST";
                if (CopiedVectorProperties.TryGetValue(st, out Vector4 scale) && material.HasProperty(st) && !SameVector(material.GetVector(st), scale))
                    change = true;
            }
            return change;
        }

        private static bool SameVector(Vector4 a, Vector4 b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

        private static bool ResetWouldChange(PropertyBlock block, MaterialProperty[] props, out bool callback)
        {
            callback = false;
            bool change = false;
            for (int i = 0; i < props.Length; i++)
            {
                MaterialProperty prop = props[i];
                if (!ResetTarget(prop, block, out Shader shader, out int index)) continue;
                if (AlreadyDefault(prop, shader, index)) continue;
                change = true;
                if (prop.applyPropertyCallback != null) callback = true;
            }
            return change;
        }

        private static bool ResetTarget(MaterialProperty prop, PropertyBlock block, out Shader shader, out int index)
        {
            shader = null;
            index = -1;
            if (prop == null || !FairyToonPropertyBlocks.Belongs(block, prop.name)) return false;
            if (prop.targets == null || prop.targets.Length == 0 || !(prop.targets[0] is Material material) || material.shader == null)
                return false;
            shader = material.shader;
            index = shader.FindPropertyIndex(prop.name);
            return index >= 0;
        }

        private static bool ChangesValue(MaterialProperty dest, MaterialProperty source)
        {
            if (dest.hasMixedValue) return true;
            switch (dest.propertyType)
            {
                case ShaderPropertyType.Color: return dest.colorValue != source.colorValue;
                case ShaderPropertyType.Vector: return dest.vectorValue != source.vectorValue;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: return dest.floatValue != source.floatValue;
                case ShaderPropertyType.Int: return dest.intValue != source.intValue;
                case ShaderPropertyType.Texture: return dest.textureValue != source.textureValue;
                default: throw new InvalidOperationException("Unhandled shader property type " + dest.propertyType);
            }
        }

        private static bool AlreadyDefault(MaterialProperty prop, Shader shader, int index)
        {
            if (prop.hasMixedValue) return false;
            switch (prop.propertyType)
            {
                case ShaderPropertyType.Color:
                    Vector4 color = shader.GetPropertyDefaultVectorValue(index);
                    return prop.colorValue == new Color(color.x, color.y, color.z, color.w);
                case ShaderPropertyType.Vector:
                    return prop.vectorValue == shader.GetPropertyDefaultVectorValue(index);
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    return prop.floatValue == shader.GetPropertyDefaultFloatValue(index);
                case ShaderPropertyType.Int:
                    return prop.intValue == shader.GetPropertyDefaultIntValue(index);
                case ShaderPropertyType.Texture:
                    return prop.textureValue == null;
                default:
                    throw new InvalidOperationException("Unhandled shader property type " + prop.propertyType);
            }
        }

        private static void ApplyDefault(MaterialProperty prop, Shader shader, int index)
        {
            FairyToonGUI.NoteExplicitProperty(prop.name);
            switch (prop.propertyType)
            {
                case ShaderPropertyType.Color:
                    Vector4 color = shader.GetPropertyDefaultVectorValue(index);
                    prop.colorValue = new Color(color.x, color.y, color.z, color.w);
                    break;
                case ShaderPropertyType.Vector:
                    prop.vectorValue = shader.GetPropertyDefaultVectorValue(index);
                    break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    prop.floatValue = shader.GetPropertyDefaultFloatValue(index);
                    break;
                case ShaderPropertyType.Int:
                    prop.intValue = shader.GetPropertyDefaultIntValue(index);
                    break;
                case ShaderPropertyType.Texture:
                    prop.textureValue = null;
                    break;
                default:
                    throw new InvalidOperationException("Unhandled shader property type " + prop.propertyType);
            }
        }

        private static void ApplyValue(MaterialProperty dest, MaterialProperty source)
        {
            switch (dest.propertyType)
            {
                case ShaderPropertyType.Color:
                    dest.colorValue = source.colorValue;
                    FairyToonGUI.NoteExplicitProperty(dest.name);
                    break;
                case ShaderPropertyType.Vector:
                    dest.vectorValue = source.vectorValue;
                    FairyToonGUI.NoteExplicitProperty(dest.name);
                    break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    dest.floatValue = source.floatValue;
                    FairyToonGUI.NoteExplicitProperty(dest.name);
                    break;
                case ShaderPropertyType.Int:
                    dest.intValue = source.intValue;
                    FairyToonGUI.NoteExplicitProperty(dest.name);
                    break;
                case ShaderPropertyType.Texture:
                    dest.textureValue = source.textureValue;
                    FairyToonGUI.NoteExplicitProperty(dest.name);
                    break;
                default:
                    throw new InvalidOperationException("Unhandled shader property type " + dest.propertyType);
            }
        }

        private struct CoverageSnapshot
        {
            public Material Material;
            public int Mode;
            public int Transparent;
            public int Queue;
            public int RawQueue;
        }

        private static List<CoverageSnapshot> SnapshotCoverage(MaterialEditor editor)
        {
            var shots = new List<CoverageSnapshot>();
            foreach (UnityEngine.Object target in editor.targets)
            {
                var material = target as Material;
                if (material == null) continue;
                shots.Add(new CoverageSnapshot
                {
                    Material = material,
                    Mode = FairyToonMaterialSetup.GetRenderingMode(material),
                    Transparent = material.HasProperty("_TransparentMode") ? Mathf.RoundToInt(material.GetFloat("_TransparentMode")) : FairyToonMaterialSetup.GetRenderingMode(material),
                    Queue = material.renderQueue,
                    RawQueue = material.rawRenderQueue
                });
            }
            return shots;
        }

        private static void MigrateCoverage(List<CoverageSnapshot> shots)
        {
            for (int i = 0; i < shots.Count; i++)
            {
                CoverageSnapshot shot = shots[i];
                if (!FairyToonGUI.TargetWroteAsset(shot.Material)) continue;
                FairyToonMaterialSetup.MigrateMainCoverageQueue(shot.Material, shot.Mode, shot.Queue, shot.RawQueue);
            }
        }

        private static bool Contains(FairyToonPropertyEntry entry, PropertyBlock block)
        {
            for (int i = 0; i < entry.Blocks.Length; i++)
                if (entry.Blocks[i] == block) return true;
            return false;
        }

        private static MaterialProperty Find(MaterialProperty[] props, string name)
        {
            for (int i = 0; i < props.Length; i++)
                if (props[i] != null && props[i].name == name) return props[i];
            return null;
        }

        private static int BeginUndo(MaterialEditor editor, string undo)
        {
            // A new group, so this paste does not collapse into whatever the inspector already has open.
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undo);
            Undo.RecordObjects(editor.targets, undo);
            return group;
        }

        private static void ApplyMaterials(MaterialEditor editor, List<CoverageSnapshot> coverage)
        {
            foreach (UnityEngine.Object target in editor.targets)
            {
                if (!FairyToonGUI.TargetWroteAsset(target)) continue;
                var material = target as Material;
                int previous = -1;
                if (coverage != null)
                    for (int i = 0; i < coverage.Count; i++)
                        if (coverage[i].Material == material) previous = coverage[i].Transparent;
                FairyToonMaterialSetup.Apply(material, false);
                int current = material != null && material.HasProperty("_TransparentMode")
                    ? Mathf.RoundToInt(material.GetFloat("_TransparentMode")) : previous;
                bool blendsExplicit = FairyToonGUI.ExplicitProperty("_SrcBlend") || FairyToonGUI.ExplicitProperty("_DstBlend")
                    || FairyToonGUI.ExplicitProperty("_SrcBlendAlpha") || FairyToonGUI.ExplicitProperty("_DstBlendAlpha");
                if (material != null && previous >= 0 && current != previous && !blendsExplicit)
                    FairyToonMaterialSetup.SetCoverageBlends(material);
            }
            editor.PropertiesChanged();
        }
    }
}
